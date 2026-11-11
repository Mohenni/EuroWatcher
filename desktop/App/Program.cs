// EuroWatcher for Windows.
//
// The program is the web page (index.html, app.js, ...) plus a tiny web server that only this PC
// can reach (127.0.0.1). It opens the page in its own Microsoft Edge (or Chrome) window, keeps the
// draw history in %LOCALAPPDATA%\EuroWatcher, and lets the page ask it to fetch new draws.
// It closes by itself when that window is closed.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("EuroWatcher")]
[assembly: AssemblyProduct("EuroWatcher")]
[assembly: AssemblyDescription("Every EuroMillions draw, checked against your numbers")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace EuroWatcher
{
    internal static class App
    {
        public const string Version = "1.0.0";
        public const string Website = "https://github.com/Mohenni/EuroWatcher";

        public static string DataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EuroWatcher"); }
        }

        static readonly object LogGate = new object();

        public static void Log(string message)
        {
            try
            {
                lock (LogGate)
                {
                    Directory.CreateDirectory(DataDir);
                    string path = Path.Combine(DataDir, "log.txt");
                    if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Delete(path);
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message + Environment.NewLine);
                }
            }
            catch
            {
            }
        }

        public static byte[] Resource(string name)
        {
            using (Stream s = typeof(App).Assembly.GetManifestResourceStream(name))
            {
                if (s == null) return null;
                using (MemoryStream ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        public static void EnableModernTls()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { }
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; }   // TLS 1.3 where Windows has it
            catch { }
        }
    }

    /// <summary>The draw history on this PC: %LOCALAPPDATA%\EuroWatcher\draws.js.</summary>
    internal static class DataStore
    {
        static readonly object Gate = new object();
        public static string FilePath { get { return Path.Combine(App.DataDir, "draws.js"); } }

        static int CountDraws(string text)
        {
            return text == null ? 0 : Regex.Matches(text, @"^\[\d+,""", RegexOptions.Multiline).Count;
        }

        /// <summary>Puts the copy that comes with the program in place, unless the one on file is newer.</summary>
        public static void Prepare()
        {
            lock (Gate)
            {
                Directory.CreateDirectory(App.DataDir);
                byte[] shipped = App.Resource("web/draws.js");
                if (shipped == null) return;
                string onFile = null;
                try { if (File.Exists(FilePath)) onFile = File.ReadAllText(FilePath, Encoding.UTF8); }
                catch { }
                if (CountDraws(onFile) < CountDraws(Encoding.UTF8.GetString(shipped))) WriteBytes(shipped);
            }
        }

        public static byte[] ReadBytes()
        {
            lock (Gate)
            {
                try { if (File.Exists(FilePath)) return File.ReadAllBytes(FilePath); }
                catch { }
                return App.Resource("web/draws.js") ?? new byte[0];
            }
        }

        public static string ReadText() { return Encoding.UTF8.GetString(ReadBytes()); }

        public static void Write(string text) { lock (Gate) WriteBytes(new UTF8Encoding(false).GetBytes(text)); }

        static void WriteBytes(byte[] bytes)
        {
            Directory.CreateDirectory(App.DataDir);
            string temp = FilePath + ".new";
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
        }
    }

    /// <summary>A minimal web server on 127.0.0.1 that serves the page and two small requests.</summary>
    internal sealed class LocalServer
    {
        // A fixed port keeps the page's address the same, so saved plays and settings stay with it.
        public const int FirstPort = 47811;
        TcpListener _listener;
        public int Port;
        public string Url { get { return "http://127.0.0.1:" + Port + "/"; } }

        static readonly Dictionary<string, string[]> Files = new Dictionary<string, string[]>
        {
            { "/", new[] { "web/index.html", "text/html; charset=utf-8" } },
            { "/index.html", new[] { "web/index.html", "text/html; charset=utf-8" } },
            { "/styles.css", new[] { "web/styles.css", "text/css; charset=utf-8" } },
            { "/app.js", new[] { "web/app.js", "text/javascript; charset=utf-8" } },
            { "/i18n.js", new[] { "web/i18n.js", "text/javascript; charset=utf-8" } },
        };

        public void Start(int firstPort)
        {
            for (int port = firstPort; port < firstPort + 20; port++)
            {
                try
                {
                    TcpListener listener = new TcpListener(IPAddress.Loopback, port);
                    listener.Start();
                    _listener = listener;
                    Port = port;
                    break;
                }
                catch (SocketException)
                {
                }
            }
            if (_listener == null) throw new InvalidOperationException("no free local port between " + firstPort + " and " + (firstPort + 19));
            Thread accept = new Thread(AcceptLoop);
            accept.IsBackground = true;
            accept.Name = "EuroWatcher server";
            accept.Start();
        }

        public void Stop()
        {
            try { if (_listener != null) _listener.Stop(); }
            catch { }
        }

        void AcceptLoop()
        {
            while (true)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch { return; }
                ThreadPool.QueueUserWorkItem(Handle, client);
            }
        }

        void Handle(object state)
        {
            using (TcpClient client = (TcpClient)state)
            {
                try
                {
                    client.ReceiveTimeout = 15000;
                    client.SendTimeout = 15000;
                    NetworkStream stream = client.GetStream();
                    string head = ReadHead(stream);
                    if (head == null) return;
                    string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    string[] first = lines[0].Split(' ');
                    if (first.Length < 2) return;
                    string method = first[0].ToUpperInvariant();
                    string path = first[1];
                    int q = path.IndexOf('?');
                    if (q >= 0) path = path.Substring(0, q);
                    Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 1; i < lines.Length; i++)
                    {
                        int colon = lines[i].IndexOf(':');
                        if (colon > 0) headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
                    }
                    Respond(stream, method, path, headers);
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException)) App.Log("request failed: " + ex.Message);
                }
            }
        }

        static string ReadHead(NetworkStream stream)
        {
            MemoryStream buffer = new MemoryStream();
            int matched = 0;
            byte[] end = { 13, 10, 13, 10 };
            while (buffer.Length < 16384)
            {
                int b = stream.ReadByte();
                if (b < 0) return null;
                buffer.WriteByte((byte)b);
                matched = b == end[matched] ? matched + 1 : (b == 13 ? 1 : 0);
                if (matched == 4) return Encoding.ASCII.GetString(buffer.ToArray(), 0, (int)buffer.Length - 4);
            }
            return null;
        }

        void Respond(NetworkStream stream, string method, string path, Dictionary<string, string> headers)
        {
            // Only this program's own page may talk to it: the address must be exactly ours
            // (this blocks other web sites that try to reach it through a look-alike name).
            string host;
            headers.TryGetValue("Host", out host);
            if (host != "127.0.0.1:" + Port && host != "localhost:" + Port)
            {
                Send(stream, 403, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Forbidden"));
                return;
            }

            if (path == "/api/status" && method == "GET")
            {
                SendJson(stream, 200, "{\"app\":\"EuroWatcher\",\"version\":\"" + App.Version + "\"}");
                return;
            }
            if (path == "/api/update")
            {
                string marker;
                // A custom header cannot be sent by another web site without our permission, which we never give.
                if (method != "POST" || !headers.TryGetValue("X-EuroWatcher", out marker) || marker != "1")
                {
                    SendJson(stream, 403, "{\"ok\":false,\"message\":\"forbidden\"}");
                    return;
                }
                UpdateResult r = Updater.Run();
                SendJson(stream, r.Ok ? 200 : 502, "{\"ok\":" + (r.Ok ? "true" : "false") +
                    ",\"added\":" + r.Added + ",\"total\":" + r.Total +
                    ",\"latest\":" + JsonString(r.Latest ?? "") + ",\"via\":" + JsonString(r.Via) +
                    ",\"message\":" + JsonString(r.Message) + "}");
                return;
            }
            if (method != "GET" && method != "HEAD")
            {
                Send(stream, 405, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Method not allowed"));
                return;
            }
            if (path == "/data/draws.js")
            {
                Send(stream, 200, "text/javascript; charset=utf-8", DataStore.ReadBytes(), method == "HEAD");
                return;
            }
            string[] file;
            byte[] body = Files.TryGetValue(path, out file) ? App.Resource(file[0]) : null;
            if (body == null)
            {
                Send(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not found"));
                return;
            }
            Send(stream, 200, file[1], body, method == "HEAD");
        }

        static string JsonString(string s)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append(' ');
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static void SendJson(NetworkStream stream, int status, string json)
        {
            Send(stream, status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
        }

        static void Send(NetworkStream stream, int status, string type, byte[] body, bool headOnly = false)
        {
            string reason = status == 200 ? "OK" : status == 403 ? "Forbidden" : status == 404 ? "Not Found" : status == 405 ? "Method Not Allowed" : "Bad Gateway";
            string head = "HTTP/1.1 " + status + " " + reason + "\r\n" +
                          "Content-Type: " + type + "\r\n" +
                          "Content-Length: " + body.Length + "\r\n" +
                          "Cache-Control: no-store\r\n" +
                          "X-Content-Type-Options: nosniff\r\n" +
                          "Referrer-Policy: no-referrer\r\n" +
                          "Connection: close\r\n\r\n";
            byte[] h = Encoding.ASCII.GetBytes(head);
            stream.Write(h, 0, h.Length);
            if (!headOnly) stream.Write(body, 0, body.Length);
            stream.Flush();
        }
    }

    /// <summary>Finds Microsoft Edge or Google Chrome, which can show a page as its own app window.</summary>
    internal static class Browser
    {
        public static string Find()
        {
            foreach (string exe in new[] { "msedge.exe", "chrome.exe" })
            {
                foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                {
                    foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                    {
                        try
                        {
                            using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                            using (RegistryKey key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                            {
                                string path = key != null ? key.GetValue(null) as string : null;
                                if (!string.IsNullOrEmpty(path) && File.Exists(path.Trim('"'))) return path.Trim('"');
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            string[] known =
            {
                @"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe",
                @"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe",
                @"%ProgramFiles%\Google\Chrome\Application\chrome.exe",
                @"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe",
                @"%LocalAppData%\Google\Chrome\Application\chrome.exe",
            };
            foreach (string k in known)
            {
                string path = Environment.ExpandEnvironmentVariables(k);
                if (File.Exists(path)) return path;
            }
            return null;
        }
    }

    /// <summary>Runs while the EuroWatcher window is open.</summary>
    internal sealed class MainContext : ApplicationContext
    {
        readonly LocalServer _server = new LocalServer();
        readonly Control _ui = new Control();
        readonly EventWaitHandle _openSignal;
        readonly System.Windows.Forms.Timer _watch = new System.Windows.Forms.Timer();
        readonly string _profile = Path.Combine(App.DataDir, "window");
        NotifyIcon _tray;
        bool _windowMode;
        DateTime _launched;

        public const string OpenSignalName = @"Local\EuroWatcher.Open";

        public MainContext()
        {
            _ui.CreateControl();
            DataStore.Prepare();
            _server.Start(LocalServer.FirstPort);
            App.Log("EuroWatcher " + App.Version + " serving " + _server.Url);

            _openSignal = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSignalName);
            Thread listen = new Thread(delegate ()
            {
                while (true)
                {
                    try { _openSignal.WaitOne(); }
                    catch { return; }
                    try { _ui.BeginInvoke(new MethodInvoker(OpenWindow)); }
                    catch { return; }
                }
            });
            listen.IsBackground = true;
            listen.Start();

            OpenWindow();

            _watch.Interval = 2000;
            _watch.Tick += delegate { if (_windowMode && DateTime.UtcNow - _launched > TimeSpan.FromSeconds(10) && !WindowStillOpen()) ExitThread(); };
            _watch.Start();
        }

        void OpenWindow()
        {
            string browser = Browser.Find();
            if (browser != null)
            {
                try
                {
                    Directory.CreateDirectory(_profile);
                    // Its own profile keeps EuroWatcher apart from the user's normal browsing, and lets us see when the window closes.
                    string args = "--app=" + _server.Url + " --user-data-dir=\"" + _profile + "\" --no-first-run --no-default-browser-check --window-size=1280,900";
                    Process.Start(new ProcessStartInfo(browser, args) { UseShellExecute = false });
                    _windowMode = true;
                    _launched = DateTime.UtcNow;
                    return;
                }
                catch (Exception ex)
                {
                    App.Log("could not start " + browser + ": " + ex.Message);
                }
            }
            try { Process.Start(_server.Url); }
            catch (Exception ex) { App.Log("could not open the default browser: " + ex.Message); }
            ShowTray();
        }

        /// <summary>Edge and Chrome hold "lockfile" in their profile folder for as long as any of their windows is open.</summary>
        bool WindowStillOpen()
        {
            string lockFile = Path.Combine(_profile, "lockfile");
            if (!File.Exists(lockFile)) return false;
            try
            {
                using (new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch
            {
                return false;
            }
        }

        void ShowTray()
        {
            if (_tray != null) return;
            _tray = new NotifyIcon();
            try { _tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { _tray.Icon = SystemIcons.Application; }
            _tray.Text = "EuroWatcher";
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(Strings.Get("TrayOpen"), null, delegate { OpenInDefaultBrowser(); });
            menu.Items.Add(Strings.Get("TrayExit"), null, delegate { ExitThread(); });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { OpenInDefaultBrowser(); };
            _tray.Visible = true;
            _tray.ShowBalloonTip(8000, "EuroWatcher", Strings.Get("NoBrowser"), ToolTipIcon.Info);
        }

        void OpenInDefaultBrowser()
        {
            try { Process.Start(_server.Url); }
            catch { }
        }

        protected override void ExitThreadCore()
        {
            _watch.Stop();
            _server.Stop();
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            try { _openSignal.Dispose(); }
            catch { }
            base.ExitThreadCore();
        }
    }

    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            App.EnableModernTls();

            if (args.Length >= 1 && args[0] == "--selftest")
                return SelfTest.Run(args.Length > 1 ? args[1] : ".");

            // Offline rebuild from saved copies of the sources (used to compare with tools/update-data.mjs):
            //   EuroWatcher.exe --build-data out.js list.csv prizes.json latest.xml [previous.js]
            if (args.Length >= 5 && args[0] == "--build-data")
            {
                DrawFile previous = args.Length > 5 ? DrawFile.Parse(File.ReadAllText(args[5], Encoding.UTF8)) : null;
                DrawFile built = DrawFile.Build(previous, File.ReadAllText(args[2], Encoding.UTF8), File.ReadAllText(args[3], Encoding.UTF8), File.ReadAllText(args[4], Encoding.UTF8));
                File.WriteAllText(args[1], built.Serialize(), new UTF8Encoding(false));
                return 0;
            }

            bool firstCopy;
            using (Mutex single = new Mutex(true, @"Local\EuroWatcher.Instance", out firstCopy))
            {
                if (!firstCopy)
                {
                    // Already running: ask it to open another window instead of starting twice.
                    for (int attempt = 0; attempt < 20; attempt++)
                    {
                        try
                        {
                            using (EventWaitHandle signal = EventWaitHandle.OpenExisting(MainContext.OpenSignalName))
                                signal.Set();
                            return 0;
                        }
                        catch
                        {
                            Thread.Sleep(150);
                        }
                    }
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    Application.Run(new MainContext());
                    return 0;
                }
                catch (Exception ex)
                {
                    App.Log("start failed: " + ex);
                    MessageBox.Show(Strings.Format("StartFailed", ex.Message), "EuroWatcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }
        }
    }

    /// <summary>
    /// "EuroWatcher.exe --selftest folder": checks the data round trip and the local server without
    /// opening a window or touching the internet. Writes done.txt, or errors.txt with what failed.
    /// </summary>
    internal static class SelfTest
    {
        public static int Run(string folder)
        {
            List<string> errors = new List<string>();
            List<string> notes = new List<string>();
            Directory.CreateDirectory(folder);
            try
            {
                // 1. The shipped data reads back and writes out row for row the same.
                string shipped = Encoding.UTF8.GetString(App.Resource("web/draws.js"));
                DrawFile file = DrawFile.Parse(shipped);
                string again = file.Serialize();
                string[] a = Rows(shipped), b = Rows(again);
                if (a.Length != b.Length) errors.Add("row count changed: " + a.Length + " vs " + b.Length);
                for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
                    if (a[i] != b[i]) { errors.Add("row " + (i + 1) + " differs: " + a[i] + "  vs  " + b[i]); break; }
                List<string> problems = file.Problems();
                if (problems.Count > 0) errors.Add("shipped data: " + problems[0]);
                notes.Add("data: " + file.Draws.Count + " draws, latest " + file.Latest);

                // 2. The local server answers the page, the data and the status request, and refuses strangers.
                LocalServer server = new LocalServer();
                server.Start(LocalServer.FirstPort + 100);
                try
                {
                    Expect(errors, notes, server, "GET", "/", null, 200, "EuroWatcher");
                    Expect(errors, notes, server, "GET", "/app.js", null, 200, "EURO_I18N");
                    Expect(errors, notes, server, "GET", "/i18n.js", null, 200, "Français");
                    Expect(errors, notes, server, "GET", "/styles.css", null, 200, ":root");
                    Expect(errors, notes, server, "GET", "/data/draws.js", null, 200, "window.EURO_DATA");
                    Expect(errors, notes, server, "GET", "/api/status", null, 200, "\"app\":\"EuroWatcher\"");
                    Expect(errors, notes, server, "POST", "/api/update", null, 403, "forbidden");
                    Expect(errors, notes, server, "GET", "/nothing-here", null, 404, null);
                    Expect(errors, notes, server, "GET", "/", "evil.example:" + server.Port, 403, null);
                }
                finally
                {
                    server.Stop();
                }
            }
            catch (Exception ex)
            {
                errors.Add("crashed: " + ex);
            }
            File.WriteAllLines(Path.Combine(folder, "notes.txt"), notes);
            if (errors.Count > 0)
            {
                File.WriteAllLines(Path.Combine(folder, "errors.txt"), errors);
                return 1;
            }
            File.WriteAllText(Path.Combine(folder, "done.txt"), "ok");
            return 0;
        }

        static string[] Rows(string text)
        {
            List<string> rows = new List<string>();
            foreach (string line in text.Split('\n'))
                if (Regex.IsMatch(line, @"^\[\d+,""")) rows.Add(line.TrimEnd('\r', ','));
            return rows.ToArray();
        }

        static void Expect(List<string> errors, List<string> notes, LocalServer server, string method, string path, string host, int status, string contains)
        {
            using (TcpClient client = new TcpClient("127.0.0.1", server.Port))
            {
                NetworkStream stream = client.GetStream();
                string request = method + " " + path + " HTTP/1.1\r\nHost: " + (host ?? "127.0.0.1:" + server.Port) + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                byte[] bytes = Encoding.ASCII.GetBytes(request);
                stream.Write(bytes, 0, bytes.Length);
                string response = new StreamReader(stream, Encoding.UTF8).ReadToEnd();
                string first = response.Split('\n')[0].Trim();
                bool ok = first.StartsWith("HTTP/1.1 " + status + " ", StringComparison.Ordinal) && (contains == null || response.Contains(contains));
                notes.Add(method + " " + path + (host != null ? " (host " + host + ")" : "") + " -> " + first);
                if (!ok) errors.Add(method + " " + path + ": expected " + status + (contains != null ? " containing " + contains : "") + ", got " + first);
            }
        }
    }
}
