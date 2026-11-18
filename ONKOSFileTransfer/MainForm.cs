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
            Text = "ONKOS File Transfer";
            Size = new Size(760, 680);
            MinimumSize = new Size(700, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(24, 25, 38);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5F);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            Label title = new Label
            {
                Text = "ONKOS FILE TRANSFER",
                Font = new Font("Segoe UI", 21, FontStyle.Bold),
                ForeColor = Color.FromArgb(177, 139, 255),
                Location = new Point(28, 20),
                Size = new Size(500, 38)
            };
            Controls.Add(title);

            Label subtitle = new Label
            {
                Text = "Envía archivos directamente entre equipos de tu red",
                ForeColor = Color.FromArgb(175, 178, 195),
                Location = new Point(31, 61),
                Size = new Size(650, 24)
            };
            Controls.Add(subtitle);

            Label localIp = new Label
            {
                Text = "TU IP: " + NetworkManager.GetLocalIPv4() + "     •     RECEPCIÓN AUTOMÁTICA ACTIVA",
                ForeColor = Color.LightGreen,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Location = new Point(31, 101),
                Size = new Size(680, 25)
            };
            Controls.Add(localIp);

            Label ipLabel = MakeLabel("IP DEL EQUIPO DESTINO", 31, 145);
            Controls.Add(ipLabel);

            txtIP = new TextBox
            {
                Location = new Point(31, 172),
                Size = new Size(680, 34),
                Font = new Font("Segoe UI", 11),
                BackColor = Color.FromArgb(39, 40, 57),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(txtIP);

            Label folderLabel = MakeLabel("CARPETA DONDE SE GUARDAN LOS ARCHIVOS RECIBIDOS", 31, 220);
            Controls.Add(folderLabel);

            txtFolder = new TextBox
            {
                Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ONKOS_Received"),
                Location = new Point(31, 247),
                Size = new Size(550, 32),
                BackColor = Color.FromArgb(39, 40, 57),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(txtFolder);

            Button browse = MakeButton("CAMBIAR", 592, 246, 119, 34, Color.FromArgb(66, 68, 91));
            browse.Click += BrowseFolder;
            Controls.Add(browse);

            Label filesLabel = MakeLabel("ARCHIVOS PARA ENVIAR", 31, 300);
            Controls.Add(filesLabel);

            listFiles = new ListView
            {
                Location = new Point(31, 328),
                Size = new Size(680, 155),
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                MultiSelect = false,
                BackColor = Color.FromArgb(32, 33, 49),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            listFiles.Columns.Add("Archivo", 390);
            listFiles.Columns.Add("Tamaño", 120);
            listFiles.Columns.Add("Estado", 145);
            Controls.Add(listFiles);

            Button add = MakeButton("AGREGAR ARCHIVOS", 31, 496, 215, 39, Color.FromArgb(66, 68, 91));
            add.Click += AddFiles;
            Controls.Add(add);

            Button remove = MakeButton("QUITAR SELECCIONADO", 258, 496, 215, 39, Color.FromArgb(66, 68, 91));
            remove.Click += RemoveSelected;
            Controls.Add(remove);

            btnSend = MakeButton("ENVIAR ARCHIVOS", 486, 496, 225, 39, Color.FromArgb(112, 70, 194));
            btnSend.Click += SendFiles;
            Controls.Add(btnSend);

            Label progressTitle = MakeLabel("PROGRESO DE TRANSFERENCIA", 31, 552);
            Controls.Add(progressTitle);

            lblProgress = new Label
            {
                Text = "0%",
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(640, 550),
                Size = new Size(70, 24)
            };
            Controls.Add(lblProgress);

            progressBar = new ProgressBar
            {
                Location = new Point(31, 579),
                Size = new Size(680, 13),
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Continuous
            };
            Controls.Add(progressBar);

            lblStatus = new Label
            {
                Text = "Listo. Este equipo ya puede recibir archivos.",
                ForeColor = Color.FromArgb(155, 205, 255),
                Location = new Point(31, 606),
                Size = new Size(680, 28)
            };
            Controls.Add(lblStatus);
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
