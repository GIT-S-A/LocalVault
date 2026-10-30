using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;

namespace System.Security.Cryptography
{
    internal enum MemoryProtectionScope
    {
        SameProcess = 0,
        CrossProcess = 1,
        SameLogon = 2,
    }

    internal static partial class ProtectedMemory
    {
        private const int BlockSize = 16;

        public static void Protect(byte[] userData, MemoryProtectionScope scope)
        {
            Require(userData);
            if (!CryptProtectMemory(userData, (uint)userData.Length, (uint)scope))
            {
                throw new CryptographicException(Marshal.GetLastPInvokeError());
            }
        }

        public static void Unprotect(byte[] encryptedData, MemoryProtectionScope scope)
        {
            Require(encryptedData);
            if (!CryptUnprotectMemory(encryptedData, (uint)encryptedData.Length, (uint)scope))
            {
                throw new CryptographicException(Marshal.GetLastPInvokeError());
            }
        }

        private static void Require(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (data.Length == 0 || data.Length % BlockSize != 0)
            {
                throw new CryptographicException($"Length must be a positive multiple of {BlockSize}.");
            }
        }

        [LibraryImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CryptProtectMemory(byte[] pDataIn, uint cbDataIn, uint dwFlags);

        [LibraryImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CryptUnprotectMemory(byte[] pDataIn, uint cbDataIn, uint dwFlags);
    }
}

namespace KeePassLib.Serialization
{
    internal static class FileAccessControl
    {
        extension(File)
        {
            public static FileSecurity GetAccessControl(string path, AccessControlSections sections) =>
                new FileInfo(path).GetAccessControl(sections);

            public static void SetAccessControl(string path, FileSecurity security) =>
                new FileInfo(path).SetAccessControl(security);
        }
    }
}
