using System.Security.Cryptography;
using LocalVault.Core;

namespace LocalVault.Core.Tests;

public sealed class BackupFileWriterTests : IDisposable
{
    private const string Id = "0123456789abcdef";
    private const string OtherId = "fedcba9876543210";
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private readonly TempDirectory directory = new();

    public void Dispose() => directory.Dispose();

    [Fact]
    public void RetentionKeepsNewestTenAndOnePerDayForThirtyDays()
    {
        var names = new List<string>();
        for (var day = 0; day < 40; day++)
        {
            for (var hour = 0; hour < 3; hour++)
            {
                names.Add(Stamp(Now.AddDays(-day).AddHours(-hour), Id));
            }
        }

        names.Add("notes.txt");
        names.Add(Stamp(Now.AddDays(-50), null));
        names.Add(Stamp(Now.AddDays(-50), OtherId));
        names.Add("20261008T120000000Z.lvbak.tmp-1");
        names.Add("20261399T000000000Z-" + Id + ".lvbak");
        names.Add("２０２６1008T120000000Z-" + Id + ".lvbak");

        var delete = BackupFileWriter.SelectForDeletion(names, Id, Now, Stamp(Now, Id));
        var kept = names.Where(n => n.EndsWith(Id + ".lvbak", StringComparison.Ordinal)).Except(delete).ToList();

        Assert.Contains(Stamp(Now, Id), kept);
        Assert.Equal(BackupFileWriter.NewestKept + 27 + 2, kept.Count);
        Assert.DoesNotContain(Stamp(Now.AddDays(-35), Id), kept);
        Assert.All(delete, name => Assert.EndsWith("-" + Id + ".lvbak", name, StringComparison.Ordinal));
        Assert.DoesNotContain("20261399T000000000Z-" + Id + ".lvbak", delete);
    }

    [Fact]
    public void RetentionKeepsTheBackupJustWrittenWhenTheClockWentBack()
    {
        var names = Enumerable.Range(0, 12).Select(i => Stamp(Now.AddMinutes(i), Id)).ToList();
        var justWritten = Stamp(Now.AddDays(-60), Id);
        names.Add(justWritten);

        Assert.DoesNotContain(justWritten, BackupFileWriter.SelectForDeletion(names, Id, Now.AddDays(-60), justWritten));
    }

    [Fact]
    public void WriteSealsBodyAndFindNewestReturnsOwnBackups()
    {
        var keyFile = RandomNumberGenerator.GetBytes(32);
        var settings = BackupSettings.Create(Id, directory.Path, "backup passphrase".AsSpan(), keyFile);
        var body = VaultBody.CreateEmpty();
        body.Set(VaultEntry.NewSecret(EntryName.Parse("a"), "1"u8, Now));
        var writer = new BackupFileWriter();

        var first = writer.Write(body, settings, Now);
        var second = writer.Write(body, settings, Now.AddSeconds(1));
        File.WriteAllText(directory.Combine(Stamp(Now.AddSeconds(5), OtherId)), "other vault");

        Assert.Equal(second, BackupFileWriter.FindNewest(directory.Path, Id));
        Assert.EndsWith("-" + Id + ".lvbak", second, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.Equal(Now.AddSeconds(1), BackupFileWriter.TakenAt(second));
        Assert.Equal([first, second], BackupFileWriter.FindOwn(directory.Path, Id).Order(StringComparer.Ordinal));
        var content = VaultTransfer.OpenBackup(second, "backup passphrase", keyFile);
        Assert.Equal("a", Assert.Single(content.Entries).Name.FullName);
    }

    [Theory]
    [InlineData("%LV_TEST_UNSET_VARIABLE%\\LocalVault")]
    [InlineData("relative\\folder")]
    public void DestinationMustResolveToAFullPath(string destination)
    {
        Assert.Throws<VaultException>(() => BackupFileWriter.ResolveDestination(destination));
    }

    [Fact]
    public void GeneratedPassphraseIsLongAndAlphanumeric()
    {
        var passphrase = GeneratedPassphrase.Create();

        Assert.Equal(32, passphrase.Length);
        Assert.All(passphrase, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
    }

    [Fact]
    public void SettingsRefuseAKeyFileOfTheWrongLength()
    {
        Assert.Throws<VaultException>(() => BackupSettings.Create(Id, directory.Path, "backup passphrase".AsSpan(), new byte[100]));
    }
    [Fact]
    public void FolderNameCombinesProjectHashAndMachine()
    {
        var location = new VaultLocation(@"C:\dev\proj\infra\secrets.local.lv", VaultPathSource.Explicit, @"C:\dev\proj");

        var name = BackupLocator.FolderName(location, "PC1");

        Assert.Matches(@"^proj-[0-9a-f]{8}-PC1$", name);
        Assert.Equal(name, BackupLocator.FolderName(location with { Path = @"c:\DEV\proj\infra\secrets.local.lv" }, "PC1"));
    }

    private static string Stamp(DateTimeOffset time, string? id) =>
        time.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture)
        + (id is null ? string.Empty : "-" + id) + ".lvbak";
}
