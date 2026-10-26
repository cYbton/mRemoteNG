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

        // Ring and dot are drawn at this multiple of the icon size and scaled down - circles of a few pixels drawn
        // directly come out jagged, and a cut-out drawn with CompositingMode.SourceCopy loses its anti-aliasing
        private const int DotSupersampling = 4;

        private static Bitmap WithConnectedDot(Icon icon, Size size)
        {
            Bitmap result = new(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Icon sized = new(icon, size))
            using (Bitmap rendered = sized.ToBitmap())
            using (Graphics gr = Graphics.FromImage(result))
            {
                gr.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                gr.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                gr.DrawImage(rendered, new Rectangle(Point.Empty, size));
            }

            float dot = Math.Max(5f, Math.Min(size.Width, size.Height) * 0.45f);
            float ring = Math.Max(1.5f, dot * 0.2f);
            RectangleF dotRect = new(size.Width - dot, size.Height - dot, dot, dot);
            RectangleF ringRect = RectangleF.Inflate(dotRect, ring, ring);

            // The ring: the icon's own alpha is lowered by how much of each pixel the ring covers
            using (Bitmap ringMask = RenderEllipse(size, ringRect, Color.White))
            {
                for (int y = 0; y < size.Height; y++)
                {
                    for (int x = 0; x < size.Width; x++)
                    {
                        int coverage = ringMask.GetPixel(x, y).A;
                        if (coverage == 0) continue;
                        Color pixel = result.GetPixel(x, y);
                        result.SetPixel(x, y, Color.FromArgb(pixel.A * (255 - coverage) / 255, pixel.R, pixel.G, pixel.B));
                    }
                }
            }

            using (Bitmap greenDot = RenderEllipse(size, dotRect, ConnectedDotColor))
            using (Graphics gr = Graphics.FromImage(result))
                gr.DrawImageUnscaled(greenDot, 0, 0);

            return result;
        }

        // An anti-aliased ellipse at the final size, drawn DotSupersampling times larger and scaled down
        private static Bitmap RenderEllipse(Size size, RectangleF ellipse, Color color)
        {
            using Bitmap large = new(size.Width * DotSupersampling, size.Height * DotSupersampling, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics gr = Graphics.FromImage(large))
            using (SolidBrush brush = new(color))
            {
                gr.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                gr.FillEllipse(brush, ellipse.X * DotSupersampling, ellipse.Y * DotSupersampling, ellipse.Width * DotSupersampling, ellipse.Height * DotSupersampling);
            }

            Bitmap small = new(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics gr = Graphics.FromImage(small))
            {
                gr.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                gr.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                gr.DrawImage(large, new Rectangle(Point.Empty, size));
            }
            return small;
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