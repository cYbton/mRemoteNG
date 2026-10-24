using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using mRemoteNG.Themes;

namespace mRemoteNG.UI
{
    /// <summary>
    /// sysprep fork: adapts the Visual Studio icons (white fills, dark grey outlines) to a dark theme,
    /// the way Visual Studio does it: the lightness of grey pixels is inverted towards the background,
    /// so the white fills take the background colour and the outlines turn light. Coloured pixels
    /// (blue globe, red cross, tan folder) stay as they are. In a light theme nothing changes.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class DarkThemeIcons
    {
        private const float GreySaturationLimit = 0.2f;

        private static readonly ConditionalWeakTable<Image, Image> Converted = new();
        private static readonly ConditionalWeakTable<Image, Image> Originals = new();

        public static bool IsActive
        {
            get
            {
                ThemeManager themeManager = ThemeManager.getInstance();
                return themeManager.ThemingActive && themeManager.IsActiveThemeDark;
            }
        }

        /// <summary>The image adapted to the dark theme, or the image itself in a light theme.</summary>
        public static Image Adapt(Image image)
        {
            if (image == null || !IsActive || Originals.TryGetValue(image, out _))
                return image;

            if (Converted.TryGetValue(image, out Image converted))
                return converted;

            Color background = ThemeManager.getInstance().ActiveTheme.Theme.ColorPalette.MainWindowActive.Background;
            converted = Convert(image, background.GetBrightness());
            Converted.AddOrUpdate(image, converted);
            Originals.AddOrUpdate(converted, image);
            return converted;
        }

        /// <summary>Adapts the images of all items of the strip, including its drop-down menus.</summary>
        public static void ApplyTo(ToolStrip strip)
        {
            foreach (ToolStripItem item in strip.Items)
            {
                item.Image = Adapt(item.Image);
                if (item is ToolStripDropDownItem { HasDropDownItems: true } dropDownItem)
                    ApplyTo(dropDownItem.DropDown);
            }
        }

        private static Bitmap Convert(Image image, float backgroundLightness)
        {
            Bitmap result = new(image);
            for (int y = 0; y < result.Height; y++)
            {
                for (int x = 0; x < result.Width; x++)
                {
                    Color pixel = result.GetPixel(x, y);
                    if (pixel.A == 0 || pixel.GetSaturation() > GreySaturationLimit)
                        continue;

                    float lightness = backgroundLightness + (1 - pixel.GetBrightness()) * (1 - backgroundLightness);
                    int grey = (int)(lightness * 255 + 0.5f);
                    result.SetPixel(x, y, Color.FromArgb(pixel.A, grey, grey, grey));
                }
            }

            return result;
        }
    }
}
