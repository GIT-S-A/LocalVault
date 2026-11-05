using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KeePassLib;
using KeePassLib.Keys;
using KeePassLib.Security;
using KeePassLib.Serialization;

namespace LocalVault.Core;

public static class KeePassGroupPath
{
    public const string Root = "LocalVault";

    public static IReadOnlyList<string> For(VaultLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        var segments = new List<string> { Root };
        if (location.GitRoot is { } gitRoot)
        {
            segments.Add(Path.GetFileName(Path.TrimEndingDirectorySeparator(gitRoot)));
            var relative = Path.GetRelativePath(gitRoot, location.Directory);
            if (relative != ".")
            {
                segments.AddRange(relative.Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries));
            }
        }
        else
        {
            segments.Add(Path.GetFileName(Path.TrimEndingDirectorySeparator(location.Directory)));
        }

        var fileName = Path.GetFileName(location.Path);
        if (!string.Equals(fileName, VaultLocation.DefaultFileName, StringComparison.OrdinalIgnoreCase))
        {
            segments.Add(fileName);
        }

        return segments;
    }

    public static IReadOnlyList<string>? ProjectOf(VaultLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return location.GitRoot is { } gitRoot
            ? [Root, Path.GetFileName(Path.TrimEndingDirectorySeparator(gitRoot))]
            : null;
    }

    public static string Display(IReadOnlyList<string> segments) => string.Join('/', segments);
}

public sealed class KeePassItem : IDisposable
{
    internal KeePassItem(EntryName name, string kind, string? storedPath, byte[] value)
    {
        Name = name;
        Kind = kind;
        StoredPath = storedPath;
        Value = value;
        Hash = VaultEntry.ComputeContentHash(kind, storedPath, value);
    }

    internal KeePassItem(EntryName name, string storedPath, byte[] value, byte[]? previous, SshKeyInfo ssh, bool isPersonal)
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

    internal KeePassItem(EntryName name, string note, string? shape, string? requiredPath, IReadOnlyList<string> requiredHosts, KeePassItem? personalValue)
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

    internal KeePassItem(EntryName name, string? userName, byte[] password, TotpSecret? totp, IReadOnlyList<string> urls)
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

    public KeePassItem? PersonalValue { get; }

    internal byte[] Value { get; }

    internal byte[]? Previous { get; }

    internal string? UserName { get; }

    internal TotpSecret? Totp { get; }

    internal IReadOnlyList<string> Urls { get; } = [];

    public VaultEntry ToVaultEntry(DateTimeOffset created) => Kind switch
    {
        VaultEntry.FileKind when Ssh is not null => VaultEntry.NewSshKey(Name, Value, StoredPath!, Ssh, Previous ?? [], created),
        VaultEntry.FileKind => VaultEntry.NewFile(Name, Value, StoredPath!, created),
        VaultEntry.RequiredKind => VaultEntry.NewRequired(Name, System.Text.Encoding.UTF8.GetString(Value), created, Shape, RequiredPath, RequiredHosts),
        VaultEntry.CredentialKind => VaultEntry.NewCredential(Name, UserName, Value, Totp, Urls, created),
        _ => VaultEntry.NewSecret(Name, Value, created),
    };

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

public sealed class AutoOpenTarget(string title, string path, string? keyFile, char[] password) : IDisposable
{
    public string Title => title;

    public string Path => path;

    public string? KeyFile => keyFile;

    public ReadOnlySpan<char> Password => password;

    public void Dispose() => Array.Clear(password);
}

public sealed class KeePassGroupContent : IDisposable
{
    internal KeePassGroupContent(IReadOnlyList<KeePassItem> items, IReadOnlyList<string> skipped, string databaseId)
    {
        DatabaseId = databaseId;
        Items = items;
        Skipped = skipped;
    }

    public IReadOnlyList<KeePassItem> Items { get; }

    public IReadOnlyList<string> Skipped { get; }

    public string DatabaseId { get; }

    public KeePassItem? Find(EntryName name) => Items.FirstOrDefault(item => item.Name.Equals(name));

