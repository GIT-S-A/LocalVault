using System.Runtime.CompilerServices;
using System.Text;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

namespace LocalVault.Core.Tests;

public sealed class KeePassSyncTests : IDisposable
{
    private const string Master = "test master 1";
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyList<string> Group = ["LocalVault", "proj"];

    private readonly TempDirectory directory = new();
    private readonly string database;

    public KeePassSyncTests()
    {
        database = directory.Combine("team.kdbx");
        CreateDatabase(database, recycleBin: true);
    }

    public void Dispose() => directory.Dispose();

    [Fact]
    public void GroupFollowsRepositoryFolderAndFileName()
    {
        var root = directory.CreateDirectory("repo");
        Assert.Equal(["LocalVault", "repo"], KeePassGroupPath.For(new VaultLocation(Path.Combine(root, "secrets.local.lv"), VaultPathSource.GitRoot, root)));
        Assert.Equal(
            ["LocalVault", "repo", "infra", "deep"],
            KeePassGroupPath.For(new VaultLocation(Path.Combine(root, "infra", "deep", "secrets.local.lv"), VaultPathSource.Explicit, root)));
        Assert.Equal(
            ["LocalVault", "repo", "other.lv"],
            KeePassGroupPath.For(new VaultLocation(Path.Combine(root, "other.lv"), VaultPathSource.Explicit, root)));
        Assert.Equal(
            ["LocalVault", "loose"],
            KeePassGroupPath.For(new VaultLocation(Path.Combine(directory.Path, "loose", "secrets.local.lv"), VaultPathSource.Explicit, null)));
    }

    [Fact]
    public void SaveWritesReadableEntriesAndRecordsBases()
    {
        var body = Body(("common/db", "s3cret"));
        body.Set(VaultEntry.NewFile(EntryName.Parse("clouds.yaml"), "a: 1"u8, "infra/clouds.yaml", Now));

        var result = Save(body);

        Assert.Equal(2, result.Added);
        using (var raw = OpenRaw())
        {
            var group = raw.RootGroup.FindCreateGroup("LocalVault", false).FindCreateGroup("proj", false);
            var secret = group.Entries.Single(e => e.Strings.ReadSafe(PwDefs.TitleField) == "common/db");
            Assert.Equal("s3cret", secret.Strings.ReadSafe(PwDefs.PasswordField));
            var file = group.Entries.Single(e => e.Strings.ReadSafe(PwDefs.TitleField) == "clouds.yaml");
            Assert.Equal("infra/clouds.yaml", file.Strings.ReadSafe(KeePassVault.PathField));
            Assert.Equal("a: 1", Encoding.UTF8.GetString(file.Binaries.Get("clouds.yaml").ReadData()));
        }

        Assert.All(body.Entries, entry => Assert.Equal(entry.ContentHash(), entry.KeePassBase));
        Assert.All(Compare(body), c => Assert.Equal(KeePassSyncState.Saved, c.State));
    }

    [Fact]
    public void CredentialsUseKeePassFieldsAndComeBackIdentical()
    {
        var body = VaultBody.CreateEmpty();
        body.Set(VaultEntry.NewCredential(
            EntryName.Parse("gh"), "jdoe", "pa55word"u8, TotpSecret.Parse("otpauth://totp/x?secret=JBSWY3DPEHPK3PXP&digits=8"), ["https://a", "https://b", "https://c"], Now));

        Save(body);

        using (var raw = OpenRaw())
        {
            var entry = Single(raw, "gh");
            Assert.Equal("jdoe", entry.Strings.ReadSafe(PwDefs.UserNameField));
            Assert.Equal("pa55word", entry.Strings.ReadSafe(PwDefs.PasswordField));
            Assert.Equal("https://a", entry.Strings.ReadSafe(PwDefs.UrlField));
            Assert.Equal("https://b", entry.Strings.ReadSafe("KP2A_URL_1"));
            Assert.Equal("https://c", entry.Strings.ReadSafe("KP2A_URL_2"));
            Assert.Equal("JBSWY3DPEHPK3PXP", entry.Strings.ReadSafe("TimeOtp-Secret-Base32"));
            Assert.Equal("8", entry.Strings.ReadSafe("TimeOtp-Length"));
            Assert.False(entry.Strings.Exists("TimeOtp-Period"));
            Assert.StartsWith("otpauth://totp/", entry.Strings.ReadSafe("otp"), StringComparison.Ordinal);
        }

        Assert.All(Compare(body), c => Assert.Equal(KeePassSyncState.Saved, c.State));
        var loaded = VaultBody.CreateEmpty();
        Load(loaded);
        Assert.Equal(body.Entries[0].ContentHash(), Assert.Single(loaded.Entries).ContentHash());
    }

