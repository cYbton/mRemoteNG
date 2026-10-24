using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using BrightIdeasSoftware;

namespace mRemoteNG.UI.Controls.ConnectionTree
{
    /// <summary>
    /// sysprep fork: draws the expand/collapse glyphs of the connection tree as small triangles in the
    /// text colour of the theme. The default glyph is the classic Windows plus/minus box with a white
    /// fill, which shows up as a white square next to the root and the folders in a dark theme.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class ThemedTreeRenderer : TreeListView.TreeRenderer
    {
        public Color GlyphColor { get; set; } = SystemColors.ControlText;

        public Color GlyphHotColor { get; set; } = SystemColors.HotTrack;

        protected override void DrawExpansionGlyph(Graphics g, Rectangle r, bool isExpanded)
        {
            int half = Math.Max(3, r.Height / 4);
            int cx = r.X + r.Width / 2;
            int cy = r.Y + r.Height / 2;

            Point[] triangle = isExpanded
                ? [new Point(cx - half, cy - half / 2), new Point(cx + half, cy - half / 2), new Point(cx, cy + half / 2 + 1)]
                : [new Point(cx - half / 2, cy - half), new Point(cx - half / 2, cy + half), new Point(cx + half / 2 + 1, cy)];

            SmoothingMode previousMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush brush = new(IsExpansionHot ? GlyphHotColor : GlyphColor))
                g.FillPolygon(brush, triangle);
            g.SmoothingMode = previousMode;
        }
    }
}
