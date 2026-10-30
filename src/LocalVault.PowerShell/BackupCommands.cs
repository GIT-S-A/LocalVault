using System.Management.Automation;
using System.Security;
using System.Security.Cryptography;
using LocalVault.Core;

namespace LocalVault.PowerShell;

public sealed record BackupStatus(string Destination, string? Newest, DateTimeOffset? LastBackup, string? LastError);

public sealed record BackupTestResult(string Backup, int Entries, DateTimeOffset? Taken);


[Cmdlet(VerbsLifecycle.Enable, "VaultBackup", DefaultParameterSetName = GeneratedSet)]
public sealed class EnableVaultBackupCommand : VaultCmdletBase
{
    internal const string GeneratedSet = "Generated";
    internal const string ManualSet = "Manual";

    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Destination { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = ManualSet)]
    public SwitchParameter Manual { get; set; }

    [Parameter(ParameterSetName = ManualSet)]
    [ValidateNotNullOrEmpty]
    public string? KeyFile { get; set; }

    [Parameter(ParameterSetName = ManualSet)]
    [ValidateNotNullOrEmpty]
    public string? KeyFileOut { get; set; }

    [Parameter(ParameterSetName = ManualSet)]
    public SecureString? Passphrase { get; set; }

    protected override void Run()
    {
        if (ReadVault().Body.Backup is { } existing)
        {
            Say("Backups are already on. Nothing changed.");
            BackupStatusReader.Tell(Say, existing);
            Say("To change the folder or the key: Disable-VaultBackup, then Enable-VaultBackup.");
            return;
        }

        var destination = Destination is null
            ? BackupLocator.DefaultDestination(Location, Environment.MachineName)
            : GetUnresolvedProviderPathFromPSPath(Destination);
        var folder = BackupFileWriter.ResolveDestination(destination);

        var options = WriteOptions();
        var settings = Manual ? ManualSettings(destination) : GeneratedSettings(destination);
        if (settings is null)
        {
            return;
        }

        var result = VaultFile.Update(
            body => body.Backup = body.Backup is null
                ? settings
                : throw new VaultException("Backups were turned on meanwhile. Nothing changed."),
            options);
        Report(result);
        Say($"Backups on. Every change to the vault now also writes a backup to {folder}.");
        if (result.BackupPath is not null)
        {
            Say($"First backup: {result.BackupPath}");
        }

    }

    private static BackupSettings GeneratedSettings(string destination)
    {
        var passphrase = GeneratedPassphrase.Create();
        var keyFile = RandomNumberGenerator.GetBytes(Envelope.KeyFileLength);
        try
        {
            return BackupSettings.Create(BackupSettings.NewId(), destination, passphrase, keyFile);
        }
        finally
        {
            Array.Clear(passphrase);
            CryptographicOperations.ZeroMemory(keyFile);
        }
    }

    private string AskKeyFileOut()
    {
        Host.UI.Write("Where to write the new keyfile: ");
        return Host.UI.ReadLine();
    }

    private BackupSettings? ManualSettings(string destination)
    {
        var passphrase = SecretText.ToChars(ReadNewPassphrase(Passphrase));
        var keyFile = KeyFile is null
            ? RandomNumberGenerator.GetBytes(Envelope.KeyFileLength)
            : File.ReadAllBytes(GetUnresolvedProviderPathFromPSPath(KeyFile));
        try
        {
            var settings = BackupSettings.Create(BackupSettings.NewId(), destination, passphrase, keyFile);
            if (KeyFile is null)
            {
                var output = KeyFileOut ?? (IsInteractive
                    ? AskKeyFileOut()
                    : throw new VaultException("Give -KeyFileOut (where to write the new keyfile) or -KeyFile."));
                var outputPath = GetUnresolvedProviderPathFromPSPath(output);
                SealedFile.WriteBytes(outputPath, keyFile, overwrite: false);
                Say($"New keyfile written to {outputPath} (id {KeyFileIdentity.Compute(keyFile)}).");
            }

            if (IsInteractive && !Confirm("Are the keyfile and the passphrase stored outside this machine (KeePass)? Without both, backups cannot be restored."))
            {
                Say("Backups not enabled.");
                return null;
            }

            if (!IsInteractive)
            {
                WriteWarning("Store the keyfile and the passphrase outside this machine; without both, backups cannot be restored.");
            }

            return settings;
        }
        finally
        {
            Array.Clear(passphrase);
            CryptographicOperations.ZeroMemory(keyFile);
        }
    }
}

