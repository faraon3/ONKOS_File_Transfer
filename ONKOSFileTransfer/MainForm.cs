using System;
using System.Collections.Generic;
using System.Drawing;
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

        private void BuildInterface()
        {
            Text = "ONKOS File Transfer 2.0";
            Size = new Size(940, 790);
            MinimumSize = new Size(900, 740);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(17, 19, 31);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5F);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;

            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 112,
                BackColor = Color.FromArgb(27, 29, 46),
                Padding = new Padding(28, 18, 28, 12)
            };
            Controls.Add(header);

            Label brand = new Label
            {
                Text = "ONKOS",
                Font = new Font("Segoe UI", 23, FontStyle.Bold),
                ForeColor = Color.FromArgb(183, 148, 255),
                Location = new Point(26, 13),
                AutoSize = true
            };
            header.Controls.Add(brand);

            Label product = new Label
            {
                Text = "FILE TRANSFER  2.0",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(31, 57),
                AutoSize = true
            };
            header.Controls.Add(product);

            Label tagline = new Label
            {
                Text = "Transferencia directa entre equipos de la red",
                ForeColor = Color.FromArgb(174, 178, 199),
                Font = new Font("Segoe UI", 9),
                Location = new Point(250, 43),
                AutoSize = true
            };
            header.Controls.Add(tagline);

            Panel content = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(26, 20, 26, 22),
                BackColor = BackColor
            };
            Controls.Add(content);

            Label section = new Label
            {
                Text = "Centro de transferencias",
                Font = new Font("Segoe UI", 17, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(28, 20),
                AutoSize = true
            };
            content.Controls.Add(section);

            Label subtitle = new Label
            {
                Text = "Envía archivos a otro computador y recibe transferencias en esta estación.",
                ForeColor = Color.FromArgb(165, 170, 192),
                Location = new Point(30, 55),
                AutoSize = true
            };
            content.Controls.Add(subtitle);

            Panel statusCard = new Panel
            {
                Location = new Point(28, 88),
                Size = new Size(840, 68),
                BackColor = Color.FromArgb(35, 39, 58)
            };
            content.Controls.Add(statusCard);

            Label localIp = new Label
            {
                Text = "ESTE EQUIPO   •   IP " + NetworkManager.GetLocalIPv4(),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(18, 12),
                AutoSize = true
            };
            statusCard.Controls.Add(localIp);

            Label receiveState = new Label
            {
                Text = "●  RECEPCIÓN AUTOMÁTICA",
                ForeColor = Color.FromArgb(133, 225, 169),
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Location = new Point(18, 38),
                AutoSize = true
            };
            statusCard.Controls.Add(receiveState);

            Label destinationLabel = MakeLabel("DESTINO", 30, 176);
            content.Controls.Add(destinationLabel);

            txtIP = new TextBox
            {
                Location = new Point(30, 202),
                Size = new Size(390, 34),
                Font = new Font("Segoe UI", 11),
                BackColor = Color.FromArgb(35, 39, 58),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(txtIP);
            content.Controls.Add(txtIP);

            Label destinationHint = new Label
            {
                Text = "Escribe la dirección IPv4 del computador que recibirá los archivos.",
                ForeColor = Color.FromArgb(145, 151, 174),
                Location = new Point(30, 240),
                AutoSize = true
            };
            content.Controls.Add(destinationHint);

            Label folderLabel = MakeLabel("CARPETA DE RECEPCIÓN", 30, 278);
            content.Controls.Add(folderLabel);

            txtFolder = new TextBox
            {
                Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ONKOS_Received"),
                Location = new Point(30, 304),
                Size = new Size(690, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(35, 39, 58),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            content.Controls.Add(txtFolder);

            Button browse = MakeButton("EXAMINAR", 735, 303, 133, 36, Color.FromArgb(62, 66, 91));
            browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            browse.Click += BrowseFolder;
            content.Controls.Add(browse);

            Label filesLabel = MakeLabel("ARCHIVOS SELECCIONADOS", 30, 359);
            content.Controls.Add(filesLabel);

            listFiles = new ListView
            {
                Location = new Point(30, 386),
                Size = new Size(838, 190),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                MultiSelect = false,
                BackColor = Color.FromArgb(27, 30, 47),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            listFiles.Columns.Add("Archivo", 490);
            listFiles.Columns.Add("Tamaño", 150);
            listFiles.Columns.Add("Estado", 170);
            content.Controls.Add(listFiles);

            Button add = MakeButton("+  AGREGAR ARCHIVOS", 30, 592, 205, 40, Color.FromArgb(62, 66, 91));
            add.Click += AddFiles;
            content.Controls.Add(add);

            Button remove = MakeButton("QUITAR SELECCIONADO", 247, 592, 205, 40, Color.FromArgb(62, 66, 91));
            remove.Click += RemoveSelected;
            content.Controls.Add(remove);

            btnSend = MakeButton("ENVIAR ARCHIVOS", 464, 592, 404, 40, Color.FromArgb(112, 70, 194));
            btnSend.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnSend.Click += SendFiles;
            content.Controls.Add(btnSend);

            Label progressTitle = MakeLabel("PROGRESO", 30, 654);
            content.Controls.Add(progressTitle);

            lblProgress = new Label
            {
                Text = "0%",
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(798, 650),
                Size = new Size(70, 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            content.Controls.Add(lblProgress);

            progressBar = new ProgressBar
            {
                Location = new Point(30, 681),
                Size = new Size(838, 14),
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
                Location = new Point(30, 710),
                Size = new Size(838, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            content.Controls.Add(lblStatus);
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
}
