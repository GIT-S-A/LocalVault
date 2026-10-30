using LocalVault.Core;

namespace LocalVault.PowerShell;

public sealed class StoreNotDefinedException : VaultException
{
    public StoreNotDefinedException()
    {
    }

    public StoreNotDefinedException(string message)
        : base(message)
    {
    }

    public StoreNotDefinedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
