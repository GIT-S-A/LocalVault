using System.Text.RegularExpressions;
using KeePassLib;
using KeePassLib.Security;
using KeePassLib.Utility;

namespace LocalVault.Core;

public sealed partial record KeePassReference(string DatabaseId, string Uuid, string? Attachment, string Title)
{
    public static bool IsUuid(string text) => text is not null && UuidPattern().IsMatch(text);

    [GeneratedRegex("^[0-9A-Fa-f]{32}$")]
    private static partial Regex UuidPattern();
}

public sealed partial class KeePassVault
{
    public const string RefField = "LocalVault.Ref";
    public const int MaxReferenceChain = 8;
    private const string ReferenceMarker = "{REF:";

    // KeePass field reference syntax {REF:<wanted field>@<search field>:<text>}; only search by UUID (I) is resolved.
    [GeneratedRegex(@"^\{REF:([TUPAN])@I:([0-9A-Fa-f]{32})\}$", RegexOptions.IgnoreCase)]
    private static partial Regex WholeReference();

    private static readonly string[] ReferenceFields = [PwDefs.PasswordField, PwDefs.UserNameField, PwDefs.UrlField];

    private static bool HasReference(PwEntry entry) =>
        entry.Strings.ReadSafe(RefField).Length > 0
        || ReferenceFields.Any(key => entry.Strings.ReadSafe(key).Contains(ReferenceMarker, StringComparison.OrdinalIgnoreCase));

    private static string Placeholder(char field, string uuid) => $"{{REF:{field}@I:{uuid}}}";

    private (PwEntry Resolved, KeePassReference Reference) Resolve(PwEntry entry)
    {
        var resolved = entry.CloneDeep();
        PwEntry? direct = null;
        PwEntry? passwordSource = null;
        string? attachment = null;
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
                throw new VaultException($"field '{key}' holds a reference LocalVault can't read: only a whole-field {{REF:<T|U|P|A|N>@I:<uuid>}}");
            }

            var first = FindByUuid(match.Groups[2].Value);
            var (value, last) = FollowField(char.ToUpperInvariant(match.Groups[1].Value[0]), match.Groups[2].Value);
            resolved.Strings.Set(key, new ProtectedString(entry.Strings.Get(key).IsProtected, value));
            if (key == PwDefs.PasswordField)
            {
                direct = first;
                passwordSource = last;
            }

