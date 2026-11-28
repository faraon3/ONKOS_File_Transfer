using System;
using System.Drawing;
using System.Windows.Forms;
using ONKOS.Core.Notifications;

namespace ONKOS.UI
{
    public sealed class VisualToastForm : Form
    {
        private readonly Timer _closeTimer;
        private readonly Timer _animTimer;
        private readonly int _targetX;
        private readonly int _targetY;

        public VisualToastForm(NotificationRecord record, int durationSeconds, Action onOpenAction = null)
        {
            ThemePalette p = ThemeManager.GetCurrentPalette();

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(340, 68);
            BackColor = p.SurfaceBg;
            ForeColor = p.TextPrimary;
            Font = ThemeManager.BodyFont;
            Opacity = ThemeManager.AreAnimationsEnabled() ? 0.0 : 0.96;

            Screen screen = Screen.FromPoint(Cursor.Position) ?? Screen.PrimaryScreen;
            Rectangle work = screen.WorkingArea;
            _targetX = Math.Max(work.Left + 12, work.Right - Width - 16);
            _targetY = Math.Max(work.Top + 12, work.Bottom - Height - 16);
            Location = new Point(_targetX, ThemeManager.AreAnimationsEnabled() ? _targetY + 14 : _targetY);

            var lblTitle = new Label
            {
                Text = record.Title ?? "Aviso",
                Font = ThemeManager.BodyFont,
                ForeColor = p.TextPrimary,
                Location = new Point(42, 13),
                Size = new Size(210, 20)
            };

            var lblSub = new Label
            {
                Text = record.SenderOrTarget ?? string.Empty,
                Font = ThemeManager.SecondaryFont,
                ForeColor = p.TextSecondary,
                Location = new Point(42, 34),
                Size = new Size(210, 18)
            };

            var btnOpen = new Button
            {
                Text = "Abrir",
                Size = new Size(68, 30),
                Location = new Point(258, 19)
            };
            ThemeManager.ConfigureSmoothButton(btnOpen, p, ButtonVariant.Secondary, 10);
            btnOpen.Click += (s, e) =>
            {
                Close();
                if (onOpenAction != null)
                    onOpenAction();
            };

            Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var r = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
                using (var gp = ThemeManager.CreateRoundedRectangle(r, 14F))
                using (var b = new SolidBrush(p.SurfaceBg))
                using (var pen = new Pen(p.BorderSoft, 1F))
                {
                    e.Graphics.FillPath(b, gp);
                    e.Graphics.DrawPath(pen, gp);
                }

                Color iconCol = record.Category == NotificationCategory.Error ? p.StatusError : p.AccentStart;
                LineIconKind kind = record.Category == NotificationCategory.Error ? LineIconKind.ErrorCircle : LineIconKind.CheckCircle;
                LineIcons.DrawIcon(e.Graphics, kind, new Rectangle(12, 21, 22, 22), iconCol, 1.75F);
            };

            Controls.AddRange(new Control[] { lblTitle, lblSub, btnOpen });

            _animTimer = new Timer { Interval = 18 };
            _animTimer.Tick += (s, e) =>
            {
                if (IsDisposed || Disposing) return;
                if (!ThemeManager.AreAnimationsEnabled())
                {
                    Location = new Point(_targetX, _targetY);
                    Opacity = 0.96;
                    _animTimer.Stop();
                    return;
                }

                if (Location.Y > _targetY)
                    Location = new Point(_targetX, Math.Max(_targetY, Location.Y - 3));
                if (Opacity < 0.96)
                    Opacity = Math.Min(0.96, Opacity + 0.16);
                if (Location.Y <= _targetY && Opacity >= 0.96)
                    _animTimer.Stop();
            };

            int ms = Math.Max(2, Math.Min(20, durationSeconds)) * 1000;
            _closeTimer = new Timer { Interval = ms };
            _closeTimer.Tick += (s, e) =>
            {
                _closeTimer.Stop();
                Close();
            };

            Shown += (s, e) =>
            {
                _animTimer.Start();
                _closeTimer.Start();
            };
            FormClosed += (s, e) =>
            {
                _animTimer.Stop();
                _animTimer.Dispose();
                _closeTimer.Stop();
                _closeTimer.Dispose();
            };
        }

        protected override bool ShowWithoutActivation => true;
    }
}
