using System.Text.Json.Nodes;

namespace LocalVault.Engine.Tests;

public sealed class DotNetProjectsTests : IDisposable
{
    private const string ApiId = "api-1f2e";
    private const string WorkerId = "worker-77";

    private readonly TempDirectory repo = new();

    public void Dispose() => repo.Dispose();

    [Fact]
    public void SolutionNamesTheProjectsAndProjectsWithoutIdAreLeftOut()
    {
        var api = Project("src/Api/Api.csproj", ApiId);
        Project("src/Lib/Lib.csproj", null);
        Project("tools/Other/Other.csproj", "other-id");
        File.WriteAllText(repo.Combine("App.sln"), string.Join(
            "\r\n",
            "Microsoft Visual Studio Solution File, Format Version 12.00",
            @"Project(""{9A19103F-16F7-4668-BE54-9A1E7A4F7556}"") = ""Api"", ""src\Api\Api.csproj"", ""{11111111-1111-1111-1111-111111111111}""",
            "EndProject",
            @"Project(""{9A19103F-16F7-4668-BE54-9A1E7A4F7556}"") = ""Lib"", ""src\Lib\Lib.csproj"", ""{22222222-2222-2222-2222-222222222222}""",
            "EndProject",
            @"Project(""{2150E333-8FDC-42A3-9474-1A3956D46DE8}"") = ""docs"", ""docs"", ""{33333333-3333-3333-3333-333333333333}""",
            "EndProject"));

        var projects = DotNetProjects.Find(repo.Path);

        Assert.Equal([new DotNetProject(api, "Api", ApiId)], projects);
    }

    [Fact]
    public void XmlSolutionIsRead()
    {
        var worker = Project("src/Worker/Worker.csproj", WorkerId);
        Project("src/Api/Api.csproj", ApiId);
        File.WriteAllText(repo.Combine("App.slnx"), """
            <Solution>
              <Folder Name="/src/">
                <Project Path="src/Worker/Worker.csproj" />
              </Folder>
            </Solution>
            """);

        Assert.Equal([new DotNetProject(worker, "Worker", WorkerId)], DotNetProjects.Find(repo.Path));
    }

    [Fact]
    public void WithoutSolutionEveryProjectOutsideBuildAndDotFoldersCounts()
    {
        var api = Project("src/Api/Api.csproj", ApiId);
        Project("src/Api/bin/Debug/Copy.csproj", "bin-id");
        Project("src/Api/obj/Copy.csproj", "obj-id");
        Project(".vs/Hidden/Hidden.csproj", "hidden-id");

        Assert.Equal([new DotNetProject(api, "Api", ApiId)], DotNetProjects.Find(repo.Path));
    }

    [Fact]
    public void NearestBuildPropsSuppliesTheId()
    {
        var api = Project("src/Api/Api.csproj", null);
        Directory.CreateDirectory(repo.Combine("src"));
        File.WriteAllText(repo.Combine("src", DotNetProjects.PropsFileName), Props(WorkerId));
        File.WriteAllText(repo.Combine(DotNetProjects.PropsFileName), Props("outer-id"));

        Assert.Equal(WorkerId, DotNetProjects.IdOf(api, repo.Path));
    }

    [Fact]
    public void PropertyExpressionIsRefusedNamingTheFile()
    {
        var api = Project("src/Api/Api.csproj", "$(MSBuildProjectName)-secrets");

        var error = Assert.Throws<VaultException>(() => DotNetProjects.Find(repo.Path));

        Assert.Contains(api, error.Message, StringComparison.Ordinal);
        Assert.Contains("literally", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"..\..\Roaming")]
    [InlineData("..")]
    [InlineData("a b")]
    public void IdThatCouldLeaveTheSecretsFolderIsRefused(string id)
    {
        var api = Project("src/Api/Api.csproj", id);

        Assert.Throws<VaultException>(() => DotNetProjects.IdOf(api, repo.Path));
    }

    private string Project(string relative, string? id)
    {
        var path = repo.Combine(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, id is null
            ? """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>"""
            : $"""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><UserSecretsId>{System.Security.SecurityElement.Escape(id)}</UserSecretsId></PropertyGroup></Project>""");
        return path;
    }

    private static string Props(string id) => $"<Project><PropertyGroup><UserSecretsId>{id}</UserSecretsId></PropertyGroup></Project>";
}

public sealed class UserSecretsFileTests : IDisposable
{
    private readonly TempDirectory directory = new();

    public void Dispose() => directory.Dispose();

