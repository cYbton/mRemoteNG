using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using mRemoteNG.UI.Controls.ConnectionInfoPropertyGrid;
using mRemoteNG.UI.Controls.ConnectionTree;

namespace mRemoteNG.UI
{
    /// <summary>
    /// sysprep fork: applies the program font size from the options (Appearance) to the main window,
    /// the panels and the options. The designers use Segoe UI 8.25 pt for normal text (some 9 pt); that
    /// becomes the chosen size, larger fonts such as headings grow by the same ratio. Menus, toolbars
    /// and context menus keep the Windows menu font, the connection tree keeps its own font size and
    /// the tabs take the program font size in DockPaneStripNG.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class UiFontScaler
    {
        private const float DesignedTextSize = 8.25f;
        private const float LargestTextSize = 9f;

        // The font each control had before the first scaling, so a second pass over the same
        // control (a panel inside the main window) does not scale it twice
        private static readonly ConditionalWeakTable<Control, Font> OriginalFonts = new();
        private static readonly Dictionary<(string, float, FontStyle), Font> FontCache = new();

        private static float TargetSize => Properties.OptionsAppearancePage.Default.ProgramFontSize;

        public static void Apply(Control root)
        {
            float target = TargetSize;
            if (root == null || root.IsDisposed || target <= 0 || Math.Abs(target - DesignedTextSize) < 0.01f)
                return;

            // Collect first, then set parents before children: a child that only inherits its
            // parent's font then already has the right size and does not lay itself out twice
            List<(Control Control, Font Original)> controls = new();
            Collect(root, controls);

            foreach ((Control control, Font original) in controls)
                control.Font = Scale(original, target);
        }

        /// <summary>
        /// Like <see cref="Apply"/>, for a control that places its children at fixed positions and scales with
        /// the DPI only (most options pages, the reconnect box): positions and sizes grow with the font, as a
        /// control scaling with the font would do it itself, so the larger text does not overlap.
        /// </summary>
        public static void ApplyWithLayout(ContainerControl root)
        {
            if (root == null || root.IsDisposed) return;

            float sizeBefore = root.Font.SizeInPoints;
            Apply(root);
            float ratio = root.Font.SizeInPoints / sizeBefore;
            if (root.AutoScaleMode != AutoScaleMode.Font && Math.Abs(ratio - 1) > 0.01f)
                root.Scale(new SizeF(ratio, ratio));
        }

        private static void Collect(Control control, List<(Control, Font)> controls)
        {
            // The tree and the Config panel's property grid have the tree font size of their own
            if (control.IsDisposed || control is ConnectionTree || control is ConnectionInfoPropertyGrid || control is ToolStrip)
                return;

            controls.Add((control, OriginalFonts.GetValue(control, c => c.Font)));
            foreach (Control child in control.Controls)
                Collect(child, controls);
        }

        private static Font Scale(Font original, float target)
        {
            float size = original.SizeInPoints <= LargestTextSize + 0.01f
                ? target
                : (float)Math.Round(original.SizeInPoints * target / DesignedTextSize * 4) / 4;

            (string, float, FontStyle) key = (original.FontFamily.Name, size, original.Style);
            if (!FontCache.TryGetValue(key, out Font font))
            {
                font = new Font(original.FontFamily, size, original.Style, GraphicsUnit.Point, original.GdiCharSet);
                FontCache[key] = font;
            }

            return font;
        }
    }
}
