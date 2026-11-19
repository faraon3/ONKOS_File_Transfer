using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ONKOSTray
{
    public enum ToastType
    {
        Info,
        Success,
        Warning,
        Error
    }

    public class ToastNotificationForm : Form
    {
        private static readonly List<ToastNotificationForm> ActiveToasts = new List<ToastNotificationForm>();
        private static readonly object LockObj = new object();

        private readonly Timer _lifeTimer;
        private readonly Color _accentColor;
        private readonly Action _onClickAction;

        // Evita que la notificación robe el foco si el usuario está escribiendo en otro programa
        protected override bool ShowWithoutActivation => true;

        public ToastNotificationForm(string title, string message, ToastType type, Action onClickAction = null, int durationMs = 6000)
        {
            _onClickAction = onClickAction;
            _accentColor = GetAccentColor(type);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            Size = new Size(360, 92);
            BackColor = Color.FromArgb(30, 34, 42); // Fondo oscuro moderno propio de ONKOS
            DoubleBuffered = true;

            BuildControls(title, message, type);

            _lifeTimer = new Timer { Interval = durationMs };
            _lifeTimer.Tick += (s, e) => CloseToast();
        }

        private Color GetAccentColor(ToastType type)
        {
            switch (type)
            {
                case ToastType.Success: return Color.FromArgb(46, 204, 113); // Verde
                case ToastType.Warning: return Color.FromArgb(243, 156, 18); // Ámbar
                case ToastType.Error:   return Color.FromArgb(231, 76, 60);  // Rojo
                default:                return Color.FromArgb(52, 152, 219); // Azul ONKOS
            }
        }

        private void BuildControls(string title, string message, ToastType type)
        {
            // Franja lateral de estado
            var sideBar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 6,
                BackColor = _accentColor
            };

            // Botón cerrar
            var btnClose = new Label
            {
                Text = "✕",
                ForeColor = Color.FromArgb(170, 178, 189),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Size = new Size(24, 24),
                Location = new Point(Width - 28, 6),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            btnClose.Click += (s, e) => CloseToast();

            // Etiqueta superior de marca/estado
            var lblBadge = new Label
            {
                Text = $"ONKOS • {GetBadgeText(type)}",
                ForeColor = _accentColor,
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                Location = new Point(16, 8),
                AutoSize = true
            };

            // Título principal
            var lblTitle = new Label
            {
                Text = title,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(16, 26),
                Size = new Size(Width - 52, 20),
                AutoEllipsis = true
            };

            // Mensaje descriptivo
            var lblMessage = new Label
            {
                Text = message,
                ForeColor = Color.FromArgb(210, 215, 222),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                Location = new Point(16, 48),
                Size = new Size(Width - 32, 36),
                AutoEllipsis = true
            };

            Controls.Add(lblBadge);
            Controls.Add(lblTitle);
            Controls.Add(lblMessage);
            Controls.Add(btnClose);
            Controls.Add(sideBar);

            // Acción al hacer clic en la tarjeta
            EventHandler clickHandler = (s, e) =>
            {
                _onClickAction?.Invoke();
                CloseToast();
            };

            Click += clickHandler;
            lblTitle.Click += clickHandler;
            lblMessage.Click += clickHandler;
            lblBadge.Click += clickHandler;
        }

        private string GetBadgeText(ToastType type)
        {
            switch (type)
            {
                case ToastType.Success: return "COMPLETADO";
                case ToastType.Warning: return "ADVERTENCIA";
                case ToastType.Error:   return "ERROR";
                default:                return "INFORMACIÓN";
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // Borde sutil para que destaque sobre fondos claros u oscuros
            using (var pen = new Pen(Color.FromArgb(60, 68, 82), 1))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        public static void ShowToast(string title, string message, ToastType type, Action onClick = null, int durationMs = 6000)
        {
            var toast = new ToastNotificationForm(title, message, type, onClick, durationMs);
            lock (LockObj)
            {
                // Máximo 4 notificaciones simultáneas en pantalla
                if (ActiveToasts.Count >= 4)
                {
                    var oldest = ActiveToasts[0];
                    oldest.CloseToast();
                }

                ActiveToasts.Add(toast);
                RepositionToasts();
            }

            toast.Show();
            toast._lifeTimer.Start();
        }

        private static void RepositionToasts()
        {
            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            int margin = 12;
            int currentBottom = workingArea.Bottom - margin;

            foreach (var t in ActiveToasts)
            {
                int x = workingArea.Right - t.Width - margin;
                int y = currentBottom - t.Height;
                t.Location = new Point(x, y);
                currentBottom = y - 8; // Separación vertical entre tarjetas
            }
        }

        public void CloseToast()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(CloseToast));
                return;
            }

            _lifeTimer?.Stop();
            lock (LockObj)
            {
                ActiveToasts.Remove(this);
                RepositionToasts();
            }

            if (!IsDisposed)
                Close();
        }
    }
}
