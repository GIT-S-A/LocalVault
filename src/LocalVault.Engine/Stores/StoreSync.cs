namespace LocalVault.Engine;

public enum StoreSyncState
{
    Saved,
    New,
    Changed,
    ChangedInKeePass,
    Conflict,
    OnlyInKeePass,
    RemovedInKeePass,
    RemovalPending,
    Local,
}

public sealed record StoreComparison(EntryName Name, string Kind, StoreSyncState State, string? VaultHash, string? KeePassHash);

public sealed record StoreSaveResult(
    string DatabaseId,
    int Added,
    int Updated,
    int Deleted,
    IReadOnlyList<StoreComparison> Skipped,
    IReadOnlyList<(EntryName Name, string Hash)> Bases,
    IReadOnlyList<EntryName> DroppedRemovals)
{
    public IReadOnlyList<EntryName> Overwritten { get; init; } = [];

    public bool Personal { get; init; }

    public IReadOnlyList<HelloSkip> HelloMissing { get; init; } = [];

    public IReadOnlyList<ReferenceRefusal> ReferenceRefused { get; init; } = [];
}

public sealed record ReferenceRefusal(EntryName Name, ItemReference Reference);

public sealed record HelloSkip(EntryName Name, string Reason);

public sealed record StoreLoadResult(int Added, int Taken, int Kept, int Removed)
{
    public IReadOnlyList<HelloSkip> NotSealed { get; init; } = [];

    public IReadOnlyList<HelloSkip> StillMissing { get; init; } = [];
}

public sealed record StoreCopyResult(int Written, IReadOnlyList<HelloSkip> HelloMissing);

public sealed record HelloContext(HelloEntries Entries, string Vault);

public static class StoreSync
{
    public static string PersonalKey(string databaseId) => VaultEntry.PersonalBasePrefix + databaseId;

    public static IReadOnlyList<StoreComparison> Compare(VaultBody body, StoreContent keePass, bool excludeRequired = false)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(keePass);
        var result = new List<StoreComparison>();
        foreach (var entry in body.Entries.Where(entry => entry.IsSyncable && !(excludeRequired && entry.IsRequired) && !keePass.IsUnresolved(entry.Name, personal: false)))
        {
            var item = Shared(keePass.Find(entry.Name));
            if (entry.IsLocal && !entry.IsRequired)
            {
                result.Add(new StoreComparison(entry.Name, entry.Kind, StoreSyncState.Local, null, item?.Hash));
                continue;
            }

            var local = entry.SyncHash();
            var kind = entry.IsRequired ? VaultEntry.RequiredKind : entry.Kind;
            result.Add(new StoreComparison(entry.Name, kind, Classify(local, entry.KeePassBaseFor(keePass.DatabaseId), item?.Hash), local, item?.Hash));
        }

        var removals = body.KeePassRemovalsFor(keePass.DatabaseId);
        foreach (var item in keePass.Items.Where(item => body.Find(item.Name) is null && Shared(item) is not null))
        {
            var removal = removals.FirstOrDefault(r => r.Name.Equals(item.Name));
            var state = removal is null
                ? StoreSyncState.OnlyInKeePass
                : removal.Hash == item.Hash ? StoreSyncState.RemovalPending : StoreSyncState.ChangedInKeePass;
            result.Add(new StoreComparison(item.Name, item.Kind, state, null, item.Hash));
        }

