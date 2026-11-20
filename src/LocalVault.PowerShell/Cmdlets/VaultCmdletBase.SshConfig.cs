using LocalVault.Core;
using LocalVault.Engine;

namespace LocalVault.PowerShell;

public abstract partial class VaultCmdletBase
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    internal static string SshDirectory => System.IO.Path.Combine(StoredPathRules.HomeDirectory, SshKeyPath.SshFolder);

    internal void WarnOrphanedSshKeys()
    {
        try
        {
            var count = SshKeyCleanup.FindOrphans(SshDirectory).Count;
            if (count > 0)
            {
                WriteWarning($"{count} orphaned SSH keys: lv ssh-key clean");
            }
        }
        catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
        {
            WriteVerbose($"Orphaned SSH keys not checked: {ex.Message}");
        }
    }

    internal void ReportReleasedSshKeys(IReadOnlyList<SshKeyRelease> released)
    {
        ArgumentNullException.ThrowIfNull(released);
        foreach (var key in released)
        {
            switch (key.State)
            {
                case SshKeyReleaseState.Deleted:
                    Say($"Deleted the SSH key {key.Path}");
                    break;
                case SshKeyReleaseState.KeptForOthers:
                    Say($"Kept the SSH key {key.Path}: other clones use it ({string.Join(", ", key.OtherUsers)}).");
                    break;
                default:
                    WriteWarning($"Kept the SSH key {key.Path}: it differs from the key LocalVault wrote there.");
                    break;
            }
        }

        foreach (var folder in released.Where(key => key.State != SshKeyReleaseState.Deleted)
            .Select(key => System.IO.Path.GetDirectoryName(key.Path)!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            RefreshSshConfig(null, folder);
        }
    }

    internal void RefreshSshConfig(VaultLocation? location, string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        try
        {
            var entries = new Dictionary<string, VaultEntry>(StringComparer.OrdinalIgnoreCase);
            if (location is not null)
            {
                AddSshEntries(entries, RepositoryVaults.StoreFor(location).Read().Body, folder);
            }

            List<string> users;
            using (var marker = SshKeyFolder.Open(folder))
            {
                users = [.. marker.Keys.Values.SelectMany(record => record.Users).Distinct(StringComparer.OrdinalIgnoreCase)];
            }

            foreach (var user in users.Where(user => File.Exists(user) && (location is null || !string.Equals(System.IO.Path.GetFullPath(user), System.IO.Path.GetFullPath(location.Path), StringComparison.OrdinalIgnoreCase))))
            {
                var other = VaultLocation.Resolve(user, null, System.IO.Path.GetDirectoryName(user)!);
                AddSshEntries(entries, RepositoryVaults.StoreFor(other).Read().Body, folder);
            }

            SshConfigFile.Write(folder, entries.Values);
        }
        catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
        {
            WriteWarning($"{System.IO.Path.Combine(folder, SshKeyPath.ConfigFileName)} not updated: {ex.Message}");
        }
    }

    internal void RetireReplacedSshKeys(IReadOnlyList<VaultLocation> vaults)
    {
        ArgumentNullException.ThrowIfNull(vaults);
        SshTools? tools = null;
        foreach (var location in vaults)
        {
            var store = RepositoryVaults.StoreFor(location);
            var candidates = store.Read().Body.Entries
                .Where(entry => entry is { IsMissing: false, Ssh: not null, HasPrevious: true } && entry.SshHosts.Count > 0)
                .ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            try
            {
                tools ??= SshTools.Find(new PcSettings(PcSettings.DefaultPath));
            }
            catch (VaultException ex)
            {
                WriteVerbose($"Replaced SSH keys not tested: {ex.Message}");
                return;
            }

            var retired = candidates.Where(entry => AllHostsAccept(tools, entry)).ToList();
            if (retired.Count == 0)
            {
                continue;
            }

            Report(store.Update(body =>
            {
                foreach (var entry in retired)
                {
                    body.RetirePreviousKey(entry.Name);
                }
            }));
            foreach (var entry in retired)
            {
                SshKeyFiles.DeleteOldKeys(SshKeyFiles.TargetOf(entry));
                Say($"{entry.Name}: every host accepts the new key; the old key is retired.");
            }

            foreach (var folder in retired.Select(entry => System.IO.Path.GetDirectoryName(SshKeyFiles.TargetOf(entry))!).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                RefreshSshConfig(location, folder);
            }
        }
    }

    private void OfferSshInclude(bool wroteKey)
    {
        try
        {
            var settings = new PcSettings(PcSettings.DefaultPath);
            var include = settings.SshInclude;
            if (include is null && wroteKey && IsInteractive)
            {
                include = Confirm(
                    $"Add '{SshConfigFile.IncludeLine}' to {SshConfigFile.UserConfigPath}? ssh, scp and other OpenSSH tools then use the repository keys for their hosts without -i.",
                    defaultYes: true);
                settings.SshInclude = include;
            }

            if (include == true && SshConfigFile.AddInclude(SshConfigFile.UserConfigPath))
            {
                Say($"Added '{SshConfigFile.IncludeLine}' to {SshConfigFile.UserConfigPath}.");
            }
        }
        catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
        {
            WriteWarning($"{SshConfigFile.UserConfigPath} not updated: {ex.Message}");
        }
    }

    private bool AllHostsAccept(SshTools tools, VaultEntry entry)
    {
        if (!File.Exists(SshKeyFiles.KeyFileOf(entry)))
        {
            return false;
        }

        foreach (var host in entry.SshHosts)
        {
            var check = SshKeyRetirement.Check(entry, host, arguments => ToolProcess.Run(tools.SshPath, arguments, ProbeTimeout));
            if (check == SshHostCheck.UnknownHostKey)
            {
                WriteWarning($"{entry.Name}: the old key is kept, not tested: host key unknown for {host}; connect once with lv ssh {entry.Name} {host}");
            }

            if (check != SshHostCheck.Accepted)
            {
                return false;
            }
        }

        return true;
    }

    private static void AddSshEntries(Dictionary<string, VaultEntry> entries, VaultBody body, string folder)
    {
        foreach (var entry in body.Entries.Where(e => e is { IsMissing: false, Ssh: not null }))
        {
            var target = SshKeyFiles.TargetOf(entry);
            if (string.Equals(System.IO.Path.GetDirectoryName(target), System.IO.Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase))
            {
                entries.TryAdd(System.IO.Path.GetFileName(target), entry);
            }
        }
    }
}
