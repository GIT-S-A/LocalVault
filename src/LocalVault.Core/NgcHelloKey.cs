using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace LocalVault.Core;

internal sealed partial class NgcHelloKey : IHelloKey
{
    public const string Provider = "Microsoft Passport Key Storage Provider";

    private const string KeyLeaf = "LocalVault/Hello/LocalVault-Hello";
    private const int PadPkcs1 = 2;
    private const int OverwriteKey = 0x80;
    private const int AuthMandatory = 1;
    private const int AllUsages = 0x00ffffff;
    private const int HardwareFlag = 1;
    private const int RemoteSession = 0x1000;
    private const int RsaKeyBytes = 256;
    private const int BadKeyset = unchecked((int)0x80090016);
    private const int NotFound = unchecked((int)0x80090011);
    private const int UserCancelled = unchecked((int)0x80090036);
    private const int Cancelled = unchecked((int)0x800704C7);

    public string ProviderName => Provider;

    public HelloState State => Probe(out _);

    public string? UnavailableReason => Probe(out var reason) == HelloState.Unavailable ? reason : null;

    public bool IsHardware
    {
        get
        {
            using var key = OpenKey();
            Span<byte> value = stackalloc byte[4];
            return GetProperty(key.Handle, "NgcKeyImplType", value) == 0 && (BitConverter.ToInt32(value) & HardwareFlag) != 0;
        }
    }

    public string KeyId => HelloKeys.ComputeKeyId(PublicKey);

    public RSAParameters PublicKey
    {
        get
        {
            using var key = OpenKey();
            var blob = new byte[1024];
            Check(NCryptExportKey(key.Handle, 0, "RSAPUBLICBLOB", 0, blob, blob.Length, out var length, 0), "read the Windows Hello public key");
            return ParsePublicBlob(blob.AsSpan(0, length));
        }
    }

    public void Create(string message)
    {
        if (Probe(out var reason) == HelloState.Unavailable)
        {
            throw new HelloException($"Windows Hello is not available: {reason}.");
        }

        using var provider = OpenProvider();
        Check(NCryptCreatePersistedKey(provider.Handle, out var created, "RSA", KeyName, 0, OverwriteKey), "create the Windows Hello key");
        using var key = new NcryptHandle(created);
        SetInt(key.Handle, "Length", RsaKeyBytes * 8);
        SetInt(key.Handle, "Key Usage", AllUsages);
        SetInt(key.Handle, "Export Policy", 0);
        SetInt(key.Handle, "NgcCacheType", AuthMandatory);
        SetPrompt(key.Handle, message);
        var status = NCryptFinalizeKey(key.Handle, 0);
        if (status is UserCancelled or Cancelled)
        {
            _ = NCryptDeleteKey(key.Handle, 0);
            key.Release();
            throw new HelloCancelledException();
        }

        Check(status, "create the Windows Hello key");
    }

    public byte[] Decrypt(ReadOnlySpan<byte> wrapped, string message)
    {
        if (Probe(out var reason) == HelloState.Unavailable)
        {
            throw new HelloException($"Windows Hello is not available: {reason}.");
        }

        using var key = OpenKey();
        SetPrompt(key.Handle, message);
        var output = new byte[RsaKeyBytes];
        try
        {
            var status = NCryptDecrypt(key.Handle, wrapped.ToArray(), wrapped.Length, 0, output, output.Length, out var length, PadPkcs1);
            if (status is UserCancelled or Cancelled)
            {
                throw new HelloCancelledException();
            }

            Check(status, "decrypt with the Windows Hello key");
            return output.AsSpan(0, length).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(output);
        }
    }

    private static string KeyName => $"{WindowsIdentity.GetCurrent().User!.Value}//{KeyLeaf}";

    private static HelloState Probe(out string? reason)
    {
        reason = null;
        if (GetSystemMetrics(RemoteSession) != 0)
        {
            reason = "this is a remote desktop session";
            return HelloState.Unavailable;
        }

        var status = NCryptOpenStorageProvider(out var provider, Provider, 0);
        if (status != 0)
        {
            reason = $"the Windows Hello key provider can't be opened (0x{status:X8})";
            return HelloState.Unavailable;
        }

        using var providerHandle = new NcryptHandle(provider);
        status = NCryptOpenKey(provider, out var key, KeyName, 0, 0);
        if (status == 0)
        {
            _ = NCryptFreeObject(key);
            return HelloState.Ready;
        }

        if (status is BadKeyset or NotFound)
        {
            return HelloState.None;
        }

        reason = $"the Windows Hello key can't be opened (0x{status:X8})";
        return HelloState.Unavailable;
    }

