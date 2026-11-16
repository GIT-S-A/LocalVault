using System.Text.RegularExpressions;
using KeePassLib.Collections;

namespace LocalVault.KeePass;

public static partial class KdbxHeader
{
    private const uint Signature1 = 0x9AA2D903;
    private const uint Signature2 = 0xB54BFB67;
    private const uint FirstMajorWithPublicData = 4;
    private const byte EndOfHeaderField = 0;
    private const byte PublicCustomDataField = 12;
    private const int MaxHeaderBytes = 1 << 20;

    // KDBX 4 outer header: signatures, uint32 version (major in the high word), then fields of
    // byte id, int32 size, data, all little-endian, ending with field 0. Read without the key.
    public static string? ReadDatabaseId(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != Signature1 || reader.ReadUInt32() != Signature2 || reader.ReadUInt32() >> 16 < FirstMajorWithPublicData)
            {
                return null;
            }

            var total = 0;
            while (true)
            {
                var field = reader.ReadByte();
                var size = reader.ReadInt32();
                total += size;
                if (size < 0 || total > MaxHeaderBytes)
                {
                    return null;
                }

                var data = reader.ReadBytes(size);
                if (field == EndOfHeaderField || data.Length != size)
                {
                    return null;
                }

                if (field == PublicCustomDataField)
                {
                    var id = VariantDictionary.Deserialize(data)?.GetString(KeePassStore.DatabaseIdField);
                    return id is not null && DatabaseIdPattern().IsMatch(id) ? id : null;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    [GeneratedRegex("^[0-9A-Fa-f]{32}$")]
    private static partial Regex DatabaseIdPattern();
}
