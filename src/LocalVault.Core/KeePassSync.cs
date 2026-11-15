namespace LocalVault.Core;

public enum KeePassSyncState
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

public sealed record KeePassComparison(EntryName Name, string Kind, KeePassSyncState State, string? VaultHash, string? KeePassHash);

public sealed record KeePassSaveResult(
    string DatabaseId,
    int Added,
    int Updated,
    int Deleted,
    IReadOnlyList<KeePassComparison> Skipped,
    IReadOnlyList<(EntryName Name, string Hash)> Bases,
    IReadOnlyList<EntryName> DroppedRemovals)
{
    public IReadOnlyList<EntryName> Overwritten { get; init; } = [];

    public bool Personal { get; init; }

    public IReadOnlyList<HelloSkip> HelloMissing { get; init; } = [];

    public IReadOnlyList<ReferenceRefusal> ReferenceRefused { get; init; } = [];
}

public sealed record ReferenceRefusal(EntryName Name, KeePassReference Reference);

public sealed record HelloSkip(EntryName Name, string Reason);

public sealed record KeePassLoadResult(int Added, int Taken, int Kept, int Removed)
{
    public IReadOnlyList<HelloSkip> NotSealed { get; init; } = [];

    public IReadOnlyList<HelloSkip> StillMissing { get; init; } = [];
}

public sealed record KeePassCopyResult(int Written, IReadOnlyList<HelloSkip> HelloMissing);

public sealed record HelloContext(HelloEntries Entries, string Vault);

public static class KeePassSync
{
    public static string PersonalKey(string databaseId) => VaultEntry.PersonalBasePrefix + databaseId;

    public static IReadOnlyList<KeePassComparison> Compare(VaultBody body, KeePassGroupContent keePass, bool excludeRequired = false)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(keePass);
        var result = new List<KeePassComparison>();
        foreach (var entry in body.Entries.Where(entry => entry.IsSyncable && !(excludeRequired && entry.IsRequired) && !keePass.IsUnresolved(entry.Name, personal: false)))
        {
            var item = Shared(keePass.Find(entry.Name));
            if (entry.IsLocal && !entry.IsRequired)
            {
                result.Add(new KeePassComparison(entry.Name, entry.Kind, KeePassSyncState.Local, null, item?.Hash));
                continue;
            }

            var local = entry.SyncHash();
            var kind = entry.IsRequired ? VaultEntry.RequiredKind : entry.Kind;
            result.Add(new KeePassComparison(entry.Name, kind, Classify(local, entry.KeePassBaseFor(keePass.DatabaseId), item?.Hash), local, item?.Hash));
        }

        var removals = body.KeePassRemovalsFor(keePass.DatabaseId);
        foreach (var item in keePass.Items.Where(item => body.Find(item.Name) is null && Shared(item) is not null))
        {
            var removal = removals.FirstOrDefault(r => r.Name.Equals(item.Name));
            var state = removal is null
                ? KeePassSyncState.OnlyInKeePass
                : removal.Hash == item.Hash ? KeePassSyncState.RemovalPending : KeePassSyncState.ChangedInKeePass;
            result.Add(new KeePassComparison(item.Name, item.Kind, state, null, item.Hash));
        }

