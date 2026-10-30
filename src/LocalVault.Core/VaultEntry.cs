using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace LocalVault.Core;

public sealed class VaultEntry
{
    public const string SecretKind = "secret";
    public const string FileKind = "file";
    public const string RequiredKind = "required";
    public const string CredentialKind = "credential";

    private const string NameField = "name";
    private const string UserField = "user";
    private const string UrlsField = "urls";
    private const string TotpField = "totp";
    private const string KindField = "kind";
    private const string ValueField = "value";
    private const string MetaField = "meta";
    private const string CreatedField = "created";
    private const string PathField = "path";
    private const string LocalField = "local";
    private const string KeePassField = "kp";
    private const string HashField = "hash";
    private const string SyncedAtField = "at";
    private const string RequiredField = "required";
    private const string NoteField = "note";

    internal VaultEntry(JsonObject node)
    {
        Node = node;
        Name = EntryName.Parse(ReadString(node, NameField));
        _ = ReadString(node, KindField);
        if (!Base64.IsValid(ReadString(node, ValueField)))
        {
            throw new VaultException($"Vault entry '{Name}' has a value that is not base64.");
        }

        ValidateMeta(node[MetaField]);
        ValidateCredential(node);
    }

    public EntryName Name { get; }

    public string Kind => ReadString(Node, KindField);

    public bool IsSecret => Kind == SecretKind;

    public bool IsFile => Kind == FileKind;

    public bool IsMissing => Kind == RequiredKind;

    public bool IsCredential => Kind == CredentialKind;

    public bool IsKnownKind => IsSecret || IsFile || IsMissing || IsCredential;

    public string? UserName => Node[UserField] is JsonValue value && value.TryGetValue<string>(out var user) ? user : null;

    public IReadOnlyList<string> Urls =>
        Node[UrlsField] is JsonArray urls ? urls.Select(url => url!.GetValue<string>()).ToList() : [];

    public TotpSecret? Totp => Node[TotpField] is JsonValue value && value.TryGetValue<string>(out var uri) ? TotpSecret.Parse(uri) : null;

    public bool IsRequired => IsMissing || Meta?[RequiredField] is JsonObject;

    public string? RequiredNote =>
        (Meta?[RequiredField] as JsonObject)?[NoteField] is JsonValue value && value.TryGetValue<string>(out var note) ? note : null;

    public DateTimeOffset? Created
    {
        get
        {
            var text = Meta?[CreatedField]?.GetValue<string>();
            return text is null
                ? null
                : DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        }
    }

    public string? StoredPath => Meta?[PathField]?.GetValue<string>();

    public bool IsLocal => Meta?[LocalField] is JsonValue value && value.TryGetValue<bool>(out var local) && local;

    public string? KeePassBase => KeePassBaseFor(null);

    public IReadOnlyList<(string? Database, string Hash)> KeePassBases
    {
        get
        {
            if (Meta?[KeePassField] is not JsonObject sync)
            {
                return [];
            }

            if (ReadHash(sync) is { } legacy)
            {
                return [(null, legacy)];
            }

            return sync.Where(pair => ReadHash(pair.Value as JsonObject) is not null)
                .Select(pair => ((string?)pair.Key, ReadHash(pair.Value as JsonObject)!))
                .ToList();
        }
    }

    public string? KeePassBaseFor(string? databaseId)
    {
        if (Meta?[KeePassField] is not JsonObject sync)
        {
            return null;
        }

        if (ReadHash(sync) is { } legacy)
        {
            return legacy;
        }

        return databaseId is null
            ? sync.Select(pair => ReadHash(pair.Value as JsonObject)).FirstOrDefault(hash => hash is not null)
            : ReadHash(sync[databaseId] as JsonObject);
    }

    internal JsonObject Node { get; }

    private JsonObject? Meta => Node[MetaField] as JsonObject;

    public static VaultEntry NewSecret(EntryName name, ReadOnlySpan<byte> utf8Value, DateTimeOffset created) =>
        new(BuildNode(name, SecretKind, utf8Value, created, null));

    public static VaultEntry NewFile(EntryName name, ReadOnlySpan<byte> content, string storedPath, DateTimeOffset created) =>
        new(BuildNode(name, FileKind, content, created, StoredPathRules.Normalize(storedPath)));

