using System.Text;
using LocalVault.Core;
using LocalVault.Engine;

namespace LocalVault.PowerShell;

internal sealed record TrackedValue(string Key, string Reference, string Vault, string? Value, string? Missing);

internal sealed class UserSecretTracking
{
    private readonly Dictionary<string, VaultBody?> bodies = new(StringComparer.OrdinalIgnoreCase);

    public UserSecretTracking(VaultLocation repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        Repository = repository.GitRoot is not null
            ? repository
            : throw new VaultException(".NET user secrets are tracked per git repository; this folder is not in one.");
        Tracking = ProjectFile.ReadUserSecrets(repository);
    }

    public VaultLocation Repository { get; }

    public IReadOnlyList<ProjectUserSecrets> Tracking { get; }

    public IReadOnlyList<DotNetProject> Projects => projects ??= DotNetProjects.Find(Repository.GitRoot!);

    private IReadOnlyList<DotNetProject>? projects;

    public static string Label(string id, IEnumerable<DotNetProject> users)
    {
        var names = users.Where(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)).Select(p => p.Name).ToList();
        return names.Count == 0 ? $"user secrets {id}" : $"user secrets of {string.Join(", ", names)} ({id})";
    }

    public static string? Refusal(VaultEntry entry, EntryReference reference)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.IsHello || entry.IsRequiredHello ? $"'{entry.Name}' is protected by Windows Hello; its value is never written to a plain-text file."
            : entry.IsSshKey ? $"'{entry.Name}' is an SSH key, not a value for user secrets."
            : entry.IsFile || entry.IsFileShape ? $"'{entry.Name}' is a file entry, not a value for user secrets."
            : reference.Part == EntryPart.Totp ? $"{reference} changes every 30 seconds; a copy in a file would be stale at once."
            : null;
    }

    public string Label(string id)
    {
        try
        {
            return Label(id, Projects);
        }
        catch (VaultException)
        {
            return Label(id, []);
        }
    }

    public IEnumerable<ProjectUserSecrets> For(string id, IReadOnlySet<string>? vaults = null) => Tracking.Where(t =>
        string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase) && (vaults is null || vaults.Contains(t.Vault)));

    public IReadOnlyList<TrackedValue> Values(string id, IReadOnlySet<string>? vaults = null, string? onlyKey = null)
    {
        var values = new List<TrackedValue>();
        foreach (var tracking in For(id, vaults))
        {
            var body = Body(tracking.Vault);
            foreach (var (key, reference) in tracking.Keys.Where(k => onlyKey is null || string.Equals(k.Key, onlyKey, StringComparison.OrdinalIgnoreCase)))
            {
                values.Add(ValueOf(key, reference, tracking.Vault, body));
            }
        }

        return values;
    }

    public void Forget(string vault) => bodies.Remove(vault);

    public VaultBody? Body(string vault)
    {
        if (!bodies.TryGetValue(vault, out var body))
        {
            body = File.Exists(vault) ? VaultAt(vault).Read().Body : null;
            bodies[vault] = body;
        }

        return body;
    }

    public static VaultFile VaultAt(string vault) =>
        RepositoryVaults.StoreFor(VaultLocation.Resolve(vault, null, Path.GetDirectoryName(vault)!));

    public void Load(IVaultHost host, IReadOnlySet<string>? vaults, string? onlyId, string? onlyKey, bool overwrite, bool lenient)
    {
        ArgumentNullException.ThrowIfNull(host);
        var conflicts = new List<string>();
        var ids = Tracking.Where(t => vaults is null || vaults.Contains(t.Vault)).Select(t => t.Id)
            .Where(id => onlyId is null || string.Equals(id, onlyId, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            var label = Label(id);
            if (!Projects.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                host.Warn($"{label}: no project of this repository uses it any more; not written. Untrack its keys with lv user-secrets untrack {id} <key>.");
                continue;
            }

            var values = Values(id, vaults, onlyKey);
            var missing = values.Where(v => v.Value is null).ToList();
            foreach (var reason in missing.GroupBy(v => v.Missing))
            {
                host.Warn($"{label}: not written ({reason.Key}): {string.Join(", ", reason.Select(v => v.Key))}.");
            }

            var wanted = values.Where(v => v.Value is not null).ToDictionary(v => v.Key, v => v.Value!, StringComparer.OrdinalIgnoreCase);
            if (wanted.Count == 0)
            {
                continue;
            }

            var path = UserSecretsFile.PathFor(id);
            var current = UserSecretsFile.Read(path);
            var comparison = UserSecretsFile.Compare(current, wanted);
            if (comparison.Differing.Count > 0 && !overwrite)
            {
                var question = $"{label}: {string.Join(", ", comparison.Differing)} differ{(comparison.Differing.Count == 1 ? "s" : string.Empty)} from the vault. Replace them with the vault's values?";
                if (host.CanConfirm)
                {
                    if (!host.AskYesNo(question))
                    {
                        host.Tell($"{label}: left as it was.");
                        continue;
                    }
                }
                else if (lenient)
                {
                    host.Warn($"{label}: not written, {string.Join(", ", comparison.Differing)} differ from the vault. Run lv user-secrets load --overwrite to replace them.");
                    continue;
                }
                else
                {
                    conflicts.Add($"{label}: {string.Join(", ", comparison.Differing)}");
                    continue;
                }
            }

            var written = comparison.Absent.Concat(comparison.Differing).ToList();
            if (written.Count == 0)
            {
                if (!lenient)
                {
                    host.Tell($"{label}: up to date.");
                }

                continue;
            }

            UserSecretsFile.Write(path, UserSecretsFile.Apply(current, wanted));
            host.Tell($"{label}: wrote {string.Join(", ", written)} to {path}.");
        }

        if (conflicts.Count > 0)
        {
            throw new ImportConflictException(
                $"Keys differ from the vault: {string.Join("; ", conflicts)}. Nothing was written for them. Use -Overwrite (lv user-secrets load --overwrite) to replace them.");
        }
    }

    public void Clear(IVaultHost host, IReadOnlySet<string>? vaults, string? onlyId, string? onlyKey)
    {
        ArgumentNullException.ThrowIfNull(host);
        var ids = Tracking.Where(t => vaults is null || vaults.Contains(t.Vault)).Select(t => t.Id)
            .Where(id => onlyId is null || string.Equals(id, onlyId, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            var path = UserSecretsFile.PathFor(id);
            if (!File.Exists(path))
            {
                continue;
            }

            var label = Label(id);
            var current = UserSecretsFile.Read(path);
            var removed = new List<string>();
            var kept = new List<string>();
            foreach (var value in Values(id, vaults, onlyKey).Where(v => current.ContainsKey(v.Key)))
            {
                if (value.Value is not null && string.Equals(UserSecretsFile.Text(current[value.Key]), value.Value, StringComparison.Ordinal))
                {
                    current.Remove(value.Key);
                    removed.Add(value.Key);
                }
                else
                {
                    kept.Add(value.Key);
                }
            }

            if (removed.Count > 0)
            {
                UserSecretsFile.Write(path, current);
                host.Tell($"{label}: removed {string.Join(", ", removed)}{(current.Count == 0 ? $"; deleted {path}, now empty" : string.Empty)}.");
            }

            if (kept.Count > 0)
            {
                host.Warn($"{label}: kept {string.Join(", ", kept)}; their values differ from the vault.");
            }
        }
    }

    private static TrackedValue ValueOf(string key, string reference, string vault, VaultBody? body)
    {
        if (!EntryReference.TryParse(reference, out var parsed))
        {
            return new TrackedValue(key, reference, vault, null, $"'{reference}' is not an entry name");
        }

        var entry = body?.Find(parsed.Name);
        if (entry is null || entry.IsMissing)
        {
            return new TrackedValue(key, reference, vault, null, "no value on this PC");
        }

        if (Refusal(entry, parsed) is not null || !(entry.IsSecret || entry.IsCredential))
        {
            return new TrackedValue(key, reference, vault, null, "not a value user secrets can hold");
        }

        byte[] bytes;
        try
        {
            bytes = entry.ReadPart(parsed, DateTimeOffset.UtcNow);
        }
        catch (VaultException ex)
        {
            return new TrackedValue(key, reference, vault, null, ex.Message.TrimEnd('.'));
        }

        try
        {
            return new TrackedValue(key, reference, vault, Encoding.UTF8.GetString(bytes), null);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }
}
