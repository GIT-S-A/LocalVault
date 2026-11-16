using System.Management.Automation;
using System.Text;
using LocalVault.Core;

namespace LocalVault.PowerShell;

public sealed record VaultTemplateInfo(string Path, string Template, string Vault, bool Rendered, bool? UpToDate);

public sealed record RenderedTemplate(string Path, string Action);

public abstract class TemplateCmdletBase : VaultCmdletBase
{
    internal VaultLocation Repository => Location.GitRoot is not null
        ? Location
        : throw new VaultException("Templates belong to a git repository; this folder is not in one.");

    internal IReadOnlyList<ProjectTemplate> Selected(string? file)
    {
        var templates = ProjectFile.ReadTemplates(Repository);
        if (file is null)
        {
            return templates;
        }

        var full = GetUnresolvedProviderPathFromPSPath(file);
        return templates.Where(t => string.Equals(t.Path, full, StringComparison.OrdinalIgnoreCase)).ToList() is { Count: > 0 } found
            ? found
            : throw new VaultException($"'{file}' is not a template of this repository. lv template lists them.");
    }

    internal static Dictionary<EntryReference, string> Secrets(VaultBody body)
    {
        var values = new Dictionary<EntryReference, string>();
        foreach (var entry in body.Entries.Where(entry => !entry.IsHello && (entry.IsSecret || (entry.IsCredential && !entry.IsMissing))))
        {
            values[new EntryReference(entry.Name)] = Encoding.UTF8.GetString(entry.GetValue());
            if (entry.UserName is { } user)
            {
                values[new EntryReference(entry.Name, EntryPart.User)] = user;
            }

            for (var i = 0; i < entry.Urls.Count; i++)
            {
                values[new EntryReference(entry.Name, EntryPart.Url, i + 1)] = entry.Urls[i];
            }

            if (entry.Totp is { } totp)
            {
                values[new EntryReference(entry.Name, EntryPart.Totp)] = totp.Secret;
            }
        }

        return values;
    }

    internal static void StoreValue(VaultBody body, CapturedValue value, DateTimeOffset now)
    {
        var bytes = Encoding.UTF8.GetBytes(value.Value!);
        try
        {
            var existing = body.Find(value.Name);
            body.Set(existing is { IsCredential: true } || value.Reference.Part != EntryPart.Password
                ? (existing ?? VaultEntry.NewCredential(value.Name, null, [], null, [], now)).WithPart(value.Reference, value.Value, bytes, now)
                : VaultEntry.NewSecret(value.Name, bytes, now));
        }
        finally
        {
            Array.Clear(bytes);
        }
    }

    internal static VaultFile VaultAt(string path) => RepositoryVaults.StoreFor(VaultLocation.Resolve(path, null, System.IO.Path.GetDirectoryName(path)!));

    internal static void RequireIgnored(string path)
    {
        if (!GitIgnore.IsIgnored(path))
        {
            throw new VaultException($"'{path}' would hold secrets but git does not ignore it. Add it to .gitignore first. Nothing was written.");
        }
    }

    internal static string Render(ProjectTemplate template, VaultBody body)
    {
        var secrets = Secrets(body);
        var now = DateTimeOffset.UtcNow;
        return TemplateText.Render(File.ReadAllText(template.Template), reference =>
            body.Find(reference.Name) is { IsMissing: true } missing ? throw new VaultException($"'{reference.Name}' is required but not set on this PC: {missing.RequiredNote ?? "ask the team"}. Run lv set {reference.Name}.")
            : reference.Part == EntryPart.Totp ? body.Find(reference.Name)?.Totp?.Code(now)
            : secrets.TryGetValue(reference, out var value) ? value
            : null);
    }
}

[Cmdlet(VerbsCommon.Add, "VaultTemplate", SupportsShouldProcess = true)]
[OutputType(typeof(VaultTemplateInfo))]
public sealed class AddVaultTemplateCommand : TemplateCmdletBase
{
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string File { get; set; } = string.Empty;

    [Parameter]
    public string[] Capture { get; set; } = [];

