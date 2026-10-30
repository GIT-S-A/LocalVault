using System.Text;

namespace LocalVault.Core;

public static class SealedFile
{
    public static void Write(string destination, string content, Action<string> verify, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(verify);
        var full = Path.GetFullPath(destination);
        if (File.Exists(full) && !overwrite)
        {
            throw new VaultException($"'{full}' already exists. Nothing was written; use -Overwrite (--overwrite) to replace it.");
        }

        var directory = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, Path.GetFileName(full) + ".tmp-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteDurably(temp, Encoding.UTF8.GetBytes(content));
            verify(File.ReadAllText(temp));
            File.Move(temp, full, overwrite);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    public static void WriteBytes(string destination, byte[] content, bool overwrite)
    {
        var full = Path.GetFullPath(destination);
        var directory = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, Path.GetFileName(full) + ".tmp-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteDurably(temp, content);
            File.Move(temp, full, overwrite);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    public static void WriteDurably(string path, ReadOnlySpan<byte> content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }
}
