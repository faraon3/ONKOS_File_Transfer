using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;
using ONKOSFileTransfer.Network;

namespace ONKOSFileTransfer
{
    public class MainForm : Form
    {
        private const int Port = 5050;

        private static readonly Color BgWindow = Color.FromArgb(24, 25, 38);
        private static readonly Color BgInput = Color.FromArgb(39, 40, 57);
        private static readonly Color BgList = Color.FromArgb(32, 33, 49);
        private static readonly Color BtnSecondary = Color.FromArgb(66, 68, 91);
        private static readonly Color BtnPrimary = Color.FromArgb(112, 70, 194);
        private static readonly Color AccentPurple = Color.FromArgb(177, 139, 255);
        private static readonly Color TextMuted = Color.FromArgb(175, 178, 195);
        private static readonly Color TextLabel = Color.FromArgb(200, 202, 218);
        private static readonly Color TextInfo = Color.FromArgb(155, 205, 255);

        private readonly FileReceiver receiver;
        private TextBox txtIP;
        private TextBox txtFolder;
        private Label lblStatus;
        private Label lblReceiverInfo;
        private Label lblProgress;
        private ProgressBar progressBar;
        private ListView listFiles;
        private Button btnAdd;
        private Button btnRemove;
        private Button btnSend;
        private string activeFolder;
        private bool isSending;

        public MainForm()
        {
            activeFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ONKOS_Received");

            receiver = new FileReceiver();
            receiver.FileReceived += path =>
                UpdateReceiverStatus("Último archivo recibido: " + Path.GetFileName(path));
            receiver.TransferProgress += (name, received, size) =>
                UpdateReceiverStatus("Recibiendo " + name + " (" + FormatBytes(received) + " / " + FormatBytes(size) + ")");
            receiver.Error += message =>
                UpdateReceiverStatus("Error de recepción: " + message);

            BuildInterface();
            StartReceiver(activeFolder);
        }

        private void BuildInterface()
        {
            Text = "ONKOS File Transfer";
            ClientSize = new Size(744, 642);
            MinimumSize = new Size(744, 642);
            MaximumSize = new Size(744, 642);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgWindow;
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.5F);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            Controls.Add(new Label
            {
                Text = "ONKOS FILE TRANSFER",
                Font = new Font("Segoe UI", 21, FontStyle.Bold),
                ForeColor = AccentPurple,
                Location = new Point(28, 18),
                Size = new Size(460, 38)
            });

            Controls.Add(new Label
            {
                Text = "Envía y recibe archivos directamente entre equipos de tu red local",
                ForeColor = TextMuted,
                Location = new Point(30, 58),
                Size = new Size(680, 22)
            });

            lblReceiverInfo = new Label
            {
                Text = "TU IP: " + NetworkManager.GetLocalIPv4() + "  •  PUERTO " + Port + "  •  RECEPCIÓN ACTIVA",
                ForeColor = Color.LightGreen,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Location = new Point(30, 88),
                Size = new Size(684, 22)
            };
            Controls.Add(lblReceiverInfo);

            Controls.Add(MakeSectionLabel("IP DEL EQUIPO DESTINO", 30, 124));
            txtIP = new TextBox
            {
                Location = new Point(30, 148),
                Size = new Size(684, 30),
                Font = new Font("Segoe UI", 10.5F),
                BackColor = BgInput,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(txtIP);

            Controls.Add(MakeSectionLabel("CARPETA DE ARCHIVOS RECIBIDOS", 30, 192));
            txtFolder = new TextBox
            {
                Text = activeFolder,
                Location = new Point(30, 216),
                Size = new Size(556, 30),
                BackColor = BgInput,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(txtFolder);

            Button btnBrowse = MakeButton("CAMBIAR", 596, 215, 118, 32, BtnSecondary);
            btnBrowse.Click += BrowseFolder;
            Controls.Add(btnBrowse);

            Controls.Add(MakeSectionLabel("COLA DE ARCHIVOS PARA ENVIAR", 30, 262));
            listFiles = new ListView
            {
                Location = new Point(30, 286),
                Size = new Size(684, 156),
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                MultiSelect = false,
                BackColor = BgList,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            listFiles.Columns.Add("Archivo", 400);
            listFiles.Columns.Add("Tamaño", 120);
            listFiles.Columns.Add("Estado", 140);
            Controls.Add(listFiles);

            btnAdd = MakeButton("AGREGAR ARCHIVOS", 30, 456, 218, 38, BtnSecondary);
            btnAdd.Click += AddFiles;
            Controls.Add(btnAdd);

            btnRemove = MakeButton("QUITAR SELECCIONADO", 260, 456, 218, 38, BtnSecondary);
            btnRemove.Click += RemoveSelected;
            Controls.Add(btnRemove);

            btnSend = MakeButton("ENVIAR ARCHIVOS", 490, 456, 224, 38, BtnPrimary);
            btnSend.Click += SendFiles;
            Controls.Add(btnSend);

            Controls.Add(MakeSectionLabel("PROGRESO DE ENVÍO", 30, 510));
            lblProgress = new Label
            {
                Text = "0%",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(644, 510),
                Size = new Size(70, 22)
            };
            Controls.Add(lblProgress);

            progressBar = new ProgressBar
            {
                Location = new Point(30, 536),
                Size = new Size(684, 14),
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Continuous
            };
            Controls.Add(progressBar);

            lblStatus = new Label
            {
                Text = "Listo para enviar archivos.",
                ForeColor = TextInfo,
                Location = new Point(30, 562),
                Size = new Size(684, 24)
            };
            Controls.Add(lblStatus);

            Label lblFooter = new Label
            {
                Text = "Recepción automática activa en segundo plano.",
                ForeColor = TextMuted,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(30, 594),
                Size = new Size(684, 24)
            };
            Controls.Add(lblFooter);
        }

        private static Label MakeSectionLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextLabel,
                Location = new Point(x, y),
                Size = new Size(600, 20)
            };
        }

