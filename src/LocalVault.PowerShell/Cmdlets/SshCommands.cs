using System.Management.Automation;
using LocalVault.Core;
using LocalVault.Engine;

namespace LocalVault.PowerShell;

public sealed record VaultSshKeyInfo(
    string Name, string Sharing, string Source, string? Type, string? Fingerprint, string[] Hosts, bool Previous, string Path, string? PublicKey);

public sealed record VaultSshToolsInfo(string Directory, bool IsDefault, string Settings);

public abstract class SshKeyCmdletBase : VaultCmdletBase
{
    public const string PersonalSharing = "Personal";
    public const string SharedSharing = "Shared";
    public const string FileSource = "file";
    public const string AgentSource = "agent";
    public const string MissingSource = "missing";
    public const string ReferenceSource = "reference";

    protected string RepositoryName => RepositoryNameOf(Location);

    protected static SshTools Tools => SshTools.Find(new PcSettings(PcSettings.DefaultPath));

    internal static VaultSshKeyInfo Describe(VaultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var ssh = entry.Ssh;
        var source = entry.IsMissing ? MissingSource
            : ssh is { Agent: true } ? AgentSource
            : entry.Reference is not null ? ReferenceSource
            : FileSource;
        return new VaultSshKeyInfo(
            entry.Name.FullName,
            entry.IsRequired ? PersonalSharing : SharedSharing,
            source,
            ssh?.Type,
            ssh?.Fingerprint,
            [.. entry.SshHosts],
            entry.HasPrevious,
            SshKeyFiles.TargetOf(entry),
            ssh?.PublicKey);
    }

    protected static VaultEntry RequireSshKey(VaultBody body, EntryName name)
    {
        ArgumentNullException.ThrowIfNull(body);
        var entry = body.Find(name) ?? throw new VaultException($"No entry '{name}'.");
        return entry.IsSshKey ? entry : throw new VaultException($"'{name}' is a {entry.Kind} entry, not an SSH key.");
    }
}

[Cmdlet(VerbsCommon.New, "VaultSshKey", SupportsShouldProcess = true)]
[OutputType(typeof(VaultSshKeyInfo))]
public sealed class NewVaultSshKeyCommand : SshKeyCmdletBase
{
    [Parameter(Mandatory = true, Position = 0)]
    public string Name { get; set; } = string.Empty;

    [Parameter]
    public string[]? HostName { get; set; }

    [Parameter]
    public SwitchParameter Shared { get; set; }

    [Parameter]
    [ValidateSet(SshKeyInfo.Ed25519, SshKeyInfo.Rsa, SshKeyInfo.Ecdsa)]
    public string Type { get; set; } = SshKeyInfo.Ed25519;

    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Comment { get; set; }

    [Parameter]
    public SwitchParameter Overwrite { get; set; }

    [Parameter]
    public SwitchParameter Replace { get; set; }

