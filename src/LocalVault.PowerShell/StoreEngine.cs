using System.Security;
using LocalVault.Core;

namespace LocalVault.PowerShell;

internal interface IVaultHost
{
    bool CanPrompt { get; }

    bool CanConfirm { get; }

    DateTimeOffset Clock { get; }

    void Tell(string message);

    void Warn(string message);

    SecureString AskSecret(string prompt);

    bool AskYesNo(string question);

    void ReportWrite(VaultWriteResult result);

    HelloEntries Hello { get; }
}

internal sealed class StoreAccess(IVaultHost host, string? storeName) : IDisposable
{
    private const int MaxUnlockChain = 8;
    private readonly StoreRegistry registry = new(StoreRegistry.DefaultPath);
    private readonly Dictionary<string, (IStore Vault, StoreDefinition Store)> opened = new(StringComparer.OrdinalIgnoreCase);

    public StoreRegistry Registry => registry;

    public bool StoreNamed => storeName is not null;

    public StoreResolution Resolve(VaultLocation location) => StoreResolver.For(location, storeName, registry);

    public IReadOnlyList<StoreResolution> MirrorsFor(VaultLocation location, StoreResolution primary) =>
        storeName is null ? StoreResolver.MirrorsFor(location, primary, registry) : [];

    public (IStore Vault, StoreDefinition Store) For(VaultLocation location) => For(Resolve(location));

    public (IStore Vault, StoreDefinition Store) For(StoreResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        var id = resolution.DatabaseId;
        if (id is not null && opened.TryGetValue(id, out var hit))
        {
            return hit;
        }

        var candidate = resolution.Definition ?? throw new StoreNotDefinedException(resolution.Problem!);
        var pair = OpenStore(candidate, id, 0);
        if (id is not null && !string.Equals(pair.Vault.DatabaseId, id, StringComparison.OrdinalIgnoreCase))
        {
            throw new VaultException(
                $"{resolution.Vault} uses store '{resolution.Label}', and '{pair.Store.ExpandedTarget}' is another database. " +
                "Point the store at the right file with lv store add.");
        }

        registry.RecordId(pair.Store, pair.Vault.DatabaseId);
        return pair;
    }

    public IStore Explicit(StoreDefinition store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return OpenStore(store, store.Id, 0).Vault;
    }

    public void Dispose()
    {
        foreach (var vault in opened.Values.Select(pair => pair.Vault).Distinct())
        {
            vault.Dispose();
        }
    }

    private static string TargetKey(StoreDefinition store) => "target:" + Path.GetFullPath(store.ExpandedTarget);

    private (IStore Vault, StoreDefinition Store) Remember(IStore vault, StoreDefinition store)
    {
        if (store.Id is { } recorded && !string.Equals(recorded, vault.DatabaseId, StringComparison.OrdinalIgnoreCase))
        {
            vault.Dispose();
            throw new VaultException(
                $"'{store.ExpandedTarget}' isn't the KeePass database that store '{store.Name}' was added with: another database replaced it. " +
                $"If that is intended, add the store again: lv store add {store.Name} \"{store.Target}\"{(store.Scope == StoreScope.Project ? " --project" : string.Empty)}");
        }

        var pair = (vault, store);
        opened[vault.DatabaseId] = pair;
        opened[TargetKey(store)] = pair;
        return pair;
    }