    public void Dispose()
    {
        foreach (var item in Items)
        {
            item.Dispose();
        }
    }
}

public sealed partial class KeePassVault : IDisposable
{
    public const string KindField = "LocalVault.Kind";
    public const string PathField = "LocalVault.Path";
    public const string ShapeField = "LocalVault.Shape";
    public const string RequiredPathField = "LocalVault.RequiredPath";
    public const string DatabaseIdField = "LocalVault.DatabaseId";
    public const string SshTypeField = "LocalVault.SshType";
    public const string SshPublicKeyField = "LocalVault.SshPublicKey";
    public const string SshHostsField = "LocalVault.SshHosts";
    public const string SshAgentField = "LocalVault.SshAgent";
    public const string ReplacedAtField = "LocalVault.ReplacedAt";
    public const string PersonalField = "LocalVault.Personal";
    public const string AutoOpenGroup = "AutoOpen";
    public const string OriginKind = "origin";
    public const string InstallKind = "install";
    public const string OriginTitle = "Repository origin";
    public const string InstallTitle = "Install instructions for localVault";
    public const string RepositoryUrl = "https://github.com/GIT-S-A/LocalVault";
    public const string InstallCommand =
        "$d=New-Item -ItemType Directory \"$env:TEMP\\lv-$(New-Guid)\"; irm " + RepositoryUrl +
        "/releases/latest/download/install.ps1 -OutFile \"$d\\install.ps1\"; & \"$d\\install.ps1\"";
    private const string IfDeviceField = "IfDevice";
    private const string ExtraUrlPrefix = "KP2A_URL";
    private const string TotpFieldPrefix = "TimeOtp-";
    private const string TotpSecretField = "TimeOtp-Secret";
    private const string TotpSecretHexField = "TimeOtp-Secret-Hex";
    private const string TotpSecretBase32Field = "TimeOtp-Secret-Base32";
    private const string TotpSecretBase64Field = "TimeOtp-Secret-Base64";
    private const string TotpLengthField = "TimeOtp-Length";
    private const string TotpPeriodField = "TimeOtp-Period";
    private const string TotpAlgorithmField = "TimeOtp-Algorithm";
    private const string XcOtpField = "otp";

    private readonly PwDatabase database;
    private readonly IOConnectionInfo connection;
    private readonly CompositeKey key;
    private readonly byte[] openedHash;

    private KeePassVault(PwDatabase database, IOConnectionInfo connection, CompositeKey key, byte[] openedHash)
    {
        this.database = database;
        this.connection = connection;
        this.key = key;
        this.openedHash = openedHash;
    }

    public static KeePassVault Open(string path, string? keyFile, ReadOnlySpan<char> password)
    {
        if (!File.Exists(path))
        {
            throw new VaultException($"KeePass database '{path}' does not exist.");
        }

        var key = new CompositeKey();
        if (!password.IsEmpty)
        {
            var utf8 = new byte[Encoding.UTF8.GetByteCount(password)];
            Encoding.UTF8.GetBytes(password, utf8);
            try
            {
                key.AddUserKey(new KcpPassword(utf8));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(utf8);
            }
        }

        if (!string.IsNullOrEmpty(keyFile))
        {
            key.AddUserKey(new KcpKeyFile(keyFile));
        }

        if (key.UserKeyCount == 0)
        {
            throw new VaultException("KeePass needs a master password or a keyfile.");
        }

        var connection = IOConnectionInfo.FromPath(path);
        var openedHash = HashFile(path);
        var database = new PwDatabase();
        try
        {
            database.Open(connection, key, null);
        }
        catch (InvalidCompositeKeyException ex)
        {
            throw new VaultException($"KeePass: the master password or keyfile does not open '{path}'.", ex);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or CryptographicException)
        {
            throw new VaultException($"KeePass: '{path}' could not be opened: {ex.Message}", ex);
        }

        return new KeePassVault(database, connection, key, openedHash);
    }

    public string Path => connection.Path;

    public string DatabaseId => database.RootGroup.Uuid.ToHexString();

