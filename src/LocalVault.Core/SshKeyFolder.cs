using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalVault.Core;

public sealed record SshKeyRecord(string Hash, DateTimeOffset WrittenAt, string WrittenBy, IReadOnlyList<string> Users);

public enum SshKeyWriteState
{
    Write,
    Identical,
    OtherRepository,
    NotWrittenByLocalVault,
    NewerElsewhere,
}

public sealed record SshKeyWritePlan(SshKeyWriteState State, string Target, SshKeyRecord? Record)
{
    public bool Refused => State is SshKeyWriteState.OtherRepository or SshKeyWriteState.NotWrittenByLocalVault or SshKeyWriteState.NewerElsewhere;
}

public sealed class SshKeyFolder : IDisposable
{
    public const int CurrentSchema = 1;
    private const string SchemaField = "schema";
    private const string OriginField = "origin";
    private const string KeysField = "keys";
    private const string HashField = "hash";
    private const string WrittenAtField = "writtenAt";
    private const string WrittenByField = "writtenBy";
    private const string UsersField = "users";
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

    private readonly FileStream heldLock;
    private readonly JsonObject root;

    private SshKeyFolder(string directory, FileStream heldLock, JsonObject root)
    {
        Directory = directory;
        this.heldLock = heldLock;
        this.root = root;
    }

    public string Directory { get; }

    public string MarkerPath => Path.Combine(Directory, SshKeyPath.MarkerFileName);

    public string? Origin => root[OriginField] is JsonValue value && value.TryGetValue<string>(out var origin) ? origin : null;

