using LocalVault.Core;
using LocalVault.Engine;

namespace LocalVault.PowerShell;

internal sealed record VaultSyncTotals(int Vaults, int Added, int Updated, int Removed, int Kept);

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

        var group = location.StoreGroup;
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
            known[gitRoot] = origin = location.ReadOrigin();
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
        var group = location.StoreGroup;
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
            host.Tell($"{label} -> personal store '{saved.Target.Name}', {StoreGroupPath.Display(saved.Location.StoreGroup)}: {result.Added} of your values added, {result.Updated} updated, {result.Deleted} removed, {result.Skipped.Count} not saved.");
            return;
        }

        var kind = saved.Mirror ? "mirror" : "store";
        host.Tell($"{label} -> {kind} '{saved.Target.Name}', {StoreGroupPath.Display(saved.Location.StoreGroup)}: {result.Added} added, {result.Updated} updated, {result.Deleted} removed, {result.Skipped.Count} not saved.");
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
            var group = location.StoreGroup;
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