    public KeePassGroupContent Read(IReadOnlyList<string> groupPath)
    {
        var group = FindGroup(groupPath, create: false);
        if (group is null)
        {
            return new KeePassGroupContent([], [], DatabaseId);
        }

        var items = new List<KeePassItem>();
        var skipped = new List<string>();
        var duplicates = group.Entries
            .GroupBy(entry => entry.Strings.ReadSafe(PwDefs.TitleField), EntryName.Comparer)
            .Where(sameTitle => sameTitle.Count() > 1)
            .Select(sameTitle => sameTitle.Key)
            .ToHashSet(EntryName.Comparer);
        foreach (var entry in group.Entries.Where(entry => !IsGuide(entry)))
        {
            var title = entry.Strings.ReadSafe(PwDefs.TitleField);
            if (duplicates.Contains(title))
            {
                skipped.Add($"{title} (more than one KeePass entry has this title)");
                continue;
            }

            if (!EntryName.TryParse(title, out var name))
            {
                skipped.Add($"{title} (not a valid vault name)");
                continue;
            }

            KeePassItem? item;
            try
            {
                item = ReadItem(entry, name);
            }
            catch (Exception ex) when (ex is VaultException or FormatException)
            {
                skipped.Add($"{title} ({ex.Message})");
                continue;
            }

            if (item is not null)
            {
                items.Add(item);
            }
            else
            {
                skipped.Add($"{title} (file entry without its attachment)");
            }
        }

        return new KeePassGroupContent(items, skipped, DatabaseId);
    }

    public void Put(IReadOnlyList<string> groupPath, VaultEntry entry) => Put(groupPath, entry, personal: false);

    private void Put(IReadOnlyList<string> groupPath, VaultEntry entry, bool personal)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var group = FindGroup(groupPath, create: true)!;
        var target = FindEntry(group, entry.Name);
        if (target is null)
        {
            target = new PwEntry(true, true);
            group.AddEntry(target, true);
        }
        else
        {
            target.CreateBackup(database);
        }

        var value = entry.GetValue();
        try
        {
            var previousKind = target.Strings.ReadSafe(KindField);
            if (previousKind == VaultEntry.CredentialKind)
            {
                RemoveCredentialStrings(target);
            }

            target.Strings.Set(PwDefs.TitleField, new ProtectedString(database.MemoryProtection.ProtectTitle, entry.Name.FullName));
            target.Strings.Set(KindField, new ProtectedString(false, entry.IsRequired ? VaultEntry.RequiredKind : entry.Kind));
            SetOrRemove(target, PersonalField, personal ? "true" : null);
            if (!entry.IsRequired)
            {
                target.Strings.Remove(ShapeField);
                target.Strings.Remove(RequiredPathField);
                RemoveSshStrings(target);
            }

            if (entry.IsCredential && !entry.IsRequired)
            {
                target.Strings.Remove(PathField);
                WriteCredential(target, entry, value);
            }
            else if (entry.IsRequired)
            {
                if (previousKind != VaultEntry.RequiredKind)
                {
                    target.Strings.Remove(PwDefs.PasswordField);
                    target.Strings.Remove(PathField);
                    target.Binaries.Clear();
                    RemoveSshStrings(target);
                }

                WriteDeclaration(target, entry);
            }
            else if (entry.IsFile)
            {
                target.Strings.Remove(PwDefs.PasswordField);
                target.Strings.Set(PathField, new ProtectedString(false, entry.StoredPath!));
                target.Binaries.Clear();
                target.Binaries.Set(AttachmentName(entry.StoredPath!), new ProtectedBinary(true, value));
                WriteSsh(target, entry, writeHosts: true);
            }
            else
            {
                target.Strings.Remove(PathField);
                target.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, value));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(value);
        }

