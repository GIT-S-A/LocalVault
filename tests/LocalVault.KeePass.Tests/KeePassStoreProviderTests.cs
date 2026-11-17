namespace LocalVault.KeePass.Tests;

public sealed class KeePassStoreProviderTests : IDisposable
{
    private readonly TempDirectory directory = new();

    public void Dispose() => directory.Dispose();

    [Fact]
    public void OpenAsksTheMasterPasswordOfTheNamedStore()
    {
        var path = directory.Combine("team.kdbx");
        CreateDatabase(path);
        var prompt = new ScriptedPrompt("team", Master);

        using var store = new KeePassStoreProvider().Open(path, null, prompt);

        Assert.Equal(["KeePass master password for team"], prompt.Questions);
        Assert.Equal(path, store.Path);
    }

    [Fact]
    public void WrongAnswerDoesNotOpen()
    {
        var path = directory.Combine("team.kdbx");
        CreateDatabase(path);

        var error = Assert.Throws<VaultException>(() => new KeePassStoreProvider().Open(path, null, new ScriptedPrompt("team", "wrong")));

        Assert.Contains("does not open", error.Message, StringComparison.Ordinal);
    }

    private sealed class ScriptedPrompt(string storeName, string answer) : IStorePrompt
    {
        public List<string> Questions { get; } = [];

        public string StoreName => storeName;

        public ReadOnlySpan<char> AskSecret(string question)
        {
            Questions.Add(question);
            return answer;
        }

        public bool Confirm(string question) => false;

        public void Tell(string message)
        {
        }
    }
}
