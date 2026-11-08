// ============================================================================
//  Spidey Companion - single-file installer for v1.3.0.
//  Self-built stand-in for the Inno Setup installer (same behaviour):
//  installs per-user to %LOCALAPPDATA%\SpideyCompanion, no admin, writes
//  the autostart Run value the self-updater reads back, adds Start-menu
//  shortcuts and an uninstall entry (this same exe, copied in as
//  unins000.exe and run with /uninstall, removes everything).
//  Payload (app exe + config, docs, full source) is embedded as resources.
// ============================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class SetupProgram
{
    private const string VersionExe = "SpideyCompanion-v1.3.1.exe";
    private const string AppId = "{0133FFD4-30BD-421B-BB17-87FA57E877C5}";
    private const string DisplayName = "Spidey Companion";
    private const string Version = "1.3.1";

    private static string InstallDir
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpideyCompanion"); }
    }

    private static string GroupDir
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Spidey Companion");
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        bool uninstall = false, silent = false;
        foreach (string a in args)
        {
            if (string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase)) uninstall = true;
            if (string.Equals(a, "/silent", StringComparison.OrdinalIgnoreCase)) silent = true;
        }
        try
        {
            if (uninstall) { DoUninstall(); return 0; }
            RunFlow(silent);
            return 0;
        }
        catch (Exception ex)
        {
            if (!silent) MessageBox.Show(ex.Message, "Spidey Companion Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    // ------------------------------------------------- version-aware flow
    //  Relaunching this installer on a machine that already has Spidey
    //  Companion offers Update or Uninstall. "Latest" is read from the
    //  project's GitHub (version.txt in the repo); when a newer release
    //  exists, Update downloads that release's Setup from GitHub
    //  Releases and hands over to it. Offline or missing release?
    //  Everything degrades to installing the embedded copy.

    private const string RepoRaw = "https://raw.githubusercontent.com/DaddyKing816/SpideyCompanion/main/";
    private const string RepoDl = "https://github.com/DaddyKing816/SpideyCompanion/releases/download/";

    private static string InstalledVersion()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId))
            {
                if (k == null) return null;
                string v = k.GetValue("DisplayVersion") as string;
                return string.IsNullOrEmpty(v) ? null : v.Trim();
            }
        }
        catch { return null; }
    }

    private static string FetchLatestVersion()
    {
        try
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(RepoRaw + "version.txt");
            req.Timeout = 4000;
            req.ReadWriteTimeout = 4000;
            req.UserAgent = "SpideyCompanion-Setup";
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream()))
            {
                string t = sr.ReadToEnd().Trim();
                System.Version v;
                return System.Version.TryParse(t, out v) ? v.ToString() : null;
            }
        }
        catch { return null; }
    }

    private static bool TryOnlineSetup(string version)
    {
        try
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string file = "SpideyCompanion-Setup-v" + version + ".exe";
            string dest = Path.Combine(Path.GetTempPath(), file);
            using (var wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "SpideyCompanion-Setup";
                wc.DownloadFile(RepoDl + "v" + version + "/" + file, dest);
            }
            Process.Start(dest);
            return true;
        }
        catch { return false; }
    }

    private static int ShowOptions(string title, string text, params string[] buttons)
    {
        using (var f = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            Width = 450,
            Height = 195,
            TopMost = true
        })
        {
            var lbl = new Label { Text = text, Left = 16, Top = 14, Width = 405, Height = 72 };
            f.Controls.Add(lbl);
            int result = -1;
            int x = 16;
            for (int i = 0; i < buttons.Length; i++)
            {
                int idx = i;
                var b = new Button { Text = buttons[i], Left = x, Top = 108, Width = 128, Height = 30 };
                b.Click += (s, e) => { result = idx; f.Close(); };
                f.Controls.Add(b);
                x += 136;
            }
            f.ShowDialog();
            return result;
        }
    }

    private static void RunFlow(bool silent)
    {
        string installed = InstalledVersion();
        if (installed == null || silent) { DoInstall(silent); return; }

        System.Version iv, ev;
        System.Version.TryParse(installed, out iv);
        System.Version.TryParse(Version, out ev);
        string latest = FetchLatestVersion();
        System.Version lv = null;
        bool hasLatest = latest != null && System.Version.TryParse(latest, out lv);
        bool outdated = hasLatest && iv != null && lv > iv;

        if (outdated)
        {
            int c = ShowOptions("Spidey Companion Setup",
                "An update is available.\n\nInstalled: v" + installed + "\nLatest:    v" + latest,
                "Update", "Uninstall", "Cancel");
            if (c == 0)
            {
                if (lv == ev) { DoInstall(false); return; }
                if (TryOnlineSetup(latest)) return;
                MessageBox.Show("Couldn't download the update, so this installer's copy (v" + Version + ") will be installed instead.",
                    "Spidey Companion Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DoInstall(false);
            }
            else if (c == 1)
            {
                DoUninstall();
                MessageBox.Show("Spidey Companion has been uninstalled.", "Spidey Companion Setup",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }

        int c2 = ShowOptions("Spidey Companion Setup",
            "Spidey Companion v" + installed + " is already installed" +
            (hasLatest ? " and up to date." : ".\n(Couldn't check online for a newer version.)"),
            "Reinstall", "Uninstall", "Cancel");
        if (c2 == 0) DoInstall(false);
        else if (c2 == 1)
        {
            DoUninstall();
            MessageBox.Show("Spidey Companion has been uninstalled.", "Spidey Companion Setup",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // ------------------------------------------------------------------ util

    private static void KillRunning()
    {
        string dir = InstallDir;
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                string path = p.MainModule.FileName;
                if (path.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
            }
            catch { /* not ours or already gone */ }
        }
    }

    private static void Extract(string suffix, string destPath, bool overwrite)
    {
        if (File.Exists(destPath) && !overwrite) return;
        Assembly asm = Assembly.GetExecutingAssembly();
        string full = null;
        foreach (string n in asm.GetManifestResourceNames())
            if (n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { full = n; break; }
        if (full == null) throw new InvalidOperationException("Missing embedded file: " + suffix);
        using (Stream s = asm.GetManifestResourceStream(full))
        using (var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write))
            s.CopyTo(fs);
    }

    private static void MakeShortcut(string linkPath, string target, string workDir)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath));
        Type t = Type.GetTypeFromProgID("WScript.Shell");
        dynamic shell = Activator.CreateInstance(t);
        dynamic sc = shell.CreateShortcut(linkPath);
        sc.TargetPath = target;
        sc.WorkingDirectory = workDir;
        sc.Save();
        Marshal.ReleaseComObject(sc);
        Marshal.ReleaseComObject(shell);
    }

    // --------------------------------------------------------------- install

    private static void DoInstall(bool silent)
    {
        string dir = InstallDir;
        KillRunning();
        Directory.CreateDirectory(dir);

        Extract(VersionExe, Path.Combine(dir, VersionExe), true);
        Extract(VersionExe + ".config", Path.Combine(dir, VersionExe + ".config"), true);
        Extract("README.txt", Path.Combine(dir, "README.txt"), true);
        Extract("update-feed.txt", Path.Combine(dir, "update-feed.txt"), false);
        Extract("Program.cs", Path.Combine(dir, "Program.cs"), true);
        Extract("Buddy.cs", Path.Combine(dir, "Buddy.cs"), true);

        // Remove older versioned exes left by previous installs.
        foreach (string f in Directory.GetFiles(dir, "SpideyCompanion-v*.exe"))
            if (!string.Equals(Path.GetFileName(f), VersionExe, StringComparison.OrdinalIgnoreCase))
                try { File.Delete(f); File.Delete(f + ".config"); } catch { }
        try { File.Delete(Path.Combine(dir, "SpiderCompanion.exe")); } catch { }

        // Copy ourselves in as the uninstaller.
        string unins = Path.Combine(dir, "unins000.exe");
        File.Copy(Assembly.GetExecutingAssembly().Location, unins, true);

        // Autostart: the app self-updater reads this value back to find the
        // exe name - the quoted-path format must stay exactly like this.
        using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            run.SetValue("SpideyCompanion", "\"" + Path.Combine(dir, VersionExe) + "\"");

        // Uninstall entry for Windows Settings.
        using (RegistryKey un = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId))
        {
            un.SetValue("DisplayName", DisplayName);
            un.SetValue("DisplayVersion", Version);
            un.SetValue("Publisher", "Bailey Powell");
            un.SetValue("DisplayIcon", Path.Combine(dir, VersionExe));
            un.SetValue("InstallLocation", dir);
            un.SetValue("UninstallString", "\"" + unins + "\" /uninstall");
            un.SetValue("NoModify", 1, RegistryValueKind.DWord);
            un.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }

        MakeShortcut(Path.Combine(GroupDir, "Spidey Companion.lnk"), Path.Combine(dir, VersionExe), dir);
        MakeShortcut(Path.Combine(GroupDir, "Read Me.lnk"), Path.Combine(dir, "README.txt"), dir);

        if (!silent)
        {
            MessageBox.Show("Spidey Companion 1.3.1 is installed and running.\n\nThe desktop buddy is OFF by default - " +
                            "right-click the Spidey tray icon and tick 'Show buddy' to bring him out.",
                            "Spidey Companion Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Process.Start(Path.Combine(dir, VersionExe));
        }
    }

    // ------------------------------------------------------------- uninstall

    private static void DoUninstall()
    {
        string dir = InstallDir;
        KillRunning();

        using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            run.DeleteValue("SpideyCompanion", false);
        Registry.CurrentUser.DeleteSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId, false);

        try { Directory.Delete(GroupDir, true); } catch { }

        foreach (string name in new[] { VersionExe, VersionExe + ".config", "README.txt", "Program.cs", "Buddy.cs" })
            try { File.Delete(Path.Combine(dir, name)); } catch { }
        foreach (string f in Directory.GetFiles(dir, "SpideyCompanion-v*.exe"))
            try { File.Delete(f); File.Delete(f + ".config"); } catch { }

        // We are running from inside the folder: hand the final cleanup to
        // cmd so the uninstaller can delete itself and the folder.
        string cmd = "timeout /t 2 /nobreak >nul & del /f /q \"" + Path.Combine(dir, "unins000.exe") + "\" & rmdir \"" + dir + "\"";
        Process.Start(new ProcessStartInfo("cmd.exe", "/c " + cmd) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
    }
}
