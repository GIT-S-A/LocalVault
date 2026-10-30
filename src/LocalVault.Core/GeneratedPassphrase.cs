using System.Security.Cryptography;

namespace LocalVault.Core;

public static class GeneratedPassphrase
{
    public const int Length = 32;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static char[] Create() => RandomNumberGenerator.GetItems<char>(Alphabet, Length);
}