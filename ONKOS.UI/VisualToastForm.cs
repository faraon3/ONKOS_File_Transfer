using System;
using System.Drawing;
using System.Windows.Forms;
using ONKOS.Core.Notifications;

namespace ONKOS.UI
{
    public sealed class VisualToastForm : Form
    {
        private readonly Timer _closeTimer;

        public VisualToastForm(NotificationRecord record, int durationSeconds)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(370, 108);
            BackColor = Color.FromArgb(24, 27, 44);
            ForeColor = Color.FromArgb(246, 247, 252);
            Font = new Font("Segoe UI", 9.5F);

            var lblCat = new Label
            {
                Text = GetCategoryLabel(record.Category),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(184, 148, 255),
                Location = new Point(14, 10),
                Size = new Size(310, 18)
            };

            var btnClose = new Button
            {
                Text = "×",
                Location = new Point(336, 6),
                Size = new Size(26, 22),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(200, 206, 232),
                BackColor = Color.FromArgb(24, 27, 44)
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => Close();

            var lblTitle = new Label
            {
                Text = record.Title ?? string.Empty,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(14, 30),
                Size = new Size(342, 22)
            };

            var lblPeer = new Label
            {
                Text = record.SenderOrTarget ?? string.Empty,
                ForeColor = Color.FromArgb(170, 225, 195),
                Location = new Point(14, 54),
                Size = new Size(342, 20)
            };

            var lblDetail = new Label
            {
                Text = record.Detail ?? string.Empty,
                ForeColor = Color.FromArgb(175, 182, 210),
                Location = new Point(14, 76),
                Size = new Size(342, 22)
            };

            Controls.AddRange(new Control[] { lblCat, btnClose, lblTitle, lblPeer, lblDetail });
            PositionOnActiveMonitor();

            int intervalMs = Math.Max(2, Math.Min(30, durationSeconds)) * 1000;
            _closeTimer = new Timer { Interval = intervalMs };
            _closeTimer.Tick += (s, e) =>
            {
                _closeTimer.Stop();
                Close();
            };
            Shown += (s, e) => _closeTimer.Start();
            FormClosed += (s, e) =>
            {
                _closeTimer.Stop();
                _closeTimer.Dispose();
            };
        }

        protected override bool ShowWithoutActivation => true;

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
                    return "AUTORIZACIÓN PENDIENTE";
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
