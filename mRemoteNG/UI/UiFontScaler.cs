using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using mRemoteNG.UI.Controls.ConnectionTree;

namespace mRemoteNG.UI
{
    /// <summary>
    /// sysprep fork: applies the program font size from the options (Appearance) to the main window,
    /// the panels, the options and their menus. The designers use Segoe UI 8.25 pt for normal text and
    /// the menus use the Windows menu font (9 pt); both become the chosen size, larger fonts such as
    /// headings grow by the same ratio. The connection tree keeps its own font size, the tabs take the
    /// program font size in DockPaneStripNG.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class UiFontScaler
    {
        private const float DesignedTextSize = 8.25f;
        private const float LargestTextSize = 9f;

        // The font each control had before the first scaling, so a second pass over the same
        // control (a panel inside the main window) does not scale it twice
        private static readonly ConditionalWeakTable<object, Font> OriginalFonts = new();
        private static readonly Dictionary<(string, float, FontStyle, GraphicsUnit), Font> FontCache = new();

        private static float TargetSize => Properties.OptionsAppearancePage.Default.ProgramFontSize;

        public static void Apply(Control root)
        {
            float target = TargetSize;
            if (root == null || target <= 0 || Math.Abs(target - DesignedTextSize) < 0.01f)
                return;

            // Collect first, then set parents before children: a child that only inherits its
            // parent's font then already has the right size and does not lay itself out twice
            List<(Control Control, Font Original)> controls = new();
            List<(ToolStrip Strip, Font Original)> strips = new();
            Collect(root, controls, strips);

            foreach ((Control control, Font original) in controls)
                control.Font = Scale(original, target);
            foreach ((ToolStrip strip, Font original) in strips)
                strip.Font = Scale(original, target);
        }

        private static void Collect(Control control, List<(Control, Font)> controls, List<(ToolStrip, Font)> strips)
        {
            if (control.ContextMenuStrip != null)
                CollectStrip(control.ContextMenuStrip, strips);

            if (control is ConnectionTree)
                return;

            if (control is ToolStrip strip)
            {
                CollectStrip(strip, strips);
                return;
            }

            controls.Add((control, OriginalFonts.GetValue(control, c => ((Control)c).Font)));
            foreach (Control child in control.Controls)
                Collect(child, controls, strips);
        }

        private static void CollectStrip(ToolStrip strip, List<(ToolStrip, Font)> strips)
        {
            if (strips.Exists(s => ReferenceEquals(s.Item1, strip)))
                return;

            strips.Add((strip, OriginalFonts.GetValue(strip, s => ((ToolStrip)s).Font)));
            foreach (ToolStripItem item in strip.Items)
            {
                if (item is ToolStripDropDownItem { HasDropDownItems: true } dropDownItem)
                    CollectStrip(dropDownItem.DropDown, strips);
            }
        }

        private static Font Scale(Font original, float target)
        {
            float size = original.SizeInPoints <= LargestTextSize + 0.01f
                ? target
                : (float)Math.Round(original.SizeInPoints * target / DesignedTextSize * 4) / 4;

            (string, float, FontStyle, GraphicsUnit) key = (original.FontFamily.Name, size, original.Style, GraphicsUnit.Point);
            if (!FontCache.TryGetValue(key, out Font font))
            {
                font = new Font(original.FontFamily, size, original.Style, GraphicsUnit.Point, original.GdiCharSet);
                FontCache[key] = font;
            }

            return font;
        }
    }
}