    [Fact]
    public void ACredentialTurnedSecretLeavesNoCredentialFields()
    {
        var body = VaultBody.CreateEmpty();
        body.Set(VaultEntry.NewCredential(EntryName.Parse("gh"), "jdoe", "p"u8, TotpSecret.Parse("JBSWY3DPEHPK3PXP"), ["https://a", "https://b"], Now));
        Save(body);

        body.Set(VaultEntry.NewSecret(EntryName.Parse("gh"), "plain"u8, Now));
        Save(body);

        using var raw = OpenRaw();
        var entry = Single(raw, "gh");
        Assert.Equal("secret", entry.Strings.ReadSafe(KeePassVault.KindField));
        Assert.DoesNotContain(entry.Strings.GetKeys(), key => key is PwDefs.UserNameField or PwDefs.UrlField or "otp" || key.StartsWith("KP2A_URL", StringComparison.Ordinal) || key.StartsWith("TimeOtp-", StringComparison.Ordinal));
    }

    [Fact]
    public void EntriesMadeInKeePassAreReadAsCredentials()
    {
        using (var raw = OpenRaw())
        {
            var group = raw.RootGroup.FindCreateGroup("LocalVault", true).FindCreateGroup("proj", true);
            group.AddEntry(RawEntry("native", ("UserName", "u1"), ("Password", "p1"), ("TimeOtp-Secret-Hex", "3132333435363738393031323334353637383930"), ("TimeOtp-Algorithm", "HMAC-SHA-256"), ("TimeOtp-Period", "60")), true);
            group.AddEntry(RawEntry("xc", ("Password", "p2"), ("otp", "otpauth://totp/xc?secret=JBSWY3DPEHPK3PXP"), ("KP2A_URL", "https://second"), ("URL", "https://first")), true);
            group.AddEntry(RawEntry("plain", ("Password", "p3")), true);
            raw.Save();
        }

        var body = VaultBody.CreateEmpty();
        Load(body);

        var native = body.Find(EntryName.Parse("native"))!;
        Assert.True(native.IsCredential);
        Assert.Equal(("u1", "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", "SHA256", 60), (native.UserName, native.Totp!.Secret, native.Totp.Algorithm, native.Totp.Period));
        var xc = body.Find(EntryName.Parse("xc"))!;
        Assert.Equal("JBSWY3DPEHPK3PXP", xc.Totp!.Secret);
        Assert.Equal(["https://first", "https://second"], xc.Urls);
        Assert.True(body.Find(EntryName.Parse("plain"))!.IsSecret);
        Assert.All(Compare(body), c => Assert.Equal(KeePassSyncState.Saved, c.State));
    }

    [Fact]
    public void ABrokenTotpSkipsOnlyThatEntry()
    {
        using (var raw = OpenRaw())
        {
            var group = raw.RootGroup.FindCreateGroup("LocalVault", true).FindCreateGroup("proj", true);
            group.AddEntry(RawEntry("broken", ("UserName", "u"), ("otp", "otpauth://hotp/x?secret=AA")), true);
            group.AddEntry(RawEntry("fine", ("Password", "p")), true);
            raw.Save();
        }

        using var keePass = KeePassVault.Open(database, null, Master);
        using var content = keePass.Read(Group);

        Assert.Equal("fine", Assert.Single(content.Items).Name.FullName);
        Assert.StartsWith("broken (", Assert.Single(content.Skipped), StringComparison.Ordinal);
    }

    private static PwEntry Single(RawDatabase raw, string title) =>
        raw.RootGroup.FindCreateGroup("LocalVault", false).FindCreateGroup("proj", false).Entries.Single(e => e.Strings.ReadSafe(PwDefs.TitleField) == title);

    private static PwEntry RawEntry(string title, params (string Key, string Value)[] fields)
    {
        var entry = new PwEntry(true, true);
        entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        foreach (var (key, value) in fields)
        {
            entry.Strings.Set(key, new ProtectedString(false, value));
        }

        return entry;
    }

    [Fact]
    public void SecondSaveChangesNothing()
    {
        var body = Body(("a", "1"));
        Save(body);

        var again = Save(body);

        Assert.Equal(0, again.Added + again.Updated + again.Deleted);
        Assert.Single(History("a"));
    }

