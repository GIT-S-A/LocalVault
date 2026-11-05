using System.Security.Cryptography;
using System.Text;

namespace LocalVault.Core;

public static class SshKeyFiles
{
    public static string TargetOf(VaultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var stored = entry.StoredPath ?? entry.RequiredPath ?? throw new VaultException($"SSH key '{entry.Name}' has no path.");
        return StoredPathRules.Resolve(StoredPathRules.HomeDirectory, SshKeyPath.Validate(stored));
    }

    public static string KeyFileOf(VaultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var target = TargetOf(entry);
        return entry.Ssh is { Agent: true } ? target + SshKeyPath.PublicKeySuffix : target;
    }

    public static string OldKeyOf(string target) => target + VaultEntry.OldKeySuffix;

    public static string OldPublicKeyOf(string target) => OldKeyOf(target) + SshKeyPath.PublicKeySuffix;

    public static bool IsPrivateKey(ReadOnlySpan<byte> value) => value.StartsWith(PrivateKeyStart);

    public static byte[] KeyFileBytesOf(VaultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsMissing || entry.Ssh is not { } ssh)
        {
            throw new VaultException($"'{entry.Name}' holds no SSH key on this PC.");
        }

        return ssh.Agent ? PublicKeyFileBytes(ssh) : entry.GetValue();
    }

    public static SshKeyWritePlan Write(VaultEntry entry, string vaultPath, string? origin, bool overwrite, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var keyBytes = KeyFileBytesOf(entry);
        var ssh = entry.Ssh!;
        var target = TargetOf(entry);
        var fileName = Path.GetFileName(target);
        using var folder = SshKeyFolder.Open(Path.GetDirectoryName(target)!);
        try
        {
            var plan = folder.Plan(fileName, origin, vaultPath, keyBytes, entry.Created ?? now, overwrite);
            if (plan.Refused)
            {
                return plan;
            }

            if (plan.State == SshKeyWriteState.Write)
            {
                if (ssh.Agent)
                {
                    File.Delete(target);
                }
                else
                {
                    PrivateFile.Write(target, keyBytes);
                }

                PrivateFile.Write(target + SshKeyPath.PublicKeySuffix, PublicKeyFileBytes(ssh));
                WriteOldKey(target, entry.GetPrevious());
                folder.Record(fileName, origin, vaultPath, keyBytes, now);
            }
            else
            {
                folder.AddUser(fileName, origin, vaultPath);
            }

            folder.PruneUsers(File.Exists);
            folder.Save();
            return plan;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    private static ReadOnlySpan<byte> PrivateKeyStart => "-----BEGIN "u8;

    private static byte[] PublicKeyFileBytes(SshKeyInfo ssh) => Encoding.UTF8.GetBytes(ssh.PublicKey + "\n");

    private static void WriteOldKey(string target, byte[]? previous)
    {
        if (previous is null)
        {
            File.Delete(OldKeyOf(target));
            File.Delete(OldPublicKeyOf(target));
            return;
        }

        try
        {
            var (written, deleted) = IsPrivateKey(previous) ? (OldKeyOf(target), OldPublicKeyOf(target)) : (OldPublicKeyOf(target), OldKeyOf(target));
            PrivateFile.Write(written, previous);
            File.Delete(deleted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(previous);
        }
    }

    public static string Describe(SshKeyWritePlan plan, EntryName name)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.State switch
        {
            SshKeyWriteState.OtherRepository =>
                $"Not writing {name}: '{Path.GetDirectoryName(plan.Target)}' holds the keys of another repository.",
            SshKeyWriteState.NotWrittenByLocalVault =>
                $"Not writing {name}: '{plan.Target}' exists and wasn't written by LocalVault. Use --overwrite (-Overwrite) to replace it.",
            SshKeyWriteState.NewerElsewhere =>
                $"Not writing {name}: newer key written by {plan.Record!.WrittenBy} at {plan.Record.WrittenAt:yyyy-MM-dd HH:mm} UTC. Run lv load, or --overwrite (-Overwrite) to replace it.",
            SshKeyWriteState.Identical => $"{name} is already in {plan.Target}.",
            _ => $"Wrote {name} to {plan.Target}, readable by your account only.",
        };
    }
}
