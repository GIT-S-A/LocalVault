using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Security;
using LocalVault.Core;

namespace LocalVault.PowerShell;

public abstract class VaultCmdletBase : PSCmdlet, IVaultHost
{
    private VaultLocation? location;
    private VaultStore? store;

    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Path { get; set; }

    protected VaultLocation Location => location ??= VaultLocation.Resolve(
        Path,
        Environment.GetEnvironmentVariable(VaultLocation.EnvironmentVariable),
        SessionState.Path.CurrentFileSystemLocation.ProviderPath);

    internal VaultLocation VaultLocation => Location;

    protected VaultStore VaultFile => store ??= new(Location, new ClixmlLegacyReader(), new BackupFileWriter());

    protected static bool IsInteractive =>
        Environment.UserInteractive
        && !Console.IsInputRedirected
        && !Environment.GetCommandLineArgs().Any(
            argument => argument.StartsWith("-noni", StringComparison.OrdinalIgnoreCase));

    protected static DateTimeOffset Now => DateTimeOffset.UtcNow;

    protected override void ProcessRecord()
    {
        try
        {
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
            allowCreate = true;
        }

        return new VaultUpdateOptions
        {
            AllowCreate = allowCreate,
            ExpectedFingerprint = expectedFingerprint,
            IgnoreLegacy = ignoreLegacy,
        };
    }

    bool IVaultHost.CanPrompt => IsInteractive || Console.IsInputRedirected;

    bool IVaultHost.CanConfirm => IsInteractive;

    DateTimeOffset IVaultHost.Clock => Now;

    void IVaultHost.Tell(string message) => Say(message);

    void IVaultHost.Warn(string message) => WriteWarning(message);

    SecureString IVaultHost.AskSecret(string prompt) => ReadSecret(prompt);

    bool IVaultHost.AskYesNo(string question) => Confirm(question);

    void IVaultHost.ReportWrite(VaultWriteResult result) => Report(result);

    private bool autoLoaded;

    protected VaultBody ReadForUse(Func<VaultBody, bool> satisfied)
    {
        ArgumentNullException.ThrowIfNull(satisfied);
        var body = ReadVault().Body;
        if (satisfied(body) || autoLoaded || !IsInteractive || !HasStore())
        {
            return body;
        }

        autoLoaded = true;
        Say("Not on this PC yet: loading this repository's vaults from their stores.");
        using (var access = new StoreAccess(this, null))
        {
            VaultSync.Load(this, access, RepositoryVaults.Of(Location, ExplicitVault), (_, _) => false, (_, _) => false);
        }

        return ReadVault().Body;
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

        if (result.BackupError is not null)
        {
            WriteWarning($"Saved, but the backup failed: {result.BackupError}");
        }

        if (result.BackupCleanupError is not null)
        {
            WriteWarning($"Saved and backed up, but old backups could not be deleted: {result.BackupCleanupError}");
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

    protected bool Confirm(string question)
    {
        Collection<ChoiceDescription> choices = [new("&Yes"), new("&No")];
        return Host.UI.PromptForChoice("LocalVault", question, choices, 1) == 0;
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