    protected override void Run()
    {
        var name = EntryName.Parse(Name);
        var existing = ReadVault().Body.Find(name);
        if (existing is { IsMissing: false, IsSshKey: false })
        {
            throw new VaultException($"'{name}' is a {existing.Kind} entry, not an SSH key.");
        }

        if (existing is { IsMissing: true } && !existing.IsSshKeyShape)
        {
            throw new VaultException($"'{name}' is declared as a {existing.RequiredShape ?? "secret"}, not an SSH key.");
        }

        if (existing is { IsRequired: true } && Shared)
        {
            throw new VaultException($"'{name}' is declared as a personal key; -Shared doesn't apply to it.");
        }

        var replaced = existing is { IsMissing: false } ? existing : null;
        var personal = existing?.IsRequired ?? !Shared;
        var hosts = (HostName ?? existing?.SshHosts ?? []).Select(SshKeyInfo.NormalizeHost).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var repository = RepositoryName;
        var storedPath = existing?.RequiredPath ?? existing?.StoredPath ?? SshKeyPath.For(repository, name);
        var target = StoredPathRules.Resolve(StoredPathRules.HomeDirectory, storedPath);
        if (replaced is not null)
        {
            RefuseReplacementWithoutStore(name, personal);
        }

        var tools = Tools;
        if (!ShouldProcess($"{name} in {Location.Path}", replaced is null ? "Create SSH key" : "Replace SSH key"))
        {
            return;
        }

        RefuseOccupiedTarget(target, name, replaced);
        if (replaced is not null && !Replace && !ConfirmReplacement(name, target, personal))
        {
            Say($"{name} keeps its key. Nothing was written.");
            return;
        }

        var comment = Comment ?? $"{Environment.UserName}@{Environment.MachineName} {repository}/{name}";
        byte[] previous = replaced?.GetValue() ?? [];
        VaultEntry stored;
        try
        {
            using var key = SshKeyGenerator.Generate(tools, System.IO.Path.GetDirectoryName(target)!, Type, comment);
            var ssh = new SshKeyInfo(Type, key.PublicKey, personal ? [] : hosts, ReplacedAt: replaced is null ? null : Now);
            var value = VaultEntry.NewSshKey(name, key.PrivateKey, storedPath, ssh, previous, Now);
            Report(VaultFile.Update(
                body =>
                {
                    if (personal)
                    {
                        body.Set(VaultEntry.NewRequired(name, existing?.RequiredNote, Now, VaultEntry.SshKeyShape, storedPath, hosts));
                    }

                    body.Set(value);
                },
                WriteOptions()));
            stored = ReadVault().Body.Find(name)!;
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(previous);
        }

        WriteKeyFile(stored, Overwrite);
        if (replaced is not null)
        {
            Say($"{name}: the old key stays as a fallback until every host accepts the new one.");
        }

        AnnouncePublicKey(name, stored.Ssh!, hosts);
        if (!personal)
        {
            WriteWarning($"{name} is shared: its private key is readable by everyone with access to {StoreLabel()}.");
        }
        else if (new StoreRegistry(StoreRegistry.DefaultPath).Personal is null)
        {
            WriteWarning($"{name} is only on this PC: no personal store is defined. Name one with lv store set <name> --personal, then run lv save.");
        }

        WriteObject(Describe(stored));
    }

    private void RefuseReplacementWithoutStore(EntryName name, bool personal)
    {
        var registry = new StoreRegistry(StoreRegistry.DefaultPath);
        var problem = personal
            ? registry.Personal is null ? "no personal store is defined on this PC. Name one with lv store set <name> --personal." : null
            : StoreResolver.For(Location, null, registry).Problem;
        if (problem is not null)
        {
            throw new VaultException($"'{name}' already holds a key, and replacing it needs the store that carries it to the other PCs: {problem} Nothing was written.");
        }
    }

    private bool ConfirmReplacement(EntryName name, string target, bool personal)
    {
        if (!IsInteractive)
        {
            throw new VaultException($"'{name}' already holds a key. Add -Replace (--replace) to replace it. Nothing was written.");
        }

        var question = new List<string> { $"'{name}' already holds a key. Replace it with a new one?" };
        var clones = OtherClonesUsing(target);
        if (clones.Count > 0)
        {
            question.Add($"Other clones using it on this PC: {string.Join(", ", clones)}.");
        }

        if (!personal)
        {
            question.Add("Every teammate gets the new key on the next lv load.");
        }

        question.Add("The old public key stays registered where it was added; the old key stays as a fallback until every host accepts the new one.");
        return Confirm(string.Join(Environment.NewLine, question));
    }

    private List<string> OtherClonesUsing(string target)
    {
        var directory = System.IO.Path.GetDirectoryName(target)!;
        if (!System.IO.Directory.Exists(directory))
        {
            return [];
        }

        using var folder = SshKeyFolder.Open(directory);
        return folder.Keys.TryGetValue(System.IO.Path.GetFileName(target), out var record)
            ? [.. record.Users.Where(user => !string.Equals(user, Location.Path, StringComparison.OrdinalIgnoreCase))]
            : [];
    }

