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

    protected override void Run()
    {
        var name = EntryName.Parse(Name);
        var existing = ReadVault().Body.Find(name);
        if (existing is { IsMissing: false })
        {
            throw new VaultException(existing.IsSshKey
                ? $"'{name}' already holds a key on this PC."
                : $"'{name}' is a {existing.Kind} entry, not an SSH key.");
        }

        if (existing is { IsMissing: true } && !existing.IsSshKeyShape)
        {
            throw new VaultException($"'{name}' is declared as a {existing.RequiredShape ?? "secret"}, not an SSH key.");
        }

        if (existing is not null && Shared)
        {
            throw new VaultException($"'{name}' is declared as a personal key; -Shared doesn't apply to it.");
        }

        var hosts = (HostName ?? existing?.RequiredHosts ?? []).Select(SshKeyInfo.NormalizeHost).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var repository = RepositoryName;
        var storedPath = existing?.RequiredPath ?? SshKeyPath.For(repository, name);
        var target = StoredPathRules.Resolve(StoredPathRules.HomeDirectory, storedPath);
        var tools = Tools;
        if (!ShouldProcess($"{name} in {Location.Path}", "Create SSH key"))
        {
            return;
        }

        RefuseOccupiedTarget(target, name);
        var comment = Comment ?? $"{Environment.UserName}@{Environment.MachineName} {repository}/{name}";
        VaultEntry stored;
        using (var key = SshKeyGenerator.Generate(tools, System.IO.Path.GetDirectoryName(target)!, Type, comment))
        {
            var personal = !Shared;
            var value = VaultEntry.NewSshKey(name, key.PrivateKey, storedPath, new SshKeyInfo(Type, key.PublicKey, personal ? [] : hosts), [], Now);
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

        WriteKeyFile(stored, Overwrite);
        var info = Describe(stored);
        Say($"Public key of {name} ({info.Fingerprint}):");
        Say(info.PublicKey!);
        CopyToClipboard(info.PublicKey!);
        WriteWarning($"{name} is unusable until someone with access adds this public key to {(hosts.Count > 0 ? string.Join(", ", hosts) : "the hosts it is for")}.");
        if (Shared)
        {
            WriteWarning($"{name} is shared: its private key is readable by everyone with access to {StoreLabel()}.");
        }
        else if (new StoreRegistry(StoreRegistry.DefaultPath).Personal is null)
        {
            WriteWarning($"{name} is only on this PC: no personal store is defined. Name one with lv store set <name> --personal, then run lv save.");
        }

        WriteObject(info);
    }

    private void RefuseOccupiedTarget(string target, EntryName name)
    {
        var directory = System.IO.Path.GetDirectoryName(target)!;
        if (!System.IO.Directory.Exists(directory))
        {
            return;
        }

        using var folder = SshKeyFolder.Open(directory);
        var plan = folder.Plan(System.IO.Path.GetFileName(target), OriginUrl, Location.Path, [], DateTimeOffset.MinValue, Overwrite);
        if (plan.Refused)
        {
            throw new VaultException(plan.State == SshKeyWriteState.NewerElsewhere
                ? $"'{target}' already holds {name}, written by {plan.Record!.WrittenBy}. Run lv load to use it, or -Overwrite (--overwrite) to replace it. Nothing was written."
                : SshKeyFiles.Describe(plan, name) + " Nothing was written.");
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
