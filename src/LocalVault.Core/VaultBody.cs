using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalVault.Core;

public sealed class VaultBody
{
    public const int CurrentSchema = 5;
    public const int HelloSchema = 6;

    private const string SchemaField = "schema";
    private const string ExportedField = "exported";
    private const string LastExportedField = "lastExported";
    private const string BackupField = "backup";
    private const string EntriesField = "entries";
    private const string MigratedLegacyField = "migratedLegacy";
    private const string KeePassRemovedField = "kpRemoved";
    private const string RemovedNameField = "name";
    private const string RemovedHashField = "hash";
    private const string RemovedDatabaseField = "db";
    private const string HelloField = "hello";
    private const string HelloKeyIdField = "keyId";
    private const string HelloPublicKeyField = "publicKey";
    private const string HelloWrappedField = "wrapped";
    private const string HelloPadField = "pad";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = false };

    private readonly JsonObject root;
    private readonly List<VaultEntry> entries;
    private readonly int currentSchema;

    private VaultBody(JsonObject root, List<VaultEntry> entries, int currentSchema, int? upgradedFrom)
    {
        this.root = root;
        this.entries = entries;
        this.currentSchema = currentSchema;
        UpgradedFrom = upgradedFrom;
    }

    public int Schema => root[SchemaField]!.GetValue<int>();

    public bool IsWritable => Schema <= (currentSchema == CurrentSchema ? HelloSchema : currentSchema);

    public bool HasHello => root[HelloField] is not null || entries.Exists(entry => entry.IsHello);

    public HelloVaultKey? HelloKey
    {
        get => root[HelloField] is JsonObject node
            ? new HelloVaultKey(
                node[HelloKeyIdField]!.GetValue<string>(),
                Convert.FromBase64String(node[HelloPublicKeyField]!.GetValue<string>()),
                Convert.FromBase64String(node[HelloWrappedField]!.GetValue<string>()),
                node[HelloPadField]!.GetValue<string>())
            : null;
        set
        {
            if (value is null)
            {
                root.Remove(HelloField);
                return;
            }

            root[HelloField] = new JsonObject
            {
                [HelloKeyIdField] = value.KeyId,
                [HelloPublicKeyField] = Convert.ToBase64String(value.PublicKey),
                [HelloWrappedField] = Convert.ToBase64String(value.Wrapped),
                [HelloPadField] = value.Pad,
            };
        }
    }

    public int? UpgradedFrom { get; }

    public IReadOnlyList<VaultEntry> Entries => entries;

    public DateTimeOffset? Exported
    {
        get => ReadTime(ExportedField);
        set => WriteTime(ExportedField, value);
    }

    public DateTimeOffset? LastExported
    {
        get => ReadTime(LastExportedField);
        set => WriteTime(LastExportedField, value);
    }

    public string? MigratedLegacy
    {
        get => root[MigratedLegacyField] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        set => root[MigratedLegacyField] = value;
    }

    public BackupSettings? Backup
    {
        get => root[BackupField] is JsonObject node ? new BackupSettings(node) : null;
        set
        {
            if (value is null)
            {
                root.Remove(BackupField);
            }
            else
            {
                root[BackupField] = value.Node.DeepClone();
            }
        }
    }

    public IReadOnlyList<KeePassRemoval> KeePassRemovals =>
        root[KeePassRemovedField] is JsonArray removed ? removed.OfType<JsonObject>().Select(ToRemoval).ToList() : [];

    public IReadOnlyList<KeePassRemoval> KeePassRemovalsFor(string? databaseId) =>
        KeePassRemovals.Where(r => r.Database is null || databaseId is null || string.Equals(r.Database, databaseId, StringComparison.OrdinalIgnoreCase)).ToList();

    public static VaultBody CreateEmpty() =>
        new(new JsonObject { [SchemaField] = CurrentSchema, [EntriesField] = new JsonArray() }, [], CurrentSchema, null);

    public static VaultBody Parse(ReadOnlySpan<byte> utf8Json) => Parse(utf8Json, BodyUpgrades.Steps, CurrentSchema);

    internal static VaultBody Parse(
        ReadOnlySpan<byte> utf8Json, IReadOnlyDictionary<int, Action<JsonObject>> upgrades, int currentSchema)
    {
        ArgumentNullException.ThrowIfNull(upgrades);
        JsonObject root;
        try
        {
            root = JsonNode.Parse(utf8Json) as JsonObject
                ?? throw new VaultException("Vault body is not a JSON object.");
            _ = root.Count;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw new VaultException("Vault body is not valid JSON.", ex);
        }

        if (root[SchemaField] is not JsonValue schema || !schema.TryGetValue<int>(out var version) || version < 2)
        {
            throw new VaultException("Vault body has no supported 'schema' number.");
        }

        int? upgradedFrom = version < currentSchema ? version : null;
        for (; version < currentSchema; version++)
        {
            if (!upgrades.TryGetValue(version, out var upgrade))
            {
                throw new VaultException($"Vault body schema {version} has no upgrade to schema {version + 1}.");
            }

            upgrade(root);
            root[SchemaField] = version + 1;
        }

        if (root[EntriesField] is not JsonArray array)
        {
            throw new VaultException("Vault body has no 'entries' array.");
        }

        foreach (var field in new[] { ExportedField, LastExportedField })
        {
            if (root[field] is { } time
                && !(time is JsonValue value
                    && value.TryGetValue<string>(out var text)
                    && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _)))
            {
                throw new VaultException($"Vault body has a '{field}' time that is not a date.");
            }
        }

        ValidateKeePassRemovals(root[KeePassRemovedField]);
        ValidateHello(root[HelloField]);

        var parsed = new List<VaultEntry>(array.Count);
        var seen = new HashSet<string>(EntryName.Comparer);
        foreach (var item in array)
        {
            if (item is not JsonObject node)
            {
                throw new VaultException("Vault body has an entry that is not an object.");
            }

            VaultEntry entry;
            try
            {
                entry = new VaultEntry(node);
            }
            catch (ArgumentException ex)
            {
                throw new VaultException("Vault body has an entry that is not valid JSON.", ex);
            }

            if (!seen.Add(entry.Name.FullName))
            {
                throw new VaultException($"Vault body holds '{entry.Name}' twice (names differ only by case).");
            }

            parsed.Add(entry);
        }

        return new VaultBody(root, parsed, currentSchema, upgradedFrom);
    }

    public VaultEntry? Find(EntryName name) => entries.Find(entry => entry.Name.Equals(name));

    public void Set(VaultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var existing = Find(entry.Name);
        if (existing is not null && entry.IsMissing && !existing.IsMissing)
        {
            existing.SetRequired(entry.RequiredNote, entry.RequiredShape, entry.RequiredPath, entry.RequiredHosts, entry.IsRequiredHello);
            return;
        }

        if (entry.IsUnderHome && !entry.IsRequired && entry.Ssh is null && existing is not { IsRequired: true })
        {
            throw new VaultException(
                $"'{entry.Name}' would be restored under your profile ({entry.StoredPath}); only a required entry's personal value can. Declare it with lv require {entry.Name} --file <path> first.");
        }

        if (existing is not null)
        {
            existing.CopyContentFrom(entry);
            return;
        }

        var added = entry.Clone();
        foreach (var removal in TakeRemovals(entry.Name, databaseId: null))
        {
            added.SetKeePassBase(removal.Database, removal.Hash, DateTimeOffset.UtcNow);
        }

        EntriesArray.Add(added.Node);
        entries.Add(added);
    }

    public void SetPersonalValue(EntryName name, VaultEntry value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var existing = Find(name);
        if (existing is not { IsRequired: true } || value.IsRequired || !(value.IsSecret || value.IsCredential || value.IsFile))
        {
            throw new VaultException($"'{name}' is not a required entry; it takes no personal value.");
        }

        existing.CopyContentFrom(value);
    }

    public bool Remove(EntryName name) => Remove(name, recordForKeePass: true);

    public bool Remove(EntryName name, bool recordForKeePass)
    {
        var existing = Find(name);
        if (existing is null)
        {
            return false;
        }

        EntriesArray.Remove(existing.Node);
        entries.Remove(existing);
        if (recordForKeePass && (!existing.IsLocal || existing.IsRequired))
        {
            TakeRemovals(existing.Name, databaseId: null);
            foreach (var (database, hash) in existing.KeePassBases)
            {
                var marker = new JsonObject { [RemovedNameField] = existing.Name.FullName, [RemovedHashField] = hash };
                if (database is not null)
                {
                    marker[RemovedDatabaseField] = database;
                }

                RemovalsArray.Add(marker);
            }
        }

        return true;
    }

    public void SetSshHosts(EntryName name, IReadOnlyList<string> hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        (Find(name) ?? throw new VaultException($"No entry '{name}'.")).SetSshHosts(hosts.Select(SshKeyInfo.NormalizeHost).ToList());
    }

    public void SetSharing(EntryName name, bool local)
    {
        var existing = Find(name) ?? throw new VaultException($"No entry '{name}'.");
        if (existing.IsRequired)
        {
            throw new VaultException($"'{name}' is a required personal value: its value never goes to KeePass, only its declaration.");
        }

        existing.SetLocal(local);
    }

    public void SetRequiredHello(EntryName name, bool hello)
    {
        var existing = Find(name) ?? throw new VaultException($"No entry '{name}'.");
        if (!existing.IsRequired)
        {
            throw new VaultException($"'{name}' is not a required entry.");
        }

        existing.SetRequired(existing.RequiredNote, existing.RequiredShape, existing.RequiredPath, existing.RequiredHosts, hello);
    }

    public void RecordKeePassBase(EntryName name, string? databaseId, string? hash, DateTimeOffset at) =>
        (Find(name) ?? throw new VaultException($"No entry '{name}'.")).SetKeePassBase(databaseId, hash, at);

    public IReadOnlyList<KeePassRemoval> TakeRemovals(EntryName name, string? databaseId)
    {
        if (root[KeePassRemovedField] is not JsonArray removed)
        {
            return [];
        }

        var taken = removed.OfType<JsonObject>()
            .Where(item => EntryName.Parse(item[RemovedNameField]!.GetValue<string>()).Equals(name))
            .Where(item => databaseId is null
                || item[RemovedDatabaseField] is null
                || string.Equals(item[RemovedDatabaseField]!.GetValue<string>(), databaseId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var node in taken)
        {
            removed.Remove(node);
        }

        if (removed.Count == 0)
        {
            root.Remove(KeePassRemovedField);
        }

        return taken.Select(ToRemoval).ToList();
    }

    private static KeePassRemoval ToRemoval(JsonObject node) => new(
        EntryName.Parse(node[RemovedNameField]!.GetValue<string>()),
        node[RemovedHashField]!.GetValue<string>(),
        node[RemovedDatabaseField]?.GetValue<string>());

    public VaultBody Clone() => Parse(ToUtf8(), BodyUpgrades.Steps, currentSchema);

    public VaultBody WithoutLocalSettings()
    {
        var copy = Clone();
        copy.root.Remove(BackupField);
        return copy;
    }

    public VaultBody ForExport(IReadOnlyCollection<EntryName>? hello = null)
    {
        var copy = WithoutLocalSettings();
        copy.root.Remove(KeePassRemovedField);
        foreach (var entry in copy.entries)
        {
            entry.RemoveKeePassBase();
            entry.DropPersonalValue();
            if (hello is not null && entry.IsSealable && !entry.IsRequired && hello.Contains(entry.Name))
            {
                entry.MarkHello();
            }
        }

        return copy;
    }

    public byte[] ToUtf8()
    {
        if (!entries.Exists(entry => entry.IsHello))
        {
            root.Remove(HelloField);
        }

        if (currentSchema == CurrentSchema && Schema is CurrentSchema or HelloSchema)
        {
            root[SchemaField] = HasHello ? HelloSchema : CurrentSchema;
        }

        return JsonSerializer.SerializeToUtf8Bytes(root, WriteOptions);
    }

    private JsonArray EntriesArray => (JsonArray)root[EntriesField]!;

    private JsonArray RemovalsArray
    {
        get
        {
            if (root[KeePassRemovedField] is JsonArray removed)
            {
                return removed;
            }

            var created = new JsonArray();
            root[KeePassRemovedField] = created;
            return created;
        }
    }

    private static void ValidateKeePassRemovals(JsonNode? removed)
    {
        if (removed is null)
        {
            return;
        }

        if (removed is not JsonArray items
            || items.Any(item => !(item is JsonObject node
                && node[RemovedNameField] is JsonValue name && name.TryGetValue<string>(out var text) && EntryName.TryParse(text, out _)
                && node[RemovedHashField] is JsonValue hash && hash.TryGetValue<string>(out _))))
        {
            throw new VaultException("Vault body has a 'kpRemoved' list that is damaged.");
        }
    }

    private static void ValidateHello(JsonNode? hello)
    {
        if (hello is null)
        {
            return;
        }

        if (hello is not JsonObject node
            || !IsBase64String(node[HelloPublicKeyField])
            || !IsBase64String(node[HelloWrappedField])
            || node[HelloKeyIdField] is not JsonValue keyId || !keyId.TryGetValue<string>(out _)
            || node[HelloPadField] is not JsonValue pad || !pad.TryGetValue<string>(out _))
        {
            throw new VaultException("Vault body has Windows Hello key data that is damaged.");
        }
    }

    private static bool IsBase64String(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && System.Buffers.Text.Base64.IsValid(text);

    private DateTimeOffset? ReadTime(string field)
    {
        var text = root[field]?.GetValue<string>();
        return text is null
            ? null
            : DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }

    private void WriteTime(string field, DateTimeOffset? value)
    {
        if (value is null)
        {
            root.Remove(field);
        }
        else
        {
            root[field] = value.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }
    }
}

public sealed record HelloVaultKey(string KeyId, byte[] PublicKey, byte[] Wrapped, string Pad)
{
    public string Fingerprint => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(PublicKey))[..16];
}

public sealed record KeePassRemoval(EntryName Name, string Hash, string? Database = null);
