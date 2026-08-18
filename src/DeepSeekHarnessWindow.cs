using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

[assembly: AssemblyTitle("DeepSeek Harness")]
[assembly: AssemblyDescription("DeepSeek Harness standalone window (WebView2)")]
[assembly: AssemblyCompany("Community project, not affiliated with DeepSeek")]
[assembly: AssemblyProduct("DeepSeek Harness Window")]
[assembly: AssemblyVersion("1.2.2.0")]
[assembly: AssemblyFileVersion("1.2.2.0")]

internal static class Program
{
    private const string AppTitle = "DeepSeek Harness";
    private const string DefaultHost = "127.0.0.1";
    private const int DefaultPort = 3080;
    private const string PickerPatch =
        "- id: directory-picker\r\n" +
        "  disabled: true\r\n" +
        "- insert:\r\n" +
        "    - id: directory-picker-browse\r\n" +
        "      name: '@deepseek-ai/dsh-host-directory-picker-browse'\r\n" +
        "    - id: directory-picker-browse-ui\r\n" +
        "      name: '@deepseek-ai/dsh-client-ui-directory-picker-browse'\r\n";

    private static LauncherConfig config;
    private static ResolvedPaths paths;
    private static string patchPath;
    private static string appUrl;
    private static Process spawnedServer;
    private static Mutex singleInstance;

    internal static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeepSeekHarnessWindow", "launcher.log");

