using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Security;
using System.Text;
using LocalVault.Core;
using LocalVault.Engine;

namespace LocalVault.PowerShell;

public sealed record VaultUserSecretInfo(string Id, string[] Projects, string Key, string? Entry, string State);

public abstract class UserSecretCmdletBase : VaultCmdletBase
{
    private UserSecretTracking? tracking;

    internal UserSecretTracking Tracking => tracking ??= new UserSecretTracking(Location);

    internal IReadOnlySet<string>? VaultFilter => ExplicitVault ? OwnVault : null;

    internal IReadOnlySet<string> OwnVault => new HashSet<string>([Location.Path], StringComparer.OrdinalIgnoreCase);

    internal string ResolveTarget(string target, bool trackedToo)
    {
        var projects = Tracking.Projects;
        if (projects.FirstOrDefault(p => string.Equals(p.Id, target, StringComparison.OrdinalIgnoreCase)) is { } byId)
        {
            return byId.Id;
        }

        if (trackedToo && Tracking.Tracking.FirstOrDefault(t => string.Equals(t.Id, target, StringComparison.OrdinalIgnoreCase)) is { } tracked)
        {
            return tracked.Id;
        }

        var named = projects.Where(p => string.Equals(p.Name, target, StringComparison.OrdinalIgnoreCase)).ToList();
        if (named.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
        {
            return named[0].Id;
        }

        if (named.Count > 1)
        {
            throw new VaultException($"Several projects are named '{target}': {string.Join(", ", named.Select(p => p.Path))}. Give the {DotNetProjects.IdProperty} instead.");
        }

        var known = projects.Count == 0
            ? $"No project of this repository has a {DotNetProjects.IdProperty}; dotnet user-secrets init --project <project> adds one."
            : "User secrets here: " + string.Join("; ", projects.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Select(g => $"{g.Key} ({string.Join(", ", g.Select(p => p.Name))})")) + ".";
        throw new VaultException($"No project of this repository is named '{target}' or has it as {DotNetProjects.IdProperty}. {known}");
    }

    internal static string RequireKey(string key) =>
        key.Trim() is { Length: > 0 } trimmed ? trimmed : throw new VaultException("The key is empty. Give a configuration key such as ConnectionStrings:Default.");
}

[Cmdlet(VerbsCommon.Get, "VaultUserSecretList")]
[OutputType(typeof(VaultUserSecretInfo))]
public sealed class GetVaultUserSecretListCommand : UserSecretCmdletBase
{
    [Parameter(Position = 0)]
    [ValidateNotNullOrEmpty]
    public string? Target { get; set; }

    protected override void Run()
    {
        var projects = Tracking.Projects;
        var only = Target is null ? null : ResolveTarget(Target, trackedToo: true);
        var ids = projects.Select(p => p.Id)
            .Concat(Tracking.Tracking.Where(t => VaultFilter is null || VaultFilter.Contains(t.Vault)).Select(t => t.Id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(id => only is null || string.Equals(id, only, StringComparison.OrdinalIgnoreCase));
        foreach (var id in ids)
        {
            var names = projects.Where(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)).Select(p => p.Name).ToArray();
            if (names.Length == 0)
            {
                WriteWarning($"user secrets {id}: no project of this repository uses it any more. Untrack its keys with lv user-secrets untrack {id} <key>.");
            }

            var current = UserSecretsFile.Read(UserSecretsFile.PathFor(id));
            var rows = 0;
            foreach (var value in Tracking.Values(id, VaultFilter))
            {
                var state = value.Value is null ? "NoValue"
                    : !current.TryGetValue(value.Key, out var node) ? "Absent"
                    : string.Equals(UserSecretsFile.Text(node), value.Value, StringComparison.Ordinal) ? "Written"
                    : "Differs";
                WriteObject(new VaultUserSecretInfo(id, names, value.Key, value.Reference, state));
                rows++;
            }

            var tracked = Tracking.Values(id).Select(v => v.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var key in current.Keys.Where(k => !tracked.Contains(k)))
            {
                WriteObject(new VaultUserSecretInfo(id, names, key, null, "Untracked"));
                rows++;
            }

            if (rows == 0)
            {
                WriteObject(new VaultUserSecretInfo(id, names, string.Empty, null, string.Empty));
            }
        }
    }
}

[Cmdlet(VerbsCommon.Set, "VaultUserSecret", DefaultParameterSetName = ValueSet)]
public sealed class SetVaultUserSecretCommand : UserSecretCmdletBase
{
    private const string ValueSet = "Value";
    private const string EntrySet = "Entry";
    private const string FromFileSet = "FromFile";

    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Target { get; set; } = string.Empty;

