using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ONKOS.Core.Logging;
using ONKOS.Core.Models;
using ONKOS.Core.Storage;

namespace ONKOS.UI
{
    public sealed class MainForm : Form
    {
        private const int TransferPort = 5050;
        private PeerInfo _localProfile;
        private bool _isSending;

        private readonly Label _lblIdentity;
        private readonly Label _lblInboxPath;
        private readonly Label _lblStatus;
        private readonly TextBox _txtRecipientIp;
        private readonly ListBox _lstFiles;
        private readonly Button _btnAddFiles;
        private readonly Button _btnRemoveFile;
        private readonly Button _btnSend;
        private readonly Button _btnInbox;
        private readonly Button _btnHistory;
        private readonly Button _btnSettings;
        private readonly Button _btnProfile;

        public MainForm(PeerInfo profile)
        {
            _localProfile = profile;

            Text = "ONKOS File Transfer 2.0";
            Size = new Size(760, 480);
            MinimumSize = new Size(680, 420);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(14, 15, 24);
            ForeColor = Color.FromArgb(246, 247, 252);
            Font = new Font("Segoe UI", 9.5F);
            AllowDrop = true;

            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "onkos.ico");
                if (File.Exists(icoPath))
                    Icon = new Icon(icoPath);
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("UI.MainForm", "No se pudo cargar icono principal.", ex);
            }

            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = Color.FromArgb(20, 22, 34),
                Padding = new Padding(16, 10, 16, 10)
            };

            var lblTitle = new Label
            {
                Text = "ONKOS File Transfer 2.0",
                Font = new Font("Segoe UI", 12.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(184, 148, 255),
                Location = new Point(16, 8),
                AutoSize = true
            };

            _lblIdentity = new Label
            {
                Text = BuildIdentityText(),
                Font = new Font("Segoe UI", 8.8F),
                ForeColor = Color.FromArgb(170, 176, 204),
                Location = new Point(17, 34),
                AutoSize = true
            };

            _btnProfile = new Button
            {
                Text = "Mi Perfil",
                Location = new Point(400, 16),
                Size = new Size(80, 30),
                BackColor = Color.FromArgb(38, 41, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnProfile.Click += BtnProfile_Click;

            _btnHistory = new Button
            {
                Text = "Historial",
                Location = new Point(486, 16),
                Size = new Size(84, 30),
                BackColor = Color.FromArgb(38, 41, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnHistory.Click += (s, e) =>
            {
                using (var dlg = new TransferHistoryForm())
                    dlg.ShowDialog(this);
                RefreshInboxBadgeText();
            };

            _btnSettings = new Button
            {
                Text = "Configuración",
                Location = new Point(576, 16),
                Size = new Size(92, 30),
                BackColor = Color.FromArgb(38, 41, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnSettings.Click += BtnSettings_Click;

            _btnInbox = new Button
            {
                Text = "Recibidos",
                Location = new Point(674, 16),
                Size = new Size(70, 30),
                BackColor = Color.FromArgb(38, 41, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnInbox.Click += (s, e) => OpenInboxFolder();

            pnlTop.Controls.AddRange(new Control[] { lblTitle, _lblIdentity, _btnProfile, _btnHistory, _btnSettings, _btnInbox });

            var lblTarget = new Label
            {
                Text = "IP destino en LAN:",
                Location = new Point(18, 82),
                Size = new Size(130, 22)
            };

            _txtRecipientIp = new TextBox
            {
                Location = new Point(150, 80),
                Size = new Size(200, 26),
                BackColor = Color.FromArgb(28, 31, 48),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            _btnAddFiles = new Button
            {
                Text = "+ Adjuntar",
                Location = new Point(530, 78),
                Size = new Size(100, 28),
                BackColor = Color.FromArgb(124, 77, 224),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnAddFiles.Click += BtnAddFiles_Click;

            _btnRemoveFile = new Button
            {
                Text = "Quitar",
                Location = new Point(638, 78),
                Size = new Size(96, 28),
                BackColor = Color.FromArgb(38, 41, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnRemoveFile.Click += BtnRemoveFile_Click;

            _lstFiles = new ListBox
            {
                Location = new Point(18, 116),
                Size = new Size(716, 210),
                SelectionMode = SelectionMode.MultiExtended,
                BackColor = Color.FromArgb(18, 20, 32),
                ForeColor = Color.FromArgb(242, 244, 252),
                BorderStyle = BorderStyle.FixedSingle
            };

            var pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 82,
                BackColor = Color.FromArgb(20, 22, 34),
                Padding = new Padding(16, 10, 16, 10)
            };

            _lblStatus = new Label
            {
                Text = "Listo. Solo el servicio de Windows recibe archivos y registra el historial.",
                Location = new Point(16, 10),
                Size = new Size(540, 22),
                ForeColor = Color.FromArgb(215, 220, 240)
            };

            _lblInboxPath = new Label
            {
                Text = "Carpeta recibidos: " + AppSettingsStore.GetInboxFolder(),
                Location = new Point(16, 36),
                Size = new Size(540, 22),
                ForeColor = Color.FromArgb(155, 162, 192)
            };

            _btnSend = new Button
            {
                Text = "Enviar",
                Location = new Point(580, 16),
                Size = new Size(150, 40),
                BackColor = Color.FromArgb(124, 77, 224),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnSend.Click += BtnSend_Click;

            pnlBottom.Controls.AddRange(new Control[] { _lblStatus, _lblInboxPath, _btnSend });

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            Load += (s, e) => RefreshInboxBadgeText();

            Controls.AddRange(new Control[] { pnlTop, lblTarget, _txtRecipientIp, _btnAddFiles, _btnRemoveFile, _lstFiles, pnlBottom });
        }

        public void BringToFrontFromSecondInstance()
        {
            if (IsDisposed)
                return;

            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;

            Show();
            BringToFront();
            Activate();
        }

        private string BuildIdentityText()
        {
            string name = _localProfile != null ? _localProfile.FullName : "Sin perfil";
            string area = _localProfile != null ? _localProfile.Area : "";
            return $"{name} ({area})  •  Equipo: {Environment.MachineName}";
        }

        private void RefreshInboxBadgeText()
        {
            int unseen = TransferHistoryStore.GetUnseenReceivedCount();
            _btnInbox.Text = unseen > 0 ? $"Recibidos ({unseen})" : "Recibidos";
            _lblInboxPath.Text = "Carpeta recibidos: " + AppSettingsStore.GetInboxFolder();
        }

        private void OpenInboxFolder()
        {
            try
            {
                string folder;
                string err;
                if (!AppSettingsStore.TryEnsureInboxReady(out folder, out err))
                {
                    _lblStatus.Text = err;
                    return;
                }

                TransferHistoryStore.MarkReceivedAsSeen();
                RefreshInboxBadgeText();
                Process.Start("explorer.exe", folder);
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("UI.MainForm", "Error al abrir carpeta de recibidos.", ex);
                _lblStatus.Text = "No se pudo abrir la carpeta de recibidos.";
            }
        }

        private void BtnSettings_Click(object sender, EventArgs e)
        {
            using (var dlg = new SettingsForm())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    RefreshInboxBadgeText();
                    _lblStatus.Text = "Carpeta de recibidos actualizada.";
                }
            }
        }

        private void BtnProfile_Click(object sender, EventArgs e)
        {
            using (var dlg = new WelcomeRegistrationForm(_localProfile))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.RegisteredProfile != null)
                {
                    _localProfile = dlg.RegisteredProfile;
                    _lblIdentity.Text = BuildIdentityText();
                    _lblStatus.Text = "Perfil actualizado.";
                }
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (dropped != null)
                    AddPaths(dropped);
            }
        }

        private void BtnAddFiles_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog { Multiselect = true, Title = "Seleccionar archivos" })
            {
                if (ofd.ShowDialog(this) == DialogResult.OK)
                    AddPaths(ofd.FileNames);
            }
        }

        private void AddPaths(IEnumerable<string> paths)
        {
            foreach (string p in paths)
            {
                if (string.IsNullOrWhiteSpace(p))
                    continue;
                if (!File.Exists(p) && !Directory.Exists(p))
                    continue;

                bool exists = _lstFiles.Items.Cast<string>().Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                    _lstFiles.Items.Add(p);
            }
        }

        private void BtnRemoveFile_Click(object sender, EventArgs e)
        {
            var selected = _lstFiles.SelectedItems.Cast<string>().ToList();
            foreach (string item in selected)
                _lstFiles.Items.Remove(item);
        }

        private async void BtnSend_Click(object sender, EventArgs e)
        {
            if (_isSending)
                return;

            string ipText = (_txtRecipientIp.Text ?? string.Empty).Trim();
            IPAddress targetIp;
            if (!IPAddress.TryParse(ipText, out targetIp) || targetIp.AddressFamily != AddressFamily.InterNetwork)
            {
                _lblStatus.Text = "Escribe una dirección IPv4 válida de destino.";
                return;
            }

            List<string> items = _lstFiles.Items.Cast<string>().ToList();
            if (items.Count == 0)
            {
                _lblStatus.Text = "Agrega al menos un archivo o carpeta para enviar.";
                return;
            }

            _isSending = true;
            _btnSend.Enabled = false;
            var tempFiles = new List<string>();

            try
            {
                foreach (string sourcePath in items)
                {
                    string payloadPath = sourcePath;
                    string displayName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.IsNullOrWhiteSpace(displayName))
                        displayName = sourcePath;

                    if (Directory.Exists(sourcePath))
                    {
                        string zipPath = Path.Combine(Path.GetTempPath(), $"{displayName}_{Guid.NewGuid():N}.onkosfolder");
                        ZipFile.CreateFromDirectory(sourcePath, zipPath, CompressionLevel.Fastest, false);
                        tempFiles.Add(zipPath);
                        payloadPath = zipPath;
                    }
                    else if (!File.Exists(sourcePath))
                    {
                        _lblStatus.Text = $"No existe el elemento: {sourcePath}";
                        continue;
                    }

                    long sizeBytes = new FileInfo(payloadPath).Length;
                    _lblStatus.Text = $"Enviando '{displayName}' a {targetIp}...";

                    await SendSingleFileAsync(targetIp.ToString(), TransferPort, payloadPath, displayName).ConfigureAwait(true);

                    TransferHistoryStore.AppendEntry(
                        "ENVIADO",
                        displayName,
                        sizeBytes,
                        targetIp.ToString(),
                        "Completado",
                        targetIp.ToString(),
                        string.Empty,
                        "Enviado desde MainForm");
                }

                _lstFiles.Items.Clear();
                _lblStatus.Text = "Envío completado.";
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Error("UI.Send", "Error durante el envío de archivos.", ex);
                _lblStatus.Text = "Error en el envío: " + ex.Message;
            }
            finally
            {
                foreach (string temp in tempFiles)
                {
                    try
                    {
                        if (File.Exists(temp))
                            File.Delete(temp);
                    }
                    catch (IOException ex)
                    {
                        FileLogger.Instance.Warn("UI.Send", "No se pudo borrar paquete temporal.", ex);
                    }
                }

                _isSending = false;
                _btnSend.Enabled = true;
            }
        }

        private static async Task SendSingleFileAsync(string host, int port, string filePath, string displayName)
        {
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(host, port).ConfigureAwait(false);
                client.NoDelay = true;

                var info = new FileInfo(filePath);
                using (NetworkStream stream = client.GetStream())
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                using (var input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                {
                    writer.Write(Path.GetFileName(filePath));
                    writer.Write(info.Length);
                    writer.Flush();

                    byte[] buffer = new byte[81920];
                    long sent = 0L;
                    while (sent < info.Length)
                    {
                        int read = await input.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                        if (read <= 0)
                            break;

                        await stream.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                        sent += read;
                    }

                    await stream.FlushAsync().ConfigureAwait(false);
                    byte[] ack = new byte[1];
                    int ackRead = await stream.ReadAsync(ack, 0, 1).ConfigureAwait(false);
                    if (ackRead != 1 || ack[0] != 1)
                        throw new IOException($"El destino no confirmó la recepción de '{displayName}'.");
                }
            }
        }
    }
}