    private void RefuseOccupiedTarget(string target, EntryName name, VaultEntry? replaced)
    {
        var directory = System.IO.Path.GetDirectoryName(target)!;
        if (!System.IO.Directory.Exists(directory))
        {
            return;
        }

        byte[] current = replaced is null ? [] : SshKeyFiles.KeyFileBytesOf(replaced);
        try
        {
            using var folder = SshKeyFolder.Open(directory);
            var created = replaced?.Created ?? DateTimeOffset.MinValue;
            var plan = folder.Plan(System.IO.Path.GetFileName(target), OriginUrl, Location.Path, current, created, Overwrite);
            if (plan.Refused)
            {
                throw new VaultException(plan.State == SshKeyWriteState.NewerElsewhere
                    ? $"'{target}' already holds {name}, written by {plan.Record!.WrittenBy}. Run lv load to use it, or -Overwrite (--overwrite) to replace it. Nothing was written."
                    : SshKeyFiles.Describe(plan, name) + " Nothing was written.");
            }
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(current);
        }
    }

    private string StoreLabel()
    {
        try
        {
            return $"the store '{StoreResolver.For(Location, null, new StoreRegistry(StoreRegistry.DefaultPath)).Label}'";
        }
        catch (VaultException)
        {
            return "the project's store";
        }
    }
}

[Cmdlet(VerbsCommon.Set, "VaultSshKeyHost", SupportsShouldProcess = true)]
public sealed class SetVaultSshKeyHostCommand : SshKeyCmdletBase
{
    [Parameter(Mandatory = true, Position = 0)]
    public string Name { get; set; } = string.Empty;

    [Parameter(Mandatory = true, Position = 1)]
    [AllowEmptyCollection]
    public string[] HostName { get; set; } = [];

    protected override void Run()
    {
        var name = EntryName.Parse(Name);
        _ = RequireSshKey(ReadVault().Body, name);
        var hosts = HostName.Select(SshKeyInfo.NormalizeHost).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!ShouldProcess($"{name} in {Location.Path}", "Set hosts"))
        {
            return;
        }

        Report(VaultFile.Update(body => body.SetSshHosts(name, hosts)));
        Say(hosts.Count == 0 ? $"{name} has no hosts." : $"{name} is for {string.Join(", ", hosts)}.");
        var entry = ReadVault().Body.Find(name)!;
        var folder = System.IO.Path.GetDirectoryName(SshKeyFiles.TargetOf(entry))!;
        if (!entry.IsMissing && System.IO.Directory.Exists(folder))
        {
            RefreshSshConfig(Location, folder);
        }
    }
}

public abstract class SshRunCmdletBase : SshKeyCmdletBase
{
    [Parameter(Mandatory = true, Position = 0)]
    public string Name { get; set; } = string.Empty;

    protected VaultEntry KeyForUse()
    {
        var name = EntryName.Parse(Name);
        AdoptNewerSshKeys([Location]);
        var entry = RequireSshKey(ReadForUse(body => body.Find(name) is { IsMissing: false }, _ => [name]), name);
        RequireValue(entry);
        if (!File.Exists(SshKeyFiles.KeyFileOf(entry)))
        {
            WriteKeyFile(entry, overwrite: false);
        }

        return File.Exists(SshKeyFiles.KeyFileOf(entry))
            ? entry
            : throw new VaultException($"The key file of {name} isn't on this PC and couldn't be written.");
    }

    protected void RunAttached(string executable, IReadOnlyList<string> arguments) =>
        SessionState.PSVariable.Set("global:LASTEXITCODE", ToolProcess.RunAttached(executable, arguments, SessionState.Path.CurrentFileSystemLocation.ProviderPath));
}

[Cmdlet(VerbsLifecycle.Invoke, "VaultSsh")]
public sealed class InvokeVaultSshCommand : SshRunCmdletBase
{
    [Parameter(Position = 1)]
    [ValidateNotNullOrEmpty]
    public string? Target { get; set; }

    [Parameter]
    public string[] ArgumentList { get; set; } = [];

