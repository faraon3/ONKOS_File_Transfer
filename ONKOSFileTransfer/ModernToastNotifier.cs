using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace ONKOSFileTransfer
{
    public static class ModernToastNotifier
    {
        private static SynchronizationContext _uiContext;
        private static readonly object _toastLock = new object();
        private static ModernBannerForm _activeToast;

        public static void Inicializar()
        {
            if (SynchronizationContext.Current != null)
                _uiContext = SynchronizationContext.Current;
        }

        public static void MostrarEnvioExitoso(string resumen, string rutaAbrir = null)
        {
            MostrarNotificacion(
                "Envío completado",
                string.IsNullOrWhiteSpace(resumen) ? "Los elementos se enviaron correctamente." : resumen,
                rutaAbrir,
                false,
                "ENVIADO");
        }

        public static void MostrarLoteRecibido(object lote)
        {
            string inboxFallback = GetSafeInboxPath();

            if (lote == null)
            {
                MostrarNotificacion(
                    "Archivos recibidos",
                    "Haz clic para abrir tu carpeta de recibidos.",
                    inboxFallback,
                    false,
                    "RECIBIDO");
                return;
            }

            if (lote is IEnumerable enumerable && !(lote is string))
            {
                var paths = new List<string>();
                foreach (var item in enumerable)
                {
                    if (item != null)
                        paths.Add(item.ToString());
                }
                MostrarLoteRecibido(null, null, null, paths, inboxFallback);
                return;
            }

            string remitente = GetStringProp(lote, "Remitente", "SenderName", "FullName", "Nombre");
            string area = GetStringProp(lote, "Area", "SenderArea", "Departamento");
            string ip = GetStringProp(lote, "Ip", "IP", "IPv4", "SenderIp", "RemoteIp");
            string carpeta = GetStringProp(lote, "Carpeta", "Folder", "DestinationFolder", "InboxFolder");
            if (string.IsNullOrWhiteSpace(carpeta))
                carpeta = inboxFallback;

            var archivos = new List<string>();
            object rawFiles = GetObjectProp(lote, "Archivos", "Files", "Items", "ReceivedFiles");
            if (rawFiles is IEnumerable rawEnum && !(rawFiles is string))
            {
                foreach (var f in rawEnum)
                {
                    if (f != null)
                        archivos.Add(f.ToString());
                }
            }

            MostrarLoteRecibido(remitente, area, ip, archivos, carpeta);
        }

        public static void MostrarLoteRecibido(
            string remitente,
            string ipRemitente,
            IList<string> archivosRecibidos,
            string carpetaDestino)
        {
            MostrarLoteRecibido(remitente, string.Empty, ipRemitente, archivosRecibidos, carpetaDestino);
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
                ? $"Recibido de {quien}"
                : (count > 1 ? $"{count} elementos de {quien}" : $"Nuevo envío de {quien}");

            string detalle = count == 1 && archivosRecibidos != null && archivosRecibidos.Count > 0
                ? Path.GetFileName(archivosRecibidos[0])
                : "Haz clic para abrir la carpeta de recibidos.";

            string abrirRuta = (count == 1 && archivosRecibidos != null && archivosRecibidos.Count > 0)
                ? archivosRecibidos[0]
                : (string.IsNullOrWhiteSpace(carpetaDestino) ? GetSafeInboxPath() : carpetaDestino);

            MostrarNotificacion(titulo, detalle, abrirRuta, false, "RECIBIDO");
        }

        public static void MostrarNotificacion(
            string titulo,
            string mensaje,
            string rutaAbrir = null,
            bool esError = false,
            string badgeText = null)
        {
            string resolvedBadge = !string.IsNullOrWhiteSpace(badgeText)
                ? badgeText.Trim().ToUpperInvariant()
                : (esError ? "AVISO" : "ONKOS");

            void ShowOnThread()
            {
                try
                {
                    lock (_toastLock)
                    {
                        if (_activeToast != null && !_activeToast.IsDisposed)
                        {
                            try { _activeToast.Close(); } catch { }
                            _activeToast = null;
                        }

                        var toast = new ModernBannerForm(titulo, mensaje, rutaAbrir, esError, resolvedBadge);
                        _activeToast = toast;
                        toast.FormClosed += (s, e) =>
                        {
                            lock (_toastLock)
                            {
                                if (ReferenceEquals(_activeToast, toast))
                                    _activeToast = null;
                            }
                        };
                        toast.Show();
                    }
                }
                catch
                {
                }
            }

            if (_uiContext != null)
            {
                try
                {
                    _uiContext.Post(_ => ShowOnThread(), null);
                    return;
                }
                catch
                {
                }
            }

            Form openForm = null;
            try
            {
                if (Application.OpenForms.Count > 0)
                    openForm = Application.OpenForms[0];
            }
            catch
            {
            }

            if (openForm != null && !openForm.IsDisposed && openForm.IsHandleCreated)
            {
                try
                {
                    openForm.BeginInvoke(new Action(ShowOnThread));
                    return;
                }
                catch
                {
                }
            }

            var staThread = new Thread(() =>
            {
                Application.EnableVisualStyles();
                var toast = new ModernBannerForm(titulo, mensaje, rutaAbrir, esError, resolvedBadge);
                toast.FormClosed += (s, e) => Application.ExitThread();
                toast.Show();
                Application.Run();
            });
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.IsBackground = true;
            staThread.Start();
        }

        private static string GetSafeInboxPath()
        {
            try
            {
                string cfg = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ONKOS",
                    "ruta_recibidos.txt");

                if (File.Exists(cfg))
                {
                    string saved = File.ReadAllText(cfg).Trim();
                    if (!string.IsNullOrWhiteSpace(saved))
                        return saved;
                }
            }
            catch
            {
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "Archivos Recibidos ONKOS");
        }

        private static object GetObjectProp(object obj, params string[] names)
        {
            if (obj == null) return null;
            Type t = obj.GetType();
            foreach (string name in names)
            {
                PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (p != null)
                    return p.GetValue(obj, null);
                FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (f != null)
                    return f.GetValue(obj);
            }
            return null;
        }

        private static string GetStringProp(object obj, params string[] names)
        {
            object val = GetObjectProp(obj, names);
            return val != null ? val.ToString() : string.Empty;
        }

        private sealed class ModernBannerForm : Form
        {
            private readonly string _rutaAbrir;
            private readonly string _badgeText;
            private readonly Color _accentStart;
            private readonly Color _accentEnd;
            private readonly System.Windows.Forms.Timer _lifeTimer;
            private int _ticks;
            private int _targetY;

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    const int WS_EX_TOOLWINDOW = 0x00000080;
                    const int WS_EX_NOACTIVATE = 0x08000000;
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                    return cp;
                }
            }

            public ModernBannerForm(string titulo, string mensaje, string rutaAbrir, bool esError, string badgeText)
            {
                _rutaAbrir = rutaAbrir;
                _badgeText = badgeText;

                if (esError)
                {
                    _accentStart = Color.FromArgb(244, 63, 94);
                    _accentEnd = Color.FromArgb(190, 24, 93);
                }
                else if (string.Equals(badgeText, "RECIBIDO", StringComparison.OrdinalIgnoreCase))
                {
                    _accentStart = Color.FromArgb(45, 212, 191);
                    _accentEnd = Color.FromArgb(124, 77, 224);
                }
                else
                {
                    _accentStart = Color.FromArgb(139, 92, 246);
                    _accentEnd = Color.FromArgb(99, 102, 241);
                }

                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                TopMost = true;
                Size = new Size(388, 102);
                BackColor = Color.FromArgb(20, 22, 36);
                Opacity = 0.0;
                Cursor = !string.IsNullOrWhiteSpace(rutaAbrir) ? Cursors.Hand : Cursors.Default;

                Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
                int targetX = workArea.Right - Width - 18;
                _targetY = workArea.Bottom - Height - 18;
                Location = new Point(targetX, _targetY + 14);

                ApplyRoundedRegion(18);

                var picBadge = new PictureBox
                {
                    Location = new Point(20, 24),
                    Size = new Size(44, 44),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.FromArgb(30, 33, 52)
                };

                try
                {
                    string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "onkos.ico");
                    if (File.Exists(icoPath))
                        picBadge.Image = new Icon(icoPath, 48, 48).ToBitmap();
                    else
                        picBadge.Image = SystemIcons.Information.ToBitmap();
                }
                catch
                {
                }

                var lblBadge = new Label
                {
                    Text = _badgeText,
                    Font = new Font("Segoe UI", 7.2F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(205, 195, 255),
                    BackColor = Color.FromArgb(38, 34, 62),
                    Location = new Point(76, 12),
                    Size = new Size(76, 17),
                    TextAlign = ContentAlignment.MiddleCenter
                };

                var lblTitle = new Label
                {
                    Text = titulo,
                    Font = new Font("Segoe UI", 9.8F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(248, 250, 255),
                    BackColor = Color.FromArgb(20, 22, 36),
                    Location = new Point(76, 32),
                    Size = new Size(276, 22),
                    AutoEllipsis = true
                };

                var lblMsg = new Label
                {
                    Text = mensaje,
                    Font = new Font("Segoe UI", 8.6F),
                    ForeColor = Color.FromArgb(176, 182, 208),
                    BackColor = Color.FromArgb(20, 22, 36),
                    Location = new Point(76, 55),
                    Size = new Size(278, 34),
                    AutoEllipsis = true
                };

                var btnClose = new Label
                {
                    Text = "×",
                    Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(155, 162, 190),
                    BackColor = Color.FromArgb(20, 22, 36),
                    Location = new Point(356, 10),
                    Size = new Size(20, 20),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Cursor = Cursors.Hand
                };
                btnClose.Click += (s, e) => Close();

                EventHandler onClickOpen = (s, e) =>
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

                Click += onClickOpen;
                picBadge.Click += onClickOpen;
                lblBadge.Click += onClickOpen;
                lblTitle.Click += onClickOpen;
                lblMsg.Click += onClickOpen;

                Controls.AddRange(new Control[] { picBadge, lblBadge, lblTitle, lblMsg, btnClose });

                _lifeTimer = new System.Windows.Forms.Timer { Interval = 25 };
                _lifeTimer.Tick += (s, e) =>
                {
                    _ticks++;
                    if (_ticks <= 8)
                    {
                        Opacity = Math.Min(0.98, _ticks * 0.125);
                        int stepOffset = (int)Math.Round((8 - _ticks) * 1.75);
                        Location = new Point(Location.X, _targetY + stepOffset);
                    }
                    else if (_ticks > 190)
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
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle fullRect = new Rectangle(0, 0, Width, Height);

                using (var bgGrad = new LinearGradientBrush(
                    fullRect,
                    Color.FromArgb(25, 28, 45),
                    Color.FromArgb(17, 19, 31),
                    LinearGradientMode.Vertical))
                {
                    e.Graphics.FillRectangle(bgGrad, fullRect);
                }

                Rectangle accentRect = new Rectangle(0, 0, 7, Height);
                using (var barGrad = new LinearGradientBrush(accentRect, _accentStart, _accentEnd, LinearGradientMode.Vertical))
                {
                    e.Graphics.FillRectangle(barGrad, accentRect);
                }

                Rectangle borderRect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath borderPath = CreateRoundRect(borderRect, 18))
                using (var borderPen = new Pen(Color.FromArgb(68, 74, 110), 1.2f))
                {
                    e.Graphics.DrawPath(borderPen, borderPath);
                }
            }

            private void ApplyRoundedRegion(int radius)
            {
                using (GraphicsPath path = CreateRoundRect(new Rectangle(0, 0, Width, Height), radius))
                {
                    Region = new Region(path);
                }
            }

            private static GraphicsPath CreateRoundRect(Rectangle r, int radius)
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

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _lifeTimer?.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
