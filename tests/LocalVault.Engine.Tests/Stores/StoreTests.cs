using LocalVault.Core;

namespace LocalVault.Engine.Tests;

public sealed class StoreRegistryTests : IDisposable
{
    private readonly TempDirectory directory = new();

    public void Dispose() => directory.Dispose();

    [Fact]
    public void EmptyRegistryHasNoDefault()
    {
        var registry = NewRegistry();

        Assert.Empty(registry.Load());
        Assert.Null(registry.Default);
    }

    [Fact]
    public void SoleStoreIsTheDefault()
    {
        var registry = NewRegistry();
        registry.Register("work", Touch("work.kdbx"), null, makeDefault: false);

        Assert.Equal("work", registry.Default!.Name);
    }

    [Fact]
    public void DefaultIsMarkedAndSeveralWithoutDefaultHaveNone()
    {
        var registry = NewRegistry();
        registry.Register("work", Touch("work.kdbx"), null, makeDefault: false);
        registry.Register("home", Touch("home.kdbx"), null, makeDefault: false);
        Assert.Null(registry.Default);

        registry.Register("home", Touch("home.kdbx"), Touch("home.key"), makeDefault: true);

        Assert.Equal("home", registry.Default!.Name);
        Assert.Equal(directory.Combine("home.key"), registry.FindByName("HOME")!.KeyFile);
        Assert.Equal(StoreTypes.KeePass, registry.FindByName("work")!.Type);
    }

    [Fact]
    public void RegisterUpdatesAndUnregisterRemovesDefault()
    {
        var registry = NewRegistry();
        registry.Register("work", Touch("a.kdbx"), null, makeDefault: true);
        registry.Register("Work", Touch("b.kdbx"), null, makeDefault: false);

        var single = Assert.Single(registry.Load());
        Assert.Equal(directory.Combine("b.kdbx"), single.Target);
        Assert.True(single.IsDefault);

        Assert.True(registry.Unregister("WORK"));
        Assert.False(registry.Unregister("work"));
        Assert.Empty(registry.Load());
        Assert.Null(registry.Default);
    }

    [Fact]
    public void UnlockingStoreMustExistMustNotLoopAndCannotBeRemovedWhileUsed()
    {
        var registry = NewRegistry();
        registry.Register("mine", Touch("mine.kdbx"), null, makeDefault: false);
        Assert.Throws<VaultException>(() => registry.Register("team", Touch("team.kdbx"), null, false, unlockWith: "nobody"));

        registry.Register("team", Touch("team.kdbx"), null, false, unlockWith: "MINE");
        registry.Register("proj", Touch("proj.kdbx"), null, false, unlockWith: "team");

        Assert.Equal("mine", registry.FindByName("team")!.UnlockWith);
        Assert.Contains("already unlocks", Assert.Throws<VaultException>(() => registry.Register("mine", Touch("mine.kdbx"), null, false, unlockWith: "proj")).Message, StringComparison.Ordinal);
        Assert.Throws<VaultException>(() => registry.Register("mine", Touch("mine.kdbx"), null, false, unlockWith: "mine"));
        Assert.Contains("unlocks team", Assert.Throws<VaultException>(() => registry.Unregister("mine")).Message, StringComparison.Ordinal);

        registry.Register("team", Touch("team.kdbx"), null, false);
        Assert.Null(registry.FindByName("team")!.UnlockWith);
        Assert.True(registry.Unregister("mine"));
    }

    [Fact]
    public void BetaSevenKeyringBecomesTheUnlockingStoreOfTheOthers()
    {
        var registry = NewRegistry();
        Directory.CreateDirectory(Path.GetDirectoryName(registry.RegistryPath)!);
        File.WriteAllText(registry.RegistryPath, $$"""
            { "schema": 1, "keyring": "mine",
              "stores": [
                { "name": "mine", "type": "keepass", "target": {{System.Text.Json.JsonSerializer.Serialize(Touch("mine.kdbx"))}} },
                { "name": "team", "type": "keepass", "target": {{System.Text.Json.JsonSerializer.Serialize(Touch("team.kdbx"))}} } ] }
            """);

        var stores = registry.Load();

        Assert.Null(stores.Single(s => s.Name == "mine").UnlockWith);
        Assert.Equal("mine", stores.Single(s => s.Name == "team").UnlockWith);
        Assert.DoesNotContain("keyring", File.ReadAllText(registry.RegistryPath), StringComparison.Ordinal);
    }