    public static VaultEntry NewRequired(EntryName name, string? note, DateTimeOffset created)
    {
        var entry = new VaultEntry(BuildNode(name, RequiredKind, [], created, null));
        entry.SetRequired(note);
        return entry;
    }

    public static VaultEntry NewCredential(
        EntryName name, string? userName, ReadOnlySpan<byte> password, TotpSecret? totp, IReadOnlyList<string> urls, DateTimeOffset created)
    {
        ArgumentNullException.ThrowIfNull(urls);
        var node = BuildNode(name, CredentialKind, password, created, null);
        WriteCredentialFields(node, name, userName, totp, urls);
        return new VaultEntry(node);
    }

    public byte[] GetValue() => Convert.FromBase64String(ReadString(Node, ValueField));

    public byte[] ReadPart(EntryReference reference, DateTimeOffset now)
    {
        if (IsFile)
        {
            throw new VaultException($"'{Name}' is a file entry, not a secret.");
        }

        if (reference.Part == EntryPart.Password)
        {
            return GetValue();
        }

        if (!IsCredential)
        {
            throw new VaultException($"'{Name}' is a {Kind} entry, not a credential: it has no {reference} part.");
        }

        var text = reference.Part switch
        {
            EntryPart.User => UserName ?? throw new VaultException($"'{Name}' has no user name."),
            EntryPart.Totp => Totp?.Code(now) ?? throw new VaultException($"'{Name}' has no TOTP."),
            _ => Urls.ElementAtOrDefault(reference.UrlNumber - 1)
                ?? throw new VaultException($"'{Name}' has {Urls.Count} URL(s); there is no {reference}."),
        };
        return Encoding.UTF8.GetBytes(text);
    }

    public VaultEntry WithPart(EntryReference reference, string? text, ReadOnlySpan<byte> password, DateTimeOffset now)
    {
        if (IsFile)
        {
            throw new VaultException($"'{Name}' is a file entry; it has no {reference} part.");
        }

        var currentPassword = IsMissing ? [] : GetValue();
        try
        {
            var user = UserName;
            var totp = Totp;
            var urls = Urls.ToList();
            switch (reference.Part)
            {
                case EntryPart.Password when !IsCredential:
                    return NewSecret(Name, password, now);
                case EntryPart.Password:
                    return NewCredential(Name, user, password, totp, urls, now);
                case EntryPart.User:
                    user = string.IsNullOrEmpty(text) ? null : text;
                    break;
                case EntryPart.Totp:
                    totp = string.IsNullOrEmpty(text) ? null : TotpSecret.Parse(text);
                    break;
                default:
                    SetUrl(urls, reference, text);
                    break;
            }

            return NewCredential(Name, user, currentPassword, totp, urls, now);
        }
        finally
        {
            Array.Clear(currentPassword);
        }
    }

    public static string ComputeCredentialHash(string? userName, ReadOnlySpan<byte> password, TotpSecret? totp, IReadOnlyList<string> urls)
    {
        ArgumentNullException.ThrowIfNull(urls);
        using var buffer = new MemoryStream();
        AppendField(buffer, Encoding.UTF8.GetBytes(userName ?? string.Empty));
        AppendField(buffer, password);
        AppendField(buffer, Encoding.UTF8.GetBytes(totp?.Canonical ?? string.Empty));
        foreach (var url in urls)
        {
            AppendField(buffer, Encoding.UTF8.GetBytes(url));
        }

        var content = buffer.ToArray();
        try
        {
            return ComputeContentHash(CredentialKind, null, content);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(content);
        }
    }

    public static string ComputeContentHash(string kind, string? storedPath, ReadOnlySpan<byte> value)
    {
        ArgumentNullException.ThrowIfNull(kind);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(kind));
        hash.AppendData([0]);
        hash.AppendData(Encoding.UTF8.GetBytes(storedPath ?? string.Empty));
        hash.AppendData([0]);
        hash.AppendData(value);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public string ContentHash()
    {
        var value = GetValue();
        try
        {
            return IsCredential ? ComputeCredentialHash(UserName, value, Totp, Urls) : ComputeContentHash(Kind, StoredPath, value);
        }
        finally
        {
            Array.Clear(value);
        }
    }

