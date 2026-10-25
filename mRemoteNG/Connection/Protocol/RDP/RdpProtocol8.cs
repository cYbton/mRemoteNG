using System;
using System.Drawing;
using System.Windows.Forms;
using AxMSTSCLib;
using mRemoteNG.App;
using mRemoteNG.Messages;
using MSTSCLib;
using mRemoteNG.Resources.Language;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace mRemoteNG.Connection.Protocol.RDP
{
    [SupportedOSPlatform("windows")]
    /* RDP v8 requires Windows 7 with:
		* https://support.microsoft.com/en-us/kb/2592687 
		* OR
		* https://support.microsoft.com/en-us/kb/2923545
		* 
		* Windows 8+ support RDP v8 out of the box.
		*/
    public class RdpProtocol8 : RdpProtocol7
    {
        private MsRdpClient8NotSafeForScripting RdpClient8 => (MsRdpClient8NotSafeForScripting)((AxHost)Control).GetOcx();

        protected override RdpVersion RdpProtocolVersion => RDP.RdpVersion.Rdc8;
        protected FormWindowState LastWindowState = FormWindowState.Minimized;

        // Debounce timer to reduce flickering during resize
        private System.Timers.Timer _resizeDebounceTimer;
        private Size _pendingResizeSize;
        private bool _hasPendingResize = false;

        // sysprep fork: the size last sent to the session - a resize to the same size again is skipped,
        // each one makes the server rebuild the desktop
        private Size _lastSessionSize = Size.Empty;

        public RdpProtocol8()
        {
            // Initialize debounce timer (300ms delay).
            // Keep this in the constructor because it doesn't root the instance in any
            // external static object – it's safe for the temporary probing instances
            // created by RdpProtocolFactory.
            _resizeDebounceTimer = new System.Timers.Timer(300);
            _resizeDebounceTimer.AutoReset = false;
            _resizeDebounceTimer.Elapsed += ResizeDebounceTimer_Elapsed;
        }

        public override bool Initialize()
        {
            if (!base.Initialize())
                return false;

            if (RdpVersion < Versions.RDC81) return false; // minimum dll version checked, loaded MSTSCLIB dll version is not capable

            // Subscribe to static/external events here (not in the constructor) so that
            // temporary probing instances created by RdpProtocolFactory.RdpVersionSupported()
            // are not rooted and do not accumulate memory leaks or spurious callbacks.
            _frmMain.ResizeEnd += ResizeEnd;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            // https://learn.microsoft.com/en-us/windows/win32/termserv/imsrdpextendedsettings-property
            if (connectionInfo.UseRestrictedAdmin)
            {
                SetExtendedProperty("RestrictedLogon", true);
            }
            else if (connectionInfo.UseRCG)
            {
                if (ShouldDisableCredentialsDelegation(connectionInfo.Username))
                {
                    SetExtendedProperty("DisableCredentialsDelegation", true);
                }
                SetExtendedProperty("RedirectedAuthentication", true);
            }
            
            return true;
        }

        public override bool Fullscreen
        {
            get => base.Fullscreen;
            protected set
            {
                base.Fullscreen = value;
                DoResizeClient();
            }
        }

        protected override void Resize(object sender, EventArgs e)
        {
            if (_frmMain == null) return;

            // Skip resize entirely when minimized or minimizing
            if (_frmMain.WindowState == FormWindowState.Minimized) return;

            Runtime.MessageCollector.AddMessage(MessageClass.DebugMsg,
                $"Resize() called - WindowState={_frmMain.WindowState}, LastWindowState={LastWindowState}");

            // Update control size during resize to keep UI synchronized
            // Actual RDP session resize is deferred to ResizeEnd() to prevent flickering
            // sysprep fork: not for FitToWindow with AutomaticResize - re-laying out the control on
            // every mouse move while dragging made it flicker; it is fitted once on ResizeEnd instead
            if (!(InterfaceControl?.Info?.Resolution == RDPResolutions.FitToWindow && InterfaceControl.Info.AutomaticResize))
                DoResizeControl();

            // Only resize RDP session on window state changes (Maximize/Restore)
            // Manual drag-resizing will be handled by ResizeEnd()
            if (LastWindowState != _frmMain.WindowState)
            {
                Runtime.MessageCollector.AddMessage(MessageClass.DebugMsg,
                    $"Resize() - Window state changed from {LastWindowState} to {_frmMain.WindowState}, calling DoResizeClient()");
                LastWindowState = _frmMain.WindowState;
                DoResizeClient();
            }
            else if (InterfaceControl?.Info?.Resolution == RDPResolutions.FitToWindow && InterfaceControl.Info.AutomaticResize)
            {
                // sysprep fork: the panel also changes size without the main window being resized (splitter
                // dragged, a panel shown or hidden, window snapped) - ResizeEnd never comes then
                ScheduleDebouncedResize();
            }
            else
            {
                Runtime.MessageCollector.AddMessage(MessageClass.DebugMsg,
                    $"Resize() - Window state unchanged ({_frmMain.WindowState}), deferring to ResizeEnd()");
            }
        }

        protected override void ResizeEnd(object sender, EventArgs e)
        {
            if (_frmMain == null) return;

            // Skip resize when minimized
            if (_frmMain.WindowState == FormWindowState.Minimized) return;

            Runtime.MessageCollector.AddMessage(MessageClass.DebugMsg,
                $"ResizeEnd() called - WindowState={_frmMain.WindowState}");

            // Update window state tracking
            LastWindowState = _frmMain.WindowState;

            // Update control size immediately (no flicker)
            DoResizeControl();

            // Debounce the RDP session resize to reduce flickering
            ScheduleDebouncedResize();
        }

        private void ScheduleDebouncedResize()
        {
            if (InterfaceControl == null) return;

            // Store the pending size
            _pendingResizeSize = InterfaceControl.Size;
            _hasPendingResize = true;

            // Reset the timer (this delays the resize if called repeatedly)
            _resizeDebounceTimer?.Stop();
            _resizeDebounceTimer?.Start();

            Runtime.MessageCollector?.AddMessage(MessageClass.DebugMsg,
                $"Resize debounced - will resize to {_pendingResizeSize.Width}x{_pendingResizeSize.Height} after 300ms");
        }

        private void ResizeDebounceTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!_hasPendingResize) return;

            // Check if controls are still valid (not disposed during shutdown)
            if (Control == null || Control.IsDisposed || InterfaceControl == null || InterfaceControl.IsDisposed)
            {
                _hasPendingResize = false;
                return;
            }

            // Guard against the window handle not yet being created or already destroyed
            if (!InterfaceControl.IsHandleCreated)
            {
                _hasPendingResize = false;
                return;
            }

            _hasPendingResize = false;

            Runtime.MessageCollector?.AddMessage(MessageClass.DebugMsg,
                $"Debounce timer fired - executing delayed resize to {_pendingResizeSize.Width}x{_pendingResizeSize.Height}");

            // Marshal to the UI thread because DoResizeClient() accesses WinForms and COM objects.
            // Wrap in try/catch: even after the guards above, there is a disposal race between
            // this timer thread and the UI thread that can cause ObjectDisposedException or
            // InvalidOperationException from BeginInvoke.
            try
            {
                if (InterfaceControl.InvokeRequired)
                {
                    InterfaceControl.BeginInvoke(new Action(DoResizeClient));
                }
                else
                {
                    DoResizeClient();
                }
            }
            catch (ObjectDisposedException ex)
            {
                Runtime.MessageCollector?.AddMessage(MessageClass.DebugMsg,
                    $"ResizeDebounceTimer_Elapsed: control disposed during BeginInvoke ({ex.GetType().Name})");
            }
            catch (InvalidOperationException ex)
            {
                Runtime.MessageCollector?.AddMessage(MessageClass.DebugMsg,
                    $"ResizeDebounceTimer_Elapsed: control handle unavailable during BeginInvoke ({ex.GetType().Name})");
            }
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            // When display settings change (e.g., outer RDP session reconnects with a different
            // resolution/viewport), schedule a debounced resize so the inner RDP session is
            // updated to match the new panel dimensions once the display has settled.
            // SystemEvents.DisplaySettingsChanged can fire on a non-UI thread, so marshal
            // ScheduleDebouncedResize() back to the UI thread before touching UI state.
            if (!loginComplete) return;
            if (InterfaceControl == null || InterfaceControl.IsDisposed) return;

            if (InterfaceControl.InvokeRequired)
            {
                InterfaceControl.BeginInvoke(new Action(ScheduleDebouncedResize));
            }
            else
            {
                ScheduleDebouncedResize();
            }
        }

        protected override AxHost CreateActiveXRdpClientControl()
        {
            return new AxMsRdpClient8NotSafeForScripting();
        }

        private void DoResizeClient()
        {
            if (!loginComplete)
            {
                Runtime.MessageCollector.AddMessage(MessageClass.DebugMsg,
                    $"Resize skipped for '{connectionInfo.Hostname}': Login not complete");
                return;
            }

            if (!InterfaceControl.Info.AutomaticResize)
            {
                Runtime.MessageCollector.AddMessage(MessageClass.DebugMsg,
                    $"Resize skipped for '{connectionInfo.Hostname}': AutomaticResize is disabled");
                return;
            }

            // sysprep fork: FitToWindow follows the window again, as it did in 1.76 - now through
            // UpdateSessionDisplaySettings (RDP client 9+), so the session is resized in place instead
            // of reconnecting. The SmartSize resolution only scales the picture client-side and needs
            // no session resize. (FitToWindow with AutomaticResize switches SmartSizing on itself, to
            // scale the picture while the window is being dragged - see RdpProtocol.SetResolution.)
            if (InterfaceControl.Info.Resolution != RDPResolutions.Fullscreen &&
                InterfaceControl.Info.Resolution != RDPResolutions.FitToWindow)
            {
                Runtime.MessageCollector.AddMessage(MessageClass.DebugMsg,
                    $"Resize skipped for '{connectionInfo.Hostname}': Resolution is {InterfaceControl.Info.Resolution} (only Fullscreen and FitToWindow support dynamic resize)");
                return;
            }

            Runtime.MessageCollector.AddMessage(MessageClass.DebugMsg,
                $"Resizing RDP connection to host '{connectionInfo.Hostname}'");

            try
            {
                // Use InterfaceControl.Size instead of Control.Size because Control may be docked
                // and not reflect the actual available space
                // sysprep fork: FitToWindow gets the panel's client area inside its padding (the connection frame)
                bool fitToWindow = InterfaceControl.Info.Resolution == RDPResolutions.FitToWindow;
                Size size = Fullscreen
                    ? Screen.FromControl(Control).Bounds.Size
                    : fitToWindow
                        ? new Size(InterfaceControl.ClientSize.Width - InterfaceControl.Padding.Horizontal,
                                   InterfaceControl.ClientSize.Height - InterfaceControl.Padding.Vertical)
                        : InterfaceControl.Size;

                // sysprep fork: no resize while the tab is hidden or collapsed (e.g. minimized into the auto-hide
                // strip) - the session would shrink to the minimum and rearrange the desktop icons
                if (fitToWindow && (!InterfaceControl.Visible || size.Width < 200 || size.Height < 200))
                {
                    Runtime.MessageCollector.AddMessage(MessageClass.DebugMsg,
                        $"Resize skipped for '{connectionInfo.Hostname}': panel hidden or smaller than 200 px");
                    return;
                }

                // sysprep fork: the session accepts 200 to 8192 pixels per side; outside of that the call
                // fails (a narrow panel next to a wide tree) and would end in a reconnect. An even width
                // avoids the client rounding it and scaling the picture by one pixel.
                size = new Size(Math.Clamp(size.Width, 200, 8192) & ~1, Math.Clamp(size.Height, 200, 8192));

                // sysprep fork: the same size again (window state change and ResizeEnd often both arrive)
                // would only make the server rebuild the desktop once more
                if (size == _lastSessionSize)
                {
                    Runtime.MessageCollector.AddMessage(MessageClass.DebugMsg,
                        $"Resize skipped for '{connectionInfo.Hostname}': session already has {size.Width}x{size.Height}");
                    return;
                }

                Runtime.MessageCollector.AddMessage(MessageClass.DebugMsg,
                    $"Calling UpdateSessionDisplaySettings({size.Width}, {size.Height}) for '{connectionInfo.Hostname}' (Control.Size={Control.Size}, InterfaceControl.Size={InterfaceControl.Size})");

                UpdateSessionDisplaySettings((uint)size.Width, (uint)size.Height);
                _lastSessionSize = size;

                // sysprep fork: the control may refuse to become smaller than the session until the
                // session itself has been resized - fit it to the panel once more afterwards
                if (fitToWindow)
                    FitControlToPanel();

                Runtime.MessageCollector.AddMessage(MessageClass.DebugMsg,
                    $"Successfully resized RDP session for '{connectionInfo.Hostname}' to {size.Width}x{size.Height}");
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionMessage(
                    string.Format(Language.ChangeConnectionResolutionError, connectionInfo.Hostname),
                    ex, MessageClass.WarningMsg, false);
            }
        }

        private bool DoResizeControl()
        {
            if (Control == null || InterfaceControl == null) return false;

            // Check if controls are being disposed during shutdown
            if (Control.IsDisposed || InterfaceControl.IsDisposed) return false;

            // FitToWindow: control is undocked at a fixed size with scrollbars; don't touch it.
            // sysprep fork: unless AutomaticResize is on - then the session follows the window
            // (DoResizeClient), and the control has to follow it too or the resized session stays
            // inside the old, larger control with scrollbars.
            if (InterfaceControl.Info.Resolution == RDPResolutions.FitToWindow && !InterfaceControl.Info.AutomaticResize)
                return false;

            if (InterfaceControl.Info.Resolution == RDPResolutions.FitToWindow)
                return FitControlToPanel();

            Runtime.MessageCollector?.AddMessage(MessageClass.DebugMsg,
                $"DoResizeControl - Before: Control.Size={Control.Size}, InterfaceControl.Size={InterfaceControl.Size}, Control.Dock={Control.Dock}");

            // If control is docked, we need to temporarily undock it, resize it, then redock it
            // because WinForms ignores Size assignments on docked controls
            bool wasDocked = Control.Dock == DockStyle.Fill;

            if (wasDocked)
            {
                Control.Dock = DockStyle.None;
            }

            Control.Location = InterfaceControl.Location;

            if (Control.Size == InterfaceControl.Size || InterfaceControl.Size == Size.Empty)
            {
                // Restore docking if we changed it
                if (wasDocked)
                {
                    Control.Dock = DockStyle.Fill;
                }

                Runtime.MessageCollector?.AddMessage(MessageClass.DebugMsg,
                    $"DoResizeControl - Skipped: Sizes already match or InterfaceControl.Size is empty");
                return false;
            }

            Control.Size = InterfaceControl.Size;

            // Restore docking
            if (wasDocked)
            {
                Control.Dock = DockStyle.Fill;
            }

            Runtime.MessageCollector?.AddMessage(MessageClass.DebugMsg,
                $"DoResizeControl - After: Control.Size={Control.Size}, Control.Dock={Control.Dock}");

            return true;
        }

        // sysprep fork: FitToWindow with AutomaticResize - the control fills the panel's client area
        // (inside its padding, the connection frame), with no scroll area of the size at connect time
        // left over. Positioned in the panel's own coordinates, not at the panel's location in its parent.
        private bool FitControlToPanel()
        {
            if (InterfaceControl.AutoScroll || InterfaceControl.AutoScrollMinSize != Size.Empty)
            {
                InterfaceControl.AutoScrollMinSize = Size.Empty;
                InterfaceControl.AutoScroll = false;
            }

            Padding padding = InterfaceControl.Padding;
            Rectangle target = new(padding.Left, padding.Top,
                Math.Max(1, InterfaceControl.ClientSize.Width - padding.Horizontal),
                Math.Max(1, InterfaceControl.ClientSize.Height - padding.Vertical));
            if (Control.Bounds == target) return false;

            Control.Dock = DockStyle.None;
            Control.Bounds = target;
            return true;
        }

        // sysprep fork: a reconnect after a disconnect starts at the size of the first connect - the size
        // sent last no longer applies, and the session is fitted to the panel again once logged in
        protected override void OnSessionDisconnected()
        {
            base.OnSessionDisconnected();
            _lastSessionSize = Size.Empty;
        }

        protected override void OnSessionLoginComplete()
        {
            base.OnSessionLoginComplete();
            if (_lastSessionSize == Size.Empty && _hadSession &&
                InterfaceControl?.Info?.Resolution == RDPResolutions.FitToWindow && InterfaceControl.Info.AutomaticResize)
                ScheduleDebouncedResize();
            _hadSession = true;
        }

        private bool _hadSession;

        protected virtual void UpdateSessionDisplaySettings(uint width, uint height)
        {
            // sysprep fork: without a resize in place (RDP client 8, or the newer call failed) the session is
            // reconnected with the new size, as 1.76 always did - the resolution follows the window either way
            string host = connectionInfo?.Hostname ?? "unknown";
            Runtime.MessageCollector?.AddMessage(MessageClass.InformationMsg,
                $"Resizing the session for '{host}' to {width}x{height} by reconnecting.", true);
            RdpClient8.Reconnect(width, height);
        }

        public override void Close()
        {
            // Unsubscribe from external/static events to prevent memory leaks
            _frmMain.ResizeEnd -= ResizeEnd;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

            // Clean up debounce timer
            if (_resizeDebounceTimer != null)
            {
                _resizeDebounceTimer.Stop();
                _resizeDebounceTimer.Elapsed -= ResizeDebounceTimer_Elapsed;
                _resizeDebounceTimer.Dispose();
                _resizeDebounceTimer = null;
            }

            base.Close();
        }

    }
}
