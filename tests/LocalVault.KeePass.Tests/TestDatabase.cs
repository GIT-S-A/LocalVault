using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Serialization;

namespace LocalVault.KeePass.Tests;

internal static class TestDatabase
{
    public const string Master = "test master 1";

    private const ulong FastAesRounds = 1000;

    public static CompositeKey Key(string? keyFile = null)
    {
        var key = new CompositeKey();
        key.AddUserKey(new KcpPassword(Master));
        if (keyFile is not null)
        {
            key.AddUserKey(new KcpKeyFile(keyFile));
        }

        return key;
    }

    public static void CreateDatabase(string path, bool recycleBin = true, string? keyFile = null)
    {
        var db = new PwDatabase();
        db.New(IOConnectionInfo.FromPath(path), Key(keyFile));
        var kdf = new AesKdf().GetDefaultParameters();
        kdf.SetUInt64(AesKdf.ParamRounds, FastAesRounds);
        db.KdfParameters = kdf;
        db.RecycleBinEnabled = recycleBin;
        db.Save(null);
        db.Close();
    }
}
