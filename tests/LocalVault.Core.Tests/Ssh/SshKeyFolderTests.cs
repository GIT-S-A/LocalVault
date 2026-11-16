using System.Text;

namespace LocalVault.Core.Tests;

public sealed class SshKeyFolderTests : IDisposable
{
    private const string Origin = "https://github.com/example/proj.git";
    private static readonly DateTimeOffset Earlier = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory directory = new();
    private readonly string folder;
    private readonly string cloneA;
    private readonly string cloneB;

    public SshKeyFolderTests()
    {
        folder = directory.Combine("ssh", "proj");
        cloneA = directory.Combine("a", "secrets.local.lv");
        cloneB = directory.Combine("b", "secrets.local.lv");
    }

    public void Dispose() => directory.Dispose();

    [Fact]
    public void FirstWriteIsPlannedAndRecorded()
    {
        using (var keys = SshKeyFolder.Open(folder))
        {
            Assert.Equal(SshKeyWriteState.Write, keys.Plan("deploy", Origin, cloneA, "KEY A"u8, Earlier, overwrite: false).State);
            WriteAndRecord(keys, "KEY A", cloneA, Now);
        }

        using var reopened = SshKeyFolder.Open(folder);
        var record = reopened.Keys["deploy"];
        Assert.Equal(Origin, reopened.Origin);
        Assert.Equal(SshKeyFolder.HashOf("KEY A"u8), record.Hash);
        Assert.Equal(Path.GetFullPath(cloneA), record.WrittenBy);
        Assert.Equal([Path.GetFullPath(cloneA)], record.Users);
    }

    [Fact]
    public void FolderOfAnotherRepositoryIsRefused()
    {
        using var keys = SshKeyFolder.Open(folder);
        WriteAndRecord(keys, "KEY A", cloneA, Now);

        var plan = keys.Plan("deploy", "https://github.com/other/proj.git", cloneB, "KEY B"u8, Now, overwrite: true);

        Assert.Equal(SshKeyWriteState.OtherRepository, plan.State);
        Assert.True(plan.Refused);
    }

    [Fact]
    public void FolderOfRepositoryWithoutOriginIsRefusedToOneWithOrigin()
    {
        using var keys = SshKeyFolder.Open(folder);
        keys.Record("deploy", null, cloneA, "KEY A"u8, Now);
        keys.Save();

        Assert.Equal(string.Empty, keys.Origin);
        Assert.Equal(SshKeyWriteState.OtherRepository, keys.Plan("deploy", Origin, cloneB, "KEY B"u8, Now, overwrite: true).State);
        Assert.Equal(SshKeyWriteState.Write, keys.Plan("deploy", null, cloneB, "KEY B"u8, Now, overwrite: true).State);
    }

    [Fact]
    public void UnclaimedFolderBelongsToAnyRepository()
    {
        using var keys = SshKeyFolder.Open(folder);

        Assert.Null(keys.Origin);
        Assert.True(keys.BelongsTo(Origin));
        Assert.True(keys.BelongsTo(null));
    }

