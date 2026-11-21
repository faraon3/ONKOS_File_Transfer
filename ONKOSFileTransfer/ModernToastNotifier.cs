using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ONKOSFileTransfer
{
    public static class ModernToastNotifier
    {
        private static SynchronizationContext _uiContext;

        public static void Inicializar()
        {
            _uiContext = SynchronizationContext.Current;
        }

        public static void MostrarLoteRecibido(
            string remitente,
            string area,
            string ipRemitente,
            IList<string> archivosRecibidos,
            string carpetaDestino)
        {
            int count = archivosRecibidos != null ? archivosRecibidos.Count : 0;
            string quien = !string.IsNullOrWhiteSpace(remitente)
                ? (string.IsNullOrWhiteSpace(area) ? remitente : $"{remitente} ({area})")
                : (!string.IsNullOrWhiteSpace(ipRemitente) ? $"Equipo {ipRemitente}" : "Red local");

            string titulo = count == 1
                ? $"Archivo recibido de {quien}"
                : $"{count} archivos recibidos de {quien}";

            string detalle = count == 1 && archivosRecibidos != null && archivosRecibidos.Count > 0
                ? Path.GetFileName(archivosRecibidos[0])
                : "Haz clic para abrir la carpeta de recibidos.";

            string abrirRuta = (count == 1 && archivosRecibidos != null && archivosRecibidos.Count > 0)
                ? archivosRecibidos[0]
                : carpetaDestino;

            MostrarNotificacion(titulo, detalle, abrirRuta, false);
        }

        public static void MostrarNotificacion(string titulo, string mensaje, string rutaAbrir = null, bool esError = false)
        {
            void ShowOnThread()
            {
                try
                {
                    var toast = new ModernBannerForm(titulo, mensaje, rutaAbrir, esError);
                    toast.Show();
                }
                catch
                {
                }
            }

            if (_uiContext != null)
            {
                _uiContext.Post(_ => ShowOnThread(), null);
                return;
            }

            var openForm = Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null;
            if (openForm != null && !openForm.IsDisposed && openForm.IsHandleCreated)
            {
                openForm.BeginInvoke(new Action(ShowOnThread));
                return;
            }

            var thread = new Thread(() =>
            {
                Application.EnableVisualStyles();
                var toast = new ModernBannerForm(titulo, mensaje, rutaAbrir, esError);
                toast.FormClosed += (s, e) => Application.ExitThread();
                toast.Show();
                Application.Run();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        private sealed class ModernBannerForm : Form
        {
            private readonly string _rutaAbrir;
            private readonly Color _accentColor;
            private readonly System.Windows.Forms.Timer _lifeTimer;
            private int _ticks;

            protected override bool ShowWithoutActivation => true;

            public ModernBannerForm(string titulo, string mensaje, string rutaAbrir, bool esError)
            {
                _rutaAbrir = rutaAbrir;
                _accentColor = esError
                    ? Color.FromArgb(239, 83, 108)
                    : Color.FromArgb(139, 92, 246);

                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                TopMost = true;
                Size = new Size(370, 92);
                BackColor = Color.FromArgb(22, 24, 38);
                Opacity = 0.0;
                Cursor = !string.IsNullOrWhiteSpace(rutaAbrir) ? Cursors.Hand : Cursors.Default;

                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(area.Right - Width - 18, area.Bottom - Height - 18);

                ApplyRoundedRegion(16);

                var picIcon = new PictureBox
                {
                    Location = new Point(18, 22),
                    Size = new Size(42, 42),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.Transparent
                };

                try
                {
                    string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "onkos.ico");
                    if (File.Exists(icoPath))
                        picIcon.Image = new Icon(icoPath, 48, 48).ToBitmap();
                    else
                        picIcon.Image = SystemIcons.Information.ToBitmap();
                }
                catch
                {
                }

                var lblTitle = new Label
                {
                    Text = titulo,
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(246, 247, 252),
                    Location = new Point(70, 14),
                    Size = new Size(262, 22),
                    AutoEllipsis = true
                };

                var lblMsg = new Label
                {
                    Text = mensaje,
                    Font = new Font("Segoe UI", 8.8F),
                    ForeColor = Color.FromArgb(182, 188, 212),
                    Location = new Point(70, 38),
                    Size = new Size(276, 38),
                    AutoEllipsis = true
                };

                var lblClose = new Label
                {
                    Text = "×",
                    Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(150, 155, 180),
                    Location = new Point(340, 8),
                    Size = new Size(20, 20),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Cursor = Cursors.Hand
                };
                lblClose.Click += (s, e) => Close();

                EventHandler openAction = (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(_rutaAbrir))
                    {
                        try
                        {
                            if (Directory.Exists(_rutaAbrir))
                                Process.Start("explorer.exe", _rutaAbrir);
                            else if (File.Exists(_rutaAbrir))
                                Process.Start("explorer.exe", "/select,\"" + _rutaAbrir + "\"");
                        }
                        catch
                        {
                        }
                    }
                    Close();
                };

                Click += openAction;
                picIcon.Click += openAction;
                lblTitle.Click += openAction;
                lblMsg.Click += openAction;

                Controls.AddRange(new Control[] { picIcon, lblTitle, lblMsg, lblClose });

                _lifeTimer = new System.Windows.Forms.Timer { Interval = 30 };
                _lifeTimer.Tick += (s, e) =>
                {
                    _ticks++;
                    if (_ticks <= 10)
                    {
                        Opacity = Math.Min(0.97, _ticks * 0.10);
                    }
                    else if (_ticks > 155)
                    {
                        Opacity = Math.Max(0.0, Opacity - 0.08);
                        if (Opacity <= 0.02)
                        {
                            _lifeTimer.Stop();
                            Close();
                        }
                    }
                };
                _lifeTimer.Start();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                using (var accentBrush = new SolidBrush(_accentColor))
                {
                    e.Graphics.FillRectangle(accentBrush, 0, 0, 6, Height);
                }

                using (var pen = new Pen(Color.FromArgb(55, 60, 88), 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                }
            }

            private void ApplyRoundedRegion(int radius)
            {
                var path = new GraphicsPath();
                int d = radius * 2;
                path.AddArc(0, 0, d, d, 180, 90);
                path.AddArc(Width - d, 0, d, d, 270, 90);
                path.AddArc(Width - d, Height - d, d, d, 0, 90);
                path.AddArc(0, Height - d, d, d, 90, 90);
                path.CloseFigure();
                Region = new Region(path);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _lifeTimer?.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
