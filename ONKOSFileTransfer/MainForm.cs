using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ONKOSFileTransfer.Network;

namespace ONKOSFileTransfer
{
    public class MainForm : Form
    {
        private const int TransferPort = 5050;

        private readonly PeerDiscoveryService _discovery;
        private readonly List<PeerInfo> _selectedRecipients = new List<PeerInfo>();
        private readonly List<string> _queuedItems = new List<string>();

        private PeerInfo _localProfile;
        private bool _isSending;

        private Label _lblIdentity;
        private TextBox _txtSearch;
        private ListBox _lstPeers;
        private Button _btnAddRecipient;
        private FlowLayoutPanel _flpChips;
        private Button _btnClearRecipients;

        private ListBox _lstFiles;
        private Button _btnAddFiles;
        private Button _btnAddFolder;
        private Button _btnRemoveFile;
        private Button _btnClearFiles;

        private ProgressBar _progressBar;
        private Label _lblStatus;
        private Button _btnSend;
        private Button _btnOpenInbox;
        private Button _btnEditProfile;

        public MainForm(PeerInfo localProfile)
        {
            _localProfile = localProfile;
            _discovery = new PeerDiscoveryService();

            InitializeComponent();
            WireEvents();
        }

        private void InitializeComponent()
        {
            Text = "ONKOS File Transfer 2.0";
            Size = new Size(880, 620);
            MinimumSize = new Size(780, 560);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(15, 16, 26);
            ForeColor = Color.FromArgb(246, 247, 252);
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);

            try
            {
                if (File.Exists("onkos.ico"))
                    Icon = new Icon("onkos.ico");
            }
            catch
            {
            }

            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = Color.FromArgb(24, 26, 40),
                Padding = new Padding(18, 10, 18, 10)
            };

            var lblBrand = new Label
            {
                Text = "ONKOS FILE TRANSFER 2.0",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(184, 148, 255),
                Location = new Point(18, 10),
                AutoSize = true
            };

            _lblIdentity = new Label
            {
                Text = BuildIdentityText(),
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(175, 180, 205),
                Location = new Point(18, 35),
                AutoSize = true
            };

            _btnEditProfile = CreateStyledButton("Mi Perfil", new Point(620, 15), new Size(105, 32), Color.FromArgb(42, 45, 68));
            _btnOpenInbox = CreateStyledButton("Recibidos", new Point(735, 15), new Size(115, 32), Color.FromArgb(42, 45, 68));
            _btnOpenInbox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnEditProfile.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            pnlHeader.Controls.AddRange(new Control[] { lblBrand, _lblIdentity, _btnEditProfile, _btnOpenInbox });

