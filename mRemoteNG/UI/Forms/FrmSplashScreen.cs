using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using mRemoteNG.App.Info;

namespace mRemoteNG.UI.Forms
{
    // sysprep fork: the splash screen as a WinForms form instead of WPF - WPF was only used for the splash and
    // added about 50 MB of libraries to the download. Same look as the former FrmSplashScreenNew.xaml: dark panel
    // with rounded corners, "mRemoteNG" in the logo font, version and subtitle below.
    [SupportedOSPlatform("windows")]
    public class FrmSplashScreen : Form
    {
        private static readonly Color PanelColor = ColorTranslator.FromHtml("#373C42");
        private static readonly Color BorderColor = ColorTranslator.FromHtml("#211E1B");
        private static readonly Color AccentColor = ColorTranslator.FromHtml("#5289F9");
        private static readonly Color TextColor = ColorTranslator.FromHtml("#E8EBEE");

        private static PrivateFontCollection? _logoFonts;
        private static IntPtr _logoFontMemory;

        private readonly Rectangle _screenArea;
        private readonly string _version = $"v. {GeneralAppInfo.ApplicationVersion}";

        public FrmSplashScreen(Rectangle screenArea)
        {
            _screenArea = screenArea;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = PanelColor;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            ClientSize = new Size(450, 120);
            Text = "mRemoteNG";
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Windows 11 rounds the corners itself; older Windows keeps square corners
            int roundCorners = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref roundCorners, sizeof(int));
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Rectangle area = _screenArea.IsEmpty ? (Screen.PrimaryScreen ?? Screen.AllScreens[0]).WorkingArea : _screenArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float scale = DeviceDpi / 96F;

            using (Pen border = new(BorderColor))
                g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

            FontFamily logoFamily = LogoFontFamily();
            using (Font logoFont = new(logoFamily, 40 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Brush accent = new SolidBrush(AccentColor))
            using (Brush text = new SolidBrush(TextColor))
            {
                g.DrawString("m", logoFont, accent, 109 * scale, 5 * scale);
                g.DrawString("RemoteNG", logoFont, text, 139 * scale, 5 * scale);
            }

            using StringFormat centered = new() { Alignment = StringAlignment.Center };
            using Brush textBrush = new SolidBrush(TextColor);
            using (Font versionFont = new("Segoe UI", 16 * scale, FontStyle.Regular, GraphicsUnit.Pixel))
                g.DrawString(_version, versionFont, textBrush, new RectangleF(0, 55 * scale, ClientSize.Width, 28 * scale), centered);
            using (Font subtitleFont = new("Segoe UI", 16 * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                g.DrawString("Multi-Remote Next Generation Connection Manager", subtitleFont, textBrush, new RectangleF(0, 88 * scale, ClientSize.Width, 30 * scale), centered);
        }

        // The logo font is embedded in the assembly; GDI+ needs it in memory for as long as the process runs
        private static FontFamily LogoFontFamily()
        {
            try
            {
                if (_logoFonts == null)
                {
                    Assembly assembly = typeof(FrmSplashScreen).Assembly;
                    string? resourceName = assembly.GetManifestResourceNames().FirstOrDefault(name => name.EndsWith("HandelGotDBol.ttf", StringComparison.OrdinalIgnoreCase));
                    if (resourceName == null) return FontFamily.GenericSansSerif;
                    using Stream? stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream == null) return FontFamily.GenericSansSerif;
                    byte[] fontData = new byte[stream.Length];
                    stream.ReadExactly(fontData);
                    _logoFontMemory = Marshal.AllocCoTaskMem(fontData.Length);
                    Marshal.Copy(fontData, 0, _logoFontMemory, fontData.Length);
                    PrivateFontCollection fonts = new();
                    fonts.AddMemoryFont(_logoFontMemory, fontData.Length);
                    _logoFonts = fonts;
                }
                return _logoFonts.Families.Length > 0 ? _logoFonts.Families[0] : FontFamily.GenericSansSerif;
            }
            catch (Exception)
            {
                return FontFamily.GenericSansSerif;
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
