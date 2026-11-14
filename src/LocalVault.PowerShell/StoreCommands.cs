using System.Management.Automation;
using LocalVault.Core;

namespace LocalVault.PowerShell;

public sealed record VaultStoreInfo(string Name, StoreScope Scope, string Type, string Target, string? KeyFile, bool Default, string? UnlockWith, bool Mirror, bool Exists, bool Personal = false);

public abstract class StoreDefinitionCmdletBase : VaultCmdletBase
{
    internal static StoreRegistry Registry => new(StoreRegistry.DefaultPath);

    internal static VaultStoreInfo Describe(StoreDefinition store, bool isDefault)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new VaultStoreInfo(
            store.Name, store.Scope, store.Type, store.Target, store.KeyFile, isDefault, store.UnlockWith, store.IsMirror, File.Exists(store.ExpandedTarget), store.IsPersonal);
    }

    internal VaultLocation? ProjectLocation()
    {
        try
        {
            return Location.GitRoot is null ? null : Location;
        }
        catch (VaultException)
        {
            return null;
        }
    }

    internal VaultLocation RequireProject() => ProjectLocation()
        ?? throw new VaultException("Project stores live in localvault.json at the root of a git repository; this folder is not in one.");

    internal static bool IsProjectDefault(VaultLocation project, string name) =>
        ProjectFile.ReadDefault(project) is { } reference
        && string.Equals(reference.Name, name, StringComparison.OrdinalIgnoreCase);

    internal static IEnumerable<VaultStoreInfo> ProjectEntries(VaultLocation project)
    {
        var defined = ProjectFile.ReadStores(project);
        foreach (var entry in ProjectFile.ReadEntries(project))
        {
            if (defined.FirstOrDefault(store => string.Equals(store.Name, entry.Name, StringComparison.OrdinalIgnoreCase)) is { } store)
            {
                yield return Describe(store, IsProjectDefault(project, store.Name));
                continue;
            }

            var local = Registry.FindByName(entry.Name);
            yield return new VaultStoreInfo(
                entry.Name, StoreScope.Project, "reference", local?.Target ?? "(not on this PC)", null,
                IsProjectDefault(project, entry.Name), null, entry.IsMirror, local is not null && File.Exists(local.ExpandedTarget));
        }
    }

    internal const string PersonalOnPcOnly =
        "The personal store belongs to this PC: give -Personal without -Project (lv store add|set <name> --personal).";

    internal void SayPersonal(StoreDefinition store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (store.IsPersonal)
        {
            Say($"'{store.Name}' is this PC's personal store: lv save keeps your own values of required entries there, lv load reads them back.");
        }
    }

    internal string Rooted(string path) =>
        path.Contains('%', StringComparison.Ordinal) ? path : GetUnresolvedProviderPathFromPSPath(path);
}

[Cmdlet(VerbsLifecycle.Register, "VaultStore", SupportsShouldProcess = true)]
[OutputType(typeof(VaultStoreInfo))]
public sealed class RegisterVaultStoreCommand : StoreDefinitionCmdletBase
{
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Name { get; set; }

    [Parameter(Position = 0)]
    [ValidateNotNullOrEmpty]
    public string? Target { get; set; }

    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? KeyFile { get; set; }

    [Parameter]
    public SwitchParameter Default { get; set; }

    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? UnlockWith { get; set; }

    [Parameter]
    public SwitchParameter Mirror { get; set; }

    [Parameter]
    public SwitchParameter Personal { get; set; }

    [Parameter]
    public SwitchParameter Project { get; set; }

    protected override void Run()
    {
        if (Target is null && (!Project || Name is null))
        {
            throw new VaultException("Give a target, e.g. a .kdbx file. In a project, -Name alone refers to the store of that name defined on each PC.");
        }

        if (Target is not null)
        {
            StoreTypes.Of(Target);
        }

        var name = Name ?? StoreTypes.NameFromTarget(Target!);
        var target = Target is null ? null : Rooted(Target);
        if (!Project)
        {
            var store = Registry.Register(name, target!, KeyFile is null ? null : Rooted(KeyFile), Default, UnlockWith, Mirror, Personal);
            SayPersonal(store);
            WriteObject(Describe(store, store.IsDefault));
            return;
        }

        if (Personal)
        {
            throw new VaultException(PersonalOnPcOnly);
        }

        if (KeyFile is not null || UnlockWith is not null)
        {
            throw new VaultException(
                "A project store can't name a key file or an unlocking store: those are personal. Add the same target on this PC with lv store add <name> <target> [<keyfile>] [--unlock-with <store>].");
        }

        var project = RequireProject();
        var file = ProjectFile.PathFor(project)!;
        var what = target is null ? $"Refer to the store '{name}' defined on each PC" : $"Define store '{name}' ({target})";
        if (!ShouldProcess(file, what + (Mirror ? ", as a mirror" : string.Empty)))
        {
            return;
        }

        ProjectFile.AddStore(project, name, target, Default, Mirror);
        Say($"{file} now {(target is null ? "refers to" : "defines")} store '{name}'{(Mirror ? " as a mirror: every save also goes there" : string.Empty)}. Commit it.");
        if (target is null && Registry.FindByName(name) is null)
        {
            WriteWarning($"No store named '{name}' on this PC yet. Add it with lv store add {name} <target>.");
        }

        WriteObject(ProjectEntries(project).Single(info => string.Equals(info.Name, name, StringComparison.OrdinalIgnoreCase)));
    }
}