    // No origin field: folder unclaimed. Empty origin: claimed by a repository without origin, never matching a URL.
    public bool BelongsTo(string? origin) =>
        Origin is not { } marked || string.Equals(marked, origin ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, SshKeyRecord> Keys =>
        root[KeysField] is JsonObject keys
            ? keys.Where(pair => pair.Value is JsonObject).ToDictionary(pair => pair.Key, pair => ToRecord((JsonObject)pair.Value!), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, SshKeyRecord>(StringComparer.OrdinalIgnoreCase);

    public static string FolderOf(string storedPath) =>
        Path.GetDirectoryName(StoredPathRules.Resolve(StoredPathRules.HomeDirectory, SshKeyPath.Validate(storedPath)))!;

    public static SshKeyFolder Open(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!System.IO.Directory.Exists(directory))
        {
            PrivateFile.CreateFolder(directory);
        }

        var heldLock = AcquireLock(Path.Combine(directory, SshKeyPath.MarkerLockFileName));
        try
        {
            return new SshKeyFolder(directory, heldLock, ReadMarker(Path.Combine(directory, SshKeyPath.MarkerFileName)));
        }
        catch
        {
            heldLock.Dispose();
            throw;
        }
    }

    public static IReadOnlyList<string> MarkedFolders(string sshDirectory)
    {
        ArgumentNullException.ThrowIfNull(sshDirectory);
        return System.IO.Directory.Exists(sshDirectory)
            ? System.IO.Directory.EnumerateDirectories(sshDirectory).Where(folder => File.Exists(Path.Combine(folder, SshKeyPath.MarkerFileName))).ToList()
            : [];
    }

    public static string HashOf(ReadOnlySpan<byte> content) => Convert.ToHexStringLower(SHA256.HashData(content));

    public static string? HashOfFile(string path) => File.Exists(path) ? HashOf(File.ReadAllBytes(path)) : null;

    public SshKeyWritePlan Plan(string fileName, string? origin, string vaultPath, ReadOnlySpan<byte> value, DateTimeOffset entryCreated, bool overwrite)
    {
        var target = Path.Combine(Directory, fileName);
        Keys.TryGetValue(fileName, out var record);
        if (!BelongsTo(origin))
        {
            return new SshKeyWritePlan(SshKeyWriteState.OtherRepository, target, record);
        }

        if (!File.Exists(target))
        {
            return new SshKeyWritePlan(SshKeyWriteState.Write, target, record);
        }

        var current = File.ReadAllBytes(target);
        try
        {
            if (current.AsSpan().SequenceEqual(value))
            {
                return new SshKeyWritePlan(SshKeyWriteState.Identical, target, record);
            }

            if (overwrite)
            {
                return new SshKeyWritePlan(SshKeyWriteState.Write, target, record);
            }

            if (record is null)
            {
                return new SshKeyWritePlan(SshKeyWriteState.NotWrittenByLocalVault, target, null);
            }

            return HashOf(current) == record.Hash
                && !SameVault(record.WrittenBy, vaultPath)
                && record.WrittenAt > entryCreated
                ? new SshKeyWritePlan(SshKeyWriteState.NewerElsewhere, target, record)
                : new SshKeyWritePlan(SshKeyWriteState.Write, target, record);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(current);
        }
    }

    public SshKeyRecord? NewerThan(string fileName, ReadOnlySpan<byte> value, DateTimeOffset entryCreated)
    {
        if (!Keys.TryGetValue(fileName, out var record) || HashOf(value) == record.Hash || record.WrittenAt <= entryCreated)
        {
            return null;
        }

        return HashOfFile(Path.Combine(Directory, fileName)) == record.Hash ? record : null;
    }

    public void Record(string fileName, string? origin, string vaultPath, ReadOnlySpan<byte> written, DateTimeOffset at)
    {
        root[OriginField] = origin ?? string.Empty;
        var users = Keys.TryGetValue(fileName, out var existing) ? existing.Users.ToList() : [];
        if (!users.Any(user => SameVault(user, vaultPath)))
        {
            users.Add(Path.GetFullPath(vaultPath));
        }

        KeysNode[fileName] = new JsonObject
        {
            [HashField] = HashOf(written),
            [WrittenAtField] = at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            [WrittenByField] = Path.GetFullPath(vaultPath),
            [UsersField] = new JsonArray(users.Select(user => (JsonNode?)JsonValue.Create(user)).ToArray()),
        };
    }

    public void AddUser(string fileName, string? origin, string vaultPath)
    {
        if (!Keys.TryGetValue(fileName, out var record))
        {
            return;
        }

        root[OriginField] = origin ?? string.Empty;
        if (!record.Users.Any(user => SameVault(user, vaultPath)))
        {
            ((JsonArray)((JsonObject)KeysNode[fileName]!)[UsersField]!).Add(Path.GetFullPath(vaultPath));
        }
    }

    public IReadOnlyList<string> PruneUsers(Func<string, bool> vaultExists)
    {
        ArgumentNullException.ThrowIfNull(vaultExists);
        var pruned = new List<string>();
        foreach (var (file, node) in KeysNode.Where(pair => pair.Value is JsonObject).Select(pair => (pair.Key, (JsonObject)pair.Value!)).ToList())
        {
            if (node[UsersField] is not JsonArray users)
            {
                continue;
            }

            foreach (var user in users.ToList())
            {
                var path = user!.GetValue<string>();
                if (!vaultExists(path))
                {
                    users.Remove(user);
                    pruned.Add($"{file}: {path}");
                }
            }
        }

        return pruned;
    }

    public void Save()
    {
        var text = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        SealedFile.Write(MarkerPath, text, written => JsonNode.Parse(written), overwrite: true);
    }

    public void Dispose() => heldLock.Dispose();

    private JsonObject KeysNode
    {
        get
        {
            if (root[KeysField] is JsonObject keys)
            {
                return keys;
            }

            var created = new JsonObject();
            root[KeysField] = created;
            return created;
        }
    }

    private static bool SameVault(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static SshKeyRecord ToRecord(JsonObject node) => new(
        node[HashField]!.GetValue<string>(),
        DateTimeOffset.Parse(node[WrittenAtField]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
        node[WrittenByField]!.GetValue<string>(),
        node[UsersField] is JsonArray users ? users.Select(user => user!.GetValue<string>()).ToList() : []);

    private static JsonObject ReadMarker(string path)
    {
        if (!File.Exists(path))
        {
            return new JsonObject { [SchemaField] = CurrentSchema, [KeysField] = new JsonObject() };
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new VaultException($"'{path}' is not a JSON object.");
            if (root[SchemaField] is not JsonValue schema || !schema.TryGetValue<int>(out var number) || number > CurrentSchema)
            {
                throw new VaultException($"'{path}' has no supported 'schema' number. Run lv update.");
            }

            if (root[KeysField] is not JsonObject keys || keys.Any(pair => !IsRecord(pair.Value)))
            {
                throw new VaultException($"'{path}' has a 'keys' list that is damaged.");
            }

            return root;
        }
        catch (JsonException ex)
        {
            throw new VaultException($"'{path}' is not valid JSON.", ex);
        }
    }

    private static bool IsRecord(JsonNode? node) =>
        node is JsonObject record
        && record[HashField] is JsonValue hash && hash.TryGetValue<string>(out _)
        && record[WrittenAtField] is JsonValue at && at.TryGetValue<string>(out var atText)
        && DateTimeOffset.TryParse(atText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _)
        && record[WrittenByField] is JsonValue by && by.TryGetValue<string>(out _)
        && record[UsersField] is JsonArray users && users.All(user => user is JsonValue value && value.TryGetValue<string>(out _));

    private static FileStream AcquireLock(string lockPath)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (waited.Elapsed < LockTimeout)
            {
                Thread.Sleep(RetryDelay);
            }
            catch (IOException ex)
            {
                throw new VaultException($"'{lockPath}' is locked by another process for more than {LockTimeout.TotalSeconds} s.", ex);
            }
        }
    }
}
