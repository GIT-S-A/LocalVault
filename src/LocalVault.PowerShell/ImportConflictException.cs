using LocalVault.Core;

namespace LocalVault.PowerShell;

public sealed class ImportConflictException : VaultException
{
    public ImportConflictException()
    {
    }

    public ImportConflictException(string message)
        : base(message)
    {
    }

    public ImportConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
