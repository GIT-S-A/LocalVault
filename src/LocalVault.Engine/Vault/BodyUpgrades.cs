using System.Text.Json.Nodes;

namespace LocalVault.Engine;

internal static class BodyUpgrades
{
    public static IReadOnlyDictionary<int, Action<JsonObject>> Steps { get; } = new Dictionary<int, Action<JsonObject>>
    {
        [2] = _ => { },
        [3] = _ => { },
        [4] = _ => { },
    };
}
