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
    public const string HelloKind = "hello";
    public const string CredentialShape = "credential";
    public const string FileShape = "file";
    public const string SshKeyShape = "ssh-key";
    public const string PersonalBasePrefix = "personal:";
    public const string OldKeySuffix = ".old";

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
    private const string ShapeField = "shape";
    private const string HostsField = "hosts";
    private const string PreviousField = "previous";
    private const string SshField = "ssh";
    private const string TypeField = "type";
    private const string PublicKeyField = "publicKey";
    private const string AgentField = "agent";
    private const string ReplacedAtField = "replacedAt";
    private const string HelloField = "hello";
    private const string VaultKeyField = "vaultKey";
    private const string RefField = "ref";
    private const string RefDatabaseField = "db";
    private const string RefUuidField = "uuid";
    private const string RefAttachmentField = "attachment";
    private const string RefTitleField = "title";

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
        if (IsHello && HelloInnerKind is null)
        {
            throw new VaultException($"Vault entry '{Name}' is sealed by Windows Hello but has no 'hello' data.");
        }

        ValidateCredential(node);
        if (node[PreviousField] is { } previous
            && !(previous is JsonValue previousValue && previousValue.TryGetValue<string>(out var previousText) && Base64.IsValid(previousText)))
        {
            throw new VaultException($"Vault entry '{Name}' has a previous key that is not base64.");
        }

        if (Meta?[SshField] is not null)
        {
            _ = SshKeyPath.Validate(StoredPath ?? throw new VaultException($"SSH key '{Name}' has no path."));
        }

        if (IsSshKeyShape && RequiredPath is { } requiredPath)
        {
            _ = SshKeyPath.Validate(requiredPath);
        }
    }

    public EntryName Name { get; }

    public string Kind => IsHello ? HelloInnerKind! : StoredKind;

    public bool IsSecret => Kind == SecretKind;

    public bool IsFile => Kind == FileKind;

    public bool IsMissing => Kind == RequiredKind;

    public bool IsCredential => Kind == CredentialKind;

    public bool IsKnownKind => !IsHello && (IsSecret || IsFile || IsMissing || IsCredential);

    public bool IsHello => StoredKind == HelloKind;

    public bool IsSyncable => IsKnownKind || (IsHello && HelloInnerKind is SecretKind or FileKind or CredentialKind);

    public bool IsSealable => !IsHello && (IsSecret || IsFile || IsCredential) && Ssh is null;

    public bool IsHelloMarked => !IsHello && Meta?[HelloField] is JsonObject { Count: 0 };

    private string StoredKind => ReadString(Node, KindField);

    public string? HelloInnerKind => (Meta?[HelloField] as JsonObject)?[KindField]?.GetValue<string>();

    public string? HelloHash => (Meta?[HelloField] as JsonObject)?[HashField]?.GetValue<string>();

    public string? HelloVaultKey => (Meta?[HelloField] as JsonObject)?[VaultKeyField]?.GetValue<string>();

    public bool IsRequiredHello => (Meta?[RequiredField] as JsonObject)?[HelloField] is JsonValue value && value.TryGetValue<bool>(out var hello) && hello;

    public string? UserName => Node[UserField] is JsonValue value && value.TryGetValue<string>(out var user) ? user : null;

    public IReadOnlyList<string> Urls =>
        Node[UrlsField] is JsonArray urls ? urls.Select(url => url!.GetValue<string>()).ToList() : [];

    public TotpSecret? Totp => Node[TotpField] is JsonValue value && value.TryGetValue<string>(out var uri) ? TotpSecret.Parse(uri) : null;

    public bool IsRequired => IsMissing || Meta?[RequiredField] is JsonObject;

    public string? RequiredNote =>
        (Meta?[RequiredField] as JsonObject)?[NoteField] is JsonValue value && value.TryGetValue<string>(out var note) ? note : null;

    public string? RequiredShape =>
        (Meta?[RequiredField] as JsonObject)?[ShapeField] is JsonValue value && value.TryGetValue<string>(out var shape) ? shape : null;

    public bool IsCredentialShape => RequiredShape == CredentialShape;

    public bool IsFileShape => RequiredShape == FileShape;

    public bool IsSshKeyShape => RequiredShape == SshKeyShape;

    public bool IsSshKey => Ssh is not null || IsSshKeyShape;

    public IReadOnlyList<string> RequiredHosts => ReadStrings((Meta?[RequiredField] as JsonObject)?[HostsField]);

    public IReadOnlyList<string> SshHosts => IsRequired ? RequiredHosts : Ssh?.Hosts ?? [];

    public SshKeyInfo? Ssh
    {
        get
        {
            if (Meta?[SshField] is not JsonObject ssh)
            {
                return null;
            }

            var replacedAt = ssh[ReplacedAtField]?.GetValue<string>();
            return new SshKeyInfo(
                ssh[TypeField]!.GetValue<string>(),
                ssh[PublicKeyField]!.GetValue<string>(),
                ReadStrings(ssh[HostsField]),
                ssh[AgentField] is JsonValue agent && agent.TryGetValue<bool>(out var isAgent) && isAgent,
                replacedAt is null ? null : DateTimeOffset.Parse(replacedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
        }
    }

    public KeePassReference? Reference =>
        Meta?[RefField] is JsonObject reference
            ? new KeePassReference(
                reference[RefDatabaseField]!.GetValue<string>(),
                reference[RefUuidField]!.GetValue<string>(),
                reference[RefAttachmentField]?.GetValue<string>(),
                reference[RefTitleField]!.GetValue<string>())
            : null;

    public VaultEntry WithReference(KeePassReference? reference)
    {
        var copy = Clone();
        copy.SetReference(reference);
        return copy;
    }

    private void SetReference(KeePassReference? reference)
    {
        if (reference is null)
        {
            Meta?.Remove(RefField);
            return;
        }

        var node = new JsonObject
        {
            [RefDatabaseField] = reference.DatabaseId,
            [RefUuidField] = reference.Uuid,
            [RefTitleField] = reference.Title,
        };
        if (reference.Attachment is not null)
        {
            node[RefAttachmentField] = reference.Attachment;
        }

        EnsureMeta()[RefField] = node;
    }

    public bool HasPrevious => Node[PreviousField] is not null;

    public byte[]? GetPrevious() =>
        Node[PreviousField] is JsonValue value && value.TryGetValue<string>(out var text) ? Convert.FromBase64String(text) : null;

    public string? RequiredPath =>
        (Meta?[RequiredField] as JsonObject)?[PathField] is JsonValue value && value.TryGetValue<string>(out var path) ? path : null;

    public bool IsUnderHome => StoredPath is { } path && StoredPathRules.IsUnderHome(path);

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
            ? sync.Where(pair => !pair.Key.StartsWith(PersonalBasePrefix, StringComparison.Ordinal))
                .Select(pair => ReadHash(pair.Value as JsonObject)).FirstOrDefault(hash => hash is not null)
            : ReadHash(sync[databaseId] as JsonObject);
    }

    internal JsonObject Node { get; }

    private JsonObject? Meta => Node[MetaField] as JsonObject;

    public static VaultEntry NewSecret(EntryName name, ReadOnlySpan<byte> utf8Value, DateTimeOffset created) =>
        new(BuildNode(name, SecretKind, utf8Value, created, null));

    public static VaultEntry NewFile(EntryName name, ReadOnlySpan<byte> content, string storedPath, DateTimeOffset created) =>
        new(BuildNode(name, FileKind, content, created, StoredPathRules.Normalize(storedPath)));

    public static VaultEntry NewRequired(
        EntryName name, string? note, DateTimeOffset created, string? shape = null, string? path = null, IReadOnlyList<string>? hosts = null, bool hello = false)
    {
        var entry = new VaultEntry(BuildNode(name, RequiredKind, [], created, null));
        entry.SetRequired(note, shape, path, hosts, hello);
        return entry;
    }

    public static VaultEntry NewSshKey(
        EntryName name, ReadOnlySpan<byte> content, string storedPath, SshKeyInfo ssh, ReadOnlySpan<byte> previous, DateTimeOffset created)
    {
        ArgumentNullException.ThrowIfNull(ssh);
        var node = BuildNode(name, FileKind, content, created, StoredPathRules.Normalize(storedPath));
        var meta = (JsonObject)node[MetaField]!;
        var sshNode = new JsonObject
        {
            [TypeField] = ssh.Type,
            [PublicKeyField] = SshKeyInfo.NormalizePublicKey(ssh.PublicKey),
        };
        if (ssh.Hosts.Count > 0)
        {
            sshNode[HostsField] = ToArray(ssh.Hosts);
        }

        if (ssh.Agent)
        {
            sshNode[AgentField] = true;
        }

        if (ssh.ReplacedAt is { } replacedAt)
        {
            sshNode[ReplacedAtField] = replacedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        meta[SshField] = sshNode;
        if (!previous.IsEmpty)
        {
            node[PreviousField] = Convert.ToBase64String(previous);
        }

        return new VaultEntry(node);
    }

    public static VaultEntry NewCredential(
        EntryName name, string? userName, ReadOnlySpan<byte> password, TotpSecret? totp, IReadOnlyList<string> urls, DateTimeOffset created)
    {
        ArgumentNullException.ThrowIfNull(urls);
        var node = BuildNode(name, CredentialKind, password, created, null);
        WriteCredentialFields(node, name, userName, totp, urls);
        return new VaultEntry(node);
    }

    public byte[] GetValue() => IsHello
        ? throw new HelloException($"'{Name}' is sealed by Windows Hello; unseal it first.")
        : Convert.FromBase64String(ReadString(Node, ValueField));

    internal byte[] GetSealedValue() => Convert.FromBase64String(ReadString(Node, ValueField));

    internal JsonObject SealablePayload()
    {
        if (IsHello)
        {
            throw new HelloException($"'{Name}' is already sealed by Windows Hello.");
        }

        if (!(IsSecret || IsFile || IsCredential) || Ssh is not null)
        {
            throw new VaultException($"'{Name}' is a {(Ssh is null ? Kind : "ssh-key")} entry; Windows Hello seals secret, credential and file entries only.");
        }

        var payload = new JsonObject { [ValueField] = ReadString(Node, ValueField) };
        foreach (var field in CredentialFields.Where(field => Node[field] is not null))
        {
            payload[field] = Node[field]!.DeepClone();
        }

        return payload;
    }

    internal VaultEntry ToSealed(ReadOnlySpan<byte> blob, string vaultKey)
    {
        var hash = ContentHash();
        var node = (JsonObject)Node.DeepClone();
        var innerKind = Kind;
        node[KindField] = HelloKind;
        node[ValueField] = Convert.ToBase64String(blob);
        RemoveCredentialFields(node);
        var meta = node[MetaField] as JsonObject ?? new JsonObject();
        node[MetaField] = meta;
        meta[HelloField] = new JsonObject { [KindField] = innerKind, [HashField] = hash, [VaultKeyField] = vaultKey };
        return new VaultEntry(node);
    }

    internal VaultEntry ToUnsealed(JsonObject payload)
    {
        var node = (JsonObject)Node.DeepClone();
        node[KindField] = HelloInnerKind;
        node[ValueField] = payload[ValueField]!.GetValue<string>();
        foreach (var field in CredentialFields.Where(field => payload[field] is not null))
        {
            node[field] = payload[field]!.DeepClone();
        }

        (node[MetaField] as JsonObject)?.Remove(HelloField);
        return new VaultEntry(node);
    }

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
        if (IsHello)
        {
            return HelloHash!;
        }

        var value = GetValue();
        var previous = GetPrevious();
        try
        {
            if (IsCredential)
            {
                return ComputeCredentialHash(UserName, value, Totp, Urls);
            }

            return Ssh is { } ssh
                ? ComputeSshKeyHash(StoredPath, value, previous, ssh.Hosts)
                : ComputeContentHash(Kind, StoredPath, value);
        }
        finally
        {
            Array.Clear(value);
            if (previous is not null)
            {
                Array.Clear(previous);
            }
        }
    }

    public static string ComputeSshKeyHash(string? storedPath, ReadOnlySpan<byte> value, ReadOnlySpan<byte> previous, IReadOnlyList<string> hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        using var buffer = new MemoryStream();
        AppendField(buffer, value);
        AppendField(buffer, previous);
        AppendField(buffer, Encoding.UTF8.GetBytes(string.Join('\n', hosts)));
        var content = buffer.ToArray();
        try
        {
            return ComputeContentHash(FileKind, storedPath, content);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(content);
        }
    }

    public string SyncHash() =>
        IsRequired ? ComputeDeclarationHash(RequiredNote, RequiredShape, RequiredPath, RequiredHosts) : ContentHash();

    public static string ComputeDeclarationHash(string? note, string? shape, string? path = null, IReadOnlyList<string>? hosts = null)
    {
        var form = path is null ? shape : $"{shape}|{path}";
        if (hosts is { Count: > 0 })
        {
            form = $"{form}|{string.Join(',', hosts)}";
        }

        return ComputeContentHash(RequiredKind, form, Encoding.UTF8.GetBytes(note ?? string.Empty));
    }

    public VaultEntry AsPersonalValue()
    {
        if (!IsRequired || IsMissing)
        {
            throw new VaultException($"'{Name}' holds no personal value.");
        }

        var copy = Clone();
        copy.Meta?.Remove(RequiredField);
        copy.Meta?.Remove(KeePassField);
        copy.Meta?.Remove(LocalField);
        return copy;
    }

    internal void SetRequired(string? note, string? shape, string? path, IReadOnlyList<string>? hosts = null, bool hello = false)
    {
        if (hello && shape == SshKeyShape)
        {
            throw new VaultException($"'{Name}' is an SSH key; Windows Hello seals secret, credential and file entries only.");
        }

        var required = new JsonObject();
        if (hello)
        {
            required[HelloField] = true;
        }

        if (hosts is { Count: > 0 })
        {
            required[HostsField] = ToArray(hosts.Select(SshKeyInfo.NormalizeHost));
        }

        if (!string.IsNullOrEmpty(note))
        {
            required[NoteField] = note;
        }

        if (!string.IsNullOrEmpty(shape))
        {
            required[ShapeField] = shape;
        }

        if (!string.IsNullOrEmpty(path))
        {
            required[PathField] = StoredPathRules.Normalize(path);
        }

        EnsureMeta()[RequiredField] = required;
        Meta!.Remove(LocalField);
    }

    internal void SetSshHosts(IReadOnlyList<string> hosts)
    {
        if (IsRequired)
        {
            SetRequired(RequiredNote, RequiredShape, RequiredPath, hosts, IsRequiredHello);
        }
        else if (Meta?[SshField] is JsonObject ssh)
        {
            ssh.Remove(HostsField);
            if (hosts.Count > 0)
            {
                ssh[HostsField] = ToArray(hosts);
            }
        }
        else
        {
            throw new VaultException($"'{Name}' is not an SSH key.");
        }
    }

    internal void DropPersonalValue()
    {
        if (IsRequired && !IsMissing)
        {
            Node[KindField] = RequiredKind;
            Node[ValueField] = string.Empty;
            RemoveCredentialFields(Node);
            Node.Remove(PreviousField);
            Meta?.Remove(SshField);
            Meta?.Remove(HelloField);
            Meta?.Remove(RefField);
        }
    }

    internal void MarkHello() => EnsureMeta()[HelloField] = new JsonObject();

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

        if (IsCredential || IsHello || other.IsHello || Ssh is not null || other.Ssh is not null)
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
        Node[KindField] = source.StoredKind;
        Node[ValueField] = ReadString(source.Node, ValueField);
        Node.Remove(PreviousField);
        if (source.Node[PreviousField] is { } previous)
        {
            Node[PreviousField] = previous.DeepClone();
        }

        if (source.Meta?[SshField] is null)
        {
            Meta?.Remove(SshField);
        }

        if (source.Meta?[HelloField] is null)
        {
            Meta?.Remove(HelloField);
        }

        if (source.Meta?[RefField] is null)
        {
            Meta?.Remove(RefField);
        }

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
                && IsOptionalString(requiredFields[NoteField])
                && IsOptionalString(requiredFields[ShapeField])
                && IsOptionalString(requiredFields[PathField])
                && IsOptionalHosts(requiredFields[HostsField])
                && (requiredFields[HelloField] is null || (requiredFields[HelloField] is JsonValue requiredHello && requiredHello.TryGetValue<bool>(out _)))))
        {
            throw new VaultException($"Vault entry '{Name}' has a 'required' field that is damaged.");
        }

        if (fields[SshField] is { } ssh
            && !(ssh is JsonObject sshFields
                && sshFields[TypeField] is JsonValue type && type.TryGetValue<string>(out var typeText) && SshKeyInfo.Types.Contains(typeText)
                && sshFields[PublicKeyField] is JsonValue publicKey && publicKey.TryGetValue<string>(out var publicKeyText)
                && IsPublicKey(publicKeyText)
                && IsOptionalHosts(sshFields[HostsField])
                && (sshFields[AgentField] is null || (sshFields[AgentField] is JsonValue agent && agent.TryGetValue<bool>(out _)))
                && (sshFields[ReplacedAtField] is null
                    || (sshFields[ReplacedAtField] is JsonValue replaced && replaced.TryGetValue<string>(out var replacedText)
                        && DateTimeOffset.TryParse(replacedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _)))))
        {
            throw new VaultException($"Vault entry '{Name}' has SSH key data that is damaged.");
        }

        if (fields[HelloField] is { } hello
            && !(hello is JsonObject { Count: 0 } && StoredKind != HelloKind)
            && !(hello is JsonObject helloFields
                && helloFields[KindField] is JsonValue innerKind && innerKind.TryGetValue<string>(out var innerText)
                && innerText is SecretKind or FileKind or CredentialKind
                && helloFields[HashField] is JsonValue helloHash && helloHash.TryGetValue<string>(out _)
                && helloFields[VaultKeyField] is JsonValue vaultKey && vaultKey.TryGetValue<string>(out _)))
        {
            throw new VaultException($"Vault entry '{Name}' has Windows Hello data that is damaged.");
        }

        if (fields[RefField] is { } reference
            && !(reference is JsonObject refFields
                && refFields[RefDatabaseField] is JsonValue database && database.TryGetValue<string>(out _)
                && refFields[RefUuidField] is JsonValue uuid && uuid.TryGetValue<string>(out var uuidText) && KeePassReference.IsUuid(uuidText)
                && refFields[RefTitleField] is JsonValue title && title.TryGetValue<string>(out _)
                && IsOptionalString(refFields[RefAttachmentField])))
        {
            throw new VaultException($"Vault entry '{Name}' has KeePass reference data that is damaged.");
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

    private static bool IsOptionalString(JsonNode? node) => node is null || (node is JsonValue value && value.TryGetValue<string>(out _));

    private static bool IsOptionalStrings(JsonNode? node) =>
        node is null || (node is JsonArray items && items.All(item => item is JsonValue value && value.TryGetValue<string>(out _)));

    private static bool IsOptionalHosts(JsonNode? node) =>
        node is null || (node is JsonArray items && items.All(item => item is JsonValue value && value.TryGetValue<string>(out var host) && SshKeyInfo.IsHost(host)));

    private static bool IsPublicKey(string line)
    {
        try
        {
            _ = SshKeyInfo.NormalizePublicKey(line);
            return true;
        }
        catch (VaultException)
        {
            return false;
        }
    }

    private static List<string> ReadStrings(JsonNode? node) =>
        node is JsonArray items ? items.Select(item => item!.GetValue<string>()).ToList() : [];

    private static JsonArray ToArray(IEnumerable<string> values) =>
        new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

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
