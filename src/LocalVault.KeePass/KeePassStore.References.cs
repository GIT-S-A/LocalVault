using System.Text.RegularExpressions;
using KeePassLib;
using KeePassLib.Security;
using KeePassLib.Utility;
using LocalVault.Core;

namespace LocalVault.KeePass;

public sealed partial class KeePassStore
{
    public const string RefField = "LocalVault.Ref";
    public const string RefShapeField = "LocalVault.RefShape";
    public const string ReferenceKind = "reference";
    public const int MaxReferenceChain = 8;
    private const string ReferenceMarker = "{REF:";

    // KeePass field reference syntax {REF:<wanted field>@<search field>:<text>}; only search by UUID (I) is resolved.
    [GeneratedRegex(@"^\{REF:([TUPAN])@I:([0-9A-Fa-f]{32})\}$", RegexOptions.IgnoreCase)]
    private static partial Regex WholeReference();

    private static readonly string[] ReferenceFields = [PwDefs.PasswordField, PwDefs.UserNameField, PwDefs.UrlField];

    private static readonly string[] ReferenceShapes = [VaultEntry.SecretKind, VaultEntry.CredentialKind, VaultEntry.FileKind, VaultEntry.SshKeyShape];

    private sealed record ReadContext(IReferenceStores? Stores, List<string> Skipped, List<ItemLink> Links, List<UnresolvedReference> Unresolved);

    private static bool HasFieldReference(PwEntry entry) =>
        ReferenceFields.Any(key => entry.Strings.ReadSafe(key).Contains(ReferenceMarker, StringComparison.OrdinalIgnoreCase));

    private static string FieldReferenceRefusal(EntryName name) =>
        $"its KeePass entry uses a {{REF:}} field. Use lv set {name} -Reference \"<group/title>\" instead.";

    private static ItemLink ReadLink(PwEntry entry, EntryName name, bool personal)
    {
        var shape = entry.Strings.ReadSafe(RefShapeField);
        if (!ReferenceShapes.Contains(shape, StringComparer.Ordinal))
        {
            throw new VaultException($"'{RefShapeField}' must be {string.Join(", ", ReferenceShapes)}, not '{shape}'");
        }

        return new ItemLink(name, ItemReference.ParseLink(entry.Strings.ReadSafe(RefField), RefField), shape, personal);
    }

    private static StoreItem? ResolveLink(PwEntry entry, ItemLink link, ReadContext context)
    {
        context.Links.Add(link);
        if (context.Stores is null)
        {
            return null;
        }

        try
        {
            var store = context.Stores.Open(link.Reference, link.IsPersonal);
            var storedPath = entry.Strings.ReadSafe(PathField);
            var item = store.Store.ReadReferenced(link, storedPath.Length == 0 ? null : storedPath, link.IsPersonal ? [] : ReadHosts(entry), store.Name);
            item.IsHello = IsSet(entry, HelloField);
            return item;
        }
        catch (Exception ex) when (ex is VaultException or FormatException)
        {
            context.Unresolved.Add(new UnresolvedReference(link.Name, ex.Message, link.IsPersonal));
            return null;
        }
    }

    public StoreItem ReadReferenced(ItemLink link, string? storedPath, IReadOnlyList<string> hosts, string storeName)
    {
        ArgumentNullException.ThrowIfNull(link);
        var target = FindByUuid(link.Reference.ItemId)
            ?? throw new VaultException($"the KeePass entry it points to is no longer in store '{storeName}' (deleted or in the recycle bin).");
        var item = ReadTargetContent(target, link.Name, link.Shape, link.Reference.Attachment, storedPath, hosts, link.IsPersonal, storeName);
        item.Reference = link.Reference with { Title = PathOf(target) };
        return item;
    }

    public StoreItem ReadReferenceTarget(string search, EntryName name, string? shape, string? storedPath, IReadOnlyList<string> hosts, bool personal, string storeName)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(hosts);
        var target = FindReferenceTarget(search);
        shape ??= LooksLikeCredential(target) ? VaultEntry.CredentialKind : VaultEntry.SecretKind;
        string? attachment = null;
        if (shape is VaultEntry.FileKind or VaultEntry.SshKeyShape)
        {
            if (storedPath is null)
            {
                throw new VaultException($"'{name}' has no path to restore the file to; declare it with a path before referencing a file.");
            }

            attachment = ChooseAttachment(target, AttachmentName(storedPath), shape == VaultEntry.SshKeyShape);
        }