    protected override void Run()
    {
        var entry = KeyForUse();
        RunAttached(Tools.SshPath, SshCommandLine.Ssh(entry, Target ?? SoleHost(entry), ArgumentList));
    }

    private static string SoleHost(VaultEntry entry) => entry.SshHosts.Count switch
    {
        1 => entry.SshHosts[0],
        0 => throw new VaultException($"{entry.Name} has no hosts: give one (lv ssh {entry.Name} <user@host>), or add it with lv ssh-key hosts {entry.Name} <user@host>."),
        _ => throw new VaultException($"{entry.Name} is for several hosts: give one of {string.Join(", ", entry.SshHosts)}."),
    };
}

[Cmdlet(VerbsLifecycle.Invoke, "VaultScp")]
public sealed class InvokeVaultScpCommand : SshRunCmdletBase
{
    [Parameter(Mandatory = true)]
    public string[] ArgumentList { get; set; } = [];

    protected override void Run()
    {
        var tools = Tools;
        if (!File.Exists(tools.ScpPath))
        {
            throw new VaultException($"{tools.Directory} has no {SshTools.Scp}.");
        }

        RunAttached(tools.ScpPath, SshCommandLine.Scp(KeyForUse(), ArgumentList));
    }
}

public sealed record VaultSshOrphanInfo(string Repository, string Key, DateTimeOffset LastWrite, string Action);

