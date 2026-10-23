using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace MishkaHelper.Core;

/// <summary>
/// Where the app keeps its files. The app runs as administrator, so anything it reads commands from (settings with
/// scenarios/launchers) or executes (llama-server, Piper, RustDesk, update installers) must not be writable by
/// ordinary programs — otherwise any of them could get admin rights without a UAC prompt. Hence
/// %ProgramData%\MishkaHelper with "write: Administrators/SYSTEM only". Non-elevated dev copies use the per-user folders.
/// </summary>
public static class AppPaths
{
    public static readonly bool Elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    // MISHKA_PATHS_TEST=<dir>: self-test of the migration on throwaway folders instead of the real ones.
    static readonly string? Test = Environment.GetEnvironmentVariable("MISHKA_PATHS_TEST");
    static readonly string Root = Test != null ? Path.Combine(Test, "programdata") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MishkaHelper");
    static readonly string OldData = Test != null ? Path.Combine(Test, "appdata") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MishkaHelper");
    static readonly string OldShared = Test != null ? Path.Combine(Test, "localappdata") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MishkaHelper");

    /// <summary>Per-user settings, notes, logs.</summary>
    public static readonly string Data;
    /// <summary>Models, runtimes, downloads that get executed.</summary>
    public static readonly string Shared;
    /// <summary>Problems met while securing the folders (logged later — Log itself depends on Data).</summary>
    public static readonly List<string> Notes = new();

    static AppPaths()
    {
        if (!Elevated)
        {
            Data = OldData;
            Shared = Directory.Exists(Path.Combine(Root, "models")) ? Root : OldShared;   // dev copies can still read the installed models
            return;
        }
        try
        {
            var data = Path.Combine(Root, "users", WindowsIdentity.GetCurrent().User!.Value);
            Secure();
            Directory.CreateDirectory(data);
            Migrate(data);
            (Data, Shared) = (data, Root);
        }
        catch (Exception e)
        {
            // Never lose settings over this: keep working from the old folders and say why in the log.
            (Data, Shared) = (OldData, OldShared);
            Notes.Add("AppPaths: protected folder failed, using per-user folders: " + e.Message);
        }
    }

    static readonly SecurityIdentifier Admins = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    static readonly SecurityIdentifier System_ = new(WellKnownSidType.LocalSystemSid, null);
    static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    /// <summary>Create the root with an admin-only-write ACL; a folder someone else made first is moved aside, not trusted.</summary>
    static void Secure()
    {
        if (Directory.Exists(Root))
        {
            var owner = new DirectoryInfo(Root).GetAccessControl().GetOwner(typeof(SecurityIdentifier));
            if (owner != Admins && owner != System_)
            {
                var aside = Root + ".untrusted-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                Directory.Move(Root, aside);
                Notes.Add($"AppPaths: {Root} was created by {owner}, moved to {aside}");
            }
        }
        var sec = new DirectorySecurity();
        sec.SetOwner(Admins);
        sec.SetAccessRuleProtection(true, false);   // no inherited ProgramData rules (they let Users create files)
        const InheritanceFlags all = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        sec.AddAccessRule(new FileSystemAccessRule(Admins, FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(System_, FileSystemRights.FullControl, all, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, all, PropagationFlags.None, AccessControlType.Allow));
        if (!Directory.Exists(Root)) new DirectoryInfo(Root).Create(sec);
        else new DirectoryInfo(Root).SetAccessControl(sec);
    }

    /// <summary>One-time move from the old per-user folders. Moved files keep their old ACLs, so they are reset to inherit ours.</summary>
    static void Migrate(string data)
    {
        bool moved = false;
        if (!File.Exists(Path.Combine(data, "settings.json")) && File.Exists(Path.Combine(OldData, "settings.json")))
        {
            foreach (var f in Directory.GetFiles(OldData))
                File.Copy(f, Path.Combine(data, Path.GetFileName(f)), true);
            moved = true;
        }
        foreach (var sub in new[] { "models", "rustdesk" })
        {
            string from = Path.Combine(OldShared, sub), to = Path.Combine(Root, sub);
            if (!Directory.Exists(from) || Directory.Exists(to)) continue;
            try { Directory.Move(from, to); }   // same drive: instant even for gigabytes
            catch (IOException)
            {
                // Other drive or a file in use: copy under a temp name, so a half copy never looks complete.
                var part = to + ".part";
                if (Directory.Exists(part)) Directory.Delete(part, true);
                CopyTree(from, part);
                Directory.Move(part, to);
                try { Directory.Delete(from, true); } catch { }
            }
            moved = true;
        }
        if (moved) ResetAcl(Root);
    }

    static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var d in Directory.GetDirectories(from, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, d)));
        foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories)) File.Copy(f, Path.Combine(to, Path.GetRelativePath(from, f)), true);
    }

    /// <summary>icacls /reset: every file below inherits only the root's rules.
    /// Not via Shell.Run: its error path logs, and Log needs Data, which is being initialised right now.</summary>
    static void ResetAcl(string dir)
    {
        try   // must not throw: the models are already moved by now, falling back to the old folders would lose them
        {
            using var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "icacls.exe"), $"\"{dir}\\*\" /reset /T /C /Q")
                { UseShellExecute = false, CreateNoWindow = true });
            p?.WaitForExit(120_000);
        }
        catch (Exception e) { Notes.Add("AppPaths: icacls failed: " + e.Message); }
    }
}
