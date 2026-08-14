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
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

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

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            bool checkOnly = HasFlag(args, "--check");
            bool noWindow = HasFlag(args, "--no-window") || HasFlag(args, "--no-open");

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            paths = ResolvePaths(appDir, localAppData);
            config = LoadConfig(Path.Combine(appDir, "dsh-window.config.json"));
            appUrl = "http://" + config.Host + ":" + config.Port.ToString(CultureInfo.InvariantCulture) + "/";
            patchPath = Path.Combine(localAppData, "DeepSeekHarnessLauncher", "directory-picker.patch.yml");

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
            Application.Run(new MainForm(spawnedServer, appUrl, serverReady, config.KillServerOnClose));
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
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

    private static void FocusExistingInstance()
    {
        Process current = Process.GetCurrentProcess();
        foreach (Process process in Process.GetProcessesByName(current.ProcessName))
        {
            if (process.Id == current.Id)
                continue;
            IntPtr handle = process.MainWindowHandle;
            if (handle != IntPtr.Zero)
            {
                ShowWindow(handle, 9); // SW_RESTORE
                SetForegroundWindow(handle);
                return;
            }
        }
    }
}

internal sealed class MainForm : Form
{
    private readonly WebView2 webView;
    private readonly Process ownedServer;
    private readonly bool killServerOnClose;
    private readonly string webUrl;
    private bool serverReady;

    public MainForm(Process ownedServer, string webUrl, bool serverReady, bool killServerOnClose)
    {
        this.ownedServer = ownedServer;
        this.webUrl = webUrl;
        this.serverReady = serverReady;
        this.killServerOnClose = killServerOnClose;

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

        Load += OnLoad;
        FormClosed += OnFormClosed;
    }

    private const string ProgramTitle = "DeepSeek Harness";

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
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            File.WriteAllText(StatePath, serializer.Serialize(state), Encoding.UTF8);
        }
        catch
        {
        }
    }

    private void OnFormClosed(object sender, FormClosedEventArgs e)
    {
        SaveWindowState();
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
