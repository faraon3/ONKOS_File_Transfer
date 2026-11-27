using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ONKOSFileTransfer.Network;

namespace ONKOSFileTransfer
{
    public class MainForm : Form
    {
        private const int TransferPort = 5050;

        private enum QueueItemDeliveryState
        {
            Pending,
            Sending,
            Sent,
            Failed
        }

        private sealed class QueuedTransferItem
        {
            public string SourcePath { get; set; }
            public string DisplayName { get; set; }
            public bool IsDirectory { get; set; }
            public long SizeBytes { get; set; }
            public QueueItemDeliveryState State { get; set; } = QueueItemDeliveryState.Pending;
        }

        private sealed class PreparedPayload
        {
            public QueuedTransferItem SourceItem { get; set; }
            public string PayloadPath { get; set; }
            public string DisplayName { get; set; }
            public long Size { get; set; }
            public bool IsTempPackage { get; set; }
        }

        private PeerInfo _localProfile;
        private readonly PeerDiscoveryService _discovery;
        private readonly List<QueuedTransferItem> _queuedItems = new List<QueuedTransferItem>();
        private readonly List<PeerInfo> _allPeers = new List<PeerInfo>();
        private readonly List<PeerInfo> _selectedTargets = new List<PeerInfo>();

        private readonly Label _lblProfile;
        private readonly TextBox _txtSearchPeer;
        private readonly CheckedListBox _clbPeers;
        private readonly ListView _lvFiles;
        private readonly RoundedButton _btnAddFiles;
        private readonly RoundedButton _btnSend;
        private readonly ProgressBar _progressBar;
        private readonly Label _lblStatus;
        private readonly Label _lblEta;

        private bool _isSending;
        private bool _suppressPeerItemCheck;
        private string _etaText = string.Empty;

        public MainForm(PeerInfo localProfile)
        {
            _localProfile = localProfile;

            Text = "ONKOS File Transfer 2.0";
            Size = new Size(920, 560);
            MinimumSize = new Size(800, 480);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(14, 15, 24);
            ForeColor = Color.FromArgb(246, 247, 252);
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            AllowDrop = true;

            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "onkos.ico");
                if (File.Exists(icoPath))
                    Icon = new Icon(icoPath);
            }
            catch
            {
            }

