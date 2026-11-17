namespace LocalVault.Core;


public class HelloException : VaultException
{
    public HelloException()
    {
    }

    public HelloException(string message)
        : base(message)
    {
    }

    public HelloException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class HelloCancelledException : HelloException
{
    public HelloCancelledException()
        : base("Windows Hello was cancelled.")
    {
    }

    public HelloCancelledException(string message)
        : base(message)
    {
    }

    public HelloCancelledException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
