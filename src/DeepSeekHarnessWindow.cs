using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

[assembly: AssemblyTitle("DeepSeek Harness")]
[assembly: AssemblyDescription("DeepSeek Harness standalone window (WebView2)")]
[assembly: AssemblyCompany("Local launcher for DeepSeek Harness")]
[assembly: AssemblyProduct("DeepSeek Harness Window")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

internal static class Program
{
    private const string WebUrl = "http://127.0.0.1:3080/";
    private const string PickerPatch =
        "- id: directory-picker\r\n" +
        "  disabled: true\r\n" +
        "- insert:\r\n" +
        "    - id: directory-picker-browse\r\n" +
        "      name: '@deepseek-ai/dsh-host-directory-picker-browse'\r\n" +
        "    - id: directory-picker-browse-ui\r\n" +
        "      name: '@deepseek-ai/dsh-client-ui-directory-picker-browse'\r\n";

    private static Process spawnedServer;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            bool checkOnly = HasFlag(args, "--check");
            bool noWindow = HasFlag(args, "--no-window") || HasFlag(args, "--no-open");

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string nodeRoot = Path.Combine(localAppData, "Programs", "nodejs");
            string nodeExe = Path.Combine(nodeRoot, "node.exe");
            string dshEntry = Path.Combine(nodeRoot, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
            string gitRoot = Path.Combine(localAppData, "Programs", "Git", "cmd");
            string launcherData = Path.Combine(localAppData, "DeepSeekHarnessLauncher");
            string patchPath = Path.Combine(launcherData, "directory-picker.patch.yml");

            if (!File.Exists(nodeExe) || !File.Exists(dshEntry))
            {
                MessageBox.Show(
                    "找不到已安装的 DeepSeek Harness。\n\n请先运行：npm.cmd install -g @deepseek-ai/dsh",
                    "DeepSeek Harness",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 2;
            }

            Directory.CreateDirectory(launcherData);
            File.WriteAllText(patchPath, PickerPatch);

            if (checkOnly)
                return 0;

            if (!IsOnline(WebUrl, 1200))
            {
                spawnedServer = StartHarness(nodeExe, dshEntry, patchPath, nodeRoot, gitRoot);

                if (!WaitFor(WebUrl, 30000))
                {
                    MessageBox.Show(
                        "Harness 已启动，但 30 秒内没有响应。\n\n可运行 Start-DeepSeek-Harness-FullAccess.cmd 查看详细日志。",
                        "DeepSeek Harness",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return 3;
                }
            }

            if (noWindow)
                return 0;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(spawnedServer, WebUrl));
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "DeepSeek Harness", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
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

    private static Process StartHarness(string nodeExe, string dshEntry, string patchPath, string nodeRoot, string gitRoot)
    {
        ProcessStartInfo start = new ProcessStartInfo();
        start.FileName = nodeExe;
        start.Arguments = Quote(dshEntry) + " --profile web --patch " + Quote(patchPath) +
            " --host 127.0.0.1 --port 3080";
        start.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.WindowStyle = ProcessWindowStyle.Hidden;
        start.EnvironmentVariables["DSH_PERMISSION_MODE"] = "danger-full-access";
        start.EnvironmentVariables["DSH_TELEMETRY_MODE"] = "DISABLED";
        start.EnvironmentVariables["PATH"] = nodeRoot + ";" + gitRoot + ";" +
            (Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
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
}

internal sealed class MainForm : Form
{
    private readonly WebView2 webView;
    private readonly Process ownedServer;
    private readonly string webUrl;

    public MainForm(Process ownedServer, string webUrl)
    {
        this.ownedServer = ownedServer;
        this.webUrl = webUrl;
        Text = "DeepSeek Harness";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1440, 900);
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
            webView.CoreWebView2.DocumentTitleChanged += delegate
            {
                string title = webView.CoreWebView2.DocumentTitle;
                if (!string.IsNullOrEmpty(title))
                    Text = title;
            };
            webView.CoreWebView2.Navigate(webUrl);
        }
        catch (Exception error)
        {
            MessageBox.Show(
                "无法初始化窗口组件（WebView2）。\n\n请安装 Microsoft Edge WebView2 Runtime 后重试：\nhttps://developer.microsoft.com/microsoft-edge/webview2/\n\n错误：" + error.Message,
                "DeepSeek Harness",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
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

    private void OnFormClosed(object sender, FormClosedEventArgs e)
    {
        if (ownedServer != null)
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
