namespace LocalVault.Engine.Tests;

public sealed class SshAgentTests
{
    private const string Ed25519Key = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGmCmhyZfFSbAVfJ/bovNv4H3f/VIW8/KGdICZD31KiE";
    private const string Fingerprint = "SHA256:ygwjj+x/jcbcUNmwhazPNZzoRvNv7XDnLrscAy0ZQ0M";

    [Fact]
    public void AgentListGivesEachSupportedKeyWithItsComment()
    {
        var keys = SshAgent.Parse($"{Ed25519Key} me@example.com deploy key\r\n{Ed25519Key}\r\n");

        Assert.Equal(2, keys.Count);
        Assert.Equal(new SshAgentKey(Ed25519Key, SshKeyInfo.Ed25519, Fingerprint, "me@example.com deploy key"), keys[0]);
        Assert.Equal(string.Empty, keys[1].Comment);
    }

    [Theory]
    [InlineData("The agent has no identities.")]
    [InlineData("sk-ssh-ed25519@openssh.com AAAAGnNrLXNzaC1lZDI1NTE5QG9wZW5zc2guY29t security key")]
    [InlineData("ssh-ed25519 not-base64! comment")]
    [InlineData("")]
    public void AgentLinesThatAreNotSupportedKeysAreLeftOut(string output) =>
        Assert.Empty(SshAgent.Parse(output));
}