        target.Touch(true, false);
        target.MaintainBackups(database);
    }

    public void PutPersonalValue(IReadOnlyList<string> groupPath, VaultEntry entry, bool withDeclaration)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!withDeclaration)
        {
            Put(groupPath, entry.AsPersonalValue(), personal: true);
            return;
        }

        var group = FindGroup(groupPath, create: true)!;
        var target = FindEntry(group, entry.Name);
        if (target is null)
        {
            target = new PwEntry(true, true);
            group.AddEntry(target, true);
        }
        else
        {
            target.CreateBackup(database);
        }

        var value = entry.GetValue();
        try
        {
            RemoveCredentialStrings(target);
            RemoveSshStrings(target);
            target.Strings.Remove(PersonalField);
            target.Strings.Remove(PathField);
            target.Strings.Remove(PwDefs.PasswordField);
            target.Binaries.Clear();
            target.Strings.Set(PwDefs.TitleField, new ProtectedString(database.MemoryProtection.ProtectTitle, entry.Name.FullName));
            target.Strings.Set(KindField, new ProtectedString(false, VaultEntry.RequiredKind));
            WriteDeclaration(target, entry);
            if (entry.IsCredential)
            {
                WriteCredential(target, entry, value);
            }
            else if (entry.IsFile)
            {
                target.Strings.Set(PathField, new ProtectedString(false, entry.StoredPath!));
                target.Binaries.Set(AttachmentName(entry.StoredPath!), new ProtectedBinary(true, value));
                WriteSsh(target, entry, writeHosts: false);
            }
            else
            {
                target.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, value));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(value);
        }

        target.Touch(true, false);
        target.MaintainBackups(database);
    }

    public void Delete(IReadOnlyList<string> groupPath, EntryName name)
    {
        var group = FindGroup(groupPath, create: false);
        if (group is null || FindEntry(group, name) is not { } entry)
        {
            return;
        }

        group.Entries.Remove(entry);
        if (database.RecycleBinEnabled)
        {
            RecycleBin().AddEntry(entry, true, true);
            entry.Touch(false);
        }
        else
        {
            database.DeletedObjects.Add(new PwDeletedObject(entry.Uuid, DateTime.UtcNow));
        }

        group.Touch(true, false);
    }

    public bool EnsureInstallInstructions()
    {
        var group = database.RootGroup.FindCreateGroup(KeePassGroupPath.Root, true);
        var existing = group.Entries.FirstOrDefault(entry => entry.Strings.ReadSafe(KindField) == InstallKind
            || string.Equals(entry.Strings.ReadSafe(PwDefs.TitleField), InstallTitle, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var notes = existing.Strings.ReadSafe(PwDefs.NotesField);
            if (!RetiredRepositoryUrl().IsMatch(notes))
            {
                return false;
            }

            var madeByModule = existing.Strings.ReadSafe(KindField) == InstallKind;
            existing.CreateBackup(database);
            existing.Strings.Set(PwDefs.NotesField, new ProtectedString(false, madeByModule ? InstallCommand : RetiredRepositoryUrl().Replace(notes, RepositoryUrl)));
            existing.Touch(true, false);
            existing.MaintainBackups(database);
            return true;
        }

        var created = new PwEntry(true, true);
        created.Strings.Set(PwDefs.TitleField, new ProtectedString(false, InstallTitle));
        created.Strings.Set(PwDefs.NotesField, new ProtectedString(false, InstallCommand));
        created.Strings.Set(KindField, new ProtectedString(false, InstallKind));
        group.AddEntry(created, true);
        return true;
    }

    public bool MaintainOrigin(IReadOnlyList<string> projectGroup, RepositoryOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var group = FindGroup(projectGroup, create: true)!;
        var notes = string.Join(
            Environment.NewLine,
            $"git clone {origin.Url} {origin.Folder}",
            $"cd {origin.Folder}",
            "lv load",
            $"LocalVault not installed: see '{InstallTitle}' in the {KeePassGroupPath.Root} group.");
        var entry = group.Entries.FirstOrDefault(e => e.Strings.ReadSafe(KindField) == OriginKind);
        if (entry is not null
            && entry.Strings.ReadSafe(PwDefs.TitleField) == OriginTitle
            && entry.Strings.ReadSafe(PwDefs.UrlField) == origin.Url
            && SameLines(entry.Strings.ReadSafe(PwDefs.NotesField), notes))
        {
            return false;
        }

        if (entry is null)
        {
            entry = new PwEntry(true, true);
            group.AddEntry(entry, true);
        }
        else
        {
            entry.CreateBackup(database);
        }

        entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, OriginTitle));
        entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, origin.Url));
        entry.Strings.Set(PwDefs.NotesField, new ProtectedString(false, notes));
        entry.Strings.Set(KindField, new ProtectedString(false, OriginKind));
        entry.Touch(true, false);
        entry.MaintainBackups(database);
        return true;
    }

    public void Save()
    {
        if (!HashFile(Path).AsSpan().SequenceEqual(openedHash))
        {
            var current = new PwDatabase();
            try
            {
                current.Open(connection, key, null);
                database.MergeIn(current, PwMergeMethod.Synchronize);
            }
            finally
            {
                current.Close();
            }
        }

        database.Save(null);
    }

    public AutoOpenTarget? FindAutoOpen(string? databaseId, IEnumerable<string> knownPaths, string machineName)
    {
        ArgumentNullException.ThrowIfNull(knownPaths);
        if (database.RootGroup.FindCreateGroup(AutoOpenGroup, false) is not { } group)
        {
            return null;
        }

        var paths = knownPaths.Select(NormalizePath).ToList();
        foreach (var entry in group.Entries.Where(entry => ForDevice(entry, machineName)))
        {
            var path = ResolveRelative(entry.Strings.ReadSafe(PwDefs.UrlField));
            var id = entry.Strings.ReadSafe(DatabaseIdField);
            var matches = (databaseId is not null && string.Equals(id, databaseId, StringComparison.OrdinalIgnoreCase))
                || (path.Length > 0 && paths.Contains(NormalizePath(path), StringComparer.OrdinalIgnoreCase));
            if (matches)
            {
                var keyFile = entry.Strings.ReadSafe(PwDefs.UserNameField);
                return new AutoOpenTarget(
                    entry.Strings.ReadSafe(PwDefs.TitleField),
                    path,
                    keyFile.Length == 0 ? null : ResolveRelative(keyFile),
                    entry.Strings.GetSafe(PwDefs.PasswordField).ReadChars());
            }
        }

        return null;
    }

    public void RememberAutoOpen(string title, string path, string? keyFile, ReadOnlySpan<char> password, string databaseId)
    {
        var group = database.RootGroup.FindCreateGroup(AutoOpenGroup, true);
        var entry = group.Entries.FirstOrDefault(e => string.Equals(e.Strings.ReadSafe(DatabaseIdField), databaseId, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            entry = new PwEntry(true, true);
            group.AddEntry(entry, true);
        }
        else
        {
            entry.CreateBackup(database);
        }

        var utf8 = new byte[Encoding.UTF8.GetByteCount(password)];
        Encoding.UTF8.GetBytes(password, utf8);
        try
        {
            entry.Strings.Set(PwDefs.TitleField, new ProtectedString(false, title));
            entry.Strings.Set(PwDefs.UrlField, new ProtectedString(false, path));
            entry.Strings.Set(PwDefs.UserNameField, new ProtectedString(false, keyFile ?? string.Empty));
            entry.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, utf8));
            entry.Strings.Set(DatabaseIdField, new ProtectedString(false, databaseId));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(utf8);
        }

        entry.Touch(true, false);
    }

    public void Dispose() => database.Close();

    private static bool ForDevice(PwEntry entry, string machineName)
    {
        var devices = entry.Strings.ReadSafe(IfDeviceField)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (devices.Any(d => d.StartsWith('!') && string.Equals(d[1..], machineName, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var allowed = devices.Where(d => !d.StartsWith('!')).ToList();
        return allowed.Count == 0 || allowed.Contains(machineName, StringComparer.OrdinalIgnoreCase);
    }

    private string ResolveRelative(string value)
    {
        var text = value.Trim();
        if (text.StartsWith("kdbx://", StringComparison.OrdinalIgnoreCase))
        {
            text = text["kdbx://".Length..];
        }
        else if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            text = uri.LocalPath;
        }

        var directory = System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
        text = text.Replace("{DB_DIR}", directory, StringComparison.OrdinalIgnoreCase);
        text = Environment.ExpandEnvironmentVariables(text);
        return text.Length == 0 || System.IO.Path.IsPathFullyQualified(text) ? text : System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, text));
    }

    private static string NormalizePath(string path) =>
        System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)));

    private static byte[] HashFile(string path) => SHA256.HashData(File.ReadAllBytes(path));

    private static string AttachmentName(string storedPath) => System.IO.Path.GetFileName(storedPath);

    private static bool SameLines(string a, string b) =>
        string.Equals(a.ReplaceLineEndings("\n"), b.ReplaceLineEndings("\n"), StringComparison.Ordinal);

    [GeneratedRegex(@"https://dev\.azure\.com/[^/\s]+/[^/\s]+/_git/LocalVault(?![\w.-])", RegexOptions.IgnoreCase)]
    private static partial Regex RetiredRepositoryUrl();

    private static bool IsGuide(PwEntry entry) => entry.Strings.ReadSafe(KindField) is OriginKind or InstallKind;

    private static PwEntry? FindEntry(PwGroup group, EntryName name) =>
        group.Entries.FirstOrDefault(entry => EntryName.Comparer.Equals(entry.Strings.ReadSafe(PwDefs.TitleField), name.FullName));

    private static void WriteDeclaration(PwEntry target, VaultEntry entry)
    {
        target.Strings.Set(PwDefs.NotesField, new ProtectedString(false, entry.RequiredNote ?? string.Empty));
        SetOrRemove(target, ShapeField, entry.RequiredShape);
        SetOrRemove(target, RequiredPathField, entry.RequiredPath);
        SetOrRemove(target, SshHostsField, entry.RequiredHosts.Count > 0 ? string.Join('\n', entry.RequiredHosts) : null);
    }

    private static void WriteSsh(PwEntry target, VaultEntry entry, bool writeHosts)
    {
        if (entry.Ssh is not { } ssh)
        {
            if (writeHosts)
            {
                RemoveSshStrings(target);
            }

            return;
        }

        target.Strings.Set(SshTypeField, new ProtectedString(false, ssh.Type));
        target.Strings.Set(SshPublicKeyField, new ProtectedString(false, ssh.PublicKey));
        SetOrRemove(target, SshAgentField, ssh.Agent ? "true" : null);
        SetOrRemove(target, ReplacedAtField, ssh.ReplacedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        if (writeHosts)
        {
            SetOrRemove(target, SshHostsField, ssh.Hosts.Count > 0 ? string.Join('\n', ssh.Hosts) : null);
        }

        var previous = entry.GetPrevious();
        if (previous is null)
        {
            return;
        }

        try
        {
            target.Binaries.Set(AttachmentName(entry.StoredPath!) + VaultEntry.OldKeySuffix, new ProtectedBinary(true, previous));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(previous);
        }
    }

    private static void RemoveSshStrings(PwEntry target)
    {
        foreach (var field in new[] { SshTypeField, SshPublicKeyField, SshHostsField, SshAgentField, ReplacedAtField })
        {
            target.Strings.Remove(field);
        }
    }

    private static string[] ReadHosts(PwEntry entry) =>
        entry.Strings.ReadSafe(SshHostsField).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SshKeyInfo.NormalizeHost).ToArray();

    private static KeePassItem? ReadSshKey(PwEntry entry, EntryName name, string storedPath, byte[] value, bool withHosts)
    {
        var type = entry.Strings.ReadSafe(SshTypeField);
        if (type.Length == 0)
        {
            return null;
        }

        _ = SshKeyPath.Validate(storedPath);
        var replacedAt = entry.Strings.ReadSafe(ReplacedAtField);
        var ssh = new SshKeyInfo(
            type,
            SshKeyInfo.NormalizePublicKey(entry.Strings.ReadSafe(SshPublicKeyField)),
            withHosts ? ReadHosts(entry) : [],
            entry.Strings.ReadSafe(SshAgentField) == "true",
            replacedAt.Length == 0 ? null : DateTimeOffset.Parse(replacedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
        var previous = entry.Binaries.Get(AttachmentName(storedPath) + VaultEntry.OldKeySuffix)?.ReadData();
        return new KeePassItem(name, StoredPathRules.Normalize(storedPath), value, previous, ssh, entry.Strings.ReadSafe(PersonalField) == "true");
    }

    private static void SetOrRemove(PwEntry target, string field, string? value)
    {
        if (value is null)
        {
            target.Strings.Remove(field);
        }
        else
        {
            target.Strings.Set(field, new ProtectedString(false, value));
        }
    }

    private void WriteCredential(PwEntry target, VaultEntry entry, byte[] password)
    {
        target.Strings.Set(PwDefs.UserNameField, new ProtectedString(database.MemoryProtection.ProtectUserName, entry.UserName ?? string.Empty));
        target.Strings.Set(PwDefs.PasswordField, new ProtectedString(true, password));
        var urls = entry.Urls;
        target.Strings.Set(PwDefs.UrlField, new ProtectedString(database.MemoryProtection.ProtectUrl, urls.Count > 0 ? urls[0] : string.Empty));
        for (var i = 1; i < urls.Count; i++)
        {
            target.Strings.Set(string.Create(CultureInfo.InvariantCulture, $"{ExtraUrlPrefix}_{i}"), new ProtectedString(false, urls[i]));
        }

        if (entry.Totp is { } totp)
        {
            target.Strings.Set(TotpSecretBase32Field, new ProtectedString(true, totp.Secret));
            if (totp.Digits != TotpSecret.DefaultDigits)
            {
                target.Strings.Set(TotpLengthField, new ProtectedString(false, totp.Digits.ToString(CultureInfo.InvariantCulture)));
            }

            if (totp.Period != TotpSecret.DefaultPeriod)
            {
                target.Strings.Set(TotpPeriodField, new ProtectedString(false, totp.Period.ToString(CultureInfo.InvariantCulture)));
            }

            if (totp.Algorithm != TotpSecret.DefaultAlgorithm)
            {
                target.Strings.Set(TotpAlgorithmField, new ProtectedString(false, KeePassAlgorithmName(totp.Algorithm)));
            }

            target.Strings.Set(XcOtpField, new ProtectedString(true, totp.ToUri(entry.Name.FullName)));
        }
    }

    private static void RemoveCredentialStrings(PwEntry target)
    {
        foreach (var key in target.Strings.GetKeys().Where(IsCredentialString))
        {
            target.Strings.Remove(key);
        }
    }

    private static bool IsCredentialString(string key) =>
        key is PwDefs.UserNameField or PwDefs.UrlField or XcOtpField
        || key.StartsWith(ExtraUrlPrefix, StringComparison.Ordinal)
        || key.StartsWith(TotpFieldPrefix, StringComparison.Ordinal);

    private static KeePassItem ReadCredential(PwEntry entry, EntryName name)
    {
        var urls = new List<string> { entry.Strings.ReadSafe(PwDefs.UrlField) };
        urls.AddRange(entry.Strings.GetKeys()
            .Select(key => (Key: key, Number: ExtraUrlNumber(key)))
            .Where(item => item.Number is not null)
            .OrderBy(item => item.Number)
            .Select(item => entry.Strings.ReadSafe(item.Key)));
        var userName = entry.Strings.ReadSafe(PwDefs.UserNameField);
        return new KeePassItem(
            name,
            userName.Length == 0 ? null : userName,
            entry.Strings.GetSafe(PwDefs.PasswordField).ReadUtf8(),
            ReadTotp(entry),
            urls.Where(url => !string.IsNullOrWhiteSpace(url)).Select(url => url.Trim()).ToList());
    }

    private static int? ExtraUrlNumber(string key)
    {
        if (key == ExtraUrlPrefix)
        {
            return 0;
        }

        return key.StartsWith(ExtraUrlPrefix + "_", StringComparison.Ordinal)
            && int.TryParse(key[(ExtraUrlPrefix.Length + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static TotpSecret? ReadTotp(PwEntry entry)
    {
        var digits = ReadNumber(entry, TotpLengthField, TotpSecret.DefaultDigits);
        var period = ReadNumber(entry, TotpPeriodField, TotpSecret.DefaultPeriod);
        var algorithm = entry.Strings.ReadSafe(TotpAlgorithmField) is { Length: > 0 } name ? name : TotpSecret.DefaultAlgorithm;
        if (entry.Strings.ReadSafe(TotpSecretBase32Field) is { Length: > 0 } base32)
        {
            return TotpSecret.Create(base32, digits, period, algorithm);
        }

        if (entry.Strings.ReadSafe(TotpSecretHexField) is { Length: > 0 } hex)
        {
            return TotpSecret.FromKey(Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal)), digits, period, algorithm);
        }

        if (entry.Strings.ReadSafe(TotpSecretBase64Field) is { Length: > 0 } base64)
        {
            return TotpSecret.FromKey(Convert.FromBase64String(base64), digits, period, algorithm);
        }

        if (entry.Strings.ReadSafe(TotpSecretField) is { Length: > 0 } raw)
        {
            return TotpSecret.FromKey(Encoding.UTF8.GetBytes(raw), digits, period, algorithm);
        }

        return entry.Strings.ReadSafe(XcOtpField) is { Length: > 0 } otp ? TotpSecret.Parse(otp) : null;
    }

    private static int ReadNumber(PwEntry entry, string field, int fallback) =>
        int.TryParse(entry.Strings.ReadSafe(field), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : fallback;

    private static string KeePassAlgorithmName(string algorithm) => "HMAC-" + algorithm[..3] + "-" + algorithm[3..];

    private static bool LooksLikeCredential(PwEntry entry) =>
        entry.Strings.ReadSafe(PwDefs.UserNameField).Length > 0
        || entry.Strings.ReadSafe(PwDefs.UrlField).Length > 0
        || entry.Strings.GetKeys().Any(key => key == XcOtpField || key.StartsWith(TotpFieldPrefix, StringComparison.Ordinal));

    private static KeePassItem? ReadItem(PwEntry entry, EntryName name)
    {
        var kind = entry.Strings.ReadSafe(KindField);
        if (kind == VaultEntry.CredentialKind || (kind.Length == 0 && LooksLikeCredential(entry)))
        {
            return ReadCredential(entry, name);
        }

        if (kind == VaultEntry.RequiredKind)
        {
            var shape = entry.Strings.ReadSafe(ShapeField);
            var requiredPath = entry.Strings.ReadSafe(RequiredPathField);
            return new KeePassItem(
                name,
                entry.Strings.ReadSafe(PwDefs.NotesField),
                shape.Length == 0 ? null : shape,
                requiredPath.Length == 0 ? null : StoredPathRules.Normalize(requiredPath),
                ReadHosts(entry),
                ReadPersonalValue(entry, name));
        }

        if (kind == VaultEntry.FileKind)
        {
            var storedPath = entry.Strings.ReadSafe(PathField);
            var attachment = entry.Binaries.Get(AttachmentName(storedPath));
            if (string.IsNullOrEmpty(storedPath) || attachment is null)
            {
                return null;
            }

            var content = attachment.ReadData();
            return ReadSshKey(entry, name, storedPath, content, withHosts: true)
                ?? new KeePassItem(name, VaultEntry.FileKind, storedPath, content);
        }

        return new KeePassItem(name, VaultEntry.SecretKind, null, entry.Strings.GetSafe(PwDefs.PasswordField).ReadUtf8());
    }

    private static KeePassItem? ReadPersonalValue(PwEntry entry, EntryName name)
    {
        var storedPath = entry.Strings.ReadSafe(PathField);
        if (storedPath.Length > 0 && entry.Binaries.Get(AttachmentName(storedPath)) is { } attachment)
        {
            var content = attachment.ReadData();
            return ReadSshKey(entry, name, storedPath, content, withHosts: false)
                ?? new KeePassItem(name, VaultEntry.FileKind, StoredPathRules.Normalize(storedPath), content);
        }

        if (LooksLikeCredential(entry))
        {
            return ReadCredential(entry, name);
        }

        return entry.Strings.ReadSafe(PwDefs.PasswordField).Length > 0
            ? new KeePassItem(name, VaultEntry.SecretKind, null, entry.Strings.GetSafe(PwDefs.PasswordField).ReadUtf8())
            : null;
    }

    private PwGroup? FindGroup(IReadOnlyList<string> groupPath, bool create)
    {
        var group = database.RootGroup;
        foreach (var segment in groupPath)
        {
            group = group.FindCreateGroup(segment, create);
            if (group is null)
            {
                return null;
            }
        }

        return group;
    }

    private PwGroup RecycleBin()
    {
        if (database.RecycleBinUuid is { } uuid && !uuid.Equals(PwUuid.Zero) && database.RootGroup.FindGroup(uuid, true) is { } bin)
        {
            return bin;
        }

        var created = new PwGroup(true, true, "Recycle Bin", PwIcon.TrashBin)
        {
            EnableAutoType = false,
            EnableSearching = false,
            IsExpanded = false,
        };
        database.RootGroup.AddGroup(created, true);
        database.RecycleBinUuid = created.Uuid;
        database.RecycleBinChanged = DateTime.UtcNow;
        return created;
    }
}
