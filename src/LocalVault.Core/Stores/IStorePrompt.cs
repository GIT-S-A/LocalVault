namespace LocalVault.Core;

public interface IStorePrompt
{
    string StoreName { get; }

    // The span stays valid until the prompt is disposed, which clears it.
    ReadOnlySpan<char> AskSecret(string question);

    bool Confirm(string question);

    void Tell(string message);
}
