using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;
using mRemoteNG.App;
using mRemoteNG.Connection;
using mRemoteNG.Container;
using mRemoteNG.Tree.Root;

namespace mRemoteNG.UI
{
    [SupportedOSPlatform("windows")]
    public class StatusImageList : IDisposable
    {
        public ImageList ImageList { get; }

        public StatusImageList()
        {
            DisplayProperties display = new();
            // sysprep fork: icon size of the connection tree as set in the options (Appearance), scaled with the display
            int iconSize = Math.Clamp(Properties.OptionsAppearancePage.Default.ConnectionTreeIconSize, 16, 48);

            ImageList = new ImageList
            {
                ColorDepth = ColorDepth.Depth32Bit,
                ImageSize = new Size((int)Math.Round(iconSize * display.ResolutionScalingFactor.Width), (int)Math.Round(iconSize * display.ResolutionScalingFactor.Height)),
                TransparentColor = Color.Transparent
            };

            FillImageList(ImageList);
        }

        public object ImageGetter(object rowObject)
        {
            return GetKey(rowObject as ConnectionInfo);
        }

        public Image GetImage(ConnectionInfo connectionInfo)
        {
            string key = GetKey(connectionInfo);
            return ImageList.Images.ContainsKey(key)
                ? ImageList.Images[key]
                : null;
        }

        public string GetKey(ConnectionInfo connectionInfo)
        {
            if (connectionInfo == null) return "";
            if (connectionInfo is RootPuttySessionsNodeInfo) return "PuttySessions";
            if (connectionInfo is RootNodeInfo) return "Root";
            if (connectionInfo is ContainerInfo { IsRootGroup: true }) return "Root";
            if (connectionInfo is ContainerInfo) return "Folder";

            return GetConnectionIcon(connectionInfo);
        }

        private static string BuildConnectionIconName(string icon, bool connected)
        {
            string status = connected ? "Play" : "Default";
            return $"Connection_{icon}_{status}";
        }

        private const string DefaultConnectionIcon = "";

        private string GetConnectionIcon(ConnectionInfo connection)
        {
            if (string.IsNullOrEmpty(connection.Icon))
            {
                return DefaultConnectionIcon;
            }

            bool connected = connection.OpenConnections.Count > 0;
            string name = BuildConnectionIconName(connection.Icon, connected);
            if (ImageList.Images.ContainsKey(name)) return name;
            Icon image = ConnectionIcon.FromString(connection.Icon);
            if (image == null)
            {
                return DefaultConnectionIcon;
            }

            ImageList.Images.Add(BuildConnectionIconName(connection.Icon, false), image);
            ImageList.Images.Add(BuildConnectionIconName(connection.Icon, true), WithConnectedDot(image, ImageList.ImageSize));
            return name;
        }

        // sysprep fork: a green dot at the bottom right instead of the play button overlay. The icon is rendered
        // at the size of the image list, and the ring around the dot is cut out to transparent, so the dot stays
        // apart from the icon on any background.
        private static readonly Color ConnectedDotColor = Color.FromArgb(70, 200, 90);

        private static Bitmap WithConnectedDot(Icon icon, Size size)
        {
            Bitmap result;
            using (Icon sized = new(icon, size))
            using (Bitmap rendered = sized.ToBitmap())
                result = new Bitmap(rendered, size);

            float dot = Math.Max(5f, Math.Min(size.Width, size.Height) * 0.45f);
            float ring = Math.Max(1.5f, dot * 0.2f);
            RectangleF dotRect = new(size.Width - dot, size.Height - dot, dot, dot);
            RectangleF ringRect = RectangleF.Inflate(dotRect, ring, ring);

            using (Graphics gr = Graphics.FromImage(result))
            {
                gr.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                gr.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                using (SolidBrush clear = new(Color.Transparent))
                    gr.FillEllipse(clear, ringRect);
                gr.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
                using (SolidBrush green = new(ConnectedDotColor))
                    gr.FillEllipse(green, dotRect);
            }

            return result;
        }

        private static void FillImageList(ImageList imageList)
        {
            try
            {
                // sysprep fork: root and folder icons without their white fills in a dark theme; the
                // connection icons are left alone, they are drawn for any background
                imageList.Images.Add("Root", DarkThemeIcons.Adapt(Properties.Resources.ASPWebSite_16x));
                imageList.Images.Add("Folder", DarkThemeIcons.Adapt(Properties.Resources.FolderClosed_16x));
                imageList.Images.Add("PuttySessions", DarkThemeIcons.Adapt(Properties.Resources.PuttySessions));
            }
            catch (Exception ex)
            {
                Runtime.MessageCollector.AddExceptionStackTrace($"Unable to fill the image list of type {nameof(StatusImageList)}", ex);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                ImageList?.Dispose();
            }
        }
    }
}