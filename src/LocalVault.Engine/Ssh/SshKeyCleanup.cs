namespace LocalVault.Engine;

public enum SshKeyReleaseState
{
    Deleted,
    KeptForOthers,
    KeptChanged,
}

public sealed record SshKeyRelease(string Path, SshKeyReleaseState State, IReadOnlyList<string> OtherUsers);

public sealed record SshKeyOrphan(string Folder, string File, DateTimeOffset WrittenAt)
{
    public string Repository => System.IO.Path.GetFileName(Folder);
}

public static class SshKeyCleanup
{
    public static IReadOnlyList<SshKeyRelease> Release(string vaultPath, IEnumerable<VaultEntry> keys)
    {
        ArgumentNullException.ThrowIfNull(vaultPath);
        ArgumentNullException.ThrowIfNull(keys);
        var released = new List<SshKeyRelease>();
        foreach (var group in keys.Where(e => e is { IsMissing: false, Ssh: not null })
            .GroupBy(e => Path.GetDirectoryName(SshKeyFiles.TargetOf(e))!, StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(Path.Combine(group.Key, SshKeyPath.MarkerFileName)))
            {
                continue;
            }

            var folder = SshKeyFolder.Open(group.Key);
            try
            {
                foreach (var fileName in group.Select(e => Path.GetFileName(SshKeyFiles.TargetOf(e))).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!folder.Keys.ContainsKey(fileName))
                    {
                        continue;
                    }

                    folder.RemoveUser(fileName, vaultPath);
                    folder.PruneUsers(File.Exists);
                    var record = folder.Keys[fileName];
                    var path = Path.Combine(group.Key, fileName);
                    released.Add(record.Users.Count > 0
                        ? new SshKeyRelease(path, SshKeyReleaseState.KeptForOthers, record.Users)
                        : new SshKeyRelease(path, DeleteKey(folder, fileName, record) ? SshKeyReleaseState.Deleted : SshKeyReleaseState.KeptChanged, []));
                }
            }
            finally
            {
                Finish(folder);
            }
        }

        return released;
    }

    public static IReadOnlyList<SshKeyOrphan> FindOrphans(string sshDirectory)
    {
        ArgumentNullException.ThrowIfNull(sshDirectory);
        var orphans = new List<SshKeyOrphan>();
        foreach (var folder in SshKeyFolder.MarkedFolders(sshDirectory))
        {
            IReadOnlyDictionary<string, SshKeyRecord> keys;
            try
            {
                keys = SshKeyFolder.Peek(folder);
            }
            catch (VaultException)
            {
                continue;
            }

            orphans.AddRange(keys.Where(pair => !pair.Value.Users.Any(File.Exists))
                .Select(pair => new SshKeyOrphan(folder, pair.Key, pair.Value.WrittenAt)));
        }

        return orphans;
    }

    public static IReadOnlyList<SshKeyRelease> RemoveOrphans(IEnumerable<SshKeyOrphan> orphans)
    {
        ArgumentNullException.ThrowIfNull(orphans);
        var removed = new List<SshKeyRelease>();
        foreach (var group in orphans.GroupBy(o => o.Folder, StringComparer.OrdinalIgnoreCase))
        {
            var folder = SshKeyFolder.Open(group.Key);
            try
            {
                folder.PruneUsers(File.Exists);
                foreach (var orphan in group)
                {
                    if (!folder.Keys.TryGetValue(orphan.File, out var record) || record.Users.Count > 0)
                    {
                        continue;
                    }

                    var path = Path.Combine(group.Key, orphan.File);
                    removed.Add(new SshKeyRelease(path, DeleteKey(folder, orphan.File, record) ? SshKeyReleaseState.Deleted : SshKeyReleaseState.KeptChanged, []));
                }
            }
            finally
            {
                Finish(folder);
            }
        }

        return removed;
    }

    private static bool DeleteKey(SshKeyFolder folder, string fileName, SshKeyRecord record)
    {
        var target = Path.Combine(folder.Directory, fileName);
        var publicKey = target + SshKeyPath.PublicKeySuffix;
        var written = File.Exists(target) ? target : publicKey;
        if (File.Exists(written) && SshKeyFolder.HashOfFile(written) != record.Hash)
        {
            return false;
        }

        File.Delete(target);
        File.Delete(publicKey);
        SshKeyFiles.DeleteOldKeys(target);
        folder.RemoveKey(fileName);
        return true;
    }

    private static void Finish(SshKeyFolder folder)
    {
        var directory = folder.Directory;
        var empty = folder.Keys.Count == 0;
        try
        {
            if (!empty)
            {
                folder.Save();
            }
        }
        finally
        {
            folder.Dispose();
        }

        if (!empty)
        {
            return;
        }

        foreach (var file in new[] { SshKeyPath.MarkerFileName, SshKeyPath.ConfigFileName, SshKeyPath.MarkerLockFileName })
        {
            File.Delete(Path.Combine(directory, file));
        }

        if (!Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }
}
