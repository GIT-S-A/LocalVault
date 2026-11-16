using LocalVault.Core;

namespace LocalVault.KeePass;

public sealed class KeePassStoreProvider : IStoreProvider
{
    public string Type => StoreTypes.KeePass;

    public IStore Open(string target, string? keyFile, ReadOnlySpan<char> password) => KeePassStore.Open(target, keyFile, password);

    public string? ReadDatabaseId(string target) => KdbxHeader.ReadDatabaseId(target);
}
