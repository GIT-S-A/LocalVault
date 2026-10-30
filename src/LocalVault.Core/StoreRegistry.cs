using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LocalVault.Core;

public enum StoreScope
{
    Computer,
    Project,
}

public sealed record StoreDefinition(
    string Name,
    string Type,
    string Target,
    string? KeyFile,
    StoreScope Scope,
    string? Id = null,
    bool IsDefault = false,
    string? UnlockWith = null,
    bool IsMirror = false)
{
    public string ExpandedTarget => Environment.ExpandEnvironmentVariables(Target);

    public string? ExpandedKeyFile => KeyFile is null ? null : Environment.ExpandEnvironmentVariables(KeyFile);
}

public sealed record StoreReference(string? Name, string? Id);

public static partial class StoreTypes
{
    public const string KeePass = "keepass";

    public static string Of(string target)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        if (target.EndsWith(".kdbx", StringComparison.OrdinalIgnoreCase))
        {
            return KeePass;
        }

        if (TypePrefix().Match(target) is { Success: true } prefix)
        {
            throw new VaultException($"Stores of type '{prefix.Groups[1].Value}' are not supported yet. Supported: a KeePass .kdbx file.");
        }

        throw new VaultException($"'{target}' is not a store target LocalVault knows. Give a KeePass .kdbx file.");
    }

    public static string NameFromTarget(string target)
    {
        var stem = Path.GetFileNameWithoutExtension(target);
        var name = InvalidNameCharacters().Replace(stem, "-").Trim('-', '.');
        return name.Length == 0 ? "store" : name;
    }

    public static void RequireValidName(string name)
    {
        if (!EntryName.IsValidSegment(name) || name.Any(c => c is ':' or '@' or '+'))
        {
            throw new VaultException($"'{name}' is not a valid store name. Use letters, digits and . _ - only.");
        }
    }

    [GeneratedRegex("^([A-Za-z][A-Za-z0-9]+):")]
    private static partial Regex TypePrefix();

    [GeneratedRegex("[^A-Za-z0-9._-]+")]
    private static partial Regex InvalidNameCharacters();
}

public sealed class StoreRegistry
{
    public const int CurrentSchema = 1;
    public const string FileName = "stores.json";
    public const string LegacyFileName = "keepass.json";
    private const string SchemaField = "schema";
    private const string DefaultField = "default";
    private const string LegacyKeyringField = "keyring";
    private const string UnlockWithField = "unlockWith";
    private const string MirrorField = "mirror";
    private const string StoresField = "stores";
    private const string LegacyDatabasesField = "databases";
    private const string LegacyPathField = "path";
    private const string NameField = "name";
    private const string TypeField = "type";
    private const string TargetField = "target";
    private const string KeyFileField = "keyFile";
    private const string IdField = "id";

    public StoreRegistry(string path)
    {
        RegistryPath = path;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetEnvironmentVariable("LOCALAPPDATA") is { Length: > 0 } localAppData
            ? localAppData
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LocalVault",
        FileName);

    public string RegistryPath { get; }

