using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Serialization;

namespace LocalVault.KeePass.Tests;

public sealed class KeePassStoreProviderTests : IDisposable
{
    private const string Master = "test master 1";

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

    private static void CreateDatabase(string path)
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(Master));
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(path), key);
        var kdf = new AesKdf().GetDefaultParameters();
        kdf.SetUInt64(AesKdf.ParamRounds, 1000);
        db.KdfParameters = kdf;
        db.Save(null);
        db.Close();
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