    [Fact]
    public void FileNotWrittenByLocalVaultIsRefusedUnlessOverwritten()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "deploy"), "HAND MADE");
        using var keys = SshKeyFolder.Open(folder);

        Assert.Equal(SshKeyWriteState.NotWrittenByLocalVault, keys.Plan("deploy", Origin, cloneA, "KEY A"u8, Now, overwrite: false).State);
        Assert.Equal(SshKeyWriteState.Write, keys.Plan("deploy", Origin, cloneA, "KEY A"u8, Now, overwrite: true).State);
    }

    [Fact]
    public void NewerKeyFromAnotherCloneIsRefusedAndAdopted()
    {
        using var keys = SshKeyFolder.Open(folder);
        WriteAndRecord(keys, "NEW KEY", cloneA, Now);

        Assert.Equal(SshKeyWriteState.NewerElsewhere, keys.Plan("deploy", Origin, cloneB, "OLD KEY"u8, Earlier, overwrite: false).State);
        Assert.Equal(SshKeyWriteState.Write, keys.Plan("deploy", Origin, cloneB, "OLD KEY"u8, Earlier, overwrite: true).State);
        Assert.Equal(SshKeyWriteState.Identical, keys.Plan("deploy", Origin, cloneB, "NEW KEY"u8, Earlier, overwrite: false).State);

        Assert.NotNull(keys.NewerThan("deploy", "OLD KEY"u8, Earlier));
        Assert.Null(keys.NewerThan("deploy", "NEW KEY"u8, Earlier));
        Assert.Null(keys.NewerThan("deploy", "OLD KEY"u8, Now.AddMinutes(1)));
    }

    [Fact]
    public void OlderRecordFromAnotherCloneIsReplaced()
    {
        using var keys = SshKeyFolder.Open(folder);
        WriteAndRecord(keys, "OLD KEY", cloneA, Earlier);

        Assert.Equal(SshKeyWriteState.Write, keys.Plan("deploy", Origin, cloneB, "NEW KEY"u8, Now, overwrite: false).State);
    }

    [Fact]
    public void FileChangedByHandIsNotTakenForANewerKey()
    {
        using var keys = SshKeyFolder.Open(folder);
        WriteAndRecord(keys, "KEY A", cloneA, Now);
        File.WriteAllText(Path.Combine(folder, "deploy"), "EDITED");

        Assert.Equal(SshKeyWriteState.Write, keys.Plan("deploy", Origin, cloneB, "KEY B"u8, Earlier, overwrite: false).State);
        Assert.Null(keys.NewerThan("deploy", "KEY B"u8, Earlier));
    }

    [Fact]
    public void UsersOfDeletedClonesArePruned()
    {
        using var keys = SshKeyFolder.Open(folder);
        WriteAndRecord(keys, "KEY", cloneA, Now);
        keys.AddUser("deploy", Origin, cloneB);
        keys.AddUser("deploy", Origin, cloneB);
        Assert.Equal(2, keys.Keys["deploy"].Users.Count);

        var pruned = keys.PruneUsers(path => string.Equals(path, Path.GetFullPath(cloneB), StringComparison.OrdinalIgnoreCase));

        Assert.Single(pruned);
        Assert.Equal([Path.GetFullPath(cloneB)], keys.Keys["deploy"].Users);
    }

    [Fact]
    public void DamagedMarkerIsRefused()
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, SshKeyPath.MarkerFileName), """{"schema":1,"keys":{"deploy":{"hash":"x"}}}""");

        Assert.Throws<VaultException>(() => SshKeyFolder.Open(folder));
    }

    [Fact]
    public async Task SecondOpenWaitsForTheLock()
    {
        var keys = SshKeyFolder.Open(folder);
        var second = Task.Run(() => SshKeyFolder.Open(folder), TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.False(second.IsCompleted);
        keys.Dispose();
        (await second).Dispose();
    }

    [Fact]
    public void ConfiguredToolsFolderMustHoldTheThreeTools()
    {
        var settings = new PcSettings(directory.Combine("appdata", PcSettings.FileName));
        var tools = directory.Combine("tools");
        Directory.CreateDirectory(tools);
        settings.SshDir = tools;

        var missing = Assert.Throws<VaultException>(() => SshTools.Find(settings));
        Assert.Contains(SshTools.KeyGen, missing.Message, StringComparison.Ordinal);

        foreach (var tool in new[] { SshTools.Ssh, SshTools.KeyGen, SshTools.Add })
        {
            File.WriteAllText(Path.Combine(tools, tool), string.Empty);
        }

        var found = SshTools.Find(settings);
        Assert.Equal(tools, found.Directory);
        Assert.False(found.IsDefault);
        Assert.Throws<VaultException>(() => settings.SshDir = "relative\\folder");

        settings.SshDir = null;
        Assert.Null(new PcSettings(settings.SettingsPath).SshDir);
    }

    [Fact]
    public void SettingsKeepTheIncludeAnswer()
    {
        var settings = new PcSettings(directory.Combine("appdata", PcSettings.FileName));
        Assert.Null(settings.SshInclude);
        settings.SshInclude = false;
        Assert.False(new PcSettings(settings.SettingsPath).SshInclude);
    }

    private void WriteAndRecord(SshKeyFolder keys, string value, string vault, DateTimeOffset at)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        PrivateFile.Write(Path.Combine(folder, "deploy"), bytes);
        keys.Record("deploy", Origin, vault, bytes, at);
        keys.Save();
    }
}