    [Fact]
    public void ChangingTheTargetForgetsTheId()
    {
        var registry = NewRegistry();
        var store = registry.Register("work", Touch("a.kdbx"), null, makeDefault: false);
        registry.RecordId(store, "id-a");
        Assert.Equal("work", registry.FindById("ID-A")!.Name);

        registry.Register("work", Touch("b.kdbx"), null, makeDefault: false);

        Assert.Null(registry.FindById("id-a"));
    }

    [Fact]
    public void EnvironmentVariablesStayLiteralAndExpandOnUse()
    {
        Touch("db.kdbx");
        Environment.SetEnvironmentVariable("LV_TEST_KP_DIR", directory.Path);
        try
        {
            var registry = NewRegistry();
            registry.Register("env", @"%LV_TEST_KP_DIR%\db.kdbx", null, makeDefault: false);

            var store = registry.FindByName("env")!;
            Assert.Equal(@"%LV_TEST_KP_DIR%\db.kdbx", store.Target);
            Assert.Equal(directory.Combine("db.kdbx"), store.ExpandedTarget);
            Assert.Equal("env", registry.FindByTarget(directory.Combine("db.kdbx"))!.Name);
            Assert.Contains("%LV_TEST_KP_DIR%", File.ReadAllText(registry.RegistryPath), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LV_TEST_KP_DIR", null);
        }
    }

    [Fact]
    public void RegisterRefusesMissingFilesBadNamesAndUnknownTypes()
    {
        var registry = NewRegistry();

        Assert.Throws<VaultException>(() => registry.Register("x", directory.Combine("missing.kdbx"), null, false));
        Assert.Throws<VaultException>(() => registry.Register("x", Touch("x.kdbx"), directory.Combine("missing.key"), false));
        Assert.Throws<VaultException>(() => registry.Register("a b", Touch("y.kdbx"), null, false));
        var unsupported = Assert.Throws<VaultException>(() => registry.Register("az", "azkv:git-saas-prod", null, false));
        Assert.Contains("'azkv'", unsupported.Message, StringComparison.Ordinal);
        Assert.Throws<VaultException>(() => registry.Register("txt", Touch("notes.txt"), null, false));
        Assert.False(File.Exists(registry.RegistryPath));
    }

    [Theory]
    [InlineData(@"S:\shared\KeePass\team.kdbx", "team")]
    [InlineData(@"D:\My DB (old).kdbx", "My-DB-old")]
    [InlineData(@"D:\(().kdbx", "store")]
    public void NameFromTargetUsesTheFileName(string path, string expected) =>
        Assert.Equal(expected, StoreTypes.NameFromTarget(path));

    [Fact]
    public void UnknownFieldsSurvive()
    {
        var registry = NewRegistry();
        Directory.CreateDirectory(Path.GetDirectoryName(registry.RegistryPath)!);
        File.WriteAllText(registry.RegistryPath, """{ "schema": 1, "future": 7, "stores": [] }""");

        registry.Register("work", Touch("work.kdbx"), null, makeDefault: false);

        Assert.Contains("\"future\": 7", File.ReadAllText(registry.RegistryPath), StringComparison.Ordinal);
    }

    [Fact]
    public void KeePassRegistryIsConvertedOnceAndLeftAsItWas()
    {
        var appData = directory.CreateDirectory("appdata");
        var legacy = Path.Combine(appData, StoreRegistry.LegacyFileName);
        var legacyText = $$"""
            { "schema": 1, "default": "team", "keyring": "mine",
              "databases": [
                { "name": "team", "path": {{System.Text.Json.JsonSerializer.Serialize(Touch("team.kdbx"))}}, "id": "abc" },
                { "name": "mine", "path": "%OneDrive%\\mine.kdbx", "keyFile": "C:\\keys\\mine.key" } ] }
            """;
        File.WriteAllText(legacy, legacyText);
        var registry = NewRegistry();

        var stores = registry.Load();

        Assert.Equal(["team", "mine"], stores.Select(s => s.Name));
        Assert.Equal("abc", stores[0].Id);
        Assert.True(stores[0].IsDefault);
        Assert.Equal("mine", stores[0].UnlockWith);
        Assert.Null(stores[1].UnlockWith);
        Assert.Equal(@"%OneDrive%\mine.kdbx", stores[1].Target);
        Assert.Equal(@"C:\keys\mine.key", stores[1].KeyFile);
        Assert.All(stores, s => Assert.Equal(StoreTypes.KeePass, s.Type));
        Assert.True(File.Exists(registry.RegistryPath));
        Assert.Equal(legacyText, File.ReadAllText(legacy));

        registry.Unregister("team");
        Assert.Single(new StoreRegistry(registry.RegistryPath).Load());
    }

    private StoreRegistry NewRegistry() => new(directory.Combine("appdata", StoreRegistry.FileName));

    private string Touch(string name)
    {
        var path = directory.Combine(name);
        File.WriteAllText(path, "x");
        return path;
    }
}

public sealed class ProjectFileTests : IDisposable
{
    private readonly TempDirectory repository = TempDirectory.GitRepository();

