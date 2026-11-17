using System.Collections;
using System.Management.Automation;
using System.Security;
using LocalVault.Core;
using LocalVault.Engine;

namespace LocalVault.PowerShell;

internal sealed class ClixmlLegacyReader : ILegacyVaultReader
{
    public IReadOnlyList<KeyValuePair<string, string>> Read(string path)
    {
        object deserialized;
        try
        {
            deserialized = PSSerializer.Deserialize(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is PSInvalidOperationException or System.Xml.XmlException or System.Security.Cryptography.CryptographicException)
        {
            throw new VaultException($"'{path}' cannot be read by this Windows account on this machine.", ex);
        }

        var table = (deserialized is PSObject wrapper ? wrapper.BaseObject : deserialized) as IDictionary
            ?? throw new VaultException($"'{path}' does not hold a table of secrets.");

        var values = new List<KeyValuePair<string, string>>();
        foreach (DictionaryEntry item in table)
        {
            var name = item.Key?.ToString() ?? string.Empty;
            var value = (item.Value is PSObject inner ? inner.BaseObject : item.Value) switch
            {
                SecureString secure => SecretText.ToPlain(secure),
                string text => text,
                null => string.Empty,
                _ => throw new VaultException($"'{path}': value of '{name}' is not a secret."),
            };
            values.Add(new KeyValuePair<string, string>(name, value));
        }

        return values;
    }
}