    private (IStore Vault, StoreDefinition Store) OpenStore(StoreDefinition store, string? id, int depth)
    {
        if (opened.TryGetValue(TargetKey(store), out var hit))
        {
            return hit;
        }

        _ = StoreProviders.For(store);
        if (store.UnlockWith is null)
        {
            host.Tell($"Opening store '{store.Name}' (KeePass {store.ExpandedTarget}).");
            return Remember(Prompted(store), store);
        }

        if (depth >= MaxUnlockChain)
        {
            throw new VaultException($"Store '{store.Name}' is unlocked through more than {MaxUnlockChain} stores. Check --unlock-with in lv store list.");
        }

        var unlockerStore = registry.FindByName(store.UnlockWith)
            ?? throw new VaultException($"Store '{store.Name}' is unlocked with '{store.UnlockWith}', which isn't defined on this PC. Add it with lv store add {store.UnlockWith} <target>.");
        var (unlocking, _) = OpenStore(unlockerStore, unlockerStore.Id, depth + 1);
        var unlocker = unlocking as IUnlockingStore
            ?? throw new VaultException($"Store '{store.Name}' is unlocked with '{unlockerStore.Name}', a {unlockerStore.Type} store, which can't keep other stores' passwords. Remove it with lv store set {store.Name} --no-unlock.");
        registry.RecordId(unlockerStore, unlocker.DatabaseId);
        using (var target = unlocker.FindAutoOpen(id ?? store.Id, [store.ExpandedTarget], Environment.MachineName))
        {
            if (target is not null)
            {
                host.Tell($"Opening store '{store.Name}' (KeePass {target.Path}) with the password kept in '{unlockerStore.Name}'.");
                return Remember(StoreProviders.For(store).Open(target.Path, target.KeyFile ?? store.ExpandedKeyFile, target.Password), store);
            }
        }

        host.Tell($"Opening store '{store.Name}' (KeePass {store.ExpandedTarget}). '{unlockerStore.Name}' doesn't hold its password yet.");
        var master = ReadMaster(store);
        try
        {
            var vault = StoreProviders.For(store).Open(store.ExpandedTarget, store.ExpandedKeyFile, master);
            if (host.CanConfirm && host.AskYesNo($"Keep the password of '{store.Name}' in '{unlockerStore.Name}' (AutoOpen group), so that next time '{store.Name}' opens with it?"))
            {
                unlocker.RememberAutoOpen(store.Name, store.ExpandedTarget, store.ExpandedKeyFile, master, vault.DatabaseId);
                unlocker.Save();
                host.Tell($"Kept in '{unlockerStore.Name}'. If KeePass has {Path.GetFileName(unlockerStore.ExpandedTarget)} open, reload it there before saving.");
            }

            return Remember(vault, store);
        }
        finally
        {
            Array.Clear(master);
        }
    }

    private IStore Prompted(StoreDefinition store)
    {
        var master = ReadMaster(store);
        try
        {
            return StoreProviders.For(store).Open(store.ExpandedTarget, store.ExpandedKeyFile, master);
        }
        finally
        {
            Array.Clear(master);
        }
    }

    private char[] ReadMaster(StoreDefinition store)
    {
        if (!host.CanPrompt)
        {
            throw new VaultException("The KeePass master password is needed and there is no console to ask on.");
        }

        return SecretText.ToChars(host.AskSecret($"KeePass master password for {store.Name}"));
    }
}

internal sealed record VaultSyncTotals(int Vaults, int Added, int Updated, int Removed, int Kept);

internal sealed record PersonalTarget(IStore Vault, StoreDefinition Store, bool WithDeclaration);

internal sealed class PersonalStore(IVaultHost host, StoreAccess access)
{
    private bool failed;
    private bool? shareWithPrimary;

    public StoreDefinition? Definition { get; } = access.Registry.Personal;

    public bool Is(StoreResolution resolution) => Definition is not null && SameStore(resolution, Definition);

    public PersonalTarget? Open(StoreResolution primary, string? primaryDatabaseId = null)
    {
        ArgumentNullException.ThrowIfNull(primary);
        if (Definition is null || failed)
        {
            return null;
        }

        (IStore Vault, StoreDefinition Store) opened;
        try
        {
            opened = access.For(new StoreResolution(primary.Vault, new StoreReference(Definition.Name, Definition.Id), Definition, null));
        }
        catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
        {
            failed = true;
            host.Warn($"Personal store '{Definition.Name}' not reached: {ex.Message} Your own values stay on this PC.");
            return null;
        }

        var primaryId = primaryDatabaseId ?? primary.DatabaseId;
        var sharesPrimary = (primaryId is not null && string.Equals(primaryId, opened.Vault.DatabaseId, StringComparison.OrdinalIgnoreCase))
            || SameStore(primary, Definition);
        if (sharesPrimary && !(shareWithPrimary ??= AskShare()))
        {
            return null;
        }

        return new PersonalTarget(opened.Vault, opened.Store, sharesPrimary);
    }

