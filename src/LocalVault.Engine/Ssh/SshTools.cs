namespace LocalVault.Engine;

public sealed record SshTools(string Directory, bool IsDefault)
{
    public const string Ssh = "ssh.exe";
    public const string KeyGen = "ssh-keygen.exe";
    public const string Add = "ssh-add.exe";
    public const string Scp = "scp.exe";
    public const string InstallCommand = "Add-WindowsCapability -Online -Name OpenSSH.Client~~~~0.0.1.0";

    private static readonly string[] Required = [Ssh, KeyGen, Add];

    public static string WindowsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH");

    public string SshPath => Path.Combine(Directory, Ssh);

    public string KeyGenPath => Path.Combine(Directory, KeyGen);

    public string AddPath => Path.Combine(Directory, Add);

    public string ScpPath => Path.Combine(Directory, Scp);

    public static SshTools Find(PcSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SshDir is { } configured)
        {
            if (!Path.IsPathFullyQualified(configured))
            {
                throw new VaultException($"sshDir '{configured}' in {settings.SettingsPath} is not an absolute folder. Fix it with lv ssh-key tools <dir>.");
            }

            var missing = Required.Where(tool => !File.Exists(Path.Combine(configured, tool))).ToList();
            if (missing.Count > 0)
            {
                throw new VaultException($"sshDir '{configured}' in {settings.SettingsPath} has no {string.Join(", ", missing)}. Fix it with lv ssh-key tools <dir>, or lv ssh-key tools -Default.");
            }

            return new SshTools(configured, IsDefault: false);
        }

        var windows = WindowsDirectory;
        if (Required.Any(tool => !File.Exists(Path.Combine(windows, tool))))
        {
            throw new VaultException($"Windows OpenSSH client not installed: {InstallCommand}");
        }

        return new SshTools(windows, IsDefault: true);
    }
}