    public string SyncHash() =>
        IsRequired
            ? ComputeContentHash(RequiredKind, null, Encoding.UTF8.GetBytes(RequiredNote ?? string.Empty))
            : ContentHash();

    internal void SetRequired(string? note)
    {
        var required = new JsonObject();
        if (!string.IsNullOrEmpty(note))
        {
            required[NoteField] = note;
        }

        EnsureMeta()[RequiredField] = required;
        Meta!.Remove(LocalField);
    }

    internal void DropPersonalValue()
    {
        if (IsRequired && !IsMissing)
        {
            Node[KindField] = RequiredKind;
            Node[ValueField] = string.Empty;
            RemoveCredentialFields(Node);
        }
    }

    internal void SetLocal(bool local)
    {
        if (local)
        {
            EnsureMeta()[LocalField] = true;
        }
        else
        {
            Meta?.Remove(LocalField);
        }
    }

    internal void SetKeePassBase(string? databaseId, string? hash, DateTimeOffset at)
    {
        var record = hash is null
            ? null
            : new JsonObject
            {
                [HashField] = hash,
                [SyncedAtField] = at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            };
        var sync = Meta?[KeePassField] as JsonObject;
        if (databaseId is null || (sync is not null && ReadHash(sync) is not null))
        {
            sync = null;
        }

        if (databaseId is null)
        {
            if (record is null)
            {
                Meta?.Remove(KeePassField);
            }
            else
            {
                EnsureMeta()[KeePassField] = record;
            }

            return;
        }

        sync ??= new JsonObject();
        if (record is null)
        {
            sync.Remove(databaseId);
        }
        else
        {
            sync[databaseId] = record;
        }

        if (sync.Count == 0)
        {
            Meta?.Remove(KeePassField);
        }
        else
        {
            EnsureMeta()[KeePassField] = sync;
        }
    }

    private static string? ReadHash(JsonObject? record) =>
        record?[HashField] is JsonValue value && value.TryGetValue<string>(out var hash) ? hash : null;

    internal void RemoveKeePassBase() => Meta?.Remove(KeePassField);

    public bool HasSameContent(VaultEntry other)
    {
        if (Kind != other.Kind || !string.Equals(StoredPath, other.StoredPath, StringComparison.Ordinal))
        {
            return false;
        }

        if (IsCredential)
        {
            return ContentHash() == other.ContentHash();
        }

        var mine = GetValue();
        var theirs = other.GetValue();
        try
        {
            return mine.AsSpan().SequenceEqual(theirs);
        }
        finally
        {
            Array.Clear(mine);
            Array.Clear(theirs);
        }
    }

    internal void CopyContentFrom(VaultEntry source)
    {
        Node[KindField] = source.Kind;
        Node[ValueField] = ReadString(source.Node, ValueField);
        RemoveCredentialFields(Node);
        foreach (var field in CredentialFields.Where(field => source.Node[field] is not null))
        {
            Node[field] = source.Node[field]!.DeepClone();
        }

        var targetMeta = Meta ?? new JsonObject();
        if (Meta is null)
        {
            Node[MetaField] = targetMeta;
        }

        if (source.Meta is { } sourceMeta)
        {
            foreach (var (key, value) in sourceMeta)
            {
                targetMeta[key] = value?.DeepClone();
            }
        }

        if (source.StoredPath is null)
        {
            targetMeta.Remove(PathField);
        }
    }

    internal VaultEntry Clone() => new((JsonObject)Node.DeepClone());

    private JsonObject EnsureMeta()
    {
        if (Meta is { } meta)
        {
            return meta;
        }

        var created = new JsonObject();
        Node[MetaField] = created;
        return created;
    }

    private static JsonObject BuildNode(
        EntryName name, string kind, ReadOnlySpan<byte> value, DateTimeOffset created, string? storedPath)
    {
        var meta = new JsonObject
        {
            [CreatedField] = created.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        };
        if (storedPath is not null)
        {
            meta[PathField] = storedPath;
        }

        return new JsonObject
        {
            [NameField] = name.FullName,
            [KindField] = kind,
            [ValueField] = Convert.ToBase64String(value),
            [MetaField] = meta,
        };
    }

