using System.Management.Automation;
using System.Security;
using LocalVault.Core;

namespace LocalVault.PowerShell;

public sealed record VaultEntryInfo(string Name, string? Scope, string Key, string Kind, string Sync, DateTimeOffset? Created, TimeSpan? Age, string? Note = null, string? User = null);

public enum VaultEntrySharing
{
    Shared,
    Local,
}

[Cmdlet(VerbsCommon.Get, "VaultEntryList")]
[OutputType(typeof(VaultEntryInfo))]
public sealed class GetVaultEntryListCommand : VaultCmdletBase
{
    public const int ExportWarningDays = 30;

    [Parameter(Position = 0)]
    [ValidateNotNullOrEmpty]
    public string? Scope { get; set; }

    protected override void Run()
    {
        AdoptNewerSshKeys([Location]);
        var snapshot = ReadVault();
        var now = Now;
        var states = KeePassSync.ComparePending(snapshot.Body, PrimaryDatabaseId());
        var sync = states.Where(s => s.VaultHash is not null || s.State == KeePassSyncState.Local)
            .ToDictionary(s => s.Name.FullName, s => s.State.ToString(), EntryName.Comparer);
        foreach (var entry in snapshot.Body.Entries.Where(e => e.IsRequired))
        {
            sync[entry.Name.FullName] = entry.IsMissing ? "Missing" : "Personal";
        }
        foreach (var entry in snapshot.Body.Entries
                     .Where(e => Scope is null || string.Equals(e.Name.Scope, Scope, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(e => e.Name.FullName, StringComparer.OrdinalIgnoreCase))
        {
            WriteObject(new VaultEntryInfo(
                entry.Name.FullName, entry.Name.Scope, entry.Name.Key, entry.Kind,
                sync.TryGetValue(entry.Name.FullName, out var state) ? state : string.Empty,
                entry.Created, now - entry.Created, entry.RequiredNote, entry.UserName));
        }

        WarnAboutStore(states);
        var missing = snapshot.Body.Entries.Where(e => e.IsMissing).Select(e => e.Name.FullName).ToList();
        if (missing.Count > 0)
        {
            WriteWarning($"Required values not set on this PC: {string.Join(", ", missing)}. Run lv set <name> for each (lv start shows where to get them).");
        }
        WarnAboutUnexported(snapshot.Body, now);
    }

    private void WarnAboutStore(IReadOnlyList<KeePassComparison> states)
    {
        var pending = states.Count(s => s.State is KeePassSyncState.New or KeePassSyncState.Changed or KeePassSyncState.RemovalPending);
        if (pending == 0 || !StoreKnown())
        {
            return;
        }

        WriteWarning($"{pending} change(s) to shared entries are not in the store yet. Run lv save (Export-VaultToStore).");
    }

    private string? PrimaryDatabaseId()
    {
        try
        {
            return StoreResolver.For(Location, null, new StoreRegistry(StoreRegistry.DefaultPath)).DatabaseId;
        }
        catch (VaultException)
        {
            return null;
        }
    }

    private static string? PersonalStoreId()
    {
        try
        {
            return new StoreRegistry(StoreRegistry.DefaultPath).Personal?.Id;
        }
        catch (VaultException)
        {
            return null;
        }
    }

    private bool StoreKnown()
    {
        try
        {
            return StoreResolver.For(Location, null, new StoreRegistry(StoreRegistry.DefaultPath)) is { } resolution
                && (resolution.Definition is not null || resolution.Reference is not null);
        }
        catch (VaultException)
        {
            return false;
        }
    }

    private void WarnAboutUnexported(VaultBody body, DateTimeOffset now)
    {
        if (body.LastExported is { } exported && now - exported <= TimeSpan.FromDays(ExportWarningDays))
        {
            return;
        }

        var storeKnown = StoreKnown();
        var personalKey = PersonalStoreId() is { } id ? KeePassSync.PersonalKey(id) : null;
        var onlyHere = body.Entries
            .Where(e => !e.IsMissing && (!storeKnown || e.IsLocal || (e.IsRequired && (personalKey is null || e.KeePassBaseFor(personalKey) is null))))
            .Select(e => e.Name.FullName)
            .ToList();
        if (onlyHere.Count == 0)
        {
            return;
        }

        var since = body.LastExported is { } last ? $"last export {last:yyyy-MM-dd}" : "never exported";
        WriteWarning($"Only on this PC, {since}: {string.Join(", ", onlyHere)}. Keep a copy with lv export (Export-Vault).");
    }
}

[Cmdlet(VerbsCommon.Get, "VaultEntry", DefaultParameterSetName = ValueSet)]
[OutputType(typeof(SecureString), typeof(string), typeof(byte[]), typeof(PSCredential))]
public sealed class GetVaultEntryCommand : VaultCmdletBase
{
    private const string ValueSet = "Value";
    private const string CredentialSet = "Credential";

    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    public string Name { get; set; } = string.Empty;

    [Parameter(ParameterSetName = ValueSet)]
    public SwitchParameter AsPlainText { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = CredentialSet)]
    public SwitchParameter AsCredential { get; set; }

    protected override void Run()
    {
        var reference = EntryReference.Parse(Name);
        var entry = FindEntry(ReadForUse(body => body.Find(reference.Name) is not null, _ => [reference.Name]), reference.Name);
        RequireValue(entry);
        if (AsCredential)
        {
            if (Name.Contains(EntryReference.PartSeparator, StringComparison.Ordinal))
            {
                throw new VaultException("-AsCredential takes the entry name alone, without a #part.");
            }

            WriteObject(CredentialOf(entry));
            return;
        }

        if (entry.IsFile && reference.Part == EntryPart.Password)
        {
            WriteObject(entry.GetValue(), enumerateCollection: false);
            return;
        }

        WriteObject(PartOutput(entry, reference, AsPlainText));
    }

    private static PSCredential CredentialOf(VaultEntry entry)
    {
        if (entry.UserName is not { } user)
        {
            throw new VaultException($"'{entry.Name}' has no user name; -AsCredential needs one. Set it with lv set {entry.Name}#user.");
        }

        var password = entry.GetValue();
        try
        {
            return new PSCredential(user, SecretText.FromUtf8(password));
        }
        finally
        {
            Array.Clear(password);
        }
    }
}

[Cmdlet(VerbsCommon.Copy, "VaultEntry")]
public sealed class CopyVaultEntryCommand : VaultCmdletBase
{
    public const int DefaultSeconds = 20;

    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    public string Name { get; set; } = string.Empty;

