using System.Management.Automation;
using LocalVault.Core;

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

    protected string RepositoryName
    {
        get
        {
            var origin = RepositoryOrigin.Of(Location);
            var root = Location.GitRoot ?? Location.Directory;
            return SshKeyPath.RepositoryNameOf(origin?.Url, origin?.Folder ?? System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(root)));
        }
    }

    protected string? OriginUrl => RepositoryOrigin.Of(Location)?.Url;

    protected static SshTools Tools => SshTools.Find(new PcSettings(PcSettings.DefaultPath));

    internal static VaultSshKeyInfo Describe(VaultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var ssh = entry.Ssh;
        var source = entry.IsMissing ? MissingSource : ssh is { Agent: true } ? AgentSource : FileSource;
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

    internal void WriteKeyFile(VaultEntry entry, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var plan = SshKeyFiles.Write(entry, Location.Path, OriginUrl, overwrite, Now);
        if (plan.Refused)
        {
            WriteWarning(SshKeyFiles.Describe(plan, entry.Name));
        }
        else if (plan.State == SshKeyWriteState.Write)
        {
            Say(SshKeyFiles.Describe(plan, entry.Name));
        }
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
    private static readonly TimeSpan ClipboardTime = TimeSpan.FromMinutes(2);

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

        var info = Describe(stored);
        Say($"Public key of {name} ({info.Fingerprint}):");
        Say(info.PublicKey!);
        CopyToClipboard(info.PublicKey!);
        WriteWarning($"{name} is unusable until someone with access adds this public key to {(hosts.Count > 0 ? string.Join(", ", hosts) : "the hosts it is for")}.");
        if (!personal)
        {
            WriteWarning($"{name} is shared: its private key is readable by everyone with access to {StoreLabel()}.");
        }
        else if (new StoreRegistry(StoreRegistry.DefaultPath).Personal is null)
        {
            WriteWarning($"{name} is only on this PC: no personal store is defined. Name one with lv store set <name> --personal, then run lv save.");
        }

        WriteObject(info);
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

    private void CopyToClipboard(string publicKey)
    {
        try
        {
            SecureClipboard.SetText(publicKey, ClipboardTime);
            Say($"The public key is on the clipboard for {ClipboardTime.TotalMinutes} minutes.");
        }
        catch (Exception ex) when (ex is VaultException or System.ComponentModel.Win32Exception)
        {
            WriteVerbose($"Clipboard not available: {ex.Message}");
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