        var item = ReadTargetContent(target, name, shape, attachment, storedPath, hosts, personal, storeName);
        item.Reference = new ItemReference(DatabaseId, target.Uuid.ToHexString(), attachment, PathOf(target));
        return item;
    }

    private StoreItem ReadTargetContent(
        PwEntry target, EntryName name, string shape, string? attachment, string? storedPath, IReadOnlyList<string> hosts, bool personal, string storeName)
    {
        var title = PathOf(target);
        if (shape is VaultEntry.FileKind or VaultEntry.SshKeyShape)
        {
            var sshKey = shape == VaultEntry.SshKeyShape;
            if (storedPath is null)
            {
                throw new VaultException($"its KeePass entry has no '{PathField}'.");
            }

            var binary = attachment is null ? null : target.Binaries.Get(attachment);
            if (binary is null)
            {
                throw new VaultException($"'{title}' in store '{storeName}' has no {(sshKey ? "private key" : "file")} '{attachment}'.");
            }

            var content = binary.ReadData();
            if (!sshKey)
            {
                return new StoreItem(name, VaultEntry.FileKind, StoredPathRules.Normalize(storedPath), content);
            }

            string publicKey;
            try
            {
                publicKey = OpenSshPrivateKey.PublicKeyOf(content);
            }
            catch (VaultException)
            {
                Array.Clear(content);
                throw new VaultException($"'{title}' in store '{storeName}' doesn't hold an OpenSSH private key (expected '{OpenSshPrivateKey.Armor}').");
            }

            _ = SshKeyPath.Validate(storedPath);
            return new StoreItem(name, StoredPathRules.Normalize(storedPath), content, null, new SshKeyInfo(SshKeyInfo.TypeOf(publicKey), publicKey, hosts), personal);
        }

        var resolved = WithFieldReferencesResolved(target);
        if (resolved.Strings.ReadSafe(PwDefs.PasswordField).Length == 0)
        {
            throw new VaultException($"'{title}' in store '{storeName}' has no password.");
        }

        return shape == VaultEntry.CredentialKind
            ? ReadCredential(resolved, name)
            : new StoreItem(name, VaultEntry.SecretKind, null, resolved.Strings.GetSafe(PwDefs.PasswordField).ReadUtf8());
    }

    private PwEntry WithFieldReferencesResolved(PwEntry entry)
    {
        var resolved = entry.CloneDeep();
        foreach (var key in ReferenceFields.Where(entry.Strings.Exists))
        {
            var text = entry.Strings.ReadSafe(key);
            if (!text.Contains(ReferenceMarker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var match = WholeReference().Match(text);
            if (!match.Success)
            {
                throw new VaultException($"'{PathOf(entry)}' holds a {{REF:}} LocalVault can't read: only a whole-field {{REF:<T|U|P|A|N>@I:<uuid>}}.");
            }

            var value = FollowField(char.ToUpperInvariant(match.Groups[1].Value[0]), match.Groups[2].Value);
            resolved.Strings.Set(key, new ProtectedString(entry.Strings.Get(key).IsProtected, value));
        }

        return resolved;
    }

    private string FollowField(char field, string uuid)
    {
        for (var hop = 0; hop < MaxReferenceChain; hop++)
        {
            var target = FindByUuid(uuid)
                ?? throw new VaultException($"a {{REF:}} field points to entry {uuid.ToUpperInvariant()}, which is no longer in this database.");
            var value = field switch
            {
                'T' => target.Strings.ReadSafe(PwDefs.TitleField),
                'U' => target.Strings.ReadSafe(PwDefs.UserNameField),
                'P' => target.Strings.ReadSafe(PwDefs.PasswordField),
                'A' => target.Strings.ReadSafe(PwDefs.UrlField),
                _ => target.Strings.ReadSafe(PwDefs.NotesField),
            };
            var next = WholeReference().Match(value);
            if (!next.Success)
            {
                if (value.Contains(ReferenceMarker, StringComparison.OrdinalIgnoreCase))
                {
                    throw new VaultException($"'{PathOf(target)}' holds a {{REF:}} LocalVault can't read.");
                }

                return value;
            }

            field = char.ToUpperInvariant(next.Groups[1].Value[0]);
            uuid = next.Groups[2].Value;
        }

        throw new VaultException($"its {{REF:}} fields form a chain longer than {MaxReferenceChain} entries.");
    }

    private PwEntry? FindByUuid(string uuid)
    {
        var entry = database.RootGroup.FindEntry(new PwUuid(MemUtil.HexStringToByteArray(uuid)), true);
        return entry is null || IsInRecycleBin(entry) ? null : entry;
    }

    private bool IsInRecycleBin(PwEntry entry) =>
        database.RecycleBinEnabled
        && database.RootGroup.FindGroup(database.RecycleBinUuid, true) is { } bin
        && entry.IsContainedIn(bin);

    private static string PathOf(PwEntry entry)
    {
        var group = entry.ParentGroup?.GetFullPath("/", false) ?? string.Empty;
        var title = entry.Strings.ReadSafe(PwDefs.TitleField);
        return group.Length == 0 ? title : $"{group}/{title}";
    }

    private static string ShapeOf(VaultEntry entry) =>
        entry.Ssh is not null ? VaultEntry.SshKeyShape
        : entry.IsFile ? VaultEntry.FileKind
        : entry.IsCredential ? VaultEntry.CredentialKind
        : VaultEntry.SecretKind;

    private static void WriteReference(PwEntry target, VaultEntry entry, ItemReference reference, bool writeHosts)
    {
        RemoveCredentialStrings(target);
        target.Strings.Remove(PwDefs.PasswordField);
        target.Binaries.Clear();
        foreach (var field in new[] { SshTypeField, SshPublicKeyField, SshAgentField, ReplacedAtField })
        {
            target.Strings.Remove(field);
        }

        target.Strings.Set(RefField, new ProtectedString(false, reference.Link));
        target.Strings.Set(RefShapeField, new ProtectedString(false, ShapeOf(entry)));
        SetOrRemove(target, PathField, entry.IsFile ? entry.StoredPath : null);
        if (entry.Ssh is { } ssh)
        {
            target.Strings.Set(SshTypeField, new ProtectedString(false, ssh.Type));
            target.Strings.Set(SshPublicKeyField, new ProtectedString(false, ssh.PublicKey));
            if (writeHosts)
            {
                SetOrRemove(target, SshHostsField, ssh.Hosts.Count > 0 ? string.Join('\n', ssh.Hosts) : null);
            }
        }
        else if (writeHosts)
        {
            target.Strings.Remove(SshHostsField);
        }
    }

    private static void RemoveReferenceStrings(PwEntry target)
    {
        target.Strings.Remove(RefField);
        target.Strings.Remove(RefShapeField);
    }

    private PwEntry FindReferenceTarget(string search)
    {
        var path = search.Trim().Trim('/');
        var candidates = database.RootGroup.GetEntries(true)
            .Where(entry => !IsInRecycleBin(entry) && !IsUnderLocalVaultGroup(entry))
            .ToList();
        var matches = candidates.Where(entry => string.Equals(PathOf(entry), path, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0)
        {
            matches = candidates.Where(entry => string.Equals(entry.Strings.ReadSafe(PwDefs.TitleField), path, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new VaultException($"No KeePass entry '{search}' outside {StoreGroupPath.Root}/ in '{Path}'."),
            _ => throw new VaultException($"More than one KeePass entry matches '{search}': {string.Join(", ", matches.Select(PathOf))}. Give the group path."),
        };
    }

    private bool IsUnderLocalVaultGroup(PwEntry entry)
    {
        var group = database.RootGroup.Groups.FirstOrDefault(g => string.Equals(g.Name, StoreGroupPath.Root, StringComparison.OrdinalIgnoreCase));
        return group is not null && entry.IsContainedIn(group);
    }

    private static string ChooseAttachment(PwEntry target, string preferred, bool privateKey)
    {
        var names = target.Binaries.Select(pair => pair.Key)
            .Where(name => !privateKey || !name.EndsWith(SshKeyPath.PublicKeySuffix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (names.Contains(preferred, StringComparer.Ordinal))
        {
            return preferred;
        }

        return names.Count switch
        {
            1 => names[0],
            0 => throw new VaultException($"KeePass entry '{PathOf(target)}' has no attachment to reference."),
            _ => throw new VaultException($"KeePass entry '{PathOf(target)}' has several attachments ({string.Join(", ", names)}); rename the one to use to {preferred}."),
        };
    }
}