    [Parameter]
    [ValidateRange(1, 3600)]
    public int Seconds { get; set; } = DefaultSeconds;

    protected override void Run()
    {
        var reference = EntryReference.Parse(Name);
        var entry = FindEntry(ReadForUse(body => body.Find(reference.Name) is not null, _ => [reference.Name]), reference.Name);
        RequireValue(entry);
        if (!entry.IsSecret && !entry.IsCredential)
        {
            throw new VaultException($"'{entry.Name}' is a {entry.Kind} entry, not a secret.");
        }

        var bytes = entry.ReadPart(reference, Now);
        var chars = System.Text.Encoding.UTF8.GetChars(bytes);
        try
        {
            SecureClipboard.SetText(chars, TimeSpan.FromSeconds(Seconds));
        }
        finally
        {
            Array.Clear(bytes);
            Array.Clear(chars);
        }

        Say($"{new EntryReference(entry.Name, reference.Part, reference.UrlNumber)} is on the clipboard for {Seconds} s, kept out of the clipboard history.");
    }
}
[Cmdlet(VerbsCommon.Set, "VaultEntry", DefaultParameterSetName = SecretSet, SupportsShouldProcess = true)]
public sealed class SetVaultEntryCommand : VaultCmdletBase
{
    private const string SecretSet = "Secret";
    private const string FileSet = "File";
    private const string SharingSet = "Sharing";
    private const string RequiredSet = "Required";
    private const string CredentialSet = "Credential";

    [Parameter(Mandatory = true, Position = 0)]
    public string Name { get; set; } = string.Empty;

    [Parameter(Mandatory = true, ParameterSetName = CredentialSet)]
    [Parameter(ParameterSetName = RequiredSet)]
    public SwitchParameter Credential { get; set; }

    [Parameter(ParameterSetName = CredentialSet)]
    [AllowEmptyString]
    public string? UserName { get; set; }

