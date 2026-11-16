using System.Text.RegularExpressions;

namespace LocalVault.Core;

public sealed partial record ItemReference(string StoreId, string ItemId, string? Attachment, string Title)
{
    public string Link => Attachment is null ? $"{StoreId}/{ItemId}" : $"{StoreId}/{ItemId}/{Attachment}";

    public bool SameTarget(ItemReference? other) =>
        other is not null
        && string.Equals(StoreId, other.StoreId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(ItemId, other.ItemId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Attachment, other.Attachment, StringComparison.Ordinal);

    public static ItemReference ParseLink(string link, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(link);
        var parts = link.Split('/', 3);
        if (parts.Length < 2 || !IsUuid(parts[0]) || !IsUuid(parts[1]) || (parts.Length == 3 && parts[2].Length == 0))
        {
            throw new VaultException($"'{fieldName}' must be <store id>/<entry id>[/<attachment>], not '{link}'");
        }

        return new ItemReference(parts[0], parts[1], parts.Length == 3 ? parts[2] : null, string.Empty);
    }

    public static bool IsUuid(string text) => text is not null && UuidPattern().IsMatch(text);

    [GeneratedRegex("^[0-9A-Fa-f]{32}$")]
    private static partial Regex UuidPattern();
}

public sealed record ItemLink(EntryName Name, ItemReference Reference, string Shape, bool IsPersonal);

public sealed record UnresolvedReference(EntryName Name, string Reason, bool IsPersonal);

public sealed record ReferenceStore(IStore Store, string Name);

public interface IReferenceStores
{
    // Throws VaultException whose message completes "<name> not loaded: ".
    ReferenceStore Open(ItemReference reference, bool personal);
}
