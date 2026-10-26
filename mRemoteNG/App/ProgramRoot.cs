﻿using Microsoft.IdentityModel.Tokens;

using mRemoteNG.App.Update;
using mRemoteNG.Config.Settings;
using mRemoteNG.Messages;
using mRemoteNG.Themes;
using mRemoteNG.UI.Forms;
using mRemoteNG.Resources.Language;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Globalization;



namespace mRemoteNG.App
{
    [SupportedOSPlatform("windows")]
    public static class ProgramRoot
    {
        private static Mutex? _mutex;
        private static string customResourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages");

        private static System.Threading.Thread? _wpfSplashThread;
        private static FrmSplashScreenNew? _wpfSplash;

        public static event EventHandler? UiCultureChanged;

        [STAThread]
        public static void Main(string[] args)
        {
            // sysprep fork: Hyper-Lab starts a newly unpacked mRemoteNG.exe once with --warmup, which ends right
            // here. Windows checks a new exe on its first start, before any of its code runs - with no window to
            // show for it, the first start by a user would otherwise hang for seconds without a splash screen.
            if (args.Length == 1 && string.Equals(args[0], "--warmup", StringComparison.OrdinalIgnoreCase))
                return;

            // Must be called before any other WinForms / Application.* usage so that
            // per-monitor font scaling and hit-testing are initialised correctly from
            // the very first UI operation (dialogs shown in MainAsync, exception
            // handlers, EnableVisualStyles, …).  The app manifest already declares
            // PerMonitorV2 awareness; this call keeps the WinForms runtime in sync.
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            // sysprep fork: the splash comes first, on its own thread - everything below (SQLite, runtime check,
            // settings) is loaded while it is already showing. On the first start of a version, Defender checks
            // every newly loaded file, and nothing showed that mRemoteNG was starting.
            MainEnteredUtc = DateTime.UtcNow;
            ShowSplashOnStaThread();

            InitializeSqliteProvider();

            // Ensure the real entry point is definitely STA
            MainAsync(args).GetAwaiter().GetResult();
        }

        private static Task MainAsync(string[] args)
        {
            if (!ShouldSkipNativeRuntimeChecks(args))
            {
                // Runtime checks only needed for framework-dependent deployments
                // Self-contained builds include the runtime, so no check is needed
                // Note: .NET runtime check is not needed here — the .NET host (apphost)
                // natively displays a missing-runtime dialog with a download link.

                var checkFail = false;

                // Checking Visual C++ Redistributable version
                if (VCppRuntimeCheck.GetInstalledVcRedistVersions() == null || VCppRuntimeCheck.GetInstalledVcRedistVersions().Count == 0)
                {
                    var downloadUrl2 = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
                    try
                    {
                        var result = ShowDownloadCancelDialog(
                            $"A Visual C++ (MSVC) " + Language.MsgRuntimeIsRequired + "\n\n" +
                            Language.MsgDownloadLatestRuntime + "\n" + downloadUrl2 + "\n\n" +
                            Language.MsgExit + "\n\n",
                            Language.MsgMissingRuntime + " Visual C++ Redistributable x64");

                        if (result == DialogResult.OK && InternetConnection.IsPosible())
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo(fileName: downloadUrl2) { UseShellExecute = true });
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Unable to open download link: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                    }
                    catch { }
                    checkFail = true;
                }

                if (checkFail)
                {
                    Environment.Exit(0);
                }
            }

            Lazy<bool> singleInstanceOption = new(() => Properties.OptionsStartupExitPage.Default.SingleInstance);
            if (singleInstanceOption.Value)
                StartApplicationAsSingleInstance();
            else
                StartApplication();

            return Task.CompletedTask;
        }

        private static void InitializeSqliteProvider()
        {
            try
            {
                Type batteriesType = Type.GetType("SQLitePCL.Batteries_V2, SQLitePCLRaw.batteries_v2", throwOnError: false);
                MethodInfo initMethod = batteriesType?.GetMethod("Init", BindingFlags.Public | BindingFlags.Static);
                initMethod?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SQLite provider initialization failed: {ex}");
            }
        }

        internal static bool IsPortableBuild
        {
            get
            {
#if PORTABLE
                return true;
#else
                return false;
#endif
            }
        }

