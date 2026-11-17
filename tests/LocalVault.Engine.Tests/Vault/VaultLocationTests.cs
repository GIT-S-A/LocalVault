using LocalVault.Core;

namespace LocalVault.Engine.Tests;

public class VaultLocationTests
{
    [Fact]
    public void ExplicitPathWinsOverEnvironmentAndGitRoot()
    {
        using var repo = TempDirectory.GitRepository();
        var explicitPath = repo.Combine("infra", "secrets.local.lv");

        var location = VaultLocation.Resolve(explicitPath, repo.Combine("env.lv"), repo.Path);

        Assert.Equal(explicitPath, location.Path);
        Assert.Equal(VaultPathSource.Explicit, location.Source);
        Assert.Equal(repo.Path, location.GitRoot);
    }

    [Fact]
    public void EnvironmentWinsOverGitRoot()
    {
        using var repo = TempDirectory.GitRepository();

        var location = VaultLocation.Resolve(null, repo.Combine("env.lv"), repo.Path);

        Assert.Equal(repo.Combine("env.lv"), location.Path);
        Assert.Equal(VaultPathSource.Environment, location.Source);
    }

    [Fact]
    public void DefaultIsGitRootFromSubdirectory()
    {
        using var repo = TempDirectory.GitRepository();
        var sub = repo.CreateDirectory("a", "b");

        var location = VaultLocation.Resolve(" ", null, sub);

        Assert.Equal(repo.Combine("secrets.local.lv"), location.Path);
        Assert.Equal(VaultPathSource.GitRoot, location.Source);
    }

    [Fact]
    public void RelativeExplicitPathResolvesAgainstWorkingDirectory()
    {
        using var repo = TempDirectory.GitRepository();
        var sub = repo.CreateDirectory("tools");

        var location = VaultLocation.Resolve("secrets.local.lv", null, sub);

        Assert.Equal(repo.Combine("tools", "secrets.local.lv"), location.Path);
    }

    [Fact]
    public void OutsideGitWithoutPathIsAnError()
    {
        using var plain = new TempDirectory();

        Assert.Throws<VaultException>(() => VaultLocation.Resolve(null, null, plain.Path));
    }

    [Fact]
    public void SideFilePathsSitBesideTheVault()
    {
        var location = new VaultLocation(@"C:\p\infra\secrets.local.lv", VaultPathSource.Explicit, null);

        Assert.Equal(@"C:\p\infra\secrets.local.clixml", location.LegacyPath);
        Assert.Equal(@"C:\p\infra\secrets.local.lv.lock", location.LockPath);
        Assert.Equal(@"C:\p\infra\secrets.local.lv.bak", location.PreviousGenerationPath);
    }

    [Fact]
    public void DefaultPathCreationNeedsConfirmation()
    {
        using var repo = TempDirectory.GitRepository();
        var location = VaultLocation.Resolve(null, null, repo.Path);

        Assert.Throws<VaultException>(() => location.EnsureCanCreate(personConfirmed: false));
        location.EnsureCanCreate(personConfirmed: true);
    }

    [Fact]
    public void ExplicitPathCreationNeedsNoConfirmation()
    {
        using var repo = TempDirectory.GitRepository();

        VaultLocation.Resolve(repo.Combine("infra", "secrets.local.lv"), null, repo.Path)
            .EnsureCanCreate(personConfirmed: false);
    }

    [Fact]
    public void CreationRefusedWhenAnotherVaultExistsElsewhere()
    {
        using var repo = TempDirectory.GitRepository();
        repo.CreateDirectory("infra");
        File.WriteAllText(repo.Combine("infra", "secrets.local.clixml"), "x");
        var location = VaultLocation.Resolve(null, null, repo.Path);

        var error = Assert.Throws<VaultException>(() => location.EnsureCanCreate(personConfirmed: true));
        Assert.Contains(repo.Combine("infra", "secrets.local.clixml"), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyFileBesideTheVaultIsNotAnotherVault()
    {
        using var repo = TempDirectory.GitRepository();
        repo.CreateDirectory("infra");
        File.WriteAllText(repo.Combine("infra", "secrets.local.clixml"), "x");
        var location = VaultLocation.Resolve(repo.Combine("infra", "secrets.local.lv"), null, repo.Path);

        Assert.Empty(location.FindOtherVaultFiles());
    }

    [Fact]
    public void CreationRefusedWhenGitDoesNotIgnoreTheVault()
    {
        using var repo = TempDirectory.GitRepository(gitignore: null);
        var location = VaultLocation.Resolve(repo.Combine("secrets.local.lv"), null, repo.Path);

        var error = Assert.Throws<VaultException>(location.EnsureIgnoredByGit);
        Assert.Contains(".gitignore", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoreCheckRunsForAFolderThatDoesNotExistYet()
    {
        using var repo = TempDirectory.GitRepository(gitignore: null);
        var location = VaultLocation.Resolve(repo.Combine("infra", "new", "secrets.local.lv"), null, repo.Path);

        Assert.Throws<VaultException>(location.EnsureIgnoredByGit);
    }

    [Fact]
    public void IgnoredVaultPassesTheCheck()
    {
        using var repo = TempDirectory.GitRepository();

        VaultLocation.Resolve(repo.Combine("infra", "secrets.local.lv"), null, repo.Path).EnsureIgnoredByGit();
    }

    [Fact]
    public void ExplicitPathMayCreateAnotherVaultInTheRepository()
    {
        using var repo = TempDirectory.GitRepository();
        repo.CreateDirectory("infra");
        File.WriteAllText(repo.Combine("infra", "secrets.local.lv"), "x");

        VaultLocation.Resolve(repo.Combine("migration", "secrets.local.lv"), null, repo.Path).EnsureCanCreate(personConfirmed: false);
    }
    [Fact]
    public void OutsideGitCreationIsAllowed()
    {
        using var plain = new TempDirectory();
        var location = VaultLocation.Resolve(plain.Combine("v.lv"), null, plain.Path);

        location.EnsureIgnoredByGit();
        location.EnsureCanCreate(personConfirmed: false);
    }
}