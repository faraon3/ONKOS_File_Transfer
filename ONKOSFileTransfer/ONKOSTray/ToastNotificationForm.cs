using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace ONKOSFileTransfer.ONKOSTray
{
    public class ToastNotificationForm : Form
    {
        private readonly Timer lifeTimer;
        private readonly Timer fadeTimer;
        private readonly string targetFolder;

        private static readonly Color BgColor = Color.FromArgb(20, 24, 33);
        private static readonly Color BorderColor = Color.FromArgb(48, 58, 76);
        private static readonly Color AccentColor = Color.FromArgb(28, 169, 255);
        private static readonly Color SuccessAccent = Color.FromArgb(20, 196, 166);
        private static readonly Color TitleColor = Color.FromArgb(244, 247, 252);
        private static readonly Color SubtitleColor = Color.FromArgb(165, 177, 196);
        private static readonly Color MutedColor = Color.FromArgb(115, 128, 150);

        public ToastNotificationForm(string title, string message, string inboxPath = @"C:\ProgramData\ONKOS\Inbox", int durationMs = 5500, bool isSuccess = true)
        {
            targetFolder = inboxPath;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            Size = new Size(370, 96);
            BackColor = BgColor;
            Opacity = 0.97;

            Color stripeColor = isSuccess ? SuccessAccent : AccentColor;

            Panel accentBar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 5,
                BackColor = stripeColor
            };

            PictureBox iconBox = new PictureBox
            {
                Location = new Point(18, 22),
                Size = new Size(44, 44),
                SizeMode = PictureBoxSizeMode.CenterImage,
                BackColor = Color.FromArgb(30, 37, 51)
            };
            iconBox.Image = BuildBadgeBitmap(stripeColor);

            Label lblTag = new Label
            {
                Text = "ONKOS TRANSFER",
                Location = new Point(74, 13),
                Size = new Size(230, 16),
                Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                ForeColor = stripeColor,
                BackColor = Color.Transparent
            };

            Label lblTitle = new Label
            {
                Text = title,
                Location = new Point(74, 30),
                Size = new Size(255, 22),
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                ForeColor = TitleColor,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };

            Label lblMessage = new Label
            {
                Text = message,
                Location = new Point(74, 53),
                Size = new Size(265, 32),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Regular),
                ForeColor = SubtitleColor,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };

            Label lblHint = new Label
            {
                Text = "Clic para abrir carpeta",
                Location = new Point(242, 73),
                Size = new Size(112, 16),
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 7.5F, FontStyle.Italic),
                ForeColor = MutedColor,
                BackColor = Color.Transparent
            };

            Label btnClose = new Label
            {
                Text = "×",
                Location = new Point(340, 8),
                Size = new Size(22, 22),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = MutedColor,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand
            };
            btnClose.MouseEnter += (s, e) => btnClose.ForeColor = TitleColor;
            btnClose.MouseLeave += (s, e) => btnClose.ForeColor = MutedColor;
            btnClose.Click += (s, e) => Close();

            Controls.Add(lblHint);
            Controls.Add(lblMessage);
            Controls.Add(lblTitle);
            Controls.Add(lblTag);
            Controls.Add(iconBox);
            Controls.Add(btnClose);
            Controls.Add(accentBar);

            WireClickToOpen(this);
            WireClickToOpen(iconBox);
            WireClickToOpen(lblTag);
            WireClickToOpen(lblTitle);
            WireClickToOpen(lblMessage);
            WireClickToOpen(lblHint);

            PositionBottomRight();

            lifeTimer = new Timer { Interval = Math.Max(1500, durationMs) };
            lifeTimer.Tick += (s, e) =>
            {
                lifeTimer.Stop();
                fadeTimer.Start();
            };

            fadeTimer = new Timer { Interval = 35 };
            fadeTimer.Tick += (s, e) =>
            {
                if (Opacity > 0.08)
                {
                    Opacity -= 0.08;
                }
                else
                {
                    fadeTimer.Stop();
                    Close();
                }
            };

            Shown += (s, e) => lifeTimer.Start();
        }

        private void WireClickToOpen(Control ctrl)
        {
            ctrl.Cursor = Cursors.Hand;
            ctrl.Click += (s, e) =>
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(targetFolder))
                    {
                        Directory.CreateDirectory(targetFolder);
                        Process.Start("explorer.exe", targetFolder);
                    }
                }
                catch
                {
                }
                Close();
            };
        }

        private Bitmap BuildBadgeBitmap(Color color)
        {
            Bitmap bmp = new Bitmap(28, 28);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen p = new Pen(color, 2.4F))
                {
                    g.DrawArc(p, 3, 3, 22, 22, 0, 360);
                    g.DrawLine(p, 14, 7, 14, 18);
                    g.DrawLine(p, 9, 13, 14, 18);
                    g.DrawLine(p, 19, 13, 14, 18);
                }
            }
            return bmp;
        }

        private void PositionBottomRight()
        {
            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            int margin = 16;
            int x = workingArea.Right - Width - margin;
            int y = workingArea.Bottom - Height - margin;
            Location = new Point(x, y);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen borderPen = new Pen(BorderColor, 1))
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (lifeTimer != null)
            {
                lifeTimer.Stop();
                lifeTimer.Dispose();
            }
            if (fadeTimer != null)
            {
                fadeTimer.Stop();
                fadeTimer.Dispose();
            }
            base.OnFormClosed(e);
        }
    }
}
