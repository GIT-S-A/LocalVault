namespace LocalVault.Engine;

public sealed record ImportSourceContent(
    IReadOnlyList<VaultEntry> Entries,
    IReadOnlyList<RefusedName> Refused,
    IReadOnlyList<IReadOnlyList<VaultEntry>> CaseClashes);

public sealed class ImportPlan
{
    private ImportPlan(
        IReadOnlyList<VaultEntry> missing,
        IReadOnlyList<VaultEntry> identical,
        IReadOnlyList<VaultEntry> different,
        IReadOnlyList<IReadOnlyList<VaultEntry>> caseClashes)
    {
        Missing = missing;
        Identical = identical;
        Different = different;
        CaseClashes = caseClashes;
    }

    public IReadOnlyList<VaultEntry> Missing { get; }

    public IReadOnlyList<VaultEntry> Identical { get; }

    public IReadOnlyList<VaultEntry> Different { get; }

    public IReadOnlyList<IReadOnlyList<VaultEntry>> CaseClashes { get; }

    public bool HasConflicts => Different.Count > 0 || CaseClashes.Count > 0;

    public static ImportPlan Create(VaultBody current, ImportSourceContent incoming)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);
        var missing = new List<VaultEntry>();
        var identical = new List<VaultEntry>();
        var different = new List<VaultEntry>();
        foreach (var entry in incoming.Entries)
        {
            var existing = current.Find(entry.Name);
            if (existing is null)
            {
                missing.Add(entry);
            }
            else if (existing.HasSameContent(entry))
            {
                identical.Add(entry);
            }
            else
            {
                different.Add(entry);
            }
        }

        return new ImportPlan(missing, identical, different, incoming.CaseClashes);
    }

    public IReadOnlyList<VaultEntry> Select(IReadOnlyCollection<string> replace, bool replaceAll)
    {
        ArgumentNullException.ThrowIfNull(replace);
        var replaceNames = new HashSet<string>(replace, EntryName.Comparer);
        var selected = new List<VaultEntry>(Missing);
        selected.AddRange(Different.Where(e => replaceAll || replaceNames.Contains(e.Name.FullName)));
        foreach (var group in CaseClashes)
        {
            var exact = group.Where(e => replace.Contains(e.Name.FullName, StringComparer.Ordinal)).ToList();
            if (exact.Count > 1)
            {
                throw new VaultException(
                    $"Choose one of {string.Join(", ", group.Select(e => e.Name.FullName))}, not several.");
            }

            selected.AddRange(exact);
        }

        return selected;
    }

    public IReadOnlyList<string> Unresolved(IReadOnlyCollection<string> replace, bool replaceAll)
    {
        ArgumentNullException.ThrowIfNull(replace);
        var unresolved = new List<string>();
        if (!replaceAll)
        {
            var replaceNames = new HashSet<string>(replace, EntryName.Comparer);
            unresolved.AddRange(Different.Select(e => e.Name.FullName).Where(name => !replaceNames.Contains(name)));
        }

        foreach (var group in CaseClashes)
        {
            if (!group.Any(e => replace.Contains(e.Name.FullName, StringComparer.Ordinal)))
            {
                unresolved.AddRange(group.Select(e => e.Name.FullName));
            }
        }

        return unresolved;
    }
}
