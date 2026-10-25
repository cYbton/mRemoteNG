using System;
using System.Drawing;
using mRemoteNG.Resources.Language;

namespace mRemoteNG.Tools
{
    public partial class ReconnectGroup
    {
        public ReconnectGroup()
        {
            InitializeComponent();
            // NoReconnect is the option "Automatically try to reconnect when disconnected from server"
            chkReconnectWhenReady.Checked = Properties.OptionsAdvancedPage.Default.NoReconnect;
            ApplyTheme();
            UI.UiFontScaler.ApplyWithLayout(this); // sysprep fork: program font size, as in the other dialogs
        }

        // sysprep fork: theme colours instead of the fixed white background, on which the themed (light) labels
        // could not be read in a dark theme
        private void ApplyTheme()
        {
            Themes.ThemeManager themeManager = Themes.ThemeManager.getInstance();
            if (!themeManager.ActiveAndExtended) return;

            Color background = themeManager.ActiveTheme.ExtendedPalette.getColor("Dialog_Background");
            Color foreground = themeManager.ActiveTheme.ExtendedPalette.getColor("Dialog_Foreground");
            BackColor = background;
            ForeColor = foreground;
            grpAutomaticReconnect.BackColor = background;
            grpAutomaticReconnect.ForeColor = foreground;
            lblAnimation.ForeColor = foreground;
        }

        private bool _ServerReady;

        public bool ServerReady
        {
            get => _ServerReady;
            set
            {
                SetStatusImage(value ? Properties.Resources.HostStatus_On : Properties.Resources.HostStatus_Off);

                _ServerReady = value;
            }
        }

        private delegate void SetStatusImageCB(Image Img);

        private void SetStatusImage(Image Img)
        {
            if (pbServerStatus.InvokeRequired)
            {
                SetStatusImageCB d = new(SetStatusImage);
                ParentForm?.Invoke(d, new object[] {Img});
            }
            else
            {
                pbServerStatus.Image = Img;
            }
        }

        private void chkReconnectWhenReady_CheckedChanged(object sender, EventArgs e)
        {
            _ReconnectWhenReady = chkReconnectWhenReady.Checked;
        }

        private bool _ReconnectWhenReady;

        public bool ReconnectWhenReady
        {
            get => _ReconnectWhenReady;
            set
            {
                _ReconnectWhenReady = value;
                SetCheckbox(value);
            }
        }

        private delegate void SetCheckboxCB(bool Val);

        private void SetCheckbox(bool Val)
        {
            if (chkReconnectWhenReady.InvokeRequired)
            {
                SetCheckboxCB d = new(SetCheckbox);
                ParentForm?.Invoke(d, new object[] {Val});
            }
            else
            {
                chkReconnectWhenReady.Checked = Val;
            }
        }

        public delegate void CloseClickedEventHandler();

        private CloseClickedEventHandler CloseClickedEvent;

        public event CloseClickedEventHandler CloseClicked
        {
            add => CloseClickedEvent = (CloseClickedEventHandler)Delegate.Combine(CloseClickedEvent, value);
            remove => CloseClickedEvent = (CloseClickedEventHandler)Delegate.Remove(CloseClickedEvent, value);
        }


        private void btnClose_Click(object sender, EventArgs e)
        {
            CloseClickedEvent?.Invoke();
        }

        private void tmrAnimation_Tick(object sender, EventArgs e)
        {
            switch (lblAnimation.Text)
            {
                case "":
                    lblAnimation.Text = "»";
                    break;
                case "»":
                    lblAnimation.Text = "»»";
                    break;
                case "»»":
                    lblAnimation.Text = "»»»";
                    break;
                case "»»»":
                    lblAnimation.Text = "";
                    break;
            }
        }

        private delegate void DisposeReconnectGroupCB();

        public void DisposeReconnectGroup()
        {
            if (InvokeRequired)
            {
                DisposeReconnectGroupCB d = new(DisposeReconnectGroup);
                ParentForm?.Invoke(d);
            }
            else
            {
                Dispose();
            }
        }

        public void ReconnectGroup_Load(object sender, EventArgs e)
        {
            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            grpAutomaticReconnect.Text = Language.GroupboxAutomaticReconnect;
            btnClose.Text = Language._Close;
            lblServerStatus.Text = Language.ServerStatus;
            chkReconnectWhenReady.Text = Language.CheckboxReconnectWhenReady;
        }
    }
}