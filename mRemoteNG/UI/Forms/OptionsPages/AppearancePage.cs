using System;
using System.Windows.Forms;
using mRemoteNG.App;
using mRemoteNG.Properties;
using mRemoteNG.Tools;
using mRemoteNG.Resources.Language;
using System.Runtime.Versioning;
using mRemoteNG.Config.Settings.Registry;
using mRemoteNG.UI.Controls;

namespace mRemoteNG.UI.Forms.OptionsPages
{
    [SupportedOSPlatform("windows")]
    public sealed partial class AppearancePage
    {
        private OptRegistryAppearancePage pageRegSettingsInstance;

        // sysprep fork: font size of the connection tree, icon size in the connection tree,
        // font size of the rest of the program (tabs, panels, dialogs; menus keep the Windows menu font).
        // The setting behind the tree font is still called TreeAndTabFontSize so existing values are kept.
        // Created here instead of in the designer to keep the change easy to merge with upstream.
        private MrngLabel lblTreeAndTabFontSize;
        private MrngNumericUpDown numTreeAndTabFontSize;
        private MrngLabel lblConnectionTreeIconSize;
        private MrngNumericUpDown numConnectionTreeIconSize;
        private MrngLabel lblProgramFontSize;
        private MrngNumericUpDown numProgramFontSize;
        private MrngLabel lblSizesRestartRequired;

        public AppearancePage()
        {
            InitializeComponent();
            AddSizeOptions();
            ApplyTheme();
            PageIcon = Resources.ImageConverter.GetImageAsIcon(Properties.Resources.Panel_16x);
        }

        private void AddSizeOptions()
        {
            lblTreeAndTabFontSize = new MrngLabel { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 6, 3, 3) };
            numTreeAndTabFontSize = new MrngNumericUpDown { DecimalPlaces = 2, Increment = 0.75M, Minimum = 7.5M, Maximum = 16.5M, Width = 70, Margin = new Padding(6, 3, 3, 3) };
            lblConnectionTreeIconSize = new MrngLabel { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 6, 3, 3) };
            numConnectionTreeIconSize = new MrngNumericUpDown { Increment = 4M, Minimum = 16M, Maximum = 48M, Width = 70, Margin = new Padding(6, 3, 3, 3) };
            lblProgramFontSize = new MrngLabel { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(6, 6, 3, 3) };
            numProgramFontSize = new MrngNumericUpDown { DecimalPlaces = 2, Increment = 0.75M, Minimum = 7.5M, Maximum = 16.5M, Width = 70, Margin = new Padding(6, 3, 3, 3) };
            lblSizesRestartRequired = new MrngLabel { AutoSize = true, Margin = new Padding(6, 3, 3, 6) };

