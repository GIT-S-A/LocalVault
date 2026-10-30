using System.Text;
using LocalVault.Core;

namespace LocalVault.Core.Tests;

public sealed class VaultStoreTests : IDisposable
{
    private static readonly DateTimeOffset Created = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
    private readonly TempDirectory directory = new();
    private readonly FakeLegacyReader legacy = new();

    public void Dispose() => directory.Dispose();

    [Fact]
    public void ReadOfMissingVaultIsEmptyAndWritesNothing()
    {
        var store = NewStore();

        var snapshot = store.Read();

        Assert.False(snapshot.Exists);
        Assert.Empty(snapshot.Body.Entries);
        Assert.Equal(VaultStore.AbsentFingerprint, snapshot.Fingerprint);
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public void UpdateWithoutAllowCreateRefusesMissingVault()
    {
        var store = NewStore();

        Assert.Throws<VaultException>(() => store.Update(body => Put(body, "a", "1")));
        Assert.False(File.Exists(store.Location.Path));
    }

    [Fact]
    public void UpdateCreatesThenKeepsUnrelatedEntries()
    {
        var store = NewStore();
        var first = store.Update(body => Put(body, "a", "1"), new VaultUpdateOptions { AllowCreate = true });
        var second = store.Update(body => Put(body, "b", "2"));

        Assert.True(first.Created);
        Assert.False(second.Created);
        var names = store.Read().Body.Entries.Select(e => e.Name.FullName);
        Assert.Equal(["a", "b"], names);
        Assert.StartsWith("LOCALVAULT-2\nlocal\nseal=dpapi-user\n", File.ReadAllText(store.Location.Path), StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaceKeepsPreviousGeneration()
    {
        var store = NewStore();
        store.Update(body => Put(body, "a", "1"), new VaultUpdateOptions { AllowCreate = true });
        store.Update(body => Put(body, "a", "2"));

        var previous = VaultBody.Parse(Envelope.OpenLocal(File.ReadAllText(store.Location.PreviousGenerationPath)));
        Assert.Equal("1", Value(previous, "a"));
        Assert.Equal("2", Value(store.Read().Body, "a"));
    }

    [Fact]
    public void FailedMutationLeavesVaultUntouched()
    {
        var store = NewStore();
        store.Update(body => Put(body, "a", "1"), new VaultUpdateOptions { AllowCreate = true });
        var before = File.ReadAllText(store.Location.Path);

        Assert.Throws<InvalidOperationException>(() => store.Update(_ => throw new InvalidOperationException()));

        Assert.Equal(before, File.ReadAllText(store.Location.Path));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp-*"));
    }

    [Fact]
    public void LockFileStaysAfterWrites()
    {
        var store = NewStore();
        store.Update(body => Put(body, "a", "1"), new VaultUpdateOptions { AllowCreate = true });

        Assert.True(File.Exists(store.Location.LockPath));
    }

    [Fact]
    public void HeldLockMakesWriterWaitThenFail()
    {
        var store = NewStore();
        store.Update(body => Put(body, "a", "1"), new VaultUpdateOptions { AllowCreate = true });

        using (new FileStream(store.Location.LockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var error = Assert.Throws<VaultException>(() => store.Update(body => Put(body, "b", "2")));
            Assert.Contains("locked", error.Message, StringComparison.Ordinal);
        }

        Assert.Null(store.Read().Body.Find(EntryName.Parse("b")));
    }

    [Fact]
    public void ConcurrentWritersLoseNothing()
    {
        var path = directory.Combine("secrets.local.lv");
        NewStore().Update(_ => { }, new VaultUpdateOptions { AllowCreate = true });

        Parallel.For(0, 4, writer =>
        {
            var store = new VaultStore(new VaultLocation(path, VaultPathSource.Explicit, null));
            for (var i = 0; i < 15; i++)
            {
                store.Update(body => Put(body, $"w{writer}-{i}", "x"));
            }
        });

        Assert.Equal(60, NewStore().Read().Body.Entries.Count);
    }

    [Fact]
    public void ExpectedFingerprintMismatchAborts()
    {
        var store = NewStore();
        store.Update(body => Put(body, "a", "1"), new VaultUpdateOptions { AllowCreate = true });
        var seen = store.Read().Fingerprint;
        store.Update(body => Put(body, "b", "2"));

        Assert.Throws<VaultChangedException>(() => store.Update(
            body => Put(body, "c", "3"),
            new VaultUpdateOptions { ExpectedFingerprint = seen }));
        Assert.Null(store.Read().Body.Find(EntryName.Parse("c")));
    }

    [Fact]
    public void NewerSchemaIsReadButNotWritten()
    {
        var store = NewStore();
        File.WriteAllText(store.Location.Path, Envelope.SealLocal("""{"schema":4,"entries":[]}"""u8));

        Assert.Equal(4, store.Read().Body.Schema);
        var error = Assert.Throws<VaultException>(() => store.Update(body => Put(body, "a", "1")));
        Assert.Contains("newer", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NewerEnvelopeIsNeitherReadNorOverwritten()
    {
        var store = NewStore();
        const string next = "LOCALVAULT-3\nlocal\nseal=dpapi-user\nAAAA\n";
        File.WriteAllText(store.Location.Path, next);

        var error = Assert.Throws<VaultException>(() => store.Read());
        Assert.Contains(Envelope.NewerFormatMessage, error.Message, StringComparison.Ordinal);
        Assert.Throws<VaultException>(() => store.Update(body => Put(body, "a", "1")));
        Assert.Equal(next, File.ReadAllText(store.Location.Path));
    }

    [Fact]
    public void UnreadableVaultIsNeverOverwritten()
    {
        var store = NewStore();
        File.WriteAllText(store.Location.Path, "garbage");

        Assert.Throws<VaultException>(() => store.Read());
        Assert.Throws<VaultException>(() => store.Update(body => Put(body, "a", "1")));
        Assert.Equal("garbage", File.ReadAllText(store.Location.Path));
    }

    [Fact]
    public void LegacyIsReadInMemoryWithoutMigrating()
    {
        legacy.Values = [new("OsPassword", "pw"), new("__SshPrivateKey", "key")];
        var store = NewStore();
        File.WriteAllText(store.Location.LegacyPath!, "legacy");

        var snapshot = store.Read();

        Assert.True(snapshot.LegacyPending);
        Assert.Equal("pw", Value(snapshot.Body, "OsPassword"));
        Assert.Equal("__SshPrivateKey", Assert.Single(snapshot.LegacyRefused).Name);
        Assert.False(File.Exists(store.Location.Path));
        Assert.True(File.Exists(store.Location.LegacyPath!));
    }

    [Fact]
    public void FirstWriteMigratesAndRetiresLegacy()
    {
        legacy.Values = [new("OsPassword", "pw"), new("GrafanaAdminPassword", "g")];
        var store = NewStore();
        File.WriteAllText(store.Location.LegacyPath!, "legacy");

        var result = store.Update(body => Put(body, "new", "n"));

        Assert.Equal(["OsPassword", "GrafanaAdminPassword"], result.MigratedNames);
        Assert.False(File.Exists(store.Location.LegacyPath!));
        Assert.NotNull(result.MigratedLegacyPath);
        Assert.Matches(@"secrets\.local\.clixml\.migrated-\d{8}T\d{6}Z$", result.MigratedLegacyPath);
        Assert.Equal("legacy", File.ReadAllText(result.MigratedLegacyPath));
        var body = store.Read().Body;
        Assert.Equal("g", Value(body, "GrafanaAdminPassword"));
        Assert.Equal("n", Value(body, "new"));
    }

    [Fact]
    public void RetiringLegacyNeverOverwritesEarlierBackup()
    {
        var store = NewStore();
        File.WriteAllText(store.Location.LegacyPath!, "first");
        var first = store.RetireLegacy(store.Location.LegacyPath!);
        File.WriteAllText(store.Location.LegacyPath!, "second");
        var second = store.RetireLegacy(store.Location.LegacyPath!);

        Assert.NotEqual(first, second);
        Assert.Equal("first", File.ReadAllText(first));
        Assert.Equal("second", File.ReadAllText(second));
    }

    [Fact]
    public void LegacyCaseClashBlocksMigration()
    {
        legacy.Values = [new("Foo", "1"), new("foo", "2")];
        var store = NewStore();
        File.WriteAllText(store.Location.LegacyPath!, "legacy");

        Assert.Throws<VaultException>(() => store.Update(body => Put(body, "a", "1")));
        Assert.True(File.Exists(store.Location.LegacyPath!));
        Assert.False(File.Exists(store.Location.Path));
    }

    [Fact]
    public void BothPresentAndDifferentWarnsOnReadAndBlocksWrites()
    {
        var store = NewStore();
        store.Update(body => Put(body, "a", "1"), new VaultUpdateOptions { AllowCreate = true });
        legacy.Values = [new("a", "other")];
        File.WriteAllText(store.Location.LegacyPath!, "legacy");

        var snapshot = store.Read();
        Assert.Equal("1", Value(snapshot.Body, "a"));
        Assert.Contains("Import-Vault", snapshot.Warning, StringComparison.Ordinal);
        var error = Assert.Throws<VaultException>(() => store.Update(body => Put(body, "b", "2")));
        Assert.Contains("Import-Vault", error.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(store.Location.LegacyPath!));
    }

    [Fact]
    public void LegacyFileLeftAfterItsMigrationIsRetiredOnTheNextWrite()
    {
        legacy.Values = [new("a", "1")];
        var store = NewStore();
        File.WriteAllText(store.Location.LegacyPath!, "legacy");
        store.Update(body => Put(body, "a", "changed"));
        File.WriteAllText(store.Location.LegacyPath!, "legacy");

        Assert.Null(store.Read().Warning);
        store.Update(body => Put(body, "b", "2"));

        Assert.False(File.Exists(store.Location.LegacyPath!));
        Assert.Equal("changed", Value(store.Read().Body, "a"));
    }

    [Fact]
    public void OnlyTheDefaultVaultNameHasALegacyFile()
    {
        legacy.Values = [new("a", "1")];
        File.WriteAllText(directory.Combine("secrets.local.clixml"), "legacy");
        var store = new VaultStore(new VaultLocation(directory.Combine("other.lv"), VaultPathSource.Explicit, null), legacy);

        store.Update(body => Put(body, "b", "2"), new VaultUpdateOptions { AllowCreate = true });

        Assert.Null(store.Location.LegacyPath);
        Assert.Null(store.Read().Body.Find(EntryName.Parse("a")));
        Assert.True(File.Exists(directory.Combine("secrets.local.clixml")));
    }

    [Fact]
    public void BothPresentAndContainedFinishesMigrationOnWrite()
    {
        var store = NewStore();
        store.Update(body => Put(body, "a", "1"), new VaultUpdateOptions { AllowCreate = true });
        legacy.Values = [new("a", "1")];
        File.WriteAllText(store.Location.LegacyPath!, "legacy");

        Assert.Equal("1", Value(store.Read().Body, "a"));
        Assert.True(File.Exists(store.Location.LegacyPath!));

        store.Update(body => Put(body, "b", "2"));
        Assert.False(File.Exists(store.Location.LegacyPath!));
        Assert.Single(Directory.EnumerateFiles(directory.Path, "secrets.local.clixml.migrated-*"));
    }

    [Fact]
    public void IgnoreLegacyAllowsMergingADifferingLegacyFile()
    {
        var store = NewStore();
        store.Update(body => Put(body, "a", "1"), new VaultUpdateOptions { AllowCreate = true });
        legacy.Values = [new("a", "other")];
        File.WriteAllText(store.Location.LegacyPath!, "legacy");

        Assert.Equal("1", Value(store.Read(ignoreLegacy: true).Body, "a"));
        store.Update(body => Put(body, "b", "2"), new VaultUpdateOptions { IgnoreLegacy = true });

        Assert.Equal("2", Value(store.Read(ignoreLegacy: true).Body, "b"));
        Assert.True(File.Exists(store.Location.LegacyPath!));
    }

    [Fact]
    public void DeleteRemovesVaultPreviousGenerationAndLegacy()
    {
        var store = NewStore();
        store.Update(body => Put(body, "a", "1"), new VaultUpdateOptions { AllowCreate = true });
        store.Update(body => Put(body, "a", "2"));

        store.Delete();

        Assert.False(File.Exists(store.Location.Path));
        Assert.False(File.Exists(store.Location.PreviousGenerationPath));
    }

    [Fact]
    public void BackupWriterReceivesBodyWithoutSettingsAndResultIsRecorded()
    {
        var writer = new RecordingBackupWriter();
        var store = NewStore(writer);
        store.Update(
            body =>
            {
                Put(body, "a", "1");
                body.Backup = BackupSettings.Create("0123456789abcdef", "D:/b", "passphrase-123".AsSpan(), new byte[32]);
            },
            new VaultUpdateOptions { AllowCreate = true });

        Assert.NotNull(writer.LastBody);
        Assert.Null(writer.LastBody.Backup);
        Assert.Equal("1", Value(writer.LastBody, "a"));
        var settings = store.Read().Body.Backup!;
        Assert.NotNull(settings.LastBackup);
        Assert.Null(settings.LastError);
    }

    [Fact]
    public void BackupFailureDoesNotBlockTheWrite()
    {
        var writer = new RecordingBackupWriter { Fail = true };
        var store = NewStore(writer);

        var result = store.Update(
            body =>
            {
                Put(body, "a", "1");
                body.Backup = BackupSettings.Create("0123456789abcdef", "D:/b", "passphrase-123".AsSpan(), new byte[32]);
            },
            new VaultUpdateOptions { AllowCreate = true });

        Assert.Equal("destination offline", result.BackupError);
        Assert.Equal("1", Value(store.Read().Body, "a"));
        Assert.Equal("destination offline", store.Read().Body.Backup!.LastError);
    }

    [Fact]
    public void FailedReplaceLeavesTheVaultAndNoTemporaryFile()
    {
        var store = NewStore();
        store.Update(body => Put(body, "a", "1"), new VaultUpdateOptions { AllowCreate = true });
        var before = File.ReadAllText(store.Location.Path);

        using (new FileStream(store.Location.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Assert.Throws<VaultException>(() => store.Update(body => Put(body, "a", "2")));
            Assert.Contains("Nothing was changed", error.Message, StringComparison.Ordinal);
        }

        Assert.Equal(before, File.ReadAllText(store.Location.Path));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp-*"));
    }

    [Fact]
    public void AnyBackupFailureLeavesTheWriteDone()
    {
        var writer = new RecordingBackupWriter { Failure = new FormatException("bad name") };
        var store = NewStore(writer);

        var result = store.Update(
            body =>
            {
                Put(body, "a", "1");
                body.Backup = BackupSettings.Create("0123456789abcdef", "D:/b", "passphrase-123".AsSpan(), new byte[32]);
            },
            new VaultUpdateOptions { AllowCreate = true });

        Assert.Equal("bad name", result.BackupError);
        Assert.Equal("1", Value(store.Read().Body, "a"));
    }

    [Fact]
    public void OldBackupsArePrunedOnlyAfterTheVaultIsWritten()
    {
        var path = directory.Combine("secrets.local.lv");
        var writer = new RecordingBackupWriter { VaultPath = path };
        var store = NewStore(writer);

        store.Update(
            body => body.Backup = BackupSettings.Create("0123456789abcdef", "D:/b", "passphrase-123".AsSpan(), new byte[32]),
            new VaultUpdateOptions { AllowCreate = true });

        Assert.Equal("backup.lvbak", writer.Pruned);
        Assert.True(writer.VaultExistedAtPrune);
    }

    [Fact]
    public void SettingsWithoutAnIdGetOneOnTheNextBackup()
    {
        var writer = new RecordingBackupWriter();
        var store = NewStore(writer);
        var settings = BackupSettings.Create("0123456789abcdef", "D:/b", "passphrase-123".AsSpan(), new byte[32]);
        settings.Node.Remove("id");

        store.Update(body => body.Backup = settings, new VaultUpdateOptions { AllowCreate = true });

        Assert.Matches("^[0-9a-f]{16}$", writer.LastId);
        Assert.Equal(writer.LastId, store.Read().Body.Backup!.Id);
    }

    [Fact]
    public void MigrationIntoAVaultGitDoesNotIgnoreIsRefused()
    {
        using var repo = TempDirectory.GitRepository(gitignore: "*.clixml\n");
        File.WriteAllText(repo.Combine("secrets.local.clixml"), "<Objs />");
        legacy.Values = [new("a", "1")];
        var store = new VaultStore(VaultLocation.Resolve(repo.Combine("secrets.local.lv"), null, repo.Path), legacy);

        var error = Assert.Throws<VaultException>(() => store.Update(body => Put(body, "b", "2")));

        Assert.Contains(".gitignore", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(store.Location.Path));
        Assert.False(File.Exists(store.Location.LockPath));
        Assert.True(File.Exists(store.Location.LegacyPath!));
    }

    private VaultStore NewStore(IBackupWriter? writer = null) =>
        new(new VaultLocation(directory.Combine("secrets.local.lv"), VaultPathSource.Explicit, null), legacy, writer);

    private static void Put(VaultBody body, string name, string value) =>
        body.Set(VaultEntry.NewSecret(EntryName.Parse(name), Encoding.UTF8.GetBytes(value), Created));

    private static string Value(VaultBody body, string name) =>
        Encoding.UTF8.GetString(body.Find(EntryName.Parse(name))!.GetValue());

    private sealed class FakeLegacyReader : ILegacyVaultReader
    {
        public IReadOnlyList<KeyValuePair<string, string>> Values { get; set; } = [];

        public IReadOnlyList<KeyValuePair<string, string>> Read(string path) => Values;
    }

    private sealed class RecordingBackupWriter : IBackupWriter
    {
        public bool Fail { get; init; }

        public Exception? Failure { get; init; }

        public VaultBody? LastBody { get; private set; }

        public string? LastId { get; private set; }

        public string? Pruned { get; private set; }

        public bool VaultExistedAtPrune { get; private set; }

        public string? VaultPath { get; init; }

        public string Write(VaultBody transferBody, BackupSettings settings, DateTimeOffset now)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            if (Fail)
            {
                throw new IOException("destination offline");
            }

            LastBody = transferBody;
            LastId = settings.Id;
            return "backup.lvbak";
        }

        public void Prune(BackupSettings settings, DateTimeOffset now, string justWritten)
        {
            Pruned = justWritten;
            VaultExistedAtPrune = VaultPath is not null && File.Exists(VaultPath);
        }
    }
}