            var pnlBody = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18, 14, 18, 12)
            };

            // Columna izquierda: Destinatarios
            var pnlLeft = new Panel
            {
                Location = new Point(18, 14),
                Size = new Size(395, 415),
                BackColor = Color.FromArgb(24, 26, 40),
                Padding = new Padding(12)
            };

            var lblPeersTitle = new Label
            {
                Text = "1. Destinatarios en red o vínculo manual (Nombre = IP)",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(235, 237, 248),
                Location = new Point(12, 10),
                Size = new Size(370, 22)
            };

            _txtSearch = new TextBox
            {
                Location = new Point(12, 36),
                Size = new Size(322, 28),
                BackColor = Color.FromArgb(34, 37, 56),
                ForeColor = Color.FromArgb(246, 247, 252),
                BorderStyle = BorderStyle.FixedSingle
            };

            _btnAddRecipient = CreateStyledButton("+", new Point(342, 36), new Size(40, 28), Color.FromArgb(124, 77, 224));
            _btnAddRecipient.Font = new Font("Segoe UI", 12F, FontStyle.Bold);

            _lstPeers = new ListBox
            {
                Location = new Point(12, 72),
                Size = new Size(370, 185),
                BackColor = Color.FromArgb(20, 22, 34),
                ForeColor = Color.FromArgb(246, 247, 252),
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false
            };

            var lblSelectedHeader = new Label
            {
                Text = "Destinatarios seleccionados:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(184, 148, 255),
                Location = new Point(12, 264),
                Size = new Size(240, 20)
            };

            _btnClearRecipients = CreateStyledButton("Limpiar", new Point(312, 261), new Size(70, 24), Color.FromArgb(42, 45, 68));
            _btnClearRecipients.Font = new Font("Segoe UI", 8.5F);

            _flpChips = new FlowLayoutPanel
            {
                Location = new Point(12, 289),
                Size = new Size(370, 114),
                BackColor = Color.FromArgb(20, 22, 34),
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle
            };

            pnlLeft.Controls.AddRange(new Control[]
            {
                lblPeersTitle, _txtSearch, _btnAddRecipient, _lstPeers,
                lblSelectedHeader, _btnClearRecipients, _flpChips
            });

            // Columna derecha: Archivos y carpetas
            var pnlRight = new Panel
            {
                Location = new Point(427, 14),
                Size = new Size(417, 415),
                BackColor = Color.FromArgb(24, 26, 40),
                Padding = new Padding(12)
            };

            var lblFilesTitle = new Label
            {
                Text = "2. Archivos y carpetas a enviar",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(235, 237, 248),
                Location = new Point(12, 10),
                Size = new Size(380, 22)
            };

            _btnAddFiles = CreateStyledButton("+ Archivos", new Point(12, 36), new Size(115, 30), Color.FromArgb(124, 77, 224));
            _btnAddFolder = CreateStyledButton("+ Carpeta", new Point(135, 36), new Size(115, 30), Color.FromArgb(95, 58, 185));
            _btnRemoveFile = CreateStyledButton("Quitar", new Point(258, 36), new Size(64, 30), Color.FromArgb(42, 45, 68));
            _btnClearFiles = CreateStyledButton("Vaciar", new Point(328, 36), new Size(76, 30), Color.FromArgb(42, 45, 68));

            _lstFiles = new ListBox
            {
                Location = new Point(12, 74),
                Size = new Size(392, 329),
                BackColor = Color.FromArgb(20, 22, 34),
                ForeColor = Color.FromArgb(246, 247, 252),
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false
            };

            pnlRight.Controls.AddRange(new Control[]
            {
                lblFilesTitle, _btnAddFiles, _btnAddFolder, _btnRemoveFile, _btnClearFiles, _lstFiles
            });

            // Pie: Progreso, estado y botón enviar
            var pnlFooter = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 84,
                BackColor = Color.FromArgb(24, 26, 40),
                Padding = new Padding(18, 10, 18, 10)
            };

            _progressBar = new ProgressBar
            {
                Location = new Point(18, 12),
                Size = new Size(645, 18),
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Style = ProgressBarStyle.Continuous
            };

            _lblStatus = new Label
            {
                Text = "Listo. Selecciona uno o varios destinatarios y agrega archivos o carpetas.",
                ForeColor = Color.FromArgb(185, 190, 215),
                Location = new Point(18, 36),
                Size = new Size(645, 28)
            };

            _btnSend = CreateStyledButton("Enviar (0)", new Point(678, 12), new Size(166, 50), Color.FromArgb(124, 77, 224));
            _btnSend.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);

            pnlFooter.Controls.AddRange(new Control[] { _progressBar, _lblStatus, _btnSend });

            pnlBody.Controls.AddRange(new Control[] { pnlLeft, pnlRight });
            Controls.AddRange(new Control[] { pnlBody, pnlFooter, pnlHeader });

            UpdateSendButtonText();
        }

        private static Button CreateStyledButton(string text, Point location, Size size, Color bgColor)
        {
            var btn = new Button
            {
                Text = text,
                Location = location,
                Size = size,
                BackColor = bgColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private string BuildIdentityText()
        {
            string name = _localProfile != null ? _localProfile.FullName : "Sin perfil";
            string area = _localProfile != null ? _localProfile.Area : "";
            string ip = NetworkManager.GetLocalIPv4();
            return $"{name} ({area})  •  IP local: {ip}  •  Puerto TCP: {TransferPort}";
        }

        private void WireEvents()
        {
            Load += MainForm_Load;
            FormClosing += MainForm_FormClosing;

            _discovery.PeersUpdated += OnPeersUpdatedSafe;
            _txtSearch.TextChanged += (s, e) => RefreshPeersList();
            _txtSearch.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    AddRecipientFromInputOrSelection();
                }
            };

            _btnAddRecipient.Click += (s, e) => AddRecipientFromInputOrSelection();
            _lstPeers.DoubleClick += (s, e) => AddRecipientFromInputOrSelection();
            _btnClearRecipients.Click += (s, e) =>
            {
                _selectedRecipients.Clear();
                RefreshRecipientChips();
                UpdateSendButtonText();
            };

            _btnAddFiles.Click += BtnAddFiles_Click;
            _btnAddFolder.Click += BtnAddFolder_Click;
            _btnRemoveFile.Click += BtnRemoveFile_Click;
            _btnClearFiles.Click += (s, e) =>
            {
                _queuedItems.Clear();
                RefreshFilesList();
                UpdateSendButtonText();
            };

            _btnEditProfile.Click += BtnEditProfile_Click;
            _btnOpenInbox.Click += (s, e) =>
            {
                Directory.CreateDirectory(InboxMonitor.CarpetaRecibidos);
                Process.Start("explorer.exe", InboxMonitor.CarpetaRecibidos);
            };

            _btnSend.Click += BtnSend_Click;
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            _discovery.Start(_localProfile);
            RefreshPeersList();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _discovery.Dispose();
        }

        private void OnPeersUpdatedSafe()
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(new Action(RefreshPeersList));
            }
            catch
            {
            }
        }

        private void RefreshPeersList()
        {
            string filter = _txtSearch.Text;
            List<PeerInfo> peers = _discovery.GetActivePeers(filter);

            _lstPeers.BeginUpdate();
            _lstPeers.Items.Clear();
            foreach (PeerInfo p in peers)
                _lstPeers.Items.Add(p);
            _lstPeers.EndUpdate();
        }

        private async void AddRecipientFromInputOrSelection()
        {
            string raw = (_txtSearch.Text ?? string.Empty).Trim();

            // Soporta formato manual "Nombre = IP" o "Nombre - IP"
            if (raw.Contains("="))
            {
                string[] parts = raw.Split(new[] { '=' }, 2);
                string manualName = parts[0].Trim();
                string manualIp = parts[1].Trim();

                if (IPAddress.TryParse(manualIp, out IPAddress parsed) && parsed.AddressFamily == AddressFamily.InterNetwork)
                {
                    var identity = await FileSender.QueryPeerIdentityAsync(parsed.ToString(), TransferPort, 500);
                    string finalName = !string.IsNullOrWhiteSpace(identity.FullName) && !UserProfileStore.IsIgnoredSystemName(identity.FullName)
                        ? identity.FullName
                        : manualName;
                    string finalArea = !string.IsNullOrWhiteSpace(identity.Area) ? identity.Area : "Vinculado";

                    PeerInfo registered = _discovery.RegisterManualContact(finalName, finalArea, parsed.ToString());
                    if (registered != null)
                    {
                        AddRecipientToSelection(registered);
                        _txtSearch.Clear();
                        SetStatus($"Vinculado y agregado: {registered.DisplayText} ({registered.IPv4}).");
                        return;
                    }
                }

                SetStatus("Formato inválido. Usa por ejemplo: Juan Pérez = 192.168.1.45");
                return;
            }

            if (_lstPeers.SelectedItem is PeerInfo selectedPeer)
            {
                AddRecipientToSelection(selectedPeer);
                _txtSearch.Clear();
                return;
            }

            if (!string.IsNullOrWhiteSpace(raw))
            {
                PeerInfo found = _discovery.FindPeerByNameOrIp(raw);
                if (found != null)
                {
                    AddRecipientToSelection(found);
                    _txtSearch.Clear();
                    return;
                }

                if (IPAddress.TryParse(raw, out IPAddress ipOnly) && ipOnly.AddressFamily == AddressFamily.InterNetwork)
                {
                    var id = await FileSender.QueryPeerIdentityAsync(ipOnly.ToString(), TransferPort, 500);
                    string pName = !string.IsNullOrWhiteSpace(id.FullName) && !UserProfileStore.IsIgnoredSystemName(id.FullName)
                        ? id.FullName
                        : "Equipo " + ipOnly;
                    string pArea = !string.IsNullOrWhiteSpace(id.Area) ? id.Area : "Red local";

                    PeerInfo reg = _discovery.RegisterManualContact(pName, pArea, ipOnly.ToString());
                    if (reg != null)
                    {
                        AddRecipientToSelection(reg);
                        _txtSearch.Clear();
                        return;
                    }
                }

                SetStatus($"No se encontró '{raw}' en la red. Puedes escribir 'Nombre = IP' para vincularlo.");
            }
        }

        private void AddRecipientToSelection(PeerInfo peer)
        {
            if (peer == null || string.IsNullOrWhiteSpace(peer.IPv4))
                return;

            bool alreadyAdded = _selectedRecipients.Any(r =>
                string.Equals(r.IPv4, peer.IPv4, StringComparison.OrdinalIgnoreCase));

            if (!alreadyAdded)
            {
                _selectedRecipients.Add(new PeerInfo
                {
                    FullName = peer.FullName,
                    Area = peer.Area,
                    IPv4 = peer.IPv4,
                    MachineName = peer.MachineName,
                    LastSeenUtc = DateTime.UtcNow
                });
                RefreshRecipientChips();
                UpdateSendButtonText();
            }
        }

        private void RefreshRecipientChips()
        {
            _flpChips.SuspendLayout();
            _flpChips.Controls.Clear();

            foreach (PeerInfo recipient in _selectedRecipients)
            {
                var chip = new Panel
                {
                    Size = new Size(340, 28),
                    BackColor = Color.FromArgb(46, 36, 78),
                    Margin = new Padding(3)
                };

                var lbl = new Label
                {
                    Text = $"{recipient.DisplayText} ({recipient.IPv4})",
                    ForeColor = Color.FromArgb(246, 247, 252),
                    Location = new Point(8, 5),
                    Size = new Size(300, 18),
                    Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
                };

                var btnRemove = new Button
                {
                    Text = "×",
                    Location = new Point(312, 3),
                    Size = new Size(22, 22),
                    BackColor = Color.FromArgb(95, 58, 185),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand,
                    Tag = recipient.IPv4
                };
                btnRemove.FlatAppearance.BorderSize = 0;
                btnRemove.Click += (s, e) =>
                {
                    string ipToRemove = (string)((Button)s).Tag;
                    _selectedRecipients.RemoveAll(x => string.Equals(x.IPv4, ipToRemove, StringComparison.OrdinalIgnoreCase));
                    RefreshRecipientChips();
                    UpdateSendButtonText();
                };

                chip.Controls.AddRange(new Control[] { lbl, btnRemove });
                _flpChips.Controls.Add(chip);
            }

            _flpChips.ResumeLayout();
        }

        private void BtnAddFiles_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Multiselect = true;
                ofd.Title = "Seleccionar archivos para enviar";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    foreach (string path in ofd.FileNames)
                    {
                        if (!_queuedItems.Contains(path, StringComparer.OrdinalIgnoreCase))
                            _queuedItems.Add(path);
                    }
                    RefreshFilesList();
                    UpdateSendButtonText();
                }
            }
        }

        private void BtnAddFolder_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Selecciona la carpeta que deseas enviar";
                if (fbd.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    string folderPath = fbd.SelectedPath;
                    if (!_queuedItems.Contains(folderPath, StringComparer.OrdinalIgnoreCase))
                        _queuedItems.Add(folderPath);
                    RefreshFilesList();
                    UpdateSendButtonText();
                }
            }
        }

        private void BtnRemoveFile_Click(object sender, EventArgs e)
        {
            int idx = _lstFiles.SelectedIndex;
            if (idx >= 0 && idx < _queuedItems.Count)
            {
                _queuedItems.RemoveAt(idx);
                RefreshFilesList();
                UpdateSendButtonText();
            }
        }

        private void RefreshFilesList()
        {
            _lstFiles.BeginUpdate();
            _lstFiles.Items.Clear();
            foreach (string item in _queuedItems)
            {
                if (Directory.Exists(item))
                    _lstFiles.Items.Add("[CARPETA] " + Path.GetFileName(item) + "  (" + item + ")");
                else
                    _lstFiles.Items.Add("[ARCHIVO] " + Path.GetFileName(item) + "  (" + item + ")");
            }
            _lstFiles.EndUpdate();
        }

        private void BtnEditProfile_Click(object sender, EventArgs e)
        {
            using (var dlg = new WelcomeRegistrationForm(_localProfile))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.RegisteredProfile != null)
                {
                    _localProfile = dlg.RegisteredProfile;
                    _lblIdentity.Text = BuildIdentityText();
                    _discovery.UpdateLocalProfile(_localProfile);
                    SetStatus($"Perfil actualizado a '{_localProfile.DisplayText}'.");
                }
            }
        }

        private void UpdateSendButtonText()
        {
            int count = _selectedRecipients.Count;
            _btnSend.Text = count == 1
                ? "Enviar a 1 persona"
                : $"Enviar a {count} personas";
            _btnSend.Enabled = !_isSending && _selectedRecipients.Count > 0 && _queuedItems.Count > 0;
        }

        private void SetStatus(string text)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(SetStatus), text);
                return;
            }

            _lblStatus.Text = text;
        }

        private void SetProgress(int percentage)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action<int>(SetProgress), percentage);
                return;
            }

            _progressBar.Value = Math.Max(0, Math.Min(100, percentage));
        }

        private async void BtnSend_Click(object sender, EventArgs e)
        {
            if (_isSending || _selectedRecipients.Count == 0 || _queuedItems.Count == 0)
                return;

            _isSending = true;
            UpdateSendButtonText();
            SetProgress(0);

            List<PeerInfo> targets = _selectedRecipients.ToList();
            List<string> items = _queuedItems.ToList();
            List<string> tempPackages = new List<string>();

            int totalOperations = targets.Count * items.Count;
            int completedOperations = 0;
            int successCount = 0;
            int failCount = 0;

            try
            {
                // Empaquetar carpetas en .onkosfolder una sola vez antes de enviar
                List<string> preparedPayloads = new List<string>();
                foreach (string item in items)
                {
                    if (Directory.Exists(item))
                    {
                        string folderName = Path.GetFileName(item.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        if (string.IsNullOrWhiteSpace(folderName))
                            folderName = "Carpeta";

                        string pkgPath = Path.Combine(
                            Path.GetTempPath(),
                            $"{folderName}_{Guid.NewGuid():N}.onkosfolder");

                        if (File.Exists(pkgPath))
                            File.Delete(pkgPath);

                        ZipFile.CreateFromDirectory(item, pkgPath, CompressionLevel.Fastest, false);
                        tempPackages.Add(pkgPath);
                        preparedPayloads.Add(pkgPath);
                    }
                    else if (File.Exists(item))
                    {
                        preparedPayloads.Add(item);
                    }
                }

                totalOperations = Math.Max(1, targets.Count * preparedPayloads.Count);

                foreach (PeerInfo target in targets)
                {
                    foreach (string payload in preparedPayloads)
                    {
                        string displayName = Path.GetFileName(payload);
                        SetStatus($"Enviando '{displayName}' a {target.DisplayText} ({target.IPv4})...");

                        try
                        {
                            await FileSender.SendFileAsync(
                                target.IPv4,
                                TransferPort,
                                payload,
                                (sent, total) =>
                                {
                                    double itemFraction = total > 0 ? (double)sent / total : 1.0;
                                    double overall = ((completedOperations + itemFraction) / totalOperations) * 100.0;
                                    SetProgress((int)Math.Round(overall));
                                });

                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            SetStatus($"Error enviando '{displayName}' a {target.FullName}: {ex.Message}");
                        }

                        completedOperations++;
                        SetProgress((int)Math.Round((double)completedOperations / totalOperations * 100.0));
                    }
                }

                if (failCount == 0)
                {
                    SetStatus($"Envío completado correctamente ({successCount} envío(s) exitosos).");
                }
                else
                {
                    SetStatus($"Finalizado con novedades: {successCount} exitosos, {failCount} con error.");
                }
            }
            finally
            {
                foreach (string temp in tempPackages)
                {
                    try
                    {
                        if (File.Exists(temp))
                            File.Delete(temp);
                    }
                    catch
                    {
                    }
                }

                _isSending = false;
                UpdateSendButtonText();
            }
        }
    }
}