[Cmdlet(VerbsCommon.Remove, "VaultSshOrphan", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
[OutputType(typeof(VaultSshOrphanInfo))]
public sealed class RemoveVaultSshOrphanCommand : PSCmdlet
{
    protected override void ProcessRecord()
    {
        try
        {
            var orphans = SshKeyCleanup.FindOrphans(VaultCmdletBase.SshDirectory);
            if (orphans.Count == 0)
            {
                return;
            }

            var list = string.Join(Environment.NewLine, orphans.Select(o => $"  {o.Repository}/{o.File}, last written {o.WrittenAt.ToLocalTime():yyyy-MM-dd HH:mm}"));
            if (!ShouldProcess(
                $"{orphans.Count} orphaned SSH keys:{Environment.NewLine}{list}",
                $"Delete {orphans.Count} SSH keys whose clones no longer exist:{Environment.NewLine}{list}{Environment.NewLine}",
                "Delete orphaned SSH keys"))
            {
                return;
            }

            var removed = SshKeyCleanup.RemoveOrphans(orphans).ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);
            foreach (var orphan in orphans)
            {
                var path = System.IO.Path.Combine(orphan.Folder, orphan.File);
                var action = removed.TryGetValue(path, out var result) && result.State == SshKeyReleaseState.Deleted ? "Deleted" : "Kept";
                if (action == "Kept")
                {
                    WriteWarning($"Kept the SSH key {path}: it differs from the key LocalVault wrote there, or a clone uses it again.");
                }

                WriteObject(new VaultSshOrphanInfo(orphan.Repository, orphan.File, orphan.WrittenAt, action));
            }
        }
        catch (VaultException ex)
        {
            ThrowTerminatingError(new ErrorRecord(ex, "LocalVault", ErrorCategory.InvalidOperation, null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ThrowTerminatingError(new ErrorRecord(ex, "LocalVault.File", ErrorCategory.WriteError, null));
        }
    }
}

public sealed record VaultSshIncludeInfo(bool Enabled, string ConfigPath);

[Cmdlet(VerbsCommon.Set, "VaultSshInclude", SupportsShouldProcess = true)]
[OutputType(typeof(VaultSshIncludeInfo))]
public sealed class SetVaultSshIncludeCommand : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0)]
    public bool Enabled { get; set; }

    protected override void ProcessRecord()
    {
        var config = SshConfigFile.UserConfigPath;
        try
        {
            if (!ShouldProcess(config, Enabled ? $"Add '{SshConfigFile.IncludeLine}'" : $"Remove '{SshConfigFile.IncludeLine}'"))
            {
                return;
            }

            new PcSettings(PcSettings.DefaultPath).SshInclude = Enabled;
            _ = Enabled ? SshConfigFile.AddInclude(config) : SshConfigFile.RemoveInclude(config);
            WriteObject(new VaultSshIncludeInfo(Enabled, config));
        }
        catch (VaultException ex)
        {
            ThrowTerminatingError(new ErrorRecord(ex, "LocalVault", ErrorCategory.InvalidOperation, null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ThrowTerminatingError(new ErrorRecord(ex, "LocalVault.File", ErrorCategory.WriteError, null));
        }
    }
}

[Cmdlet(VerbsCommon.Get, "VaultSshKey")]
[OutputType(typeof(VaultSshKeyInfo))]
public sealed class GetVaultSshKeyCommand : SshKeyCmdletBase
{
    [Parameter(Position = 0)]
    [ValidateNotNullOrEmpty]
    public string? Name { get; set; }

    protected override void Run()
    {
        AdoptNewerSshKeys([Location]);
        var body = ReadVault().Body;
        if (Name is not null)
        {
            WriteObject(Describe(RequireSshKey(body, EntryName.Parse(Name))));
            return;
        }

        foreach (var entry in body.Entries.Where(e => e.IsSshKey).OrderBy(e => e.Name.FullName, StringComparer.OrdinalIgnoreCase))
        {
            WriteObject(Describe(entry));
        }
    }
}

[Cmdlet(VerbsCommon.Get, "VaultSshKeyPath")]
[OutputType(typeof(string))]
public sealed class GetVaultSshKeyPathCommand : SshKeyCmdletBase
{
    [Parameter(Mandatory = true, Position = 0)]
    public string Name { get; set; } = string.Empty;

    protected override void Run()
    {
        var name = EntryName.Parse(Name);
        AdoptNewerSshKeys([Location]);
        var entry = RequireSshKey(ReadForUse(body => body.Find(name) is { IsMissing: false }, _ => [name]), name);
        RequireValue(entry);
        var path = SshKeyFiles.KeyFileOf(entry);
        if (!File.Exists(path))
        {
            WriteKeyFile(entry, overwrite: false);
        }

        WriteObject(path);
    }
}

[Cmdlet(VerbsCommon.Set, "VaultSshTools", DefaultParameterSetName = ShowSet, SupportsShouldProcess = true)]
[OutputType(typeof(VaultSshToolsInfo))]
public sealed class SetVaultSshToolsCommand : PSCmdlet
{
    private const string ShowSet = "Show";
    private const string DirectorySet = "Directory";
    private const string DefaultSet = "Default";

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = DirectorySet)]
    [ValidateNotNullOrEmpty]
    public string Directory { get; set; } = string.Empty;

    [Parameter(Mandatory = true, ParameterSetName = DefaultSet)]
    public SwitchParameter Default { get; set; }

    protected override void ProcessRecord()
    {
        try
        {
            Run();
        }
        catch (VaultException ex)
        {
            ThrowTerminatingError(new ErrorRecord(ex, "LocalVault", ErrorCategory.InvalidOperation, null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ThrowTerminatingError(new ErrorRecord(ex, "LocalVault.File", ErrorCategory.WriteError, null));
        }
    }

    private void Run()
    {
        var settings = new PcSettings(PcSettings.DefaultPath);
        if (ParameterSetName == DirectorySet && ShouldProcess(settings.SettingsPath, $"Use the OpenSSH tools in {Directory}"))
        {
            var full = GetUnresolvedProviderPathFromPSPath(Directory);
            var previous = settings.SshDir;
            settings.SshDir = full;
            try
            {
                _ = SshTools.Find(settings);
            }
            catch (VaultException)
            {
                settings.SshDir = previous;
                throw;
            }
        }
        else if (ParameterSetName == DefaultSet && ShouldProcess(settings.SettingsPath, "Use Windows OpenSSH"))
        {
            settings.SshDir = null;
        }

        var tools = SshTools.Find(settings);
        WriteObject(new VaultSshToolsInfo(tools.Directory, tools.IsDefault, settings.SettingsPath));
    }
}