    [Parameter(Mandatory = true, Position = 1)]
    [ValidateNotNullOrEmpty]
    public string Key { get; set; } = string.Empty;

    [Parameter(ParameterSetName = ValueSet, ValueFromPipeline = true)]
    public SecureString? Value { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = EntrySet)]
    [ValidateNotNullOrEmpty]
    public string Entry { get; set; } = string.Empty;

    [Parameter(Mandatory = true, ParameterSetName = FromFileSet)]
    public SwitchParameter FromFile { get; set; }

    [Parameter(ParameterSetName = ValueSet)]
    [Parameter(ParameterSetName = FromFileSet)]
    public SwitchParameter Personal { get; set; }

    protected override void Run()
    {
        var id = ResolveTarget(Target, trackedToo: false);
        var label = Tracking.Label(id);
        var key = RequireKey(Key);
        if (Tracking.Tracking.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)
                && !OwnVault.Contains(t.Vault) && t.Keys.ContainsKey(key)) is { } other)
        {
            throw new VaultException($"{label}: {key} is already tracked by {other.Vault}. Nothing was changed.");
        }

        var own = Tracking.For(id, OwnVault).Select(t => t.Keys.GetValueOrDefault(key)).OfType<string>().FirstOrDefault();
        var body = ReadVault().Body;
        EntryReference reference;
        if (ParameterSetName == EntrySet)
        {
            reference = EntryReference.Parse(Entry);
            var entry = FindEntry(body, reference.Name);
            if (UserSecretTracking.Refusal(entry, reference) is { } refusal)
            {
                throw new VaultException($"{refusal} Nothing was changed.");
            }

            if (!entry.IsSecret && !entry.IsCredential && !entry.IsMissing)
            {
                throw new VaultException($"'{entry.Name}' is a {entry.Kind} entry, not a value for user secrets. Nothing was changed.");
            }
        }
        else
        {
            var text = FromFile ? FileValue(id, key, label) : TypedValue(key, label);
            reference = own is not null ? EntryReference.Parse(own) : new EntryReference(NewName(body, key));
            var existing = body.Find(reference.Name);
            if (existing is not null && UserSecretTracking.Refusal(existing, reference) is { } refusal)
            {
                throw new VaultException($"{refusal} Nothing was changed.");
            }

            if (Personal && existing is { IsRequired: false })
            {
                WriteWarning($"{reference.Name} exists already as a shared entry; it stays shared.");
            }

            StoreValue(reference, text, existing?.IsRequired ?? ChoosePersonal(key, label), key);
        }

        var project = ProjectFile.TrackUserSecret(Location, id, key, reference.ToString());
        if (!string.Equals(own, reference.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            Say($"{label}: {key} is tracked as {reference}. Commit {project} so that lv load fills it in on every clone.");
        }

        if (!FromFile)
        {
            new UserSecretTracking(Location).Load(this, OwnVault, id, key, overwrite: false, lenient: false);
        }
    }

    private static string FileValue(string id, string key, string label)
    {
        var path = UserSecretsFile.PathFor(id);
        return UserSecretsFile.Read(path).TryGetValue(key, out var node)
            ? UserSecretsFile.Text(node) is { Length: > 0 } text ? text : throw new VaultException($"{label}: {key} is empty in {path}. Nothing was changed.")
            : throw new VaultException($"{label}: {key} is not in {path}. Nothing was changed.");
    }

    private string TypedValue(string key, string label)
    {
        var text = SecretText.ToPlain(Value ?? ReadSecret($"Value of {key} for {label}"));
        return text.Length > 0 ? text : throw new VaultException("Empty value. Nothing was changed.");
    }

    private EntryName NewName(VaultBody body, string key)
    {
        var reason = !EntryName.TryParse(key, out var name) ? "it isn't a valid entry name"
            : body.Find(name) is not null ? "the vault already has an entry of that name"
            : null;
        if (reason is null)
        {
            return name;
        }

        if (!IsInteractive)
        {
            throw new VaultException($"'{key}' can't be the vault entry's name: {reason}. Run this in a console to choose a name, or map an existing entry with -Entry (--entry). Nothing was changed.");
        }

        var typed = ReadText($"'{key}' can't be the vault entry's name: {reason}. Name of the new entry");
        if (!EntryName.TryParse(typed, out name) || body.Find(name) is not null)
        {
            throw new VaultException($"'{typed}' is not a free entry name. Nothing was changed.");
        }

        return name;
    }

    private bool ChoosePersonal(string key, string label)
    {
        if (Personal || !IsInteractive)
        {
            return Personal;
        }

        Collection<ChoiceDescription> choices =
        [
            new("&Shared", "Everyone with access to the project's store gets this value with lv load."),
            new("&Personal", "Each person sets their own value; yours goes to your personal store."),
        ];
        return Host.UI.PromptForChoice("LocalVault", $"{label}: is {key} the same for the whole team, or personal?", choices, 0) == 1;
    }

