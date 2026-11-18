using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;
using ONKOSFileTransfer.Network;

namespace ONKOSFileTransfer
{
    public class MainForm : Form
    {
        private const int Port = 5050;
        private readonly FileReceiver receiver;
        private TextBox txtIP;
        private TextBox txtFolder;
        private Label lblStatus;
        private Label lblProgress;
        private ProgressBar progressBar;
        private ListView listFiles;
        private Button btnSend;
        private long totalBytes;
        private long totalSent;
        private bool isSending;

        public MainForm()
        {
            receiver = new FileReceiver();
            receiver.FileReceived += path => UpdateStatus("Recibido: " + Path.GetFileName(path));
            receiver.TransferProgress += (name, received, size) =>
                UpdateStatus("Recibiendo " + name + " (" + FormatBytes(received) + " / " + FormatBytes(size) + ")");
            receiver.Error += message => UpdateStatus("Error al recibir: " + message);

            BuildInterface();
            StartReceiver();
        }

        private Panel pageHost;
        private Button activeNavButton;

        private void BuildInterface()
        {
            Text = "ONKOS File Transfer 2.0";
            Size = new Size(1180, 820);
            MinimumSize = new Size(1050, 720);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(17, 19, 31);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9F);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;

            Panel root = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(17, 19, 31)
            };
            Controls.Add(root);

            Panel sidebar = new Panel
            {
                Location = new Point(0, 0),
                Width = 228,
                BackColor = Color.FromArgb(23, 25, 40),
                Padding = new Padding(14, 18, 14, 14)
            };
            Label brand = new Label
            {
                Text = "◈  ONKOS",
                Font = new Font("Segoe UI", 23, FontStyle.Bold),
                ForeColor = Color.FromArgb(183, 148, 255),
                Location = new Point(20, 20),
                AutoSize = true
            };
            sidebar.Controls.Add(brand);

            Label product = new Label
            {
                Text = "FILE TRANSFER 2.0",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(205, 207, 222),
                Location = new Point(23, 59),
                AutoSize = true
            };
            sidebar.Controls.Add(product);