    protected override void Run()
    {
        var repository = Repository;
        var target = GetUnresolvedProviderPathFromPSPath(File);
        StoredPathRules.FromFullPath(repository.GitRoot!, target);
        var template = target + TemplateText.TemplateSuffix;
        var example = target + TemplateText.ExampleSuffix;
        if (System.IO.File.Exists(template))
        {
            throw new VaultException($"'{template}' exists already. Edit {File} and run lv template update instead.");
        }

        var body = ReadVault().Body;
        TemplateDraft draft;
        if (System.IO.File.Exists(target))
        {
            RequireIgnored(target);
            draft = TemplateText.FromFile(System.IO.File.ReadAllText(target), Secrets(body), Capture);
        }
        else if (System.IO.File.Exists(example))
        {
            draft = TemplateText.FromExample(System.IO.File.ReadAllText(example), body.Entries.Select(e => e.Name).ToHashSet(), Capture);
        }
        else
        {
            throw new VaultException($"Neither '{target}' nor '{example}' exists. Create the file as it should be, then add it.");
        }

        if (!ShouldProcess(template, $"Write the template of {File}"))
        {
            return;
        }

        var now = Now;
        var added = draft.Captured.Where(c => c.Value is not null || body.Find(c.Name) is null).ToList();
        if (added.Count > 0)
        {
            Report(VaultFile.Update(
                current =>
                {
                    foreach (var captured in added)
                    {
                        current.Set(captured.Value is null
                            ? VaultEntry.NewRequired(captured.Name, $"value for {System.IO.Path.GetFileName(target)}", now)
                            : VaultEntry.NewSecret(captured.Name, Encoding.UTF8.GetBytes(captured.Value), now));
                    }
                },
                WriteOptions()));
        }

        System.IO.File.WriteAllText(template, draft.Template);
        var project = ProjectFile.AddTemplate(Location, target, template);
        foreach (var captured in added)
        {
            Say(captured.Value is null
                ? $"{captured.Name} is declared; each person sets it with lv set {captured.Name}."
                : $"{captured.Name} moved into the vault.");
        }

        if (GitIgnore.IsIgnored(template))
        {
            WriteWarning($"git ignores '{template}'; the template should be committed. Adjust .gitignore.");
        }

        Say($"Wrote {template}. Commit it with {project}; lv template render writes {File} from it.");
        WriteObject(new VaultTemplateInfo(target, template, Location.Path, System.IO.File.Exists(target), null));
    }
}

[Cmdlet(VerbsData.Update, "VaultTemplate", SupportsShouldProcess = true)]
public sealed class UpdateVaultTemplateCommand : TemplateCmdletBase
{
    [Parameter(Position = 0)]
    [ValidateNotNullOrEmpty]
    public string? File { get; set; }

    [Parameter]
    public SwitchParameter AcceptChanges { get; set; }

    protected override void Run()
    {
        foreach (var group in Selected(File).GroupBy(t => t.Vault, StringComparer.OrdinalIgnoreCase))
        {
            var store = VaultAt(group.Key);
            var body = store.Read().Body;
            var updates = new List<(ProjectTemplate Template, TemplateUpdate Update)>();
            foreach (var template in group)
            {
                if (!System.IO.File.Exists(template.Path))
                {
                    WriteWarning($"{template.Display} isn't written on this PC; nothing to take from it.");
                    continue;
                }

                updates.Add((template, TemplateText.Update(System.IO.File.ReadAllText(template.Template), System.IO.File.ReadAllText(template.Path), Secrets(body))));
            }

            var changed = updates.SelectMany(u => u.Update.ChangedSecrets).ToList();
            var accepted = changed.Where(c => AcceptChanges || (IsInteractive && Confirm($"'{c.Reference}' has a new value in the file. Store it in the vault?"))).ToList();
            var refused = changed.Except(accepted).Select(c => c.Reference.ToString()).ToList();
            if (refused.Count > 0 && !IsInteractive)
            {
                throw new VaultException($"New values in the file for {string.Join(", ", refused)}. Use -AcceptChanges (lv template update --accept) to store them. Nothing was written.");
            }

            if (accepted.Count > 0 && ShouldProcess(group.Key, $"Store new values of {string.Join(", ", accepted.Select(c => c.Reference))}"))
            {
                var now = Now;
                Report(store.Update(current =>
                {
                    foreach (var value in accepted)
                    {
                        StoreValue(current, value, now);
                    }
                }));
            }

            foreach (var (template, update) in updates)
            {
                if (update.Template == System.IO.File.ReadAllText(template.Template))
                {
                    Say($"{template.Display}: template unchanged.");
                }
                else if (ShouldProcess(template.Template, "Update the template"))
                {
                    System.IO.File.WriteAllText(template.Template, update.Template);
                    Say($"{template.Display}: template updated; commit it.");
                }
            }
        }
    }
}