        return result.OrderBy(c => c.Name.FullName, EntryName.Comparer).ToList();
    }

    public static IReadOnlyList<StoreComparison> ComparePersonal(VaultBody body, StoreContent keePass)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(keePass);
        var key = PersonalKey(keePass.DatabaseId);
        var result = new List<StoreComparison>();
        foreach (var entry in body.Entries.Where(entry => entry.IsRequired && !keePass.IsUnresolved(entry.Name, personal: true)))
        {
            var stored = PersonalValueOf(keePass.Find(entry.Name));
            if (entry.IsMissing)
            {
                if (stored is not null)
                {
                    result.Add(new StoreComparison(entry.Name, stored.Kind, StoreSyncState.OnlyInKeePass, null, stored.Hash));
                }

                continue;
            }

            var local = entry.ContentHash();
            result.Add(new StoreComparison(entry.Name, entry.Kind, Classify(local, entry.KeePassBaseFor(key), stored?.Hash), local, stored?.Hash));
        }

        foreach (var removal in PersonalRemovals(body, key).Where(r => body.Find(r.Name) is null))
        {
            if (PersonalValueOf(keePass.Find(removal.Name)) is { } stored)
            {
                var state = removal.Hash == stored.Hash ? StoreSyncState.RemovalPending : StoreSyncState.ChangedInKeePass;
                result.Add(new StoreComparison(removal.Name, stored.Kind, state, null, stored.Hash));
            }
        }

        return result.OrderBy(c => c.Name.FullName, EntryName.Comparer).ToList();
    }

    public static StoreItem? PersonalValueOf(StoreItem? item) => item?.Kind switch
    {
        VaultEntry.RequiredKind => item.PersonalValue,
        VaultEntry.SecretKind or VaultEntry.CredentialKind or VaultEntry.FileKind => item,
        _ => null,
    };

    public static bool ReferenceMoved(VaultBody body, StoreItem? stored)
    {
        ArgumentNullException.ThrowIfNull(body);
        return stored?.Reference is { } reference && body.Find(stored.Name) is { } entry
            && (!reference.SameTarget(entry.Reference) || entry.Reference!.Title != reference.Title);
    }

    private static StoreItem? Shared(StoreItem? item) => item is { IsUnderHome: true } && item is not { Ssh: not null, IsPersonal: false } ? null : item;

    private static IEnumerable<StoreRemoval> PersonalRemovals(VaultBody body, string key) =>
        body.KeePassRemovals.Where(r => string.Equals(r.Database, key, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<StoreComparison> ComparePending(VaultBody body, string? databaseId = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        var result = body.Entries
            .Where(entry => entry.IsSyncable)
            .Select(entry =>
            {
                if (entry.IsLocal && !entry.IsRequired)
                {
                    return new StoreComparison(entry.Name, entry.Kind, StoreSyncState.Local, null, null);
                }

                var local = entry.SyncHash();
                var baseHash = entry.KeePassBaseFor(databaseId);
                var state = baseHash is null
                    ? StoreSyncState.New
                    : baseHash == local ? StoreSyncState.Saved : StoreSyncState.Changed;
                return new StoreComparison(entry.Name, entry.Kind, state, local, baseHash);
            })
            .ToList();
        result.AddRange(body.KeePassRemovalsFor(databaseId).Select(r =>
            new StoreComparison(r.Name, string.Empty, StoreSyncState.RemovalPending, null, r.Hash)));
        return result;
    }

    public static StoreSaveResult Save(
        VaultBody body,
        StoreContent keePass,
        IStore target,
        IReadOnlyList<string> groupPath,
        Func<EntryName, bool> replaceConflict,
        bool mirror = false,
        bool excludeRequired = false,
        HelloContext? hello = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(replaceConflict);
        int added = 0, updated = 0, deleted = 0;
        var skipped = new List<StoreComparison>();
        var bases = new List<(EntryName, string)>();
        var dropped = new List<EntryName>();
        var overwritten = new List<EntryName>();
        var refused = new List<ReferenceRefusal>();
        var missingReferences = new List<HelloSkip>();
        var handled = new HashSet<EntryName>();
        foreach (var entry in body.Entries.Where(e => e.IsSyncable && !e.IsRequired && !e.IsLocal))
        {
            var link = keePass.LinkFor(entry.Name, personal: false);
            if (entry.Reference is null && link is null)
            {
                continue;
            }

            handled.Add(entry.Name);
            var local = entry.SyncHash();
            var comparison = new StoreComparison(entry.Name, entry.Kind, StoreSyncState.ChangedInKeePass, local, null);
            if (entry.Reference is { } reference && reference.SameTarget(link?.Reference))
            {
                if (mirror || entry.KeePassBaseFor(keePass.DatabaseId) == local)
                {
                    bases.Add((entry.Name, local));
                }
                else
                {
                    refused.Add(new ReferenceRefusal(entry.Name, reference));
                }
            }
            else if (entry.Reference is not null || mirror || replaceConflict(entry.Name))
            {
                var (plainValue, notReadable) = entry.Reference is null ? UnsealForStore(body, [entry], hello, "save to the store") : (new Dictionary<EntryName, VaultEntry>(), []);
                if (notReadable.Count > 0)
                {
                    missingReferences.AddRange(notReadable);
                    continue;
                }

                target.Put(groupPath, PlainOf(entry, plainValue), entry.IsHello);
                bases.Add((entry.Name, local));
                if (link is null && keePass.Find(entry.Name) is null)
                {
                    added++;
                }
                else
                {
                    updated++;
                }

                if (mirror && link is not null && entry.Reference?.SameTarget(link.Reference) != true)
                {
                    overwritten.Add(entry.Name);
                }
            }
            else
            {
                skipped.Add(comparison);
            }
        }

        foreach (var removal in body.KeePassRemovalsFor(keePass.DatabaseId).Where(r => body.Find(r.Name) is null && keePass.LinkFor(r.Name, personal: false) is not null))
        {
            handled.Add(removal.Name);
            target.Delete(groupPath, removal.Name);
            dropped.Add(removal.Name);
            deleted++;
        }

        var plan = new List<(StoreComparison Comparison, SaveStep Step)>();
        foreach (var comparison in Compare(body, keePass, excludeRequired).Where(c => !handled.Contains(c.Name)))
        {
            var exists = body.Find(comparison.Name) is not null;
            var step = mirror && comparison.State is StoreSyncState.Conflict or StoreSyncState.ChangedInKeePass or StoreSyncState.RemovedInKeePass
                ? exists ? SaveStep.Overwrite : SaveStep.OverwriteByDelete
                : comparison.State switch
                {
                    StoreSyncState.New => SaveStep.Add,
                    StoreSyncState.Changed => SaveStep.Update,
                    StoreSyncState.Conflict when replaceConflict(comparison.Name) => SaveStep.Update,
                    StoreSyncState.Saved => SaveStep.Keep,
                    StoreSyncState.RemovalPending => SaveStep.Delete,
                    StoreSyncState.Conflict or StoreSyncState.ChangedInKeePass or StoreSyncState.RemovedInKeePass => SaveStep.Skip,
                    StoreSyncState.OnlyInKeePass when mirror => SaveStep.Skip,
                    _ => SaveStep.None,
                };
            plan.Add((comparison, step));
        }

        var writes = plan.Where(p => p.Step is SaveStep.Add or SaveStep.Update or SaveStep.Overwrite)
            .Select(p => body.Find(p.Comparison.Name)!);
        var (plain, missing) = UnsealForStore(body, writes.Where(e => !e.IsRequired), hello, "save to the store");
        foreach (var (comparison, step) in plan)
        {
            var entry = body.Find(comparison.Name);
            if (entry is not null && missing.Any(m => m.Name.Equals(entry.Name)))
            {
                continue;
            }

            switch (step)
            {
                case SaveStep.Overwrite:
                    target.Put(groupPath, PlainOf(entry!, plain), entry!.IsHello);
                    bases.Add((comparison.Name, comparison.VaultHash!));
                    overwritten.Add(comparison.Name);
                    updated++;
                    break;
                case SaveStep.OverwriteByDelete:
                    target.Delete(groupPath, comparison.Name);
                    dropped.Add(comparison.Name);
                    overwritten.Add(comparison.Name);
                    updated++;
                    break;
                case SaveStep.Add:
                    target.Put(groupPath, PlainOf(entry!, plain), entry!.IsHello);
                    bases.Add((comparison.Name, comparison.VaultHash!));
                    added++;
                    break;
                case SaveStep.Update:
                    target.Put(groupPath, PlainOf(entry!, plain), entry!.IsHello);
                    bases.Add((comparison.Name, comparison.VaultHash!));
                    updated++;
                    break;
                case SaveStep.Keep:
                    bases.Add((comparison.Name, comparison.VaultHash!));
                    if (keePass.Find(comparison.Name) is { } item && FlagsDiffer(entry!, item))
                    {
                        target.SetHelloFlags(groupPath, comparison.Name, entry!.IsHello && !entry.IsRequired, entry.IsRequired && entry.IsRequiredHello);
                        updated++;
                    }

                    break;
                case SaveStep.Delete:
                    target.Delete(groupPath, comparison.Name);
                    dropped.Add(comparison.Name);
                    deleted++;
                    break;
                case SaveStep.Skip:
                    skipped.Add(comparison);
                    break;
                default:
                    break;
            }
        }

        dropped.AddRange(body.KeePassRemovalsFor(keePass.DatabaseId)
            .Where(r => keePass.Find(r.Name) is null && keePass.LinkFor(r.Name, personal: false) is null)
            .Select(r => r.Name));
        return new StoreSaveResult(keePass.DatabaseId, added, updated, deleted, skipped, bases, dropped)
        {
            Overwritten = overwritten,
            HelloMissing = [.. missing, .. missingReferences],
            ReferenceRefused = refused,
        };
    }

    private enum SaveStep
    {
        None,
        Add,
        Update,
        Keep,
        Delete,
        Skip,
        Overwrite,
        OverwriteByDelete,
    }

    private static bool FlagsDiffer(VaultEntry entry, StoreItem item) => entry.IsRequired
        ? item.Kind == VaultEntry.RequiredKind && item.IsRequiredHello != entry.IsRequiredHello
        : item.IsHello != entry.IsHello;

    private static VaultEntry PlainOf(VaultEntry entry, IReadOnlyDictionary<EntryName, VaultEntry> plain) =>
        plain.TryGetValue(entry.Name, out var unsealed) ? unsealed : entry;

    private static (IReadOnlyDictionary<EntryName, VaultEntry> Plain, IReadOnlyList<HelloSkip> Missing) UnsealForStore(
        VaultBody body, IEnumerable<VaultEntry> entries, HelloContext? hello, string action)
    {
        var sealedEntries = entries.Where(e => e.IsHello).ToList();
        if (sealedEntries.Count == 0)
        {
            return (new Dictionary<EntryName, VaultEntry>(), []);
        }

        var missing = sealedEntries
            .Select(e => new HelloSkip(e.Name, hello is null ? "Windows Hello is not available" : hello.Entries.MissingReason(body, e) ?? string.Empty))
            .Where(m => m.Reason.Length > 0)
            .ToList();
        var readable = sealedEntries.Where(e => !missing.Exists(m => m.Name.Equals(e.Name))).ToList();
        return readable.Count == 0
            ? (new Dictionary<EntryName, VaultEntry>(), missing)
            : (hello!.Entries.Unseal(body, readable, action, hello.Vault), missing);
    }

    public static StoreSaveResult SavePersonal(
        VaultBody body,
        StoreContent keePass,
        IStore target,
        IReadOnlyList<string> groupPath,
        bool withDeclaration,
        Func<EntryName, bool> replaceConflict,
        HelloContext? hello = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(keePass);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(replaceConflict);
        var key = PersonalKey(keePass.DatabaseId);
        int added = 0, updated = 0, deleted = 0;
        var skipped = new List<StoreComparison>();
        var bases = new List<(EntryName, string)>();
        var dropped = new List<EntryName>();
        var refused = new List<ReferenceRefusal>();
        var comparisons = ComparePersonal(body, keePass)
            .Select(c => (Comparison: c, Write: body.Find(c.Name)?.Reference is null
                && (keePass.LinkFor(c.Name, personal: true) is null
                    ? c.State is StoreSyncState.New or StoreSyncState.Changed || (c.State == StoreSyncState.Conflict && replaceConflict(c.Name))
                    : body.Find(c.Name) is { IsMissing: false } && replaceConflict(c.Name))))
            .ToList();
        var (plain, missing) = UnsealForStore(
            body, comparisons.Where(c => c.Write).Select(c => body.Find(c.Comparison.Name)!), hello, "save your value of");
        foreach (var (comparison, write) in comparisons)
        {
            if (missing.Any(m => m.Name.Equals(comparison.Name)))
            {
                continue;
            }

            var entry = body.Find(comparison.Name);
            var link = keePass.LinkFor(comparison.Name, personal: true);
            if (entry?.Reference is { } reference)
            {
                if (!reference.SameTarget(link?.Reference))
                {
                    target.PutPersonalValue(groupPath, entry, withDeclaration, entry.IsHello);
                    bases.Add((comparison.Name, comparison.VaultHash!));
                    if (link is null && PersonalValueOf(keePass.Find(comparison.Name)) is null)
                    {
                        added++;
                    }
                    else
                    {
                        updated++;
                    }
                }
                else if (entry.KeePassBaseFor(key) == comparison.VaultHash)
                {
                    bases.Add((comparison.Name, comparison.VaultHash!));
                }
                else
                {
                    refused.Add(new ReferenceRefusal(comparison.Name, reference));
                }

                continue;
            }

            if (link is not null && entry is { IsMissing: false })
            {
                if (write)
                {
                    target.PutPersonalValue(groupPath, PlainOf(entry, plain), withDeclaration, entry.IsHello);
                    bases.Add((comparison.Name, comparison.VaultHash!));
                    updated++;
                }
                else
                {
                    skipped.Add(comparison with { State = StoreSyncState.ChangedInKeePass });
                }

                continue;
            }

            switch (comparison.State)
            {
                case StoreSyncState.New:
                    target.PutPersonalValue(groupPath, PlainOf(entry!, plain), withDeclaration, entry!.IsHello);
                    bases.Add((comparison.Name, comparison.VaultHash!));
                    added++;
                    break;
                case StoreSyncState.Changed:
                case StoreSyncState.Conflict when write:
                    target.PutPersonalValue(groupPath, PlainOf(entry!, plain), withDeclaration, entry!.IsHello);
                    bases.Add((comparison.Name, comparison.VaultHash!));
                    updated++;
                    break;
                case StoreSyncState.Saved:
                    bases.Add((comparison.Name, comparison.VaultHash!));
                    if (PersonalValueOf(keePass.Find(comparison.Name)) is { } stored && stored.IsHello != entry!.IsHello)
                    {
                        target.SetHelloFlags(groupPath, comparison.Name, entry.IsHello, withDeclaration && entry.IsRequiredHello);
                        updated++;
                    }

                    break;
                case StoreSyncState.RemovalPending:
                    if (!withDeclaration)
                    {
                        target.Delete(groupPath, comparison.Name);
                        deleted++;
                    }

                    dropped.Add(comparison.Name);
                    break;
                case StoreSyncState.Conflict:
                case StoreSyncState.ChangedInKeePass:
                case StoreSyncState.RemovedInKeePass:
                    skipped.Add(comparison);
                    break;
                default:
                    break;
            }
        }

        foreach (var removal in PersonalRemovals(body, key).Where(r => body.Find(r.Name) is null && keePass.LinkFor(r.Name, personal: true) is not null).ToList())
        {
            if (!withDeclaration)
            {
                target.Delete(groupPath, removal.Name);
                deleted++;
            }

            dropped.Add(removal.Name);
        }

        dropped.AddRange(PersonalRemovals(body, key)
            .Where(r => PersonalValueOf(keePass.Find(r.Name)) is null && keePass.LinkFor(r.Name, personal: true) is null)
            .Select(r => r.Name));
        return new StoreSaveResult(key, added, updated, deleted, skipped, bases, dropped)
        {
            Personal = true,
            HelloMissing = missing,
            ReferenceRefused = refused,
        };
    }

    public static bool MaintainGuides(IStore target, VaultLocation location, RepositoryOrigin? origin)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(location);
        var changed = target.EnsureInstallInstructions();
        if (origin is not null && location.ProjectStoreGroup is { } projectGroup)
        {
            changed |= target.MaintainOrigin(projectGroup, origin);
        }

        return changed;
    }

    public static StoreCopyResult Copy(VaultBody body, IStore target, IReadOnlyList<string> groupPath, HelloContext? hello = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(target);
        var entries = body.Entries.Where(entry => entry.IsSyncable && (!entry.IsLocal || entry.IsRequired)).ToList();
        var (plain, missing) = UnsealForStore(body, entries.Where(e => !e.IsRequired), hello, "copy to the store");
        var written = 0;
        foreach (var entry in entries.Where(e => !missing.Any(m => m.Name.Equals(e.Name))))
        {
            target.Put(groupPath, PlainOf(entry, plain), entry.IsHello);
            written++;
        }

        return new StoreCopyResult(written, missing);
    }

    public static void RecordSaved(VaultBody body, StoreSaveResult result, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(result);
        foreach (var (name, hash) in result.Bases)
        {
            if (body.Find(name) is { } entry && (result.Personal ? !entry.IsMissing && entry.ContentHash() == hash : entry.SyncHash() == hash))
            {
                body.RecordKeePassBase(name, result.DatabaseId, hash, now);
            }
        }

        foreach (var name in result.DroppedRemovals)
        {
            if (body.Find(name) is null)
            {
                body.TakeRemovals(name, result.DatabaseId);
            }
        }
    }

    public static StoreLoadResult Load(
        VaultBody body,
        StoreContent keePass,
        Func<EntryName, bool> takeConflict,
        Func<EntryName, bool> removeHere,
        DateTimeOffset now,
        HelloEntries? hello = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(keePass);
        ArgumentNullException.ThrowIfNull(takeConflict);
        ArgumentNullException.ThrowIfNull(removeHere);
        int added = 0, taken = 0, kept = 0, removed = 0;
        var notSealed = new List<HelloSkip>();
        var missing = MissingHello(body, hello, personal: false);
        foreach (var comparison in Compare(body, keePass))
        {
            var item = keePass.Find(comparison.Name);
            if (comparison.State != StoreSyncState.Local && Shared(item) is not null && missing.Remove(comparison.Name))
            {
                if (Take(body, item!, keePass.DatabaseId, now, hello, notSealed))
                {
                    taken++;
                }

                continue;
            }

            switch (comparison.State)
            {
                case StoreSyncState.OnlyInKeePass:
                    if (Take(body, item!, keePass.DatabaseId, now, hello, notSealed))
                    {
                        added++;
                    }

                    break;
                case StoreSyncState.ChangedInKeePass:
                    var existed = body.Find(comparison.Name) is not null;
                    if (Take(body, item!, keePass.DatabaseId, now, hello, notSealed))
                    {
                        if (existed)
                        {
                            taken++;
                        }
                        else
                        {
                            added++;
                        }
                    }

                    break;
                case StoreSyncState.Conflict when item?.Reference is not null || takeConflict(comparison.Name):
                case StoreSyncState.Saved when ReferenceMoved(body, item):
                    if (Take(body, item!, keePass.DatabaseId, now, hello, notSealed))
                    {
                        taken++;
                    }

                    break;
                case StoreSyncState.Conflict:
                    kept++;
                    break;
                case StoreSyncState.Saved:
                    body.RecordKeePassBase(comparison.Name, keePass.DatabaseId, comparison.VaultHash, now);
                    break;
                case StoreSyncState.RemovedInKeePass when removeHere(comparison.Name):
                    body.Remove(comparison.Name, recordForKeePass: false);
                    removed++;
                    break;
                case StoreSyncState.RemovedInKeePass:
                    body.RecordKeePassBase(comparison.Name, keePass.DatabaseId, null, now);
                    kept++;
                    break;
                default:
                    break;
            }
        }

        foreach (var removal in body.KeePassRemovalsFor(keePass.DatabaseId).Where(r => keePass.Find(r.Name) is null))
        {
            body.TakeRemovals(removal.Name, keePass.DatabaseId);
        }

        return new StoreLoadResult(added, taken, kept, removed) { NotSealed = notSealed, StillMissing = StillMissing(body, missing, hello) };
    }

    public static StoreLoadResult LoadPersonal(
        VaultBody body,
        StoreContent keePass,
        Func<EntryName, bool> takeConflict,
        DateTimeOffset now,
        HelloEntries? hello = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(keePass);
        ArgumentNullException.ThrowIfNull(takeConflict);
        var key = PersonalKey(keePass.DatabaseId);
        int added = 0, taken = 0, kept = 0;
        var notSealed = new List<HelloSkip>();
        var missing = MissingHello(body, hello, personal: true);
        foreach (var comparison in ComparePersonal(body, keePass))
        {
            var stored = PersonalValueOf(keePass.Find(comparison.Name));
            if (stored is not null && missing.Remove(comparison.Name))
            {
                if (TakePersonal(body, stored, key, now, hello, notSealed))
                {
                    taken++;
                }

                continue;
            }

            switch (comparison.State)
            {
                case StoreSyncState.OnlyInKeePass:
                    if (TakePersonal(body, stored!, key, now, hello, notSealed))
                    {
                        added++;
                    }

                    break;
                case StoreSyncState.ChangedInKeePass when body.Find(comparison.Name) is not null:
                case StoreSyncState.Conflict when stored?.Reference is not null || takeConflict(comparison.Name):
                case StoreSyncState.Saved when ReferenceMoved(body, stored):
                    if (TakePersonal(body, stored!, key, now, hello, notSealed))
                    {
                        taken++;
                    }

                    break;
                case StoreSyncState.Conflict:
                    kept++;
                    break;
                case StoreSyncState.Saved:
                    body.RecordKeePassBase(comparison.Name, key, comparison.VaultHash, now);
                    break;
                case StoreSyncState.RemovedInKeePass:
                    body.RecordKeePassBase(comparison.Name, key, null, now);
                    kept++;
                    break;
                default:
                    break;
            }
        }

        foreach (var removal in PersonalRemovals(body, key).Where(r => PersonalValueOf(keePass.Find(r.Name)) is null).ToList())
        {
            body.TakeRemovals(removal.Name, key);
        }

        return new StoreLoadResult(added, taken, kept, 0) { NotSealed = notSealed, StillMissing = StillMissing(body, missing, hello) };
    }

    public static bool PersonalLoadChanges(VaultBody body, StoreContent keePass, HelloEntries? hello = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(keePass);
        var key = PersonalKey(keePass.DatabaseId);
        return ComparePersonal(body, keePass).Any(c =>
                c.State is StoreSyncState.OnlyInKeePass or StoreSyncState.ChangedInKeePass or StoreSyncState.Conflict or StoreSyncState.RemovedInKeePass
                || (c.State == StoreSyncState.Saved
                    && (body.Find(c.Name)?.KeePassBaseFor(key) != c.VaultHash || ReferenceMoved(body, PersonalValueOf(keePass.Find(c.Name))))))
            || PersonalRemovals(body, key).Any(r => PersonalValueOf(keePass.Find(r.Name)) is null)
            || MissingHello(body, hello, personal: true).Any(name => PersonalValueOf(keePass.Find(name)) is not null);
    }

    public static bool HelloLoadChanges(VaultBody body, StoreContent keePass, HelloEntries? hello)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(keePass);
        return MissingHello(body, hello, personal: false).Any(name => body.Find(name)?.IsLocal != true && Shared(keePass.Find(name)) is not null);
    }

    private static HashSet<EntryName> MissingHello(VaultBody body, HelloEntries? hello, bool personal) => hello is null
        ? []
        : body.Entries
            .Where(e => e.IsHello && e.IsRequired == personal && hello.MissingReason(body, e) is not null)
            .Select(e => e.Name)
            .ToHashSet();

    private static List<HelloSkip> StillMissing(VaultBody body, HashSet<EntryName> notInStore, HelloEntries? hello) =>
        hello is null
            ? []
            : notInStore
                .Select(body.Find)
                .OfType<VaultEntry>()
                .Select(e => new HelloSkip(e.Name, hello.MissingReason(body, e) ?? string.Empty))
                .Where(s => s.Reason.Length > 0)
                .OrderBy(s => s.Name.FullName, EntryName.Comparer)
                .ToList();

    private static bool TakePersonal(VaultBody body, StoreItem stored, string key, DateTimeOffset now, HelloEntries? hello, List<HelloSkip> notSealed)
    {
        var existing = body.Find(stored.Name);
        var value = stored.ToVaultEntry(now);
        if (value.IsSealable && (stored.IsHello || existing?.IsHello == true || existing?.IsRequiredHello == true))
        {
            if (SealOrSkip(body, value, hello, notSealed) is not { } sealedValue)
            {
                return false;
            }

            value = sealedValue;
        }

        body.SetPersonalValue(stored.Name, value);
        body.RecordKeePassBase(stored.Name, key, stored.Hash, now);
        return true;
    }

    private static bool Take(VaultBody body, StoreItem item, string databaseId, DateTimeOffset now, HelloEntries? hello, List<HelloSkip> notSealed)
    {
        var existing = body.Find(item.Name);
        if (item.Kind == VaultEntry.RequiredKind && existing is not null && !existing.IsMissing)
        {
            body.Set(item.ToVaultEntry(now));
            body.RecordKeePassBase(existing.Name, databaseId, item.Hash, now);
            return true;
        }

        var entry = item.ToVaultEntry(now);
        if (entry.IsSealable && (item.IsHello || existing?.IsHello == true))
        {
            if (SealOrSkip(body, entry, hello, notSealed) is not { } sealedEntry)
            {
                return false;
            }

            entry = sealedEntry;
        }

        if (existing is not null && !existing.Name.FullName.Equals(item.Name.FullName, StringComparison.Ordinal))
        {
            body.Remove(existing.Name, recordForKeePass: false);
        }

        body.Set(entry);
        body.RecordKeePassBase(item.Name, databaseId, item.Hash, now);
        return true;
    }

    private static VaultEntry? SealOrSkip(VaultBody body, VaultEntry plain, HelloEntries? hello, List<HelloSkip> notSealed)
    {
        if (hello is null)
        {
            notSealed.Add(new HelloSkip(plain.Name, "Windows Hello is not available"));
            return null;
        }

        try
        {
            return hello.Seal(body, plain);
        }
        catch (HelloException ex)
        {
            notSealed.Add(new HelloSkip(plain.Name, ex.Message.TrimEnd('.')));
            return null;
        }
    }

    private static StoreSyncState Classify(string local, string? baseHash, string? keePass)
    {
        if (keePass is null)
        {
            return baseHash is null ? StoreSyncState.New : StoreSyncState.RemovedInKeePass;
        }

        if (keePass == local)
        {
            return StoreSyncState.Saved;
        }

        if (baseHash == keePass)
        {
            return StoreSyncState.Changed;
        }

        return baseHash == local ? StoreSyncState.ChangedInKeePass : StoreSyncState.Conflict;
    }
}
