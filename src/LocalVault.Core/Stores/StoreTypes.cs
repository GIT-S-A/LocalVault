using System.Text.RegularExpressions;

namespace LocalVault.Core;

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
