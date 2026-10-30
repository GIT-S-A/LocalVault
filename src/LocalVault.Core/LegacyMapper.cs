using System.Text;
using System.Text.Json;

namespace LocalVault.Core;

public sealed record RefusedName(string Name, string Reason);

public sealed record LegacyMapping(
    IReadOnlyList<VaultEntry> Entries,
    IReadOnlyList<RefusedName> Refused,
    IReadOnlyList<IReadOnlyList<VaultEntry>> CaseClashes);

public static class LegacyMapper
{
    public const string FilePrefix = "__File:";
    private const string ReservedPrefix = "__";
    private const string SshPrefix = "__Ssh";

    public static LegacyMapping Map(IEnumerable<KeyValuePair<string, string>> values, DateTimeOffset created)
    {
        ArgumentNullException.ThrowIfNull(values);
        var mapped = new List<VaultEntry>();
        var refused = new List<RefusedName>();

        foreach (var (sourceName, value) in values)
        {
            if (string.IsNullOrEmpty(value))
            {
                refused.Add(new RefusedName(sourceName, "empty value"));
                continue;
            }

            if (sourceName.StartsWith(FilePrefix, StringComparison.Ordinal))
            {
                var fileName = sourceName[FilePrefix.Length..];
                if (!EntryName.TryParse(fileName, out var name) || name.IsScoped)
                {
                    refused.Add(new RefusedName(sourceName, "file name is not a valid entry name"));
                    continue;
                }

                try
                {
                    mapped.Add(VaultEntry.NewFile(name, Encoding.UTF8.GetBytes(value), fileName, created));
                }
                catch (VaultException)
                {
                    refused.Add(new RefusedName(sourceName, "file name is not a valid stored path"));
                }
                continue;
            }

            if (sourceName.StartsWith(SshPrefix, StringComparison.Ordinal))
            {
                refused.Add(new RefusedName(sourceName, "SSH keys belong in KeePass, not in the vault"));
                continue;
            }

            if (sourceName.StartsWith(ReservedPrefix, StringComparison.Ordinal))
            {
                refused.Add(new RefusedName(sourceName, "reserved name"));
                continue;
            }

            if (!EntryName.TryParse(sourceName, out var secretName))
            {
                refused.Add(new RefusedName(sourceName, "not a valid entry name"));
                continue;
            }

            mapped.Add(VaultEntry.NewSecret(secretName, Encoding.UTF8.GetBytes(value), created));
        }

        var groups = mapped.GroupBy(entry => entry.Name).ToList();
        var entries = groups.Where(group => group.Count() == 1).Select(group => group.Single()).ToList();
        var clashes = groups
            .Where(group => group.Count() > 1)
            .Select(group => (IReadOnlyList<VaultEntry>)group.ToList())
            .ToList();
        return new LegacyMapping(entries, refused, clashes);
    }

    public static IReadOnlyList<KeyValuePair<string, string>> ParseExportBody(ReadOnlySpan<byte> utf8Json)
    {
        var values = new List<KeyValuePair<string, string>>();
        var reader = new Utf8JsonReader(utf8Json);
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                throw new VaultException($"The {Envelope.LegacyMagic} body is not a JSON object.");
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString()!;
                reader.Read();
                var value = reader.TokenType switch
                {
                    JsonTokenType.String => reader.GetString()!,
                    JsonTokenType.Null => string.Empty,
                    _ => throw new VaultException($"The {Envelope.LegacyMagic} value of '{name}' is not a string."),
                };
                values.Add(new KeyValuePair<string, string>(name, value));
            }
        }
        catch (JsonException ex)
        {
            throw new VaultException($"The {Envelope.LegacyMagic} body is not valid JSON.", ex);
        }

        return values;
    }
}
