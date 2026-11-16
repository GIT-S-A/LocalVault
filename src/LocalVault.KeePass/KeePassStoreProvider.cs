using LocalVault.Core;

namespace LocalVault.KeePass;

public sealed class KeePassStoreProvider : IStoreProvider
{
    public string Type => StoreTypes.KeePass;

    public IStore Open(string target, string? keyFile, IStorePrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        return KeePassStore.Open(target, keyFile, prompt.AskSecret($"KeePass master password for {prompt.StoreName}"));
    }

    public string? ReadDatabaseId(string target) => KdbxHeader.ReadDatabaseId(target);
}