[Cmdlet(VerbsCommunications.Write, "VaultTemplate", SupportsShouldProcess = true)]
[OutputType(typeof(RenderedTemplate))]
public sealed class WriteVaultTemplateCommand : TemplateCmdletBase
{
    [Parameter(Position = 0)]
    [ValidateNotNullOrEmpty]
    public string? File { get; set; }

    protected override void Run()
    {
        foreach (var group in Selected(File).GroupBy(t => t.Vault, StringComparer.OrdinalIgnoreCase))
        {
            var body = string.Equals(group.Key, Location.Path, StringComparison.OrdinalIgnoreCase)
                ? ReadForUse(
                    current => group.All(t => TemplateText.Placeholders(System.IO.File.ReadAllText(t.Template)).All(n => current.Find(n.Name) is not null)),
                    _ => group.SelectMany(t => TemplateText.Placeholders(System.IO.File.ReadAllText(t.Template)).Select(n => n.Name)))
                : VaultAt(group.Key).Read().Body;
            body = Unseal(body, group.SelectMany(t => TemplateText.Placeholders(System.IO.File.ReadAllText(t.Template)).Select(n => n.Name)), "write templates from");
            var rendered = group.Select(t => (Template: t, Text: Render(t, body))).ToList();
            foreach (var (template, text) in rendered)
            {
                RequireIgnored(template.Path);
                var bytes = Encoding.UTF8.GetBytes(text);
                if (System.IO.File.Exists(template.Path) && System.IO.File.ReadAllBytes(template.Path).AsSpan().SequenceEqual(bytes))
                {
                    WriteObject(new RenderedTemplate(template.Path, "Identical"));
                }
                else if (ShouldProcess(template.Path, "Write from its template"))
                {
                    SealedFile.WriteBytes(template.Path, bytes, overwrite: true);
                    WriteObject(new RenderedTemplate(template.Path, "Written"));
                }

                Array.Clear(bytes);
            }
        }
    }
}

[Cmdlet(VerbsCommon.Clear, "VaultTemplate", SupportsShouldProcess = true)]
[OutputType(typeof(RenderedTemplate))]
public sealed class ClearVaultTemplateCommand : TemplateCmdletBase
{
    [Parameter(Position = 0)]
    [ValidateNotNullOrEmpty]
    public string? File { get; set; }

    protected override void Run()
    {
        foreach (var template in Selected(File).Where(t => System.IO.File.Exists(t.Path)))
        {
            if (ShouldProcess(template.Path, "Delete the written file"))
            {
                System.IO.File.Delete(template.Path);
                WriteObject(new RenderedTemplate(template.Path, "Deleted"));
            }
        }
    }
}

[Cmdlet(VerbsCommon.Remove, "VaultTemplate", SupportsShouldProcess = true)]
public sealed class RemoveVaultTemplateCommand : TemplateCmdletBase
{
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string File { get; set; } = string.Empty;

    protected override void Run()
    {
        var target = GetUnresolvedProviderPathFromPSPath(File);
        if (ShouldProcess(ProjectFile.PathFor(Repository)!, $"Stop managing {File}") && !ProjectFile.RemoveTemplate(Repository, target))
        {
            WriteWarning($"'{File}' is not a template of this repository.");
        }
    }
}

[Cmdlet(VerbsCommon.Get, "VaultTemplate")]
[OutputType(typeof(VaultTemplateInfo))]
public sealed class GetVaultTemplateCommand : TemplateCmdletBase
{
    protected override void Run()
    {
        foreach (var group in Selected(null).GroupBy(t => t.Vault, StringComparer.OrdinalIgnoreCase))
        {
            var snapshot = VaultAt(group.Key).Read();
            foreach (var template in group)
            {
                var rendered = System.IO.File.Exists(template.Path);
                bool? upToDate = null;
                if (rendered && snapshot.Exists && System.IO.File.Exists(template.Template))
                {
                    try
                    {
                        upToDate = Render(template, snapshot.Body) == System.IO.File.ReadAllText(template.Path);
                    }
                    catch (VaultException)
                    {
                        upToDate = null;
                    }
                }

                WriteObject(new VaultTemplateInfo(template.Path, template.Template, template.Vault, rendered, upToDate));
            }
        }
    }
}