    [STAThread]
    private static int Main(string[] args)
    {
        // High-DPI: the embedded manifest (PerMonitorV2) is the primary
        // mechanism; this runtime fallback covers builds without a manifest.
        EnableHighDpi();
        // GitHub API requires TLS 1.2+; the .NET Framework default is lower.
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        try
        {
            bool checkOnly = HasFlag(args, "--check");
            bool noWindow = HasFlag(args, "--no-window") || HasFlag(args, "--no-open");
            bool install = HasFlag(args, "--install");
            bool uninstall = HasFlag(args, "--uninstall");

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // Installer mode runs before any runtime resolution so it also
            // works on machines without Node/DSH installed.
            if (install)
                return RunInstall(appDir) ? 0 : 1;
            if (uninstall)
                return RunUninstall() ? 0 : 1;

            paths = ResolvePaths(appDir, localAppData);
            config = LoadConfig(Path.Combine(appDir, "dsh-window.config.json"));
            appUrl = "http://" + config.Host + ":" + config.Port.ToString(CultureInfo.InvariantCulture) + "/";
            patchPath = Path.Combine(localAppData, "DeepSeekHarnessLauncher", "directory-picker.patch.yml");

            Log("start, url=" + appUrl + ", portable=" + paths.NodeExe.StartsWith(appDir, StringComparison.OrdinalIgnoreCase));

            if (checkOnly)
                return 0;

            Directory.CreateDirectory(Path.GetDirectoryName(patchPath));
            File.WriteAllText(patchPath, PickerPatch);

            bool serverReady = EnsureServer();

            if (noWindow)
                return serverReady ? 0 : 3;

            bool createdNew;
            singleInstance = new Mutex(true, @"Local\DeepSeekHarnessWindow.v2", out createdNew);
            if (!createdNew)
            {
                FocusExistingInstance();
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(spawnedServer, appUrl, serverReady, config.KillServerOnClose, config.CloseToTray));
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static void Log(string message)
    {
        if (config == null || !config.LogFile)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
            File.AppendAllText(LogPath,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine,
                Encoding.UTF8);
        }
        catch
        {
        }
    }

    // ---- High-DPI awareness ----
    // The embedded app.manifest declares PerMonitorV2; when a manifest is
    // present these calls fail with E_ACCESSDENIED and we keep the manifest
    // value. Without a manifest they promote the process so the window is
    // not bitmap-stretched on scaled displays (issue #1).

    private static readonly IntPtr DpiAwarenessContextPerMonitorV2 = new IntPtr(-4);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("shcore.dll")]
    private static extern int SetProcessDpiAwareness(int value);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    private static void EnableHighDpi()
    {
        try
        {
            if (!SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorV2))
            {
                try
                {
                    SetProcessDpiAwareness(2); // PROCESS_PER_MONITOR_DPI_AWARE
                }
                catch
                {
                    SetProcessDPIAware();
                }
            }
        }
        catch
        {
            try
            {
                SetProcessDPIAware();
            }
            catch
            {
                // Nothing left to try; the window will be virtualized.
            }
        }
    }

    /// <summary>Ensures the DSH server is online, starting it when needed.</summary>
    internal static bool EnsureServer()
    {
        if (IsOnline(appUrl, 1200))
            return true;
        if (spawnedServer == null || spawnedServer.HasExited)
            spawnedServer = StartHarness();
        return WaitFor(appUrl, config.WaitTimeoutMs);
    }

    internal sealed class UpdateInfo
    {
        public string LatestVersion;
        public string ReleaseUrl;
    }

    /// <summary>Queries GitHub for the latest release of the community repo.</summary>
    internal static UpdateInfo CheckForUpdates()
    {
        try
        {
            string url = "https://api.github.com/repos/" + config.UpdateRepo + "/releases/latest";
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = 8000;
            request.ReadWriteTimeout = 8000;
            request.UserAgent = "DeepSeek-Harness-Window";
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> release =
                    serializer.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                if (release == null || !release.ContainsKey("tag_name"))
                    return null;
                string tag = Convert.ToString(release["tag_name"]);
                Version latest = ParseVersion(tag);
                Version current = Assembly.GetExecutingAssembly().GetName().Version;
                if (latest != null && latest > current)
                {
                    return new UpdateInfo
                    {
                        LatestVersion = tag,
                        ReleaseUrl = release.ContainsKey("html_url") ? Convert.ToString(release["html_url"]) : ""
                    };
                }
            }
        }
        catch (Exception error)
        {
            Log("update check failed: " + error.Message);
        }
        return null;
    }

    private static Version ParseVersion(string tag)
    {
        if (string.IsNullOrEmpty(tag))
            return null;
        string value = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag.Substring(1) : tag;
        try
        {
            return new Version(value);
        }
        catch
        {
            return null;
        }
    }

    private static bool HasFlag(string[] args, string flag)
    {
        foreach (string arg in args)
        {
            if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private sealed class ResolvedPaths
    {
        public string NodeExe;
        public string DshEntry;
        public string GitCmd;
    }

    private static ResolvedPaths ResolvePaths(string appDir, string localAppData)
    {
        string portableNode = Path.Combine(appDir, "runtime", "node", "node.exe");
        string portableDsh = Path.Combine(appDir, "runtime", "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
        string portableGit = Path.Combine(appDir, "runtime", "git", "cmd");

        string sysNodeRoot = Path.Combine(localAppData, "Programs", "nodejs");
        string sysNode = Path.Combine(sysNodeRoot, "node.exe");
        string sysDsh = Path.Combine(sysNodeRoot, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
        string sysGit = Path.Combine(localAppData, "Programs", "Git", "cmd");

        if (File.Exists(portableNode) && File.Exists(portableDsh))
            return new ResolvedPaths { NodeExe = portableNode, DshEntry = portableDsh, GitCmd = portableGit };
        if (File.Exists(sysNode) && File.Exists(sysDsh))
            return new ResolvedPaths { NodeExe = sysNode, DshEntry = sysDsh, GitCmd = sysGit };

        throw new InvalidOperationException(
            "找不到 DeepSeek Harness 运行环境。\n\n" +
            "便携版：请确认本程序同目录下 runtime 文件夹完整（runtime\\node、runtime\\node_modules）。\n" +
            "系统版：请先安装 Node.js，再运行 npm.cmd install -g @deepseek-ai/dsh");
    }

    private sealed class LauncherConfig
    {
        public string Host = DefaultHost;
        public int Port = DefaultPort;
        public int WaitTimeoutMs = 30000;
        public bool KillServerOnClose = true;
        public string PermissionMode = "danger-full-access";
        public bool CheckUpdates = true;
        public string UpdateRepo = "ZichengGurrr/dsh-window";
        public bool CloseToTray = false;
        public bool LogFile = false;
    }

    private static LauncherConfig LoadConfig(string path)
    {
        LauncherConfig cfg = new LauncherConfig();
        if (!File.Exists(path))
            return cfg;
        try
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> map =
                serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            if (map == null)
                return cfg;
            if (map.ContainsKey("host") && map["host"] is string) cfg.Host = (string)map["host"];
            if (map.ContainsKey("port")) cfg.Port = Convert.ToInt32(map["port"]);
            if (map.ContainsKey("waitTimeoutMs")) cfg.WaitTimeoutMs = Convert.ToInt32(map["waitTimeoutMs"]);
            if (map.ContainsKey("killServerOnClose")) cfg.KillServerOnClose = Convert.ToBoolean(map["killServerOnClose"]);
            if (map.ContainsKey("permissionMode") && map["permissionMode"] is string)
                cfg.PermissionMode = (string)map["permissionMode"];
            if (map.ContainsKey("checkUpdates")) cfg.CheckUpdates = Convert.ToBoolean(map["checkUpdates"]);
            if (map.ContainsKey("updateRepo") && map["updateRepo"] is string)
                cfg.UpdateRepo = (string)map["updateRepo"];
            if (map.ContainsKey("closeToTray")) cfg.CloseToTray = Convert.ToBoolean(map["closeToTray"]);
            if (map.ContainsKey("logFile")) cfg.LogFile = Convert.ToBoolean(map["logFile"]);
        }
        catch
        {
            // A broken config file falls back to defaults; never block startup.
        }
        return cfg;
    }

    private static Process StartHarness()
    {
        ProcessStartInfo start = new ProcessStartInfo();
        start.FileName = paths.NodeExe;
        start.Arguments = Quote(paths.DshEntry) + " --profile web --patch " + Quote(patchPath) +
            " --host " + config.Host + " --port " + config.Port.ToString(CultureInfo.InvariantCulture);
        start.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.WindowStyle = ProcessWindowStyle.Hidden;
        start.EnvironmentVariables["DSH_PERMISSION_MODE"] = config.PermissionMode;
        start.EnvironmentVariables["DSH_TELEMETRY_MODE"] = "DISABLED";
        string nodeRoot = Path.GetDirectoryName(paths.NodeExe);
        string pathExtra = nodeRoot + ";" + (Directory.Exists(paths.GitCmd) ? paths.GitCmd + ";" : "");
        start.EnvironmentVariables["PATH"] = pathExtra + (Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
        return Process.Start(start);
    }

    private static bool WaitFor(string url, int timeoutMs)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeoutMs)
        {
            if (IsOnline(url, 1000))
                return true;
            Thread.Sleep(300);
        }
        return false;
    }

    private static bool IsOnline(string url, int timeoutMs)
    {
        try
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                return (int)response.StatusCode >= 200 && (int)response.StatusCode < 500;
        }
        catch
        {
            return false;
        }
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private static void FocusExistingInstance()
    {
        Process current = Process.GetCurrentProcess();
        uint targetPid = 0;
        foreach (Process process in Process.GetProcessesByName(current.ProcessName))
        {
            if (process.Id == current.Id)
                continue;
            targetPid = (uint)process.Id;
            break;
        }
        if (targetPid == 0)
            return;
        // MainWindowHandle is zero for a tray-hidden window, so enumerate
        // top-level windows of the owning PID instead (hidden ones included).
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
        {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (pid == targetPid)
            {
                ShowWindow(hWnd, 9); // SW_RESTORE
                SetForegroundWindow(hWnd);
                return false;
            }
            return true;
        }, IntPtr.Zero);
    }

    // ---- one-click installer (--install / --uninstall) ----

    private const string ExeName = "DeepSeek Harness Window.exe";
    private const string ShortcutName = "DeepSeek Harness Window.lnk";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DeepSeekHarnessWindow";

    private static readonly string[] ShipFiles = new string[]
    {
        ExeName,
        "Microsoft.Web.WebView2.Core.dll",
        "Microsoft.Web.WebView2.WinForms.dll",
        "WebView2Loader.dll"
    };

    private static string InstallDir
    {
        get
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "dsh-window");
        }
    }

    private static string ShortcutPath(Environment.SpecialFolder folder)
    {
        return Path.Combine(Environment.GetFolderPath(folder), ShortcutName);
    }

    /// <summary>Copies the app into %LOCALAPPDATA%\Programs\dsh-window and
    /// creates desktop / start-menu shortcuts plus an uninstall registry entry.</summary>
    private static bool RunInstall(string appDir)
    {
        try
        {
            string target = InstallDir;
            Directory.CreateDirectory(target);
            foreach (string file in ShipFiles)
            {
                string source = Path.Combine(appDir, file);
                if (File.Exists(source))
                    File.Copy(source, Path.Combine(target, file), true);
            }
            string exePath = Path.Combine(target, ExeName);
            if (!File.Exists(exePath))
                throw new InvalidOperationException("未找到应用文件，请把 4 个程序文件与安装器放在同一目录。");

            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            // Plugin-compatible marker: same tag format as GitHub Releases.
            File.WriteAllText(Path.Combine(target, "installed-version.txt"), "v" + version.ToString(3));

            CreateShortcut(ShortcutPath(Environment.SpecialFolder.DesktopDirectory), exePath, target);
            CreateShortcut(ShortcutPath(Environment.SpecialFolder.Programs), exePath, target);

            using (Microsoft.Win32.RegistryKey key =
                Microsoft.Win32.Registry.CurrentUser.CreateSubKey(UninstallKeyPath))
            {
                key.SetValue("DisplayName", "DeepSeek Harness Window");
                key.SetValue("DisplayVersion", version.ToString(3));
                key.SetValue("Publisher", "Community project (not affiliated with DeepSeek)");
                key.SetValue("InstallLocation", target);
                key.SetValue("DisplayIcon", exePath + ",0");
                key.SetValue("UninstallString", Quote(exePath) + " --uninstall");
                key.SetValue("NoModify", 1);
                key.SetValue("NoRepair", 1);
            }

            WriteInstallLog("installed to " + target + " (v" + version.ToString(3) + ")");
            MessageBox.Show(
                "安装完成。\n\n位置：" + target + "\n\n已在桌面和开始菜单创建快捷方式。",
                AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        catch (Exception error)
        {
            MessageBox.Show("安装失败：" + error.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    /// <summary>Removes shortcuts and the uninstall entry, then deletes the
    /// install directory when nothing else remains in it.</summary>
    private static bool RunUninstall()
    {
        try
        {
            TryDelete(ShortcutPath(Environment.SpecialFolder.DesktopDirectory));
            TryDelete(ShortcutPath(Environment.SpecialFolder.Programs));
            try
            {
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false);
            }
            catch
            {
            }

            string target = InstallDir;
            bool removedDir = false;
            try
            {
                if (Directory.Exists(target))
                {
                    foreach (string file in ShipFiles)
                        TryDelete(Path.Combine(target, file));
                    TryDelete(Path.Combine(target, "installed-version.txt"));
                    if (Directory.GetFileSystemEntries(target).Length == 0)
                    {
                        Directory.Delete(target);
                        removedDir = true;
                    }
                }
            }
            catch
            {
            }

            WriteInstallLog("uninstalled, dir removed=" + removedDir);
            MessageBox.Show(
                "已卸载。快捷方式和注册表项已移除。" +
                (removedDir ? "\n安装目录已删除。" : "\n安装目录仍有其它文件，未删除。"),
                AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        catch (Exception error)
        {
            MessageBox.Show("卸载失败：" + error.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    /// <summary>Creates a .lnk via WScript.Shell COM (reflection, no interop assembly).</summary>
    private static void CreateShortcut(string lnkPath, string targetPath, string workDir)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(shellType);
        object lnk = shellType.InvokeMember("CreateShortcut",
            System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
        Type lnkType = lnk.GetType();
        lnkType.InvokeMember("TargetPath",
            System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { targetPath });
        lnkType.InvokeMember("WorkingDirectory",
            System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { workDir });
        lnkType.InvokeMember("IconLocation",
            System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { targetPath + ",0" });
        lnkType.InvokeMember("Save",
            System.Reflection.BindingFlags.InvokeMethod, null, lnk, null);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void WriteInstallLog(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
            File.AppendAllText(LogPath,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [installer] " + message + Environment.NewLine,
                Encoding.UTF8);
        }
        catch
        {
        }
    }
}

internal sealed class MainForm : Form
{
    private readonly WebView2 webView;
    private readonly Process ownedServer;
    private readonly bool killServerOnClose;
    private readonly bool closeToTray;
    private readonly string webUrl;
    private bool serverReady;
    private bool updateCheckQueued;
    private bool reallyExit;
    private NotifyIcon trayIcon;
    private bool trayBalloonShown;

    public MainForm(Process ownedServer, string webUrl, bool serverReady, bool killServerOnClose, bool closeToTray)
    {
        this.ownedServer = ownedServer;
        this.webUrl = webUrl;
        this.serverReady = serverReady;
        this.killServerOnClose = killServerOnClose;
        this.closeToTray = closeToTray;

        Text = ProgramTitle;
        MinimumSize = new Size(800, 560);
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
        }

        webView = new WebView2();
        webView.Dock = DockStyle.Fill;
        Controls.Add(webView);

        if (closeToTray)
            SetupTray();

        Load += OnLoad;
        FormClosed += OnFormClosed;
    }

    private const string ProgramTitle = "DeepSeek Harness";

    private void SetupTray()
    {
        try
        {
            trayIcon = new NotifyIcon();
            trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            trayIcon.Text = ProgramTitle;
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("显示主窗口", null, delegate { ShowFromTray(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate
            {
                reallyExit = true;
                Close();
            });
            trayIcon.ContextMenuStrip = menu;
            trayIcon.DoubleClick += delegate { ShowFromTray(); };
            trayIcon.Visible = true;
            Program.Log("tray icon created");
        }
        catch (Exception error)
        {
            Program.Log("tray setup failed: " + error.Message);
        }
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_CLOSE = 0x0010;
        // WebView2 intercepts WM_CLOSE and re-raises FormClosing with a
        // non-UserClosing reason, so handle the X button here directly.
        if (m.Msg == WM_CLOSE && closeToTray && !reallyExit)
        {
            HideToTray();
            return;
        }
        base.WndProc(ref m);
    }

    private void HideToTray()
    {
        Hide();
        Program.Log("canceled close, hidden to tray");
        if (!trayBalloonShown && trayIcon != null)
        {
            trayBalloonShown = true;
            try
            {
                trayIcon.ShowBalloonTip(2000, ProgramTitle, "已最小化到托盘，双击托盘图标可重新打开。", ToolTipIcon.Info);
            }
            catch
            {
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        Program.Log("form closing: reason=" + e.CloseReason + ", closeToTray=" + closeToTray + ", reallyExit=" + reallyExit);
        if (closeToTray && !reallyExit && e.CloseReason != CloseReason.WindowsShutDown)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnFormClosing(e);
    }

    private async void OnLoad(object sender, EventArgs e)
    {
        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeepSeekHarnessWindow", "WebView2Data");
            CoreWebView2Environment environment =
                await CoreWebView2Environment.CreateAsync(null, userData);
            await webView.EnsureCoreWebView2Async(environment);

            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            webView.CoreWebView2.DocumentTitleChanged += delegate
            {
                string title = webView.CoreWebView2.DocumentTitle;
                if (!string.IsNullOrEmpty(title))
                    Text = title;
            };

            RestoreWindowState();

            if (serverReady)
                webView.CoreWebView2.Navigate(webUrl);
            else
                webView.CoreWebView2.NavigateToString(BuildErrorPage());
        }
        catch (Exception error)
        {
            MessageBox.Show(
                "无法初始化窗口组件（WebView2）。\n\n请安装 Microsoft Edge WebView2 Runtime 后重试：\nhttps://developer.microsoft.com/microsoft-edge/webview2/\n\n错误：" + error.Message,
                ProgramTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
    }

    private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || updateCheckQueued || !serverReady)
            return;
        updateCheckQueued = true;
        Program.Log("navigation completed, queueing update check");
        ThreadPool.QueueUserWorkItem(delegate
        {
            Program.UpdateInfo info = Program.CheckForUpdates();
            if (info != null)
            {
                Program.Log("update available: " + info.LatestVersion);
                try
                {
                    BeginInvoke((Action)delegate { ShowUpdateBanner(info); });
                }
                catch
                {
                }
            }
            else
            {
                Program.Log("update check: up to date");
            }
        });
    }

    private void ShowUpdateBanner(Program.UpdateInfo info)
    {
        try
        {
            string version = (info.LatestVersion ?? "").Replace("'", "").Replace("\"", "");
            string url = (info.ReleaseUrl ?? "").Replace("'", "").Replace("\"", "");
            string js =
                "(function(){" +
                "if(document.getElementById('dshw-update-banner'))return;" +
                "var b=document.createElement('div');b.id='dshw-update-banner';" +
                "b.style.cssText='position:fixed;bottom:16px;right:16px;z-index:99999;background:#2f6fed;color:#fff;" +
                "padding:10px 16px;border-radius:8px;font:13px system-ui;box-shadow:0 2px 10px rgba(0,0,0,.25);" +
                "display:flex;gap:12px;align-items:center';" +
                "b.innerHTML=\"<span>有新版本 " + version + "</span>" +
                "<a href='" + url + "' target='_blank' style='color:#fff;font-weight:600'>查看</a>" +
                "<a href='#' onclick=\\\"this.parentNode.remove();return false\\\" style='color:rgba(255,255,255,.75)'>✕</a>\";" +
                "document.body.appendChild(b);" +
                "})();";
            webView.CoreWebView2.ExecuteScriptAsync(js);
        }
        catch (Exception error)
        {
            Program.Log("update banner failed: " + error.Message);
        }
    }

    private string BuildErrorPage()
    {
        return
            "<html><head><meta charset=\"utf-8\"><title>连接失败 - " + ProgramTitle + "</title>" +
            "<style>body{font-family:'Segoe UI',system-ui,sans-serif;background:#f5f6f8;display:flex;" +
            "align-items:center;justify-content:center;height:100vh;margin:0;color:#1f2328}" +
            ".card{background:#fff;padding:40px 48px;border-radius:12px;box-shadow:0 2px 12px rgba(0,0,0,.08);" +
            "max-width:480px;text-align:center}h1{font-size:20px;margin:0 0 12px}" +
            "p{color:#59636e;font-size:14px;line-height:1.6;margin:0 0 24px}" +
            "button{background:#2f6fed;color:#fff;border:none;border-radius:8px;padding:10px 28px;" +
            "font-size:14px;cursor:pointer}button:hover{background:#2557c0}" +
            "a{display:block;margin-top:16px;color:#59636e;font-size:13px}</style></head><body>" +
            "<div class=\"card\"><h1>DeepSeek Harness 服务没有响应</h1>" +
            "<p>启动本地服务后没有在预期时间内连上。点下面的按钮再试一次。</p>" +
            "<button onclick=\"window.chrome.webview.postMessage('retry')\">重试</button>" +
            "<a href=\"" + webUrl + "\" target=\"_blank\">在浏览器中打开</a></div></body></html>";
    }

    private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            if (e.TryGetWebMessageAsString() != "retry")
                return;
            serverReady = Program.EnsureServer();
            if (serverReady)
                webView.CoreWebView2.Navigate(webUrl);
            else
                webView.CoreWebView2.Reload();
        }
        catch
        {
        }
    }

    private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private string StatePath
    {
        get
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeepSeekHarnessWindow", "window-state.json");
        }
    }

    private void RestoreWindowState()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                StartPosition = FormStartPosition.CenterScreen;
                Size = new Size(1440, 900);
                return;
            }
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> state =
                serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(StatePath, Encoding.UTF8));
            if (state == null)
                return;

            int x = state.ContainsKey("x") ? Convert.ToInt32(state["x"]) : int.MinValue;
            int y = state.ContainsKey("y") ? Convert.ToInt32(state["y"]) : int.MinValue;
            int w = state.ContainsKey("w") ? Convert.ToInt32(state["w"]) : 0;
            int h = state.ContainsKey("h") ? Convert.ToInt32(state["h"]) : 0;
            bool maximized = state.ContainsKey("maximized") && Convert.ToBoolean(state["maximized"]);

            // Migration: v1.x saved 96-DPI virtualized pixels (process was
            // DPI-unaware); since 1.2.1 the process is per-monitor aware and
            // works in physical pixels. Scale old values by the system DPI
            // ratio once, then persist ver=2 on the next save.
            bool legacy = !state.ContainsKey("ver") || Convert.ToInt32(state["ver"]) < 2;
            if (legacy)
            {
                float scale = GetSystemDpiScale();
                if (scale > 1f)
                {
                    x = (int)Math.Round(x * scale);
                    y = (int)Math.Round(y * scale);
                    w = (int)Math.Round(w * scale);
                    h = (int)Math.Round(h * scale);
                }
            }

            bool onScreen = false;
            if (w >= MinimumSize.Width && h >= MinimumSize.Height)
            {
                Rectangle bounds = new Rectangle(x, y, w, h);
                foreach (Screen screen in Screen.AllScreens)
                {
                    if (screen.WorkingArea.IntersectsWith(bounds))
                    {
                        onScreen = true;
                        break;
                    }
                }
            }
            if (onScreen)
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(x, y, w, h);
            }
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
                Size = new Size(1440, 900);
            }
            if (maximized)
                WindowState = FormWindowState.Maximized;
        }
        catch
        {
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1440, 900);
        }
    }

    /// <summary>System DPI ratio (primary monitor DPI / 96) for one-time
    /// migration of pre-1.2.1 window state.</summary>
    private static float GetSystemDpiScale()
    {
        try
        {
            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
            {
                if (graphics.DpiX > 0)
                    return graphics.DpiX / 96f;
            }
        }
        catch
        {
        }
        return 1f;
    }

    private void SaveWindowState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath));
            Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            Dictionary<string, object> state = new Dictionary<string, object>();
            state["x"] = bounds.X;
            state["y"] = bounds.Y;
            state["w"] = bounds.Width;
            state["h"] = bounds.Height;
            state["maximized"] = WindowState == FormWindowState.Maximized;
            state["ver"] = 2;
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            File.WriteAllText(StatePath, serializer.Serialize(state), Encoding.UTF8);
        }
        catch
        {
        }
    }

    private void OnFormClosed(object sender, FormClosedEventArgs e)
    {
        Program.Log("form closed, disposing tray and owned server");
        SaveWindowState();
        if (trayIcon != null)
        {
            try
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
            catch
            {
            }
        }
        if (killServerOnClose && ownedServer != null)
        {
            try
            {
                ownedServer.Kill();
                ownedServer.Dispose();
            }
            catch
            {
            }
        }
    }
}
