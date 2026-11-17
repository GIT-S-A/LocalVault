namespace LocalVault.Engine.Tests;

public sealed class TemplateTextTests
{
    private static readonly Dictionary<EntryReference, string> Secrets = new()
    {
        [R("OS_PASSWORD")] = "os-secret-1",
        [R("db/password")] = "db-secret-22",
        [R("short")] = "abc",
        [R("gh#user")] = "jdoe-user",
        [R("gh")] = "gh-password-5",
        [R("gh#totp")] = "JBSWY3DPEHPK3PXP",
    };

    private static EntryReference R(string text) => EntryReference.Parse(text);

    [Fact]
    public void RenderFillsPlaceholdersAndNamesWhatIsMissing()
    {
        var template = "OS_PASSWORD={{OS_PASSWORD}}\nDB={{db/password}}\nkeep {{ spaced }}\n";

        Assert.Equal("OS_PASSWORD=os-secret-1\nDB=db-secret-22\nkeep {{ spaced }}\n", TemplateText.Render(template, name => Secrets.GetValueOrDefault(name)));
        Assert.Equal([R("OS_PASSWORD"), R("db/password")], TemplateText.Placeholders(template));
        var error = Assert.Throws<VaultException>(() => TemplateText.Render("A={{absent}}\nB={{absent}}", _ => null));
        Assert.Equal("No value for absent. Nothing was written.", error.Message);
    }

    [Fact]
    public void FromFileReplacesKnownValuesAndCapturesNamedLines()
    {
        var content = "OS_AUTH_URL=https://api\r\nOS_PASSWORD=os-secret-1\r\nexport TOKEN=\"new-token-333\" # comment\r\nport: abc\r\nyaml_key: 'quoted-999'\r\n";

        var draft = TemplateText.FromFile(content, Secrets, ["TOKEN", "yaml_key"]);

        Assert.Equal(
            "OS_AUTH_URL=https://api\r\nOS_PASSWORD={{OS_PASSWORD}}\r\nexport TOKEN=\"{{TOKEN}}\" # comment\r\nport: abc\r\nyaml_key: '{{yaml_key}}'\r\n",
            draft.Template);
        Assert.Equal([new CapturedValue(R("TOKEN"), "new-token-333"), new CapturedValue(R("yaml_key"), "quoted-999")], draft.Captured);
        Assert.Contains("ABSENT", Assert.Throws<VaultException>(() => TemplateText.FromFile(content, Secrets, ["ABSENT"])).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromExampleUsesKnownNamesAndDeclaresCapturedOnes()
    {
        var example = "OS_PASSWORD=changeme\nNEW_TOKEN=\nOTHER=1\n";

        var draft = TemplateText.FromExample(example, Secrets.Keys.Select(r => r.Name).ToHashSet(), ["NEW_TOKEN"]);

        Assert.Equal("OS_PASSWORD={{OS_PASSWORD}}\nNEW_TOKEN={{NEW_TOKEN}}\nOTHER=1\n", draft.Template);
        Assert.Equal([new CapturedValue(R("NEW_TOKEN"), null)], draft.Captured);
    }

    [Fact]
    public void UpdateTakesEditsAndReportsChangedSecrets()
    {
        var template = "URL=https://old\nOS_PASSWORD={{OS_PASSWORD}}\nDB={{db/password}}\n";
        var edited = "URL=https://new\nOS_PASSWORD=os-secret-1\nDB=db-changed-44\nEXTRA=os-secret-1\n";

        var update = TemplateText.Update(template, edited, Secrets);

        Assert.Equal("URL=https://new\nOS_PASSWORD={{OS_PASSWORD}}\nDB={{db/password}}\nEXTRA={{OS_PASSWORD}}\n", update.Template);
        Assert.Equal([new CapturedValue(R("db/password"), "db-changed-44")], update.ChangedSecrets);
    }

    [Fact]
    public void CredentialPartsRenderAndFollowEditsButTheTotpCodeDoesNot()
    {
        var template = "USER={{gh#user}}\nPASS={{gh}}\nCODE={{gh#totp}}\n";
        var values = new Dictionary<EntryReference, string>(Secrets) { [R("gh#totp")] = "123456" };

        Assert.Equal("USER=jdoe-user\nPASS=gh-password-5\nCODE=123456\n", TemplateText.Render(template, values.GetValueOrDefault));
        Assert.Equal([R("gh#user"), R("gh"), R("gh#totp")], TemplateText.Placeholders(template));
        var update = TemplateText.Update(template, "USER=other-user\nPASS=gh-password-5\nCODE=654321\n", Secrets);
        Assert.Equal([new CapturedValue(R("gh#user"), "other-user")], update.ChangedSecrets);
        Assert.Equal(template, update.Template);
    }

    [Fact]
    public void UserNamesAreNotSecretsButTotpSeedsAre()
    {
        TemplateText.RequireNoSecret("login jdoe-user", Secrets);
        Assert.Contains("gh#totp", Assert.Throws<VaultException>(() => TemplateText.RequireNoSecret("seed JBSWY3DPEHPK3PXP", Secrets)).Message, StringComparison.Ordinal);
        Assert.Equal("A=jdoe-user\nB={{gh}}\n", TemplateText.FromFile("A=jdoe-user\nB=gh-password-5\n", Secrets, []).Template);
    }

    [Fact]
    public void UpdateRefusesWhenAPlaceholderLineLostItsShape()
    {
        var error = Assert.Throws<VaultException>(() => TemplateText.Update("OS_PASSWORD={{OS_PASSWORD}}\n", "OS_PASS=os-secret-1\n", Secrets));

        Assert.Contains("{{OS_PASSWORD}}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATemplateNeverHoldsAVaultValue()
    {
        Assert.Throws<VaultException>(() => TemplateText.RequireNoSecret("x=db-secret-22", Secrets));
        TemplateText.RequireNoSecret("x=abc", Secrets);
        Assert.DoesNotContain("db-secret-22", TemplateText.FromFile("A=db-secret-22\n", Secrets, []).Template, StringComparison.Ordinal);
    }
}

public sealed class ProjectTemplateTests : IDisposable
{
    private readonly TempDirectory repository = TempDirectory.GitRepository();

    public void Dispose() => repository.Dispose();

    [Fact]
    public void TemplatesAreRecordedWithTheirVault()
    {
        var vault = VaultLocation.Resolve(Path.Combine(repository.CreateDirectory("infra"), "secrets.local.lv"), null, repository.Path);
        var env = repository.Combine("infra", ".env");

        ProjectFile.AddTemplate(vault, env, env + ".template");
        ProjectFile.AddTemplate(vault, env, env + ".template");

        var template = Assert.Single(ProjectFile.ReadTemplates(vault));
        Assert.Equal(new ProjectTemplate(env, env + ".template", vault.Path, "infra/.env"), template);
        Assert.Throws<VaultException>(() => ProjectFile.AddTemplate(vault, Path.Combine(Path.GetTempPath(), "outside.env"), "x.template"));
        Assert.True(ProjectFile.RemoveTemplate(vault, env));
        Assert.Empty(ProjectFile.ReadTemplates(vault));
    }
}
