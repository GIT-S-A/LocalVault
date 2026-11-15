using System.Buffers.Binary;
using KeePassLib;
using KeePassLib.Cryptography.KeyDerivation;
using KeePassLib.Keys;
using KeePassLib.Serialization;

namespace LocalVault.Core.Tests;

public sealed class KdbxHeaderTests : IDisposable
{
    private const string Master = "test master 1";

    private readonly TempDirectory directory = new();

    public void Dispose() => directory.Dispose();

    [Fact]
    public void SaveWritesTheDatabaseIdReadableWithoutThePassword()
    {
        var path = directory.Combine("team.kdbx");
        CreateDatabase(path);
        Assert.Null(KdbxHeader.ReadDatabaseId(path));

        string id;
        using (var vault = KeePassVault.Open(path, null, Master))
        {
            id = vault.DatabaseId;
            vault.Save();
        }

        Assert.Equal(id, KdbxHeader.ReadDatabaseId(path));
    }

    [Fact]
    public void FileThatIsNotKdbx4HasNoId()
    {
        var text = directory.Combine("notes.kdbx");
        File.WriteAllText(text, "not a database");
        var truncated = directory.Combine("truncated.kdbx");
        File.WriteAllBytes(truncated, Header(0x00040000)[..10]);
        var kdbx3 = directory.Combine("old.kdbx");
        File.WriteAllBytes(kdbx3, Header(0x00030001));

        Assert.Null(KdbxHeader.ReadDatabaseId(text));
        Assert.Null(KdbxHeader.ReadDatabaseId(truncated));
        Assert.Null(KdbxHeader.ReadDatabaseId(kdbx3));
        Assert.Null(KdbxHeader.ReadDatabaseId(directory.Combine("missing.kdbx")));
    }

    private static byte[] Header(uint version)
    {
        var bytes = new byte[17];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x9AA2D903);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0xB54BFB67);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), version);
        return bytes;
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
}
