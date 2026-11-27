using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Security;
using LocalVault.Core;
using LocalVault.Engine;

namespace LocalVault.PowerShell;

public abstract partial class VaultCmdletBase : PSCmdlet, IVaultHost
{
    private VaultLocation? location;
    private VaultFile? store;

    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Path { get; set; }

    protected VaultLocation Location => location ??= VaultLocation.Resolve(
        Path,
        Environment.GetEnvironmentVariable(VaultLocation.EnvironmentVariable),
        SessionState.Path.CurrentFileSystemLocation.ProviderPath);

    internal VaultLocation VaultLocation => Location;

    protected VaultFile VaultFile => store ??= new(Location, new ClixmlLegacyReader());

    protected static bool IsInteractive =>
        Environment.UserInteractive
        && !Console.IsInputRedirected
        && !Environment.GetCommandLineArgs().Any(
            argument => argument.StartsWith("-noni", StringComparison.OrdinalIgnoreCase));

    protected static DateTimeOffset Now => DateTimeOffset.UtcNow;

    private static readonly HashSet<string> SettledBackupCleanups = new(StringComparer.OrdinalIgnoreCase);

    protected virtual bool OffersBackupCleanup => true;

    protected override void ProcessRecord()
    {
        try
        {
            OfferBackupCleanup();
            Run();
        }
        catch (ImportConflictException ex)
        {
            ThrowTerminatingError(new ErrorRecord(ex, "LocalVault.ImportConflict", ErrorCategory.ResourceExists, null));
        }
        catch (VaultException ex)
        {
            ThrowTerminatingError(new ErrorRecord(ex, "LocalVault", ErrorCategory.InvalidOperation, null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ThrowTerminatingError(new ErrorRecord(ex, "LocalVault.File", ErrorCategory.WriteError, null));
        }
    }

    protected abstract void Run();

    protected override void EndProcessing()
    {
        hello?.Dispose();
        hello = null;
        base.EndProcessing();
    }

    protected override void StopProcessing()
    {
        hello?.Dispose();
        hello = null;
        base.StopProcessing();
    }

    protected VaultSnapshot ReadVault(bool ignoreLegacy = false)
    {
        var snapshot = VaultFile.Read(ignoreLegacy);
        if (snapshot.Warning is not null)
        {
            WriteWarning(snapshot.Warning);
        }

        return snapshot;
    }

    protected VaultUpdateOptions WriteOptions(string? expectedFingerprint = null, bool ignoreLegacy = false)
    {
        var allowCreate = false;
        if (!File.Exists(Location.Path) && (ignoreLegacy || Location.LegacyPath is null || !File.Exists(Location.LegacyPath)))
        {
            var confirmed = Location.Source != VaultPathSource.GitRoot
                || (IsInteractive && Confirm($"There is no vault yet. Create '{Location.Path}'?"));
            Location.EnsureCanCreate(confirmed);
            OfferGitIgnore();
            allowCreate = true;
        }

        return new VaultUpdateOptions
        {
            AllowCreate = allowCreate,
            ExpectedFingerprint = expectedFingerprint,
            IgnoreLegacy = ignoreLegacy,
        };
    }

    private void OfferGitIgnore()
    {
        if (Location.GitRoot is not { } gitRoot || !IsInteractive || GitIgnore.IsIgnored(Location.Path))
        {
            return;
        }

        var line = GitIgnoreFile.LineFor(Location);
        if (Confirm($"git doesn't ignore '{Location.Path}'. Add '{line}' to {System.IO.Path.Combine(gitRoot, GitIgnoreFile.FileName)}?", defaultYes: true))
        {
            GitIgnoreFile.Append(gitRoot, [line]);
            Say("Commit .gitignore.");
        }
    }

    bool IVaultHost.CanPrompt => IsInteractive || Console.IsInputRedirected;

    bool IVaultHost.CanConfirm => IsInteractive;

    DateTimeOffset IVaultHost.Clock => Now;

    void IVaultHost.Tell(string message) => Say(message);

    void IVaultHost.Warn(string message) => WriteWarning(message);

    SecureString IVaultHost.AskSecret(string prompt) => ReadSecret(prompt);

    bool IVaultHost.AskYesNo(string question) => Confirm(question);

    void IVaultHost.ReportWrite(VaultWriteResult result) => Report(result);

    HelloEntries IVaultHost.Hello => HelloEntries;

    private HelloEntries? hello;

    internal HelloEntries HelloEntries => hello ??= new HelloEntries(HelloKey) { BeforePrompt = RequireHelloConsole, Warn = WriteWarning };

    private bool autoLoaded;
    private bool askedMissing;

    protected VaultBody ReadForUse(Func<VaultBody, bool> satisfied, Func<VaultBody, IEnumerable<EntryName>>? wanted = null)
    {
        ArgumentNullException.ThrowIfNull(satisfied);
        var body = ReadVault().Body;
        if (!satisfied(body) && !autoLoaded && IsInteractive && HasStore())
        {
            autoLoaded = true;
            Say("Not on this PC yet: loading this repository's vaults from their stores.");
            using var access = new StoreAccess(this, null);
            VaultSync.Load(this, access, RepositoryVaults.Of(Location, ExplicitVault), (_, _) => false, (_, _) => false);
            body = ReadVault().Body;
        }

        if (wanted is null || askedMissing || !IsInteractive)
        {
            return body;
        }

        var missing = wanted(body).Where(name => body.Find(name) is { IsMissing: true }).ToHashSet();
        if (missing.Count == 0)
        {
            return body;
        }

        askedMissing = true;
        using (var access = new StoreAccess(this, null))
        {
            FillMissingValues(access, [Location], missing);
        }

        return ReadVault().Body;
    }

    protected string? OriginUrl => Location.ReadOrigin()?.Url;

    internal void WriteKeyFile(VaultEntry entry, bool overwrite) => WriteKeyFile(Location, entry, overwrite);

    internal void AdoptNewerSshKeys(IReadOnlyList<VaultLocation> vaults)
    {
        ArgumentNullException.ThrowIfNull(vaults);
        foreach (var location in vaults.Where(location => File.Exists(location.Path)))
        {
            try
            {
                var store = RepositoryVaults.StoreFor(location);
                var origin = location.ReadOrigin()?.Url;
                var newer = SshKeyAdoption.FindNewer(store.Read().Body, origin, Now);
                if (newer.Count == 0)
                {
                    continue;
                }

                Report(store.Update(body =>
                {
                    foreach (var key in newer)
                    {
                        body.Set(key.Entry);
                    }
                }));
                foreach (var key in newer)
                {
                    SshKeyAdoption.AddUser(key.Entry, origin, location.Path);
                    Say($"{key.Entry.Name}: key adopted from {key.WrittenBy}.");
                }
            }
            catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
            {
                WriteWarning($"SSH keys of {location.Path} not checked against the other clones: {ex.Message}");
            }
        }
    }

    internal void WritePersonalFiles(IReadOnlyList<VaultLocation> vaults)
    {
        ArgumentNullException.ThrowIfNull(vaults);
        foreach (var location in vaults)
        {
            var entries = RepositoryVaults.StoreFor(location).Read().Body.Entries;
            foreach (var key in entries.Where(e => e.Ssh is not null && !e.IsMissing))
            {
                WriteKeyFile(location, key, overwrite: false);
            }

            foreach (var entry in entries.Where(e => e.IsRequired && e.IsFile && e.IsUnderHome && e.Ssh is null && !e.IsHello))
            {
                var target = StoredPathRules.Resolve(location.Directory, entry.StoredPath!);
                var content = entry.GetValue();
                try
                {
                    if (!File.Exists(target))
                    {
                        PrivateFile.Write(target, content);
                        Say($"Wrote your {entry.Name} to {target}, readable by your account only.");
                    }
                    else if (!File.ReadAllBytes(target).AsSpan().SequenceEqual(content))
                    {
                        WriteWarning($"Kept the existing '{target}'; it differs from your {entry.Name}. Run lv restore {entry.Name} --overwrite to replace it.");
                    }
                }
                finally
                {
                    Array.Clear(content);
                }
            }
        }
    }

    internal void FillMissingValues(StoreAccess access, IReadOnlyList<VaultLocation> vaults, IReadOnlySet<EntryName>? only = null)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(vaults);
        AdoptMissingSshKeys(vaults, only);
        var missing = vaults
            .Select(location => (Location: location, Entries: RepositoryVaults.StoreFor(location).Read().Body.Entries
                .Where(entry => entry.IsMissing && (only is null || only.Contains(entry.Name)))
                .ToList()))
            .Where(item => item.Entries.Count > 0)
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        if (!IsInteractive)
        {
            foreach (var (location, entries) in missing)
            {
                WriteWarning($"{RepositoryVaults.Label(location)}: required values not set on this PC: {string.Join(", ", entries.Select(e => e.Name))}. Run lv load or lv set <name> in a console.");
            }

            return;
        }

        var typed = new List<(VaultLocation Location, VaultEntry Value)>();
        var written = new List<VaultLocation>();
        var keys = 0;
        foreach (var (location, entries) in missing)
        {
            foreach (var entry in entries)
            {
                if (entry.IsSshKeyShape)
                {
                    if (!AdoptMissingSshKey(location, entry) && AskSshKey(access, location, entry))
                    {
                        keys++;
                        written.Add(location);
                    }

                    continue;
                }

                Say($"{RepositoryVaults.Label(location)}: {entry.Name} is required{(entry.RequiredNote is { } note ? $" ({note})" : string.Empty)}. Empty skips it.");
                if (AskRequiredValue(location, entry) is { } value)
                {
                    typed.Add((location, value));
                }
            }
        }

        foreach (var group in typed.GroupBy(item => item.Location))
        {
            Report(RepositoryVaults.StoreFor(group.Key).Update(body =>
            {
                foreach (var (_, value) in group)
                {
                    body.SetPersonalValue(value.Name, value);
                }
            }));
            written.Add(group.Key);
        }

        var count = typed.Count + keys;
        if (count == 0)
        {
            return;
        }

        Say($"Stored {count} value(s) in the vault.");
        written = [.. written.Distinct()];
        if (access.Registry.Personal is { } personal)
        {
            if (Confirm($"Save the {count} value(s) to your personal store '{personal.Name}' too?", defaultYes: true))
            {
                VaultSync.Save(this, access, written, (_, _) => false, personalOnly: true);
            }
        }
        else if (OfferPersonalStore(access) is not null)
        {
            VaultSync.Save(this, access, written, (_, _) => false, personalOnly: true);
        }
    }

    private VaultEntry? AskRequiredValue(VaultLocation location, VaultEntry entry)
    {
        if (entry.IsFileShape)
        {
            var declared = entry.RequiredPath is { } path ? StoredPathRules.Resolve(location.Directory, path) : null;
            var typed = ReadText($"File for {entry.Name} (empty: {(declared is null ? "skip" : $"{declared} if it exists, else skip")})");
            var source = typed.Length > 0 ? GetUnresolvedProviderPathFromPSPath(typed) : declared;
            if (source is null || !File.Exists(source))
            {
                if (typed.Length > 0)
                {
                    WriteWarning($"'{source}' doesn't exist; {entry.Name} skipped.");
                }

                return null;
            }

            var stored = entry.RequiredPath ?? StoredPathRules.HomeFormOf(source) ?? StoredPathRules.FromFullPath(location.Directory, source);
            var content = File.ReadAllBytes(source);
            try
            {
                return VaultEntry.NewFile(entry.Name, content, stored, Now);
            }
            finally
            {
                Array.Clear(content);
            }
        }

        if (entry.IsCredentialShape)
        {
            var user = ReadText($"User name for {entry.Name}");
            var password = SecretText.ToUtf8(ReadSecret($"Password for {entry.Name}"));
            try
            {
                return password.Length == 0 ? null : VaultEntry.NewCredential(entry.Name, user, password, null, [], Now);
            }
            finally
            {
                Array.Clear(password);
            }
        }

        var value = SecretText.ToUtf8(ReadSecret($"Value for {entry.Name}"));
        try
        {
            return value.Length == 0 ? null : VaultEntry.NewSecret(entry.Name, value, Now);
        }
        finally
        {
            Array.Clear(value);
        }
    }

    private bool HasStore()
    {
        try
        {
            var resolution = StoreResolver.For(Location, null, new StoreRegistry(StoreRegistry.DefaultPath));
            return resolution.Definition is not null || resolution.Reference is not null;
        }
        catch (VaultException)
        {
            return false;
        }
    }

    protected bool ExplicitVault => Path is not null || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VaultLocation.EnvironmentVariable));

    protected void Report(VaultWriteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Created)
        {
            Say($"Created vault {result.Path}");
        }

        if (result.Path is { } written && VaultLocation.Resolve(written, null, System.IO.Path.GetDirectoryName(written)!) is { GitRoot: not null } location
            && ProjectFile.AddVault(location))
        {
            Say($"Added {RepositoryVaults.Label(location)} to {ProjectFile.FileName}; commit it so that the repository knows this vault.");
        }

        if (result.MigratedLegacyPath is not null)
        {
            Say($"Migrated {result.MigratedNames.Count} entries from {VaultLocation.LegacyFileName}: " +
                string.Join(", ", result.MigratedNames));
            Say($"The old file is kept as {result.MigratedLegacyPath}");
        }

        if (result.UpgradedFromSchema is { } schema)
        {
            Say($"Upgraded the vault from schema {schema} to schema {VaultBody.CurrentSchema}.");
        }

        ReportRefused(result.LegacyRefused, result.MigratedLegacyPath);
    }

    protected LeftoverBackups FindLeftoverBackups(BackupSettings? settings) =>
        LeftoverBackups.Find(Location, settings, Environment.MachineName, Environment.GetEnvironmentVariable("OneDrive"));

    protected void DeleteLeftoverBackups(LeftoverBackups leftovers)
    {
        ArgumentNullException.ThrowIfNull(leftovers);
        leftovers.Delete();
        if (leftovers.Files.Count > 0)
        {
            Say($"Deleted {leftovers.Files.Count} old automatic backup(s) in {string.Join(", ", leftovers.Folders)}");
        }

        if (leftovers.UnresolvedDestination is { } destination)
        {
            WriteWarning($"Old automatic backups may remain in '{destination}', which does not resolve on this PC. Delete them by hand.");
        }
    }

    private void OfferBackupCleanup()
    {
        if (!OffersBackupCleanup)
        {
            return;
        }

        LeftoverBackups leftovers;
        string vault;
        try
        {
            vault = Location.Path;
            lock (SettledBackupCleanups)
            {
                if (SettledBackupCleanups.Contains(vault))
                {
                    return;
                }
            }

            leftovers = FindLeftoverBackups(File.Exists(vault) ? VaultFile.Read(ignoreLegacy: true).Body.Backup : null);
        }
        catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (!leftovers.Any)
        {
            return;
        }

        lock (SettledBackupCleanups)
        {
            SettledBackupCleanups.Add(vault);
        }

        var found = leftovers.Files.Count > 0
            ? $"{leftovers.Files.Count} old automatic backup(s) of this vault in {string.Join(", ", leftovers.Folders)}"
            : "the key of its old automatic backups";
        if (!IsInteractive)
        {
            WriteWarning($"LocalVault no longer makes automatic backups, and {vault} still has {found}. Run any lv command in a console to delete them, or lv clear.");
            return;
        }

        if (!Confirm($"LocalVault no longer makes automatic backups, and {vault} still has {found}. They are copies of its values. Delete them?"))
        {
            Say("Old backups kept. You will be asked again in the next session.");
            return;
        }

        try
        {
            DeleteLeftoverBackups(leftovers);
            if (leftovers.HasSettings)
            {
                Report(VaultFile.Update(body => body.Backup = null, new VaultUpdateOptions { DropPreviousGeneration = true }));
                Say("Removed the backup key from the vault.");
            }
        }
        catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
        {
            WriteWarning($"Old backups not fully deleted: {ex.Message}");
        }
    }

    protected void ReportRefused(IReadOnlyList<RefusedName> refused, string? keptIn)
    {
        ArgumentNullException.ThrowIfNull(refused);
        foreach (var item in refused)
        {
            WriteWarning(keptIn is null
                ? $"Not imported: {item.Name} ({item.Reason})"
                : $"Not migrated: {item.Name} ({item.Reason}); still in {keptIn}");
        }
    }

    protected void Say(string message) =>
        WriteInformation(new InformationRecord(message, MyInvocation.MyCommand.Name), ["PSHOST"]);

    protected bool Confirm(string question, bool defaultYes = false)
    {
        Collection<ChoiceDescription> choices = [new("&Yes"), new("&No")];
        return Host.UI.PromptForChoice("LocalVault", question, choices, defaultYes ? 0 : 1) == 0;
    }

    protected string ReadText(string prompt)
    {
        if (IsInteractive)
        {
            Host.UI.Write(prompt + ": ");
            return Host.UI.ReadLine() ?? string.Empty;
        }

        if (Console.IsInputRedirected)
        {
            return Console.In.ReadLine() ?? string.Empty;
        }

        throw new VaultException($"{prompt}: no value given and no console to ask on.");
    }

    protected SecureString ReadSecret(string prompt)
    {
        if (IsInteractive)
        {
            Host.UI.Write(prompt + ": ");
            return Host.UI.ReadLineAsSecureString();
        }

        if (Console.IsInputRedirected)
        {
            var line = Console.In.ReadLine() ?? string.Empty;
            return SecretText.FromChars(line);
        }

        throw new VaultException($"{prompt}: no value given and no console to ask on.");
    }

    protected SecureString ReadNewPassphrase(SecureString? given)
    {
        if (given is not null)
        {
            return given;
        }

        if (!IsInteractive)
        {
            return Console.IsInputRedirected
                ? ReadSecret("Passphrase")
                : throw new VaultException("Pass -Passphrase: there is no console to ask on.");
        }

        Say($"Passphrase: at least {Envelope.MinimumPassphraseLength} characters. It cannot be recovered; store it in KeePass now.");
        var first = ReadSecret("Passphrase");
        var second = ReadSecret("Passphrase again");
        if (!SecretText.AreEqual(first, second))
        {
            throw new VaultException("The two passphrases differ. Nothing was written.");
        }

        return first;
    }

    protected SecureString ReadPassphrase(SecureString? given, string source) =>
        given ?? (IsInteractive || Console.IsInputRedirected
            ? ReadSecret($"Passphrase for {source}")
            : throw new VaultException("Pass -Passphrase: there is no console to ask on."));

    protected static void RequireValue(VaultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsMissing)
        {
            throw new VaultException(
                $"'{entry.Name}' is required but not set on this PC. Run lv set {entry.Name}" +
                (entry.RequiredNote is { } note ? $" ({note})." : "."));
        }
    }

    internal static IHelloKey HelloKey => HelloKeys.Current;

    internal static void RequireHelloConsole()
    {
        if (HelloKey.NeedsConsole && !IsInteractive)
        {
            throw new HelloException("Windows Hello is not available: it needs an interactive console (input not redirected, no -NonInteractive). Nothing was read or written.");
        }
    }

    internal VaultBody Unseal(VaultBody body, IEnumerable<EntryName> names, string action)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(names);
        var sealedEntries = names.Select(body.Find).OfType<VaultEntry>().Where(e => e.IsHello)
            .DistinctBy(e => e.Name.FullName, EntryName.Comparer).ToList();
        if (sealedEntries.Count == 0)
        {
            return body;
        }

        var copy = body.Clone();
        foreach (var plain in HelloEntries.Unseal(body, sealedEntries, action, Location.Path).Values)
        {
            copy.Set(plain);
        }

        return copy;
    }

    internal VaultEntry Seal(VaultBody body, VaultEntry plain)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(plain);
        return HelloEntries.Seal(body, plain);
    }

    protected VaultEntry FindEntry(VaultBody body, EntryName name) =>
        body.Find(name) ?? throw new VaultException($"No entry '{name}' in {Location.Path}.");

    protected static object PartOutput(VaultEntry entry, EntryReference reference, bool asPlainText)
    {
        ArgumentNullException.ThrowIfNull(entry);
        RequireValue(entry);
        if (!entry.IsSecret && !entry.IsCredential)
        {
            throw new VaultException($"'{entry.Name}' is a {entry.Kind} entry, not a secret.");
        }

        var bytes = entry.ReadPart(reference, Now);
        try
        {
            return asPlainText || !reference.IsSecret ? System.Text.Encoding.UTF8.GetString(bytes) : SecretText.FromUtf8(bytes);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }
}
