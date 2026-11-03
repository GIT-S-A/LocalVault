using LocalVault.Core;

namespace LocalVault.Core.Tests;

public sealed class LeftoverBackupsTests : IDisposable
{
    private const string Id = "0123456789abcdef";
    private const string OtherId = "fedcba9876543210";
    private const string Machine = "PC1";
    private readonly TempDirectory directory = new();

    public void Dispose() => directory.Dispose();

    private VaultLocation Location => new(directory.Combine("repo", "secrets.local.lv"), VaultPathSource.Explicit, directory.Combine("repo"));

    [Fact]
    public void SettingsFolderYieldsOnlyThisVaultsBackups()
    {
        var folder = Directory.CreateDirectory(directory.Combine("custom")).FullName;
        var own = Touch(folder, $"20261008T120000000Z-{Id}.lvbak");
        Touch(folder, $"20261008T120000000Z-{OtherId}.lvbak");
        Touch(folder, "20261008T120000000Z.lvbak");
        Touch(folder, "notes.txt");

        var found = LeftoverBackups.Find(Location, Settings(folder), Machine, null);

        Assert.Equal([own], found.Files);
        Assert.True(found.HasSettings);
        Assert.Null(found.UnresolvedDestination);
    }

    [Fact]
    public void DefaultFolderIsSearchedWithoutSettings()
    {
        var oneDrive = directory.Combine("OneDrive");
        var folder = Directory.CreateDirectory(LeftoverBackups.DefaultFolder(Location, Machine, oneDrive)!).FullName;
        var first = Touch(folder, $"20261008T120000000Z-{Id}.lvbak");
        var second = Touch(folder, "20261007T120000000Z.lvbak");

        var found = LeftoverBackups.Find(Location, null, Machine, oneDrive);

        Assert.Equal([second, first], found.Files.Order(StringComparer.Ordinal));
        Assert.False(found.HasSettings);
        Assert.True(found.Any);
    }

    [Fact]
    public void NothingFoundWithoutSettingsOrOneDrive()
    {
        Assert.False(LeftoverBackups.Find(Location, null, Machine, null).Any);
    }

    [Fact]
    public void DeleteRemovesTheFilesAndFoldersLeftEmpty()
    {
        var oneDrive = Directory.CreateDirectory(directory.Combine("OneDrive")).FullName;
        var folder = Directory.CreateDirectory(LeftoverBackups.DefaultFolder(Location, Machine, oneDrive)!).FullName;
        Touch(folder, $"20261008T120000000Z-{Id}.lvbak");

        LeftoverBackups.Find(Location, Settings(@"%LV_TEST_UNSET_VARIABLE%\LocalVault"), Machine, oneDrive).Delete();

        Assert.False(Directory.Exists(folder));
        Assert.False(Directory.Exists(Path.Combine(oneDrive, LeftoverBackups.RootFolder)));
        Assert.True(Directory.Exists(oneDrive));
    }

    [Fact]
    public void DeleteKeepsFoldersHoldingOtherFiles()
    {
        var folder = Directory.CreateDirectory(directory.Combine("custom")).FullName;
        var own = Touch(folder, $"20261008T120000000Z-{Id}.lvbak");
        var other = Touch(folder, $"20261008T120000000Z-{OtherId}.lvbak");

        LeftoverBackups.Find(Location, Settings(folder), Machine, null).Delete();

        Assert.False(File.Exists(own));
        Assert.True(File.Exists(other));
    }

    [Theory]
    [InlineData(@"%LV_TEST_UNSET_VARIABLE%\LocalVault")]
    [InlineData(@"relative\folder")]
    public void UnresolvedDestinationIsReported(string destination)
    {
        var found = LeftoverBackups.Find(Location, Settings(destination), Machine, null);

        Assert.Equal(destination, found.UnresolvedDestination);
        Assert.Empty(found.Files);
        Assert.True(found.Any);
    }

    [Fact]
    public void FolderNameCombinesProjectHashAndMachine()
    {
        var location = new VaultLocation(@"C:\dev\proj\infra\secrets.local.lv", VaultPathSource.Explicit, @"C:\dev\proj");

        var name = LeftoverBackups.FolderName(location, Machine);

        Assert.Matches(@"^proj-[0-9a-f]{8}-PC1$", name);
        Assert.Equal(name, LeftoverBackups.FolderName(location with { Path = @"c:\DEV\proj\infra\secrets.local.lv" }, Machine));
    }

    private static BackupSettings Settings(string destination) =>
        BackupSettings.Create(Id, destination, "backup passphrase".AsSpan(), new byte[Envelope.KeyFileLength]);

    private static string Touch(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, "sealed");
        return path;
    }
}
