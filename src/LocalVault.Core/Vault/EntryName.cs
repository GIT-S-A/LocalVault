using System.Diagnostics.CodeAnalysis;

namespace LocalVault.Core;

public readonly record struct EntryName
{
    public const char ScopeSeparator = '/';

    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;

    private EntryName(string? scope, string key)
    {
        Scope = scope;
        Key = key;
    }

    public string? Scope { get; }

    public string Key { get; }

    public string FullName => Scope is null ? Key : Scope + ScopeSeparator + Key;

    public bool IsScoped => Scope is not null;

    public static EntryName Parse(string value)
    {
        if (!TryParse(value, out var name))
        {
            throw new VaultException(
                $"'{value}' is not a valid name. Use [scope/]key with letters, digits and . _ : - only.");
        }

        return name;
    }

    public static bool TryParse([NotNullWhen(true)] string? value, out EntryName name)
    {
        name = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var separator = value.IndexOf(ScopeSeparator, StringComparison.Ordinal);
        if (separator < 0)
        {
            if (!IsValidSegment(value))
            {
                return false;
            }

            name = new EntryName(null, value);
            return true;
        }

        var scope = value[..separator];
        var key = value[(separator + 1)..];
        if (!IsValidSegment(scope) || !IsValidSegment(key))
        {
            return false;
        }

        name = new EntryName(scope, key);
        return true;
    }

    public static EntryName Combine(string scope, string key)
    {
        if (!IsValidSegment(scope) || !IsValidSegment(key))
        {
            throw new VaultException($"'{scope}{ScopeSeparator}{key}' is not a valid name.");
        }

        return new EntryName(scope, key);
    }

    public static bool IsValidSegment(string segment)
    {
        if (segment.Length == 0)
        {
            return false;
        }

        foreach (var c in segment)
        {
            if (!IsValidCharacter(c))
            {
                return false;
            }
        }

        return true;
    }

    public bool Equals(EntryName other) => Comparer.Equals(FullName, other.FullName);

    public override int GetHashCode() => Comparer.GetHashCode(FullName);

    public override string ToString() => FullName;

    private static bool IsValidCharacter(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '-' or '@' or '+';
}
