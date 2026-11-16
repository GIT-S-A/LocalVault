namespace LocalVault.Core;

public sealed class UnlockTarget(string title, string path, string? keyFile, char[] password) : IDisposable
{
    public string Title => title;

    public string Path => path;

    public string? KeyFile => keyFile;

    public ReadOnlySpan<char> Password => password;

    public void Dispose() => Array.Clear(password);
}
