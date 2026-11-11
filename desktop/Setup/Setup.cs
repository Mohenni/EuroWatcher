// EuroWatcher Setup - installs EuroWatcher for the current Windows account, and removes it again
// (the same file is copied into the program folder as Uninstall.exe).
//
// Installing does exactly this, and nothing else:
//   - copies EuroWatcher.exe and Uninstall.exe into %LOCALAPPDATA%\Programs\EuroWatcher
//   - adds a Start menu shortcut (and a desktop shortcut if wanted)
//   - registers EuroWatcher under "Apps" so Windows can remove it
// No administrator permission is needed, because nothing is written outside your own account.
//
//   EuroWatcherSetup.exe                     install, with a window
//   EuroWatcherSetup.exe /silent             install without questions
//   Uninstall.exe /uninstall [/silent]       remove
//   ... /sandbox <folder>                    use a throw-away folder instead of the real places (tests)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("EuroWatcher Setup")]
[assembly: AssemblyProduct("EuroWatcher")]
[assembly: AssemblyDescription("Installs or removes EuroWatcher")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace EuroWatcher
{
    internal sealed class InstallTarget
    {
        public string Dir;
        public string StartMenuLink;
        public string DesktopLink;
        public string DataDir;
        public RegistryKey RegistryRoot = Registry.CurrentUser;
        public string RegistryPath;
        public bool IsSandbox;

        public string ExePath { get { return Path.Combine(Dir, "EuroWatcher.exe"); } }
        public string UninstallerPath { get { return Path.Combine(Dir, "Uninstall.exe"); } }

        public static InstallTarget Real()
        {
            InstallTarget t = new InstallTarget();
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            t.Dir = Path.Combine(local, "Programs", "EuroWatcher");
            t.DataDir = Path.Combine(local, "EuroWatcher");
            t.StartMenuLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "EuroWatcher.lnk");
            t.DesktopLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "EuroWatcher.lnk");
            t.RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\EuroWatcher";
            return t;
        }

        public static InstallTarget Sandbox(string folder)
        {
            InstallTarget t = new InstallTarget();
            t.IsSandbox = true;
            t.Dir = Path.Combine(folder, "Programs", "EuroWatcher");
            t.DataDir = Path.Combine(folder, "Data", "EuroWatcher");
            t.StartMenuLink = Path.Combine(folder, "Start Menu", "EuroWatcher.lnk");
            t.DesktopLink = Path.Combine(folder, "Desktop", "EuroWatcher.lnk");
            t.RegistryPath = @"Software\EuroWatcherSetupSelfTest\Uninstall\EuroWatcher";
            Directory.CreateDirectory(Path.GetDirectoryName(t.StartMenuLink));
            Directory.CreateDirectory(Path.GetDirectoryName(t.DesktopLink));
            return t;
        }
    }

    internal static class Installer
    {
        public const string Version = "1.0.0";

        static bool IsInside(string file, string dir)
        {
            try
            {
                string full = Path.GetFullPath(file), root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Closes EuroWatcher copies started from the program folder, so their files can be replaced.</summary>
        public static void CloseRunningCopies(InstallTarget target)
        {
            foreach (Process process in Process.GetProcessesByName("EuroWatcher"))
            {
                try
                {
                    string path = null;
                    try { path = process.MainModule.FileName; }
                    catch { }
                    if (path != null && IsInside(path, target.Dir))
                    {
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        public static void Install(InstallTarget target, bool desktopShortcut)
        {
            CloseRunningCopies(target);
            Directory.CreateDirectory(target.Dir);
            using (Stream payload = typeof(Installer).Assembly.GetManifestResourceStream("EuroWatcher.Payload.exe"))
            {
                if (payload == null) throw new InvalidOperationException("this setup file is incomplete: the program is missing from it");
                WriteFile(target.ExePath, payload);
            }
            string self = Application.ExecutablePath;
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(target.UninstallerPath), StringComparison.OrdinalIgnoreCase))
            {
                using (FileStream source = new FileStream(self, FileMode.Open, FileAccess.Read, FileShare.Read))
                    WriteFile(target.UninstallerPath, source);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target.StartMenuLink));
            CreateShortcut(target.StartMenuLink, target.ExePath);
            if (desktopShortcut) CreateShortcut(target.DesktopLink, target.ExePath);
            else TryDelete(target.DesktopLink);

            long size = new FileInfo(target.ExePath).Length + new FileInfo(target.UninstallerPath).Length;
            using (RegistryKey key = target.RegistryRoot.CreateSubKey(target.RegistryPath))
            {
                key.SetValue("DisplayName", "EuroWatcher");
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", "EuroWatcher");
                key.SetValue("DisplayIcon", target.ExePath);
                key.SetValue("InstallLocation", target.Dir);
                key.SetValue("URLInfoAbout", "https://github.com/Mohenni/EuroWatcher");
                key.SetValue("UninstallString", "\"" + target.UninstallerPath + "\" /uninstall");
                key.SetValue("QuietUninstallString", "\"" + target.UninstallerPath + "\" /uninstall /silent");
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                key.SetValue("EstimatedSize", (int)(size / 1024), RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        /// <summary>Removes EuroWatcher. Returns what could not be removed (normally nothing).</summary>
        public static List<string> Uninstall(InstallTarget target, bool deleteData)
        {
            List<string> problems = new List<string>();
            CloseRunningCopies(target);
            TryDelete(target.StartMenuLink);
            TryDelete(target.DesktopLink);
            try { target.RegistryRoot.DeleteSubKeyTree(target.RegistryPath, false); }
            catch (Exception ex) { problems.Add(ex.Message); }

            // Only ever delete the two files setup put there, then the folder if it is empty.
            string self = Path.GetFullPath(Application.ExecutablePath);
            foreach (string file in new[] { target.ExePath, target.UninstallerPath })
            {
                if (string.Equals(Path.GetFullPath(file), self, StringComparison.OrdinalIgnoreCase)) continue;
                if (!DeleteWithRetry(file)) problems.Add(file);
            }
            try
            {
                if (Directory.Exists(target.Dir) && Directory.GetFileSystemEntries(target.Dir).Length == 0) Directory.Delete(target.Dir, false);
            }
            catch
            {
            }

            // The draw data, saved plays and EuroWatcher's own browser profile, only when asked.
            if (deleteData && Directory.Exists(target.DataDir))
            {
                for (int attempt = 0; attempt < 10 && Directory.Exists(target.DataDir); attempt++)
                {
                    try { Directory.Delete(target.DataDir, true); }
                    catch { Thread.Sleep(500); }   // the EuroWatcher window may still be closing
                }
                if (Directory.Exists(target.DataDir)) problems.Add(target.DataDir);
            }
            return problems;
        }

        static void CreateShortcut(string linkPath, string targetPath)
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell", true);
            dynamic shell = Activator.CreateInstance(type);
            dynamic link = shell.CreateShortcut(linkPath);
            link.TargetPath = targetPath;
            link.WorkingDirectory = Path.GetDirectoryName(targetPath);
            link.Description = Strings.Get("ShortcutDescription");
            link.IconLocation = targetPath + ",0";
            link.Save();
        }

        static void WriteFile(string destination, Stream content)
        {
            IOException last = null;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                try
                {
                    using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        if (content.CanSeek) content.Position = 0;
                        content.CopyTo(output);
                    }
                    return;
                }
                catch (IOException ex)
                {
                    last = ex;          // usually: the old copy is still closing
                    Thread.Sleep(300);
                }
            }
            throw new IOException("could not write " + destination + ". " + (last != null ? last.Message : ""));
        }

        static bool DeleteWithRetry(string file)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (!File.Exists(file)) return true;
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    return true;
                }
                catch
                {
                    Thread.Sleep(300);
                }
            }
            return !File.Exists(file);
        }

        static void TryDelete(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch { }
        }
    }

    internal sealed class SetupForm : Form
    {
        static readonly Color Accent = Color.FromArgb(42, 63, 208);
        static readonly Color Ink = Color.FromArgb(22, 26, 46);
        static readonly Color Muted = Color.FromArgb(75, 81, 104);
        static readonly Color Band = Color.FromArgb(243, 241, 234);

        readonly bool _uninstall;
        readonly InstallTarget _target;
        readonly Label _title = new Label();
        readonly Label _body = new Label();
        readonly CheckBox _check1 = new CheckBox();
        readonly CheckBox _check2 = new CheckBox();
        readonly Label _status = new Label();
        readonly Button _primary = new Button();
        readonly Button _secondary = new Button();
        readonly PictureBox _logo = new PictureBox();
        bool _finished;

        public SetupForm(bool uninstall, InstallTarget target)
        {
            _uninstall = uninstall;
            _target = target;

            Text = uninstall ? Strings.Get("UninstallTitle") : Strings.Get("SetupTitle");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.75f);
            AutoScaleMode = AutoScaleMode.None;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            float k;
            using (Graphics g = CreateGraphics()) k = g.DpiX / 96f;
            Func<int, int> S = v => (int)Math.Round(v * k);
            int width = S(500), pad = S(24), textWidth = width - 2 * pad;

            Panel band = new Panel { BackColor = Band, Bounds = new Rectangle(0, 0, width, S(84)) };
            Controls.Add(band);
            _logo.Bounds = new Rectangle(pad, S(18), S(48), S(48));
            _logo.SizeMode = PictureBoxSizeMode.Zoom;
            try
            {
                using (Stream s = typeof(SetupForm).Assembly.GetManifestResourceStream("EuroWatcher.ico"))
                    if (s != null) _logo.Image = new Icon(s, S(48), S(48)).ToBitmap();
            }
            catch
            {
            }
            band.Controls.Add(_logo);
            _title.Text = Text;
            _title.Font = new Font("Segoe UI Semibold", 15f);
            _title.ForeColor = Ink;
            _title.AutoSize = false;
            _title.Bounds = new Rectangle(pad + S(62), S(26), textWidth - S(62), S(34));
            band.Controls.Add(_title);

            int y = band.Bottom + S(18);
            _body.Text = uninstall ? Strings.Get("UninstallBody") : Strings.Get("SetupBody");
            _body.ForeColor = Muted;
            _body.MaximumSize = new Size(textWidth, 0);
            _body.AutoSize = true;
            _body.Location = new Point(pad, y);
            Controls.Add(_body);
            y = _body.Bottom + S(16);

            _check1.Text = uninstall ? Strings.Get("DeleteData") : Strings.Get("DesktopShortcut");
            _check1.Checked = !uninstall;
            _check1.AutoSize = true;
            _check1.MaximumSize = new Size(textWidth, 0);
            _check1.ForeColor = Ink;
            _check1.Location = new Point(pad, y);
            Controls.Add(_check1);
            y = _check1.Bottom + S(6);
            if (!uninstall)
            {
                _check2.Text = Strings.Get("OpenAfter");
                _check2.Checked = true;
                _check2.AutoSize = true;
                _check2.ForeColor = Ink;
                _check2.Location = new Point(pad, y);
                Controls.Add(_check2);
                y = _check2.Bottom + S(6);
            }

            _status.AutoSize = true;
            _status.MaximumSize = new Size(textWidth, 0);
            _status.ForeColor = Muted;
            _status.Location = new Point(pad, y + S(8));
            Controls.Add(_status);
            y += S(48);

            int bw = S(124), bh = S(34);
            _primary.Text = uninstall ? Strings.Get("Remove") : Strings.Get("Install");
            _primary.Bounds = new Rectangle(width - pad - bw, y, bw, bh);
            _primary.FlatStyle = FlatStyle.Flat;
            _primary.FlatAppearance.BorderSize = 0;
            _primary.BackColor = Accent;
            _primary.ForeColor = Color.White;
            _primary.Click += delegate { Primary(); };
            Controls.Add(_primary);

            _secondary.Text = Strings.Get("Cancel");
            _secondary.Bounds = new Rectangle(_primary.Left - S(10) - bw, y, bw, bh);
            _secondary.FlatStyle = FlatStyle.System;
            _secondary.Click += delegate { Close(); };
            Controls.Add(_secondary);

            AcceptButton = _primary;
            CancelButton = _secondary;
            ClientSize = new Size(width, y + bh + pad);
        }

        void Primary()
        {
            if (_finished)
            {
                Close();
                return;
            }
            _primary.Enabled = false;
            _secondary.Enabled = false;
            _check1.Enabled = false;
            _check2.Enabled = false;
            _status.Text = Strings.Get("Working");
            _status.Refresh();
            Cursor = Cursors.WaitCursor;
            try
            {
                if (_uninstall)
                {
                    List<string> problems = Installer.Uninstall(_target, _check1.Checked);
                    _status.Text = problems.Count == 0 ? Strings.Get("Removed") : Strings.Get("RemoveProblems") + Environment.NewLine + string.Join(Environment.NewLine, problems);
                }
                else
                {
                    Installer.Install(_target, _check1.Checked);
                    _status.Text = Strings.Get("Installed");
                    if (_check2.Checked && !_target.IsSandbox)
                    {
                        try { Process.Start(new ProcessStartInfo(_target.ExePath) { WorkingDirectory = _target.Dir }); }
                        catch { }
                    }
                }
                _status.ForeColor = Color.FromArgb(11, 127, 87);
            }
            catch (Exception ex)
            {
                _status.Text = Strings.Format("InstallFailed", ex.Message);
                _status.ForeColor = Color.FromArgb(160, 40, 30);
            }
            Cursor = Cursors.Default;
            _finished = true;
            _primary.Text = Strings.Get("Close");
            _primary.Enabled = true;
            _secondary.Visible = false;
            _primary.Focus();
        }
    }

    internal static class SetupProgram
    {
        [STAThread]
        static int Main(string[] args)
        {
            bool uninstall = false, silent = false, keepData = true, noDesktop = false, cleanup = false;
            string sandbox = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "/uninstall") uninstall = true;
                else if (a == "/silent") silent = true;
                else if (a == "/deletedata") keepData = false;
                else if (a == "/nodesktop") noDesktop = true;
                else if (a == "/cleanup") cleanup = true;
                else if (a == "/sandbox" && i + 1 < args.Length) sandbox = args[++i];
            }
            InstallTarget target = sandbox != null ? InstallTarget.Sandbox(sandbox) : InstallTarget.Real();

            // Uninstall.exe cannot delete itself while it runs, so it hands over to a copy in the temp folder.
            if (uninstall && !cleanup && string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(target.UninstallerPath), StringComparison.OrdinalIgnoreCase))
            {
                string temp = Path.Combine(Path.GetTempPath(), "EuroWatcherUninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
                File.Copy(Application.ExecutablePath, temp, true);
                string rest = string.Join(" ", Array.ConvertAll(args, x => x.Contains(" ") ? "\"" + x + "\"" : x));
                Process.Start(new ProcessStartInfo(temp, rest + " /cleanup") { UseShellExecute = false });
                return 0;
            }

            int code = 0;
            if (silent)
            {
                try
                {
                    if (uninstall) code = Installer.Uninstall(target, !keepData).Count == 0 ? 0 : 2;
                    else Installer.Install(target, !noDesktop);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    code = 1;
                }
            }
            else
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new SetupForm(uninstall, target));
            }

            if (cleanup)
            {
                // The temporary copy removes itself a moment after it closes.
                try
                {
                    ProcessStartInfo del = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                        "/c ping 127.0.0.1 -n 3 > nul & del /q \"" + Application.ExecutablePath + "\"");
                    del.CreateNoWindow = true;
                    del.UseShellExecute = false;
                    del.WindowStyle = ProcessWindowStyle.Hidden;
                    Process.Start(del);
                }
                catch
                {
                }
            }
            return code;
        }
    }
}
