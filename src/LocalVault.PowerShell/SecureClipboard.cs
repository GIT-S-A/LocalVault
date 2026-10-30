using System.ComponentModel;
using System.Runtime.InteropServices;
using LocalVault.Core;

namespace LocalVault.PowerShell;

internal static partial class SecureClipboard
{
    private const uint UnicodeText = 13;
    private const uint Moveable = 0x0002;
    private const int OpenAttempts = 10;
    private const int AccessDenied = 5;

    private static readonly object Gate = new();
    private static Timer? clearTimer;
    private static uint ownedSequence;
    private static bool exitHooked;

    public static void SetText(ReadOnlySpan<char> text, TimeSpan clearAfter)
    {
        lock (Gate)
        {
            Open();
            try
            {
                if (!EmptyClipboard())
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                SetData(UnicodeText, text);
                SetMarker("ExcludeClipboardContentFromMonitorProcessing");
                SetMarker("CanIncludeInClipboardHistory");
                SetMarker("CanUploadToCloudClipboard");
            }
            finally
            {
                CloseClipboard();
            }

            ownedSequence = GetClipboardSequenceNumber();
            clearTimer?.Dispose();
            clearTimer = new Timer(_ => ClearIfUnchanged(), null, clearAfter, Timeout.InfiniteTimeSpan);
            if (!exitHooked)
            {
                AppDomain.CurrentDomain.ProcessExit += (_, _) => ClearIfUnchanged();
                exitHooked = true;
            }
        }
    }

    private static void ClearIfUnchanged()
    {
        lock (Gate)
        {
            if (ownedSequence == 0 || GetClipboardSequenceNumber() != ownedSequence)
            {
                return;
            }

            try
            {
                Open();
                try
                {
                    EmptyClipboard();
                }
                finally
                {
                    CloseClipboard();
                }
            }
            catch (VaultException)
            {
                return;
            }

            ownedSequence = 0;
        }
    }

    private static void Open()
    {
        var error = 0;
        for (var attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                return;
            }

            error = Marshal.GetLastPInvokeError();
            if (error == AccessDenied)
            {
                break;
            }

            Thread.Sleep(50);
        }

        throw new VaultException(error == AccessDenied
            ? "Windows denies access to the clipboard: the session is locked or this process has no desktop."
            : "The clipboard is in use by another program. Try again.");
    }

    private static unsafe void SetData(uint format, ReadOnlySpan<char> text)
    {
        var bytes = (nuint)((text.Length + 1) * sizeof(char));
        var handle = GlobalAlloc(Moveable, bytes);
        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var target = GlobalLock(handle);
        var destination = new Span<char>((void*)target, text.Length + 1);
        text.CopyTo(destination);
        destination[text.Length] = '\0';
        GlobalUnlock(handle);
        if (SetClipboardData(format, handle) == IntPtr.Zero)
        {
            destination.Clear();
            GlobalFree(handle);
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private static unsafe void SetMarker(string name)
    {
        var format = RegisterClipboardFormatW(name);
        var handle = GlobalAlloc(Moveable, sizeof(uint));
        if (format == 0 || handle == IntPtr.Zero)
        {
            return;
        }

        *(uint*)GlobalLock(handle) = 0;
        GlobalUnlock(handle);
        if (SetClipboardData(format, handle) == IntPtr.Zero)
        {
            GlobalFree(handle);
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(IntPtr owner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetClipboardData(uint format, IntPtr memory);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormatW(string name);

    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalLock(IntPtr memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr memory);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalFree(IntPtr memory);
}