        return result.OrderBy(c => c.Name.FullName, EntryName.Comparer).ToList();
    }

    public static IReadOnlyList<KeePassComparison> ComparePersonal(VaultBody body, KeePassGroupContent keePass)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(keePass);
        var key = PersonalKey(keePass.DatabaseId);
        var result = new List<KeePassComparison>();
        foreach (var entry in body.Entries.Where(entry => entry.IsRequired && !keePass.IsUnresolved(entry.Name, personal: true)))
        {
            var stored = PersonalValueOf(keePass.Find(entry.Name));
            if (entry.IsMissing)
            {
                if (stored is not null)
                {
                    result.Add(new KeePassComparison(entry.Name, stored.Kind, KeePassSyncState.OnlyInKeePass, null, stored.Hash));
                }

                continue;
            }

            var local = entry.ContentHash();
            result.Add(new KeePassComparison(entry.Name, entry.Kind, Classify(local, entry.KeePassBaseFor(key), stored?.Hash), local, stored?.Hash));
        }

        foreach (var removal in PersonalRemovals(body, key).Where(r => body.Find(r.Name) is null))
        {
            if (PersonalValueOf(keePass.Find(removal.Name)) is { } stored)
            {
                var state = removal.Hash == stored.Hash ? KeePassSyncState.RemovalPending : KeePassSyncState.ChangedInKeePass;
                result.Add(new KeePassComparison(removal.Name, stored.Kind, state, null, stored.Hash));
            }
        }

        return result.OrderBy(c => c.Name.FullName, EntryName.Comparer).ToList();
    }

    public static KeePassItem? PersonalValueOf(KeePassItem? item) => item?.Kind switch
    {
        VaultEntry.RequiredKind => item.PersonalValue,
        VaultEntry.SecretKind or VaultEntry.CredentialKind or VaultEntry.FileKind => item,
        _ => null,
    };

    public static bool ReferenceMoved(VaultBody body, KeePassItem? stored)
    {
        ArgumentNullException.ThrowIfNull(body);
        return stored?.Reference is { } reference && body.Find(stored.Name) is { } entry
            && (!reference.SameTarget(entry.Reference) || entry.Reference!.Title != reference.Title);
    }

    private static KeePassItem? Shared(KeePassItem? item) => item is { IsUnderHome: true } && item is not { Ssh: not null, IsPersonal: false } ? null : item;

    private static IEnumerable<KeePassRemoval> PersonalRemovals(VaultBody body, string key) =>
        body.KeePassRemovals.Where(r => string.Equals(r.Database, key, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<KeePassComparison> ComparePending(VaultBody body, string? databaseId = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        var result = body.Entries
            .Where(entry => entry.IsSyncable)
            .Select(entry =>
            {
                if (entry.IsLocal && !entry.IsRequired)
                {
                    return new KeePassComparison(entry.Name, entry.Kind, KeePassSyncState.Local, null, null);
                }

                var local = entry.SyncHash();
                var baseHash = entry.KeePassBaseFor(databaseId);
                var state = baseHash is null
                    ? KeePassSyncState.New
                    : baseHash == local ? KeePassSyncState.Saved : KeePassSyncState.Changed;
                return new KeePassComparison(entry.Name, entry.Kind, state, local, baseHash);
            })
            .ToList();
        result.AddRange(body.KeePassRemovalsFor(databaseId).Select(r =>
            new KeePassComparison(r.Name, string.Empty, KeePassSyncState.RemovalPending, null, r.Hash)));
        return result;
    }

    public static KeePassSaveResult Save(
        VaultBody body,
        KeePassGroupContent keePass,
        KeePassVault target,
        IReadOnlyList<string> groupPath,
        Func<EntryName, bool> replaceConflict,
        bool mirror = false,
        bool excludeRequired = false,
        HelloContext? hello = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(replaceConflict);
        int added = 0, updated = 0, deleted = 0;
        var skipped = new List<KeePassComparison>();
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
            var comparison = new KeePassComparison(entry.Name, entry.Kind, KeePassSyncState.ChangedInKeePass, local, null);
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

        var plan = new List<(KeePassComparison Comparison, SaveStep Step)>();
        foreach (var comparison in Compare(body, keePass, excludeRequired).Where(c => !handled.Contains(c.Name)))
        {
            var exists = body.Find(comparison.Name) is not null;
            var step = mirror && comparison.State is KeePassSyncState.Conflict or KeePassSyncState.ChangedInKeePass or KeePassSyncState.RemovedInKeePass
                ? exists ? SaveStep.Overwrite : SaveStep.OverwriteByDelete
                : comparison.State switch
                {
                    KeePassSyncState.New => SaveStep.Add,
                    KeePassSyncState.Changed => SaveStep.Update,
                    KeePassSyncState.Conflict when replaceConflict(comparison.Name) => SaveStep.Update,
                    KeePassSyncState.Saved => SaveStep.Keep,
                    KeePassSyncState.RemovalPending => SaveStep.Delete,
                    KeePassSyncState.Conflict or KeePassSyncState.ChangedInKeePass or KeePassSyncState.RemovedInKeePass => SaveStep.Skip,
                    KeePassSyncState.OnlyInKeePass when mirror => SaveStep.Skip,
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
        return new KeePassSaveResult(keePass.DatabaseId, added, updated, deleted, skipped, bases, dropped)
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

    private static bool FlagsDiffer(VaultEntry entry, KeePassItem item) => entry.IsRequired
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

    public static KeePassSaveResult SavePersonal(
        VaultBody body,
        KeePassGroupContent keePass,
        KeePassVault target,
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
        var skipped = new List<KeePassComparison>();
        var bases = new List<(EntryName, string)>();
        var dropped = new List<EntryName>();
        var refused = new List<ReferenceRefusal>();
        var comparisons = ComparePersonal(body, keePass)
            .Select(c => (Comparison: c, Write: body.Find(c.Name)?.Reference is null
                && (keePass.LinkFor(c.Name, personal: true) is null
                    ? c.State is KeePassSyncState.New or KeePassSyncState.Changed || (c.State == KeePassSyncState.Conflict && replaceConflict(c.Name))
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
                    skipped.Add(comparison with { State = KeePassSyncState.ChangedInKeePass });
                }

                continue;
            }

            switch (comparison.State)
            {
                case KeePassSyncState.New:
                    target.PutPersonalValue(groupPath, PlainOf(entry!, plain), withDeclaration, entry!.IsHello);
                    bases.Add((comparison.Name, comparison.VaultHash!));
                    added++;
                    break;
                case KeePassSyncState.Changed:
                case KeePassSyncState.Conflict when write:
                    target.PutPersonalValue(groupPath, PlainOf(entry!, plain), withDeclaration, entry!.IsHello);
                    bases.Add((comparison.Name, comparison.VaultHash!));
                    updated++;
                    break;
                case KeePassSyncState.Saved:
                    bases.Add((comparison.Name, comparison.VaultHash!));
                    if (PersonalValueOf(keePass.Find(comparison.Name)) is { } stored && stored.IsHello != entry!.IsHello)
                    {
                        target.SetHelloFlags(groupPath, comparison.Name, entry.IsHello, withDeclaration && entry.IsRequiredHello);
                        updated++;
                    }

                    break;
                case KeePassSyncState.RemovalPending:
                    if (!withDeclaration)
                    {
                        target.Delete(groupPath, comparison.Name);
                        deleted++;
                    }

                    dropped.Add(comparison.Name);
                    break;
                case KeePassSyncState.Conflict:
                case KeePassSyncState.ChangedInKeePass:
                case KeePassSyncState.RemovedInKeePass:
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
        return new KeePassSaveResult(key, added, updated, deleted, skipped, bases, dropped)
        {
            Personal = true,
            HelloMissing = missing,
            ReferenceRefused = refused,
        };
    }

    public static bool MaintainGuides(KeePassVault target, VaultLocation location, RepositoryOrigin? origin)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(location);
        var changed = target.EnsureInstallInstructions();
        if (origin is not null && KeePassGroupPath.ProjectOf(location) is { } projectGroup)
        {
            changed |= target.MaintainOrigin(projectGroup, origin);
        }

        return changed;
    }

    public static KeePassCopyResult Copy(VaultBody body, KeePassVault target, IReadOnlyList<string> groupPath, HelloContext? hello = null)
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

        return new KeePassCopyResult(written, missing);
    }

    public static void RecordSaved(VaultBody body, KeePassSaveResult result, DateTimeOffset now)
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

    public static KeePassLoadResult Load(
        VaultBody body,
        KeePassGroupContent keePass,
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
            if (comparison.State != KeePassSyncState.Local && Shared(item) is not null && missing.Remove(comparison.Name))
            {
                if (Take(body, item!, keePass.DatabaseId, now, hello, notSealed))
                {
                    taken++;
                }

                continue;
            }

            switch (comparison.State)
            {
                case KeePassSyncState.OnlyInKeePass:
                    if (Take(body, item!, keePass.DatabaseId, now, hello, notSealed))
                    {
                        added++;
                    }

                    break;
                case KeePassSyncState.ChangedInKeePass:
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
                case KeePassSyncState.Conflict when item?.Reference is not null || takeConflict(comparison.Name):
                case KeePassSyncState.Saved when ReferenceMoved(body, item):
                    if (Take(body, item!, keePass.DatabaseId, now, hello, notSealed))
                    {
                        taken++;
                    }

                    break;
                case KeePassSyncState.Conflict:
                    kept++;
                    break;
                case KeePassSyncState.Saved:
                    body.RecordKeePassBase(comparison.Name, keePass.DatabaseId, comparison.VaultHash, now);
                    break;
                case KeePassSyncState.RemovedInKeePass when removeHere(comparison.Name):
                    body.Remove(comparison.Name, recordForKeePass: false);
                    removed++;
                    break;
                case KeePassSyncState.RemovedInKeePass:
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

        return new KeePassLoadResult(added, taken, kept, removed) { NotSealed = notSealed, StillMissing = StillMissing(body, missing, hello) };
    }

    public static KeePassLoadResult LoadPersonal(
        VaultBody body,
        KeePassGroupContent keePass,
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
                case KeePassSyncState.OnlyInKeePass:
                    if (TakePersonal(body, stored!, key, now, hello, notSealed))
                    {
                        added++;
                    }

                    break;
                case KeePassSyncState.ChangedInKeePass when body.Find(comparison.Name) is not null:
                case KeePassSyncState.Conflict when stored?.Reference is not null || takeConflict(comparison.Name):
                case KeePassSyncState.Saved when ReferenceMoved(body, stored):
                    if (TakePersonal(body, stored!, key, now, hello, notSealed))
                    {
                        taken++;
                    }

                    break;
                case KeePassSyncState.Conflict:
                    kept++;
                    break;
                case KeePassSyncState.Saved:
                    body.RecordKeePassBase(comparison.Name, key, comparison.VaultHash, now);
                    break;
                case KeePassSyncState.RemovedInKeePass:
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

        return new KeePassLoadResult(added, taken, kept, 0) { NotSealed = notSealed, StillMissing = StillMissing(body, missing, hello) };
    }

    public static bool PersonalLoadChanges(VaultBody body, KeePassGroupContent keePass, HelloEntries? hello = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(keePass);
        var key = PersonalKey(keePass.DatabaseId);
        return ComparePersonal(body, keePass).Any(c =>
                c.State is KeePassSyncState.OnlyInKeePass or KeePassSyncState.ChangedInKeePass or KeePassSyncState.Conflict or KeePassSyncState.RemovedInKeePass
                || (c.State == KeePassSyncState.Saved
                    && (body.Find(c.Name)?.KeePassBaseFor(key) != c.VaultHash || ReferenceMoved(body, PersonalValueOf(keePass.Find(c.Name))))))
            || PersonalRemovals(body, key).Any(r => PersonalValueOf(keePass.Find(r.Name)) is null)
            || MissingHello(body, hello, personal: true).Any(name => PersonalValueOf(keePass.Find(name)) is not null);
    }

    public static bool HelloLoadChanges(VaultBody body, KeePassGroupContent keePass, HelloEntries? hello)
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

    private static bool TakePersonal(VaultBody body, KeePassItem stored, string key, DateTimeOffset now, HelloEntries? hello, List<HelloSkip> notSealed)
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

    private static bool Take(VaultBody body, KeePassItem item, string databaseId, DateTimeOffset now, HelloEntries? hello, List<HelloSkip> notSealed)
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

    private static KeePassSyncState Classify(string local, string? baseHash, string? keePass)
    {
        if (keePass is null)
        {
            return baseHash is null ? KeePassSyncState.New : KeePassSyncState.RemovedInKeePass;
        }

        if (keePass == local)
        {
            return KeePassSyncState.Saved;
        }

        if (baseHash == keePass)
        {
            return KeePassSyncState.Changed;
        }

        return baseHash == local ? KeePassSyncState.ChangedInKeePass : KeePassSyncState.Conflict;
    }
}
