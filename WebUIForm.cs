using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace HorizonSimTool;

internal sealed class WebUIForm : Form
{
    private readonly WebView2 _webView = new();
    private WebBridge? _bridge;
    private bool _webViewReady;
    private readonly Queue<string> _pendingMessages = new();

    public WebUIForm()
    {
        Text = "Horizon SimTool";
        Width = 1480;
        Height = 900;
        MinimumSize = new Size(1000, 680);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(13, 17, 23);

        var appIcon = TryLoadIcon();
        if (appIcon is not null) Icon = appIcon;

        _webView.Dock = DockStyle.Fill;
        _webView.DefaultBackgroundColor = Color.FromArgb(13, 17, 23);
        Controls.Add(_webView);

        _webView.CoreWebView2InitializationCompleted += OnInitCompleted;
        _webView.WebMessageReceived += OnWebMessageReceived;

        _ = InitWebViewAsync();
    }

    private async Task InitWebViewAsync()
    {
        var dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HorizonSimTool", "WebView2");
        var env = await CoreWebView2Environment.CreateAsync(null, dataFolder);
        await _webView.EnsureCoreWebView2Async(env);
    }

    private void OnInitCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            MessageBox.Show(
                "WebView2 initialization failed: " + e.InitializationException?.Message
                + "\n\nMake sure the WebView2 Runtime is installed (it ships with Windows 11 and Edge).",
                "Horizon SimTool",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "app.local", wwwroot, CoreWebView2HostResourceAccessKind.Allow);

        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

        _bridge = new WebBridge(this);
        _webViewReady = true;

        foreach (var msg in _pendingMessages)
        {
            _webView.CoreWebView2.PostWebMessageAsJson(msg);
        }
        _pendingMessages.Clear();

        _webView.CoreWebView2.Navigate("https://app.local/index.html");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        _bridge?.HandleMessage(e);
    }

    public Task InvokeAsync(Action action)
    {
        var tcs = new TaskCompletionSource();
        BeginInvoke(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>();
        BeginInvoke(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    public void PostMessage(string json)
    {
        if (!IsDisposed && !_webView.IsDisposed)
        {
            if (_webViewReady)
            {
                _webView.CoreWebView2.PostWebMessageAsJson(json);
            }
            else
            {
                _pendingMessages.Enqueue(json);
            }
        }
    }

    private static Icon? TryLoadIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "HorizonSimTool.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : null;
    }
}