            direct ??= first;
        }

        var fileReference = entry.Strings.ReadSafe(RefField);
        if (fileReference.Length > 0)
        {
            var separator = fileReference.IndexOf('/', StringComparison.Ordinal);
            if (separator != 32 || !KeePassReference.IsUuid(fileReference[..separator]) || separator == fileReference.Length - 1)
            {
                throw new VaultException($"'{RefField}' must be <uuid>/<attachment name>, not '{fileReference}'");
            }

            attachment = fileReference[(separator + 1)..];
            direct = FindByUuid(fileReference[..separator]);
            var content = FollowAttachment(fileReference[..separator], attachment);
            var storedPath = entry.Strings.ReadSafe(PathField);
            if (storedPath.Length == 0)
            {
                throw new VaultException($"a file reference needs '{PathField}'");
            }

            resolved.Binaries.Clear();
            resolved.Binaries.Set(AttachmentName(storedPath), new ProtectedBinary(true, content));
        }

        var credential = entry.Strings.Exists(PwDefs.UserNameField) || entry.Strings.Exists(PwDefs.UrlField);
        if (passwordSource is not null && credential && ReadTotp(resolved) is null)
        {
            foreach (var key in passwordSource.Strings.GetKeys().Where(key => key == XcOtpField || key.StartsWith(TotpFieldPrefix, StringComparison.Ordinal)))
            {
                resolved.Strings.Set(key, passwordSource.Strings.Get(key));
            }
        }

        if (direct is null)
        {
            throw new VaultException("no reference found");
        }

        return (resolved, new KeePassReference(DatabaseId, direct.Uuid.ToHexString(), attachment, PathOf(direct)));
    }

    private (string Value, PwEntry Source) FollowField(char field, string uuid)
    {
        for (var hop = 0; hop < MaxReferenceChain; hop++)
        {
            var target = FindByUuid(uuid);
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
                    throw new VaultException($"'{PathOf(target)}' holds a reference LocalVault can't read");
                }

                return (value, target);
            }

            field = char.ToUpperInvariant(next.Groups[1].Value[0]);
            uuid = next.Groups[2].Value;
        }

        throw new VaultException($"reference chain longer than {MaxReferenceChain} entries");
    }

    private byte[] FollowAttachment(string uuid, string attachment)
    {
        for (var hop = 0; hop < MaxReferenceChain; hop++)
        {
            var target = FindByUuid(uuid);
            if (target.Binaries.Get(attachment) is { } binary)
            {
                return binary.ReadData();
            }

            var next = target.Strings.ReadSafe(RefField);
            if (next.Length <= 33 || !KeePassReference.IsUuid(next[..32]) || next[32] != '/')
            {
                throw new VaultException($"'{PathOf(target)}' has no attachment '{attachment}'");
            }

            uuid = next[..32];
            attachment = next[33..];
        }

        throw new VaultException($"reference chain longer than {MaxReferenceChain} entries");
    }

    private PwEntry FindByUuid(string uuid)
    {
        var entry = database.RootGroup.FindEntry(new PwUuid(MemUtil.HexStringToByteArray(uuid)), true);
        if (entry is null || IsInRecycleBin(entry))
        {
            throw new VaultException($"reference to {uuid.ToUpperInvariant()} points to no entry in this database");
        }

        return entry;
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

    private void WriteReference(PwEntry target, VaultEntry entry, KeePassReference reference)
    {
        foreach (var key in target.Strings.GetKeys().Where(IsCredentialString).ToList())
        {
            target.Strings.Remove(key);
        }

        target.Strings.Remove(RefField);
        if (entry.IsFile)
        {
            target.Strings.Remove(PwDefs.PasswordField);
            target.Strings.Set(PathField, new ProtectedString(false, entry.StoredPath!));
            target.Binaries.Clear();
            target.Strings.Set(RefField, new ProtectedString(false, $"{reference.Uuid}/{reference.Attachment}"));
            if (entry.Ssh is { } ssh)
            {
                target.Strings.Set(SshTypeField, new ProtectedString(false, ssh.Type));
                target.Strings.Set(SshPublicKeyField, new ProtectedString(false, ssh.PublicKey));
            }

            return;
        }

        target.Strings.Remove(PathField);
        target.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, Placeholder('P', reference.Uuid)));
        if (entry.IsCredential)
        {
            target.Strings.Set(PwDefs.UserNameField, new ProtectedString(database.MemoryProtection.ProtectUserName, Placeholder('U', reference.Uuid)));
            target.Strings.Set(PwDefs.UrlField, new ProtectedString(database.MemoryProtection.ProtectUrl, Placeholder('A', reference.Uuid)));
        }
    }

    public KeePassItem ReadReferenceTarget(string search, VaultEntry declaration)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(declaration);
        var target = FindReferenceTarget(search);
        var uuid = target.Uuid.ToHexString();
        var probe = new PwEntry(false, false);
        probe.Strings.Set(PwDefs.TitleField, new ProtectedString(false, declaration.Name.FullName));
        if (declaration.IsFileShape || declaration.IsSshKeyShape)
        {
            var storedPath = declaration.RequiredPath
                ?? throw new VaultException($"'{declaration.Name}' declares no path; declare it with a path before referencing a file.");
            var attachment = ChooseAttachment(target, AttachmentName(storedPath), declaration.IsSshKeyShape);
            probe.Strings.Set(PathField, new ProtectedString(false, storedPath));
            probe.Strings.Set(RefField, new ProtectedString(false, $"{uuid}/{attachment}"));
            if (declaration.IsSshKeyShape)
            {
                var publicKey = OpenSshPrivateKey.PublicKeyOf(target.Binaries.Get(attachment)!.ReadData());
                probe.Strings.Set(SshTypeField, new ProtectedString(false, SshKeyInfo.TypeOf(publicKey)));
                probe.Strings.Set(SshPublicKeyField, new ProtectedString(false, publicKey));
            }
        }
        else
        {
            probe.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, Placeholder('P', uuid)));
            if (declaration.IsCredentialShape)
            {
                probe.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, Placeholder('U', uuid)));
                probe.Strings.Set(PwDefs.UrlField, new ProtectedString(false, Placeholder('A', uuid)));
            }
        }

        var (resolved, reference) = Resolve(probe);
        var item = ReadPersonalContent(resolved, declaration.Name)
            ?? throw new VaultException($"KeePass entry '{PathOf(target)}' holds no value for '{declaration.Name}'.");
        item.Reference = reference;
        return item;
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
            0 => throw new VaultException($"No KeePass entry '{search}' outside {KeePassGroupPath.Root}/ in '{Path}'."),
            _ => throw new VaultException($"More than one KeePass entry matches '{search}': {string.Join(", ", matches.Select(PathOf))}. Give the group path."),
        };
    }

    private bool IsUnderLocalVaultGroup(PwEntry entry)
    {
        var group = database.RootGroup.Groups.FirstOrDefault(g => string.Equals(g.Name, KeePassGroupPath.Root, StringComparison.OrdinalIgnoreCase));
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