    public void Dispose() => repository.Dispose();

    private VaultLocation Root => VaultLocation.Resolve(null, null, repository.Path);

    private VaultLocation Sub(string folder) =>
        VaultLocation.Resolve(Path.Combine(repository.CreateDirectory(folder), "secrets.local.lv"), null, repository.Path);

    [Fact]
    public void SchemaOneIsReadAsSchemaTwoAndRewrittenOnWrite()
    {
        File.WriteAllText(repository.Combine(ProjectFile.FileName), """
            { "schema": 1, "extra": true,
              "stores": [ { "role": "primary", "type": "keepass", "id": "abc123", "label": "team" }, { "role": "mirror", "type": "other" } ],
              "vaults": [ "secrets.local.lv", "infra/secrets.local.lv" ] }
            """);

        Assert.Equal(new StoreReference("team", "abc123"), ProjectFile.ReadDefault(Root));
        Assert.Equal(new StoreReference("team", "abc123"), ProjectFile.StoreFor(Sub("infra")));
        Assert.Equal([repository.Combine("secrets.local.lv"), repository.Combine("infra", "secrets.local.lv")], ProjectFile.ReadVaults(Root).Select(v => v.Path));
        Assert.Empty(ProjectFile.ReadStores(Root));

        Assert.True(ProjectFile.AddVault(Sub("migration")));

        var text = File.ReadAllText(repository.Combine(ProjectFile.FileName));
        Assert.Contains("\"schema\": 2", text, StringComparison.Ordinal);
        Assert.Contains("mirror", text, StringComparison.Ordinal);
        Assert.Contains("extra", text, StringComparison.Ordinal);
        Assert.DoesNotContain("primary", text, StringComparison.Ordinal);
        Assert.Equal(3, ProjectFile.ReadVaults(Root).Count);
        Assert.Equal(new StoreReference("team", "abc123"), ProjectFile.ReadDefault(Root));
    }

    [Fact]
    public void StringVaultItemsWrittenByBetaSixAreTolerated()
    {
        File.WriteAllText(repository.Combine(ProjectFile.FileName), """
            { "schema": 2, "stores": [], "vaults": [ { "path": "infra/secrets.local.lv", "store": "mine" }, "migration/secrets.local.lv" ] }
            """);

        Assert.Equal(["infra/secrets.local.lv", "migration/secrets.local.lv"],
            ProjectFile.ReadVaults(Root).Select(v => Path.GetRelativePath(repository.Path, v.Path).Replace('\\', '/')));
        Assert.Equal(new StoreReference("mine", null), ProjectFile.StoreFor(Sub("infra")));
        Assert.Null(ProjectFile.StoreFor(Sub("migration")));
    }

    [Fact]
    public void AssignToOneVaultOrToTheWholeRepository()
    {
        ProjectFile.AddVault(Sub("infra"));
        ProjectFile.Assign(Sub("migration"), new StoreReference("mine", "id-m"), wholeRepository: false);
        Assert.Equal(new StoreReference("mine", "id-m"), ProjectFile.StoreFor(Sub("migration")));
        Assert.Null(ProjectFile.StoreFor(Sub("infra")));

        ProjectFile.Assign(Root, new StoreReference("team", "id-t"), wholeRepository: true);

        Assert.Equal(new StoreReference("team", "id-t"), ProjectFile.StoreFor(Sub("infra")));
        Assert.Equal(new StoreReference("team", "id-t"), ProjectFile.StoreFor(Sub("migration")));
        Assert.Equal(2, ProjectFile.ReadVaults(Root).Count);
    }