    [Fact]
    public void ReadFlattensNestedObjectsAndAcceptsCommentsAndTrailingCommas()
    {
        var path = directory.Combine("secrets.json");
        File.WriteAllText(path, """
            {
              // set by hand
              "ConnectionStrings": { "Default": "db-value-1" },
              "Hosts": [ "a", "b", ],
              "Port": 8080,
            }
            """);

        var values = UserSecretsFile.Read(path);

        Assert.Equal(["ConnectionStrings:Default", "Hosts:0", "Hosts:1", "Port"], values.Keys);
        Assert.Equal("db-value-1", UserSecretsFile.Text(values["connectionstrings:default"]));
        Assert.Equal("8080", UserSecretsFile.Text(values["Port"]));
    }

    [Fact]
    public void CompareIgnoresKeyCaseAndApplyKeepsOtherKeys()
    {
        var current = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ConnectionStrings:Default"] = JsonValue.Create("old-value"),
            ["Mine"] = JsonValue.Create("hand-set"),
            ["Same"] = JsonValue.Create("same-value"),
        };
        var wanted = new Dictionary<string, string>
        {
            ["connectionstrings:default"] = "new-value",
            ["Same"] = "same-value",
            ["Added"] = "added-value",
        };

        var comparison = UserSecretsFile.Compare(current, wanted);
        var result = UserSecretsFile.Apply(current, wanted);

        Assert.Equal(["Added"], comparison.Absent);
        Assert.Equal(["connectionstrings:default"], comparison.Differing);
        Assert.Equal(["Same"], comparison.Same);
        Assert.Equal(["ConnectionStrings:Default", "Mine", "Same", "Added"], result.Keys);
        Assert.Equal("new-value", UserSecretsFile.Text(result["ConnectionStrings:Default"]));
        Assert.Equal("hand-set", UserSecretsFile.Text(result["Mine"]));
    }

    [Fact]
    public void WriteIsFlatAndAnEmptyFileIsDeletedWithItsFolder()
    {
        var path = directory.Combine("id-1", UserSecretsFile.FileName);
        var values = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase) { ["A:B"] = JsonValue.Create("value-1") };

        UserSecretsFile.Write(path, values);

        Assert.Equal("value-1", UserSecretsFile.Text(UserSecretsFile.Read(path)["A:B"]));
        Assert.Contains("\"A:B\"", File.ReadAllText(path), StringComparison.Ordinal);
        UserSecretsFile.Write(path, new Dictionary<string, JsonNode?>());
        Assert.False(Directory.Exists(directory.Combine("id-1")));
    }

    [Fact]
    public void InvalidJsonIsRefused()
    {
        var path = directory.Combine("secrets.json");
        File.WriteAllText(path, "[1, 2]");

        Assert.Throws<VaultException>(() => UserSecretsFile.Read(path));
    }
}

public sealed class ProjectUserSecretsTests : IDisposable
{
    private readonly TempDirectory repo = TempDirectory.GitRepository();

    public void Dispose() => repo.Dispose();

    [Fact]
    public void TrackingIsKeptPerIdAndVault()
    {
        var main = VaultLocation.Resolve(null, null, repo.Path);
        var infra = VaultLocation.Resolve(repo.Combine("infra", "secrets.local.lv"), null, repo.Path);

        ProjectFile.TrackUserSecret(main, "api-1", "ConnectionStrings:Default", "db");
        ProjectFile.TrackUserSecret(main, "api-1", "connectionstrings:default", "db#password");
        ProjectFile.TrackUserSecret(infra, "api-1", "Smtp:Password", "smtp");

        var tracking = ProjectFile.ReadUserSecrets(main);
        Assert.Equal(2, tracking.Count);
        Assert.Equal(main.Path, tracking[0].Vault);
        Assert.Equal("db#password", tracking[0].Keys["ConnectionStrings:Default"]);
        Assert.Equal(infra.Path, tracking[1].Vault);
        var json = File.ReadAllText(ProjectFile.PathFor(main)!);
        Assert.Contains("\"vault\": \"infra/secrets.local.lv\"", json, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"vault\""));

        Assert.True(ProjectFile.UntrackUserSecret(main, "API-1", "ConnectionStrings:Default"));
        Assert.False(ProjectFile.UntrackUserSecret(main, "api-1", "ConnectionStrings:Default"));
        Assert.Equal([infra.Path], ProjectFile.ReadUserSecrets(main).Select(t => t.Vault));
    }
}