    private bool AskShare()
    {
        var question = $"Personal store '{Definition!.Name}' is also the project's store: your own values would be readable by everyone who opens it.";
        if (host.CanConfirm && host.AskYesNo(question + " Keep them there anyway?"))
        {
            return true;
        }

        host.Warn(question + " Your own values were left out of it.");
        return false;
    }

    private static bool SameStore(StoreResolution resolution, StoreDefinition store) =>
        (resolution.DatabaseId is { } id && store.Id is { } other && string.Equals(id, other, StringComparison.OrdinalIgnoreCase))
        || (resolution.Definition is { } definition
            && string.Equals(Path.GetFullPath(definition.ExpandedTarget), Path.GetFullPath(store.ExpandedTarget), StringComparison.OrdinalIgnoreCase));
}

internal sealed class ReferenceStores(StoreAccess access, VaultLocation location, string? vaultStoreId) : IReferenceStores
{
    private readonly Dictionary<string, string> failures = new(StringComparer.OrdinalIgnoreCase);

    public ReferenceStore Open(ItemReference reference, bool personal)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var id = reference.StoreId;
        var (definition, name) = Find(id);
        if (definition is null)
        {
            throw new VaultException(name is null
                ? $"its value is in a KeePass database this PC doesn't know (id {id})."
                : $"its value is in store '{name}', which isn't defined on this PC. Add it with lv store add {name} <path to .kdbx>.");
        }

        if (!personal && !MayHoldSharedValues(id))
        {
            throw new VaultException($"shared values can only come from the project's own stores, and store '{definition.Name}' isn't one of them.");
        }

        if (failures.TryGetValue(id, out var failure))
        {
            throw new VaultException(failure);
        }

        try
        {
            var (vault, store) = access.For(new StoreResolution(RepositoryVaults.Label(location), new StoreReference(definition.Name, id), definition, null));
            return new ReferenceStore(vault, store.Name);
        }
        catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
        {
            failures[id] = $"store '{definition.Name}' couldn't be opened: {ex.Message}";
            throw new VaultException(failures[id], ex);
        }
    }

    public bool MayHoldSharedValues(string id)
    {
        var registry = access.Registry;
        var forbidden = registry.Load()
            .Where(store => store.IsPersonal || registry.Load().Any(other => string.Equals(other.UnlockWith, store.Name, StringComparison.OrdinalIgnoreCase)))
            .Any(store => string.Equals(store.Id, id, StringComparison.OrdinalIgnoreCase));
        var allowed = string.Equals(id, vaultStoreId, StringComparison.OrdinalIgnoreCase)
            || ProjectFile.ReadEntries(location).Any(entry => entry.IsSource && string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));
        return allowed && !forbidden;
    }

    public (StoreDefinition? Definition, string? Name) Find(string id)
    {
        var project = ProjectFile.ReadEntries(location).FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));
        var definition = StoreResolver.Find(new StoreReference(project?.Name, id), location, access.Registry);
        return (definition, definition?.Name ?? project?.Name);
    }
}

internal static class RepositoryVaults
{
    public static IReadOnlyList<VaultLocation> Of(VaultLocation current, bool onlyCurrent)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (onlyCurrent)
        {
            return [current];
        }

        var listed = ProjectFile.ReadVaults(current)
            .Select(vault => VaultLocation.Resolve(vault.Path, null, Path.GetDirectoryName(vault.Path)!))
            .ToList();
        if (listed.Count == 0 || (File.Exists(current.Path) && !listed.Any(l => Same(l, current))))
        {
            listed.Insert(0, current);
        }

