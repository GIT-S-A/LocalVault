using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace LocalVault.PowerShell;

internal static class SecretText
{
    public static char[] ToChars(SecureString value)
    {
        var pointer = Marshal.SecureStringToBSTR(value);
        try
        {
            var chars = new char[value.Length];
            Marshal.Copy(pointer, chars, 0, value.Length);
            return chars;
        }
        finally
        {
            Marshal.ZeroFreeBSTR(pointer);
        }
    }

    public static byte[] ToUtf8(SecureString value)
    {
        var chars = ToChars(value);
        try
        {
            return Encoding.UTF8.GetBytes(chars);
        }
        finally
        {
            Array.Clear(chars);
        }
    }

    public static SecureString FromUtf8(byte[] value)
    {
        var chars = Encoding.UTF8.GetChars(value);
        try
        {
            return FromChars(chars);
        }
        finally
        {
            Array.Clear(chars);
        }
    }

    public static SecureString FromChars(ReadOnlySpan<char> chars)
    {
        var secure = new SecureString();
        foreach (var c in chars)
        {
            secure.AppendChar(c);
        }

        secure.MakeReadOnly();
        return secure;
    }

    public static string ToPlain(SecureString value)
    {
        var chars = ToChars(value);
        try
        {
            return new string(chars);
        }
        finally
        {
            Array.Clear(chars);
        }
    }

    public static bool AreEqual(SecureString first, SecureString second)
    {
        var a = ToChars(first);
        var b = ToChars(second);
        try
        {
            return a.AsSpan().SequenceEqual(b);
        }
        finally
        {
            Array.Clear(a);
            Array.Clear(b);
        }
    }
}
