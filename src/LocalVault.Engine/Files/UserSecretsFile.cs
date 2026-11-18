using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalVault.Engine;

public sealed record UserSecretsComparison(IReadOnlyList<string> Absent, IReadOnlyList<string> Differing, IReadOnlyList<string> Same);

public static class UserSecretsFile
{
    public const string AppDataVariable = "APPDATA";
    public const string FileName = "secrets.json";
    public const char KeySeparator = ':';

    // Same location rule as Microsoft.Extensions.Configuration.UserSecrets PathHelper on Windows.
    public static string PathFor(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var appData = Environment.GetEnvironmentVariable(AppDataVariable);
        if (string.IsNullOrEmpty(appData))
        {
            throw new VaultException($"{AppDataVariable} is not set, so the .NET user secrets folder can't be found.");
        }

        return Path.Combine(appData, "Microsoft", "UserSecrets", id, FileName);
    }

    public static Dictionary<string, JsonNode?> Read(string path)
    {
        var values = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return values;
        }

        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            return values;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new VaultException($"'{path}' is not valid JSON. Nothing was changed.", ex);
        }

        if (root is not JsonObject top)
        {
            throw new VaultException($"'{path}' is not a JSON object. Nothing was changed.");
        }

        Flatten(top, null, values);
        return values;
    }

    public static string Text(JsonNode? node) => node switch
    {
        null => string.Empty,
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => node.ToJsonString(),
    };

    public static UserSecretsComparison Compare(IReadOnlyDictionary<string, JsonNode?> current, IReadOnlyDictionary<string, string> wanted)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(wanted);
        var absent = new List<string>();
        var differing = new List<string>();
        var same = new List<string>();
        foreach (var (key, value) in wanted)
        {
            if (!current.TryGetValue(key, out var node))
            {
                absent.Add(key);
            }
            else if (string.Equals(Text(node), value, StringComparison.Ordinal))
            {
                same.Add(key);
            }
            else
            {
                differing.Add(key);
            }
        }

        return new UserSecretsComparison(absent, differing, same);
    }

    public static Dictionary<string, JsonNode?> Apply(IReadOnlyDictionary<string, JsonNode?> current, IReadOnlyDictionary<string, string> wanted)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(wanted);
        var result = new Dictionary<string, JsonNode?>(current, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in wanted)
        {
            result[key] = JsonValue.Create(value);
        }

        return result;
    }

    public static void Write(string path, IReadOnlyDictionary<string, JsonNode?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            File.Delete(path);
            var directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }

            return;
        }

        var root = new JsonObject();
        foreach (var (key, value) in values)
        {
            root[key] = value?.DeepClone();
        }

        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        try
        {
            SealedFile.WriteBytes(path, bytes, overwrite: true);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    private static void Flatten(JsonNode? node, string? prefix, Dictionary<string, JsonNode?> values)
    {
        switch (node)
        {
            case JsonObject members:
                foreach (var (name, child) in members)
                {
                    Flatten(child, prefix is null ? name : prefix + KeySeparator + name, values);
                }

                break;
            case JsonArray items:
                for (var i = 0; i < items.Count; i++)
                {
                    Flatten(items[i], prefix + KeySeparator + i.ToString(System.Globalization.CultureInfo.InvariantCulture), values);
                }

                break;
            default:
                if (prefix is not null)
                {
                    values[prefix] = node?.DeepClone();
                }

                break;
        }
    }
}