        private static Button MakeButton(string text, int x, int y, int width, int height, Color color)
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

        private void StartReceiver(string targetFolder)
        {
            try
            {
                receiver.Start(Port, targetFolder);
                activeFolder = targetFolder;
                txtFolder.Text = targetFolder;
                UpdateReceiverStatus("Recepción activa en: " + targetFolder);
            }
            catch (Exception ex)
            {
                UpdateReceiverStatus("Recepción inactiva: " + ex.Message);
                MessageBox.Show(
                    "No se pudo iniciar la recepción automática. Verifica que el puerto " + Port +
                    " esté disponible y que tengas permisos sobre la carpeta.\r\n\r\n" + ex.Message,
                    "ONKOS File Transfer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void BrowseFolder(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = Directory.Exists(txtFolder.Text)
                    ? txtFolder.Text
                    : activeFolder;

                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                receiver.Stop();
                StartReceiver(dialog.SelectedPath);
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
                    if (IsFileAlreadyQueued(path))
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

        private bool IsFileAlreadyQueued(string path)
        {
            foreach (ListViewItem existing in listFiles.Items)
            {
                if (string.Equals(existing.Tag as string, path, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void RemoveSelected(object sender, EventArgs e)
        {
            if (listFiles.SelectedItems.Count > 0 && !isSending)
                listFiles.Items.Remove(listFiles.SelectedItems[0]);
        }

        private async void SendFiles(object sender, EventArgs e)
        {
            if (!IPAddress.TryParse(txtIP.Text.Trim(), out IPAddress address) ||
                address.AddressFamily != AddressFamily.InterNetwork)
            {
                MessageBox.Show(
                    "Escribe una dirección IPv4 válida (por ejemplo, 172.16.13.110).",
                    "ONKOS File Transfer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                txtIP.Focus();
                return;
            }

            if (listFiles.Items.Count == 0)
            {
                MessageBox.Show(
                    "Agrega al menos un archivo para enviar.",
                    "ONKOS File Transfer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            List<ListViewItem> items = new List<ListViewItem>();
            long totalBytes = 0;

            foreach (ListViewItem item in listFiles.Items)
            {
                items.Add(item);
                string path = item.Tag as string;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    try { totalBytes += new FileInfo(path).Length; }
                    catch { }
                }
            }

            long totalSent = 0;
            int sentCount = 0;
            List<string> failures = new List<string>();

            SetSendingState(true);
            progressBar.Value = 0;
            lblProgress.Text = "0%";

            foreach (ListViewItem item in items)
            {
                string path = item.Tag as string;
                string name = Path.GetFileName(path);

                try
                {
                    if (string.IsNullOrEmpty(path) || !File.Exists(path))
                        throw new FileNotFoundException("El archivo ya no existe.");

                    item.SubItems[2].Text = "Conectando...";
                    UpdateSendStatus("Conectando con " + address + " para enviar " + name + "...");

                    long fileSize = new FileInfo(path).Length;
                    long fileSent = 0;

                    await FileSender.SendFileAsync(
                        address.ToString(),
                        Port,
                        path,
                        (sent, size) =>
                        {
                            long delta = sent - fileSent;
                            fileSent = sent;
                            totalSent += delta;
                            UpdateProgress(totalSent, totalBytes, name, sent, size);
                        });

                    sentCount++;
                    item.SubItems[2].Text = "Enviado";
                    UpdateSendStatus("Enviado: " + name);
                }
                catch (Exception ex)
                {
                    item.SubItems[2].Text = "Error";
                    failures.Add(name + ": " + ex.Message);
                }
            }

            SetSendingState(false);

            int finalPercent = totalBytes <= 0
                ? 100
                : (int)Math.Min(100, totalSent * 100.0 / totalBytes);
            progressBar.Value = failures.Count == 0 ? 100 : finalPercent;
            lblProgress.Text = progressBar.Value + "%";

            string summary = "Transferencia finalizada.\r\n\r\nEnviados correctamente: " +
                sentCount + " de " + items.Count + ".";
            if (failures.Count > 0)
                summary += "\r\n\r\nErrores:\r\n" + string.Join("\r\n", failures.ToArray());

            UpdateSendStatus("Finalizado: " + sentCount + " de " + items.Count + " enviados.");
            MessageBox.Show(
                summary,
                "Resumen de transferencia",
                MessageBoxButtons.OK,
                failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void SetSendingState(bool sending)
        {
            isSending = sending;
            btnSend.Enabled = !sending;
            btnAdd.Enabled = !sending;
            btnRemove.Enabled = !sending;
            txtIP.Enabled = !sending;
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
            lblStatus.Text = "Enviando " + name + " (" + FormatBytes(fileSent) + " / " + FormatBytes(fileSize) + ")";
        }

        private void UpdateSendStatus(string message)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateSendStatus), message);
                return;
            }

            lblStatus.Text = message;
        }

        private void UpdateReceiverStatus(string message)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateReceiverStatus), message);
                return;
            }

            lblReceiverInfo.Text =
                "TU IP: " + NetworkManager.GetLocalIPv4() + "  •  PUERTO " + Port + "  •  " + message;
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
