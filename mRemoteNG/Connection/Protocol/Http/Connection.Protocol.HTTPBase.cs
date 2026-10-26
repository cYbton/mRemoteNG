using System;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using mRemoteNG.Tools;
using mRemoteNG.App;
using mRemoteNG.UI.Tabs;
using mRemoteNG.Resources.Language;
using System.Runtime.Versioning;
using System.Windows.Forms.VisualStyles;
using System.IO;
using System.Threading.Tasks;


namespace mRemoteNG.Connection.Protocol.Http
{
    [SupportedOSPlatform("windows")]
    public class HTTPBase : ProtocolBase
    {
        #region Private Properties

        private Control _wBrowser;
        private string _tabTitle;
        protected string httpOrS;
        protected int defaultPort;
        private string _userDataFolder;
        private CoreWebView2Environment? _webView2Environment;
        private Task _webView2InitializationTask;

        // sysprep fork: login to the web interface's login form (see WebLogin)
        private readonly WebLogin _webLogin = new();
        private System.Windows.Forms.Timer? _automaticLoginTimer;
        private DateTime _automaticLoginUntil;

        #endregion

        #region Public Methods

        protected HTTPBase(RenderingEngine renderingEngine)
        {
            try
            {
                if (renderingEngine == RenderingEngine.EdgeChromium)
                {
                    // Create a unique user data folder for each WebView2 instance
                    // This prevents session sharing between multiple HTTP/HTTPS connections
                    _userDataFolder = Path.Combine(
                        Path.GetTempPath(),
                        "mRemoteNG_WebView2",
                        Guid.NewGuid().ToString()
                    );
                    
                    Control = new Microsoft.Web.WebView2.WinForms.WebView2()
                    {
                        Dock = DockStyle.Fill,
                    };
                }
                else
                {
                    Control = new WebBrowser();
                }
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.HttpConnectionFailed, ex);
            }
        }