    [Fact]
    public void ProjectEntriesAreDefinitionsOrReferencesAndCanBeMirrors()
    {
        ProjectFile.AddStore(Root, "team", null, makeDefault: true);
        ProjectFile.AddStore(Root, "offsite", @"I:\Archive\x.kdbx", makeDefault: false, mirror: true);
        ProjectFile.AddStore(Root, "mine", null, makeDefault: false, mirror: true);

        Assert.Equal(
            [new ProjectStoreEntry("team", null, null, false), new ProjectStoreEntry("offsite", @"I:\Archive\x.kdbx", null, true), new ProjectStoreEntry("mine", null, null, true)],
            ProjectFile.ReadEntries(Root));
        Assert.Equal("offsite", Assert.Single(ProjectFile.ReadStores(Root)).Name);
        Assert.True(ProjectFile.ReadStores(Root)[0].IsMirror);
        Assert.Equal("team", ProjectFile.ReadDefault(Root)!.Name);

        ProjectFile.AddStore(Root, "offsite", @"I:\Archive\x.kdbx", makeDefault: false);
        Assert.False(ProjectFile.ReadEntries(Root).Single(e => e.Name == "offsite").IsMirror);
        Assert.True(ProjectFile.RemoveStore(Root, "mine"));
        Assert.Equal(["team", "offsite"], ProjectFile.ReadEntries(Root).Select(e => e.Name));
    }

    [Fact]
    public void ProjectStoresAreAddedAndRemovedOnlyWhenUnused()
    {
        ProjectFile.AddStore(Root, "proj", @"I:\Projects\X\x.kdbx", makeDefault: true);
        ProjectFile.RecordStoreId(Root, "proj", "id-p");

        var store = Assert.Single(ProjectFile.ReadStores(Root));
        Assert.Equal(new StoreDefinition("proj", StoreTypes.KeePass, @"I:\Projects\X\x.kdbx", null, StoreScope.Project, "id-p"), store);
        Assert.Equal("proj", ProjectFile.ReadDefault(Root)!.Name);

        var used = Assert.Throws<VaultException>(() => ProjectFile.RemoveStore(Root, "proj"));
        Assert.Contains("project default", used.Message, StringComparison.Ordinal);

        ProjectFile.Assign(Root, new StoreReference("other", null), wholeRepository: true);
        Assert.True(ProjectFile.RemoveStore(Root, "PROJ"));
        Assert.False(ProjectFile.RemoveStore(Root, "proj"));
        Assert.Empty(ProjectFile.ReadStores(Root));
    }

    [Fact]
    public void NewerSchemaAndDamagedFilesAreErrors()
    {
        File.WriteAllText(repository.Combine(ProjectFile.FileName), """{ "schema": 3, "stores": [] }""");
        Assert.Contains("newer", Assert.Throws<VaultException>(() => ProjectFile.ReadStores(Root)).Message, StringComparison.Ordinal);

        File.WriteAllText(repository.Combine(ProjectFile.FileName), "{ not json");
        Assert.Throws<VaultException>(() => ProjectFile.ReadDefault(Root));
    }
}

public sealed class StoreResolverTests : IDisposable
{
    private readonly TempDirectory repository = TempDirectory.GitRepository();
    private readonly TempDirectory appData = new();

    public void Dispose()
    {
        repository.Dispose();
        appData.Dispose();
    }

    private StoreRegistry Registry => new(appData.Combine(StoreRegistry.FileName));

    private VaultLocation Root => VaultLocation.Resolve(null, null, repository.Path);

    private VaultLocation Sub(string folder) =>
        VaultLocation.Resolve(Path.Combine(repository.CreateDirectory(folder), "secrets.local.lv"), null, repository.Path);

