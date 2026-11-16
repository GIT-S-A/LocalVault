using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalVault.Core;

public sealed class PcSettings(string path)
{
    public const int CurrentSchema = 1;
    public const string FileName = "settings.json";
    private const string SchemaField = "schema";
    private const string SshDirField = "sshDir";
    private const string SshIncludeField = "sshInclude";

    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(StoreRegistry.DefaultPath)!, FileName);

    public string SettingsPath { get; } = path;

    public string? SshDir
    {
        get => ReadRoot()[SshDirField] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        set
        {
            if (value is not null && !Path.IsPathFullyQualified(value))
            {
                throw new VaultException($"'{value}' is not an absolute folder: give the full path of the folder holding ssh.exe.");
            }

            Change(root => Set(root, SshDirField, value is null ? null : JsonValue.Create(value)));
        }
    }

    public bool? SshInclude
    {
        get => ReadRoot()[SshIncludeField] is JsonValue value && value.TryGetValue<bool>(out var include) ? include : null;
        set => Change(root => Set(root, SshIncludeField, value is null ? null : JsonValue.Create(value.Value)));
    }

    private static void Set(JsonObject root, string field, JsonNode? value)
    {
        if (value is null)
        {
            root.Remove(field);
        }
        else
        {
            root[field] = value;
        }
    }

    private void Change(Action<JsonObject> change)
    {
        var root = ReadRoot();
        change(root);
        var text = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        SealedFile.Write(SettingsPath, text, written => JsonNode.Parse(written), overwrite: true);
    }

    private JsonObject ReadRoot()
    {
        if (!File.Exists(SettingsPath))
        {
            return new JsonObject { [SchemaField] = CurrentSchema };
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject
                ?? throw new VaultException($"'{SettingsPath}' is not a JSON object.");
            if (root[SchemaField] is JsonValue schema && schema.TryGetValue<int>(out var number) && number > CurrentSchema)
            {
                throw new VaultException($"'{SettingsPath}' was written by a newer LocalVault (schema {number}). Run lv update.");
            }

            return root;
        }
        catch (JsonException ex)
        {
            throw new VaultException($"'{SettingsPath}' is not valid JSON.", ex);
        }
    }
}
