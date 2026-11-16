using LocalVault.Core;

namespace LocalVault.PowerShell;

internal sealed record PersonalTarget(IStore Vault, StoreDefinition Store, bool WithDeclaration);

internal sealed class PersonalStore(IVaultHost host, StoreAccess access)
{
    private bool failed;
    private bool? shareWithPrimary;

    public StoreDefinition? Definition { get; } = access.Registry.Personal;

    public bool Is(StoreResolution resolution) => Definition is not null && SameStore(resolution, Definition);

    public PersonalTarget? Open(StoreResolution primary, string? primaryDatabaseId = null)
    {
        ArgumentNullException.ThrowIfNull(primary);
        if (Definition is null || failed)
        {
            return null;
        }

        (IStore Vault, StoreDefinition Store) opened;
        try
        {
            opened = access.For(new StoreResolution(primary.Vault, new StoreReference(Definition.Name, Definition.Id), Definition, null));
        }
        catch (Exception ex) when (ex is VaultException or IOException or UnauthorizedAccessException)
        {
            failed = true;
            host.Warn($"Personal store '{Definition.Name}' not reached: {ex.Message} Your own values stay on this PC.");
            return null;
        }

        var primaryId = primaryDatabaseId ?? primary.DatabaseId;
        var sharesPrimary = (primaryId is not null && string.Equals(primaryId, opened.Vault.DatabaseId, StringComparison.OrdinalIgnoreCase))
            || SameStore(primary, Definition);
        if (sharesPrimary && !(shareWithPrimary ??= AskShare()))
        {
            return null;
        }

        return new PersonalTarget(opened.Vault, opened.Store, sharesPrimary);
    }

    private bool AskShare()
    {
        var question = $"Personal store '{Definition!.Name}' is also the project's store: your own values would be readable by everyone who opens it.";
        if (host.CanConfirm && host.AskYesNo(question + " Keep them there anyway?"))
        {
            return true;
        }

        host.Warn(question + " Your own values were left out of it.");
        return false;
    }

    private static bool SameStore(StoreResolution resolution, StoreDefinition store) =>
        (resolution.DatabaseId is { } id && store.Id is { } other && string.Equals(id, other, StringComparison.OrdinalIgnoreCase))
        || (resolution.Definition is { } definition
            && string.Equals(Path.GetFullPath(definition.ExpandedTarget), Path.GetFullPath(store.ExpandedTarget), StringComparison.OrdinalIgnoreCase));
}
