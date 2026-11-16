using LocalVault.Core;

namespace LocalVault.PowerShell;

internal sealed class StorePrompt(IVaultHost host, string storeName, UnlockTarget? kept = null) : IStorePrompt, IDisposable
{
    private readonly List<char[]> answers = [];
    private bool keptGiven;

    public string StoreName => storeName;

    public ReadOnlySpan<char> FirstAnswer => answers.Count > 0 ? answers[0] : default;

    public ReadOnlySpan<char> AskSecret(string question)
    {
        if (kept is not null && !keptGiven)
        {
            keptGiven = true;
            return kept.Password;
        }

        if (!host.CanPrompt)
        {
            throw new VaultException($"{question}: there is no console to ask on.");
        }

        using var secret = host.AskSecret(question);
        var answer = SecretText.ToChars(secret);
        answers.Add(answer);
        return answer;
    }

    public bool Confirm(string question) => host.CanConfirm && host.AskYesNo(question);

    public void Tell(string message) => host.Tell(message);

    public void Dispose()
    {
        foreach (var answer in answers)
        {
            Array.Clear(answer);
        }

        answers.Clear();
        kept?.Dispose();
    }
}