        return listed.DistinctBy(l => l.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string Label(VaultLocation location) => StoreResolver.VaultLabel(location);

    public static VaultFile StoreFor(VaultLocation location) => new(location, new ClixmlLegacyReader());

    private static bool Same(VaultLocation a, VaultLocation b) => string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
}

internal static class VaultSync
{
    public static VaultSyncTotals Save(
        IVaultHost host,
        StoreAccess access,
        IReadOnlyList<VaultLocation> vaults,
        Func<VaultLocation, EntryName, bool> overwrite,
        bool personalOnly = false)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(vaults);
        var done = new List<SavedToStore>();
        var snapshots = new Dictionary<VaultLocation, (VaultFile Store, VaultSnapshot Snapshot)>();
        var undefined = new List<StoreNotDefinedException>();
        var origins = new Dictionary<string, RepositoryOrigin?>(StringComparer.OrdinalIgnoreCase);
        var personal = new PersonalStore(host, access);
        foreach (var location in vaults)
        {
            var store = RepositoryVaults.StoreFor(location);
            var snapshot = store.Read();
            if (!snapshot.Exists)
            {
                host.Warn($"{RepositoryVaults.Label(location)}: no vault on this PC; nothing to save.");
                continue;
            }

            var primary = access.Resolve(location);
            var origin = OriginOf(location, origins);
            if (personalOnly)
            {
                snapshots[location] = (store, snapshot);
                SavePersonal(host, location, snapshot.Body, personal, primary, origin, overwrite, done);
                continue;
            }

            if (OpenOrSkip(host, access, primary, vaults.Count, undefined) is not { } reachedStore)
            {
                continue;
            }

            snapshots[location] = (store, snapshot);
            done.Add(SaveInto(host, location, snapshot.Body, reachedStore, origin, mirror: false, name => overwrite(location, name)));
            foreach (var mirror in access.MirrorsFor(location, primary))
            {
                if (mirror.Definition is null)
                {
                    host.Tell($"{mirror.Problem} Skipped.");
                    continue;
                }

                try
                {
                    done.Add(SaveInto(host, location, snapshot.Body, access.For(mirror), origin, mirror: true, _ => true, personal.Is(mirror)));
                }
                catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
                {
                    host.Warn($"{RepositoryVaults.Label(location)}: mirror '{mirror.Label}' not saved: {ex.Message}");
                }
            }

            if (!access.StoreNamed)
            {
                SavePersonal(host, location, snapshot.Body, personal, primary, origin, overwrite, done);
            }
        }

        ThrowIfNothingReached(snapshots.Count, undefined);
        foreach (var keePass in done.Where(d => d.Changed).Select(d => d.KeePass).Distinct().ToList())
        {
            try
            {
                keePass.Save();
            }
            catch (Exception ex) when ((ex is VaultException or IOException or UnauthorizedAccessException) && done.Where(d => d.KeePass == keePass).All(d => d.Mirror || d.Personal))
            {
                host.Warn($"Store '{done.First(d => d.KeePass == keePass).Target.Name}' not saved: {ex.Message}");
                done.RemoveAll(d => d.KeePass == keePass);
            }
        }

        foreach (var (location, (store, snapshot)) in snapshots)
        {
            var results = done.Where(d => d.Location == location).Select(d => d.Result).ToList();
            if (results.Any(result => result.DroppedRemovals.Count > 0
                    || result.Bases.Any(b => snapshot.Body.Find(b.Name)?.KeePassBaseFor(result.DatabaseId) != b.Hash)))
            {
                host.ReportWrite(store.Update(body =>
                {
                    foreach (var result in results)
                    {
                        StoreSync.RecordSaved(body, result, host.Clock);
                    }
                }));
            }
        }

        foreach (var saved in done)
        {
            Report(host, saved);
        }

