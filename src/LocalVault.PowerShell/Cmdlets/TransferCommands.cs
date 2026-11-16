using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Security;
using LocalVault.Core;

namespace LocalVault.PowerShell;

[Cmdlet(VerbsData.Export, "Vault")]
public sealed class ExportVaultCommand : VaultCmdletBase
{
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Destination { get; set; } = string.Empty;

    [Parameter]
    public SecureString? Passphrase { get; set; }

    [Parameter]
    public SwitchParameter Overwrite { get; set; }

    protected override void Run()
    {
        var body = ReadVault().Body;
        if (body.Entries.Count == 0)
        {
            throw new VaultException("The vault is empty; there is nothing to export.");
        }

        var destination = GetUnresolvedProviderPathFromPSPath(Destination);
        var passphrase = SecretText.ToChars(ReadNewPassphrase(Passphrase));
        var now = Now;
        try
        {
            var hello = body.Entries.Where(e => e.IsHello && !e.IsRequired).Select(e => e.Name).ToList();
            var plain = Unseal(body, hello, "export");
            VaultTransfer.Export(plain, destination, passphrase, Overwrite, now, hello);
        }
        finally
        {
            Array.Clear(passphrase);
        }

        Report(VaultFile.Update(current => current.LastExported = now));
        Say($"Written: {destination} ({body.Entries.Count} entries). Keep it off this machine and out of git.");
    }
}

public sealed record ImportSummary(int Added, int Replaced, int Identical, int Skipped, int Refused);

[Cmdlet(VerbsData.Import, "Vault", DefaultParameterSetName = SourceSet)]
[OutputType(typeof(ImportSummary))]
public sealed class ImportVaultCommand : VaultCmdletBase
{
    internal const string SourceSet = "Source";

    [Parameter(Mandatory = true, Position = 0, ParameterSetName = SourceSet)]
    [ValidateNotNullOrEmpty]
    public string Source { get; set; } = string.Empty;

    [Parameter(ParameterSetName = SourceSet)]
    public SecureString? Passphrase { get; set; }

    [Parameter(ParameterSetName = SourceSet)]
    [ValidateNotNullOrEmpty]
    public string? KeyFile { get; set; }

    [Parameter]
    public string[] Replace { get; set; } = [];

    [Parameter]
    public SwitchParameter ReplaceAll { get; set; }

    protected override void Run()
    {

        var path = GetUnresolvedProviderPathFromPSPath(Source);
        var kind = VaultTransfer.DetectSource(path);
        Merge(Open(path, kind), path, kind == ImportSourceKind.LegacyVault ? path : null);
    }

    internal void Merge(ImportSourceContent content, string source, string? legacySource)
    {
        var ignoreLegacy = legacySource is not null
            && string.Equals(legacySource, Location.LegacyPath, StringComparison.OrdinalIgnoreCase);
        var snapshot = ReadVault(ignoreLegacy);
        var plan = ImportPlan.Create(snapshot.Body, content);
        ReportRefused(content.Refused, keptIn: null);

        var chosen = new List<string>(Replace);
        if (plan.Unresolved(chosen, ReplaceAll).Count > 0)
        {
            if (!IsInteractive)
            {
                throw new ImportConflictException(
                    "These entries differ from the vault: " + string.Join(", ", plan.Unresolved(chosen, ReplaceAll)) +
                    ". Nothing was written. Use -Replace name,... or -ReplaceAll.");
            }

            chosen.AddRange(Ask(plan, chosen, snapshot.Body));
        }

        var selected = plan.Select(chosen, ReplaceAll);
        var replaced = selected.Count(entry => snapshot.Body.Find(entry.Name) is not null);
        var added = selected.Count - replaced;
        var skipped = plan.Different.Count + plan.CaseClashes.Sum(group => group.Count) - (selected.Count - plan.Missing.Count);
        if (selected.Count > 0)
        {
            Report(VaultFile.Update(
                body =>
                {
                    foreach (var entry in selected)
                    {
                        var keepsHello = entry.IsHelloMarked || (body.Find(entry.Name)?.IsHello == true && !entry.IsRequired);
                        body.Set(keepsHello && entry.IsSealable ? Seal(body, entry) : entry);
                    }
                },
                WriteOptions(snapshot.Fingerprint, ignoreLegacy)));
        }

        if (ignoreLegacy && skipped == 0 && File.Exists(legacySource))
        {
            Say($"The imported file is kept as {VaultFile.RetireLegacy(legacySource!)}");
        }

        Say($"From {source}: {added} added, {replaced} replaced, {plan.Identical.Count} identical, {skipped} left as they were.");
        WriteObject(new ImportSummary(added, replaced, plan.Identical.Count, skipped, content.Refused.Count));
    }