[Cmdlet(VerbsCommon.Set, "VaultStore", SupportsShouldProcess = true)]
[OutputType(typeof(VaultStoreInfo))]
public sealed class SetVaultStoreCommand : StoreDefinitionCmdletBase
{
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    [Parameter]
    public SwitchParameter Mirror { get; set; }

    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? UnlockWith { get; set; }

    [Parameter]
    public SwitchParameter NoUnlock { get; set; }

    [Parameter]
    public SwitchParameter Default { get; set; }

    [Parameter]
    public SwitchParameter Personal { get; set; }

    [Parameter]
    public SwitchParameter Project { get; set; }

    protected override void Run()
    {
        bool? mirror = MyInvocation.BoundParameters.ContainsKey(nameof(Mirror)) ? Mirror.IsPresent : null;
        bool? personal = MyInvocation.BoundParameters.ContainsKey(nameof(Personal)) ? Personal.IsPresent : null;
        if (UnlockWith is not null && NoUnlock)
        {
            throw new VaultException("Give -UnlockWith or -NoUnlock, not both.");
        }

        if (!Project)
        {
            var store = Registry.Change(Name, mirror, UnlockWith, NoUnlock, Default, personal);
            if (personal == true)
            {
                SayPersonal(store);
            }

            WriteObject(Describe(store, store.IsDefault));
            return;
        }

        if (UnlockWith is not null || NoUnlock)
        {
            throw new VaultException("A project store can't name an unlocking store: that is personal. Change the store of the same name on this PC.");
        }

        if (personal is not null)
        {
            throw new VaultException(PersonalOnPcOnly);
        }

        var project = RequireProject();
        if (!ShouldProcess(ProjectFile.PathFor(project)!, $"Change store '{Name}'"))
        {
            return;
        }

        ProjectFile.ChangeStore(project, Name, mirror, Default);
        Say($"{ProjectFile.PathFor(project)} changed. Commit it.");
        WriteObject(ProjectEntries(project).Single(info => string.Equals(info.Name, Name, StringComparison.OrdinalIgnoreCase)));
    }
}

[Cmdlet(VerbsLifecycle.Unregister, "VaultStore", SupportsShouldProcess = true)]
public sealed class UnregisterVaultStoreCommand : StoreDefinitionCmdletBase
{
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    [Parameter]
    public SwitchParameter Project { get; set; }

    protected override void Run()
    {
        if (!Project)
        {
            if (!Registry.Unregister(Name))
            {
                WriteWarning($"No store named '{Name}' on this PC.");
            }

            return;
        }

        var project = RequireProject();
        if (!ShouldProcess(ProjectFile.PathFor(project)!, $"Remove store '{Name}'"))
        {
            return;
        }

        if (!ProjectFile.RemoveStore(project, Name))
        {
            WriteWarning($"No store named '{Name}' in {ProjectFile.FileName}.");
        }
    }
}

[Cmdlet(VerbsCommon.Get, "VaultStore")]
[OutputType(typeof(VaultStoreInfo))]
public sealed class GetVaultStoreCommand : StoreDefinitionCmdletBase
{
    [Parameter(Position = 0)]
    [ValidateNotNullOrEmpty]
    public string? Name { get; set; }

    protected override void Run()
    {
        var project = ProjectLocation();
        var stores = (project is null ? [] : ProjectEntries(project))
            .Concat(Registry.Load().Select(store => Describe(store, store.IsDefault)));
        foreach (var store in stores.Where(s => Name is null || string.Equals(s.Name, Name, StringComparison.OrdinalIgnoreCase)))
        {
            WriteObject(store);
        }
    }
}

public sealed record StoreSaveSummary(int Added, int Updated, int Removed, int Skipped);

public sealed record StoreLoadSummary(int Added, int Updated, int Kept, int Removed);

public sealed record StoreEntryState(string Name, string Kind, KeePassSyncState State);

