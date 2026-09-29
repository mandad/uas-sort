using System.Security.AccessControl;
using System.Security.Principal;

namespace UasSort.Platform.Tests;

/// <summary>%TEMP%\uas-sort-test-&lt;guid&gt;, deleted on Dispose (Global Constraints: temp artefacts only there).</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Join(System.IO.Path.GetTempPath(), "uas-sort-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Sub(params string[] parts)
    {
        var p = System.IO.Path.Join([Path, .. parts]);
        Directory.CreateDirectory(p);
        return p;
    }

    public string File(string relPath, int bytes = 16, FileAttributes attributes = FileAttributes.Normal)
    {
        var p = System.IO.Path.Join(Path, relPath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
        var data = new byte[bytes];
        new Random(relPath.Length + bytes).NextBytes(data);
        System.IO.File.WriteAllBytes(p, data);
        if (attributes != FileAttributes.Normal) System.IO.File.SetAttributes(p, attributes);
        return p;
    }

    /// <summary>Adds a Deny ACE for the current user; Dispose removes every Deny ACE it finds.</summary>
    public static void Deny(string path, FileSystemRights rights)
    {
        var me = WindowsIdentity.GetCurrent().User!;
        if (Directory.Exists(path))
        {
            var d = new DirectoryInfo(path);
            var acl = d.GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(me, rights, AccessControlType.Deny));
            d.SetAccessControl(acl);
        }
        else
        {
            var f = new FileInfo(path);
            var acl = f.GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(me, rights, AccessControlType.Deny));
            f.SetAccessControl(acl);
        }
    }

    public void Dispose()
    {
        if (!Directory.Exists(Path)) return;
        foreach (var e in new DirectoryInfo(Path).EnumerateFileSystemInfos("*", new EnumerationOptions
                 { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true }).Prepend(new DirectoryInfo(Path)))
        {
            try { ClearDeny(e); } catch (UnauthorizedAccessException) { }
            try { if ((e.Attributes & FileAttributes.ReadOnly) != 0) e.Attributes &= ~FileAttributes.ReadOnly; } catch (IOException) { }
        }
        // a second pass reaches folders that were unlistable before their deny ACE was removed
        foreach (var d in Directory.EnumerateDirectories(Path, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            try { ClearDeny(new DirectoryInfo(d)); } catch (UnauthorizedAccessException) { }
        Directory.Delete(Path, recursive: true);
    }

    private static void ClearDeny(FileSystemInfo e)
    {
        if (e is DirectoryInfo d)
        {
            var acl = d.GetAccessControl();
            foreach (FileSystemAccessRule r in acl.GetAccessRules(true, false, typeof(SecurityIdentifier)))
                if (r.AccessControlType == AccessControlType.Deny) acl.RemoveAccessRuleSpecific(r);
            d.SetAccessControl(acl);
        }
        else if (e is FileInfo f)
        {
            var acl = f.GetAccessControl();
            foreach (FileSystemAccessRule r in acl.GetAccessRules(true, false, typeof(SecurityIdentifier)))
                if (r.AccessControlType == AccessControlType.Deny) acl.RemoveAccessRuleSpecific(r);
            f.SetAccessControl(acl);
        }
    }
}