    private List<string> Ask(ImportPlan plan, List<string> alreadyChosen, VaultBody current)
    {
        var answers = new List<string>();
        if (!ReplaceAll)
        {
            foreach (var entry in plan.Different.Where(e => !alreadyChosen.Contains(e.Name.FullName, StringComparer.OrdinalIgnoreCase)))
            {
                if (Confirm($"'{entry.Name}' differs from the vault. Replace it with the imported value?"))
                {
                    answers.Add(entry.Name.FullName);
                }
            }
        }

        foreach (var group in plan.CaseClashes)
        {
            Collection<ChoiceDescription> choices = [new("&None")];
            for (var i = 0; i < group.Count; i++)
            {
                choices.Add(new ChoiceDescription($"&{i + 1} {group[i].Name}"));
            }

            var existing = current.Find(group[0].Name);
            var question = existing is null
                ? "These names differ only by case. Which one to import?"
                : $"These names differ only by case. The one you pick replaces '{existing.Name}' in the vault. Which one to import?";
            var answer = Host.UI.PromptForChoice("LocalVault", question, choices, 0);
            if (answer > 0)
            {
                answers.Add(group[answer - 1].Name.FullName);
            }
        }

        return answers;
    }

    private ImportSourceContent Open(string path, ImportSourceKind kind)
    {
        if (kind == ImportSourceKind.LegacyVault)
        {
            return VaultTransfer.OpenLegacyVault(path, new ClixmlLegacyReader());
        }

        if (kind == ImportSourceKind.Backup && KeyFile is null)
        {
            var settings = ReadVault().Body.Backup
                ?? throw new VaultException($"'{path}' is a backup and this vault has no backup key. Give -KeyFile.");
            var ownPassphrase = settings.GetPassphrase();
            var ownKeyFile = settings.GetKeyFile();
            try
            {
                return VaultTransfer.OpenBackup(path, ownPassphrase, ownKeyFile);
            }
            finally
            {
                Array.Clear(ownPassphrase);
                Array.Clear(ownKeyFile);
            }
        }

        var passphrase = SecretText.ToChars(ReadPassphrase(Passphrase, path));
        try
        {
            switch (kind)
            {
                case ImportSourceKind.Export:
                    return VaultTransfer.OpenExport(path, passphrase);
                case ImportSourceKind.LegacyExport:
                    return VaultTransfer.OpenLegacyExport(path, passphrase, Now);
                default:
                    var keyFile = File.ReadAllBytes(GetUnresolvedProviderPathFromPSPath(KeyFile!));
                    try
                    {
                        return VaultTransfer.OpenBackup(path, passphrase, keyFile);
                    }
                    finally
                    {
                        Array.Clear(keyFile);
                    }
            }
        }
        finally
        {
            Array.Clear(passphrase);
        }
    }
}

[Cmdlet(VerbsCommon.Clear, "Vault", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.High)]
public sealed class ClearVaultCommand : VaultCmdletBase
{
    protected override bool OffersBackupCleanup => false;

    protected override void Run()
    {
        BackupSettings? backup = null;
        try
        {
            backup = File.Exists(Location.Path) ? ReadVault(ignoreLegacy: true).Body.Backup : null;
        }
        catch (VaultException ex)
        {
            WriteWarning($"The vault cannot be read ({ex.Message}); only the backups in its default folder are found.");
        }

        var leftovers = FindLeftoverBackups(backup);
        var backups = leftovers.Files.Count > 0 ? $" and its {leftovers.Files.Count} old automatic backup(s)" : string.Empty;
        if (!VaultFile.Exists)
        {
            if (leftovers.Files.Count > 0 && ShouldProcess(Location.Path, $"Delete the {leftovers.Files.Count} old automatic backup(s) of this vault"))
            {
                DeleteLeftoverBackups(leftovers);
            }
            else
            {
                Say("No vault to delete.");
            }

            return;
        }

        var legacy = Location.LegacyPath is { } legacyPath && File.Exists(legacyPath) ? $", the unmigrated {legacyPath}" : string.Empty;
        if (!ShouldProcess(Location.Path, $"Delete the vault, its previous generation{legacy}{backups}"))
        {
            return;
        }

        var rendered = Location.GitRoot is null
            ? []
            : ProjectFile.ReadTemplates(Location)
                .Where(t => string.Equals(t.Vault, Location.Path, StringComparison.OrdinalIgnoreCase) && File.Exists(t.Path))
                .ToList();
        VaultFile.Delete();
        Say($"Deleted {Location.Path}");
        DeleteLeftoverBackups(leftovers);
        if (rendered.Count > 0 && (!IsInteractive || Confirm($"Also delete the files written from its templates ({string.Join(", ", rendered.Select(t => t.Display))})? They hold its values in plain text.")))
        {
            foreach (var template in rendered)
            {
                File.Delete(template.Path);
            }

            Say($"Deleted {string.Join(", ", rendered.Select(t => t.Display))}");
        }
    }
}
