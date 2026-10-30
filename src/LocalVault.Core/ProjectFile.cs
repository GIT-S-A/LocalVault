using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalVault.Core;

public sealed record ProjectVault(string Path, StoreReference? Store);

public sealed record ProjectStoreEntry(string Name, string? Target, string? Id, bool IsMirror);

public sealed record ProjectTemplate(string Path, string Template, string Vault, string Display);

public static class ProjectFile
{
    public const string FileName = "localvault.json";
    public const int CurrentSchema = 2;
    private const string SchemaField = "schema";
    private const string DefaultField = "default";
    private const string StoresField = "stores";
    private const string VaultsField = "vaults";
    private const string NameField = "name";
    private const string TypeField = "type";
    private const string TargetField = "target";
    private const string IdField = "id";
    private const string PathField = "path";
    private const string StoreField = "store";
    private const string MirrorField = "mirror";
    private const string TemplatesField = "templates";
    private const string TemplateField = "template";
    private const string VaultField = "vault";
    private const string LegacyRoleField = "role";
    private const string LegacyLabelField = "label";
    private const string LegacyPrimaryRole = "primary";

    public static string? PathFor(VaultLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return location.GitRoot is null ? null : System.IO.Path.Combine(location.GitRoot, FileName);
    }

    public static IReadOnlyList<StoreDefinition> ReadStores(VaultLocation location) =>
        Read(location) is { } root ? Stores(root).Select(ToDefinition).ToList() : [];

    public static IReadOnlyList<ProjectStoreEntry> ReadEntries(VaultLocation location) =>
        Read(location) is { } root
            ? Entries(root).Select(node => new ProjectStoreEntry(
                node[NameField]!.GetValue<string>(),
                node[TargetField]?.GetValue<string>(),
                node[IdField]?.GetValue<string>(),
                node[MirrorField]?.GetValue<bool>() == true)).ToList()
            : [];

    public static StoreReference? ReadDefault(VaultLocation location) =>
        Read(location) is { } root ? ToReference(root[DefaultField] as JsonObject) : null;

