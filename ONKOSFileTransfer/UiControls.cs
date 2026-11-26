using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ONKOSFileTransfer
{
    public class RoundedPanel : Panel
    {
        public Color ParentSurfaceColor { get; set; } = Color.FromArgb(14, 15, 24);
        public Color CardColor { get; set; } = Color.FromArgb(22, 24, 38);
        public Color BorderColor { get; set; } = Color.FromArgb(44, 48, 72);
        public int CornerRadius { get; set; } = 14;

        public RoundedPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = ParentSurfaceColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(ParentSurfaceColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = CreateRoundRect(rect, CornerRadius))
            using (SolidBrush fill = new SolidBrush(CardColor))
            using (Pen pen = new Pen(BorderColor, 1))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        internal static GraphicsPath CreateRoundRect(Rectangle r, int radius)
        {
            int d = Math.Max(2, radius * 2);
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public class RoundedButton : Control
    {
        private bool _hovered;

        public Color ParentSurfaceColor { get; set; } = Color.FromArgb(22, 24, 38);
        public Color FillColor { get; set; } = Color.FromArgb(124, 77, 224);
        public Color HoverColor { get; set; } = Color.FromArgb(142, 96, 242);
        public int CornerRadius { get; set; } = 10;

        public RoundedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color surface = Parent != null ? Parent.BackColor : ParentSurfaceColor;
            e.Graphics.Clear(surface);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg = !Enabled ? Color.FromArgb(46, 50, 72) : (_hovered ? HoverColor : FillColor);

            using (GraphicsPath path = RoundedPanel.CreateRoundRect(rect, CornerRadius))
            using (SolidBrush fill = new SolidBrush(bg))
            {
                e.Graphics.FillPath(fill, path);
            }

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                rect,
                Enabled ? ForeColor : Color.FromArgb(150, 155, 180),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
