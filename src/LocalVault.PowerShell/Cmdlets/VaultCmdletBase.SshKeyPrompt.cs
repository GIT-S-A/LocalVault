using System.Globalization;
using System.Text;
using LocalVault.Core;
using LocalVault.Engine;

namespace LocalVault.PowerShell;

public abstract partial class VaultCmdletBase
{
    private static readonly TimeSpan PublicKeyClipboardTime = TimeSpan.FromMinutes(2);
    private const string SpinnerFrames = @"|/-\";

    private IReadOnlyList<SshAgentKey>? agentKeys;
    private int spinnerFrame;

    internal static string RepositoryNameOf(VaultLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        var origin = location.ReadOrigin();
        var root = location.GitRoot ?? location.Directory;
        return SshKeyPath.RepositoryNameOf(origin?.Url, origin?.Folder ?? System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(root)));
    }

    internal void AnnouncePublicKey(EntryName name, SshKeyInfo ssh, IReadOnlyList<string> hosts)
    {
        ArgumentNullException.ThrowIfNull(ssh);
        ArgumentNullException.ThrowIfNull(hosts);
        Say($"Public key of {name} ({ssh.Fingerprint}):");
        Say(ssh.PublicKey);
        try
        {
            SecureClipboard.SetText(ssh.PublicKey, PublicKeyClipboardTime);
            Say($"The public key is on the clipboard for {PublicKeyClipboardTime.TotalMinutes} minutes.");
        }
        catch (Exception ex) when (ex is VaultException or System.ComponentModel.Win32Exception)
        {
            WriteVerbose($"Clipboard not available: {ex.Message}");
        }

        WriteWarning($"{name} is unusable until someone with access adds this public key to {(hosts.Count > 0 ? string.Join(", ", hosts) : "the hosts it is for")}.");
    }

    internal void WriteKeyFile(VaultLocation location, VaultEntry entry, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(entry);
        var plan = SshKeyFiles.Write(entry, location.Path, location.ReadOrigin()?.Url, overwrite, Now);
        if (plan.Refused)
        {
            WriteWarning(SshKeyFiles.Describe(plan, entry.Name));
        }
        else if (plan.State == SshKeyWriteState.Write)
        {
            Say(SshKeyFiles.Describe(plan, entry.Name));
        }

        if (!plan.Refused)
        {
            RefreshSshConfig(location, System.IO.Path.GetDirectoryName(plan.Target)!);
            OfferSshInclude(plan.State == SshKeyWriteState.Write);
        }
    }

    private void AdoptMissingSshKeys(IReadOnlyList<VaultLocation> vaults, IReadOnlySet<EntryName>? only)
    {
        foreach (var location in vaults)
        {
            try
            {
                var store = RepositoryVaults.StoreFor(location);
                var origin = location.ReadOrigin()?.Url;
                var found = store.Read().Body.Entries
                    .Where(entry => entry.IsMissing && entry.IsSshKeyShape && (only is null || only.Contains(entry.Name)))
                    .Select(entry => SshKeyAdoption.FindForMissing(entry, origin, Now))
                    .OfType<AdoptedSshKey>()
                    .ToList();
                StoreAdoptedKeys(location, store, origin, found);
            }
            catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
            {
                WriteWarning($"SSH keys of {location.Path} not checked against the other clones: {ex.Message}");
            }
        }
    }

    private bool AdoptMissingSshKey(VaultLocation location, VaultEntry declaration)
    {
        try
        {
            var origin = location.ReadOrigin()?.Url;
            if (SshKeyAdoption.FindForMissing(declaration, origin, Now) is not { } key)
            {
                return false;
            }

            StoreAdoptedKeys(location, RepositoryVaults.StoreFor(location), origin, [key]);
            return true;
        }
        catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
        {
            WriteWarning($"SSH keys of {location.Path} not checked against the other clones: {ex.Message}");
            return false;
        }
    }

    private void StoreAdoptedKeys(VaultLocation location, VaultFile store, string? origin, List<AdoptedSshKey> found)
    {
        if (found.Count == 0)
        {
            return;
        }

        Report(store.Update(body =>
        {
            foreach (var key in found)
            {
                body.SetPersonalValue(key.Entry.Name, key.Entry);
            }
        }));
        foreach (var key in found)
        {
            SshKeyAdoption.AddUser(key.Entry, origin, location.Path);
            Say($"{key.Entry.Name}: using the key in {SshKeyFiles.TargetOf(key.Entry)}, written there by {key.WrittenBy}");
        }
    }

    private bool AskSshKey(StoreAccess access, VaultLocation location, VaultEntry declaration)
    {
        var hosts = declaration.SshHosts;
        Say($"{RepositoryVaults.Label(location)}: {declaration.Name} is your SSH key for {(hosts.Count > 0 ? string.Join(", ", hosts) : "this project")}. It isn't on this PC yet.");
        var agent = AgentKeys();
        var choices = new List<(string Text, Func<bool> Choose)>();
        if (agent.Count > 0)
        {
            choices.Add(("Use a key from your SSH agent (KeeAgent or Windows ssh-agent)", () => UseAgentKey(location, declaration, agent)));
        }

        choices.Add(("Use a private key stored in a KeePass entry", () => UseKeePassKey(access, location, declaration)));
        choices.Add(("Create a new key pair", () => CreateKeyPair(location, declaration)));
        choices.Add(("Skip (asked again at the next lv load)", () => false));
        for (var i = 0; i < choices.Count; i++)
        {
            Say($"{i + 1}. {choices[i].Text}");
        }

        if (Pick(ReadText("Choice [skip]"), choices.Count) is not { } chosen)
        {
            return false;
        }

        try
        {
            return choices[chosen].Choose();
        }
        catch (VaultException ex)
        {
            WriteWarning($"{declaration.Name} not set: {ex.Message}");
            return false;
        }
    }

    private static int? Pick(string answer, int count) =>
        int.TryParse(answer.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1 && number <= count ? number - 1 : null;

    private IReadOnlyList<SshAgentKey> AgentKeys()
    {
        if (agentKeys is not null)
        {
            return agentKeys;
        }

        Say("Asking your SSH agent for its keys. If KeePass is locked, unlock it.");
        try
        {
            agentKeys = SshAgent.ListKeys(SshTools.Find(new PcSettings(PcSettings.DefaultPath)), Spin, () => Stopping);
        }
        catch (VaultException ex)
        {
            WriteVerbose($"SSH agent not asked: {ex.Message}");
            agentKeys = [];
        }
        finally
        {
            Host.UI.Write("\r  \r");
        }

        return agentKeys;
    }

    private void Spin() => Host.UI.Write($"\r{SpinnerFrames[spinnerFrame++ % SpinnerFrames.Length]} ");

    private bool UseAgentKey(VaultLocation location, VaultEntry declaration, IReadOnlyList<SshAgentKey> agent)
    {
        for (var i = 0; i < agent.Count; i++)
        {
            var key = agent[i];
            Say($"{i + 1}. {(key.Comment.Length > 0 ? key.Comment : "(no comment)")} ({key.Type}, {key.Fingerprint})");
        }

        if (Pick(ReadText("Key number [none]"), agent.Count) is not { } chosen)
        {
            return false;
        }

        var publicKey = agent[chosen].PublicKey;
        var ssh = new SshKeyInfo(agent[chosen].Type, publicKey, [], Agent: true);
        StorePersonalSshKey(location, VaultEntry.NewSshKey(declaration.Name, Encoding.UTF8.GetBytes(publicKey + "\n"), declaration.RequiredPath!, ssh, [], Now));
        return true;
    }

    private bool UseKeePassKey(StoreAccess access, VaultLocation location, VaultEntry declaration)
    {
        var stores = access.Registry.Load();
        if (stores.Count == 0)
        {
            throw new VaultException("no store is defined on this PC. Add the KeePass database that holds the key with lv store add <path to .kdbx> -Name <name>.");
        }

        var definition = stores.Count == 1 ? stores[0] : ChooseStore(stores, access.Registry.Personal);
        if (definition is null)
        {
            return false;
        }

        var search = ReadText("KeePass entry holding the key (group/title or title)").Trim();
        if (search.Length == 0)
        {
            return false;
        }

        var (keePass, store) = access.For(new StoreResolution(location.Path, new StoreReference(definition.Name, definition.Id), definition, null));
        using var item = keePass.ReadReferenceTarget(search, declaration.Name, VaultEntry.SshKeyShape, declaration.RequiredPath, [], personal: true, store.Name);
        StorePersonalSshKey(location, item.ToVaultEntry(Now));
        Say($"{declaration.Name} comes from KeePass entry '{item.Reference!.Title}' in store '{store.Name}'. lv save writes the reference to your personal store, never the value.");
        return true;
    }

    private StoreDefinition? ChooseStore(IReadOnlyList<StoreDefinition> stores, StoreDefinition? personal)
    {
        Say("Store that holds the entry:");
        for (var i = 0; i < stores.Count; i++)
        {
            Say($"{i + 1}. {stores[i].Name} ({stores[i].Target})");
        }

        var answer = ReadText($"Store number [{personal?.Name ?? "none"}]");
        return answer.Trim().Length == 0 ? personal : Pick(answer, stores.Count) is { } chosen ? stores[chosen] : null;
    }

    private bool CreateKeyPair(VaultLocation location, VaultEntry declaration)
    {
        var tools = SshTools.Find(new PcSettings(PcSettings.DefaultPath));
        var target = SshKeyFiles.TargetOf(declaration);
        RefuseOccupiedKeyFile(location, declaration, target);
        var comment = $"{Environment.UserName}@{Environment.MachineName} {RepositoryNameOf(location)}/{declaration.Name}";
        using var key = SshKeyGenerator.Generate(tools, System.IO.Path.GetDirectoryName(target)!, SshKeyInfo.Ed25519, comment);
        var ssh = new SshKeyInfo(SshKeyInfo.Ed25519, key.PublicKey, []);
        StorePersonalSshKey(location, VaultEntry.NewSshKey(declaration.Name, key.PrivateKey, declaration.RequiredPath!, ssh, [], Now));
        AnnouncePublicKey(declaration.Name, ssh, declaration.SshHosts);
        return true;
    }

    private static void RefuseOccupiedKeyFile(VaultLocation location, VaultEntry declaration, string target)
    {
        if (!File.Exists(target))
        {
            return;
        }

        var current = SshKeyReset.PreviousOf(declaration) ?? [];
        try
        {
            using var folder = SshKeyFolder.Open(System.IO.Path.GetDirectoryName(target)!);
            var plan = folder.Plan(System.IO.Path.GetFileName(target), location.ReadOrigin()?.Url, location.Path, current, DateTimeOffset.MinValue, overwrite: false);
            if (plan.Refused)
            {
                throw new VaultException(SshKeyFiles.Describe(plan, declaration.Name) + " Nothing was written.");
            }
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(current);
        }
    }

    private void StorePersonalSshKey(VaultLocation location, VaultEntry value)
    {
        var store = RepositoryVaults.StoreFor(location);
        var declaration = store.Read().Body.Find(value.Name);
        if (declaration is { IsMissing: true })
        {
            value = SshKeyReset.AsReplacement(declaration, value, Now);
        }

        Report(store.Update(body => body.SetPersonalValue(value.Name, value)));
        WriteKeyFile(location, store.Read().Body.Find(value.Name)!, overwrite: false);
        if (value.HasPrevious)
        {
            Say($"{value.Name}: the old key stays as a fallback until every host accepts the new one.");
        }
    }

    private StoreDefinition? OfferPersonalStore(StoreAccess access)
    {
        var stores = access.Registry.Load();
        StoreDefinition? chosen = null;
        if (stores.Count > 0)
        {
            Say("These values are only on this PC. A personal store keeps them for your other PCs: a KeePass database that only you can open.");
            if (stores.Count == 1)
            {
                chosen = Confirm($"Use '{stores[0].Name}' ({stores[0].Target}) as your personal store?") ? stores[0] : null;
            }
            else
            {
                for (var i = 0; i < stores.Count; i++)
                {
                    Say($"{i + 1}. {stores[i].Name} ({stores[i].Target})");
                }

                chosen = Pick(ReadText("Number of the store to use [none]"), stores.Count) is { } number ? stores[number] : null;
            }
        }

        if (chosen is null)
        {
            WriteWarning("These values are only on this PC: no personal store is defined. Name one with lv store set <name> -Personal, then run lv save.");
            return null;
        }

        var personal = access.Registry.Change(chosen.Name, mirror: null, unlockWith: null, clearUnlock: false, makeDefault: false, personal: true);
        Say($"'{personal.Name}' is this PC's personal store: lv save keeps your own values of required entries there, lv load reads them back.");
        return personal;
    }
}