    public IReadOnlyList<StoreDefinition> Load()
    {
        var root = ReadRoot();
        var defaultName = root[DefaultField]?.GetValue<string>();
        return Stores(root)
            .Select(node =>
            {
                var name = node[NameField]!.GetValue<string>();
                return new StoreDefinition(
                    name,
                    node[TypeField]?.GetValue<string>() ?? StoreTypes.KeePass,
                    node[TargetField]!.GetValue<string>(),
                    node[KeyFileField]?.GetValue<string>(),
                    StoreScope.Computer,
                    node[IdField]?.GetValue<string>(),
                    string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase),
                    node[UnlockWithField]?.GetValue<string>(),
                    node[MirrorField]?.GetValue<bool>() == true);
            })
            .ToList();
    }

    public StoreDefinition? FindById(string id) =>
        Load().FirstOrDefault(store => string.Equals(store.Id, id, StringComparison.OrdinalIgnoreCase));

    public StoreDefinition? FindByName(string name) =>
        Load().FirstOrDefault(store => string.Equals(store.Name, name, StringComparison.OrdinalIgnoreCase));

    public StoreDefinition? FindByTarget(string expandedTarget) =>
        Load().FirstOrDefault(store => string.Equals(
            Path.GetFullPath(store.ExpandedTarget), Path.GetFullPath(expandedTarget), StringComparison.OrdinalIgnoreCase));

    public StoreDefinition? Default
    {
        get
        {
            var stores = Load();
            return stores.FirstOrDefault(store => store.IsDefault) ?? (stores.Count == 1 ? stores[0] : null);
        }
    }

    public void RecordId(StoreDefinition store, string id)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (store.Scope != StoreScope.Computer)
        {
            return;
        }

        var root = ReadRoot();
        if (FindNode(root, store.Name) is not { } node
            || !string.Equals(node[TargetField]?.GetValue<string>(), store.Target, StringComparison.OrdinalIgnoreCase)
            || string.Equals(node[IdField]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        node[IdField] = id;
        Save(root);
    }

    public StoreDefinition Register(string name, string target, string? keyFile, bool makeDefault, string? unlockWith = null, bool mirror = false)
    {
        StoreTypes.RequireValidName(name);
        if (unlockWith is not null)
        {
            RequireUnlocker(name, unlockWith);
        }

        var type = StoreTypes.Of(target);
        RequireFile(target, "store");
        if (keyFile is not null)
        {
            RequireFile(keyFile, "key file");
        }

        var root = ReadRoot();
        var node = FindNode(root, name);
        if (node is null)
        {
            node = new JsonObject { [NameField] = name };
            ((JsonArray)root[StoresField]!).Add(node);
        }

        if (!string.Equals(node[TargetField]?.GetValue<string>(), target, StringComparison.OrdinalIgnoreCase))
        {
            node.Remove(IdField);
        }

        node[TypeField] = type;
        node[TargetField] = target;
        if (keyFile is null)
        {
            node.Remove(KeyFileField);
        }
        else
        {
            node[KeyFileField] = keyFile;
        }

        if (makeDefault)
        {
            root[DefaultField] = node[NameField]!.GetValue<string>();
        }

        if (mirror)
        {
            node[MirrorField] = true;
        }
        else
        {
            node.Remove(MirrorField);
        }

        if (unlockWith is null)
        {
            node.Remove(UnlockWithField);
        }
        else
        {
            node[UnlockWithField] = FindByName(unlockWith)!.Name;
        }

        Save(root);
        return FindByName(name)!;
    }

    public StoreDefinition Change(string name, bool? mirror, string? unlockWith, bool clearUnlock, bool makeDefault)
    {
        var root = ReadRoot();
        var node = FindNode(root, name) ?? throw new VaultException($"No store named '{name}' on this PC.");
        var storeName = node[NameField]!.GetValue<string>();
        if (unlockWith is not null)
        {
            RequireUnlocker(storeName, unlockWith);
            node[UnlockWithField] = FindByName(unlockWith)!.Name;
        }
        else if (clearUnlock)
        {
            node.Remove(UnlockWithField);
        }

        if (mirror == true)
        {
            node[MirrorField] = true;
        }
        else if (mirror == false)
        {
            node.Remove(MirrorField);
        }

        if (makeDefault)
        {
            root[DefaultField] = storeName;
        }

        Save(root);
        return FindByName(storeName)!;
    }

    public bool Unregister(string name)
    {
        var root = ReadRoot();
        var node = FindNode(root, name);
        if (node is null)
        {
            return false;
        }

        var unlocked = Stores(root)
            .Where(other => string.Equals(other[UnlockWithField]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase))
            .Select(other => other[NameField]!.GetValue<string>())
            .ToList();
        if (unlocked.Count > 0)
        {
            throw new VaultException(
                $"Store '{name}' unlocks {string.Join(", ", unlocked)}. Add them again without --unlock-with, or with another store, first. Nothing was changed.");
        }

        ((JsonArray)root[StoresField]!).Remove(node);
        if (string.Equals(root[DefaultField]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase))
        {
            root.Remove(DefaultField);
        }

        Save(root);
        return true;
    }

    private void RequireUnlocker(string name, string unlockWith)
    {
        var stores = Load();
        var current = unlockWith;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
        while (current is not null)
        {
            if (!seen.Add(current))
            {
                throw new VaultException($"Store '{unlockWith}' can't unlock '{name}': '{name}' already unlocks it, directly or through other stores.");
            }

            var unlocker = stores.FirstOrDefault(store => string.Equals(store.Name, current, StringComparison.OrdinalIgnoreCase))
                ?? throw new VaultException($"No store named '{current}' on this PC. Add it first with lv store add {current} <target>.");
            current = unlocker.UnlockWith;
        }
    }

    private static void RequireFile(string path, string what)
    {
        if (!File.Exists(Environment.ExpandEnvironmentVariables(path)))
        {
            throw new VaultException($"The {what} '{path}' does not exist.");
        }
    }

    private static IEnumerable<JsonObject> Stores(JsonObject root) =>
        ((JsonArray)root[StoresField]!).OfType<JsonObject>().Where(node => node[NameField] is not null && node[TargetField] is not null);

    private static JsonObject? FindNode(JsonObject root, string name) =>
        Stores(root).FirstOrDefault(node =>
            string.Equals(node[NameField]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase));

    private JsonObject ReadRoot()
    {
        if (!File.Exists(RegistryPath))
        {
            var legacy = Path.Combine(Path.GetDirectoryName(RegistryPath)!, LegacyFileName);
            if (!File.Exists(legacy))
            {
                return new JsonObject { [SchemaField] = CurrentSchema, [StoresField] = new JsonArray() };
            }

            var converted = FromLegacy(Parse(legacy, LegacyDatabasesField));
            WithoutKeyring(converted);
            Save(converted);
            return converted;
        }

        var root = Parse(RegistryPath, StoresField);
        if (WithoutKeyring(root))
        {
            Save(root);
        }

        return root;
    }

    private static bool WithoutKeyring(JsonObject root)
    {
        if (root[LegacyKeyringField] is not JsonValue value)
        {
            return false;
        }

        var keyring = value.GetValue<string>();
        root.Remove(LegacyKeyringField);
        if (FindNode(root, keyring) is not { } ring)
        {
            return true;
        }

        foreach (var store in Stores(root).Where(store => !ReferenceEquals(store, ring) && store[UnlockWithField] is null))
        {
            store[UnlockWithField] = ring[NameField]!.GetValue<string>();
        }

        return true;
    }

    private static JsonObject FromLegacy(JsonObject legacy)
    {
        var stores = new JsonArray();
        foreach (var database in ((JsonArray)legacy[LegacyDatabasesField]!).OfType<JsonObject>())
        {
            if (database[NameField] is null || database[LegacyPathField] is null)
            {
                continue;
            }

            var store = new JsonObject
            {
                [NameField] = database[NameField]!.GetValue<string>(),
                [TypeField] = StoreTypes.KeePass,
                [TargetField] = database[LegacyPathField]!.GetValue<string>(),
            };
            foreach (var field in new[] { KeyFileField, IdField })
            {
                if (database[field] is JsonValue value)
                {
                    store[field] = value.GetValue<string>();
                }
            }

            stores.Add(store);
        }

        var root = new JsonObject { [SchemaField] = CurrentSchema, [StoresField] = stores };
        foreach (var field in new[] { DefaultField, LegacyKeyringField })
        {
            if (legacy[field] is JsonValue value)
            {
                root[field] = value.GetValue<string>();
            }
        }

        return root;
    }

    private static JsonObject Parse(string path, string listField)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new VaultException($"'{path}' is not a JSON object.");
            if (root[listField] is not JsonArray)
            {
                throw new VaultException($"'{path}' has no '{listField}' array.");
            }

            return root;
        }
        catch (JsonException ex)
        {
            throw new VaultException($"'{path}' is not valid JSON.", ex);
        }
    }

    private void Save(JsonObject root)
    {
        var text = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        SealedFile.Write(RegistryPath, text, written => JsonNode.Parse(written), overwrite: true);
    }
}