    private void StoreValue(EntryReference reference, string text, bool personal, string key)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        try
        {
            var now = Now;
            Report(VaultFile.Update(
                body =>
                {
                    if (personal && body.Find(reference.Name) is null)
                    {
                        body.Set(VaultEntry.NewRequired(reference.Name, $"value of {key} in .NET user secrets", now));
                    }

                    var current = body.Find(reference.Name);
                    body.Set(current is { IsCredential: true } || reference.Part != EntryPart.Password
                        ? (current is { IsCredential: true } ? current : VaultEntry.NewCredential(reference.Name, null, [], null, [], now)).WithPart(reference, text, bytes, now)
                        : VaultEntry.NewSecret(reference.Name, bytes, now));
                },
                WriteOptions()));
        }
        finally
        {
            Array.Clear(bytes);
        }

        Say(personal ? $"Stored {reference} as a personal value; lv save copies it to your personal store." : $"Stored {reference}.");
    }
}

[Cmdlet(VerbsCommon.Get, "VaultUserSecret")]
[OutputType(typeof(SecureString), typeof(string))]
public sealed class GetVaultUserSecretCommand : UserSecretCmdletBase
{
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Target { get; set; } = string.Empty;

    [Parameter(Mandatory = true, Position = 1)]
    [ValidateNotNullOrEmpty]
    public string Key { get; set; } = string.Empty;

    [Parameter]
    public SwitchParameter AsPlainText { get; set; }

    protected override void Run()
    {
        var id = ResolveTarget(Target, trackedToo: true);
        var key = RequireKey(Key);
        var tracking = Tracking.For(id, VaultFilter).FirstOrDefault(t => t.Keys.ContainsKey(key))
            ?? throw new VaultException($"{Tracking.Label(id)}: {key} is not tracked. lv user-secrets lists the tracked keys.");
        var reference = EntryReference.Parse(tracking.Keys[key]);
        var body = OwnVault.Contains(tracking.Vault)
            ? ReadForUse(current => current.Find(reference.Name) is not null, _ => [reference.Name])
            : UserSecretTracking.VaultAt(tracking.Vault).Read().Body;
        RequireValue(FindEntry(body, reference.Name));
        WriteObject(PartOutput(FindEntry(Unseal(body, [reference.Name], "read"), reference.Name), reference, AsPlainText));
    }
}

[Cmdlet(VerbsCommon.Remove, "VaultUserSecret")]
public sealed class RemoveVaultUserSecretCommand : UserSecretCmdletBase
{
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Target { get; set; } = string.Empty;

    [Parameter(Mandatory = true, Position = 1)]
    [ValidateNotNullOrEmpty]
    public string Key { get; set; } = string.Empty;

    protected override void Run()
    {
        var id = ResolveTarget(Target, trackedToo: true);
        var key = RequireKey(Key);
        var label = Tracking.Label(id);
        if (!Tracking.For(id, OwnVault).Any(t => t.Keys.ContainsKey(key)))
        {
            WriteWarning($"{label}: {key} is not tracked by {Location.Path}.");
            return;
        }

        Tracking.Clear(this, OwnVault, id, key);
        var project = ProjectFile.UntrackUserSecret(Location, id, key) ? ProjectFile.PathFor(Location) : null;
        Say($"{label}: {key} is no longer tracked; its vault entry stays. Commit {project}.");
    }
}

[Cmdlet(VerbsCommunications.Write, "VaultUserSecret")]
public sealed class WriteVaultUserSecretCommand : UserSecretCmdletBase
{
    [Parameter(Position = 0)]
    [ValidateNotNullOrEmpty]
    public string? Target { get; set; }

    [Parameter]
    public SwitchParameter Overwrite { get; set; }

    protected override void Run()
    {
        if (Tracking.Tracking.Count == 0)
        {
            Say("No .NET user secrets are tracked in this repository. lv user-secrets set or lv user-secrets pick starts tracking one.");
            return;
        }

        Tracking.Load(this, VaultFilter, Target is null ? null : ResolveTarget(Target, trackedToo: true), null, Overwrite, lenient: false);
    }
}

[Cmdlet(VerbsCommon.Clear, "VaultUserSecret")]
public sealed class ClearVaultUserSecretCommand : UserSecretCmdletBase
{
    [Parameter(Position = 0)]
    [ValidateNotNullOrEmpty]
    public string? Target { get; set; }

    protected override void Run() =>
        Tracking.Clear(this, VaultFilter, Target is null ? null : ResolveTarget(Target, trackedToo: true), null);
}