            Color headerBg = Color.FromArgb(20, 22, 34);
            Color cardBg = Color.FromArgb(22, 24, 38);
            Color subtleBtnBg = Color.FromArgb(34, 37, 56);
            Color subtleBtnHover = Color.FromArgb(48, 52, 76);

            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = headerBg,
                Padding = new Padding(16, 12, 16, 10)
            };

            var lblTitle = new Label
            {
                Text = "ONKOS File Transfer 2.0",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(184, 148, 255),
                Location = new Point(16, 17),
                AutoSize = true
            };

            _lblProfile = new Label
            {
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(153, 246, 228),
                Location = new Point(265, 20),
                Size = new Size(340, 24),
                TextAlign = ContentAlignment.MiddleLeft
            };
            UpdateProfileLabel();

            var btnProfile = new RoundedButton
            {
                Text = "Mi Perfil",
                Location = new Point(615, 16),
                Size = new Size(92, 30),
                ParentSurfaceColor = headerBg,
                FillColor = subtleBtnBg,
                HoverColor = subtleBtnHover,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
            };
            btnProfile.Click += BtnProfile_Click;

            var btnHistory = new RoundedButton
            {
                Text = "Historial",
                Location = new Point(715, 16),
                Size = new Size(92, 30),
                ParentSurfaceColor = headerBg,
                FillColor = subtleBtnBg,
                HoverColor = subtleBtnHover,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
            };
            btnHistory.Click += BtnHistory_Click;

            var btnInbox = new RoundedButton
            {
                Text = "Recibidos",
                Location = new Point(815, 16),
                Size = new Size(88, 30),
                ParentSurfaceColor = headerBg,
                FillColor = subtleBtnBg,
                HoverColor = subtleBtnHover,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
            };
            btnInbox.Click += BtnOpenInbox_Click;

            pnlTop.Controls.AddRange(new Control[] { lblTitle, _lblProfile, btnProfile, btnHistory, btnInbox });

            var pnlMain = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = BackColor,
                Padding = new Padding(14, 10, 14, 8)
            };

            var cardPeers = new RoundedPanel
            {
                Location = new Point(14, 10),
                Size = new Size(310, 405),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
                ParentSurfaceColor = BackColor,
                CardColor = cardBg,
                BorderColor = Color.FromArgb(42, 46, 70),
                CornerRadius = 14,
                Padding = new Padding(12)
            };

            var lblPeersTitle = new Label
            {
                Text = "Para quién",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(242, 244, 252),
                Location = new Point(14, 12),
                AutoSize = true
            };

            _txtSearchPeer = new TextBox
            {
                Location = new Point(14, 38),
                Size = new Size(282, 26),
                BackColor = Color.FromArgb(18, 20, 32),
                ForeColor = Color.FromArgb(242, 244, 252),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9F)
            };
            _txtSearchPeer.TextChanged += (s, e) => RefreshPeerList();

            _clbPeers = new CheckedListBox
            {
                Location = new Point(14, 72),
                Size = new Size(282, 318),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(18, 20, 32),
                ForeColor = Color.FromArgb(242, 244, 252),
                BorderStyle = BorderStyle.None,
                CheckOnClick = true,
                IntegralHeight = false,
                Font = new Font("Segoe UI", 9F)
            };
            _clbPeers.ItemCheck += ClbPeers_ItemCheck;

            cardPeers.Controls.AddRange(new Control[] { lblPeersTitle, _txtSearchPeer, _clbPeers });

            var cardFiles = new RoundedPanel
            {
                Location = new Point(336, 10),
                Size = new Size(556, 405),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ParentSurfaceColor = BackColor,
                CardColor = cardBg,
                BorderColor = Color.FromArgb(42, 46, 70),
                CornerRadius = 14,
                Padding = new Padding(12)
            };

            var lblFilesTitle = new Label
            {
                Text = "Archivos y carpetas",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(242, 244, 252),
                Location = new Point(14, 14),
                AutoSize = true
            };

            _btnAddFiles = new RoundedButton
            {
                Text = "+ Adjuntar",
                Location = new Point(330, 10),
                Size = new Size(102, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                ParentSurfaceColor = cardBg,
                FillColor = subtleBtnBg,
                HoverColor = subtleBtnHover,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
            };
            _btnAddFiles.Click += BtnAddFiles_Click;

            var btnClearFiles = new RoundedButton
            {
                Text = "Quitar",
                Location = new Point(440, 10),
                Size = new Size(100, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                ParentSurfaceColor = cardBg,
                FillColor = subtleBtnBg,
                HoverColor = Color.FromArgb(185, 58, 78),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
            };
            btnClearFiles.Click += BtnRemoveSelectedFiles_Click;

            _lvFiles = new ListView
            {
                Location = new Point(14, 48),
                Size = new Size(526, 342),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(18, 20, 32),
                ForeColor = Color.FromArgb(242, 244, 252),
                Font = new Font("Segoe UI", 8.8F),
                AllowDrop = true
            };
            _lvFiles.Columns.Add("Nombre", 275);
            _lvFiles.Columns.Add("Peso", 95, HorizontalAlignment.Right);
            _lvFiles.Columns.Add("Estado", 145);
            _lvFiles.DragEnter += OnDragEnter;
            _lvFiles.DragDrop += OnDragDrop;

            cardFiles.Controls.AddRange(new Control[] { lblFilesTitle, _btnAddFiles, btnClearFiles, _lvFiles });

            pnlMain.Controls.AddRange(new Control[] { cardPeers, cardFiles });

            var pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 76,
                BackColor = headerBg,
                Padding = new Padding(16, 10, 16, 10)
            };

            _progressBar = new ProgressBar
            {
                Location = new Point(16, 10),
                Size = new Size(710, 14),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Minimum = 0,
                Maximum = 100,
                Value = 0
            };

            _lblStatus = new Label
            {
                Text = "Listo para enviar archivos o carpetas.",
                Location = new Point(16, 34),
                Size = new Size(560, 24),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Color.FromArgb(190, 196, 220),
                Font = new Font("Segoe UI", 8.8F)
            };

            _lblEta = new Label
            {
                Text = string.Empty,
                Location = new Point(580, 34),
                Size = new Size(146, 24),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(153, 246, 228),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
            };

            _btnSend = new RoundedButton
            {
                Text = "Enviar a 0",
                Location = new Point(740, 12),
                Size = new Size(162, 46),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right,
                ParentSurfaceColor = headerBg,
                FillColor = Color.FromArgb(124, 77, 224),
                HoverColor = Color.FromArgb(142, 96, 242),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            _btnSend.Click += BtnSend_Click;

            pnlBottom.Controls.AddRange(new Control[] { _progressBar, _lblStatus, _lblEta, _btnSend });

            Controls.AddRange(new Control[] { pnlMain, pnlBottom, pnlTop });

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            Resize += (s, e) => AdjustFileColumns();

            _discovery = new PeerDiscoveryService();
            _discovery.PeersUpdated += OnPeersUpdated;

            Shown += (s, e) =>
            {
                ApplyDarkTitleBar();
                AdjustFileColumns();
                _discovery.Start(_localProfile);
                RefreshPeerList();
                UpdateSendButtonText();
            };

            FormClosing += MainForm_FormClosing;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _discovery.PeersUpdated -= OnPeersUpdated;
            _discovery.Dispose();
        }

        private void ApplyDarkTitleBar()
        {
            try
            {
                if (!IsHandleCreated)
                    return;

                int useDark = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref useDark, sizeof(int));
            }
            catch
            {
            }
        }

        private void AdjustFileColumns()
        {
            if (_lvFiles == null || _lvFiles.Columns.Count < 3)
                return;

            int usable = Math.Max(340, _lvFiles.ClientSize.Width - 4);
            _lvFiles.Columns[1].Width = 95;
            _lvFiles.Columns[2].Width = 140;
            _lvFiles.Columns[0].Width = Math.Max(140, usable - 95 - 140);
        }

        private void UpdateProfileLabel()
        {
            if (_localProfile == null)
            {
                _lblProfile.Text = "SIN PERFIL CONFIGURADO";
                return;
            }

            string area = string.IsNullOrWhiteSpace(_localProfile.Area) ? string.Empty : $" ({_localProfile.Area})";
            _lblProfile.Text = $"{_localProfile.FullName}{area}".ToUpperInvariant();
        }

        private void OnPeersUpdated()
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(RefreshPeerList)); } catch { }
                return;
            }

            RefreshPeerList();
        }

        private void RefreshPeerList()
        {
            string filter = _txtSearchPeer != null ? _txtSearchPeer.Text : null;
            List<PeerInfo> peers = _discovery.GetActivePeers(filter, onlyOnline: false);

            _allPeers.Clear();
            _allPeers.AddRange(peers);

            HashSet<string> checkedIps = new HashSet<string>(
                _selectedTargets.Where(t => !string.IsNullOrWhiteSpace(t.IPv4)).Select(t => t.IPv4),
                StringComparer.OrdinalIgnoreCase);

            _suppressPeerItemCheck = true;
            try
            {
                _clbPeers.BeginUpdate();
                _clbPeers.Items.Clear();

                for (int i = 0; i < _allPeers.Count; i++)
                {
                    PeerInfo p = _allPeers[i];
                    bool online = PeerDiscoveryService.IsPeerOnline(p);
                    string label = online ? $"{p.DisplayText}  •  En línea" : $"{p.DisplayText}  •  Guardado";
                    _clbPeers.Items.Add(label);

                    if (!string.IsNullOrWhiteSpace(p.IPv4) && checkedIps.Contains(p.IPv4))
                        _clbPeers.SetItemChecked(i, true);
                }

                _clbPeers.EndUpdate();
            }
            finally
            {
                _suppressPeerItemCheck = false;
            }

            SyncSelectedTargetsFromCheckedList();
            UpdateSendButtonText();
        }

        private void ClbPeers_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_suppressPeerItemCheck)
                return;

            BeginInvoke(new Action(() =>
            {
                SyncSelectedTargetsFromCheckedList();
                UpdateSendButtonText();
            }));
        }

        private void SyncSelectedTargetsFromCheckedList()
        {
            _selectedTargets.Clear();
            for (int i = 0; i < _clbPeers.CheckedIndices.Count; i++)
            {
                int idx = _clbPeers.CheckedIndices[i];
                if (idx >= 0 && idx < _allPeers.Count)
                    _selectedTargets.Add(_allPeers[idx]);
            }
        }

        private void UpdateSendButtonText()
        {
            int count = _selectedTargets.Count;
            _btnSend.Text = count == 1 ? "Enviar a 1" : $"Enviar a {count}";
            _btnSend.Enabled = !_isSending && count > 0 && _queuedItems.Count > 0;
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (_isSending)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
            else
                e.Effect = DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (_isSending || e.Data == null)
                return;

            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            {
                AddPathsToQueue(paths);
            }
        }

        private void BtnAddFiles_Click(object sender, EventArgs e)
        {
            if (_isSending)
                return;

            using (var ofd = new OpenFileDialog
            {
                Title = "Seleccionar archivos para enviar",
                Multiselect = true
            })
            {
                if (ofd.ShowDialog(this) == DialogResult.OK && ofd.FileNames.Length > 0)
                {
                    AddPathsToQueue(ofd.FileNames);
                }
            }
        }

        private void BtnRemoveSelectedFiles_Click(object sender, EventArgs e)
        {
            if (_isSending || _lvFiles.SelectedIndices.Count == 0)
                return;

            var indices = _lvFiles.SelectedIndices.Cast<int>().OrderByDescending(i => i).ToList();
            foreach (int idx in indices)
            {
                if (idx >= 0 && idx < _queuedItems.Count)
                    _queuedItems.RemoveAt(idx);
            }

            RefreshFilesListView();
            UpdateSendButtonText();
        }

        private void AddPathsToQueue(IEnumerable<string> paths)
        {
            foreach (string rawPath in paths)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                    continue;

                string fullPath = Path.GetFullPath(rawPath.Trim());
                if (_queuedItems.Any(x => string.Equals(x.SourcePath, fullPath, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (File.Exists(fullPath))
                {
                    long len = 0;
                    try { len = new FileInfo(fullPath).Length; } catch { }

                    _queuedItems.Add(new QueuedTransferItem
                    {
                        SourcePath = fullPath,
                        DisplayName = Path.GetFileName(fullPath),
                        IsDirectory = false,
                        SizeBytes = len,
                        State = QueueItemDeliveryState.Pending
                    });
                }
                else if (Directory.Exists(fullPath))
                {
                    long dirSize = ComputeDirectorySizeSafe(fullPath);
                    string dirName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.IsNullOrWhiteSpace(dirName))
                        dirName = fullPath;

                    _queuedItems.Add(new QueuedTransferItem
                    {
                        SourcePath = fullPath,
                        DisplayName = dirName,
                        IsDirectory = true,
                        SizeBytes = dirSize,
                        State = QueueItemDeliveryState.Pending
                    });
                }
            }

            RefreshFilesListView();
            UpdateSendButtonText();
        }

        private static long ComputeDirectorySizeSafe(string dirPath)
        {
            long total = 0;
            try
            {
                foreach (string file in Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(file).Length; } catch { }
                }
            }
            catch
            {
            }
            return total;
        }

        private void RefreshFilesListView()
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
            {
                try { Invoke(new Action(RefreshFilesListView)); } catch { }
                return;
            }

            _lvFiles.BeginUpdate();
            _lvFiles.Items.Clear();

            foreach (QueuedTransferItem item in _queuedItems)
            {
                string labelName = item.IsDirectory ? $"[Carpeta] {item.DisplayName}" : item.DisplayName;
                var lvi = new ListViewItem(labelName);
                lvi.SubItems.Add(FormatSize(item.SizeBytes));
                lvi.SubItems.Add(GetStateText(item.State));
                _lvFiles.Items.Add(lvi);
            }

            _lvFiles.EndUpdate();
            AdjustFileColumns();
        }

        private static string GetStateText(QueueItemDeliveryState state)
        {
            switch (state)
            {
                case QueueItemDeliveryState.Sending: return "Enviando...";
                case QueueItemDeliveryState.Sent: return "Enviado";
                case QueueItemDeliveryState.Failed: return "Error";
                default: return "Pendiente";
            }
        }

        private void SetStatus(string text)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(SetStatus), text); } catch { }
                return;
            }

            _lblStatus.Text = text ?? string.Empty;
        }

        private void SetProgress(int percent, string etaText)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<int, string>(SetProgress), percent, etaText); } catch { }
                return;
            }

            int clamped = Math.Max(0, Math.Min(100, percent));
            _progressBar.Value = clamped;
            _etaText = etaText ?? string.Empty;
            _lblEta.Text = _etaText;
        }

        private static string FormatEta(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                return string.Empty;

            if (seconds < 1.0)
                return "< 1s";

            TimeSpan ts = TimeSpan.FromSeconds(Math.Ceiling(seconds));
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1)
                return $"{ts.Minutes}m {ts.Seconds}s";
            return $"{ts.Seconds}s";
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.#") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.#") + " MB";
            return (mb / 1024.0).ToString("0.##") + " GB";
        }

        private List<PreparedPayload> PreparePayloads()
        {
            var payloads = new List<PreparedPayload>();
            string tempRoot = Path.Combine(Path.GetTempPath(), "ONKOS_FileTransfer_Packages");

            foreach (QueuedTransferItem item in _queuedItems)
            {
                if (item.IsDirectory && Directory.Exists(item.SourcePath))
                {
                    Directory.CreateDirectory(tempRoot);
                    string zipFileName = $"{item.DisplayName}_{Guid.NewGuid():N}.onkosfolder";
                    string zipPath = Path.Combine(tempRoot, zipFileName);

                    if (File.Exists(zipPath))
                        File.Delete(zipPath);

                    ZipFile.CreateFromDirectory(item.SourcePath, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
                    long pkgSize = new FileInfo(zipPath).Length;

                    payloads.Add(new PreparedPayload
                    {
                        SourceItem = item,
                        PayloadPath = zipPath,
                        DisplayName = item.DisplayName,
                        Size = pkgSize,
                        IsTempPackage = true
                    });
                }
                else if (File.Exists(item.SourcePath))
                {
                    long len = 0;
                    try { len = new FileInfo(item.SourcePath).Length; } catch { }

                    payloads.Add(new PreparedPayload
                    {
                        SourceItem = item,
                        PayloadPath = item.SourcePath,
                        DisplayName = item.DisplayName,
                        Size = len,
                        IsTempPackage = false
                    });
                }
            }

            return payloads;
        }

        private static void CleanupTempPayloads(IEnumerable<PreparedPayload> payloads)
        {
            if (payloads == null)
                return;

            foreach (var p in payloads)
            {
                if (p != null && p.IsTempPackage && !string.IsNullOrWhiteSpace(p.PayloadPath) && File.Exists(p.PayloadPath))
                {
                    try { File.Delete(p.PayloadPath); } catch { }
                }
            }
        }

        private async void BtnSend_Click(object sender, EventArgs e)
        {
            SyncSelectedTargetsFromCheckedList();

            List<PeerInfo> activeTargets = _selectedTargets
                .Where(t => t != null && FileSender.TryParseStrictIPv4(t.IPv4, out _))
                .ToList();

            if (activeTargets.Count == 0)
            {
                MessageBox.Show(this, "Selecciona al menos un destinatario válido.", "ONKOS File Transfer 2.0", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_queuedItems.Count == 0)
            {
                MessageBox.Show(this, "Adjunta al menos un archivo o carpeta para enviar.", "ONKOS File Transfer 2.0", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _isSending = true;
            UpdateSendButtonText();
            SetProgress(0, string.Empty);

            List<PreparedPayload> preparedPayloads = null;
            int successCount = 0;
            int failCount = 0;

            var itemAllSucceeded = new Dictionary<QueuedTransferItem, bool>();
            var itemLastError = new Dictionary<QueuedTransferItem, string>();

            foreach (QueuedTransferItem q in _queuedItems)
            {
                q.State = QueueItemDeliveryState.Pending;
                itemAllSucceeded[q] = true;
            }
            RefreshFilesListView();

            try
            {
                SetStatus("Preparando archivos para envío...");
                preparedPayloads = await System.Threading.Tasks.Task.Run(() => PreparePayloads());

                long totalPerTarget = preparedPayloads.Sum(p => Math.Max(1L, p.Size));
                long grandTotalBytes = Math.Max(1L, totalPerTarget * activeTargets.Count);
                long sentBytesSoFar = 0;

                Stopwatch sw = Stopwatch.StartNew();

                foreach (PeerInfo target in activeTargets)
                {
                    string batchId = Guid.NewGuid().ToString("N");
                    string senderDisplayName = _localProfile != null && !string.IsNullOrWhiteSpace(_localProfile.FullName)
                        ? _localProfile.FullName
                        : Environment.MachineName;

                    foreach (var entry in preparedPayloads)
                    {
                        entry.SourceItem.State = QueueItemDeliveryState.Sending;
                        RefreshFilesListView();

                        SetStatus($"Enviando '{entry.DisplayName}' a {target.FullName}...");
                        long baseBytesForItem = sentBytesSoFar;

                        try
                        {
                            await FileSender.SendFileAsync(
                                target.IPv4,
                                TransferPort,
                                entry.PayloadPath,
                                (sent, total) =>
                                {
                                    long currentTotalSent = baseBytesForItem + sent;
                                    int pct = (int)Math.Round((double)currentTotalSent / grandTotalBytes * 100.0);

                                    string etaStr = string.Empty;
                                    double elapsedSec = sw.Elapsed.TotalSeconds;
                                    if (currentTotalSent > 0 && elapsedSec > 0.35)
                                    {
                                        double bytesPerSec = currentTotalSent / elapsedSec;
                                        long remainingBytes = Math.Max(0, grandTotalBytes - currentTotalSent);
                                        etaStr = FormatEta(remainingBytes / bytesPerSec);
                                    }

                                    SetProgress(pct, etaStr);
                                },
                                batchId,
                                senderDisplayName);

                            target.LastSeenUtc = DateTime.UtcNow;
                            successCount++;

                            TransferHistoryStore.AppendEntry(
                                "ENVIADO",
                                entry.DisplayName,
                                entry.Size,
                                target.DisplayText,
                                "Completado",
                                target.IPv4);
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            itemAllSucceeded[entry.SourceItem] = false;
                            itemLastError[entry.SourceItem] = $"{target.FullName}: {ex.Message}";
                            SetStatus($"Error enviando a {target.FullName}: {ex.Message}");

                            TransferHistoryStore.AppendEntry(
                                "ENVIADO",
                                entry.DisplayName,
                                entry.Size,
                                target.DisplayText,
                                "Error",
                                target.IPv4,
                                ex.Message);
                        }

                        sentBytesSoFar += entry.Size;
                        int completedPct = (int)Math.Round((double)sentBytesSoFar / grandTotalBytes * 100.0);
                        SetProgress(completedPct, completedPct >= 100 ? "listo" : _etaText);
                    }
                }

                foreach (QueuedTransferItem q in _queuedItems)
                {
                    q.State = itemAllSucceeded.TryGetValue(q, out bool ok) && ok
                        ? QueueItemDeliveryState.Sent
                        : QueueItemDeliveryState.Failed;
                }
                RefreshFilesListView();

                if (failCount == 0 && successCount > 0)
                {
                    SetStatus($"Envío completado ({successCount} transferencia(s) exitosa(s)).");
                    SetProgress(100, "listo");
                }
                else if (successCount > 0)
                {
                    SetStatus($"Envío parcial: {successCount} exitosa(s), {failCount} con error.");
                }
                else
                {
                    string lastErr = itemLastError.Values.LastOrDefault() ?? "No se pudo completar el envío.";
                    SetStatus($"Error en envío: {lastErr}");
                }
            }
            catch (Exception ex)
            {
                OnkosLogger.Error("MainForm", "Error durante el envío.", ex);
                SetStatus($"Error: {ex.Message}");
            }
            finally
            {
                CleanupTempPayloads(preparedPayloads);
                _isSending = false;
                UpdateSendButtonText();
            }
        }

        private void BtnProfile_Click(object sender, EventArgs e)
        {
            if (_isSending)
                return;

            using (var dlg = new WelcomeRegistrationForm(_localProfile))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.RegisteredProfile != null)
                {
                    _localProfile = dlg.RegisteredProfile;
                    UpdateProfileLabel();
                    _discovery.UpdateLocalProfile(_localProfile);
                }
            }
        }

        private void BtnHistory_Click(object sender, EventArgs e)
        {
            using (var dlg = new TransferHistoryForm())
            {
                dlg.ShowDialog(this);
            }
        }

        private void BtnOpenInbox_Click(object sender, EventArgs e)
        {
            try
            {
                TransferHistoryStore.MarkReceivedAsSeen();
                string inbox = AppSettingsStore.GetInboxFolder();
                AppSettingsStore.EnsureInboxFolderReady(inbox);
                Process.Start("explorer.exe", inbox);
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("MainForm", "No se pudo abrir la carpeta de recibidos.", ex);
            }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    }
}
