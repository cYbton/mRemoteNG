using AxMSTSCLib;
using mRemoteNG.App;
using mRemoteNG.Messages;
using mRemoteNG.Properties;
using mRemoteNG.Resources.Language;
using mRemoteNG.Security.SymmetricEncryption;
using mRemoteNG.Tools;
using mRemoteNG.Tree.Root;
using mRemoteNG.UI;
using mRemoteNG.UI.Forms;
using mRemoteNG.UI.Tabs;
using MSTSCLib;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Timers;
using System.Windows.Forms;

namespace mRemoteNG.Connection.Protocol.RDP
{
    [SupportedOSPlatform("windows")]
    public class RdpProtocol : ProtocolBase, ISupportsViewOnly
    {
        /* RDP v8 requires Windows 7 with:
         * https://support.microsoft.com/en-us/kb/2592687
         * OR
         * https://support.microsoft.com/en-us/kb/2923545
         *
         * Windows 8+ support RDP v8 out of the box.
         */

        private MsRdpClient6NotSafeForScripting _rdpClient; // lowest version supported
        private Panel _connectingCover; // sysprep fork
        protected virtual RdpVersion RdpProtocolVersion => RDP.RdpVersion.Rdc6;
        protected ConnectionInfo connectionInfo;
        protected Version RdpVersion;
        private readonly DisplayProperties _displayProperties;
        protected readonly FrmMain _frmMain = FrmMain.Default;
        protected bool loginComplete;
        private bool _alertOnIdleDisconnect;
        protected uint DesktopScaleFactor => (uint)(_displayProperties.ResolutionScalingFactor.Width * 100);
        protected readonly uint DeviceScaleFactor = 100;
        protected readonly uint Orientation = 0;
        private AxHost AxHost => (AxHost)Control;


        #region Properties

        public virtual bool SmartSize
        {
            get
            {
                try
                {
                    return _rdpClient.AdvancedSettings2.SmartSizing;
                }
                catch (System.Runtime.InteropServices.InvalidComObjectException)
                {
                    // The COM object is separated from its RCW, try reacquiring the RCW or recreating the COM object
                    _rdpClient = new MsRdpClient6NotSafeForScripting();
                    return _rdpClient.AdvancedSettings2.SmartSizing;
                }
            }
            protected set
            {
                try
                {
                    _rdpClient.AdvancedSettings2.SmartSizing = value;
                }
                catch (System.Runtime.InteropServices.InvalidComObjectException)
                {
                    // The COM object is separated from its RCW, try reacquiring the RCW or recreating the COM object
                    _rdpClient = new MsRdpClient6NotSafeForScripting();
                    _rdpClient.AdvancedSettings2.SmartSizing = value;
                }
            }
        }

        public virtual bool Fullscreen
        {
            get => _rdpClient.FullScreen;
            protected set => _rdpClient.FullScreen = value;
        }

        private bool RedirectKeys
        {
            set
            {
                try
                {
                    Debug.Assert(Convert.ToBoolean(_rdpClient.SecuredSettingsEnabled));
                    IMsRdpClientSecuredSettings msRdpClientSecuredSettings = _rdpClient.SecuredSettings2;
                    ConfigureKeyboardRedirection(msRdpClientSecuredSettings, value);
                }
                catch (Exception ex)
                {
                    Runtime.MessageCollector.AddExceptionStackTrace(Language.RdpSetRedirectKeysFailed, ex);
                }
            }
        }

        internal static void ConfigureKeyboardRedirection(IMsRdpClientSecuredSettings securedSettings, bool redirectKeys)
        {
            securedSettings.KeyboardHookMode = redirectKeys ? 1 : 0; // Remote server or local computer, regardless of fullscreen.
        }

        public bool LoadBalanceInfoUseUtf8 { get; set; }

        public bool ViewOnly
        {
            get => !AxHost.Enabled;
            set => AxHost.Enabled = !value;
        }

        #endregion

        #region Constructors

        public RdpProtocol()
        {
            _displayProperties = new DisplayProperties();
            tmrReconnect.Elapsed += tmrReconnect_Elapsed;
        }

        #endregion

        #region Public Methods

        protected virtual AxHost CreateActiveXRdpClientControl()
        {
            return new AxMsRdpClient6NotSafeForScripting();
        }

        public override bool Initialize()
        {
            connectionInfo = InterfaceControl.Info;
            Runtime.MessageCollector.AddMessage(MessageClass.DebugMsg, $"Requesting RDP version: {connectionInfo.RdpVersion}. Using: {RdpProtocolVersion}");
            Control = CreateActiveXRdpClientControl();
            base.Initialize();
            AddConnectingCover();

            try
            {
                if (!InitializeActiveXControl()) return false;

                RdpVersion = new Version(_rdpClient.Version);

                if (RdpVersion < Versions.RDC61) return false; // only RDP versions 6.1 and greater are supported; minimum dll version checked, MSTSCLIB is not capable 

                SetRdpClientProperties();

                return true;
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.RdpSetPropsFailed, ex);
                return false;
            }
        }