    private void ValidateMeta(JsonNode? meta)
    {
        if (meta is null)
        {
            return;
        }

        if (meta is not JsonObject fields)
        {
            throw new VaultException($"Vault entry '{Name}' has a 'meta' field that is not an object.");
        }

        if (fields[CreatedField] is { } created
            && !(created is JsonValue createdValue
                && createdValue.TryGetValue<string>(out var text)
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _)))
        {
            throw new VaultException($"Vault entry '{Name}' has a 'created' time that is not a date.");
        }

        if (fields[PathField] is { } path && !(path is JsonValue pathValue && pathValue.TryGetValue<string>(out _)))
        {
            throw new VaultException($"Vault entry '{Name}' has a 'path' that is not a string.");
        }

        if (fields[RequiredField] is { } required
            && !(required is JsonObject requiredFields
                && (requiredFields[NoteField] is null || (requiredFields[NoteField] is JsonValue note && note.TryGetValue<string>(out _)))))
        {
            throw new VaultException($"Vault entry '{Name}' has a 'required' field that is damaged.");
        }

        if (fields[LocalField] is { } local && !(local is JsonValue localValue && localValue.TryGetValue<bool>(out _)))
        {
            throw new VaultException($"Vault entry '{Name}' has a 'local' flag that is not true or false.");
        }

        if (fields[KeePassField] is { } keePass
            && !(keePass is JsonObject sync
                && (ReadHash(sync) is not null || sync.All(pair => ReadHash(pair.Value as JsonObject) is not null))))
        {
            throw new VaultException($"Vault entry '{Name}' has KeePass sync data without a hash.");
        }
    }

    private static readonly string[] CredentialFields = [UserField, UrlsField, TotpField];

    private static void RemoveCredentialFields(JsonObject node)
    {
        foreach (var field in CredentialFields)
        {
            node.Remove(field);
        }
    }

    private static void WriteCredentialFields(JsonObject node, EntryName name, string? userName, TotpSecret? totp, IReadOnlyList<string> urls)
    {
        if (!string.IsNullOrEmpty(userName))
        {
            node[UserField] = userName;
        }

        var kept = urls.Where(url => !string.IsNullOrWhiteSpace(url)).Select(url => url.Trim()).ToList();
        if (kept.Count > 0)
        {
            node[UrlsField] = new JsonArray(kept.Select(url => (JsonNode?)JsonValue.Create(url)).ToArray());
        }

        if (totp is not null)
        {
            node[TotpField] = totp.ToUri(name.FullName);
        }
    }

    private static void SetUrl(List<string> urls, EntryReference reference, string? text)
    {
        var index = reference.UrlNumber - 1;
        if (index > urls.Count)
        {
            var next = new EntryReference(reference.Name, EntryPart.Url, urls.Count + 1);
            throw new VaultException($"'{reference.Name}' has {urls.Count} URL(s); set {next} first.");
        }

        if (string.IsNullOrEmpty(text))
        {
            if (index < urls.Count)
            {
                urls.RemoveAt(index);
            }
        }
        else if (index == urls.Count)
        {
            urls.Add(text);
        }
        else
        {
            urls[index] = text;
        }
    }

    private static void AppendField(MemoryStream buffer, ReadOnlySpan<byte> field)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, field.Length);
        buffer.Write(length);
        buffer.Write(field);
    }

    private void ValidateCredential(JsonObject node)
    {
        if (node[UserField] is { } user && !(user is JsonValue userValue && userValue.TryGetValue<string>(out _)))
        {
            throw new VaultException($"Vault entry '{Name}' has a user name that is not a string.");
        }

        if (node[UrlsField] is { } urls
            && !(urls is JsonArray items && items.All(url => url is JsonValue urlValue && urlValue.TryGetValue<string>(out _))))
        {
            throw new VaultException($"Vault entry '{Name}' has URLs that are not a list of strings.");
        }

        if (node[TotpField] is { } totp)
        {
            if (!(totp is JsonValue totpValue && totpValue.TryGetValue<string>(out var uri)))
            {
                throw new VaultException($"Vault entry '{Name}' has a TOTP that is not a string.");
            }

            _ = TotpSecret.Parse(uri);
        }
    }

    private static string ReadString(JsonObject node, string field)
    {
        if (node[field] is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }

        throw new VaultException($"Vault entry has no '{field}' string.");
    }
}
