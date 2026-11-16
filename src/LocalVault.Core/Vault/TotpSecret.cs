using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LocalVault.Core;

public sealed class TotpSecret
{
    public const int DefaultDigits = 6;
    public const int DefaultPeriod = 30;
    public const string DefaultAlgorithm = "SHA1";
    public const int MinimumDigits = 6;
    public const int MaximumDigits = 8;

    private const string Scheme = "otpauth://";
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private static readonly string[] Algorithms = ["SHA1", "SHA256", "SHA512"];

    private TotpSecret(string secret, int digits, int period, string algorithm)
    {
        Secret = secret;
        Digits = digits;
        Period = period;
        Algorithm = algorithm;
    }

    public string Secret { get; }

    public int Digits { get; }

    public int Period { get; }

    public string Algorithm { get; }

    public string Canonical => string.Create(
        CultureInfo.InvariantCulture, $"secret={Secret}&algorithm={Algorithm}&digits={Digits}&period={Period}");

    public static TotpSecret Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        return trimmed.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase) ? FromUri(trimmed) : Create(trimmed, DefaultDigits, DefaultPeriod, DefaultAlgorithm);
    }

    public static TotpSecret Create(string base32Secret, int digits, int period, string algorithm)
    {
        ArgumentNullException.ThrowIfNull(base32Secret);
        ArgumentNullException.ThrowIfNull(algorithm);
        var secret = NormalizeBase32(base32Secret);
        if (digits is < MinimumDigits or > MaximumDigits)
        {
            throw new VaultException($"A TOTP code has {MinimumDigits} to {MaximumDigits} digits, not {digits}.");
        }

        if (period <= 0)
        {
            throw new VaultException($"A TOTP period must be a positive number of seconds, not {period}.");
        }

        var name = algorithm.ToUpperInvariant().Replace("-", string.Empty, StringComparison.Ordinal);
        if (name.StartsWith("HMAC", StringComparison.Ordinal))
        {
            name = name["HMAC".Length..];
        }

        if (!Algorithms.Contains(name))
        {
            throw new VaultException($"TOTP algorithm '{algorithm}' is not supported. Use SHA1, SHA256 or SHA512.");
        }

        return new TotpSecret(secret, digits, period, name);
    }

    public static TotpSecret FromKey(ReadOnlySpan<byte> key, int digits, int period, string algorithm) =>
        Create(EncodeBase32(key), digits, period, algorithm);

    public string ToUri(string label) => $"{Scheme}totp/{Uri.EscapeDataString(label)}?{Canonical}";

    public string Code(DateTimeOffset now)
    {
        var key = DecodeBase32(Secret);
        Span<byte> counter = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counter, now.ToUnixTimeSeconds() / Period);
        try
        {
            using var hmac = IncrementalHash.CreateHMAC(new HashAlgorithmName(Algorithm), key);
            hmac.AppendData(counter);
            var hash = hmac.GetHashAndReset();
            var offset = hash[^1] & 0x0F;
            var binary = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset)) & 0x7FFFFFFF;
            var code = binary % (int)Math.Pow(10, Digits);
            return code.ToString(CultureInfo.InvariantCulture).PadLeft(Digits, '0');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public byte[] Key() => DecodeBase32(Secret);

    private static TotpSecret FromUri(string text)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            throw new VaultException("The TOTP is not a valid otpauth:// address.");
        }

        if (!string.Equals(uri.Host, "totp", StringComparison.OrdinalIgnoreCase))
        {
            throw new VaultException($"Only time-based codes (otpauth://totp) are supported, not otpauth://{uri.Host}.");
        }

        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .GroupBy(pair => Uri.UnescapeDataString(pair[0]), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Length > 1 ? Uri.UnescapeDataString(group.Last()[1]) : string.Empty, StringComparer.OrdinalIgnoreCase);
        if (!query.TryGetValue("secret", out var secret) || secret.Length == 0)
        {
            throw new VaultException("The otpauth:// address has no secret.");
        }

        return Create(
            secret,
            ReadNumber(query, "digits", DefaultDigits),
            ReadNumber(query, "period", DefaultPeriod),
            query.TryGetValue("algorithm", out var algorithm) && algorithm.Length > 0 ? algorithm : DefaultAlgorithm);
    }

    private static int ReadNumber(Dictionary<string, string> query, string name, int fallback)
    {
        if (!query.TryGetValue(name, out var text) || text.Length == 0)
        {
            return fallback;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new VaultException($"The otpauth:// address has a '{name}' that is not a number.");
    }

    private static string NormalizeBase32(string text)
    {
        var normalized = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is ' ' or '-' or '=')
            {
                continue;
            }

            var upper = char.ToUpperInvariant(c);
            if (!Base32Alphabet.Contains(upper, StringComparison.Ordinal))
            {
                throw new VaultException("The TOTP secret is not base32 (letters A-Z and digits 2-7). Give the otpauth:// address or the seed.");
            }

            normalized.Append(upper);
        }

        if (normalized.Length == 0)
        {
            throw new VaultException("The TOTP secret is empty.");
        }

        return normalized.ToString();
    }

    private static byte[] DecodeBase32(string secret)
    {
        var bytes = new byte[secret.Length * 5 / 8];
        int buffer = 0, bits = 0, index = 0;
        foreach (var c in secret)
        {
            buffer = (buffer << 5) | Base32Alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes[index++] = (byte)(buffer >> bits);
                buffer &= (1 << bits) - 1;
            }
        }

        return bytes;
    }

    private static string EncodeBase32(ReadOnlySpan<byte> key)
    {
        var text = new StringBuilder((key.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in key)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                text.Append(Base32Alphabet[(buffer >> bits) & 0x1F]);
            }

            buffer &= (1 << bits) - 1;
        }

        if (bits > 0)
        {
            text.Append(Base32Alphabet[(buffer << (5 - bits)) & 0x1F]);
        }

        return text.ToString();
    }
}