        private bool InitializeActiveXControl()
        {
            try
            {
                if (!Properties.OptionsStartupExitPage.Default.DisableRefocus)
                {
                    Control.GotFocus += RdpClient_GotFocus;
                }

                Control.CreateControl();

                while (!Control.Created)
                {
                    Thread.Sleep(50);
                    Application.DoEvents();
                }
                Control.Anchor = AnchorStyles.None;

                _rdpClient = (MsRdpClient6NotSafeForScripting)((AxHost)Control).GetOcx();
                
                return true;
            }
            catch (COMException ex)
            {
                if (ex.Message.Contains("CLASS_E_CLASSNOTAVAILABLE"))
                {
                    Runtime.MessageCollector.AddMessage(MessageClass.ErrorMsg, string.Format(Language.RdpProtocolVersionNotSupported, connectionInfo.RdpVersion));
                }
                else
                {
                    Runtime.MessageCollector.AddExceptionMessage(Language.RdpControlCreationFailed, ex);
                }
                Control.Dispose();
                return false;
            }
        }

        public override bool Connect()
        {
            loginComplete = false;
            _connectStartedLocal = DateTime.Now; // sysprep fork
            SetEventHandlers();

            try
            {
                _rdpClient.Connect();
                base.Connect();

                return true;
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.ConnectionOpenFailed, ex);
            }