    [Fact]
    public void LocalEntriesAreNeverSent()
    {
        var body = Body(("a", "1"), ("mine", "2"));
        body.SetSharing(EntryName.Parse("mine"), local: true);

        Save(body);

        Assert.Equal(["a"], Titles());
    }

    [Fact]
    public void TeammateChangeIsTakenOnLoadAndBlocksSave()
    {
        var mine = Body(("a", "1"));
        Save(mine);
        var theirs = Body();
        Load(theirs);
        Set(theirs, "a", "2");
        Save(theirs);

        Set(mine, "b", "x");
        var saved = Save(mine);
        Assert.Equal(KeePassSyncState.ChangedInKeePass, Assert.Single(saved.Skipped).State);

        var loaded = Load(mine);
        Assert.Equal(1, loaded.Taken);
        Assert.Equal("2", Value(mine, "a"));
    }

    [Fact]
    public void ConflictKeepsBothSidesUnlessReplaced()
    {
        var mine = Body(("a", "1"));
        Save(mine);
        var theirs = Body();
        Load(theirs);
        Set(theirs, "a", "theirs");
        Save(theirs);
        Set(mine, "a", "mine");

        Assert.Equal(KeePassSyncState.Conflict, Assert.Single(Save(mine).Skipped).State);
        Assert.Equal(1, Load(mine).Kept);
        Assert.Equal("mine", Value(mine, "a"));

        Save(mine, replace: true);
        Assert.Equal("mine", RawPassword("a"));
        Assert.Equal(3, History("a").Count);
    }

