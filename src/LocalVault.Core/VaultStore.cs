using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LocalVault.Core;

public interface ILegacyVaultReader
{
    IReadOnlyList<KeyValuePair<string, string>> Read(string path);
}

public sealed record VaultSnapshot(
    VaultBody Body,
    string Fingerprint,
    bool Exists,
    bool LegacyPending,
    IReadOnlyList<RefusedName> LegacyRefused,
    string? Warning = null);

public sealed record VaultUpdateOptions
{
    public bool AllowCreate { get; init; }

    public string? ExpectedFingerprint { get; init; }

    public bool IgnoreLegacy { get; init; }

    public bool DropPreviousGeneration { get; init; }
}

public sealed record VaultWriteResult(
    bool Created,
    IReadOnlyList<string> MigratedNames,
    IReadOnlyList<RefusedName> LegacyRefused,
    string? MigratedLegacyPath,
    int? UpgradedFromSchema = null,
    string? Path = null);

public sealed class VaultChangedException : VaultException
{
    public VaultChangedException()
        : base("The vault changed while you were deciding. Nothing was written; run the command again.")
    {
    }

    public VaultChangedException(string message)
        : base(message)
    {
    }

    public VaultChangedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class VaultStore
{
    public const string AbsentFingerprint = "absent";
    private const string MigratedSuffix = ".migrated-";
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);
    private const int ReadAttempts = 20;
    private const int ReplaceAttempts = 5;

    private readonly ILegacyVaultReader? legacyReader;
    private readonly TimeProvider time;

    public VaultStore(
        VaultLocation location,
        ILegacyVaultReader? legacyReader = null,
        TimeProvider? time = null)
    {
        Location = location ?? throw new ArgumentNullException(nameof(location));
        this.legacyReader = legacyReader;
        this.time = time ?? TimeProvider.System;
    }

    public VaultLocation Location { get; }

    public bool Exists => File.Exists(Location.Path) || LegacyExists(ignoreLegacy: false);

    public VaultSnapshot Read(bool ignoreLegacy = false) => ReadState(finishMigration: false, ignoreLegacy);

    public VaultWriteResult Update(Action<VaultBody> mutate, VaultUpdateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        options ??= new VaultUpdateOptions();

        if (!File.Exists(Location.Path) && (options.AllowCreate || LegacyExists(options.IgnoreLegacy)))
        {
            Location.EnsureIgnoredByGit();
        }

        if (!System.IO.Directory.Exists(Location.Directory))
        {
            if (!options.AllowCreate)
            {
                throw new VaultException($"There is no vault at '{Location.Path}'.");
            }

            System.IO.Directory.CreateDirectory(Location.Directory);
        }

        using var heldLock = AcquireLock();
        var snapshot = ReadState(finishMigration: true, options.IgnoreLegacy);
        if (options.ExpectedFingerprint is not null && options.ExpectedFingerprint != snapshot.Fingerprint)
        {
            throw new VaultChangedException();
        }

        var created = !snapshot.Exists && !snapshot.LegacyPending;
        if (created && !options.AllowCreate)
        {
            throw new VaultException($"There is no vault at '{Location.Path}'.");
        }

        var body = snapshot.Body;
        if (!body.IsWritable)
        {
            throw new VaultException(
                $"'{Location.Path}' (schema {body.Schema}): {Envelope.NewerFormatMessage}");
        }

        var migratedNames = snapshot.LegacyPending ? body.Entries.Select(e => e.Name.FullName).ToList() : [];
        if (snapshot.LegacyPending)
        {
            body.MigratedLegacy = Fingerprint(ReadAllTextShared(Location.LegacyPath!));
        }

        mutate(body);

        WriteVerified(body);
        if (options.DropPreviousGeneration)
        {
            File.Delete(Location.PreviousGenerationPath);
        }

        string? migratedPath = null;
        if (snapshot.LegacyPending)
        {
            migratedPath = RetireLegacy(Location.LegacyPath!);
        }

        return new VaultWriteResult(
            created,
            migratedNames,
            snapshot.LegacyRefused,
            migratedPath,
            body.UpgradedFrom,
            Location.Path);
    }

    public void Delete()
    {
        using var heldLock = AcquireLock();
        File.Delete(Location.Path);
        File.Delete(Location.PreviousGenerationPath);
        if (Location.LegacyPath is { } legacyPath)
        {
            File.Delete(legacyPath);
        }
    }

    private bool LegacyExists(bool ignoreLegacy) =>
        !ignoreLegacy && Location.LegacyPath is { } legacyPath && File.Exists(legacyPath);

    private VaultSnapshot ReadState(bool finishMigration, bool ignoreLegacy)
    {
        var hasVault = File.Exists(Location.Path);
        var hasLegacy = LegacyExists(ignoreLegacy);

        if (hasVault)
        {
            var text = ReadAllTextShared(Location.Path);
            var body = OpenVault(text);
            var warning = hasLegacy ? ResolveBothPresent(body, finishMigration, Location.LegacyPath!) : null;
            return new VaultSnapshot(body, Fingerprint(text), true, false, [], warning);
        }

        if (hasLegacy)
        {
            var mapping = ReadLegacy(Location.LegacyPath!);
            var body = VaultBody.CreateEmpty();
            foreach (var entry in mapping.Entries)
            {
                body.Set(entry);
            }

            var fingerprint = "legacy:" + Fingerprint(ReadAllTextShared(Location.LegacyPath!));
            return new VaultSnapshot(body, fingerprint, false, true, mapping.Refused);
        }

        return new VaultSnapshot(VaultBody.CreateEmpty(), AbsentFingerprint, false, false, []);
    }