            Label menuTitle = new Label
            {
                Text = "MENÚ PRINCIPAL",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 126, 151),
                Location = new Point(20, 112),
                AutoSize = true
            };
            sidebar.Controls.Add(menuTitle);

            AddNavButton(sidebar, "⇄   Transferencias", "transfer", 145);
            AddNavButton(sidebar, "▤   Historial", "history", 195);
            AddNavButton(sidebar, "▣   Equipos autorizados", "devices", 245);
            AddNavButton(sidebar, "▦   Grupos por área", "groups", 295);
            AddNavButton(sidebar, "◉   Notificaciones", "notifications", 345);

            Panel bottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 92,
                BackColor = Color.Transparent
            };
            sidebar.Controls.Add(bottom);

            Label local = new Label
            {
                Text = "RED LOCAL",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 126, 151),
                Location = new Point(8, 8),
                AutoSize = true
            };
            bottom.Controls.Add(local);

            Label ip = new Label
            {
                Text = NetworkManager.GetLocalIPv4(),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(133, 225, 169),
                Location = new Point(8, 30),
                AutoSize = true
            };
            bottom.Controls.Add(ip);

            Button settings = MakeButton("⚙   Configuración", 8, 54, 185, 30, Color.FromArgb(43, 46, 66));
            settings.TextAlign = ContentAlignment.MiddleLeft;
            settings.Click += delegate { ShowPage("settings", settings); };
            bottom.Controls.Add(settings);

            pageHost = new Panel
            {
                Location = new Point(228, 0),
                BackColor = Color.FromArgb(17, 19, 31),
                Padding = new Padding(0)
            };

            root.Controls.Add(pageHost);
            root.Controls.Add(sidebar);

            Action layout = delegate
            {
                sidebar.Bounds = new Rectangle(0, 0, 228, root.ClientSize.Height);
                pageHost.Bounds = new Rectangle(228, 0,
                    Math.Max(0, root.ClientSize.Width - 228), root.ClientSize.Height);
            };
            root.Resize += delegate { layout(); };
            layout();

            ShowPage("transfer", null);
        }

        private void AddNavButton(Panel sidebar, string text, string page, int top)
        {
            Button button = MakeButton(text, 8, top, 198, 42, Color.FromArgb(31, 34, 52));
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(12, 0, 0, 0);
            button.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            button.Click += delegate { ShowPage(page, button); };
            sidebar.Controls.Add(button);
        }

        private void ShowPage(string page, Button navButton)
        {
            if (pageHost == null) return;
            pageHost.SuspendLayout();
            pageHost.Controls.Clear();

            if (activeNavButton != null)
                activeNavButton.BackColor = Color.FromArgb(31, 34, 52);
            if (navButton != null)
            {
                activeNavButton = navButton;
                activeNavButton.BackColor = Color.FromArgb(91, 57, 157);
            }

            if (page == "transfer")
                BuildTransferPage();
            else
                BuildInformationPage(page);

            pageHost.ResumeLayout(true);
        }

        private void BuildTransferPage()
        {
            Panel content = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(30, 25, 30, 24),
                BackColor = Color.FromArgb(17, 19, 31)
            };
            pageHost.Controls.Add(content);

            Label title = new Label
            {
                Text = "Centro de transferencias",
                Font = new Font("Segoe UI", 20, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(28, 20),
                AutoSize = true
            };
            content.Controls.Add(title);

            Label subtitle = new Label
            {
                Text = "Envía archivos a otros computadores y recibe transferencias en esta estación.",
                ForeColor = Color.FromArgb(165, 170, 192),
                Location = new Point(30, 59),
                AutoSize = true
            };
            content.Controls.Add(subtitle);

            RoundedPanel statusCard = new RoundedPanel(18)
            {
                Location = new Point(28, 94),
                Size = new Size(850, 76),
                BackColor = Color.FromArgb(35, 39, 58),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            content.Controls.Add(statusCard);

            Label localIp = new Label
            {
                Text = "ESTE EQUIPO     IP " + NetworkManager.GetLocalIPv4(),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(18, 12),
                AutoSize = true
            };
            statusCard.Controls.Add(localIp);

            Label receiveState = new Label
            {
                Text = "●  RECEPCIÓN AUTOMÁTICA ACTIVA",
                ForeColor = Color.FromArgb(133, 225, 169),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Location = new Point(18, 42),
                AutoSize = true
            };
            statusCard.Controls.Add(receiveState);

            content.Controls.Add(MakeLabel("DIRECCIÓN IP DEL EQUIPO DESTINO", 30, 190));
            txtIP = new TextBox
            {
                Location = new Point(30, 218),
                Size = new Size(520, 34),
                Font = new Font("Segoe UI", 11),
                BackColor = Color.FromArgb(35, 39, 58),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            content.Controls.Add(txtIP);

            content.Controls.Add(MakeLabel("CARPETA DONDE SE GUARDAN LOS ARCHIVOS RECIBIDOS", 30, 274));
            txtFolder = new TextBox
            {
                Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ONKOS_Received"),
                Location = new Point(30, 302),
                Size = new Size(690, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(35, 39, 58),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            content.Controls.Add(txtFolder);

            Button browse = MakeButton("CAMBIAR", 735, 301, 143, 36, Color.FromArgb(62, 66, 91));
            browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            browse.Click += BrowseFolder;
            content.Controls.Add(browse);

            content.Controls.Add(MakeLabel("ARCHIVOS PARA ENVIAR", 30, 356));
            listFiles = new ListView
            {
                Location = new Point(30, 384),
                Size = new Size(848, 205),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                MultiSelect = true,
                BackColor = Color.FromArgb(27, 30, 47),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            listFiles.Columns.Add("Archivo", 470);
            listFiles.Columns.Add("Tamaño", 150);
            listFiles.Columns.Add("Estado", 190);
            content.Controls.Add(listFiles);

            Button add = MakeButton("+  AGREGAR ARCHIVOS", 30, 605, 205, 42, Color.FromArgb(62, 66, 91));
            add.Click += AddFiles;
            content.Controls.Add(add);

            Button remove = MakeButton("QUITAR SELECCIONADOS", 247, 605, 205, 42, Color.FromArgb(62, 66, 91));
            remove.Click += RemoveSelected;
            content.Controls.Add(remove);

            btnSend = MakeButton("ENVIAR ARCHIVOS", 464, 605, 414, 42, Color.FromArgb(112, 70, 194));
            btnSend.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnSend.Click += SendFiles;
            content.Controls.Add(btnSend);

            content.Controls.Add(MakeLabel("PROGRESO DE TRANSFERENCIA", 30, 670));
            lblProgress = new Label
            {
                Text = "0%",
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(808, 666),
                Size = new Size(70, 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            content.Controls.Add(lblProgress);

            progressBar = new ProgressBar
            {
                Location = new Point(30, 697),
                Size = new Size(848, 14),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Continuous
            };
            content.Controls.Add(progressBar);

            lblStatus = new Label
            {
                Text = "Listo. Este equipo puede recibir archivos.",
                ForeColor = Color.FromArgb(155, 205, 255),
                Location = new Point(30, 724),
                Size = new Size(848, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            content.Controls.Add(lblStatus);

            // Ajusta todos los elementos al ancho real del área de trabajo.
            Action adjustLayout = delegate
            {
                int right = Math.Max(420, content.ClientSize.Width - 30);
                int available = Math.Max(360, right - 30);

                statusCard.Width = available;
                browse.Left = right - browse.Width;
                txtFolder.Width = Math.Max(180, browse.Left - txtFolder.Left - 14);
                listFiles.Width = available;

                int gap = 12;
                int buttonWidth = Math.Max(110, (available - gap * 2) / 3);
                add.Width = buttonWidth;
                remove.Left = add.Right + gap;
                remove.Width = buttonWidth;
                btnSend.Left = remove.Right + gap;
                btnSend.Width = Math.Max(110, right - btnSend.Left);

                lblProgress.Left = right - lblProgress.Width;
                progressBar.Width = available;
                lblStatus.Width = available;
            };
            content.Resize += delegate { adjustLayout(); };
            adjustLayout();
        }

        private void BuildInformationPage(string page)
        {
            string title = "Sección";
            string description = "Esta sección forma parte de la nueva interfaz de ONKOS File Transfer 2.0.";
            string[] features = new string[0];

            switch (page)
            {
                case "history":
                    title = "Historial de transferencias";
                    description = "Consulta los archivos enviados y recibidos desde este computador.";
                    features = new string[] { "Registro local de envíos y recepciones", "Fecha, hora, equipo y usuario", "Resultado de cada archivo", "Búsqueda y filtros por estado" };
                    break;
                case "devices":
                    title = "Equipos autorizados";
                    description = "Administra qué computadores pueden conectarse con esta estación.";
                    features = new string[] { "Registrar un equipo por nombre e IP", "Aprobar o rechazar solicitudes", "Consultar equipos autorizados", "Revocar el acceso de un equipo" };
                    break;
                case "groups":
                    title = "Grupos por área";
                    description = "Organiza los computadores de la clínica para facilitar los envíos múltiples.";
                    features = new string[] { "Crear grupos por área", "Agregar o quitar equipos", "Seleccionar varios destinatarios", "Enviar a un grupo completo" };
                    break;
                case "notifications":
                    title = "Centro de notificaciones";
                    description = "Revisa los avisos de actividad, transferencias y seguridad.";
                    features = new string[] { "Transferencias enviadas y recibidas", "Errores y fallos de conexión", "Eventos de autorización y seguridad", "Avisos visuales, sin sonido" };
                    break;
                case "settings":
                    title = "Configuración";
                    description = "Opciones locales de funcionamiento de ONKOS File Transfer.";
                    features = new string[] { "Carpeta predeterminada de recepción", "Información de red de este equipo", "Preferencias de notificación visual", "Estado del servicio de recepción" };
                    break;
            }

            Panel content = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(34, 30, 34, 24),
                BackColor = Color.FromArgb(17, 19, 31)
            };
            pageHost.Controls.Add(content);

            Label heading = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 21, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(30, 24),
                AutoSize = true
            };
            content.Controls.Add(heading);

            Label sub = new Label
            {
                Text = description,
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(165, 170, 192),
                Location = new Point(32, 68),
                AutoSize = true
            };
            content.Controls.Add(sub);

            RoundedPanel card = new RoundedPanel(20)
            {
                Location = new Point(30, 125),
                Size = new Size(790, 330),
                BackColor = Color.FromArgb(27, 30, 47),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            content.Controls.Add(card);
            Action adjustCard = delegate
            {
                card.Width = Math.Max(360, content.ClientSize.Width - 60);
            };
            content.Resize += delegate { adjustCard(); };
            adjustCard();

            Label status = new Label
            {
                Text = "EN PREPARACIÓN",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(183, 148, 255),
                Location = new Point(25, 24),
                AutoSize = true
            };
            card.Controls.Add(status);

            Label note = new Label
            {
                Text = "La navegación visual ya está preparada. Las funciones de este módulo se implementarán y probarán en las siguientes etapas.",
                ForeColor = Color.FromArgb(190, 194, 211),
                Location = new Point(25, 58),
                Size = new Size(720, 48)
            };
            card.Controls.Add(note);

            Label listTitle = new Label
            {
                Text = "LO QUE INCLUIRÁ ESTA SECCIÓN",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(130, 136, 160),
                Location = new Point(25, 125),
                AutoSize = true
            };
            card.Controls.Add(listTitle);

            int y = 160;
            foreach (string feature in features)
            {
                Label item = new Label
                {
                    Text = "✦    " + feature,
                    Font = new Font("Segoe UI", 10.5F),
                    ForeColor = Color.FromArgb(225, 226, 236),
                    Location = new Point(28, y),
                    AutoSize = true
                };
                card.Controls.Add(item);
                y += 36;
            }
        }

        private Label MakeLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 202, 218),
                Location = new Point(x, y),
                Size = new Size(650, 23)
            };
        }

        private Button MakeButton(string text, int x, int y, int width, int height, Color color)
        {
            Button button = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                BackColor = color,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(color, 0.12F);
            button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(color, 0.08F);
            button.Region = RoundedGeometry.Create(new Rectangle(0, 0, width, height), 10);
            button.Resize += delegate { button.Region = RoundedGeometry.Create(new Rectangle(0, 0, button.Width, button.Height), 10); };
            return button;
        }

        private void StartReceiver()
        {
            try
            {
                receiver.Start(Port, txtFolder.Text);
                UpdateStatus("Recepción automática activa. Puerto " + Port + ".");
            }
            catch (Exception ex)
            {
                UpdateStatus("No se pudo activar la recepción: " + ex.Message);
                MessageBox.Show(
                    "No se pudo iniciar la recepción automática. Comprueba que el puerto 5050 esté libre y que tengas permisos para la carpeta.\r\n\r\n" + ex.Message,
                    "ONKOS File Transfer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void BrowseFolder(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = Directory.Exists(txtFolder.Text) ? txtFolder.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                txtFolder.Text = dialog.SelectedPath;
                receiver.Stop();
                StartReceiver();
            }
        }

        private void AddFiles(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Selecciona los archivos que deseas enviar";
                dialog.Multiselect = true;
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                foreach (string path in dialog.FileNames)
                {
                    bool exists = false;
                    foreach (ListViewItem existing in listFiles.Items)
                        if (string.Equals(existing.Tag as string, path, StringComparison.OrdinalIgnoreCase))
                            exists = true;

                    if (exists)
                        continue;

                    FileInfo info = new FileInfo(path);
                    ListViewItem item = new ListViewItem(info.Name);
                    item.SubItems.Add(FormatBytes(info.Length));
                    item.SubItems.Add("Pendiente");
                    item.Tag = path;
                    listFiles.Items.Add(item);
                }
            }
        }

        private void RemoveSelected(object sender, EventArgs e)
        {
            if (listFiles.SelectedItems.Count > 0 && !isSending)
                listFiles.Items.Remove(listFiles.SelectedItems[0]);
        }

        private async void SendFiles(object sender, EventArgs e)
        {
            IPAddress address;
            if (!IPAddress.TryParse(txtIP.Text.Trim(), out address) ||
                address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                MessageBox.Show("Escribe una dirección IPv4 válida.", "ONKOS File Transfer",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtIP.Focus();
                return;
            }

            if (listFiles.Items.Count == 0)
            {
                MessageBox.Show("Agrega al menos un archivo para enviar.", "ONKOS File Transfer",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<ListViewItem> items = new List<ListViewItem>();
            foreach (ListViewItem item in listFiles.Items)
                items.Add(item);

            totalBytes = 0;
            totalSent = 0;
            int sentCount = 0;
            List<string> failures = new List<string>();

            foreach (ListViewItem item in items)
            {
                try { totalBytes += new FileInfo(item.Tag as string).Length; }
                catch { }
            }

            isSending = true;
            btnSend.Enabled = false;
            progressBar.Value = 0;
            lblProgress.Text = "0%";

            foreach (ListViewItem item in items)
            {
                string path = item.Tag as string;
                string name = Path.GetFileName(path);
                try
                {
                    if (!File.Exists(path))
                        throw new FileNotFoundException("El archivo ya no existe.");

                    item.SubItems[2].Text = "Conectando...";
                    UpdateStatus("Enviando " + name + "...");
                    long fileSize = new FileInfo(path).Length;
                    long fileSent = 0;

                    await FileSender.SendFileAsync(address.ToString(), Port, path,
                        (sent, size) =>
                        {
                            long delta = sent - fileSent;
                            fileSent = sent;
                            totalSent += delta;
                            UpdateProgress(totalSent, totalBytes, name, sent, size);
                        });

                    sentCount++;
                    item.SubItems[2].Text = "Enviado";
                    UpdateStatus("Enviado: " + name);
                }
                catch (Exception ex)
                {
                    item.SubItems[2].Text = "Error";
                    failures.Add(name + ": " + ex.Message);
                }
            }

            isSending = false;
            btnSend.Enabled = true;
            progressBar.Value = 100;
            lblProgress.Text = "100%";

            string summary = "Transferencia finalizada.\r\n\r\nCorrectos: " + sentCount +
                " de " + items.Count + ".";
            if (failures.Count > 0)
                summary += "\r\n\r\nArchivos con error:\r\n" + string.Join("\r\n", failures.ToArray());

            UpdateStatus("Proceso terminado: " + sentCount + " de " + items.Count + " enviados.");
            MessageBox.Show(summary, "Resumen de transferencia",
                MessageBoxButtons.OK,
                failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void UpdateProgress(long sent, long total, string name, long fileSent, long fileSize)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action<long, long, string, long, long>(UpdateProgress),
                    sent, total, name, fileSent, fileSize);
                return;
            }

            int percent = total <= 0 ? 100 : (int)Math.Min(100, sent * 100.0 / total);
            progressBar.Value = Math.Max(0, Math.Min(100, percent));
            lblProgress.Text = percent + "%";
            lblStatus.Text = "Enviando " + name + " — " + FormatBytes(fileSent) + " / " + FormatBytes(fileSize);
        }

        private void UpdateStatus(string message)
        {
            if (IsDisposed || !IsHandleCreated)
                return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateStatus), message);
                return;
            }
            lblStatus.Text = message;
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return value.ToString("0.##") + " " + units[unit];
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            receiver.Stop();
            base.OnFormClosing(e);
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        private readonly int radius;
        public RoundedPanel(int cornerRadius)
        {
            radius = cornerRadius;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width > 0 && Height > 0)
                Region = RoundedGeometry.Create(new Rectangle(0, 0, Width, Height), radius);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedGeometry.Path(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), radius))
            using (Pen pen = new Pen(Color.FromArgb(75, 83, 112), 1F))
                e.Graphics.DrawPath(pen, path);
            base.OnPaint(e);
        }
    }

    internal static class RoundedGeometry
    {
        public static GraphicsPath Path(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = Math.Min(Math.Max(2, radius * 2), Math.Min(bounds.Width, bounds.Height));
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
        public static Region Create(Rectangle bounds, int radius)
        {
            using (GraphicsPath path = Path(bounds, radius))
                return new Region(path);
        }
    }

}