public sealed record VaultStoreStatus(
    string Vault,
    string Group,
    string? Store,
    string? Scope,
    string? Target,
    string? Problem,
    string? UnlockWith,
    string? Mirrors,
    int Shared,
    int Saved,
    int Pending,
    int Local,
    int RemovalsPending);

public abstract class StoreSyncCmdletBase : VaultCmdletBase
{
    internal static string? MirrorNames(IReadOnlyList<StoreResolution> mirrors) =>
        mirrors.Count == 0
            ? null
            : string.Join(", ", mirrors.Select(m => m.Definition is null ? $"{m.Label} (not on this PC)" : m.Label));

    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Store { get; set; }

    protected IReadOnlyList<string> Group => KeePassGroupPath.For(Location);

    internal StoreAccess NewAccess() => new(this, Store);
}

[Cmdlet(VerbsData.Export, "VaultToStore")]
[OutputType(typeof(StoreSaveSummary))]
public sealed class ExportVaultToStoreCommand : StoreSyncCmdletBase
{
    [Parameter]
    public string[] Replace { get; set; } = [];

    [Parameter]
    public SwitchParameter ReplaceAll { get; set; }

    protected override void Run()
    {
        using var access = NewAccess();
        var totals = VaultSync.Save(this, access, RepositoryVaults.Of(Location, ExplicitVault), (location, name) =>
            ReplaceAll
            || Replace.Contains(name.FullName, EntryName.Comparer)
            || (IsInteractive && Confirm($"{RepositoryVaults.Label(location)}: '{name}' changed here and in the store. Overwrite the store's value with yours?")));
        WriteObject(new StoreSaveSummary(totals.Added, totals.Updated, totals.Removed, totals.Kept));
    }
}

[Cmdlet(VerbsData.Import, "VaultFromStore")]
[OutputType(typeof(StoreLoadSummary))]
public sealed class ImportVaultFromStoreCommand : StoreSyncCmdletBase
{
    [Parameter]
    public string[] Replace { get; set; } = [];

    [Parameter]
    public SwitchParameter ReplaceAll { get; set; }

    protected override void Run()
    {
        using var access = NewAccess();
        var vaults = RepositoryVaults.Of(Location, ExplicitVault);
        var (totals, unresolved) = VaultSync.Load(
            this,
            access,
            vaults,
            (location, name) => ReplaceAll || Replace.Contains(name.FullName, EntryName.Comparer)
                ? true
                : IsInteractive ? Confirm($"{RepositoryVaults.Label(location)}: '{name}' changed here and in the store. Replace yours with the store's value?") : null,
            (location, name) => IsInteractive && Confirm($"{RepositoryVaults.Label(location)}: '{name}' was removed from the store. Remove it here too?"));
        if (unresolved.Count > 0)
        {
            throw new ImportConflictException(
                "These entries changed both here and in the store: " + string.Join(", ", unresolved) +
                ". Their vaults were left as they were. Use -Replace name,... or -ReplaceAll to take the store's values.");
        }

        var present = vaults.Where(location => File.Exists(location.Path)).ToList();
        FillMissingValues(access, present);
        AdoptNewerSshKeys(present);
        WritePersonalFiles(present);
        WriteObject(new StoreLoadSummary(totals.Added, totals.Updated, totals.Kept, totals.Removed));
    }
}

[Cmdlet(VerbsData.Compare, "VaultWithStore")]
[OutputType(typeof(StoreEntryState))]
public sealed class CompareVaultWithStoreCommand : StoreSyncCmdletBase
{
    protected override void Run()
    {
        var body = ReadVault().Body;
        using var access = NewAccess();
        var (keePass, _) = access.For(Location);
        using var content = keePass.Read(Group);
        foreach (var title in content.Skipped)
        {
            WriteWarning($"KeePass entry left out: {title}");
        }

        foreach (var comparison in KeePassSync.Compare(body, content))
        {
            WriteObject(new StoreEntryState(comparison.Name.FullName, comparison.Kind, comparison.State));
        }
    }
}