        foreach (var target in done.Where(d => d.Changed).Select(d => d.Target).DistinctBy(t => Path.GetFullPath(t.ExpandedTarget), StringComparer.OrdinalIgnoreCase))
        {
            host.Tell($"If KeePass 2 has {Path.GetFileName(target.ExpandedTarget)} open, reload it there before saving, or these changes can be lost.");
        }

        var primaries = done.Where(d => !d.Mirror && !d.Personal).ToList();
        return new VaultSyncTotals(
            primaries.Count, primaries.Sum(d => d.Result.Added), primaries.Sum(d => d.Result.Updated), primaries.Sum(d => d.Result.Deleted), primaries.Sum(d => d.Result.Skipped.Count));
    }

    private sealed record SavedToStore(VaultLocation Location, StoreSaveResult Result, IStore KeePass, StoreDefinition Target, bool Mirror, bool GuidesChanged)
    {
        public bool Changed => GuidesChanged || Result.Added + Result.Updated + Result.Deleted > 0;

        public bool Personal => Result.Personal;
    }

    private static void SavePersonal(
        IVaultHost host,
        VaultLocation location,
        VaultBody body,
        PersonalStore personal,
        StoreResolution primary,
        RepositoryOrigin? origin,
        Func<VaultLocation, EntryName, bool> overwrite,
        List<SavedToStore> done)
    {
        if (!body.Entries.Any(entry => entry.IsRequired && !entry.IsMissing)
            && !body.KeePassRemovals.Any(r => r.Database?.StartsWith(VaultEntry.PersonalBasePrefix, StringComparison.Ordinal) == true))
        {
            return;
        }

        if (personal.Open(primary) is not { } target)
        {
            return;
        }

        var group = StoreGroupPath.For(location);
        using var content = target.Vault.Read(group);
        var result = StoreSync.SavePersonal(
            body, content, target.Vault, group, target.WithDeclaration, name => overwrite(location, name), new HelloContext(host.Hello, location.Path));
        var guidesChanged = StoreSync.MaintainGuides(target.Vault, location, origin);
        done.Add(new SavedToStore(location, result, target.Vault, target.Store, Mirror: false, guidesChanged));
    }

    private static RepositoryOrigin? OriginOf(VaultLocation location, Dictionary<string, RepositoryOrigin?> known)
    {
        if (location.GitRoot is not { } gitRoot)
        {
            return null;
        }

        if (!known.TryGetValue(gitRoot, out var origin))
        {
            known[gitRoot] = origin = RepositoryOrigin.Of(location);
        }

        return origin;
    }

    private static SavedToStore SaveInto(
        IVaultHost host,
        VaultLocation location,
        VaultBody body,
        (IStore Vault, StoreDefinition Store) target,
        RepositoryOrigin? origin,
        bool mirror,
        Func<EntryName, bool> overwrite,
        bool excludeRequired = false)
    {
        var group = StoreGroupPath.For(location);
        using var content = target.Vault.Read(group);
        ReportSkipped(host, content);
        var result = StoreSync.Save(body, content, target.Vault, group, overwrite, mirror, excludeRequired, new HelloContext(host.Hello, location.Path));
        var guidesChanged = StoreSync.MaintainGuides(target.Vault, location, origin);
        return new SavedToStore(location, result, target.Vault, target.Store, mirror, guidesChanged);
    }

