namespace LocalVault.Engine;

public sealed class HelloEntries(IHelloKey key) : IDisposable
{
    private readonly Dictionary<string, HelloSeal.HelloSession> sessions = new(StringComparer.Ordinal);
    private bool warnedSoftware;

    public Action? BeforePrompt { get; init; }

    public Action<string>? Warn { get; init; }

    public string? MissingReason(VaultBody body, VaultEntry entry) => HelloSeal.MissingReason(body, entry, key);

    public IReadOnlyDictionary<EntryName, VaultEntry> Unseal(VaultBody body, IEnumerable<VaultEntry> entries, string action, string vault)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(entries);
        var sealedEntries = entries.Where(e => e.IsHello).DistinctBy(e => e.Name.FullName, EntryName.Comparer).ToList();
        var plain = new Dictionary<EntryName, VaultEntry>();
        if (sealedEntries.Count == 0)
        {
            return plain;
        }

        foreach (var entry in sealedEntries)
        {
            if (MissingReason(body, entry) is { } reason)
            {
                throw new HelloException($"'{entry.Name}' can't be read on this PC: {reason}. lv load restores it from the store.");
            }
        }

        var sessionKey = Convert.ToBase64String(body.HelloKey!.PublicKey);
        if (!sessions.TryGetValue(sessionKey, out var session))
        {
            BeforePrompt?.Invoke();
            var listed = string.Join(", ", sealedEntries.Select(e => e.Name.FullName));
            session = HelloSeal.Open(body, key, $"LocalVault: {action} {listed} ({vault})");
            sessions[sessionKey] = session;
        }

        foreach (var entry in sealedEntries)
        {
            plain[entry.Name] = session.Unseal(entry);
        }

        return plain;
    }

    public VaultEntry Seal(VaultBody body, VaultEntry plain)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(plain);
        if (key.State == HelloState.None)
        {
            BeforePrompt?.Invoke();
        }

        var result = HelloSeal.Seal(body, plain, key, $"LocalVault: create the Windows Hello key for {Environment.UserName}");
        if (!warnedSoftware && !key.IsHardware)
        {
            warnedSoftware = true;
            Warn?.Invoke($"Windows Hello key is software-only (no TPM): {key.ProviderName}.");
        }

        return result;
    }

    public void Dispose()
    {
        foreach (var session in sessions.Values)
        {
            session.Dispose();
        }

        sessions.Clear();
    }
}