[Cmdlet(VerbsCommon.Get, "VaultStoreStatus")]
[OutputType(typeof(VaultStoreStatus))]
public sealed class GetVaultStoreStatusCommand : StoreSyncCmdletBase
{
    protected override void Run()
    {
        var registry = new StoreRegistry(StoreRegistry.DefaultPath);
        foreach (var location in RepositoryVaults.Of(Location, ExplicitVault))
        {
            var resolution = StoreResolver.For(location, Store, registry);
            var store = RepositoryVaults.StoreFor(location);
            var snapshot = store.Read();
            var states = snapshot.Exists ? KeePassSync.ComparePending(snapshot.Body, resolution.DatabaseId) : [];
            WriteObject(new VaultStoreStatus(
                resolution.Vault,
                KeePassGroupPath.Display(KeePassGroupPath.For(location)),
                resolution.Definition?.Name ?? (resolution.Reference is null ? null : resolution.Label),
                resolution.Definition?.Scope.ToString(),
                resolution.Definition?.Target,
                resolution.Problem,
                resolution.Definition?.UnlockWith,
                Store is null ? MirrorNames(StoreResolver.MirrorsFor(location, resolution, registry)) : null,
                states.Count(s => s.State is not KeePassSyncState.Local and not KeePassSyncState.RemovalPending),
                states.Count(s => s.State == KeePassSyncState.Saved),
                states.Count(s => s.State is KeePassSyncState.New or KeePassSyncState.Changed),
                states.Count(s => s.State == KeePassSyncState.Local),
                states.Count(s => s.State == KeePassSyncState.RemovalPending)));
        }
    }
}

public sealed record StoreCopySummary(string Target, string Group, int Written);

[Cmdlet(VerbsCommon.Copy, "VaultToStore")]
[OutputType(typeof(StoreCopySummary))]
public sealed class CopyVaultToStoreCommand : StoreSyncCmdletBase
{
    [Parameter(Position = 0)]
    [ValidateNotNullOrEmpty]
    public string? Target { get; set; }

    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? KeyFile { get; set; }

    protected override void Run()
    {
        if ((Target is null) == (Store is null) || (Store is not null && KeyFile is not null))
        {
            throw new VaultException(
                "Give a target with -Target (and -KeyFile if it has one), or a defined store with -Store.");
        }

        var body = ReadVault().Body;
        var registry = new StoreRegistry(StoreRegistry.DefaultPath);
        StoreDefinition target;
        if (Store is not null)
        {
            target = StoreResolver.Find(Store, Location, registry)
                ?? throw new VaultException($"No store named '{Store}' in {ProjectFile.FileName} or on this PC.");
        }
        else
        {
            var path = GetUnresolvedProviderPathFromPSPath(Target!);
            target = new StoreDefinition(
                StoreTypes.NameFromTarget(path),
                StoreTypes.Of(path),
                path,
                KeyFile is null ? null : GetUnresolvedProviderPathFromPSPath(KeyFile),
                StoreScope.Computer);
        }

        using var access = NewAccess();
        var keePass = access.Explicit(target);
        var copied = KeePassSync.Copy(body, keePass, Group, new HelloContext(HelloEntries, Location.Path));
        var written = copied.Written;
        foreach (var missing in copied.HelloMissing)
        {
            WriteWarning($"'{missing.Name}' not copied: {missing.Reason}. lv load restores it from the store.");
        }

        var guidesChanged = KeePassSync.MaintainGuides(keePass, Location, RepositoryOrigin.Of(Location));
        if (written > 0 || guidesChanged)
        {
            keePass.Save();
        }

        Say($"Copied {written} shared entries to {target.ExpandedTarget}, group {KeePassGroupPath.Display(Group)}. The vault records nothing about this copy.");
        WriteObject(new StoreCopySummary(target.ExpandedTarget, KeePassGroupPath.Display(Group), written));
    }
}

[Cmdlet(VerbsCommon.Set, "VaultProjectStore", SupportsShouldProcess = true)]
public sealed class SetVaultProjectStoreCommand : VaultCmdletBase
{
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    protected override void Run()
    {
        var registry = new StoreRegistry(StoreRegistry.DefaultPath);
        var store = StoreResolver.Find(Name, Location, registry)
            ?? throw new VaultException($"No store named '{Name}' in {ProjectFile.FileName} or on this PC. Define it first with lv store add.");
        var file = ProjectFile.PathFor(Location)
            ?? throw new VaultException("A project file needs a git repository; this vault is not in one.");
        var scope = ExplicitVault ? RepositoryVaults.Label(Location) : "every vault of the repository";
        if (!ShouldProcess(file, $"Use store '{store.Name}' as the primary store of {scope}"))
        {
            return;
        }

        string id;
        using (var access = new StoreAccess(this, null))
        {
            id = access.Explicit(store).DatabaseId;
        }

        if (store.Scope == StoreScope.Project)
        {
            ProjectFile.RecordStoreId(Location, store.Name, id);
        }
        else
        {
            registry.RecordId(store, id);
        }

        ProjectFile.Assign(Location, new StoreReference(store.Name, id), wholeRepository: !ExplicitVault);
        Say($"{file} now says {scope} uses store '{store.Name}' as its primary store. Commit it, so that everyone saves into the same store.");
    }
}
