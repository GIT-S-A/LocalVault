using System.Security;
using LocalVault.Core;

namespace LocalVault.PowerShell;

internal interface IVaultHost
{
    bool CanPrompt { get; }

    bool CanConfirm { get; }

    DateTimeOffset Clock { get; }

    void Tell(string message);

    void Warn(string message);

    SecureString AskSecret(string prompt);

    bool AskYesNo(string question);

    void ReportWrite(VaultWriteResult result);

    HelloEntries Hello { get; }
}
