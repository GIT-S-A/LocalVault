using System.Security.Cryptography;
using System.Text;

namespace LocalVault.Engine;

public sealed record AdoptedSshKey(VaultEntry Entry, string WrittenBy);

public static class SshKeyAdoption
{
    public static AdoptedSshKey? FindForMissing(VaultEntry declaration, string? origin, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var target = SshKeyFiles.TargetOf(declaration);
        var directory = Path.GetDirectoryName(target)!;
        if (!File.Exists(Path.Combine(directory, SshKeyPath.MarkerFileName)) || !File.Exists(target))
        {
            return null;
        }

        using var folder = SshKeyFolder.Open(directory);
        if (!folder.BelongsTo(origin) || !folder.Keys.TryGetValue(Path.GetFileName(target), out var record))
        {
            return null;
        }

        var key = File.ReadAllBytes(target);
        try
        {
            var publicKey = OpenSshPrivateKey.PublicKeyOf(key);
            var ssh = new SshKeyInfo(SshKeyInfo.TypeOf(publicKey), publicKey, []);
            return new AdoptedSshKey(VaultEntry.NewSshKey(declaration.Name, key, declaration.RequiredPath!, ssh, [], now), record.WrittenBy);
        }
        catch (VaultException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static IReadOnlyList<AdoptedSshKey> FindNewer(VaultBody body, string? origin, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(body);
        var adopted = new List<AdoptedSshKey>();
        foreach (var entry in body.Entries.Where(e => e is { IsMissing: false, Ssh.Agent: false }))
        {
            if (NewerKey(entry, origin, now) is { } newer)
            {
                adopted.Add(newer);
            }
        }

        return adopted;
    }

    public static void AddUser(VaultEntry entry, string? origin, string vaultPath)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var target = SshKeyFiles.TargetOf(entry);
        using var folder = SshKeyFolder.Open(Path.GetDirectoryName(target)!);
        folder.AddUser(Path.GetFileName(target), origin, vaultPath);
        folder.Save();
    }

    private static AdoptedSshKey? NewerKey(VaultEntry entry, string? origin, DateTimeOffset now)
    {
        var target = SshKeyFiles.TargetOf(entry);
        var directory = Path.GetDirectoryName(target)!;
        if (!File.Exists(Path.Combine(directory, SshKeyPath.MarkerFileName)) || !File.Exists(target))
        {
            return null;
        }

        using var folder = SshKeyFolder.Open(directory);
        if (!folder.BelongsTo(origin))
        {
            return null;
        }

        var value = entry.GetValue();
        try
        {
            var record = folder.NewerThan(Path.GetFileName(target), value, entry.Created ?? DateTimeOffset.MinValue);
            return record is null ? null : Adopt(entry, target, value, record, now);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(value);
        }
    }

    private static AdoptedSshKey? Adopt(VaultEntry entry, string target, byte[] value, SshKeyRecord record, DateTimeOffset now)
    {
        var publicKeyFile = target + SshKeyPath.PublicKeySuffix;
        if (!File.Exists(publicKeyFile))
        {
            return null;
        }

        string publicKey;
        string type;
        try
        {
            publicKey = SshKeyInfo.NormalizePublicKey(File.ReadAllText(publicKeyFile, Encoding.UTF8));
            type = SshKeyInfo.TypeOf(publicKey);
        }
        catch (VaultException)
        {
            return null;
        }

        var key = File.ReadAllBytes(target);
        var oldKeyFile = SshKeyFiles.OldKeyOf(target);
        var old = File.Exists(oldKeyFile) ? File.ReadAllBytes(oldKeyFile) : [];
        try
        {
            if (!SshKeyFiles.IsPrivateKey(key) || SshKeyFolder.HashOf(key) != record.Hash)
            {
                return null;
            }

            var keepsOld = old.AsSpan().SequenceEqual(value);
            IReadOnlyList<string> hosts = entry.IsRequired ? [] : entry.Ssh!.Hosts;
            var ssh = new SshKeyInfo(type, publicKey, hosts, ReplacedAt: keepsOld ? record.WrittenAt : null);
            var adopted = VaultEntry.NewSshKey(entry.Name, key, entry.StoredPath!, ssh, keepsOld ? value : [], now);
            return new AdoptedSshKey(adopted, record.WrittenBy);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(old);
        }
    }
}
