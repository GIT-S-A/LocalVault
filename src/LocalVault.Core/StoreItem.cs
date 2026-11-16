using System.Security.Cryptography;

namespace LocalVault.Core;

public sealed class StoreItem : IDisposable
{
    public StoreItem(EntryName name, string kind, string? storedPath, byte[] value)
    {
        Name = name;
        Kind = kind;
        StoredPath = storedPath;
        Value = value;
        Hash = VaultEntry.ComputeContentHash(kind, storedPath, value);
    }

    public StoreItem(EntryName name, string storedPath, byte[] value, byte[]? previous, SshKeyInfo ssh, bool isPersonal)
    {
        Name = name;
        Kind = VaultEntry.FileKind;
        StoredPath = storedPath;
        Value = value;
        Previous = previous;
        Ssh = ssh;
        IsPersonal = isPersonal;
        Hash = VaultEntry.ComputeSshKeyHash(storedPath, value, previous ?? [], ssh.Hosts);
    }

    public StoreItem(EntryName name, string note, string? shape, string? requiredPath, IReadOnlyList<string> requiredHosts, StoreItem? personalValue)
    {
        Name = name;
        Kind = VaultEntry.RequiredKind;
        Value = System.Text.Encoding.UTF8.GetBytes(note);
        Shape = shape;
        RequiredPath = requiredPath;
        RequiredHosts = requiredHosts;
        PersonalValue = personalValue;
        Hash = VaultEntry.ComputeDeclarationHash(note, shape, requiredPath, requiredHosts);
    }

    public StoreItem(EntryName name, string? userName, byte[] password, TotpSecret? totp, IReadOnlyList<string> urls)
    {
        Name = name;
        Kind = VaultEntry.CredentialKind;
        Value = password;
        UserName = userName;
        Totp = totp;
        Urls = urls;
        Hash = VaultEntry.ComputeCredentialHash(userName, password, totp, urls);
    }

    public EntryName Name { get; }

    public string Kind { get; }

    public string? StoredPath { get; }

    public string Hash { get; }

    public string? Shape { get; }

    public string? RequiredPath { get; }

    public bool IsUnderHome => StoredPath is { } path && StoredPathRules.IsUnderHome(path);

    public IReadOnlyList<string> RequiredHosts { get; } = [];

    public SshKeyInfo? Ssh { get; }

    public bool IsPersonal { get; }

    public StoreItem? PersonalValue { get; }

    public bool IsHello { get; set; }

    public bool IsRequiredHello { get; set; }

    public ItemReference? Reference { get; set; }

    internal byte[] Value { get; }

    internal byte[]? Previous { get; }

    internal string? UserName { get; }

    internal TotpSecret? Totp { get; }

    internal IReadOnlyList<string> Urls { get; } = [];

    public VaultEntry ToVaultEntry(DateTimeOffset created)
    {
        var entry = Kind switch
        {
            VaultEntry.FileKind when Ssh is not null => VaultEntry.NewSshKey(Name, Value, StoredPath!, Ssh, Previous ?? [], created),
            VaultEntry.FileKind => VaultEntry.NewFile(Name, Value, StoredPath!, created),
            VaultEntry.RequiredKind => VaultEntry.NewRequired(Name, System.Text.Encoding.UTF8.GetString(Value), created, Shape, RequiredPath, RequiredHosts, IsRequiredHello),
            VaultEntry.CredentialKind => VaultEntry.NewCredential(Name, UserName, Value, Totp, Urls, created),
            _ => VaultEntry.NewSecret(Name, Value, created),
        };
        return Reference is null ? entry : entry.WithReference(Reference);
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(Value);
        if (Previous is not null)
        {
            CryptographicOperations.ZeroMemory(Previous);
        }

        PersonalValue?.Dispose();
    }
}