    private static void Report(IVaultHost host, SavedToStore saved)
    {
        var label = RepositoryVaults.Label(saved.Location);
        var result = saved.Result;
        foreach (var skipped in result.Skipped)
        {
            host.Warn(skipped.State switch
            {
                StoreSyncState.ChangedInKeePass when saved.Personal => $"{label}: your value of '{skipped.Name}' not saved: it changed in personal store '{saved.Target.Name}' since your last save or load. Run lv load first.",
                StoreSyncState.RemovedInKeePass when saved.Personal => $"{label}: your value of '{skipped.Name}' not saved: it was removed from personal store '{saved.Target.Name}'. Run lv load, then lv save to put it back.",
                _ when saved.Personal => $"{label}: your value of '{skipped.Name}' not saved: it changed here and in personal store '{saved.Target.Name}'. Use --replace {skipped.Name} to overwrite it there, or lv load to take it.",
                StoreSyncState.OnlyInKeePass => $"{label}: '{skipped.Name}' exists only in mirror '{saved.Target.Name}'; left as it is.",
                StoreSyncState.ChangedInKeePass => $"{label}: '{skipped.Name}' not saved: it changed in the store since your last save or load. Run lv load first.",
                StoreSyncState.RemovedInKeePass => $"{label}: '{skipped.Name}' not saved: it was removed from the store. Run lv load to remove it here or keep it.",
                _ => $"{label}: '{skipped.Name}' not saved: it changed here and in the store. Use --replace {skipped.Name} to overwrite the store, or lv load to see theirs.",
            });
        }

        foreach (var name in result.Overwritten)
        {
            host.Warn($"{label}: '{name}' had been changed directly in mirror '{saved.Target.Name}'; the vault's value replaced it.");
        }

        foreach (var missing in result.HelloMissing)
        {
            host.Warn($"{label}: '{missing.Name}' not saved to '{saved.Target.Name}': {missing.Reason}. lv load restores it from the store.");
        }

        foreach (var refused in result.ReferenceRefused)
        {
            host.Warn($"{label}: {(saved.Personal ? "your value of " : string.Empty)}'{refused.Name}' not saved: it changed on this PC only. {ReferenceOrigin(refused.Reference, StoreNameOf(saved.Location, refused.Reference.StoreId))} {ReferenceAdvice(refused.Name, refused.Reference, saved.Personal)}");
        }

        if (saved.Personal)
        {
            host.Tell($"{label} -> personal store '{saved.Target.Name}', {StoreGroupPath.Display(StoreGroupPath.For(saved.Location))}: {result.Added} of your values added, {result.Updated} updated, {result.Deleted} removed, {result.Skipped.Count} not saved.");
            return;
        }

        var kind = saved.Mirror ? "mirror" : "store";
        host.Tell($"{label} -> {kind} '{saved.Target.Name}', {StoreGroupPath.Display(StoreGroupPath.For(saved.Location))}: {result.Added} added, {result.Updated} updated, {result.Deleted} removed, {result.Skipped.Count} not saved.");
    }

