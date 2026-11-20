using System.Text;

namespace LocalVault.Engine.Tests;

public sealed class SshCommandLineTests : IDisposable
{
    private const string PublicKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGmCmhyZfFSbAVfJ/bovNv4H3f/VIW8/KGdICZD31KiE test@example.com";
    private const string KeyPath = "~/.ssh/proj/deploy";
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly EntryName Deploy = EntryName.Parse("deploy");

    private readonly TempDirectory directory = new();

    public void Dispose() => directory.Dispose();

    [Theory]
    [InlineData("srv1.example.com", new[] { "srv1.example.com" })]
    [InlineData("deploy@srv1.example.com", new[] { "deploy@srv1.example.com" })]
    [InlineData("deploy@srv1.example.com:2222", new[] { "-p", "2222", "deploy@srv1.example.com" })]
    [InlineData("deploy@[2001:db8::1]:2222", new[] { "-p", "2222", "deploy@2001:db8::1" })]
    [InlineData("[2001:db8::1]", new[] { "2001:db8::1" })]
    public void DestinationGivesThePortAsAnOption(string host, string[] expected) =>
        Assert.Equal(expected, SshDestination.Parse(host).Arguments);

    [Theory]
    [InlineData("-oProxyCommand=calc")]
    [InlineData("srv1.example.com -p 1")]
    public void DestinationRefusesWhatIsNotAHost(string host) =>
        Assert.Throws<VaultException>(() => SshDestination.Parse(host));

    [Fact]
    public void SshUsesTheKeyThenTheOldKeyBeforeTheHostAndTheExtraArguments()
    {
        var entry = Key(previous: "-----BEGIN OPENSSH PRIVATE KEY-----\nold\n");
        var target = SshKeyFiles.TargetOf(entry);

        var arguments = SshCommandLine.Ssh(entry, "deploy@srv1.example.com:2222", ["uptime"]);

        Assert.Equal(["-i", target, "-i", target + ".old", "-o", "IdentitiesOnly=yes", "-p", "2222", "deploy@srv1.example.com", "uptime"], arguments);
    }

    [Fact]
    public void AgentKeyIsGivenByItsPublicKeyFile()
    {
        var entry = Key(agent: true, previous: PublicKey);
        var target = SshKeyFiles.TargetOf(entry);

        Assert.Equal(["-i", target + ".pub", "-i", target + ".old.pub", "-o", "IdentitiesOnly=yes", "a", "b:"], SshCommandLine.Scp(entry, ["a", "b:"]));
    }

    [Fact]
    public void ProbeReadsNoConfigAndTriesTheNewKeyOnly()
    {
        var entry = Key(previous: "-----BEGIN OPENSSH PRIVATE KEY-----\nold\n");

        var arguments = SshCommandLine.Probe(entry, "deploy@srv1.example.com");

        Assert.Equal(["-F", "none", "-i", SshKeyFiles.TargetOf(entry), "-o", "IdentitiesOnly=yes", "-o", "BatchMode=yes", "-o", "ConnectTimeout=5", "deploy@srv1.example.com", "exit"], arguments);
    }

    [Theory]
    [InlineData(0, "", SshHostCheck.Accepted)]
    [InlineData(255, "Host key verification failed.\r\n", SshHostCheck.UnknownHostKey)]
    [InlineData(255, "deploy@srv1.example.com: Permission denied (publickey).", SshHostCheck.Failed)]
    public void RetirementCheckReadsTheExitCodeAndTheHostKeyError(int exitCode, string error, SshHostCheck expected) =>
        Assert.Equal(expected, SshKeyRetirement.Check(Key(), "deploy@srv1.example.com", _ => new ToolResult(exitCode, string.Empty, error)));

    [Fact]
    public void RetirementCheckThatTimesOutFails() =>
        Assert.Equal(SshHostCheck.Failed, SshKeyRetirement.Check(Key(), "srv1.example.com", _ => throw new VaultException("did not finish")));

    [Fact]
    public void ConfigHasOneMatchBlockPerHostWithBothKeys()
    {
        var text = SshConfigFile.Render([Key(previous: "-----BEGIN OPENSSH PRIVATE KEY-----\nold\n", hosts: ["deploy@srv1.example.com:2222", "srv2.example.com"])]);

        Assert.Equal(
            """
            Match host srv1.example.com user deploy
                IdentityFile ~/.ssh/proj/deploy
                IdentityFile ~/.ssh/proj/deploy.old
                IdentitiesOnly yes
            Match host srv2.example.com
                IdentityFile ~/.ssh/proj/deploy
                IdentityFile ~/.ssh/proj/deploy.old
                IdentitiesOnly yes

            """.ReplaceLineEndings("\n"),
            text[(text.IndexOf('\n', StringComparison.Ordinal) + 1)..]);
    }

    [Fact]
    public void ConfigOfKeysWithoutHostsIsEmptyAndItsFileIsDeleted()
    {
        var folder = directory.Combine("proj");
        Directory.CreateDirectory(folder);
        SshConfigFile.Write(folder, [Key(hosts: ["srv1.example.com"])]);
        Assert.True(File.Exists(Path.Combine(folder, SshKeyPath.ConfigFileName)));

        SshConfigFile.Write(folder, [Key()]);

        Assert.False(File.Exists(Path.Combine(folder, SshKeyPath.ConfigFileName)));
    }

    [Fact]
    public void IncludeLineGoesFirstOnceAndLeavesTheRestOfTheConfig()
    {
        var config = directory.Combine("ssh", "config");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, "Host srv1\r\n    User admin\r\n");

        Assert.True(SshConfigFile.AddInclude(config));
        Assert.False(SshConfigFile.AddInclude(config));
        Assert.Equal(SshConfigFile.IncludeLine + "\r\nHost srv1\r\n    User admin\r\n", File.ReadAllText(config));

        Assert.True(SshConfigFile.RemoveInclude(config));
        Assert.Equal("Host srv1\r\n    User admin\r\n", File.ReadAllText(config));
        Assert.False(SshConfigFile.RemoveInclude(config));
    }

    [Fact]
    public void IncludeCreatesAMissingConfig()
    {
        var config = directory.Combine("new", ".ssh", "config");

        Assert.True(SshConfigFile.AddInclude(config));

        Assert.Equal(SshConfigFile.IncludeLine + "\n", File.ReadAllText(config));
    }

    private static VaultEntry Key(string? previous = null, bool agent = false, IReadOnlyList<string>? hosts = null) =>
        VaultEntry.NewSshKey(
            Deploy,
            Encoding.UTF8.GetBytes(agent ? PublicKey : "-----BEGIN OPENSSH PRIVATE KEY-----\nnew\n"),
            KeyPath,
            new SshKeyInfo(SshKeyInfo.Ed25519, PublicKey, hosts ?? [], Agent: agent),
            previous is null ? [] : Encoding.UTF8.GetBytes(previous),
            Now);
}