    [Parameter(ParameterSetName = CredentialSet)]
    public SecureString? Password { get; set; }

    [Parameter(ParameterSetName = CredentialSet)]
    public SecureString? Totp { get; set; }

    [Parameter(ParameterSetName = CredentialSet)]
    public string[]? Url { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = SharingSet)]
    public VaultEntrySharing Sharing { get; set; }

    [Parameter(ParameterSetName = SecretSet)]
    [Parameter(ParameterSetName = FileSet)]
    [Parameter(ParameterSetName = CredentialSet)]
    public SwitchParameter Local { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = RequiredSet)]
    public SwitchParameter Required { get; set; }

    [Parameter(ParameterSetName = RequiredSet)]
    [ValidateNotNullOrEmpty]
    public string? Note { get; set; }

    [Parameter(ParameterSetName = SecretSet, Position = 1, ValueFromPipeline = true)]
    public SecureString? Value { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = FileSet)]
    [ValidateNotNullOrEmpty]
    public string FilePath { get; set; } = string.Empty;

    [Parameter(ParameterSetName = FileSet)]
    [Parameter(ParameterSetName = RequiredSet)]
    [ValidateNotNullOrEmpty]
    public string? StoredPath { get; set; }

    protected override void Run()
    {
        if (Name.Contains(EntryReference.PartSeparator, StringComparison.Ordinal))
        {
            var reference = EntryReference.Parse(Name);
            RefuseSshKey(reference.Name);
            SetPart(reference);
            return;
        }

        var name = EntryName.Parse(Name);
        if (ParameterSetName != SharingSet)
        {
            RefuseSshKey(name);
        }

        if (ParameterSetName == RequiredSet)
        {
            if (ShouldProcess($"{name} in {Location.Path}", "Declare required"))
            {
                if (Credential && StoredPath is not null)
                {
                    throw new VaultException("A required entry is a credential or a file, not both: give -Credential or -StoredPath.");
                }

                var shape = Credential ? VaultEntry.CredentialShape : StoredPath is not null ? VaultEntry.FileShape : null;
                Report(VaultFile.Update(body => body.Set(VaultEntry.NewRequired(name, Note, Now, shape, StoredPath)), WriteOptions()));
                var what = Credential ? ", a user name and a password" : StoredPath is not null ? $", a file restored to {StoredPathRules.Normalize(StoredPath)}" : string.Empty;
                var how = StoredPath is not null ? $"lv set {name} --file <path>" : $"lv set {name}";
                Say($"{name} is required and personal{what}: each person sets their own value with {how}; lv save shares the declaration, never the value.");
            }

            return;
        }

        if (ParameterSetName == SharingSet)
        {
            var local = Sharing == VaultEntrySharing.Local;
            if (ShouldProcess($"{name} in {Location.Path}", local ? "Make local" : "Make shared"))
            {
                Report(VaultFile.Update(body => body.SetSharing(name, local)));
                Say(local ? $"{name} is local: lv save leaves it out." : $"{name} is shared: lv save sends it to the store.");
            }

            return;
        }

        if (ParameterSetName == SecretSet && Value is null && ReadVault().Body.Find(name) is { IsFileShape: true } fileShaped)
        {
            throw new VaultException(
                $"'{name}' is declared as a file{(fileShaped.RequiredPath is { } path ? $", restored to {path}" : string.Empty)}. Give it with lv set {name} --file <path> (Set-VaultEntry -FilePath).");
        }

        var credentialShaped = ParameterSetName == SecretSet && Value is null && ReadVault().Body.Find(name) is { IsCredentialShape: true };
        var entry = ParameterSetName switch
        {
            FileSet => ReadFile(name),
            CredentialSet => ReadCredential(name),
            _ when credentialShaped => ReadCredential(name),
            _ => ReadSecretEntry(name),
        };
        if (!ShouldProcess($"{name} in {Location.Path}", "Set"))
        {
            return;
        }

        var now = Now;
        Report(VaultFile.Update(
            body =>
            {
                if (entry.IsSecret && body.Find(name) is { IsCredential: true } credential)
                {
                    var password = entry.GetValue();
                    try
                    {
                        entry = credential.WithPart(new EntryReference(name), null, password, now);
                    }
                    finally
                    {
                        Array.Clear(password);
                    }
                }

                body.Set(entry);
                if (Local)
                {
                    body.SetSharing(name, local: true);
                }
            },
            WriteOptions()));
        Say($"Stored {name}");
    }

    private void SetPart(EntryReference reference)
    {
        if (ParameterSetName != SecretSet)
        {
            throw new VaultException($"{reference} is one part of a credential: give its value alone, or use -Credential on {reference.Name}.");
        }

        var prompt = $"{Label(reference)} for {reference.Name}";
        var secret = Value ?? (reference.IsSecret ? ReadSecret(prompt) : null);
        var text = secret is null ? ReadText(prompt) : SecretText.ToPlain(secret);
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        try
        {
            if (reference.Part == EntryPart.Password && bytes.Length == 0)
            {
                throw new VaultException("Empty value. Nothing was changed.");
            }

            if (!ShouldProcess($"{reference} in {Location.Path}", "Set"))
            {
                return;
            }

            var now = Now;
            Report(VaultFile.Update(
                body =>
                {
                    var existing = body.Find(reference.Name) ?? VaultEntry.NewCredential(reference.Name, null, [], null, [], now);
                    body.Set(existing.WithPart(reference, text, bytes, now));
                    if (Local)
                    {
                        body.SetSharing(reference.Name, local: true);
                    }
                },
                WriteOptions()));
        }
        finally
        {
            Array.Clear(bytes);
        }

        Say(text.Length == 0 ? $"Removed {reference}" : $"Stored {reference}");
    }

    private void RefuseSshKey(EntryName name)
    {
        if (VaultFile.Read().Body.Find(name) is { IsSshKey: true } key)
        {
            throw new VaultException(key.IsMissing
                ? $"'{name}' is declared as an SSH key. Create yours with lv ssh-key new {name} (New-VaultSshKey). Nothing was changed."
                : $"'{name}' is an SSH key. Replace it with lv ssh-key new {name} --replace (New-VaultSshKey -Replace). Nothing was changed.");
        }
    }

    private static string Label(EntryReference reference) => reference.Part switch
    {
        EntryPart.User => "User name",
        EntryPart.Totp => "TOTP (otpauth:// address or base32 seed; empty removes it)",
        EntryPart.Url => "URL (empty removes it)",
        _ => "Password",
    };

    private VaultEntry ReadCredential(EntryName name)
    {
        var existing = ReadVault().Body.Find(name);
        if (existing is { IsFile: true })
        {
            throw new VaultException($"'{name}' is a file entry; it can't become a credential. Remove it first.");
        }

        var current = existing is null || existing.IsMissing ? [] : existing.GetValue();
        try
        {
            var ask = UserName is null && Password is null && Totp is null && Url is null;
            var user = UserName ?? existing?.UserName;
            if (ask)
            {
                var keeps = user is null ? string.Empty : $" (empty keeps {user})";
                var typed = ReadText($"User name for {name}{keeps}");
                user = typed.Length == 0 ? user : typed;
            }

            var passwordNote = current.Length > 0 ? " (empty keeps the current one)" : string.Empty;
            var password = Password ?? (ask ? ReadSecret($"Password for {name}{passwordNote}") : null);
            var passwordBytes = password is null ? [] : SecretText.ToUtf8(password);
            try
            {
                var totp = existing?.Totp;
                var totpNote = totp is null ? "empty for none" : "empty keeps the current one";
                var totpText = Totp ?? (ask ? ReadSecret($"TOTP for {name}: otpauth:// address or base32 seed ({totpNote})") : null);
                var totpPlain = totpText is null ? string.Empty : SecretText.ToPlain(totpText);
                if (totpPlain.Length > 0)
                {
                    totp = TotpSecret.Parse(totpPlain);
                }

                var urls = Url?.ToList() ?? existing?.Urls.ToList() ?? [];
                if (ask)
                {
                    var typed = new List<string>();
                    var keepNote = urls.Count > 0 ? ", keeping the current ones" : string.Empty;
                    string line;
                    while ((line = ReadText($"URL {typed.Count + 1} for {name} (empty to finish{(typed.Count == 0 ? keepNote : string.Empty)})")).Length > 0)
                    {
                        typed.Add(line);
                    }

                    if (typed.Count > 0)
                    {
                        urls = typed;
                    }
                }

                return VaultEntry.NewCredential(name, user, passwordBytes.Length > 0 ? passwordBytes : current, totp, urls, Now);
            }
            finally
            {
                Array.Clear(passwordBytes);
            }
        }
        finally
        {
            Array.Clear(current);
        }
    }

    private VaultEntry ReadSecretEntry(EntryName name)
    {
        var value = Value ?? ReadSecret($"Value for {name}");
        var bytes = SecretText.ToUtf8(value);
        try
        {
            if (bytes.Length == 0)
            {
                throw new VaultException("Empty value. Nothing was changed.");
            }

            return VaultEntry.NewSecret(name, bytes, Now);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    private VaultEntry ReadFile(EntryName name)
    {
        var full = GetUnresolvedProviderPathFromPSPath(FilePath);
        var declared = ReadVault().Body.Find(name) is { IsRequired: true } required ? required : null;
        var stored = StoredPath
            ?? declared?.RequiredPath
            ?? (declared is not null ? StoredPathRules.HomeFormOf(full) : null)
            ?? StoredPathFor(full);
        var content = File.ReadAllBytes(full);
        try
        {
            return VaultEntry.NewFile(name, content, stored, Now);
        }
        finally
        {
            Array.Clear(content);
        }
    }

    private string StoredPathFor(string full)
    {
        try
        {
            return StoredPathRules.FromFullPath(Location.Directory, full);
        }
        catch (VaultException ex)
        {
            throw new VaultException(
                $"'{full}' is outside the vault folder '{Location.Directory}'. Give -StoredPath.", ex);
        }
    }
}

[Cmdlet(VerbsCommon.Remove, "VaultEntry", SupportsShouldProcess = true)]
public sealed class RemoveVaultEntryCommand : VaultCmdletBase
{
    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    public string Name { get; set; } = string.Empty;

    protected override void Run()
    {
        var name = EntryName.Parse(Name);
        var existing = ReadVault().Body.Find(name);
        if (existing is null)
        {
            WriteWarning($"No entry '{name}' in {Location.Path}.");
            return;
        }

        if (!ShouldProcess($"{name} in {Location.Path}", "Remove"))
        {
            return;
        }

        Report(VaultFile.Update(body => body.Remove(name)));
        Say(!existing.IsLocal && existing.KeePassBase is not null
            ? $"Removed {name}. lv save also removes it from the store."
            : $"Removed {name}");
    }
}

[Cmdlet(VerbsDiagnostic.Resolve, "VaultEntry")]
[OutputType(typeof(SecureString), typeof(string))]
public sealed class ResolveVaultEntryCommand : VaultCmdletBase
{
    public const string DefaultFallback = "common";

    [Parameter(Mandatory = true, Position = 0)]
    public string Name { get; set; } = string.Empty;

    [Parameter(Mandatory = true, Position = 1)]
    public string Scope { get; set; } = string.Empty;

    [Parameter]
    public string Fallback { get; set; } = DefaultFallback;

    [Parameter]
    public SwitchParameter AsPlainText { get; set; }

    protected override void Run()
    {
        var reference = EntryReference.Parse(Name);
        var entry = Resolve(ReadForUse(body => TryResolve(body, Name, Scope, Fallback)), Name, Scope, Fallback);
        WriteObject(PartOutput(entry, new EntryReference(entry.Name, reference.Part, reference.UrlNumber), AsPlainText));
    }

    internal static bool TryResolve(VaultBody body, string key, string scope, string fallback)
    {
        try
        {
            Resolve(body, key, scope, fallback);
            return true;
        }
        catch (VaultException)
        {
            return false;
        }
    }

    internal static VaultEntry Resolve(VaultBody body, string key, string scope, string fallback)
    {
        var name = EntryReference.Parse(key).Name;
        if (name.IsScoped)
        {
            throw new VaultException($"'{key}' already has a scope. Give the key alone.");
        }

        var scoped = EntryName.Combine(scope, name.Key);
        var shared = EntryName.Combine(fallback, name.Key);
        return body.Find(scoped) ?? body.Find(shared)
            ?? throw new VaultException($"Neither '{scoped}' nor '{shared}' is in the vault.");
    }
}