    private static NcryptHandle OpenProvider()
    {
        Check(NCryptOpenStorageProvider(out var provider, Provider, 0), "open the Windows Hello key provider");
        return new NcryptHandle(provider);
    }

    private static NcryptHandle OpenKey()
    {
        using var provider = OpenProvider();
        var status = NCryptOpenKey(provider.Handle, out var key, KeyName, 0, 0);
        if (status is BadKeyset or NotFound)
        {
            throw new HelloException("Windows Hello key missing.");
        }

        Check(status, "open the Windows Hello key");
        return new NcryptHandle(key);
    }

    private static void SetPrompt(nint key, string message)
    {
        var window = GetConsoleWindow();
        if (window == 0)
        {
            window = GetForegroundWindow();
        }

        var handle = BitConverter.GetBytes((long)window);
        _ = NCryptSetProperty(key, "HWND Handle", handle, handle.Length, 0);
        var text = Encoding.Unicode.GetBytes(message + "\0");
        _ = NCryptSetProperty(key, "Use Context", text, text.Length, 0);
    }

    private static void SetInt(nint key, string property, int value)
    {
        var bytes = BitConverter.GetBytes(value);
        Check(NCryptSetProperty(key, property, bytes, bytes.Length, 0), $"set '{property}' on the Windows Hello key");
    }

    private static int GetProperty(nint handle, string property, Span<byte> value)
    {
        var buffer = new byte[value.Length];
        var status = NCryptGetProperty(handle, property, buffer, buffer.Length, out var length, 0);
        if (status == 0)
        {
            buffer.AsSpan(0, Math.Min(length, value.Length)).CopyTo(value);
        }

        return status;
    }

    private static void Check(int status, string action)
    {
        if (status != 0)
        {
            throw new HelloException($"Windows Hello couldn't {action} (0x{status:X8}).");
        }
    }

    internal static RSAParameters ParsePublicBlob(ReadOnlySpan<byte> blob)
    {
        var exponentLength = BitConverter.ToInt32(blob[8..]);
        var modulusLength = BitConverter.ToInt32(blob[12..]);
        return new RSAParameters
        {
            Exponent = blob.Slice(24, exponentLength).ToArray(),
            Modulus = blob.Slice(24 + exponentLength, modulusLength).ToArray(),
        };
    }

    private sealed class NcryptHandle(nint handle) : IDisposable
    {
        public nint Handle { get; private set; } = handle;

        public void Release() => Handle = 0;

        public void Dispose()
        {
            if (Handle != 0)
            {
                _ = NCryptFreeObject(Handle);
                Handle = 0;
            }
        }
    }

    [LibraryImport("ncrypt.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int NCryptOpenStorageProvider(out nint provider, string name, int flags);

    [LibraryImport("ncrypt.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int NCryptCreatePersistedKey(nint provider, out nint key, string algorithm, string name, int keySpec, int flags);

    [LibraryImport("ncrypt.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int NCryptOpenKey(nint provider, out nint key, string name, int keySpec, int flags);

    [LibraryImport("ncrypt.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int NCryptSetProperty(nint handle, string property, byte[] value, int length, int flags);

    [LibraryImport("ncrypt.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int NCryptGetProperty(nint handle, string property, byte[] value, int length, out int result, int flags);

    [LibraryImport("ncrypt.dll")]
    private static partial int NCryptFinalizeKey(nint key, int flags);

    [LibraryImport("ncrypt.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int NCryptExportKey(nint key, nint exportKey, string blobType, nint parameters, byte[] output, int length, out int result, int flags);

    [LibraryImport("ncrypt.dll")]
    private static partial int NCryptDecrypt(nint key, byte[] input, int inputLength, nint padding, byte[] output, int outputLength, out int result, int flags);

    [LibraryImport("ncrypt.dll")]
    private static partial int NCryptDeleteKey(nint key, int flags);

    [LibraryImport("ncrypt.dll")]
    private static partial int NCryptFreeObject(nint handle);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetConsoleWindow();

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);
}
