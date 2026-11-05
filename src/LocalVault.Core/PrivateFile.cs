using System.Security.AccessControl;
using System.Security.Principal;

namespace LocalVault.Core;

public static class PrivateFile
{
    public static void CreateFolder(string folder) => new DirectoryInfo(folder).Create(FolderSecurity());

    public static void Write(string path, ReadOnlySpan<byte> content)
    {
        var folder = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(folder))
        {
            CreateFolder(folder);
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using var stream = new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, FileSecurity());
        stream.Write(content);
    }

    private static FileSecurity FileSecurity()
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var owner in Owners())
        {
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
        }

        return security;
    }

    private static DirectorySecurity FolderSecurity()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var owner in Owners())
        {
            security.AddAccessRule(new FileSystemAccessRule(
                owner, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }

        return security;
    }

    private static IEnumerable<SecurityIdentifier> Owners()
    {
        using var identity = WindowsIdentity.GetCurrent();
        yield return identity.User!;
        yield return new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        yield return new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
    }
}
