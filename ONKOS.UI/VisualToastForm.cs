using System;
using System.Drawing;
using System.Windows.Forms;
using ONKOS.Core.Notifications;

namespace ONKOS.UI
{
    public sealed class VisualToastForm : Form
    {
        private readonly Timer _closeTimer;
        private readonly Timer _fadeTimer;

        public VisualToastForm(NotificationRecord record, int durationSeconds)
        {
            ThemePalette p = ThemeManager.GetCurrentPalette();

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(356, 94);
            BackColor = p.CardBg;
            ForeColor = p.TextPrimary;
            Font = ThemeManager.BaseFont;
            Opacity = 0.0;

            var lblCat = new Label
            {
                Text = GetCategoryLabel(record.Category),
                Font = ThemeManager.BoldFont,
                ForeColor = p.PrimaryAccent,
                Location = new Point(14, 10),
                Size = new Size(295, 18)
            };

            var btnClose = new Button
            {
                Text = "×",
                Location = new Point(320, 6),
                Size = new Size(26, 22),
                FlatStyle = FlatStyle.Flat,
                ForeColor = p.TextSecondary,
                BackColor = p.CardBg,
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => Close();

            var lblTitle = new Label
            {
                Text = record.Title ?? string.Empty,
                Font = ThemeManager.BoldFont,
                ForeColor = p.TextPrimary,
                Location = new Point(14, 30),
                Size = new Size(328, 20)
            };

            var lblPeer = new Label
            {
                Text = record.SenderOrTarget ?? string.Empty,
                ForeColor = p.TextSecondary,
                Location = new Point(14, 52),
                Size = new Size(328, 18)
            };

            var lblDetail = new Label
            {
                Text = record.Detail ?? string.Empty,
                ForeColor = p.TextSecondary,
                Location = new Point(14, 71),
                Size = new Size(328, 18)
            };

            Controls.AddRange(new Control[] { lblCat, btnClose, lblTitle, lblPeer, lblDetail });
            ThemeManager.ApplyRoundedRegion(this, 12);
            PositionOnActiveMonitor();

            _fadeTimer = new Timer { Interval = 25 };
            _fadeTimer.Tick += OnFadeTick;

            int intervalMs = Math.Max(2, Math.Min(30, durationSeconds)) * 1000;
            _closeTimer = new Timer { Interval = intervalMs };
            _closeTimer.Tick += (s, e) =>
            {
                _closeTimer.Stop();
                Close();
            };

            Shown += (s, e) =>
            {
                _fadeTimer.Start();
                _closeTimer.Start();
            };

            FormClosed += (s, e) =>
            {
                _fadeTimer.Stop();
                _fadeTimer.Dispose();
                _closeTimer.Stop();
                _closeTimer.Dispose();
            };
        }

        protected override bool ShowWithoutActivation => true;

        private void OnFadeTick(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing)
                return;

            if (Opacity < 0.96)
                Opacity = Math.Min(0.96, Opacity + 0.16);
            else
                _fadeTimer.Stop();
        }

        private void PositionOnActiveMonitor()
        {
            Screen targetScreen = Screen.FromPoint(Cursor.Position) ?? Screen.PrimaryScreen;
            Rectangle work = targetScreen.WorkingArea;
            int x = work.Right - Width - 16;
            int y = work.Bottom - Height - 16;
            Location = new Point(Math.Max(work.Left + 8, x), Math.Max(work.Top + 8, y));
        }

        private static string GetCategoryLabel(NotificationCategory cat)
        {
            switch (cat)
            {
                case NotificationCategory.Sent:
                    return "ENVÍO";
                case NotificationCategory.Received:
                    return "RECEPCIÓN";
                case NotificationCategory.Error:
                    return "ERROR";
                case NotificationCategory.AuthorizationRequest:
                    return "AUTORIZACIÓN";
                case NotificationCategory.SecurityChange:
                    return "SEGURIDAD";
                case NotificationCategory.ServiceStatus:
                    return "SERVICIO";
                default:
                    return "AVISO";
            }
        }
    }
}