        public override bool Initialize()
        {
            base.Initialize();

            try
            {
                if (InterfaceControl.Parent is ConnectionTab objConnectionTab) _tabTitle = objConnectionTab.TabText;
            }
            catch (Exception)
            {
                _tabTitle = "";
            }

            try
            {
                _wBrowser = Control;

                if (InterfaceControl.Info.RenderingEngine == RenderingEngine.EdgeChromium)
                {
                    Microsoft.Web.WebView2.WinForms.WebView2 edge = (Microsoft.Web.WebView2.WinForms.WebView2)_wBrowser;
                    edge.CoreWebView2InitializationCompleted += Edge_CoreWebView2InitializationCompleted;
                    
                    // Initialize WebView2 with unique user data folder asynchronously
                    _webView2InitializationTask = InitializeWebView2Async(edge);
                    AddLoginBar(edge);
                }
                else
                {
                    WebBrowser objWebBrowser = (WebBrowser)_wBrowser;
                    objWebBrowser.ScrollBarsEnabled = true;

                    // http://stackoverflow.com/questions/4655662/how-to-ignore-script-errors-in-webbrowser
                    objWebBrowser.ScriptErrorsSuppressed = true;

                    objWebBrowser.Navigated += WBrowser_Navigated;
                    objWebBrowser.DocumentTitleChanged += WBrowser_DocumentTitleChanged;
                }

                return true;
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.HttpSetPropsFailed, ex);
                return false;
            }
        }

        private async Task InitializeWebView2Async(Microsoft.Web.WebView2.WinForms.WebView2 webView2)
        {
            try
            {
                // Create the WebView2 environment with a unique user data folder
                _webView2Environment = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
                
                // Initialize the WebView2 control with the custom environment
                await webView2.EnsureCoreWebView2Async(_webView2Environment);
                
                // Prevent popups from opening in new windows
                webView2.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                webView2.CoreWebView2.ServerCertificateErrorDetected += CoreWebView2_ServerCertificateErrorDetected;
                webView2.CoreWebView2.BasicAuthenticationRequested += CoreWebView2_BasicAuthenticationRequested;
                StartAutomaticLogin(webView2);
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.HttpSetPropsFailed, ex);
            }
        }

        public override bool Connect()
        {
            try
            {
                if (InterfaceControl.Info.RenderingEngine == RenderingEngine.EdgeChromium)
                {
                    var webView2 = (Microsoft.Web.WebView2.WinForms.WebView2)_wBrowser;
                    
                    // Wait for WebView2 initialization to complete before connecting
                    if (_webView2InitializationTask != null && !_webView2InitializationTask.IsCompleted)
                    {
                        // Schedule navigation after initialization completes
                        _webView2InitializationTask.ContinueWith(t =>
                        {
                            if (t.IsCompletedSuccessfully && webView2.CoreWebView2 != null)
                            {
                                // Use Invoke to ensure we're on the UI thread
                                if (webView2.InvokeRequired)
                                {
                                    webView2.Invoke(new Action(() => webView2.Source = new Uri(GetUrl())));
                                }
                                else
                                {
                                    webView2.Source = new Uri(GetUrl());
                                }
                            }
                            else if (t.IsFaulted)
                            {
                                Runtime.MessageCollector.AddExceptionStackTrace(Language.HttpConnectFailed, t.Exception);
                            }
                        }, 
                        // Use UI thread scheduler if available, otherwise use default
                        System.Threading.SynchronizationContext.Current != null 
                            ? TaskScheduler.FromCurrentSynchronizationContext() 
                            : TaskScheduler.Default);
                    }
                    else if (webView2.CoreWebView2 != null)
                    {
                        // WebView2 is already initialized, navigate immediately
                        webView2.Source = new Uri(GetUrl());
                    }
                }
                else
                {
                    ((WebBrowser)_wBrowser).Navigate(GetUrl());

                }

                base.Connect();
                return true;
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.HttpConnectFailed, ex);
                return false;
            }
        }

        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            // Suppress the popup (prevent it from opening in a new window)
            e.Handled = true;
        }

        // sysprep fork: the lab's web interfaces (firewall, Citrix, ...) use self-signed certificates - accepted in
        // this tab instead of showing the certificate warning page
        private void CoreWebView2_ServerCertificateErrorDetected(object? sender, CoreWebView2ServerCertificateErrorDetectedEventArgs e)
        {
            e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
        }

        // sysprep fork: the connection's username and password answer an HTTP authentication prompt (Basic/NTLM,
        // e.g. Windows Admin Center); a login form on the page itself is filled in by WebLogin
        private void CoreWebView2_BasicAuthenticationRequested(object? sender, CoreWebView2BasicAuthenticationRequestedEventArgs e)
        {
            ConnectionInfo? info = InterfaceControl?.Info;
            if (info == null || string.IsNullOrEmpty(info.Username)) return;
            e.Response.UserName = string.IsNullOrEmpty(info.Domain) || info.Username.Contains('\\') || info.Username.Contains('@')
                ? info.Username
                : $@"{info.Domain}\{info.Username}";
            e.Response.Password = info.Password ?? string.Empty;
        }

        // sysprep fork: a slim bar above the page - "Log in" fills in the connection's username and password and
        // presses Enter, for when the automatic login didn't catch the form (e.g. it appeared late)
        private void AddLoginBar(Microsoft.Web.WebView2.WinForms.WebView2 webView2)
        {
            ToolStrip bar = new() { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System };
            ToolStripButton login = new("Log in", mRemoteNG.Properties.Resources.Key_16x)
            {
                ToolTipText = "Fill in the connection's username and password and press Enter",
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText
            };
            login.Click += async (sender, args) =>
            {
                if (webView2.CoreWebView2 == null) return;
                webView2.Focus();
                if (!await _webLogin.LoginAsync(webView2.CoreWebView2, InterfaceControl.Info.Username ?? "", InterfaceControl.Info.Password ?? "", automatic: false))
                    Runtime.MessageCollector.AddMessage(mRemoteNG.Messages.MessageClass.InformationMsg, "No login form found on this page, or the connection has no username.", true);
            };
            ToolStripButton reload = new("Reload", mRemoteNG.Properties.Resources.Refresh_16x) { DisplayStyle = ToolStripItemDisplayStyle.ImageAndText };
            reload.Click += (sender, args) => webView2.CoreWebView2?.Reload();
            bar.Items.AddRange([login, reload]);

            mRemoteNG.Themes.ThemeManager themeManager = mRemoteNG.Themes.ThemeManager.getInstance();
            if (themeManager.ThemingActive)
                new WeifenLuo.WinFormsUI.Docking.VisualStudioToolStripExtender().SetStyle(bar, themeManager.ActiveTheme.Version, themeManager.ActiveTheme.Theme);

            InterfaceControl.Controls.Add(bar);
            bar.SendToBack(); // docked before the page, so the page fills the space below the bar
        }

        // sysprep fork: tries the login form every second while the tab is new - forms of Angular apps like Citrix
        // Web Studio appear only after the page has loaded. WebLogin stops after a login or a failed one.
        private void StartAutomaticLogin(Microsoft.Web.WebView2.WinForms.WebView2 webView2)
        {
            if (string.IsNullOrEmpty(InterfaceControl.Info.Username)) return;
            _automaticLoginUntil = DateTime.UtcNow.AddSeconds(60);
            _automaticLoginTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _automaticLoginTimer.Tick += async (sender, args) =>
            {
                if (DateTime.UtcNow > _automaticLoginUntil || _webLogin.AutomaticLoginDone || webView2.IsDisposed)
                {
                    _automaticLoginTimer?.Stop();
                    return;
                }
                if (webView2.CoreWebView2 == null) return;
                await _webLogin.LoginAsync(webView2.CoreWebView2, InterfaceControl.Info.Username ?? "", InterfaceControl.Info.Password ?? "", automatic: true);
            };
            _automaticLoginTimer.Start();
        }

        #endregion

        #region Private Methods

        private string GetUrl()
        {
            try
            {
                string strHost = InterfaceControl.Info.Hostname;

                if (InterfaceControl.Info.Port != defaultPort)
                {
                    if (strHost.EndsWith("/"))
                    {
                        strHost = strHost[..^1];
                    }

                    if (strHost.Contains(httpOrS + "://") == false)
                    {
                        strHost = httpOrS + "://" + strHost;
                    }

                    strHost = strHost + ":" + InterfaceControl.Info.Port;
                }
                else
                {
                    if (strHost.Contains(httpOrS + "://") == false)
                        strHost = httpOrS + "://" + strHost;
                }

                return strHost;
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.HttpFailedUrlBuild, ex);
                return string.Empty;
            }
        }

        #endregion

        #region Events

        private void Edge_CoreWebView2InitializationCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2InitializationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.HttpFailedUrlBuild, e.InitializationException);
            }
        }

        private void WBrowser_Navigated(object sender, WebBrowserNavigatedEventArgs e)
        {
            if (_wBrowser is not WebBrowser objWebBrowser) return;

            // This can only be set once the WebBrowser control is shown, it will throw a COM exception otherwise.
            objWebBrowser.AllowWebBrowserDrop = false;

            objWebBrowser.Navigated -= WBrowser_Navigated;
        }

        private void WBrowser_DocumentTitleChanged(object sender, EventArgs e)
        {
            try
            {
                if (InterfaceControl.Parent is not ConnectionTab tabP) return;
                string shortTitle;
                if (((WebBrowser)_wBrowser).DocumentTitle.Length >= 15)
                {
                    shortTitle = ((WebBrowser)_wBrowser).DocumentTitle[..10] + "...";
                }
                else
                {
                    shortTitle = ((WebBrowser)_wBrowser).DocumentTitle;
                }

                if (!string.IsNullOrEmpty(_tabTitle))
                {
                   tabP.TabText = _tabTitle + @" - " + shortTitle;
                }
                else
                {
                   tabP.TabText = shortTitle;
                }
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.HttpDocumentTileChangeFailed, ex);
            }
        }

        #endregion

        #region Cleanup

        public override void Close()
        {
            _automaticLoginTimer?.Stop();
            _automaticLoginTimer?.Dispose();
            _automaticLoginTimer = null;

            try
            {
                // Wait for initialization to complete before disposing (non-blocking approach)
                if (_webView2InitializationTask != null && !_webView2InitializationTask.IsCompleted)
                {
                    // Create a continuation to dispose after initialization completes
                    var cleanupTask = _webView2InitializationTask.ContinueWith(_ => 
                    {
                        DisposeWebView2Environment();
                    }, TaskScheduler.Default); // Use default scheduler to avoid UI thread issues
                    
                    // Give it a reasonable time to complete, but don't block indefinitely
                    // Using a background thread to avoid blocking UI thread
                    Task.Run(() =>
                    {
                        if (!cleanupTask.Wait(TimeSpan.FromSeconds(2)))
                        {
                            // Initialization is taking too long, log and continue
                            Runtime.MessageCollector.AddMessage(mRemoteNG.Messages.MessageClass.WarningMsg, 
                                "WebView2 initialization did not complete in time during cleanup");
                        }
                    });
                }
                else
                {
                    DisposeWebView2Environment();
                }
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace("Error during HTTPBase cleanup", ex);
            }
            
            base.Close();
        }

        private void DisposeWebView2Environment()
        {
            try
            {
                // There is no Dispose method for CoreWebView2Environment, so just set to null
                _webView2Environment = null;
                
                // Clean up the temporary user data folder
                if (!string.IsNullOrEmpty(_userDataFolder) && Directory.Exists(_userDataFolder))
                {
                    try
                    {
                        // Verify the path is within the expected temp directory for safety
                        string tempPath = Path.GetTempPath();
                        string fullUserDataPath = Path.GetFullPath(_userDataFolder);
                        
                        if (fullUserDataPath.StartsWith(Path.GetFullPath(tempPath), StringComparison.OrdinalIgnoreCase) &&
                            fullUserDataPath.Contains("mRemoteNG_WebView2"))
                        {
                            Directory.Delete(_userDataFolder, true);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log but don't throw - cleanup is best effort
                        Runtime.MessageCollector.AddExceptionStackTrace("Failed to clean up WebView2 user data folder", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace("Error disposing WebView2 environment", ex);
            }
        }

        #endregion

        #region Enums

        public enum RenderingEngine
        {
            [LocalizedAttributes.LocalizedDescription(nameof(Language.HttpInternetExplorer))]
            IE = 1,

            [LocalizedAttributes.LocalizedDescription(nameof(Language.HttpCEF))]
            EdgeChromium = 2
        }

        #endregion
    }
}