        internal static bool ShouldSkipNativeRuntimeChecks(string[] args)
        {
            if (IsPortableBuild)
            {
                return true;
            }

            foreach (string arg in args)
            {
                if (string.Equals(arg, "--skip-runtime-checks", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            string? envValue = Environment.GetEnvironmentVariable("MREMOTENG_SKIP_RUNTIME_CHECKS");
            if (string.IsNullOrWhiteSpace(envValue))
            {
                return false;
            }
            if (string.Equals(envValue, "1", StringComparison.Ordinal))
            {
                return true;
            }

            return bool.TryParse(envValue, out bool skipChecks) && skipChecks;
        }

        // Assembly resolve handler
        private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
        {
            try
            {
                var requestedAssemblyName = new AssemblyName(args.Name);
                string assemblyName = requestedAssemblyName.Name ?? string.Empty;

                if (assemblyName.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                {
                    return ResolveSatelliteAssembly(args, requestedAssemblyName);
                }

                string assemblyFile = assemblyName + ".dll";
                string assemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assemblies", assemblyFile);

                if (File.Exists(assemblyPath))
                    return Assembly.LoadFrom(assemblyPath);

                // Fall back to the Plugins\ folder for plugin-shared assemblies (e.g. mRp.Contracts)
                // that are deployed alongside the plugin DLLs instead of under Assemblies\.
                string pluginsAssemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", assemblyFile);
                if (File.Exists(pluginsAssemblyPath))
                    return Assembly.LoadFrom(pluginsAssemblyPath);
            }
            catch
            {
                // Suppress resolution exceptions; return null to continue standard probing
            }
            return null;
        }

        private static Assembly? ResolveSatelliteAssembly(ResolveEventArgs args, AssemblyName requestedAssemblyName)
        {
            string? cultureName = requestedAssemblyName.CultureName;
            if (string.IsNullOrWhiteSpace(cultureName))
                return null;

            string satelliteAssemblyFileName = (requestedAssemblyName.Name ?? string.Empty) + ".dll";
            if (string.IsNullOrWhiteSpace(satelliteAssemblyFileName))
                return null;

            string appBaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string cultureSpecificPath = Path.Combine(customResourcePath, cultureName, satelliteAssemblyFileName);
            if (File.Exists(cultureSpecificPath))
            {
                var baseFull = Path.GetFullPath(customResourcePath);
                var fullPath = Path.GetFullPath(cultureSpecificPath);
                if (!fullPath.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && fullPath != baseFull)
                {
                    throw new ArgumentException("Invalid file path");
                }
                return Assembly.LoadFrom(fullPath);
            }

            if (args.RequestingAssembly is null)
                return null;

            string requestingAssemblyName = args.RequestingAssembly.GetName().Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(requestingAssemblyName))
                return null;

            string localizedAssemblyPath = Path.Combine(appBaseDirectory, "Languages", cultureName, requestingAssemblyName + ".resources.dll");
            if (File.Exists(localizedAssemblyPath))
            {
                var baseFull = Path.GetFullPath(Path.Combine(appBaseDirectory, "Languages"));
                var fullPath = Path.GetFullPath(localizedAssemblyPath);
                if (!fullPath.StartsWith(baseFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && fullPath != baseFull)
                {
                    throw new ArgumentException("Invalid file path");
                }
                return Assembly.LoadFrom(fullPath);
            }
            return null;
        }

        public static void ApplyUiCulture(string? cultureName)
        {
            CultureInfo uiCulture = string.IsNullOrWhiteSpace(cultureName)
                ? CultureInfo.InstalledUICulture
                : new CultureInfo(cultureName);

            Thread.CurrentThread.CurrentUICulture = uiCulture;
            CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
            UiCultureChanged?.Invoke(null, EventArgs.Empty);
        }

        private static void StartApplication()
        {
            CatchAllUnhandledExceptions();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Match the OS dark mode for common controls (scrollbars, context menus, ...)
            // to the active theme. Applied once at startup; theme changes require a restart.
            // Read the persisted flag instead of constructing ThemeManager here, so we avoid
            // any theme folder/file I/O before the splash is shown. The flag is kept in sync
            // by ThemeManager whenever the active theme or theming state changes.
            Application.SetColorMode(Properties.OptionsThemePage.Default.IsActiveThemeDark
                ? SystemColorMode.Dark
                : SystemColorMode.Classic);

            Application.Run(FrmMain.Default);
        }

        public static void CloseSingletonInstanceMutex()
        {
            _mutex?.Close();
        }

        private static void StartApplicationAsSingleInstance()
        {
            const string mutexID = "mRemoteNG_SingleInstanceMutex";
            _mutex = new Mutex(false, mutexID, out bool newInstanceCreated);
            if (!newInstanceCreated)
            {
                SwitchToCurrentInstance();
                return;
            }

            StartApplication();
            GC.KeepAlive(_mutex);
        }

        private static void SwitchToCurrentInstance()
        {
            IntPtr singletonInstanceWindowHandle = GetRunningSingletonInstanceWindowHandle();
            if (singletonInstanceWindowHandle == IntPtr.Zero) return;
            if (NativeMethods.IsIconic(singletonInstanceWindowHandle) != 0)
                _ = NativeMethods.ShowWindow(singletonInstanceWindowHandle, (int)NativeMethods.SW_RESTORE);
            NativeMethods.SetForegroundWindow(singletonInstanceWindowHandle);
        }

        private static IntPtr GetRunningSingletonInstanceWindowHandle()
        {
            IntPtr windowHandle = IntPtr.Zero;
            Process currentProcess = Process.GetCurrentProcess();
            foreach (Process enumeratedProcess in Process.GetProcessesByName(currentProcess.ProcessName))
            {
                // Safely check for null MainModule and FileName
                string? enumeratedFileName = null;
                string? currentFileName = null;
                try
                {
                    enumeratedFileName = enumeratedProcess.MainModule?.FileName;
                    currentFileName = currentProcess.MainModule?.FileName;
                }
                catch
                {
                    // Access to MainModule can throw exceptions for some processes; ignore and continue
                    continue;
                }

                if (enumeratedProcess.Id != currentProcess.Id &&
                    !string.IsNullOrEmpty(enumeratedFileName) &&
                    !string.IsNullOrEmpty(currentFileName) &&
                    enumeratedFileName == currentFileName &&
                    enumeratedProcess.MainWindowHandle != IntPtr.Zero)
                    windowHandle = enumeratedProcess.MainWindowHandle;
            }

            return windowHandle;
        }

        private static void CatchAllUnhandledExceptions()
        {
            Application.ThreadException += ApplicationOnThreadException;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
        }

        private static void ApplicationOnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            CloseSplash();
            if (FrmMain.Default.IsDisposed) return;
            FrmUnhandledException window = new(e.Exception, false);
            window.ShowDialog(FrmMain.Default);
        }

        private static void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception exception = e.ExceptionObject as Exception
                                  ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown error");
            FrmUnhandledException window = new(exception, e.IsTerminating);
            window.ShowDialog(FrmMain.Default);
        }

        // sysprep fork: start timings, logged once the main window shows (FrmMain_Shown)
        internal static DateTime MainEnteredUtc { get; private set; }
        internal static DateTime SplashShownUtc { get; private set; }

        internal static string GetStartTimings()
        {
            DateTime processStartUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();
            double Since(DateTime utc) => utc == default ? double.NaN : (utc - processStartUtc).TotalMilliseconds;
            return $"Start timings after process start: Main {Since(MainEnteredUtc):N0} ms, splash shown {Since(SplashShownUtc):N0} ms, main window shown {Since(DateTime.UtcNow):N0} ms.";
        }

        private static void ShowSplashOnStaThread()
        {
            // sysprep fork: worked out here, so the settings are only read on the main thread
            Rectangle splashScreenArea = GetMainWindowScreenArea();
            _wpfSplashThread = new System.Threading.Thread(() =>
            {
                _wpfSplash = FrmSplashScreenNew.GetInstance();

                // sysprep fork: WPF's CenterScreen centres on the screen with the mouse pointer - the splash is
                // placed on the screen the main window opens on instead, once its window exists
                _wpfSplash.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
                _wpfSplash.SourceInitialized += (sender, args) => CenterSplash((System.Windows.Window)sender, splashScreenArea);

                _wpfSplash.ShowInTaskbar = false;
                _wpfSplash.Show();
                SplashShownUtc = DateTime.UtcNow; // sysprep fork
                System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(_wpfSplash);
                System.Windows.Threading.Dispatcher.Run(); // WPF message loop
            })
            { IsBackground = true };
            _wpfSplashThread.SetApartmentState(System.Threading.ApartmentState.STA);
            _wpfSplashThread.Start();
        }

        // sysprep fork: the screen the main window opens on - where it was last (the screen holding most of its
        // saved bounds), else the main screen, as FrmMain does on a first start. Placed in pixels, so the DPI of
        // the screens doesn't matter.
        private static Rectangle GetMainWindowScreenArea()
        {
            try
            {
                Properties.App settings = Properties.App.Default;
                bool maximized = settings.MainFormState == FormWindowState.Maximized;
                Point savedLocation = maximized ? settings.MainFormRestoreLocation : settings.MainFormLocation;
                Size savedSize = maximized ? settings.MainFormRestoreSize : settings.MainFormSize;
                Screen screen = savedLocation.IsEmpty || savedSize.IsEmpty
                    ? Screen.PrimaryScreen
                    : Screen.FromRectangle(new Rectangle(savedLocation, savedSize));
                return (screen ?? Screen.AllScreens[0]).WorkingArea;
            }
            catch (Exception)
            {
                return Rectangle.Empty;
            }
        }

        private static void CenterSplash(System.Windows.Window splash, Rectangle area)
        {
            try
            {
                if (area.IsEmpty) return;

                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(splash).Handle;
                if (!GetWindowRect(handle, out SplashRect bounds)) return;

                int width = bounds.Right - bounds.Left;
                int height = bounds.Bottom - bounds.Top;
                const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
                SetWindowPos(handle, IntPtr.Zero, area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2,
                    0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            }
            catch (Exception)
            {
                // only the position of the splash screen - never let it stop the start
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SplashRect
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out SplashRect rect);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        internal static void CloseSplash()
        {
            // Capture and clear the cached state up front so this is safe to call from
            // multiple startup paths (e.g. the LoadConnections error handler) without
            // acting on stale references or re-running against an already-closed splash.
            FrmSplashScreenNew? splash = _wpfSplash;
            System.Threading.Thread? splashThread = _wpfSplashThread;
            _wpfSplash = null;
            _wpfSplashThread = null;

            if (splash != null)
            {
                try
                {
                    splash.Dispatcher.Invoke(() =>
                    {
                        splash.Close();
                        // The splash runs its own STA message loop; ask it to exit so the
                        // thread can actually be joined below instead of running forever.
                        splash.Dispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Normal);
                    });
                }
                catch (Exception ex)
                {
                    // Never let splash cleanup mask an in-progress startup error.
                    Runtime.MessageCollector.AddExceptionMessage("Failed to close splash screen.", ex, MessageClass.WarningMsg);
                }
            }

            // The splash thread is a background thread, so a bounded join keeps startup
            // from hanging if the dispatcher did not shut down; it dies on process exit anyway.
            splashThread?.Join(TimeSpan.FromSeconds(2));
        }

        // Helper to show a dialog with "Download" and "Cancel" buttons.
        // Returns DialogResult.OK if Download clicked, otherwise DialogResult.Cancel.
        // When hasValidUrl is false, the Download button is disabled.
        private static DialogResult ShowDownloadCancelDialog(string message, string caption, bool hasValidUrl = true)
        {
            using Form dialog = new Form()
            {
                Text = caption,
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new Size(560, 200),
                Icon = SystemIcons.Information
            };

            // Try to find a URL in the message (very simple heuristic: first "http" until whitespace/newline)
            int urlStart = message.IndexOf("http", StringComparison.OrdinalIgnoreCase);
            string? url = null;
            if (urlStart >= 0)
            {
                int urlEnd = message.IndexOfAny(new char[] { ' ', '\r', '\n', '\t' }, urlStart);
                if (urlEnd == -1) urlEnd = message.Length;
                url = message.Substring(urlStart, urlEnd - urlStart);
            }

            LinkLabel lbl = new LinkLabel()
            {
                AutoSize = false,
                Text = message,
                Location = new Point(12, 12),
                Size = new Size(dialog.ClientSize.Width - 24, dialog.ClientSize.Height - 60),
                TextAlign = ContentAlignment.TopLeft,
                LinkBehavior = LinkBehavior.SystemDefault
            };
            lbl.MaximumSize = new Size(dialog.ClientSize.Width - 24, 0);

            if (!string.IsNullOrEmpty(url) && urlStart >= 0)
            {
                // Ensure link indices are within bounds of the LinkLabel text
                int linkStartInLabel = urlStart;
                int linkLength = url.Length;
                if (linkStartInLabel + linkLength <= lbl.Text.Length)
                {
                    lbl.Links.Add(linkStartInLabel, linkLength, url);
                }
            }

            lbl.LinkClicked += (s, e) =>
            {
                string? linkUrl = e.Link?.LinkData as string;
                if (string.IsNullOrEmpty(linkUrl))
                    return;
                if (!hasValidUrl)
                    return;
                if (!InternetConnection.IsPosible())
                {
                    MessageBox.Show("No internet connection is available.", "Network", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Treat clicking the link the same as clicking the "Download" button:
                // set DialogResult to OK so the caller receives DialogResult.OK and can proceed to open the download URL.
                dialog.DialogResult = DialogResult.OK;
                // Do not call Process.Start here to avoid duplicate launches; caller already opens the URL when it sees DialogResult.OK.
            };

            Button btnDownload = new Button()
            {
                Text = "Download",
                DialogResult = DialogResult.OK,
                Size = new Size(100, 28),
                Enabled = hasValidUrl,
            };
            Button btnCancel = new Button()
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Size = new Size(100, 28),
            };

            // Position buttons
            int padding = 12;
            btnCancel.Location = new Point(dialog.ClientSize.Width - padding - btnCancel.Width, dialog.ClientSize.Height - padding - btnCancel.Height);
            btnDownload.Location = new Point(btnCancel.Left - 8 - btnDownload.Width, btnCancel.Top);

            // Set dialog defaults
            dialog.Controls.Add(lbl);
            dialog.Controls.Add(btnDownload);
            dialog.Controls.Add(btnCancel);
            dialog.AcceptButton = btnDownload;
            dialog.CancelButton = btnCancel;

            // Adjust label height to wrap text properly
            lbl.Height = btnCancel.Top - lbl.Top - 8;

            return dialog.ShowDialog();
        }
    }
}