    public static (VaultSyncTotals Totals, IReadOnlyList<string> Unresolved) Load(
        IVaultHost host,
        StoreAccess access,
        IReadOnlyList<VaultLocation> vaults,
        Func<VaultLocation, EntryName, bool?> take,
        Func<VaultLocation, EntryName, bool> removeHere)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(vaults);
        ArgumentNullException.ThrowIfNull(take);
        int count = 0, reached = 0, added = 0, taken = 0, kept = 0, removed = 0;
        var unresolved = new List<string>();
        var undefined = new List<StoreNotDefinedException>();
        var personal = new PersonalStore(host, access);
        foreach (var location in vaults)
        {
            var primary = access.Resolve(location);
            if (OpenOrSkip(host, access, primary, vaults.Count, undefined) is not { } reachedStore)
            {
                continue;
            }

            var (keePass, target) = reachedStore;

            reached++;
            var store = RepositoryVaults.StoreFor(location);
            var snapshot = store.Read();
            var group = StoreGroupPath.For(location);
            var references = new ReferenceStores(access, location, keePass.DatabaseId);
            using var content = keePass.Read(group, references);
            ReportSkipped(host, content);
            ReportUnresolved(host, location, content);
            var comparisons = StoreSync.Compare(snapshot.Body, content);
            var answers = comparisons.Where(c => c.State == StoreSyncState.Conflict && content.Find(c.Name)?.Reference is null)
                .ToDictionary(c => c.Name, c => take(location, c.Name));
            var fromOtherStores = comparisons
                .Where(c => c.State is StoreSyncState.OnlyInKeePass or StoreSyncState.ChangedInKeePass or StoreSyncState.Conflict
                    || (c.State == StoreSyncState.Saved && StoreSync.ReferenceMoved(snapshot.Body, content.Find(c.Name))))
                .Select(c => (c.Name, Reference: content.Find(c.Name)?.Reference))
                .Where(r => r.Reference is not null && !string.Equals(r.Reference.StoreId, content.DatabaseId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var open = answers.Where(a => a.Value is null).Select(a => a.Key.FullName).ToList();
            if (open.Count > 0)
            {
                unresolved.AddRange(open.Select(name => $"{RepositoryVaults.Label(location)}: {name}"));
                continue;
            }

            var mayHaveRequired = snapshot.Body.Entries.Any(e => e.IsRequired) || content.Items.Any(i => i.Kind == VaultEntry.RequiredKind);
            var personalTarget = mayHaveRequired ? personal.Open(primary, keePass.DatabaseId) : null;
            using var personalContent = personalTarget?.Vault.Read(group, references);
            if (personalContent is not null && !personalTarget!.WithDeclaration)
            {
                ReportSkipped(host, personalContent);
                ReportUnresolved(host, location, personalContent);
            }

            var personalAnswers = personalContent is null
                ? new Dictionary<EntryName, bool>()
                : StoreSync.ComparePersonal(snapshot.Body, personalContent)
                    .Where(c => c.State == StoreSyncState.Conflict && StoreSync.PersonalValueOf(personalContent.Find(c.Name))?.Reference is null)
                    .ToDictionary(c => c.Name, c => take(location, c.Name) == true);

            var remove = comparisons.Where(c => c.State == StoreSyncState.RemovedInKeePass)
                .Where(c => removeHere(location, c.Name)).Select(c => c.Name).ToHashSet();
            var needsWrite = comparisons.Any(c =>
                    c.State is StoreSyncState.OnlyInKeePass or StoreSyncState.ChangedInKeePass or StoreSyncState.Conflict or StoreSyncState.RemovedInKeePass
                    || (c.State == StoreSyncState.Saved && snapshot.Body.Find(c.Name)?.KeePassBaseFor(content.DatabaseId) != c.VaultHash))
                || snapshot.Body.KeePassRemovalsFor(content.DatabaseId).Any(r => content.Find(r.Name) is null)
                || StoreSync.HelloLoadChanges(snapshot.Body, content, host.Hello)
                || comparisons.Any(c => c.State == StoreSyncState.Saved && StoreSync.ReferenceMoved(snapshot.Body, content.Find(c.Name)))
                || (personalContent is not null && (StoreSync.PersonalLoadChanges(snapshot.Body, personalContent, host.Hello)
                    || comparisons.Any(c => c.State == StoreSyncState.OnlyInKeePass && c.Kind == VaultEntry.RequiredKind)));
            var result = new StoreLoadResult(0, 0, 0, 0);
            var personalResult = new StoreLoadResult(0, 0, 0, 0);
            if (needsWrite)
            {
                if (!snapshot.Exists && content.Items.Count == 0)
                {
                    host.Tell($"{RepositoryVaults.Label(location)}: nothing in store '{target.Name}' for this vault yet.");
                    continue;
                }

                host.ReportWrite(store.Update(
                    body =>
                    {
                        result = StoreSync.Load(body, content, name => answers.TryGetValue(name, out var yes) && yes == true, remove.Contains, host.Clock, host.Hello);
                        if (personalContent is not null)
                        {
                            personalResult = StoreSync.LoadPersonal(body, personalContent, name => personalAnswers.TryGetValue(name, out var yes) && yes, host.Clock, host.Hello);
                        }
                    },
                    new VaultUpdateOptions { AllowCreate = true }));
            }

            if (needsWrite)
            {
                foreach (var (name, reference) in fromOtherStores)
                {
                    host.Tell($"{RepositoryVaults.Label(location)}: {name}: value taken from '{reference!.Title}' in store '{references.Find(reference.StoreId).Name}'.");
                }
            }

            count++;
            added += result.Added;
            taken += result.Taken;
            kept += result.Kept;
            removed += result.Removed;
            host.Tell($"{RepositoryVaults.Label(location)} <- store '{target.Name}': {result.Added} added, {result.Taken} updated, {result.Kept} kept as they were, {result.Removed} removed.");
            if (personalTarget is not null)
            {
                host.Tell($"{RepositoryVaults.Label(location)} <- personal store '{personalTarget.Store.Name}': {personalResult.Added} of your values filled in, {personalResult.Taken} updated, {personalResult.Kept} kept as they were.");
            }

            ReportHello(host, location, result, needsWrite ? null : snapshot.Body);
            ReportHello(host, location, personalResult, null);
        }

        ThrowIfNothingReached(reached, undefined);
        return (new VaultSyncTotals(count, added, taken, removed, kept), unresolved);
    }

    private static (IStore, StoreDefinition)? OpenOrSkip(
        IVaultHost host, StoreAccess access, StoreResolution resolution, int vaultCount, List<StoreNotDefinedException> undefined)
    {
        try
        {
            return access.For(resolution);
        }
        catch (StoreNotDefinedException ex) when (vaultCount > 1)
        {
            host.Warn($"Skipped: {ex.Message}");
            undefined.Add(ex);
            return null;
        }
    }

    private static void ThrowIfNothingReached(int reached, List<StoreNotDefinedException> undefined)
    {
        if (reached == 0 && undefined.Count > 0)
        {
            throw new StoreNotDefinedException(string.Join(" ", undefined.Select(ex => ex.Message).Distinct()));
        }
    }

    private static void ReportHello(IVaultHost host, VaultLocation location, StoreLoadResult result, VaultBody? unchanged)
    {
        var label = RepositoryVaults.Label(location);
        foreach (var skipped in result.NotSealed)
        {
            host.Warn($"{label}: '{skipped.Name}' not loaded: {skipped.Reason}. It needs Windows Hello on this PC.");
        }

        var stillMissing = unchanged is null
            ? result.StillMissing
            : unchanged.Entries.Where(e => e.IsHello && host.Hello.MissingReason(unchanged, e) is not null)
                .Select(e => new HelloSkip(e.Name, host.Hello.MissingReason(unchanged, e)!)).ToList();
        foreach (var missing in stillMissing)
        {
            host.Warn($"{label}: '{missing.Name}' can't be read on this PC ({missing.Reason}) and the store has no copy of it.");
        }
    }

    public static string StoreNameOf(VaultLocation location, string id)
    {
        var project = ProjectFile.ReadEntries(location).FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));
        return StoreResolver.Find(new StoreReference(project?.Name, id), location, new StoreRegistry(StoreRegistry.DefaultPath))?.Name
            ?? project?.Name
            ?? $"id {id}";
    }

    public static string ReferenceOrigin(ItemReference reference, string store) =>
        $"Its value comes from KeePass entry '{reference.Title}' (store '{store}'); lv save won't change that entry.";

    public static string ReferenceAdvice(EntryName name, ItemReference reference, bool personal) =>
        $"To change it for every project, edit '{reference.Title}' in KeePass. To use another source here: {(personal ? $"lv reset {name}" : $"lv set {name} --ref \"<group/title>\"")}";

    private static void ReportUnresolved(IVaultHost host, VaultLocation location, StoreContent content)
    {
        foreach (var unresolved in content.Unresolved)
        {
            host.Warn($"{RepositoryVaults.Label(location)}: {unresolved.Name} not loaded: {unresolved.Reason}{(unresolved.IsPersonal ? $" To pick another source: lv reset {unresolved.Name}" : string.Empty)}");
        }
    }

    private static void ReportSkipped(IVaultHost host, StoreContent content)
    {
        foreach (var title in content.Skipped)
        {
            host.Warn($"KeePass entry left out: {title}");
        }
    }
}
