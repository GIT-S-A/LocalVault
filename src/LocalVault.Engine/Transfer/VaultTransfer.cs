using System.Security.Cryptography;

namespace LocalVault.Engine;

public enum ImportSourceKind
{
    Export,
    Backup,
    LegacyExport,
    LegacyVault,
}

public static class VaultTransfer
{
    public static void Export(
        VaultBody body, string destination, ReadOnlySpan<char> passphrase, bool overwrite, DateTimeOffset now, IReadOnlyCollection<EntryName>? hello = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        Envelope.RequireStrongPassphrase(passphrase);
        if (body.Entries.FirstOrDefault(e => e.IsHello && !e.IsRequired) is { } stillSealed)
        {
            throw new HelloException($"'{stillSealed.Name}' is sealed by Windows Hello; unseal it before export.");
        }

        var transfer = body.ForExport(hello);
        transfer.Exported = now;
        transfer.LastExported = null;
        var plain = transfer.ToUtf8();
        var passphraseCopy = passphrase.ToArray();
        try
        {
            var text = Envelope.SealExport(plain, passphraseCopy);
            SealedFile.Write(destination, text, written => VerifyExport(written, passphraseCopy, plain), overwrite);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            Array.Clear(passphraseCopy);
        }
    }

    public static ImportSourceKind DetectSource(string path)
    {
        var text = File.ReadAllText(path);
        return Envelope.Detect(text) switch
        {
            EnvelopeKind.Export => ImportSourceKind.Export,
            EnvelopeKind.Backup => ImportSourceKind.Backup,
            EnvelopeKind.LegacyExport => ImportSourceKind.LegacyExport,
            _ when text.TrimStart().StartsWith("<Objs", StringComparison.Ordinal) => ImportSourceKind.LegacyVault,
            EnvelopeKind.Newer => throw new VaultException($"'{path}': {Envelope.NewerFormatMessage}"),
            EnvelopeKind.Local => throw new VaultException(
                $"'{path}' is a local vault, sealed to its machine. Export it there with Export-Vault."),
            _ => throw new VaultException($"'{path}' is not a LocalVault export, backup or {Envelope.LegacyMagic} export."),
        };
    }

    public static ImportSourceContent OpenExport(string path, ReadOnlySpan<char> passphrase) =>
        FromBody(Envelope.OpenExport(File.ReadAllText(path), passphrase));

    public static ImportSourceContent OpenBackup(string path, ReadOnlySpan<char> passphrase, ReadOnlySpan<byte> keyFile) =>
        FromBody(Envelope.OpenBackup(File.ReadAllText(path), passphrase, keyFile));

    public static ImportSourceContent OpenLegacyExport(string path, ReadOnlySpan<char> passphrase, DateTimeOffset now)
    {
        var plain = Envelope.OpenLegacyExport(File.ReadAllText(path), passphrase);
        try
        {
            return FromMapping(LegacyMapper.Map(LegacyMapper.ParseExportBody(plain), now));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public static ImportSourceContent OpenLegacyVault(string path, ILegacyVaultReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return FromMapping(LegacyMapper.Map(reader.Read(path), File.GetLastWriteTimeUtc(path)));
    }

    private static ImportSourceContent FromBody(byte[] plain)
    {
        try
        {
            var body = VaultBody.Parse(plain);
            if (!body.IsWritable)
            {
                throw new VaultException($"The file holds schema {body.Schema}. {Envelope.NewerFormatMessage}");
            }

            foreach (var entry in body.Entries)
            {
                if (entry.StoredPath is { } storedPath)
                {
                    try
                    {
                        StoredPathRules.Normalize(storedPath);
                    }
                    catch (VaultException ex)
                    {
                        throw new VaultException($"The file is damaged: entry '{entry.Name}': {ex.Message}", ex);
                    }
                }
            }

            return new ImportSourceContent(body.Entries.ToList(), [], []);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static ImportSourceContent FromMapping(LegacyMapping mapping) =>
        new(mapping.Entries, mapping.Refused, mapping.CaseClashes);

    private static void VerifyExport(string written, char[] passphrase, byte[] expected)
    {
        var reopened = Envelope.OpenExport(written, passphrase);
        try
        {
            if (!reopened.AsSpan().SequenceEqual(expected))
            {
                throw new VaultException("The export did not read back as written. Nothing was written.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(reopened);
        }
    }
}
