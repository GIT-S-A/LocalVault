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
}

public sealed record KeePassLoadResult(int Added, int Taken, int Kept, int Removed);

public static class KeePassSync
{
    public static string PersonalKey(string databaseId) => VaultEntry.PersonalBasePrefix + databaseId;

    public static IReadOnlyList<KeePassComparison> Compare(VaultBody body, KeePassGroupContent keePass, bool excludeRequired = false)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(keePass);
        var result = new List<KeePassComparison>();
        foreach (var entry in body.Entries.Where(entry => entry.IsKnownKind && !(excludeRequired && entry.IsRequired)))
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
        foreach (var entry in body.Entries.Where(entry => entry.IsRequired))
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

    private static KeePassItem? Shared(KeePassItem? item) => item is { IsUnderHome: true } && item is not { Ssh: not null, IsPersonal: false } ? null : item;

    private static IEnumerable<KeePassRemoval> PersonalRemovals(VaultBody body, string key) =>
        body.KeePassRemovals.Where(r => string.Equals(r.Database, key, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<KeePassComparison> ComparePending(VaultBody body, string? databaseId = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        var result = body.Entries
            .Where(entry => entry.IsKnownKind)
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
        bool excludeRequired = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(replaceConflict);
        int added = 0, updated = 0, deleted = 0;
        var skipped = new List<KeePassComparison>();
        var bases = new List<(EntryName, string)>();
        var dropped = new List<EntryName>();
        var overwritten = new List<EntryName>();
        foreach (var comparison in Compare(body, keePass, excludeRequired))
        {
            if (mirror && comparison.State is KeePassSyncState.Conflict or KeePassSyncState.ChangedInKeePass or KeePassSyncState.RemovedInKeePass)
            {
                if (body.Find(comparison.Name) is { } entry)
                {
                    target.Put(groupPath, entry);
                    bases.Add((comparison.Name, comparison.VaultHash!));
                }
                else
                {
                    target.Delete(groupPath, comparison.Name);
                    dropped.Add(comparison.Name);
                }

                overwritten.Add(comparison.Name);
                updated++;
                continue;
            }

            switch (comparison.State)
            {
                case KeePassSyncState.New:
                    target.Put(groupPath, body.Find(comparison.Name)!);
                    bases.Add((comparison.Name, comparison.VaultHash!));
                    added++;
                    break;
                case KeePassSyncState.Changed:
                    target.Put(groupPath, body.Find(comparison.Name)!);
                    bases.Add((comparison.Name, comparison.VaultHash!));
                    updated++;
                    break;
                case KeePassSyncState.Conflict when replaceConflict(comparison.Name):
                    target.Put(groupPath, body.Find(comparison.Name)!);
                    bases.Add((comparison.Name, comparison.VaultHash!));
                    updated++;
                    break;
                case KeePassSyncState.Saved:
                    bases.Add((comparison.Name, comparison.VaultHash!));
                    break;
                case KeePassSyncState.RemovalPending:
                    target.Delete(groupPath, comparison.Name);
                    dropped.Add(comparison.Name);
                    deleted++;
                    break;
                case KeePassSyncState.Conflict:
                case KeePassSyncState.ChangedInKeePass:
                case KeePassSyncState.RemovedInKeePass:
                    skipped.Add(comparison);
                    break;
                case KeePassSyncState.OnlyInKeePass when mirror:
                    skipped.Add(comparison);
                    break;
                default:
                    break;
            }
        }

        dropped.AddRange(body.KeePassRemovalsFor(keePass.DatabaseId).Where(r => keePass.Find(r.Name) is null).Select(r => r.Name));
        return new KeePassSaveResult(keePass.DatabaseId, added, updated, deleted, skipped, bases, dropped) { Overwritten = overwritten };
    }

    public static KeePassSaveResult SavePersonal(
        VaultBody body,
        KeePassGroupContent keePass,
        KeePassVault target,
        IReadOnlyList<string> groupPath,
        bool withDeclaration,
        Func<EntryName, bool> replaceConflict)
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
        foreach (var comparison in ComparePersonal(body, keePass))
        {
            switch (comparison.State)
            {
                case KeePassSyncState.New:
                    target.PutPersonalValue(groupPath, body.Find(comparison.Name)!, withDeclaration);
                    bases.Add((comparison.Name, comparison.VaultHash!));
                    added++;
                    break;
                case KeePassSyncState.Changed:
                case KeePassSyncState.Conflict when replaceConflict(comparison.Name):
                    target.PutPersonalValue(groupPath, body.Find(comparison.Name)!, withDeclaration);
                    bases.Add((comparison.Name, comparison.VaultHash!));
                    updated++;
                    break;
                case KeePassSyncState.Saved:
                    bases.Add((comparison.Name, comparison.VaultHash!));
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

        dropped.AddRange(PersonalRemovals(body, key).Where(r => PersonalValueOf(keePass.Find(r.Name)) is null).Select(r => r.Name));
        return new KeePassSaveResult(key, added, updated, deleted, skipped, bases, dropped) { Personal = true };
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

    public static int Copy(VaultBody body, KeePassVault target, IReadOnlyList<string> groupPath)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(target);
        var written = 0;
        foreach (var entry in body.Entries.Where(entry => entry.IsKnownKind && (!entry.IsLocal || entry.IsRequired)))
        {
            target.Put(groupPath, entry);
            written++;
        }

        return written;
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
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(takeConflict);
        ArgumentNullException.ThrowIfNull(removeHere);
        int added = 0, taken = 0, kept = 0, removed = 0;
        foreach (var comparison in Compare(body, keePass))
        {
            var item = keePass.Find(comparison.Name);
            switch (comparison.State)
            {
                case KeePassSyncState.OnlyInKeePass:
                    Take(body, item!, keePass.DatabaseId, now);
                    added++;
                    break;
                case KeePassSyncState.ChangedInKeePass:
                    var existed = body.Find(comparison.Name) is not null;
                    Take(body, item!, keePass.DatabaseId, now);
                    if (existed)
                    {
                        taken++;
                    }
                    else
                    {
                        added++;
                    }

                    break;
                case KeePassSyncState.Conflict when takeConflict(comparison.Name):
                    Take(body, item!, keePass.DatabaseId, now);
                    taken++;
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

        return new KeePassLoadResult(added, taken, kept, removed);
    }

    public static KeePassLoadResult LoadPersonal(
        VaultBody body,
        KeePassGroupContent keePass,
        Func<EntryName, bool> takeConflict,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(keePass);
        ArgumentNullException.ThrowIfNull(takeConflict);
        var key = PersonalKey(keePass.DatabaseId);
        int added = 0, taken = 0, kept = 0;
        foreach (var comparison in ComparePersonal(body, keePass))
        {
            var stored = PersonalValueOf(keePass.Find(comparison.Name));
            switch (comparison.State)
            {
                case KeePassSyncState.OnlyInKeePass:
                    TakePersonal(body, stored!, key, now);
                    added++;
                    break;
                case KeePassSyncState.ChangedInKeePass when body.Find(comparison.Name) is not null:
                case KeePassSyncState.Conflict when takeConflict(comparison.Name):
                    TakePersonal(body, stored!, key, now);
                    taken++;
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

        return new KeePassLoadResult(added, taken, kept, 0);
    }

    public static bool PersonalLoadChanges(VaultBody body, KeePassGroupContent keePass)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(keePass);
        var key = PersonalKey(keePass.DatabaseId);
        return ComparePersonal(body, keePass).Any(c =>
                c.State is KeePassSyncState.OnlyInKeePass or KeePassSyncState.ChangedInKeePass or KeePassSyncState.Conflict or KeePassSyncState.RemovedInKeePass
                || (c.State == KeePassSyncState.Saved && body.Find(c.Name)?.KeePassBaseFor(key) != c.VaultHash))
            || PersonalRemovals(body, key).Any(r => PersonalValueOf(keePass.Find(r.Name)) is null);
    }

    private static void TakePersonal(VaultBody body, KeePassItem stored, string key, DateTimeOffset now)
    {
        body.SetPersonalValue(stored.Name, stored.ToVaultEntry(now));
        body.RecordKeePassBase(stored.Name, key, stored.Hash, now);
    }

    private static void Take(VaultBody body, KeePassItem item, string databaseId, DateTimeOffset now)
    {
        var existing = body.Find(item.Name);
        if (item.Kind == VaultEntry.RequiredKind && existing is not null && !existing.IsMissing)
        {
            body.Set(item.ToVaultEntry(now));
            body.RecordKeePassBase(existing.Name, databaseId, item.Hash, now);
            return;
        }

        var entry = item.ToVaultEntry(now);
        if (existing is not null && !existing.Name.FullName.Equals(item.Name.FullName, StringComparison.Ordinal))
        {
            body.Remove(existing.Name, recordForKeePass: false);
        }

        body.Set(entry);
        body.RecordKeePassBase(item.Name, databaseId, item.Hash, now);
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
