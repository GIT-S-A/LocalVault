using System.Collections;
using System.Management.Automation;
using LocalVault.Core;

namespace LocalVault.PowerShell;

[Cmdlet(VerbsOther.Use, "VaultEnv")]
public sealed class UseVaultEnvCommand : VaultCmdletBase
{
    [Parameter(Mandatory = true, Position = 0)]
    public Hashtable Map { get; set; } = [];

    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Scope { get; set; }

    [Parameter]
    public string Fallback { get; set; } = ResolveVaultEntryCommand.DefaultFallback;

    protected override void Run()
    {
        var body = ReadForUse(
            current => Map.Values.Cast<object?>().All(source => Find(current, source?.ToString() ?? string.Empty) is not null),
            current => Map.Values.Cast<object?>().Select(source => Find(current, source?.ToString() ?? string.Empty)?.Name).OfType<EntryName>());
        foreach (var entry in Map.Values.Cast<object?>().Select(source => Find(body, source?.ToString() ?? string.Empty)).OfType<VaultEntry>())
        {
            RequireValue(entry);
        }

        body = Unseal(body, Map.Values.Cast<object?>().Select(source => Find(body, source?.ToString() ?? string.Empty)?.Name).OfType<EntryName>(), "put into the environment");
        var missing = 0;
        foreach (DictionaryEntry pair in Map)
        {
            var variable = pair.Key.ToString() ?? string.Empty;
            var source = pair.Value?.ToString() ?? string.Empty;
            var entry = Find(body, source);
            if (entry is null)
            {
                WriteWarning($"{variable}: '{source}' is not in the vault");
                missing++;
                continue;
            }

            RequireValue(entry);
            if (!entry.IsSecret && !entry.IsCredential)
            {
                throw new VaultException($"'{entry.Name}' is a {entry.Kind} entry, not a secret.");
            }

            var reference = EntryReference.Parse(source);
            var part = new EntryReference(entry.Name, reference.Part, reference.UrlNumber);
            var bytes = entry.ReadPart(part, Now);
            try
            {
                Environment.SetEnvironmentVariable(variable, System.Text.Encoding.UTF8.GetString(bytes), EnvironmentVariableTarget.Process);
            }
            finally
            {
                Array.Clear(bytes);
            }

            Say($"{variable}: set from {part}");
        }

        Say(missing == 0 ? "Set for this window only." : $"Set for this window only; {missing} missing.");
    }

    private VaultEntry? Find(VaultBody body, string source)
    {
        var name = EntryReference.Parse(source).Name;
        if (Scope is null || name.IsScoped)
        {
            return body.Find(name);
        }

        return body.Find(EntryName.Combine(Scope, name.Key)) ?? body.Find(EntryName.Combine(Fallback, name.Key));
    }
}

public sealed record RestoredFile(string Name, string Path, string Action);

[Cmdlet(VerbsData.Restore, "VaultFile", DefaultParameterSetName = NameSet)]
[OutputType(typeof(RestoredFile))]
public sealed class RestoreVaultFileCommand : VaultCmdletBase
{
    private const string NameSet = "Name";
    private const string AllSet = "All";

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = NameSet)]
    public string[] Name { get; set; } = [];

    [Parameter(Mandatory = true, ParameterSetName = AllSet)]
    public SwitchParameter All { get; set; }

    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Destination { get; set; }

    [Parameter]
    public SwitchParameter Overwrite { get; set; }

    [Parameter]
    public SwitchParameter SkipGitIgnoreCheck { get; set; }

    protected override void Run()
    {
        var body = ReadForUse(current => All ? current.Entries.Any(e => e.IsFile) : Name.All(n => current.Find(EntryName.Parse(n)) is not null));
        var entries = All
            ? body.Entries.Where(e => e.IsFile).ToList()
            : Name.Select(n => FindEntry(body, EntryName.Parse(n))).ToList();
        var root = Destination is null ? Location.Directory : GetUnresolvedProviderPathFromPSPath(Destination);
        var plain = Unseal(body, entries.Where(e => e.IsFile).Select(e => e.Name), "restore");
        entries = entries.Select(e => plain.Find(e.Name) ?? e).ToList();

        var targets = entries.Select(entry => entry.IsFile
                ? (Entry: entry, Path: entry.Ssh is not null ? SshKeyFiles.KeyFileOf(entry) : StoredPathRules.Resolve(root, entry.StoredPath ?? entry.Name.Key))
                : throw new VaultException($"'{entry.Name}' is a {entry.Kind} entry, not a file."))
            .ToList();
        if (!SkipGitIgnoreCheck)
        {
            foreach (var (_, target) in targets.Where(item => item.Entry.Ssh is null))
            {
                RequireIgnoredByGit(target);
            }
        }

        foreach (var (entry, target) in targets)
        {
            WriteObject(new RestoredFile(entry.Name.FullName, target, Restore(entry, target)));
        }
    }

    private static void RequireIgnoredByGit(string target)
    {
        if (VaultLocation.FindGitRoot(System.IO.Path.GetDirectoryName(target)!) is not null && !GitIgnore.IsIgnored(target))
        {
            throw new VaultException(
                $"Not restoring '{target}': git does not ignore it. Add it to .gitignore, or use -SkipGitIgnoreCheck. Nothing was written.");
        }
    }

    private string Restore(VaultEntry entry, string target)
    {
        if (entry.Ssh is not null)
        {
            var plan = SshKeyFiles.Write(entry, Location.Path, RepositoryOrigin.Of(Location)?.Url, Overwrite, Now);
            if (plan.Refused)
            {
                WriteWarning(SshKeyFiles.Describe(plan, entry.Name));
                return "Kept";
            }

            return plan.State == SshKeyWriteState.Identical ? "Identical" : "Written";
        }

        var content = entry.GetValue();
        try
        {
            if (File.Exists(target))
            {
                if (File.ReadAllBytes(target).AsSpan().SequenceEqual(content))
                {
                    return "Identical";
                }

                if (!Overwrite && !(IsInteractive && Confirm($"'{target}' differs from the vault. Overwrite it?")))
                {
                    WriteWarning($"Kept the existing '{target}'; it differs from {entry.Name}. Use -Overwrite (lv restore --overwrite) to replace it.");
                    return "Kept";
                }
            }

            if (entry.IsUnderHome)
            {
                PrivateFile.Write(target, content);
            }
            else
            {
                SealedFile.WriteBytes(target, content, overwrite: true);
            }

            return "Written";
        }
        finally
        {
            Array.Clear(content);
        }
    }
}