            return false;
        }

        public override void Disconnect()
        {
            try
            {
                _rdpClient.Disconnect();
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.RdpDisconnectFailed, ex);
                Close();
            }
        }

        public override void Close()
        {
            try
            {
                if (_rdpClient != null)
                {
                    _rdpClient.OnConnecting -= RDPEvent_OnConnecting;
                    _rdpClient.OnConnected -= RDPEvent_OnConnected;
                    _rdpClient.OnLoginComplete -= RDPEvent_OnLoginComplete;
                    _rdpClient.OnFatalError -= RDPEvent_OnFatalError;
                    _rdpClient.OnDisconnected -= RDPEvent_OnDisconnected;
                    _rdpClient.OnIdleTimeoutNotification -= RDPEvent_OnIdleTimeoutNotification;
                    _rdpClient.OnLeaveFullScreenMode -= RDPEvent_OnLeaveFullscreenMode;
                }

                if (Control != null)
                {
                    Control.GotFocus -= RdpClient_GotFocus;
                }
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace("RdpProtocol: error unsubscribing event handlers", ex);
            }

            base.Close();
        }

        public void ToggleFullscreen()
        {
            try
            {
                Fullscreen = !Fullscreen;
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.RdpToggleFullscreenFailed, ex);
            }
        }

        public void ToggleSmartSize()
        {
            try
            {
                SmartSize = !SmartSize;
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.RdpToggleSmartSizeFailed, ex);
            }
        }

        /// <summary>
        /// Toggles whether the RDP ActiveX control will capture and send input events to the remote host.
        /// The local host will continue to receive data from the remote host regardless of this setting.
        /// </summary>
        public void ToggleViewOnly()
        {
            try
            {
                ViewOnly = !ViewOnly;
            }
            catch
            {
                Runtime.MessageCollector.AddMessage(MessageClass.WarningMsg, $"Could not toggle view only mode for host {connectionInfo.Hostname}");
            }
        }

        public override void Focus()
        {
            try
            {
                if (Control.ContainsFocus == false)
                {
                    Control.Focus();
                }
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.RdpFocusFailed, ex);
            }
        }

        /// <summary>
        /// Determines if this version of the RDP client
        /// is supported on this machine.
        /// </summary>
        /// <returns></returns>
        public bool RdpVersionSupported()
        {
            try
            {
                using AxHost control = CreateActiveXRdpClientControl();
                control.CreateControl();
                return true;
            }
            catch
            {
                return false;
            }
        }
        #endregion

        #region Private Methods

        protected static class Versions
        {
            // https://en.wikipedia.org/wiki/Remote_Desktop_Protocol
            public static readonly Version RDC60 = new(6, 0, 6000);
            public static readonly Version RDC61 = new(6, 0, 6001);
            public static readonly Version RDC70 = new(6, 1, 7600);
            public static readonly Version RDC80 = new(6, 2, 9200);
            public static readonly Version RDC81 = new(6, 3, 9600);
            public static readonly Version RDC100 = new(10, 0, 0);
        }

        private void SetRdpClientProperties()
        {
            // https://learn.microsoft.com/en-us/windows-server/remote/remote-desktop-services/clients/rdp-files

            _rdpClient.Server = connectionInfo.Hostname;

            SetCredentials();
            SetResolution();
            _rdpClient.FullScreenTitle = connectionInfo.Name;

            _alertOnIdleDisconnect = connectionInfo.RDPAlertIdleTimeout;
            _rdpClient.AdvancedSettings2.MinutesToIdleTimeout = connectionInfo.RDPMinutesToIdleTimeout;

            #region Remote Desktop Services
            _rdpClient.SecuredSettings2.StartProgram = connectionInfo.RDPStartProgram;
            _rdpClient.SecuredSettings2.WorkDir = connectionInfo.RDPStartProgramWorkDir;
            #endregion

            //not user changeable
            _rdpClient.AdvancedSettings2.GrabFocusOnConnect = true;
            _rdpClient.AdvancedSettings3.EnableAutoReconnect = true;
            _rdpClient.AdvancedSettings3.MaxReconnectAttempts = Settings.Default.RdpReconnectionCount;
            _rdpClient.AdvancedSettings2.keepAliveInterval = 60000; //in milliseconds (10,000 = 10 seconds)
            _rdpClient.AdvancedSettings5.AuthenticationLevel = 0;
            _rdpClient.AdvancedSettings2.EncryptionEnabled = 1;

            _rdpClient.AdvancedSettings2.overallConnectionTimeout = Settings.Default.ConRDPOverallConnectionTimeout;

            _rdpClient.AdvancedSettings2.BitmapPeristence = Convert.ToInt32(connectionInfo.CacheBitmaps);

            if (RdpVersion >= Versions.RDC61)
            {
                _rdpClient.AdvancedSettings7.EnableCredSspSupport = connectionInfo.UseCredSsp;
            }
            
            SetUseConsoleSession();
            SetPort();
            RedirectKeys = connectionInfo.RedirectKeys;
            SetRedirection();
            SetAuthenticationLevel();
            SetLoadBalanceInfo();
            SetRdGateway();
            ViewOnly = Force.HasFlag(ConnectionInfo.Force.ViewOnly);

            _rdpClient.ColorDepth = (int)connectionInfo.Colors;

            SetPerformanceFlags();

            _rdpClient.ConnectingText = Language.Connecting;
        }

        protected object GetExtendedProperty(string property)
        {
            try
            {
                // ReSharper disable once UseIndexedProperty
                return ((IMsRdpExtendedSettings)_rdpClient).get_Property(property);
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionMessage($"Error getting extended RDP property '{property}'", ex, MessageClass.WarningMsg, false);
                return null;
            }
        }

        protected void SetExtendedProperty(string property, object value)
        {
            try
            {
                // ReSharper disable once UseIndexedProperty
                ((IMsRdpExtendedSettings)_rdpClient).set_Property(property, ref value);
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionMessage($"Error setting extended RDP property '{property}'", ex, MessageClass.WarningMsg, false);
            }
        }

        internal static bool ShouldDisableCredentialsDelegation(string username)
        {
            return string.IsNullOrWhiteSpace(username);
        }

        private void SetRdGateway()
        {
            try
            {
                if (_rdpClient.TransportSettings.GatewayIsSupported == 0)
                {
                    Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, Language.RdpGatewayNotSupported, true);
                    return;
                }

                Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, Language.RdpGatewayIsSupported, true);

                if (connectionInfo.RDGatewayUsageMethod == RDGatewayUsageMethod.Never) return;

                // USE GATEWAY
                _rdpClient.TransportSettings.GatewayUsageMethod = (uint)connectionInfo.RDGatewayUsageMethod;
                _rdpClient.TransportSettings.GatewayHostname = connectionInfo.RDGatewayHostname;
                _rdpClient.TransportSettings.GatewayProfileUsageMethod = 1; // TSC_PROXY_PROFILE_MODE_EXPLICIT
                if (connectionInfo.RDGatewayUseConnectionCredentials == RDGatewayUseConnectionCredentials.SmartCard)
                {
                    _rdpClient.TransportSettings.GatewayCredsSource = 1; // TSC_PROXY_CREDS_MODE_SMARTCARD
                }

                if (RdpVersion < Versions.RDC61 || Force.HasFlag(ConnectionInfo.Force.NoCredentials)) return;

                switch (connectionInfo.RDGatewayUseConnectionCredentials)
                {
                    case RDGatewayUseConnectionCredentials.Yes:
                        _rdpClient.TransportSettings2.GatewayUsername = connectionInfo.Username;
                        //_rdpClient.TransportSettings2.GatewayPassword = connectionInfo.Password.ConvertToUnsecureString();
                        _rdpClient.TransportSettings2.GatewayPassword = connectionInfo.Password;
                        _rdpClient.TransportSettings2.GatewayDomain = connectionInfo?.Domain;
                        break;
                    case RDGatewayUseConnectionCredentials.SmartCard:
                        _rdpClient.TransportSettings2.GatewayCredSharing = 0;
                        break;
                    default:
                    {
                        _rdpClient.TransportSettings2.GatewayCredSharing = 0;

                            string gwu = connectionInfo.RDGatewayUsername;
                            string gwp = connectionInfo.RDGatewayPassword;
                            string gwd = connectionInfo.RDGatewayDomain;
                            string pkey = "";

                        // access secret server api if necessary
                        if (InterfaceControl.Info.RDGatewayExternalCredentialProvider != ExternalCredentialProvider.None ||
                            InterfaceControl.Info.ExternalCredentialProvider != ExternalCredentialProvider.None)
                            FetchGatewayCredentialsFromProvider(ref gwu, ref gwp, ref gwd, ref pkey);


                            if (connectionInfo.RDGatewayUseConnectionCredentials != RDGatewayUseConnectionCredentials.AccessToken)
                        {
                            _rdpClient.TransportSettings2.GatewayUsername = gwu;
                            _rdpClient.TransportSettings2.GatewayPassword = gwp;
                            _rdpClient.TransportSettings2.GatewayDomain = gwd;
                        }
                        else
                        {
                            //TODO: should we check client version and throw if it is less than 7
                        }
                        
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.RdpSetGatewayFailed, ex);
            }
        }

        private void SetUseConsoleSession()
        {
            try
            {
                bool value;

                if (Force.HasFlag(ConnectionInfo.Force.UseConsoleSession))
                {
                    value = true;
                }
                else if (Force.HasFlag(ConnectionInfo.Force.DontUseConsoleSession))
                {
                    value = false;
                }
                else
                {
                    value = connectionInfo.UseConsoleSession;
                }

                if (RdpVersion >= Versions.RDC61)
                {
                    Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, string.Format(Language.RdpSetConsoleSwitch, RdpVersion), true);
                    _rdpClient.AdvancedSettings7.ConnectToAdministerServer = value;
                }
                else
                {
                    Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, $"{string.Format(Language.RdpSetConsoleSwitch, RdpVersion)}{Environment.NewLine}No longer supported in this RDP version. Reference: https://msdn.microsoft.com/en-us/library/aa380863(v=vs.85).aspx", true);
                    // ConnectToServerConsole is deprecated
                    //https://msdn.microsoft.com/en-us/library/aa380863(v=vs.85).aspx
                    //_rdpClient.AdvancedSettings2.ConnectToServerConsole = value;
                }
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.RdpSetConsoleSessionFailed, ex);
            }
        }

        // sysprep fork: the external credential providers (Delinea Secret Server, Passwordstate, 1Password,
        // Vault/OpenBao) are not part of this build - ExternalConnectors.dll, unsigned, was blocked by Smart App
        // Control and held up every RDP connect, and the training labs don't use them. A connection set to one
        // gets a message instead and connects with its own credentials.
        private void FetchCredentialsFromProvider(string userViaApi, ref string userName, ref string password, ref string domain, ref string pkey)
        {
            ReportExternalProviderUnavailable();
        }

        private bool FetchDefaultCredentialsFromSecretServer(ref string userName, ref string password, ref string domain, ref string pkey)
        {
            ReportExternalProviderUnavailable();
            return false;
        }

        private void FetchGatewayCredentialsFromProvider(ref string gwu, ref string gwp, ref string gwd, ref string pkey)
        {
            ReportExternalProviderUnavailable();
        }

        private void ReportExternalProviderUnavailable()
        {
            Event_ErrorOccured(this, "External credential providers are not part of the sysprep.io build of mRemoteNG - the connection's own credentials are used.", 0);
        }

        private void SetCredentials()
        {
            try
            {
                if (Force.HasFlag(ConnectionInfo.Force.NoCredentials))
                {
                    return;
                }

                string userName = connectionInfo?.Username ?? "";
                string domain = connectionInfo?.Domain ?? "";
                string userViaApi = connectionInfo?.UserViaAPI ?? "";
                string pkey = "";
                //string password = (connectionInfo?.Password?.ConvertToUnsecureString() ?? "");
                string password = (connectionInfo?.Password ?? "");

                // access secret server api if necessary
                if (InterfaceControl.Info.ExternalCredentialProvider != ExternalCredentialProvider.None)
                    FetchCredentialsFromProvider(userViaApi, ref userName, ref password, ref domain, ref pkey);

                if (string.IsNullOrEmpty(userName))
                {
                    switch (Properties.OptionsCredentialsPage.Default.EmptyCredentials)
                    {
                        case "windows":
                            _rdpClient.UserName = Environment.UserName;
                            break;
                        case "custom" when !string.IsNullOrEmpty(Properties.OptionsCredentialsPage.Default.DefaultUsername):
                            _rdpClient.UserName = Properties.OptionsCredentialsPage.Default.DefaultUsername;
                            break;
                        case "custom":
                            if (FetchDefaultCredentialsFromSecretServer(ref userName, ref password, ref domain, ref pkey))
                                _rdpClient.UserName = userName;

                            break;
                    }
                }
                else
                {
                    _rdpClient.UserName = userName;
                }

                // Restricted Admin and Remote Credential Guard do not forward explicit passwords
                // on protocol versions that support those modes. Skipping password assignment
                // avoids potential NTLM fallback attempts that would fail for AD Protected Users.
                if (ShouldAssignClearTextPassword(connectionInfo))
                {
                    if (string.IsNullOrEmpty(password))
                    {
                        if (Properties.OptionsCredentialsPage.Default.EmptyCredentials == "custom")
                        {
                            if (Properties.OptionsCredentialsPage.Default.DefaultPassword != "")
                            {
                                LegacyRijndaelCryptographyProvider cryptographyProvider = new();
                                _rdpClient.AdvancedSettings2.ClearTextPassword = cryptographyProvider.Decrypt(Properties.OptionsCredentialsPage.Default.DefaultPassword, Runtime.EncryptionKey);
                            }
                        }
                    }
                    else
                    {
                        _rdpClient.AdvancedSettings2.ClearTextPassword = password;
                    }
                }

                if (string.IsNullOrEmpty(domain))
                {
                    _rdpClient.Domain = Properties.OptionsCredentialsPage.Default.EmptyCredentials switch
                    {
                        "windows" => Environment.UserDomainName,
                        "custom" => Properties.OptionsCredentialsPage.Default.DefaultDomain,
                        _ => _rdpClient.Domain
                    };
                }
                else
                {
                    _rdpClient.Domain = domain;
                }
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.RdpSetCredentialsFailed, ex);
            }
        }

        protected bool ShouldAssignClearTextPassword(ConnectionInfo connectionInfo)
        {
            return RdpProtocolVersion is RDP.RdpVersion.Rdc6 or RDP.RdpVersion.Rdc7
                || (!connectionInfo.UseRestrictedAdmin && !connectionInfo.UseRCG);
        }

        private void SetResolution()
        {
            try
            {
                SetExtendedProperty("DesktopScaleFactor", DesktopScaleFactor);
                SetExtendedProperty("DeviceScaleFactor", DeviceScaleFactor);

                if (Force.HasFlag(ConnectionInfo.Force.Fullscreen))
                {
                    _rdpClient.FullScreen = true;
                    _rdpClient.DesktopWidth = Screen.FromControl(_frmMain).Bounds.Width;
                    _rdpClient.DesktopHeight = Screen.FromControl(_frmMain).Bounds.Height;

                    return;
                }

                switch (InterfaceControl.Info.Resolution)
                {
                    case RDPResolutions.FitToWindow:
                        // Lock the RDP session to the current content area size.
                        // The control is undocked so it keeps this fixed size;
                        // AutoScroll on the parent panel provides scrollbars
                        // when the panel shrinks below the session resolution.
                        // Use DisplayRectangle to respect Padding (connection frame border).
                        var fitRect = InterfaceControl.DisplayRectangle;
                        _rdpClient.DesktopWidth = fitRect.Width;
                        _rdpClient.DesktopHeight = fitRect.Height;
                        Control.Dock = DockStyle.None;
                        Control.Location = fitRect.Location;
                        Control.Size = fitRect.Size;
                        if (InterfaceControl.Info.AutomaticResize)
                        {
                            // sysprep fork: with AutomaticResize the control follows the panel (anchored) and
                            // SmartSizing scales the current picture while the window is being resized - no
                            // black area, no scrollbars. On ResizeEnd the session gets the new size (RdpProtocol8),
                            // after which the picture is 1:1 again and SmartSizing has nothing left to scale.
                            _rdpClient.AdvancedSettings2.SmartSizing = true;
                            Control.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                            InterfaceControl.AutoScroll = false;
                            break;
                        }
                        InterfaceControl.AutoScroll = true;
                        InterfaceControl.AutoScrollMinSize = fitRect.Size;
                        break;
                    case RDPResolutions.SmartSize:
                        // Connect at the full screen resolution so the remote
                        // desktop is rendered at high quality, then SmartSizing
                        // scales the image to fit whatever the panel size is.
                        // Use Anchor instead of Dock.Fill because the AxHost
                        // ActiveX wrapper doesn't forward Dock-triggered resizes
                        // to the COM control's internal rendering surface.
                        // Use DisplayRectangle to respect Padding (connection frame border).
                        var screen = Screen.FromControl(_frmMain);
                        _rdpClient.DesktopWidth = screen.Bounds.Width;
                        _rdpClient.DesktopHeight = screen.Bounds.Height;
                        _rdpClient.AdvancedSettings2.SmartSizing = true;
                        var smartRect = InterfaceControl.DisplayRectangle;
                        Control.Dock = DockStyle.None;
                        Control.Location = smartRect.Location;
                        Control.Size = smartRect.Size;
                        Control.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                        break;
                    case RDPResolutions.Fullscreen:
                        _rdpClient.FullScreen = true;
                        _rdpClient.DesktopWidth = Screen.FromControl(_frmMain).Bounds.Width;
                        _rdpClient.DesktopHeight = Screen.FromControl(_frmMain).Bounds.Height;
                        break;
                }
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.RdpSetResolutionFailed, ex);
            }
        }

        private void SetPort()
        {
            try
            {
                if (connectionInfo.Port != (int)Defaults.Port)
                {
                    _rdpClient.AdvancedSettings2.RDPPort = connectionInfo.Port;
                }
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.RdpSetPortFailed, ex);
            }
        }

        private void SetRedirection()
        {
            try
            {
                SetDriveRedirection();
                _rdpClient.AdvancedSettings2.RedirectPorts = connectionInfo.RedirectPorts;
                _rdpClient.AdvancedSettings2.RedirectPrinters = connectionInfo.RedirectPrinters;
                _rdpClient.AdvancedSettings2.RedirectSmartCards = connectionInfo.RedirectSmartCards;
                _rdpClient.SecuredSettings2.AudioRedirectionMode = (int)connectionInfo.RedirectSound;
                _rdpClient.AdvancedSettings6.RedirectClipboard = connectionInfo.RedirectClipboard;
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.RdpSetRedirectionFailed, ex);
            }
        }

        private void SetDriveRedirection()
        {
            if (RDPDiskDrives.None == connectionInfo.RedirectDiskDrives)
                _rdpClient.AdvancedSettings2.RedirectDrives = false;
            else if (RDPDiskDrives.All == connectionInfo.RedirectDiskDrives)
                _rdpClient.AdvancedSettings2.RedirectDrives = true;
            else if (RDPDiskDrives.Custom == connectionInfo.RedirectDiskDrives)
            {
                IMsRdpClientNonScriptable5 rdpNS5 = (IMsRdpClientNonScriptable5)((AxHost)Control).GetOcx();
                for (uint i = 0; i < rdpNS5.DriveCollection.DriveCount; i++)
                {
                    IMsRdpDrive drive = rdpNS5.DriveCollection.DriveByIndex[i];
                    drive.RedirectionState = connectionInfo.RedirectDiskDrivesCustom.Contains(drive.Name.Substring(0, 1));
                }
            }
            else
            {
                // Local Drives
                IMsRdpClientNonScriptable5 rdpNS5 = (IMsRdpClientNonScriptable5)((AxHost)Control).GetOcx();
                for (uint i = 0; i < rdpNS5.DriveCollection.DriveCount; i++)
                {
                    IMsRdpDrive drive = rdpNS5.DriveCollection.DriveByIndex[i];
                    drive.RedirectionState = IsLocal(drive);
                }
            }
        }

        private bool IsLocal(IMsRdpDrive drive)
        {
            DriveInfo[] myDrives = DriveInfo.GetDrives();
            foreach (DriveInfo myDrive in myDrives)
            {
                if (myDrive.Name.Substring(0, 1).Equals(drive.Name.Substring(0,1)))
                {
                    return myDrive.DriveType == DriveType.Fixed;
                }
            }
            return false;
        }

        private void SetPerformanceFlags()
        {
            try
            {
                int pFlags = 0;
                if (connectionInfo.DisplayThemes == false)
                    pFlags += (int)RDPPerformanceFlags.DisableThemes;

                if (connectionInfo.DisplayWallpaper == false)
                    pFlags += (int)RDPPerformanceFlags.DisableWallpaper;

                if (connectionInfo.EnableFontSmoothing)
                    pFlags += (int)RDPPerformanceFlags.EnableFontSmoothing;

                if (connectionInfo.EnableDesktopComposition)
                    pFlags += (int)RDPPerformanceFlags.EnableDesktopComposition;

                if (connectionInfo.DisableFullWindowDrag)
                    pFlags += (int)RDPPerformanceFlags.DisableFullWindowDrag;

                if (connectionInfo.DisableMenuAnimations)
                    pFlags += (int)RDPPerformanceFlags.DisableMenuAnimations;

                if (connectionInfo.DisableCursorShadow)
                    pFlags += (int)RDPPerformanceFlags.DisableCursorShadow;

                if (connectionInfo.DisableCursorBlinking)
                    pFlags += (int)RDPPerformanceFlags.DisableCursorBlinking;

                _rdpClient.AdvancedSettings2.PerformanceFlags = pFlags;
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.RdpSetPerformanceFlagsFailed, ex);
            }
        }

        private void SetAuthenticationLevel()
        {
            try
            {
                _rdpClient.AdvancedSettings5.AuthenticationLevel = (uint)connectionInfo.RDPAuthenticationLevel;
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.RdpSetAuthenticationLevelFailed, ex);
            }
        }

        private void SetLoadBalanceInfo()
        {
            if (string.IsNullOrEmpty(connectionInfo.LoadBalanceInfo))
            {
                return;
            }

            try
            {
                _rdpClient.AdvancedSettings2.LoadBalanceInfo = LoadBalanceInfoUseUtf8
                    ? new AzureLoadBalanceInfoEncoder().Encode(connectionInfo.LoadBalanceInfo)
                    : connectionInfo.LoadBalanceInfo;
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace("Unable to set load balance info.", ex);
            }
        }

        protected virtual void SetEventHandlers()
        {
            try
            {
                _rdpClient.OnConnecting += RDPEvent_OnConnecting;
                _rdpClient.OnConnected += RDPEvent_OnConnected;
                _rdpClient.OnLoginComplete += RDPEvent_OnLoginComplete;
                _rdpClient.OnFatalError += RDPEvent_OnFatalError;
                _rdpClient.OnDisconnected += RDPEvent_OnDisconnected;
                _rdpClient.OnIdleTimeoutNotification += RDPEvent_OnIdleTimeoutNotification;
                _rdpClient.OnLeaveFullScreenMode += RDPEvent_OnLeaveFullscreenMode;
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace(Language.RdpSetEventHandlersFailed, ex);
            }
        }
        
        #endregion

        #region Private Events & Handlers

        private void RDPEvent_OnIdleTimeoutNotification()
        {
            Close(); //Simply close the RDP Session if the idle timeout has been triggered.

            if (!_alertOnIdleDisconnect) return;
            MessageBox.Show($@"The {connectionInfo.Name} session was disconnected due to inactivity", @"Session Disconnected", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RDPEvent_OnFatalError(int errorCode)
        {
            string errorMsg = RdpErrorCodes.GetError(errorCode);
            Event_ErrorOccured(this, errorMsg, errorCode);
        }

        private void RDPEvent_OnDisconnected(int discReason)
        {
            // sysprep fork: a reconnect attempt that failed - the reconnect box (and the cover it sits on) stays
            bool reconnectAttemptFailed = ReconnectGroup is { IsDisposed: false };
            if (!reconnectAttemptFailed)
                RemoveConnectingCover();
            bool hadSession = loginComplete; // sysprep fork: before OnSessionDisconnected resets it
            OnSessionDisconnected();
            const int UI_ERR_NORMAL_DISCONNECT = 0xB08;
            if (discReason != UI_ERR_NORMAL_DISCONNECT)
            {
                string reason = _rdpClient.GetErrorDescription((uint)discReason, (uint)_rdpClient.ExtendedDisconnectReason);
                Event_Disconnected(this, reason, discReason);
            }

            // sysprep fork: no reconnect when the session was taken over by another connection (the same user
            // signing in from somewhere else) - two clients with automatic reconnect took it from each other
            // every few seconds. The tab closes as it does without the reconnect option.
            ExtendedDisconnectReasonCode extendedReason = _rdpClient.ExtendedDisconnectReason;
            bool replacedByOtherConnection = extendedReason == ExtendedDisconnectReasonCode.exDiscReasonReplacedByOtherConnection;

            if (reconnectAttemptFailed && Properties.OptionsAdvancedPage.Default.ReconnectOnDisconnect)
            {
                // sysprep fork: the same box polls again - a new one with every attempt flashed up and always
                // started grey again. After MaxReconnectAttempts it stops (red) and waits for Close.
                ReconnectGroup.ServerReady = false;
                if (++_reconnectAttempts < MaxReconnectAttempts)
                    tmrReconnect.Enabled = true;
                return;
            }

            // sysprep fork: a new reconnect box only when a session was running and ended without the user
            // asking for it. Not when the first connect failed (wrong password, declined certificate - retrying
            // would repeat the prompt or lock the account), not after "Disconnect" here (1) or in the session
            // (2), not when another connection took the session over.
            const int disconnectReasonLocal = 1;
            const int disconnectReasonRemoteByUser = 2;
            bool reconnect = Properties.OptionsAdvancedPage.Default.ReconnectOnDisconnect &&
                             hadSession && !replacedByOtherConnection &&
                             discReason != disconnectReasonLocal && discReason != disconnectReasonRemoteByUser;

            if (reconnect)
            {
                // A session that ended (sign-out, or the server shutting down for a restart) is only reconnected
                // once the server was seen down - after a plain sign-out it stays up, and the tab closes then.
                _waitForServerDown = extendedReason is ExtendedDisconnectReasonCode.exDiscReasonAPIInitiatedLogoff
                                                    or ExtendedDisconnectReasonCode.exDiscReasonLogoffByUser;
                _reconnectStartedUtc = DateTime.UtcNow;
                _reconnectAttempts = 0;

                ReconnectGroup = new ReconnectGroup();
                ReconnectGroup.CloseClicked += Event_ReconnectGroupCloseClicked;
                // sysprep fork: the disconnected RDP control is a white area - the box sits on the theme
                // background instead (the connecting cover, removed again once the session is back)
                AddConnectingCover();
                System.Windows.Forms.Control parent = _connectingCover != null ? _connectingCover : Control;
                ReconnectGroup.Left = (int)((double)parent.Width / 2 - (double)ReconnectGroup.Width / 2);
                ReconnectGroup.Top = (int)((double)parent.Height / 2 - (double)ReconnectGroup.Height / 2);
                ReconnectGroup.Anchor = AnchorStyles.None; // stays centred when the tab changes size
                ReconnectGroup.Parent = parent;
                ReconnectGroup.Show();
                tmrReconnect.Enabled = true;
            }
            else
            {
                Close();
            }
        }

        private void RDPEvent_OnConnecting()
        {
            Event_Connecting(this);
        }

        private void RDPEvent_OnConnected()
        {
            // sysprep fork: the session is back - only now the reconnect box goes (with the cover it sits on)
            tmrReconnect.Enabled = false;
            if (ReconnectGroup != null)
            {
                ReconnectGroup.Dispose();
                ReconnectGroup = null;
            }
            RemoveConnectingCover();
            Event_Connected(this);
        }

        // sysprep fork: until the session is connected the RDP control shows a white area, which flashes
        // in a dark theme. A panel in the theme background lies over it until then.
        private void AddConnectingCover()
        {
            if (InterfaceControl == null || Control == null) return;

            _connectingCover = new Panel { Dock = DockStyle.Fill, BackColor = InterfaceControl.BackColor };
            InterfaceControl.Controls.Add(_connectingCover);
            _connectingCover.BringToFront();
        }

        private void RemoveConnectingCover()
        {
            if (_connectingCover == null) return;

            _connectingCover.Parent?.Controls.Remove(_connectingCover);
            _connectingCover.Dispose();
            _connectingCover = null;
            RedrawRdpControl();
        }

        // sysprep fork: the RDP control does not always repaint by itself once the cover is gone - with a
        // still desktop on the server nothing new arrives, so the tab stayed empty until a click. Its window
        // and all child windows are told to repaint right away.
        private void RedrawRdpControl()
        {
            if (Control is not { IsHandleCreated: true, IsDisposed: false }) return;

            const uint RDW_INVALIDATE = 0x0001, RDW_ERASE = 0x0004, RDW_ALLCHILDREN = 0x0080, RDW_UPDATENOW = 0x0100, RDW_FRAME = 0x0400;
            RedrawWindow(Control.Handle, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_ERASE | RDW_ALLCHILDREN | RDW_UPDATENOW | RDW_FRAME);
        }

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        private void RDPEvent_OnLoginComplete()
        {
            loginComplete = true;
            // sysprep fork: logged with the time since the connect, to tell a slow logon from a tab that did not
            // show the session; and the control repaints once more with the desktop now there
            Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg,
                $"RDP logon to \"{connectionInfo?.Hostname}\" complete after {(DateTime.Now - _connectStartedLocal).TotalMilliseconds:N0} ms.", true);
            RedrawRdpControl();
            OnSessionLoginComplete();
        }

        private DateTime _connectStartedLocal = DateTime.Now; // sysprep fork

        // sysprep fork: hooks for the RDP 8+ resize handling (RdpProtocol8)
        protected virtual void OnSessionLoginComplete()
        {
        }

        protected virtual void OnSessionDisconnected()
        {
            // no resize of a disconnected session (e.g. while the reconnect box is showing)
            loginComplete = false;
        }

        private void RDPEvent_OnLeaveFullscreenMode()
        {
            Fullscreen = false;
            _leaveFullscreenEvent?.Invoke(this, EventArgs.Empty);
        }

        private void RdpClient_GotFocus(object sender, EventArgs e)
        {
            ((ConnectionTab)Control.Parent.Parent).Focus();
        }
        #endregion

        #region Public Events & Handlers

        public delegate void LeaveFullscreenEventHandler(object sender, EventArgs e);

        private LeaveFullscreenEventHandler _leaveFullscreenEvent;

        public event LeaveFullscreenEventHandler LeaveFullscreen
        {
            add => _leaveFullscreenEvent = (LeaveFullscreenEventHandler)Delegate.Combine(_leaveFullscreenEvent, value);
            remove => _leaveFullscreenEvent = (LeaveFullscreenEventHandler)Delegate.Remove(_leaveFullscreenEvent, value);
        }

        #endregion

        #region Enums

        public enum Defaults
        {
            Colors = RDPColors.Colors16Bit,
            Sounds = RDPSounds.DoNotPlay,
            Resolution = RDPResolutions.SmartSize,
            Port = 3389
        }

        #endregion
        
        #region Reconnect Stuff

        // sysprep fork: state of the automatic reconnect
        private const int MaxReconnectAttempts = 30;
        private static readonly TimeSpan SignOutGracePeriod = TimeSpan.FromSeconds(60);
        private int _reconnectCheckRunning;
        private int _reconnectAttempts;
        private bool _waitForServerDown;
        private DateTime _reconnectStartedUtc;

        private void tmrReconnect_Elapsed(object sender, ElapsedEventArgs e)
        {
            // sysprep fork: one check at a time - the timer fires every 5 seconds regardless of a check still running
            if (Interlocked.Exchange(ref _reconnectCheckRunning, 1) == 1) return;

            try
            {
                bool srvReady = IsRdpPortOpen(connectionInfo.Hostname, connectionInfo.Port);

                ReconnectGroup reconnectGroup = ReconnectGroup;
                if (reconnectGroup == null || reconnectGroup.IsDisposed) return;

                // sysprep fork: after the session ended, wait until the server is seen down (a restart). Still
                // up after a minute means a plain sign-out - the tab closes as without automatic reconnect.
                if (_waitForServerDown)
                {
                    if (srvReady)
                    {
                        if (DateTime.UtcNow - _reconnectStartedUtc > SignOutGracePeriod)
                        {
                            tmrReconnect.Enabled = false;
                            InterfaceControl?.BeginInvoke(new Action(Close));
                        }
                        return;
                    }
                    _waitForServerDown = false;
                }

                reconnectGroup.ServerReady = srvReady;

                if (!reconnectGroup.ReconnectWhenReady || !srvReady) return;
                tmrReconnect.Enabled = false;

                // sysprep fork: the box stays (green) while connecting and goes once the session is back
                // (RDPEvent_OnConnected); a failed attempt starts the polling again (RDPEvent_OnDisconnected).
                // Connected on the UI thread the ActiveX control belongs to.
                InterfaceControl?.BeginInvoke(new Action(ReconnectNow));
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionMessage(
                    string.Format(Language.AutomaticReconnectError, connectionInfo.Hostname),
                    ex, MessageClass.WarningMsg, false);
            }
            finally
            {
                Interlocked.Exchange(ref _reconnectCheckRunning, 0);
            }
        }

        // sysprep fork: the tab may have been closed meanwhile; a failure starts the polling again instead of
        // reaching the unhandled-exception dialog with the box left green
        private void ReconnectNow()
        {
            if (Control == null || Control.IsDisposed || ReconnectGroup is not { IsDisposed: false }) return;

            try
            {
                _connectStartedLocal = DateTime.Now;
                _rdpClient.Connect();
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionMessage(
                    string.Format(Language.AutomaticReconnectError, connectionInfo.Hostname),
                    ex, MessageClass.WarningMsg, false);
                ReconnectGroup.ServerReady = false;
                if (++_reconnectAttempts < MaxReconnectAttempts)
                    tmrReconnect.Enabled = true;
            }
        }

        // sysprep fork: port check with a short timeout - a host that is still off took about 20 seconds per
        // check (TCP connect timeout), so the status stayed grey for that long
        private static bool IsRdpPortOpen(string hostname, int port)
        {
            try
            {
                using System.Net.Sockets.TcpClient client = new();
                return client.ConnectAsync(hostname, port).Wait(TimeSpan.FromSeconds(2)) && client.Connected;
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

    }
}