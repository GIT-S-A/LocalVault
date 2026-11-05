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

    public static SshKeyWritePlan Write(VaultEntry entry, string vaultPath, string? origin, bool overwrite, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsMissing || entry.Ssh is not { } ssh)
        {
            throw new VaultException($"'{entry.Name}' holds no SSH key on this PC.");
        }

        var target = TargetOf(entry);
        var fileName = Path.GetFileName(target);
        using var folder = SshKeyFolder.Open(Path.GetDirectoryName(target)!);
        var value = entry.GetValue();
        try
        {
            var keyBytes = ssh.Agent ? Encoding.UTF8.GetBytes(ssh.PublicKey + "\n") : value;
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
                    PrivateFile.Write(target, value);
                }

                PrivateFile.Write(target + SshKeyPath.PublicKeySuffix, Encoding.UTF8.GetBytes(ssh.PublicKey + "\n"));
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
            CryptographicOperations.ZeroMemory(value);
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
