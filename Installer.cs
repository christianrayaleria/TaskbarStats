using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

// The exe installs itself: run it from anywhere and it copies to Program Files,
// registers in Apps (with uninstall), adds a Start menu shortcut and optional startup task.
static class Installer
{
    const string Name = "TaskbarStats";
    const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + Name;
    const string PawnKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Name);
    static readonly string Exe = Path.Combine(Dir, Name + ".exe");
    static readonly string Shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Name + ".lnk");

    public static bool IsInstalledCopy => string.Equals(Environment.ProcessPath, Exe, StringComparison.OrdinalIgnoreCase);

    public static bool StartWithWindows
    {
        get => Run("schtasks.exe", $"/Query /TN {Name}") == 0;
        set { if (value) RegisterTask(); else Run("schtasks.exe", $"/Delete /TN {Name} /F"); }
    }

    public static void RunSetup()
    {
        var mine = typeof(Installer).Assembly.GetName().Version!;
        var current = new Version(mine.Major, mine.Minor, mine.Build);
        if (Registry.LocalMachine.OpenSubKey(UninstallKey)?.GetValue("DisplayVersion") is string s && Version.TryParse(s, out var installed) && File.Exists(Exe))
        {
            if (installed >= current)
            {
                string what = installed == current ? "This version" : $"A newer version ({installed})";
                MessageBox.Show($"{what} of {Name} is already installed ({installed}). Nothing to do.", Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // older copy: overwrite and restart it, keeping the user's startup choice
            try
            {
                Install(StartWithWindows);
                MessageBox.Show($"{Name} was updated from {installed} to {current} and restarted.", Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception e) { MessageBox.Show("Update failed: " + e.Message, Name, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            return;
        }

        var startup = new TaskDialogVerificationCheckBox("Start with Windows", true);
        var install = new TaskDialogButton("Install");
        var page = new TaskDialogPage
        {
            Caption = $"{Name} Setup",
            Heading = $"Install {Name}?",
            Text = "Shows CPU, GPU, network, RAM and mouse battery on your taskbar.\n\n" +
                   "Also installs the PawnIO driver (needed to read CPU temperature and power) if it isn't already installed.",
            Verification = startup,
            Buttons = { install, TaskDialogButton.Cancel },
        };
        if (TaskDialog.ShowDialog(page) != install) return;
        try
        {
            Install(startup.Checked);
            MessageBox.Show($"{Name} is installed and running.\n\nRight-click its tray icon for options. Remove it any time from Settings > Apps.",
                Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception e)
        {
            MessageBox.Show("Install failed: " + e.Message, Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void Install(bool startup)
    {
        StopOtherInstances();
        Directory.CreateDirectory(Dir);
        File.Copy(Environment.ProcessPath!, Exe, true);

        bool pawnByUs = Registry.LocalMachine.OpenSubKey(UninstallKey)?.GetValue("InstalledPawnIO") is 1;
        if (Registry.LocalMachine.OpenSubKey(PawnKey) == null) { InstallPawnIO(); pawnByUs = true; }

        using (var k = Registry.LocalMachine.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", Name);
            k.SetValue("DisplayVersion", typeof(Installer).Assembly.GetName().Version!.ToString(3));
            k.SetValue("Publisher", Name);
            k.SetValue("DisplayIcon", Exe);
            k.SetValue("InstallLocation", Dir);
            k.SetValue("UninstallString", $"\"{Exe}\" --uninstall");
            k.SetValue("NoModify", 1);
            k.SetValue("NoRepair", 1);
            k.SetValue("EstimatedSize", (int)(new FileInfo(Exe).Length / 1024));
            k.SetValue("InstalledPawnIO", pawnByUs ? 1 : 0);
        }

        StartWithWindows = startup;
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic lnk = shell.CreateShortcut(Shortcut);
        lnk.TargetPath = Exe;
        lnk.WorkingDirectory = Dir;
        lnk.Description = "CPU, GPU, network and RAM stats on the taskbar";
        lnk.Save();

        Process.Start(new ProcessStartInfo(Exe) { WorkingDirectory = Dir });
    }

    static void InstallPawnIO()
    {
        // extracted into Program Files (admin-only), not %TEMP%, so nothing can swap it before it runs
        var setup = Path.Combine(Dir, "PawnIO_setup.exe");
        using (var res = typeof(Installer).Assembly.GetManifestResourceStream("PawnIO_setup.exe")!)
        using (var f = File.Create(setup)) res.CopyTo(f);
        int code = Run(setup, "-install -silent");
        File.Delete(setup);
        if (code is not (0 or 3010)) throw new Exception($"PawnIO driver setup failed (code {code}).");
    }

    public static void Uninstall()
    {
        StopOtherInstances();
        Run("schtasks.exe", $"/Delete /TN {Name} /F");
        File.Delete(Shortcut);
        bool pawnByUs = Registry.LocalMachine.OpenSubKey(UninstallKey)?.GetValue("InstalledPawnIO") is 1;
        Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false);

        // only remove the driver if our installer put it there; other apps may rely on a PawnIO they installed
        if (pawnByUs && Registry.LocalMachine.OpenSubKey(PawnKey)?.GetValue("QuietUninstallString") is string q && q.StartsWith('"'))
        {
            int end = q.IndexOf('"', 1);
            Run(q[1..end], q[(end + 1)..].Trim());
        }

        // a running exe can't delete itself, so a hidden cmd removes the folder after we exit
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{Dir}\"")
        { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
        MessageBox.Show($"{Name} was removed.", Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    static void RegisterTask()
    {
        string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers><LogonTrigger><UserId>{user}</UserId><Delay>PT5S</Delay></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>"{SecurityElement.Escape(Exe)}"</Command></Exec></Actions>
            </Task>
            """;
        var file = Path.Combine(Dir, "task.xml");
        File.WriteAllText(file, xml, Encoding.Unicode);
        int code = Run("schtasks.exe", $"/Create /TN {Name} /XML \"{file}\" /F");
        File.Delete(file);
        if (code != 0) throw new Exception($"Could not create the startup task (code {code}).");
    }

    static void StopOtherInstances()
    {
        foreach (var p in Process.GetProcessesByName(Name).Where(p => p.Id != Environment.ProcessId))
        {
            try { p.Kill(); p.WaitForExit(3000); } catch { }
        }
    }

    static int Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false })!;
        p.WaitForExit();
        return p.ExitCode;
    }
}