            TableLayoutPanel sizes = new() { ColumnCount = 2, RowCount = 4, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0) };
            sizes.Controls.Add(lblTreeAndTabFontSize, 0, 0);
            sizes.Controls.Add(numTreeAndTabFontSize, 1, 0);
            sizes.Controls.Add(lblConnectionTreeIconSize, 0, 1);
            sizes.Controls.Add(numConnectionTreeIconSize, 1, 1);
            sizes.Controls.Add(lblProgramFontSize, 0, 2);
            sizes.Controls.Add(numProgramFontSize, 1, 2);
            sizes.Controls.Add(lblSizesRestartRequired, 0, 3);
            sizes.SetColumnSpan(lblSizesRestartRequired, 2);

            tableLayoutPanelAppearance.RowCount++;
            tableLayoutPanelAppearance.RowStyles.Add(new RowStyle());
            tableLayoutPanelAppearance.Controls.Add(sizes, 0, tableLayoutPanelAppearance.RowCount - 1);
            pnlOptions.Height += 128;
        }

        public override string PageName
        {
            get => Language.Appearance;
            set { }
        }

        public override void ApplyLanguage()
        {
            base.ApplyLanguage();

            lblLanguage.Text = Language.LanguageString;
            lblLanguageRestartRequired.Text = string.Empty;
            chkShowDescriptionTooltipsInTree.Text = Language.ShowDescriptionTooltips;
            chkShowFullConnectionsFilePathInTitle.Text = Language.ShowFullConsFilePath;
            chkShowSystemTrayIcon.Text = Language.AlwaysShowSysTrayIcon;
            chkMinimizeToSystemTray.Text = Language.MinimizeToSysTray;
            chkCloseToSystemTray.Text = Language.CloseToSysTray;
            chkEnableConnectionTreeAnimations.Text = Language.EnableConnectionTreeAnimations;
            lblTreeAndTabFontSize.Text = Language.TreeAndTabFontSize;
            lblConnectionTreeIconSize.Text = Language.ConnectionTreeIconSize;
            lblProgramFontSize.Text = Language.ProgramFontSize;
            lblSizesRestartRequired.Text = Language.TakesEffectAfterRestart;
            lblRegistrySettingsUsedInfo.Text = Language.OptionsCompanyPolicyMessage;
        }

        public override void LoadSettings()
        {
            // sysprep fork: the UI language is fixed to English (OverrideUICulture), the build ships no translations
            lblLanguage.Visible = false;
            cboLanguage.Visible = false;
            lblLanguageRestartRequired.Visible = false;

            cboLanguage.Items.Clear();
            cboLanguage.Items.Add(Language.LanguageDefault);

            foreach (string nativeName in SupportedCultures.CultureNativeNames)
            {
                cboLanguage.Items.Add(nativeName);
            }

            if (!string.IsNullOrEmpty(Settings.Default.OverrideUICulture) &&
                SupportedCultures.IsNameSupported(Settings.Default.OverrideUICulture))
            {
                cboLanguage.SelectedItem = SupportedCultures.get_CultureNativeName(Settings.Default.OverrideUICulture);
            }

            if (cboLanguage.SelectedIndex == -1)
            {
                cboLanguage.SelectedIndex = 0;
            }

            chkShowDescriptionTooltipsInTree.Checked = Properties.OptionsAppearancePage.Default.ShowDescriptionTooltipsInTree;
            chkShowFullConnectionsFilePathInTitle.Checked = Properties.OptionsAppearancePage.Default.ShowCompleteConsPathInTitle;
            chkShowSystemTrayIcon.Checked = Properties.OptionsAppearancePage.Default.ShowSystemTrayIcon;
            chkMinimizeToSystemTray.Checked = Properties.OptionsAppearancePage.Default.MinimizeToTray;
            chkCloseToSystemTray.Checked = Properties.OptionsAppearancePage.Default.CloseToTray;
            chkEnableConnectionTreeAnimations.Checked = Properties.OptionsAppearancePage.Default.EnableConnectionTreeAnimations;
            numTreeAndTabFontSize.Value = Math.Clamp((decimal)Properties.OptionsAppearancePage.Default.TreeAndTabFontSize, numTreeAndTabFontSize.Minimum, numTreeAndTabFontSize.Maximum);
            numConnectionTreeIconSize.Value = Math.Clamp(Properties.OptionsAppearancePage.Default.ConnectionTreeIconSize, numConnectionTreeIconSize.Minimum, numConnectionTreeIconSize.Maximum);
            numProgramFontSize.Value = Math.Clamp((decimal)Properties.OptionsAppearancePage.Default.ProgramFontSize, numProgramFontSize.Minimum, numProgramFontSize.Maximum);
        }

        public override void SaveSettings()
        {
            // sysprep fork: no language selection - OverrideUICulture keeps its English default

            Properties.OptionsAppearancePage.Default.ShowDescriptionTooltipsInTree = chkShowDescriptionTooltipsInTree.Checked;
            Properties.OptionsAppearancePage.Default.ShowCompleteConsPathInTitle = chkShowFullConnectionsFilePathInTitle.Checked;
            FrmMain.Default.ShowFullPathInTitle = chkShowFullConnectionsFilePathInTitle.Checked;

            Properties.OptionsAppearancePage.Default.ShowSystemTrayIcon = chkShowSystemTrayIcon.Checked;
            if (Properties.OptionsAppearancePage.Default.ShowSystemTrayIcon)
            {
                if (Runtime.NotificationAreaIcon == null)
                {
                    Runtime.NotificationAreaIcon = new NotificationAreaIcon();
                }
            }
            else
            {
                if (Runtime.NotificationAreaIcon != null)
                {
                    Runtime.NotificationAreaIcon.Dispose();
                    Runtime.NotificationAreaIcon = null;
                }
            }

            Properties.OptionsAppearancePage.Default.MinimizeToTray = chkMinimizeToSystemTray.Checked;
            Properties.OptionsAppearancePage.Default.CloseToTray = chkCloseToSystemTray.Checked;
            Properties.OptionsAppearancePage.Default.EnableConnectionTreeAnimations = chkEnableConnectionTreeAnimations.Checked;
            Properties.OptionsAppearancePage.Default.TreeAndTabFontSize = (float)numTreeAndTabFontSize.Value;
            Properties.OptionsAppearancePage.Default.ConnectionTreeIconSize = (int)numConnectionTreeIconSize.Value;
            Properties.OptionsAppearancePage.Default.ProgramFontSize = (float)numProgramFontSize.Value;
        }

        public override void LoadRegistrySettings()
        {
            Type settingsType = typeof(OptRegistryAppearancePage);
            RegistryLoader.RegistrySettings.TryGetValue(settingsType, out var settings);
            pageRegSettingsInstance = settings as OptRegistryAppearancePage;

            // If registry settings don't exist, create a default instance to prevent null reference exceptions
            if (pageRegSettingsInstance == null)
            {
                pageRegSettingsInstance = new OptRegistryAppearancePage();
                Logger.Instance.Log?.Debug("[AppearancePage.LoadRegistrySettings] pageRegSettingsInstance was null, created default instance");
            }

            RegistryLoader.Cleanup(settingsType);

            // ***
            // Disable controls based on the registry settings.
            //
            if (pageRegSettingsInstance.ShowDescriptionTooltipsInConTree.IsSet)
                DisableControl(chkShowDescriptionTooltipsInTree);

            if (pageRegSettingsInstance.ShowCompleteConFilePathInTitle.IsSet)
                DisableControl(chkShowFullConnectionsFilePathInTitle);

            if (pageRegSettingsInstance.AlwaysShowSystemTrayIcon.IsSet)
                DisableControl(chkShowSystemTrayIcon);

            if (pageRegSettingsInstance.MinimizeToTray.IsSet)
                DisableControl(chkMinimizeToSystemTray);

            if (pageRegSettingsInstance.CloseToTray.IsSet)
                DisableControl(chkCloseToSystemTray);

            // Updates the visibility of the information label indicating whether registry settings are used.
            lblRegistrySettingsUsedInfo.Visible = ShowRegistrySettingsUsedInfo();
        }

        /// <summary>
        /// Checks if specific registry settings related to appearence page are used.
        /// </summary>
        public bool ShowRegistrySettingsUsedInfo()
        {
            return pageRegSettingsInstance.ShowDescriptionTooltipsInConTree.IsSet
                || pageRegSettingsInstance.ShowCompleteConFilePathInTitle.IsSet
                || pageRegSettingsInstance.AlwaysShowSystemTrayIcon.IsSet
                || pageRegSettingsInstance.MinimizeToTray.IsSet
                || pageRegSettingsInstance.CloseToTray.IsSet;
        }
    }
}