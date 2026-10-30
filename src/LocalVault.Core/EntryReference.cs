using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace LocalVault.Core;

public enum EntryPart
{
    Password,
    User,
    Totp,
    Url,
}

public readonly record struct EntryReference
{
    public const char PartSeparator = '#';

    private const string UrlPart = "url";

    public EntryReference(EntryName name, EntryPart part = EntryPart.Password, int urlNumber = 1)
    {
        Name = name;
        Part = part;
        UrlNumber = part == EntryPart.Url ? urlNumber : 1;
    }

    public EntryName Name { get; }

    public EntryPart Part { get; }

    public int UrlNumber { get; }

    public bool IsSecret => Part is EntryPart.Password or EntryPart.Totp;

    public static EntryReference Parse(string value)
    {
        if (TryParse(value, out var reference))
        {
            return reference;
        }

        var separator = value.IndexOf(PartSeparator, StringComparison.Ordinal);
        if (separator >= 0 && EntryName.TryParse(value[..separator], out _))
        {
            throw new VaultException(
                $"'{value}' names no part. Use {value[..separator]}#user, #password, #totp, #url or #url2, #url3...");
        }

        return new EntryReference(EntryName.Parse(separator < 0 ? value : value[..separator]));
    }

    public static bool TryParse([NotNullWhen(true)] string? value, out EntryReference reference)
    {
        reference = default;
        if (value is null)
        {
            return false;
        }

        var separator = value.IndexOf(PartSeparator, StringComparison.Ordinal);
        if (!EntryName.TryParse(separator < 0 ? value : value[..separator], out var name))
        {
            return false;
        }

        if (separator < 0)
        {
            reference = new EntryReference(name);
            return true;
        }

        if (!TryParsePart(value[(separator + 1)..], out var part, out var urlNumber))
        {
            return false;
        }

        reference = new EntryReference(name, part, urlNumber);
        return true;
    }

    public bool Equals(EntryReference other) => Name.Equals(other.Name) && Part == other.Part && UrlNumber == other.UrlNumber;

    public override int GetHashCode() => HashCode.Combine(Name, Part, UrlNumber);

    public override string ToString() => Part switch
    {
        EntryPart.Password => Name.FullName,
        EntryPart.Url when UrlNumber == 1 => $"{Name}{PartSeparator}{UrlPart}",
        EntryPart.Url => $"{Name}{PartSeparator}{UrlPart}{UrlNumber.ToString(CultureInfo.InvariantCulture)}",
        _ => $"{Name}{PartSeparator}{Part.ToString().ToLowerInvariant()}",
    };

    private static bool TryParsePart(string text, out EntryPart part, out int urlNumber)
    {
        urlNumber = 1;
        part = EntryPart.Password;
        switch (text.ToLowerInvariant())
        {
            case "password":
                return true;
            case "user":
                part = EntryPart.User;
                return true;
            case "totp":
                part = EntryPart.Totp;
                return true;
            case UrlPart:
                part = EntryPart.Url;
                return true;
        }

        if (text.StartsWith(UrlPart, StringComparison.OrdinalIgnoreCase)
            && text[UrlPart.Length..].All(char.IsAsciiDigit)
            && int.TryParse(text[UrlPart.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out urlNumber)
            && urlNumber >= 1)
        {
            part = EntryPart.Url;
            return true;
        }

        return false;
    }
}