    private VaultBody OpenVault(string text)
    {
        switch (Envelope.Detect(text))
        {
            case EnvelopeKind.Local:
                break;
            case EnvelopeKind.Newer:
                throw new VaultException($"'{Location.Path}': {Envelope.NewerFormatMessage}");
            default:
                throw new VaultException($"'{Location.Path}' is not a LocalVault vault.");
        }

        try
        {
            var plain = Envelope.OpenLocal(text);
            try
            {
                return VaultBody.Parse(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (VaultException ex)
        {
            throw new VaultException($"'{Location.Path}': {ex.Message}", ex);
        }
    }

    private LegacyMapping ReadLegacy(string legacyPath)
    {
        if (legacyReader is null)
        {
            throw new VaultException($"'{legacyPath}' needs the PowerShell module to be read.");
        }

        var mapping = LegacyMapper.Map(legacyReader.Read(legacyPath), File.GetLastWriteTimeUtc(legacyPath));
        if (mapping.CaseClashes.Count > 0)
        {
            var names = mapping.CaseClashes.SelectMany(group => group.Select(e => e.Name.FullName));
            throw new VaultException(
                $"'{legacyPath}' holds names that differ only by case ({string.Join(", ", names)}). " +
                "Nothing was migrated. Import it with Import-Vault -Source to choose.");
        }

        return mapping;
    }

    private string? ResolveBothPresent(VaultBody body, bool finishMigration, string legacyPath)
    {
        bool complete;
        try
        {
            complete = body.MigratedLegacy == Fingerprint(ReadAllTextShared(legacyPath))
                || ReadLegacy(legacyPath).Entries.All(legacy =>
                    body.Find(legacy.Name) is { } current && current.HasSameContent(legacy));
        }
        catch (Exception ex) when (!finishMigration && ex is VaultException or IOException or UnauthorizedAccessException)
        {
            return $"'{legacyPath}' is beside the vault and could not be read: {ex.Message}";
        }

        if (!complete)
        {
            var message = $"Both '{Location.Path}' and '{legacyPath}' exist and differ. " +
                $"Merge the old file with: Import-Vault -Path '{Location.Path}' -Source '{legacyPath}'";
            return finishMigration ? throw new VaultException(message + " Nothing was changed.") : message;
        }

        if (finishMigration)
        {
            RetireLegacy(legacyPath);
        }

        return null;
    }
    public string RetireLegacy(string legacyPath)
    {
        var stamp = time.GetUtcNow().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var target = legacyPath + MigratedSuffix + stamp;
        for (var attempt = 1; File.Exists(target); attempt++)
        {
            target = legacyPath + MigratedSuffix + stamp + "-" + attempt.ToString(CultureInfo.InvariantCulture);
        }

        File.Move(legacyPath, target, overwrite: false);
        return target;
    }

    private void WriteVerified(VaultBody body)
    {
        var temp = Location.Path + ".tmp-" + Guid.NewGuid().ToString("N");
        var plain = body.ToUtf8();
        try
        {
            SealedFile.WriteDurably(temp, Encoding.UTF8.GetBytes(Envelope.SealLocal(plain)));
            var reread = Envelope.OpenLocal(File.ReadAllText(temp));
            try
            {
                if (!reread.AsSpan().SequenceEqual(plain))
                {
                    throw new VaultException("The new vault file did not read back as written. Nothing was changed.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(reread);
            }

            if (File.Exists(Location.Path))
            {
                ReplaceVault(temp);
            }
            else
            {
                File.Move(temp, Location.Path);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (File.Exists(Location.Path))
            {
                File.Delete(temp);
            }
        }
    }

    private void ReplaceVault(string temp)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Replace(temp, Location.Path, Location.PreviousGenerationPath, ignoreMetadataErrors: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (File.Exists(Location.Path) && attempt < ReplaceAttempts)
                {
                    Thread.Sleep(RetryDelay);
                    continue;
                }

                if (File.Exists(Location.Path))
                {
                    throw new VaultException($"'{Location.Path}' could not be replaced: {ex.Message} Nothing was changed.", ex);
                }

                try
                {
                    File.Move(Location.PreviousGenerationPath, Location.Path);
                }
                catch (Exception restore) when (restore is IOException or UnauthorizedAccessException)
                {
                    throw new VaultException(
                        $"'{Location.Path}' could not be replaced and is missing. The previous vault is " +
                        $"'{Location.PreviousGenerationPath}' and the new one '{temp}'. Rename one of them to '{Location.Path}'.",
                        restore);
                }

                throw new VaultException($"'{Location.Path}' could not be replaced: {ex.Message} Nothing was changed.", ex);
            }
        }
    }
    private FileStream AcquireLock()
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(Location.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (waited.Elapsed < LockTimeout)
            {
                Thread.Sleep(RetryDelay);
            }
            catch (IOException ex)
            {
                throw new VaultException(
                    $"'{Location.Path}' is locked by another process for more than {LockTimeout.TotalSeconds} s.", ex);
            }
        }
    }

    private static string ReadAllTextShared(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException ex) when (ex is not FileNotFoundException && attempt < ReadAttempts)
            {
                Thread.Sleep(RetryDelay);
            }
        }
    }

    private static string Fingerprint(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
}