[Cmdlet(VerbsCommon.Get, "VaultBackup")]
[OutputType(typeof(BackupStatus))]
public sealed class GetVaultBackupCommand : VaultCmdletBase
{
    protected override void Run()
    {
        if (ReadVault().Body.Backup is { } settings)
        {
            WriteObject(BackupStatusReader.Read(settings));
        }
        else
        {
            Say("Backups are off. See Enable-VaultBackup.");
        }
    }
}

[Cmdlet(VerbsLifecycle.Disable, "VaultBackup", SupportsShouldProcess = true)]
public sealed class DisableVaultBackupCommand : VaultCmdletBase
{
    protected override void Run()
    {
        var backup = ReadVault().Body.Backup;
        if (backup is null)
        {
            Say("Backups are not enabled.");
            return;
        }

        if (!ShouldProcess(Location.Path, "Disable automatic backups"))
        {
            return;
        }

        Report(VaultFile.Update(body => body.Backup = null));
        Say($"Backups disabled. Existing backups kept in {Environment.ExpandEnvironmentVariables(backup.Destination)}");
    }
}

[Cmdlet(VerbsLifecycle.Invoke, "VaultBackup")]
[OutputType(typeof(BackupStatus))]
public sealed class InvokeVaultBackupCommand : VaultCmdletBase
{
    protected override void Run()
    {
        if (ReadVault().Body.Backup is null)
        {
            throw new VaultException("Backups are not enabled. See Enable-VaultBackup.");
        }

        var result = VaultFile.Update(_ => { });
        Report(result);
        if (result.BackupPath is not null)
        {
            Say($"Backup written: {result.BackupPath}");
        }

        WriteObject(BackupStatusReader.Read(ReadVault().Body.Backup!));
    }
}

[Cmdlet(VerbsDiagnostic.Test, "VaultBackup", DefaultParameterSetName = VaultKeySet)]
[OutputType(typeof(BackupTestResult))]
public sealed class TestVaultBackupCommand : VaultCmdletBase
{
    private const string VaultKeySet = "VaultKey";

    [Parameter(Mandatory = true, ParameterSetName = EnableVaultBackupCommand.ManualSet)]
    public SwitchParameter Manual { get; set; }

    [Parameter(Mandatory = true, ParameterSetName = EnableVaultBackupCommand.ManualSet)]
    [ValidateNotNullOrEmpty]
    public string KeyFile { get; set; } = string.Empty;

    [Parameter(ParameterSetName = EnableVaultBackupCommand.ManualSet)]
    public SecureString? Passphrase { get; set; }

    protected override void Run()
    {
        var settings = ReadVault().Body.Backup
            ?? throw new VaultException("Backups are not enabled. See Enable-VaultBackup.");
        var newest = BackupFileWriter.FindNewest(settings.Destination, settings.Id)
            ?? throw new VaultException($"No backup in {BackupFileWriter.ResolveDestination(settings.Destination)}.");

        var passphrase = Manual ? SecretText.ToChars(ReadPassphrase(Passphrase, newest)) : settings.GetPassphrase();
        var keyFile = Manual ? File.ReadAllBytes(GetUnresolvedProviderPathFromPSPath(KeyFile)) : settings.GetKeyFile();
        ImportSourceContent content;
        try
        {
            content = VaultTransfer.OpenBackup(newest, passphrase, keyFile);
        }
        finally
        {
            Array.Clear(passphrase);
            CryptographicOperations.ZeroMemory(keyFile);
        }

        Say($"{newest} opens with {(Manual ? "the keyfile and passphrase given" : "the key kept in this vault")}: {content.Entries.Count} entries.");
        WriteObject(new BackupTestResult(newest, content.Entries.Count, BackupFileWriter.TakenAt(newest)));
    }
}
internal static class BackupStatusReader
{
    public static BackupStatus Read(BackupSettings settings) => new(
        settings.Destination,
        BackupFileWriter.FindNewest(settings.Destination, settings.Id),
        settings.LastBackup,
        settings.LastError);

    public static void Tell(Action<string> say, BackupSettings settings)
    {
        var status = Read(settings);
        say($"Folder: {BackupFileWriter.ResolveDestination(status.Destination)}");
        say($"Last backup: {status.Newest ?? "none"}");
        if (status.LastError is not null)
        {
            say($"Last backup failed: {status.LastError}");
        }
    }
}