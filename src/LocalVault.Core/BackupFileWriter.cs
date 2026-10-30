using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace LocalVault.Core;

public sealed partial class BackupFileWriter : IBackupWriter
{
    public const string Extension = ".lvbak";
    public const int NewestKept = 10;
    public const int DailyDaysKept = 30;
    private const string StampFormat = "yyyyMMdd'T'HHmmssfff'Z'";

    public string Write(VaultBody transferBody, BackupSettings settings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(transferBody);
        ArgumentNullException.ThrowIfNull(settings);
        var directory = ResolveDestination(settings.Destination);
        var path = Path.Combine(directory, FileName(now, settings.Id));
        var plain = transferBody.ToUtf8();
        var passphrase = settings.GetPassphrase();
        var keyFile = settings.GetKeyFile();
        try
        {
            var text = Envelope.SealBackup(plain, passphrase, keyFile);
            SealedFile.Write(path, text, written => Verify(written, passphrase, keyFile, plain), overwrite: false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(keyFile);
            Array.Clear(passphrase);
        }

        return path;
    }

    public void Prune(BackupSettings settings, DateTimeOffset now, string justWritten)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = ResolveDestination(settings.Destination);
        var names = Directory.EnumerateFiles(directory).Select(Path.GetFileName);
        foreach (var name in SelectForDeletion(names, settings.Id, now, Path.GetFileName(justWritten)))
        {
            File.Delete(Path.Combine(directory, name));
        }
    }

    public static string ResolveDestination(string destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var expanded = Environment.ExpandEnvironmentVariables(destination);
        if (expanded.Contains('%', StringComparison.Ordinal) || !Path.IsPathFullyQualified(expanded))
        {
            throw new VaultException(
                $"The backup folder '{destination}' does not resolve to a full path ('{expanded}'). " +
                "Set the variable it uses, or run Enable-VaultBackup -Destination.");
        }

        return expanded;
    }

    public static string? FindNewest(string destination, string? id)
    {
        var directory = ResolveDestination(destination);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var newest = Parse(Directory.EnumerateFiles(directory).Select(Path.GetFileName))
            .Where(backup => id is null || backup.Id is null || backup.Id == id)
            .MaxBy(backup => backup.Time);
        return newest is null ? null : Path.Combine(directory, newest.Name);
    }

    public static IReadOnlyList<string> FindOwn(string destination, string? id)
    {
        var directory = ResolveDestination(destination);
        return Directory.Exists(directory)
            ? Parse(Directory.EnumerateFiles(directory).Select(Path.GetFileName))
                .Where(backup => backup.Id == id)
                .Select(backup => Path.Combine(directory, backup.Name))
                .ToList()
            : [];
    }

    public static DateTimeOffset? TakenAt(string path) =>
        Parse([Path.GetFileName(path)]).SingleOrDefault() is { } backup
            ? new DateTimeOffset(backup.Time, TimeSpan.Zero)
            : null;

    public static IReadOnlyList<string> SelectForDeletion(
        IEnumerable<string?> fileNames, string? id, DateTimeOffset now, string justWritten)
    {
        var backups = Parse(fileNames)
            .Where(backup => id is not null && backup.Id == id)
            .OrderByDescending(backup => backup.Time)
            .ToList();

        var keep = new HashSet<string>(backups.Take(NewestKept).Select(backup => backup.Name), StringComparer.Ordinal)
        {
            justWritten,
        };
        var horizon = now.UtcDateTime.Date.AddDays(-DailyDaysKept);
        foreach (var day in backups.Where(backup => backup.Time >= horizon).GroupBy(backup => backup.Time.Date))
        {
            keep.Add(day.First().Name);
        }

        return backups.Where(backup => !keep.Contains(backup.Name)).Select(backup => backup.Name).ToList();
    }

    private static string FileName(DateTimeOffset now, string? id) =>
        now.UtcDateTime.ToString(StampFormat, CultureInfo.InvariantCulture) + (id is null ? string.Empty : "-" + id) + Extension;

    private static IEnumerable<BackupFile> Parse(IEnumerable<string?> fileNames)
    {
        foreach (var name in fileNames)
        {
            var match = name is null ? Match.Empty : BackupName().Match(name);
            if (match.Success && DateTime.TryParseExact(
                    match.Groups["stamp"].Value,
                    StampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var time))
            {
                yield return new BackupFile(name!, time, match.Groups["id"].Success ? match.Groups["id"].Value : null);
            }
        }
    }

    private static void Verify(string written, char[] passphrase, byte[] keyFile, byte[] expected)
    {
        var reopened = Envelope.OpenBackup(written, passphrase, keyFile);
        try
        {
            if (!reopened.AsSpan().SequenceEqual(expected))
            {
                throw new VaultException("The backup did not read back as written.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(reopened);
        }
    }

    [GeneratedRegex("^(?<stamp>[0-9]{8}T[0-9]{9}Z)(?:-(?<id>[0-9a-f]{16}))?\\.lvbak$", RegexOptions.CultureInvariant)]
    private static partial Regex BackupName();

    private sealed record BackupFile(string Name, DateTime Time, string? Id);
}
