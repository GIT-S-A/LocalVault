namespace LocalVault.Core;

public static class StoreGroupPath
{
    public const string Root = "LocalVault";

    public static string Display(IReadOnlyList<string> segments) => string.Join('/', segments);
}