    [Fact]
    public void WithoutAnythingTheProblemNamesStoreAdd()
    {
        var resolution = StoreResolver.For(Root, null, Registry);

        Assert.Null(resolution.Definition);
        Assert.Contains("lv store add", resolution.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutProjectFileTheDefaultCommonStoreIsUsed()
    {
        Registry.Register("team", Touch("team.kdbx"), null, makeDefault: true);
        Registry.Register("mine", Touch("mine.kdbx"), null, makeDefault: false);

        Assert.Equal("team", StoreResolver.For(Root, null, Registry).Definition!.Name);
        Assert.Equal("mine", StoreResolver.For(Root, "mine", Registry).Definition!.Name);
    }

    [Fact]
    public void ProjectStoreWinsOverCommonStoreOfTheSameName()
    {
        Registry.Register("team", Touch("pc-team.kdbx"), null, makeDefault: true);
        ProjectFile.AddStore(Root, "team", Touch("project-team.kdbx"), makeDefault: true);

        var resolution = StoreResolver.For(Root, null, Registry);

        Assert.Equal(StoreScope.Project, resolution.Definition!.Scope);
        Assert.Equal(appData.Combine("project-team.kdbx"), resolution.Definition.Target);
    }

    [Fact]
    public void ProjectStoreTakesKeyFileAndReachablePathFromTheCommonStoreWithTheSameId()
    {
        var local = Registry.Register("mycopy", Touch("copy.kdbx"), Touch("copy.key"), makeDefault: false);
        Registry.RecordId(local, "id-p");
        ProjectFile.AddStore(Root, "proj", @"Z:\nowhere\x.kdbx", makeDefault: true);
        ProjectFile.RecordStoreId(Root, "proj", "id-p");

        var store = StoreResolver.For(Root, null, Registry).Definition!;

        Assert.Equal("proj", store.Name);
        Assert.Equal(appData.Combine("copy.kdbx"), store.Target);
        Assert.Equal(appData.Combine("copy.key"), store.KeyFile);
    }

    [Fact]
    public void CommonStoreIsFoundByIdWhateverItsNameOnThisPc()
    {
        var mine = Registry.Register("perso", Touch("jdoe.kdbx"), null, makeDefault: false);
        Registry.RecordId(mine, "id-m");
        Registry.Register("team", Touch("team.kdbx"), null, makeDefault: true);
        ProjectFile.Assign(Sub("migration"), new StoreReference("mine", "id-m"), wholeRepository: false);

        Assert.Equal("perso", StoreResolver.For(Sub("migration"), null, Registry).Definition!.Name);
        Assert.Equal("id-m", StoreResolver.For(Sub("migration"), null, Registry).DatabaseId);
    }

    [Fact]
    public void MirrorsComeFromTheProjectAndFromThisPcWithoutThePrimaryOrDuplicates()
    {
        var team = Registry.Register("team", Touch("team.kdbx"), null, makeDefault: true);
        Registry.RecordId(team, "id-t");
        Registry.Register("mine", Touch("mine.kdbx"), null, makeDefault: false, mirror: true);
        Assert.True(Registry.FindByName("mine")!.IsMirror);
        ProjectFile.AddStore(Root, "team", null, makeDefault: true);
        ProjectFile.AddStore(Root, "offsite", Touch("offsite.kdbx"), makeDefault: false, mirror: true);
        ProjectFile.AddStore(Root, "mine", null, makeDefault: false, mirror: true);
        ProjectFile.AddStore(Root, "absent", null, makeDefault: false, mirror: true);

        var primary = StoreResolver.For(Root, null, Registry);
        var mirrors = StoreResolver.MirrorsFor(Root, primary, Registry);

        Assert.Equal("team", primary.Definition!.Name);
        Assert.Equal(StoreScope.Computer, primary.Definition.Scope);
        Assert.Equal(["offsite", "mine", "absent"], mirrors.Select(m => m.Label));
        Assert.Equal(StoreScope.Project, mirrors[0].Definition!.Scope);
        Assert.Contains("isn't defined on this PC", mirrors[2].Problem, StringComparison.Ordinal);

        Registry.Register("team", Touch("team.kdbx"), null, makeDefault: true, mirror: true);
        Assert.DoesNotContain("team", StoreResolver.MirrorsFor(Root, StoreResolver.For(Root, null, Registry), Registry).Select(m => m.Label));
    }

    [Fact]
    public void NamedStoreMissingOnThisPcIsAProblemAndNeverTheDefault()
    {
        Registry.Register("team", Touch("team.kdbx"), null, makeDefault: true);
        ProjectFile.Assign(Sub("migration"), new StoreReference("mine", "id-m"), wholeRepository: false);

        var resolution = StoreResolver.For(Sub("migration"), null, Registry);

        Assert.Null(resolution.Definition);
        Assert.Contains("migration/secrets.local.lv uses store 'mine', which isn't defined on this PC", resolution.Problem, StringComparison.Ordinal);
        Assert.Equal("id-m", resolution.DatabaseId);
    }

    private string Touch(string name)
    {
        var path = appData.Combine(name);
        File.WriteAllText(path, "x");
        return path;
    }
}