    public static IReadOnlyList<ProjectVault> ReadVaults(VaultLocation location)
    {
        if (Read(location) is not { } root)
        {
            return [];
        }

        var inside = System.IO.Path.TrimEndingDirectorySeparator(location.GitRoot!) + System.IO.Path.DirectorySeparatorChar;
        return Vaults(root)
            .Select(node => new ProjectVault(FullPath(location, node[PathField]!.GetValue<string>()), ToReference(node)))
            .Where(vault => vault.Path.StartsWith(inside, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static StoreReference? StoreFor(VaultLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        var own = ReadVaults(location).FirstOrDefault(vault => SamePath(vault.Path, location.Path))?.Store;
        return own ?? ReadDefault(location);
    }

    public static IReadOnlyList<ProjectTemplate> ReadTemplates(VaultLocation location)
    {
        if (Read(location) is not { } root)
        {
            return [];
        }

        return Templates(root)
            .Select(node => new ProjectTemplate(
                StoredPathRules.Resolve(location.GitRoot!, node[PathField]!.GetValue<string>()),
                StoredPathRules.Resolve(location.GitRoot!, node[TemplateField]!.GetValue<string>()),
                node[VaultField]?.GetValue<string>() is { } vault ? FullPath(location, vault) : location.Path,
                node[PathField]!.GetValue<string>()))
            .ToList();
    }

    public static string AddTemplate(VaultLocation location, string path, string template)
    {
        var file = RequirePath(location);
        var relative = StoredPathRules.FromFullPath(location.GitRoot!, path);
        var templateRelative = StoredPathRules.FromFullPath(location.GitRoot!, template);
        var root = Read(location) ?? NewRoot();
        if (root[TemplatesField] is not JsonArray templates)
        {
            templates = new JsonArray();
            root[TemplatesField] = templates;
        }

        if (Templates(root).FirstOrDefault(node => SameRelative(node, relative)) is { } existing)
        {
            templates.Remove(existing);
        }

        templates.Add(new JsonObject { [PathField] = relative, [TemplateField] = templateRelative, [VaultField] = Relative(location) });
        Write(file, root);
        return file;
    }

    public static bool RemoveTemplate(VaultLocation location, string path)
    {
        var file = RequirePath(location);
        var relative = StoredPathRules.FromFullPath(location.GitRoot!, path);
        if (Read(location) is not { } root || Templates(root).FirstOrDefault(node => SameRelative(node, relative)) is not { } node)
        {
            return false;
        }

        ((JsonArray)root[TemplatesField]!).Remove(node);
        Write(file, root);
        return true;
    }

    public static bool AddVault(VaultLocation location)
    {
        var path = PathFor(location);
        if (path is null || ReadVaults(location).Any(vault => SamePath(vault.Path, location.Path)))
        {
            return false;
        }

        var root = Read(location) ?? NewRoot();
        ((JsonArray)root[VaultsField]!).Add(new JsonObject { [PathField] = Relative(location) });
        Write(path, root);
        return true;
    }

    public static string AddStore(VaultLocation location, string name, string? target, bool makeDefault, bool mirror = false)
    {
        StoreTypes.RequireValidName(name);
        var type = target is null ? null : StoreTypes.Of(target);
        var path = RequirePath(location);
        var root = Read(location) ?? NewRoot();
        var node = FindStore(root, name);
        if (node is null)
        {
            node = new JsonObject { [NameField] = name };
            ((JsonArray)root[StoresField]!).Add(node);
        }

        if (!string.Equals(node[TargetField]?.GetValue<string>(), target, StringComparison.OrdinalIgnoreCase))
        {
            node.Remove(IdField);
        }

        if (target is null)
        {
            node.Remove(TypeField);
            node.Remove(TargetField);
        }
        else
        {
            node[TypeField] = type;
            node[TargetField] = target;
        }

        if (mirror)
        {
            node[MirrorField] = true;
        }
        else
        {
            node.Remove(MirrorField);
        }

        if (makeDefault)
        {
            root[DefaultField] = new JsonObject { [StoreField] = node[NameField]!.GetValue<string>() };
        }

        Write(path, root);
        return path;
    }

    public static void ChangeStore(VaultLocation location, string name, bool? mirror, bool makeDefault)
    {
        var path = RequirePath(location);
        var root = Read(location) ?? throw new VaultException($"No store named '{name}' in {FileName}.");
        var node = FindStore(root, name) ?? throw new VaultException($"No store named '{name}' in {FileName}.");
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
            root[DefaultField] = new JsonObject { [StoreField] = node[NameField]!.GetValue<string>() };
        }

        Write(path, root);
    }

    public static bool RemoveStore(VaultLocation location, string name)
    {
        var path = RequirePath(location);
        if (Read(location) is not { } root || FindStore(root, name) is not { } node)
        {
            return false;
        }

        var users = new List<string>();
        if (Names(root[DefaultField] as JsonObject, name))
        {
            users.Add("the project default");
        }

        users.AddRange(Vaults(root).Where(vault => Names(vault, name)).Select(vault => vault[PathField]!.GetValue<string>()));
        if (users.Count > 0)
        {
            throw new VaultException(
                $"Store '{name}' is used by {string.Join(", ", users)}. Give them another store with lv store use <name> first. Nothing was changed.");
        }

        ((JsonArray)root[StoresField]!).Remove(node);
        Write(path, root);
        return true;
    }

    public static string Assign(VaultLocation location, StoreReference reference, bool wholeRepository)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var path = RequirePath(location);
        var root = Read(location) ?? NewRoot();
        if (wholeRepository)
        {
            root[DefaultField] = ReferenceNode(new JsonObject(), reference);
            foreach (var vault in Vaults(root))
            {
                vault.Remove(StoreField);
                vault.Remove(IdField);
            }
        }
        else
        {
            var vault = Vaults(root).FirstOrDefault(node => SamePath(FullPath(location, node[PathField]!.GetValue<string>()), location.Path));
            if (vault is null)
            {
                vault = new JsonObject { [PathField] = Relative(location) };
                ((JsonArray)root[VaultsField]!).Add(vault);
            }

            ReferenceNode(vault, reference);
        }

        Write(path, root);
        return path;
    }

    public static void RecordStoreId(VaultLocation location, string name, string id)
    {
        var path = RequirePath(location);
        if (Read(location) is not { } root || FindStore(root, name) is not { } node
            || string.Equals(node[IdField]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        node[IdField] = id;
        Write(path, root);
    }

    private static JsonObject ReferenceNode(JsonObject node, StoreReference reference)
    {
        node.Remove(StoreField);
        node.Remove(IdField);
        if (reference.Name is not null)
        {
            node[StoreField] = reference.Name;
        }

        if (reference.Id is not null)
        {
            node[IdField] = reference.Id;
        }

        return node;
    }

    private static StoreDefinition ToDefinition(JsonObject node) => new(
        node[NameField]!.GetValue<string>(),
        node[TypeField]?.GetValue<string>() ?? StoreTypes.KeePass,
        node[TargetField]!.GetValue<string>(),
        null,
        StoreScope.Project,
        node[IdField]?.GetValue<string>(),
        IsMirror: node[MirrorField]?.GetValue<bool>() == true);

    private static StoreReference? ToReference(JsonObject? node)
    {
        var name = node?[StoreField]?.GetValue<string>();
        var id = node?[IdField]?.GetValue<string>();
        return name is null && id is null ? null : new StoreReference(name, id);
    }

    private static bool Names(JsonObject? node, string name) =>
        string.Equals(node?[StoreField]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase);

    private static string RequirePath(VaultLocation location) => PathFor(location)
        ?? throw new VaultException("A project file needs a git repository; this vault is not in one.");

    private static string Relative(VaultLocation location) =>
        System.IO.Path.GetRelativePath(location.GitRoot!, location.Path).Replace(System.IO.Path.DirectorySeparatorChar, '/');

    private static string FullPath(VaultLocation location, string relative) =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(location.GitRoot!, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));

    private static bool SamePath(string a, string b) =>
        string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static JsonObject NewRoot() => new() { [SchemaField] = CurrentSchema, [StoresField] = new JsonArray(), [VaultsField] = new JsonArray() };

    private static void Write(string path, JsonObject root)
    {
        root[SchemaField] = CurrentSchema;
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static IEnumerable<JsonObject> Stores(JsonObject root) =>
        ((JsonArray)root[StoresField]!).OfType<JsonObject>().Where(node => node[NameField] is not null && node[TargetField] is not null);

    private static IEnumerable<JsonObject> Vaults(JsonObject root) =>
        ((JsonArray)root[VaultsField]!).OfType<JsonObject>().Where(node => node[PathField] is not null);

    private static IEnumerable<JsonObject> Templates(JsonObject root) =>
        (root[TemplatesField] as JsonArray ?? []).OfType<JsonObject>().Where(node => node[PathField] is not null && node[TemplateField] is not null);

    private static bool SameRelative(JsonObject node, string relative) =>
        string.Equals(node[PathField]!.GetValue<string>().Replace('\\', '/'), relative, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<JsonObject> Entries(JsonObject root) =>
        ((JsonArray)root[StoresField]!).OfType<JsonObject>().Where(node => node[NameField] is not null);

    private static JsonObject? FindStore(JsonObject root, string name) =>
        Entries(root).FirstOrDefault(node => string.Equals(node[NameField]!.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase));

    private static JsonObject? Read(VaultLocation location)
    {
        var path = PathFor(location);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new VaultException($"'{path}' is not a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new VaultException($"'{path}' is not valid JSON.", ex);
        }

        if (root[SchemaField] is JsonValue schema && schema.TryGetValue<int>(out var number) && number > CurrentSchema)
        {
            throw new VaultException($"'{path}' was written by a newer LocalVault (schema {number}). Update the module with lv update.");
        }

        return Normalized(root, path);
    }

    private static JsonObject Normalized(JsonObject root, string path)
    {
        if (root[StoresField] is null)
        {
            root[StoresField] = new JsonArray();
        }

        if (root[VaultsField] is null)
        {
            root[VaultsField] = new JsonArray();
        }

        if (root[StoresField] is not JsonArray stores || root[VaultsField] is not JsonArray vaults)
        {
            throw new VaultException($"'{path}': 'stores' and 'vaults' must be lists.");
        }

        foreach (var primary in stores.OfType<JsonObject>()
                     .Where(node => string.Equals(node[LegacyRoleField]?.GetValue<string>(), LegacyPrimaryRole, StringComparison.Ordinal))
                     .ToList())
        {
            if (root[DefaultField] is null)
            {
                root[DefaultField] = ReferenceNode(
                    new JsonObject(),
                    new StoreReference(primary[LegacyLabelField]?.GetValue<string>(), primary[IdField]?.GetValue<string>()));
            }

            stores.Remove(primary);
        }

        for (var i = 0; i < vaults.Count; i++)
        {
            if (vaults[i] is JsonValue value && value.TryGetValue<string>(out var relative))
            {
                vaults[i] = new JsonObject { [PathField] = relative };
            }
        }

        return root;
    }
}