    [Fact]
    public void MirrorTakesTheVaultValueOverEveryEditMadeInIt()
    {
        var mine = Body(("changed", "1"), ("removed", "2"), ("conflict", "3"), ("gone", "4"));
        Save(mine, mirror: true);
        using (var raw = OpenRaw())
        {
            var group = raw.RootGroup.FindCreateGroup("LocalVault", false).FindCreateGroup("proj", false);
            group.Entries.Single(e => e.Strings.ReadSafe(PwDefs.TitleField) == "changed").Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "edited"));
            group.Entries.Single(e => e.Strings.ReadSafe(PwDefs.TitleField) == "conflict").Strings.Set(PwDefs.PasswordField, new ProtectedString(true, "edited"));
            group.Entries.Remove(group.Entries.Single(e => e.Strings.ReadSafe(PwDefs.TitleField) == "removed"));
            AddRaw(group, "extra", "x");
            raw.Save();
        }

        Set(mine, "conflict", "mine");
        mine.Remove(EntryName.Parse("gone"), recordForKeePass: true);

        var result = Save(mine, mirror: true);

        Assert.Equal(["changed", "conflict", "removed"], result.Overwritten.Select(n => n.FullName).Order(StringComparer.Ordinal));
        Assert.Equal("extra", Assert.Single(result.Skipped).Name.FullName);
        Assert.Equal("1", RawPassword("changed"));
        Assert.Equal("mine", RawPassword("conflict"));
        Assert.Equal("2", RawPassword("removed"));
        Assert.Equal(["changed", "conflict", "extra", "removed"], Titles());
        Assert.All(Compare(mine).Where(c => c.Name.FullName != "extra"), c => Assert.Equal(KeePassSyncState.Saved, c.State));
    }

    [Fact]
    public void LoadTakesConflictWhenAsked()
    {
        var mine = Body(("a", "1"));
        Save(mine);
        var theirs = Body();
        Load(theirs);
        Set(theirs, "a", "theirs");
        Save(theirs);
        Set(mine, "a", "mine");

        Load(mine, take: true);

        Assert.Equal("theirs", Value(mine, "a"));
    }

    [Fact]
    public void RemovalHereReachesKeePassRecycleBin()
    {
        var body = Body(("a", "1"), ("b", "2"));
        Save(body);
        body.Remove(EntryName.Parse("a"));

        Assert.Equal(0, Load(body).Added);
        Assert.Equal(1, Save(body).Deleted);

        Assert.Equal(["b"], Titles());
        Assert.Empty(body.KeePassRemovals);
        using var raw = OpenRaw();
        Assert.Contains(raw.RootGroup.FindGroup(raw.RecycleBinUuid, true).Entries, e => e.Strings.ReadSafe(PwDefs.TitleField) == "a");
    }

    [Fact]
    public void RemovalWithoutRecycleBinLeavesNoEntry()
    {
        CreateDatabase(database, recycleBin: false);
        var body = Body(("a", "1"));
        Save(body);
        body.Remove(EntryName.Parse("a"));

        Save(body);

        using var raw = OpenRaw();
        Assert.DoesNotContain(raw.RootGroup.GetEntries(true), e => e.Strings.ReadSafe(PwDefs.TitleField) == "a");
    }

    [Fact]
    public void SettingARemovedNameAgainRestoresItsBase()
    {
        var body = Body(("a", "1"));
        Save(body);
        body.Remove(EntryName.Parse("a"));
        Set(body, "a", "2");

        Assert.Empty(body.KeePassRemovals);
        Assert.Equal(1, Save(body).Updated);
        Assert.Equal("2", RawPassword("a"));
    }

    [Fact]
    public void RemovalInKeePassIsAskedOnLoad()
    {
        var mine = Body(("a", "1"));
        Save(mine);
        var theirs = Body();
        Load(theirs);
        theirs.Remove(EntryName.Parse("a"));
        Save(theirs);

        Assert.Equal(KeePassSyncState.RemovedInKeePass, Assert.Single(Save(mine).Skipped).State);
        Assert.Equal(1, Load(mine, remove: false).Kept);
        Assert.Null(mine.Find(EntryName.Parse("a"))!.KeePassBase);
        Assert.Equal(1, Save(mine).Added);

        var other = Body(("x", "1"));
        Save(other);
        var gone = Body();
        Load(gone);
        gone.Remove(EntryName.Parse("x"));
        Save(gone);
        Assert.Equal(1, Load(other, remove: true).Removed);
        Assert.Null(other.Find(EntryName.Parse("x")));
        Assert.Empty(other.KeePassRemovals);
    }

    [Fact]
    public void HandMadeEntriesLoadAsSecretsAndBadTitlesAreSkipped()
    {
        using (var raw = OpenRaw())
        {
            var group = raw.RootGroup.FindCreateGroup("LocalVault", true).FindCreateGroup("proj", true);
            AddRaw(group, "by-hand", "typed in KeePass");
            AddRaw(group, "bad name!", "x");
            AddRaw(group, "twice", "1");
            AddRaw(group, "TWICE", "2");
            raw.Save();
        }

        var body = Body();
        using var keePass = KeePassVault.Open(database, null, Master);
        using var content = keePass.Read(Group);
        KeePassSync.Load(body, content, _ => false, _ => false, Now);

        Assert.Equal("typed in KeePass", Value(body, "by-hand"));
        Assert.Single(body.Entries);
        Assert.Equal(3, content.Skipped.Count);
    }

    [Fact]
    public void SaveMergesChangesMadeToTheFileMeanwhile()
    {
        var body = Body(("a", "1"));
        using (var keePass = KeePassVault.Open(database, null, Master))
        {
            using var content = keePass.Read(Group);
            KeePassSync.Save(body, content, keePass, Group, _ => false);

            using (var other = OpenRaw())
            {
                AddRaw(other.RootGroup, "elsewhere", "x");
                other.Save();
            }

            keePass.Save();
        }

        using var raw = OpenRaw();
        var titles = raw.RootGroup.GetEntries(true).Select(e => e.Strings.ReadSafe(PwDefs.TitleField)).ToList();
        Assert.Contains("a", titles);
        Assert.Contains("elsewhere", titles);
    }

    [Fact]
    public void KeyFileAndWrongPasswordAreHandled()
    {
        var keyFile = directory.Combine("team.keyx");
        KcpKeyFile.Create(keyFile, null);
        var path = directory.Combine("keyed.kdbx");
        CreateDatabase(path, recycleBin: true, keyFile);

        using (KeePassVault.Open(path, keyFile, Master))
        {
        }

        Assert.Throws<VaultException>(() => KeePassVault.Open(path, null, Master));
        var error = Assert.Throws<VaultException>(() => KeePassVault.Open(database, null, "wrong master"));
        Assert.DoesNotContain("wrong master", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportDropsSyncDataAndReplacingKeepsIt()
    {
        var body = Body(("a", "1"), ("b", "2"));
        body.SetSharing(EntryName.Parse("b"), local: true);
        Save(body);
        Set(body, "a", "changed");
        Assert.NotNull(body.Find(EntryName.Parse("a"))!.KeePassBase);
        Assert.True(body.Find(EntryName.Parse("b"))!.IsLocal);
        body.Remove(EntryName.Parse("a"));

        var export = body.ForExport();

        Assert.Empty(export.KeePassRemovals);
        Assert.All(export.Entries, entry => Assert.Null(entry.KeePassBase));
        Assert.True(export.Find(EntryName.Parse("b"))!.IsLocal);
        Assert.NotEmpty(body.WithoutLocalSettings().KeePassRemovals);
    }

    [Fact]
    public void CopySendsSharedEntriesAndRecordsNothing()
    {
        var body = Body(("a", "1"), ("mine", "2"));
        body.SetSharing(EntryName.Parse("mine"), local: true);
        Save(body);
        var other = directory.Combine("other.kdbx");
        CreateDatabase(other, recycleBin: true);
        var before = body.ToUtf8();

        using (var keePass = KeePassVault.Open(other, null, Master))
        {
            Assert.Equal(1, KeePassSync.Copy(body, keePass, Group).Written);
            keePass.Save();
        }

        Assert.Equal(before, body.ToUtf8());
        using var copy = KeePassVault.Open(other, null, Master);
        using var content = copy.Read(Group);
        Assert.Equal(["a"], content.Items.Select(i => i.Name.FullName));
        Assert.All(Compare(body).Where(c => c.State != KeePassSyncState.Local), c => Assert.Equal(KeePassSyncState.Saved, c.State));
    }
    [Fact]
    public void RequiredValuesShareTheirDeclarationOnly()
    {
        var mine = Body();
        mine.Set(VaultEntry.NewRequired(EntryName.Parse("os"), "your own account", Now));
        Set(mine, "os", "my-password");
        Assert.True(mine.Find(EntryName.Parse("os"))!.IsRequired);

        Assert.Equal(1, Save(mine).Added);
        Assert.NotEqual("my-password", RawPassword("os"));
        using (var raw = OpenRaw())
        {
            var entry = raw.RootGroup.GetEntries(true).Single(e => e.Strings.ReadSafe(PwDefs.TitleField) == "os");
            Assert.Equal("your own account", entry.Strings.ReadSafe(PwDefs.NotesField));
            Assert.Equal(VaultEntry.RequiredKind, entry.Strings.ReadSafe(KeePassVault.KindField));
        }

        var theirs = Body();
        Load(theirs);
        var declared = theirs.Find(EntryName.Parse("os"))!;
        Assert.True(declared.IsMissing);
        Assert.Equal("your own account", declared.RequiredNote);

        Set(theirs, "os", "their-password");
        Assert.Equal(0, Save(theirs).Updated);
        Assert.Equal("their-password", Value(theirs, "os"));
        Load(mine);
        Assert.Equal("my-password", Value(mine, "os"));
        Assert.True(mine.ForExport().Find(EntryName.Parse("os"))!.IsMissing);
    }

    [Fact]
    public void DeclaringAnExistingValueRequiredKeepsIt()
    {
        var body = Body(("token", "kept"));
        body.Set(VaultEntry.NewRequired(EntryName.Parse("token"), null, Now));

        var entry = body.Find(EntryName.Parse("token"))!;
        Assert.True(entry.IsRequired);
        Assert.False(entry.IsMissing);
        Assert.Equal("kept", Value(body, "token"));
        Assert.Throws<VaultException>(() => body.SetSharing(EntryName.Parse("token"), local: false));
    }

    [Fact]
    public void EachDatabaseKeepsItsOwnSyncState()
    {
        var body = Body(("a", "1"));
        Save(body);
        var other = directory.Combine("other.kdbx");
        CreateDatabase(other, recycleBin: true);

        using (var keePass = KeePassVault.Open(other, null, Master))
        {
            using var content = keePass.Read(Group);
            var result = KeePassSync.Save(body, content, keePass, Group, _ => false);
            Assert.Equal(1, result.Added);
            keePass.Save();
            KeePassSync.RecordSaved(body, result, Now);
        }

        Assert.Equal(2, body.Find(EntryName.Parse("a"))!.KeePassBases.Count);
        Assert.All(Compare(body), c => Assert.Equal(KeePassSyncState.Saved, c.State));
    }

    [Fact]
    public void BaseWithoutDatabaseIdIsAdoptedByTheNextDatabase()
    {
        Save(Body(("a", "1")));
        var body = VaultBody.Parse(Encoding.UTF8.GetBytes(
            $$"""{ "schema": 2, "entries": [ { "name": "a", "kind": "secret", "value": "MQ==", "meta": { "created": "2026-10-09T12:00:00Z", "kp": { "hash": "{{VaultEntry.ComputeContentHash("secret", null, "1"u8)}}", "at": "2026-10-09T12:00:00Z" } } } ] }"""));
        Save(body);
        Set(body, "a", "2");

        Assert.Equal(1, Save(body).Updated);
        Assert.Equal("2", RawPassword("a"));
        Assert.Single(body.Find(EntryName.Parse("a"))!.KeePassBases);
        Assert.NotNull(body.Find(EntryName.Parse("a"))!.KeePassBases[0].Database);
    }

    [Fact]
    public void KeyringOpensDatabasesFromItsAutoOpenGroup()
    {
        var keyringPath = directory.Combine("keyring.kdbx");
        CreateDatabase(keyringPath, recycleBin: true);
        string id;
        using (var team = KeePassVault.Open(database, null, Master))
        {
            id = team.DatabaseId;
        }

        using (var keyring = KeePassVault.Open(keyringPath, null, Master))
        {
            Assert.Null(keyring.FindAutoOpen(id, [database], "PC1"));
            keyring.RememberAutoOpen("team", "{DB_DIR}\\team.kdbx", null, "team secret", id);
            keyring.Save();
        }

        using (var keyring = KeePassVault.Open(keyringPath, null, Master))
        {
            using var byId = keyring.FindAutoOpen(id, [], "PC1");
            Assert.NotNull(byId);
            Assert.Equal(database, byId.Path, ignoreCase: true);
            Assert.Equal("team secret", new string(byId.Password));
            using var byPath = keyring.FindAutoOpen(null, [database], "PC1");
            Assert.NotNull(byPath);
        }

        using (var raw = new RawDatabase(keyringPath, Key()))
        {
            var entry = raw.RootGroup.FindCreateGroup(KeePassVault.AutoOpenGroup, false).Entries.Single();
            entry.Strings.Set("IfDevice", new ProtectedString(false, "!PC1"));
            raw.Save();
        }

        using (var keyring = KeePassVault.Open(keyringPath, null, Master))
        {
            Assert.Null(keyring.FindAutoOpen(id, [], "PC1"));
            using var elsewhere = keyring.FindAutoOpen(id, [], "PC2");
            Assert.NotNull(elsewhere);
        }
    }

    [Fact]
    public void MailboxNamesRoundTripThroughKeePass()
    {
        var body = Body(("migration/jane+test@example.org", "pw-1"));
        Save(body);

        var other = Body();
        Load(other);

        Assert.Equal("pw-1", Value(other, "migration/jane+test@example.org"));
        Assert.Equal(["migration/jane+test@example.org"], Titles());
    }

    [Fact]
    public void PendingStateNeedsNoDatabase()
    {
        var body = Body(("a", "1"), ("b", "2"), ("c", "3"));
        Save(body);
        Set(body, "b", "changed");
        body.SetSharing(EntryName.Parse("c"), local: true);
        Set(body, "d", "new");
        body.Remove(EntryName.Parse("a"));

        var states = KeePassSync.ComparePending(body).ToDictionary(c => c.Name.FullName, c => c.State);

        Assert.Equal(KeePassSyncState.RemovalPending, states["a"]);
        Assert.Equal(KeePassSyncState.Changed, states["b"]);
        Assert.Equal(KeePassSyncState.Local, states["c"]);
        Assert.Equal(KeePassSyncState.New, states["d"]);
    }

    [Fact]
    public void OriginAndInstallEntriesAreWrittenOnceAndNeverReadAsVaultEntries()
    {
        using var repo = TempDirectory.GitRepository();
        repo.Git("remote", "add", "origin", "https://jdoe:t0ken@dev.azure.com/org/proj/_git/app");
        var location = new VaultLocation(repo.Combine("infra", "secrets.local.lv"), VaultPathSource.Explicit, repo.Path);
        var origin = RepositoryOrigin.Of(location)!;
        var folder = Path.GetFileName(repo.Path);
        IReadOnlyList<string> projectGroup = ["LocalVault", folder];

        using (var keePass = KeePassVault.Open(database, null, Master))
        {
            Assert.True(KeePassSync.MaintainGuides(keePass, location, origin));
            Assert.False(KeePassSync.MaintainGuides(keePass, location, origin));
            keePass.Save();
        }

        using (var keePass = KeePassVault.Open(database, null, Master))
        {
            Assert.False(KeePassSync.MaintainGuides(keePass, location, origin));
            using var content = keePass.Read(projectGroup);
            Assert.Empty(content.Items);
            Assert.Empty(content.Skipped);
            Assert.True(KeePassSync.MaintainGuides(keePass, location, origin with { Url = "https://dev.azure.com/org/proj/_git/moved" }));
            keePass.Save();
        }

        using var raw = OpenRaw();
        var root = raw.RootGroup.FindCreateGroup("LocalVault", false);
        var install = Assert.Single(root.Entries);
        Assert.Equal(KeePassVault.InstallTitle, install.Strings.ReadSafe(PwDefs.TitleField));
        Assert.Contains("install.ps1", install.Strings.ReadSafe(PwDefs.NotesField), StringComparison.Ordinal);
        var entry = Assert.Single(root.FindCreateGroup(folder, false).Entries);
        Assert.Equal(KeePassVault.OriginTitle, entry.Strings.ReadSafe(PwDefs.TitleField));
        Assert.Equal("https://dev.azure.com/org/proj/_git/moved", entry.Strings.ReadSafe(PwDefs.UrlField));
        Assert.Contains($"git clone https://dev.azure.com/org/proj/_git/moved {folder}", entry.Strings.ReadSafe(PwDefs.NotesField), StringComparison.Ordinal);
        Assert.Equal("https://dev.azure.com/org/proj/_git/app", Assert.Single(entry.History).Strings.ReadSafe(PwDefs.UrlField));
    }

    [Fact]
    public void AnInstallEntryMadeByHandIsLeftAsItIs()
    {
        using (var raw = OpenRaw())
        {
            AddRaw(raw.RootGroup.FindCreateGroup("LocalVault", true), "install instructions for LOCALVAULT", "x");
            raw.Save();
        }

        using var keePass = KeePassVault.Open(database, null, Master);
        var loose = new VaultLocation(directory.Combine("loose", "secrets.local.lv"), VaultPathSource.Explicit, null);

        Assert.False(KeePassSync.MaintainGuides(keePass, loose, null));
    }

    [Fact]
    public void AnInstallEntryPointingAtTheRetiredRepositoryGetsTheNewOne()
    {
        const string retired = "git clone https://dev.azure.com/acme/Tools/_git/LocalVault $d; & \"$d\\install.ps1\"";
        using (var raw = OpenRaw())
        {
            var entry = new PwEntry(true, true);
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, KeePassVault.InstallTitle));
            entry.Strings.Set(PwDefs.NotesField, new ProtectedString(false, retired));
            raw.RootGroup.FindCreateGroup("LocalVault", true).AddEntry(entry, true);
            raw.Save();
        }

        var loose = new VaultLocation(directory.Combine("loose", "secrets.local.lv"), VaultPathSource.Explicit, null);
        using (var keePass = KeePassVault.Open(database, null, Master))
        {
            Assert.True(KeePassSync.MaintainGuides(keePass, loose, null));
            Assert.False(KeePassSync.MaintainGuides(keePass, loose, null));
            keePass.Save();
        }

        var install = Assert.Single(History(KeePassVault.InstallTitle).Skip(1));
        Assert.Equal($"git clone {KeePassVault.RepositoryUrl} $d; & \"$d\\install.ps1\"", install.Strings.ReadSafe(PwDefs.NotesField));
        Assert.Equal(retired, Assert.Single(History(KeePassVault.InstallTitle).SkipLast(1)).Strings.ReadSafe(PwDefs.NotesField));
    }

    [Fact]
    public void AnInstallEntryMadeByTheModuleGetsTheCurrentInstallLine()
    {
        using (var raw = OpenRaw())
        {
            var entry = new PwEntry(true, true);
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, KeePassVault.InstallTitle));
            entry.Strings.Set(PwDefs.NotesField, new ProtectedString(false, "git clone https://dev.azure.com/acme/Tools/_git/LocalVault $d"));
            entry.Strings.Set(KeePassVault.KindField, new ProtectedString(false, KeePassVault.InstallKind));
            raw.RootGroup.FindCreateGroup("LocalVault", true).AddEntry(entry, true);
            raw.Save();
        }

        using (var keePass = KeePassVault.Open(database, null, Master))
        {
            Assert.True(KeePassSync.MaintainGuides(keePass, new VaultLocation(directory.Combine("loose", "secrets.local.lv"), VaultPathSource.Explicit, null), null));
            keePass.Save();
        }

        Assert.Equal(KeePassVault.InstallCommand, History(KeePassVault.InstallTitle)[^1].Strings.ReadSafe(PwDefs.NotesField));
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("skills/localvault/SKILL.md")]
    public void InstallEntryUsesTheDocumentedInstallLine(string document) =>
        Assert.Contains(KeePassVault.InstallCommand, File.ReadAllText(RepositoryFile(document)), StringComparison.Ordinal);

    [Fact]
    public void ARepositoryWithoutOriginHasNone()
    {
        using var repo = TempDirectory.GitRepository();

        Assert.Null(RepositoryOrigin.Of(new VaultLocation(repo.Combine("secrets.local.lv"), VaultPathSource.GitRoot, repo.Path)));
    }

    [Theory]
    [InlineData("https://user:pat@dev.azure.com/org/p/_git/r", "https://dev.azure.com/org/p/_git/r")]
    [InlineData("https://org@dev.azure.com/org/p/_git/r", "https://dev.azure.com/org/p/_git/r")]
    [InlineData("ssh://git:secret@host:22/r.git", "ssh://git@host:22/r.git")]
    [InlineData("ssh://git@host/r.git", "ssh://git@host/r.git")]
    [InlineData("git@github.com:o/r.git", "git@github.com:o/r.git")]
    [InlineData("https://host/a@b", "https://host/a@b")]
    [InlineData("C:/repos/r", "C:/repos/r")]
    public void OriginUrlLosesItsCredentials(string url, string expected) =>
        Assert.Equal(expected, RepositoryOrigin.WithoutCredentials(url));

    private static string RepositoryFile(string relative, [CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", relative));

    private static VaultBody Body(params (string Name, string Value)[] entries)
    {
        var body = VaultBody.CreateEmpty();
        foreach (var (name, value) in entries)
        {
            Set(body, name, value);
        }

        return body;
    }

    private static void Set(VaultBody body, string name, string value) =>
        body.Set(VaultEntry.NewSecret(EntryName.Parse(name), Encoding.UTF8.GetBytes(value), Now));

    private static string Value(VaultBody body, string name) =>
        Encoding.UTF8.GetString(body.Find(EntryName.Parse(name))!.GetValue());

    private static void AddRaw(PwGroup group, string title, string password)
    {
        var entry = new PwEntry(true, true);
        entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
        entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, password));
        group.AddEntry(entry, true);
    }

    private static CompositeKey Key(string? keyFile = null)
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(Master));
        if (keyFile is not null)
        {
            key.AddUserKey(new KcpKeyFile(keyFile));
        }

        return key;
    }

    private static void CreateDatabase(string path, bool recycleBin, string? keyFile = null)
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(path), Key(keyFile));
        var kdf = new AesKdf().GetDefaultParameters();
        kdf.SetUInt64(AesKdf.ParamRounds, 1000);
        db.KdfParameters = kdf;
        db.RecycleBinEnabled = recycleBin;
        db.Save(null);
        db.Close();
    }

    private KeePassSaveResult Save(VaultBody body, bool replace = false, bool mirror = false)
    {
        using var keePass = KeePassVault.Open(database, null, Master);
        using var content = keePass.Read(Group);
        var result = KeePassSync.Save(body, content, keePass, Group, _ => replace, mirror);
        keePass.Save();
        KeePassSync.RecordSaved(body, result, Now);
        return result;
    }

    private KeePassLoadResult Load(VaultBody body, bool take = false, bool remove = false)
    {
        using var keePass = KeePassVault.Open(database, null, Master);
        using var content = keePass.Read(Group);
        return KeePassSync.Load(body, content, _ => take, _ => remove, Now);
    }

    private IReadOnlyList<KeePassComparison> Compare(VaultBody body)
    {
        using var keePass = KeePassVault.Open(database, null, Master);
        using var content = keePass.Read(Group);
        return KeePassSync.Compare(body, content);
    }

    private RawDatabase OpenRaw() => new(database, Key());

    private List<string> Titles()
    {
        using var raw = OpenRaw();
        return raw.RootGroup.FindCreateGroup("LocalVault", false).FindCreateGroup("proj", false).Entries
            .Select(e => e.Strings.ReadSafe(PwDefs.TitleField)).Order(StringComparer.Ordinal).ToList();
    }

    private string RawPassword(string title)
    {
        using var raw = OpenRaw();
        return raw.RootGroup.GetEntries(true).Single(e => e.Strings.ReadSafe(PwDefs.TitleField) == title)
            .Strings.ReadSafe(PwDefs.PasswordField);
    }

    private List<PwEntry> History(string title)
    {
        using var raw = OpenRaw();
        var entry = raw.RootGroup.GetEntries(true).Single(e => e.Strings.ReadSafe(PwDefs.TitleField) == title);
        return [.. entry.History, entry];
    }

    private sealed class RawDatabase : IDisposable
    {
        private readonly PwDatabase db = new();

        public RawDatabase(string path, CompositeKey key) => db.Open(IOConnectionInfo.FromPath(path), key, null);

        public PwGroup RootGroup => db.RootGroup;

        public PwUuid RecycleBinUuid => db.RecycleBinUuid;

        public void Save() => db.Save(null);

        public void Dispose() => db.Close();
    }
}
