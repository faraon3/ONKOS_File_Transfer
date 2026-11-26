using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using ONKOSFileTransfer.Network;

namespace ONKOSFileTransfer
{
    internal enum QueueItemDeliveryState
    {
        Pending,
        Sending,
        Sent,
        PartialError,
        Error
    }

    internal sealed class TransferQueueItem
    {
        public string FullPath { get; set; }
        public string DisplayName { get; set; }
        public bool IsFolder { get; set; }
        public long SizeBytes { get; set; }
        public QueueItemDeliveryState State { get; set; } = QueueItemDeliveryState.Pending;
        public string ErrorMessage { get; set; }
    }

    internal sealed class PreparedPayloadEntry
    {
        public TransferQueueItem SourceItem { get; set; }
        public string PayloadPath { get; set; }
        public string DisplayName { get; set; }
        public long Size { get; set; }
    }

    public class MainForm : Form
    {
        private const int TransferPort = 5050;

        private PeerInfo _localProfile;
        private readonly PeerDiscoveryService _discovery;
        private readonly List<TransferQueueItem> _queue = new List<TransferQueueItem>();
        private readonly HashSet<string> _selectedTargetIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private Label _lblProfileInfo;
        private Label _lblInboxPath;
        private TextBox _txtSearchPeers;
        private CheckBox _chkOnlyOnline;
        private ListView _lvPeers;
        private ListView _lvFiles;
        private ProgressBar _progressBar;
        private Label _lblStatus;
        private Label _lblPercent;
        private RoundedButton _btnSend;
        private RoundedButton _btnReceived;
        private string _etaText = string.Empty;
        private bool _isSending;
        private int _unseenReceivedCount;

        public MainForm(PeerInfo profile)
        {
            _localProfile = profile ?? new PeerInfo
            {
                FullName = Environment.UserName,
                Area = "General",
                IPv4 = NetworkManager.GetLocalIPv4(),
                MachineName = Environment.MachineName
            };

            Text = "ONKOS File Transfer 2.0";
            Size = new Size(980, 620);
            MinimumSize = new Size(880, 540);
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

            BuildUi();

            _discovery = new PeerDiscoveryService();
            _discovery.PeersUpdated += () =>
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    try { BeginInvoke(new Action(RefreshPeersList)); }
                    catch { }
                }
            };
            _discovery.Start(_localProfile);

            TransferHistoryStore.UnseenReceivedCountChanged += OnUnseenCountChanged;
            _unseenReceivedCount = TransferHistoryStore.GetUnseenReceivedCount();
            UpdateReceivedBadge();

            RefreshPeersList();
            RefreshInboxLabel();

            DragEnter += MainForm_DragEnter;
            DragDrop += MainForm_DragDrop;
            Shown += (s, e) => ApplyDarkTitleBar();
            FormClosing += MainForm_FormClosing;
        }

        private void BuildUi()
        {
            Color headerBg = Color.FromArgb(20, 22, 34);
            Color cardBg = Color.FromArgb(22, 24, 38);
            Color borderColor = Color.FromArgb(42, 46, 70);
            Color subtleBtnBg = Color.FromArgb(34, 37, 56);
            Color subtleBtnHover = Color.FromArgb(48, 52, 76);

            // Cabecera superior
            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 68,
                BackColor = headerBg,
                Padding = new Padding(18, 12, 18, 10)
            };

            var lblAppTitle = new Label
            {
                Text = "ONKOS File Transfer 2.0",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(184, 148, 255),
                Location = new Point(18, 10),
                AutoSize = true
            };

            _lblProfileInfo = new Label
            {
                Text = $"{_localProfile.FullName}  •  {_localProfile.Area}  •  IPv4: {NetworkManager.GetLocalIPv4()}",
                Font = new Font("Segoe UI", 8.8F),
                ForeColor = Color.FromArgb(172, 178, 206),
                Location = new Point(18, 38),
                AutoSize = true
            };

            var btnProfile = new RoundedButton
            {
                Text = "Mi perfil",
                Size = new Size(92, 32),
                ParentSurfaceColor = headerBg,
                FillColor = subtleBtnBg,
                HoverColor = subtleBtnHover,
                Font = new Font("Segoe UI", 8.6F, FontStyle.Bold),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnProfile.Click += (s, e) => EditProfile();

            var btnHistory = new RoundedButton
            {
                Text = "Historial",
                Size = new Size(96, 32),
                ParentSurfaceColor = headerBg,
                FillColor = subtleBtnBg,
                HoverColor = subtleBtnHover,
                Font = new Font("Segoe UI", 8.6F, FontStyle.Bold),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnHistory.Click += (s, e) =>
            {
                using (var hist = new TransferHistoryForm())
                    hist.ShowDialog(this);
            };

            _btnReceived = new RoundedButton
            {
                Text = "Recibidos",
                Size = new Size(122, 32),
                ParentSurfaceColor = headerBg,
                FillColor = Color.FromArgb(30, 58, 66),
                HoverColor = Color.FromArgb(38, 76, 86),
                Font = new Font("Segoe UI", 8.6F, FontStyle.Bold),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnReceived.Paint += BtnReceived_PaintBadge;
            _btnReceived.Click += (s, e) =>
            {
                TransferHistoryStore.MarkReceivedAsSeen();
                string inbox = AppSettingsStore.GetInboxFolder();
                AppSettingsStore.EnsureInboxFolderReady(inbox);
                Process.Start("explorer.exe", inbox);
            };

            pnlTop.Resize += (s, e) =>
            {
                int right = pnlTop.ClientSize.Width - 18;
                _btnReceived.Location = new Point(right - _btnReceived.Width, 18);
                right -= (_btnReceived.Width + 8);
                btnHistory.Location = new Point(right - btnHistory.Width, 18);
                right -= (btnHistory.Width + 8);
                btnProfile.Location = new Point(right - btnProfile.Width, 18);
            };

            pnlTop.Controls.AddRange(new Control[] { lblAppTitle, _lblProfileInfo, btnProfile, btnHistory, _btnReceived });

            // Barra inferior de estado y envío
            var pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 76,
                BackColor = headerBg,
                Padding = new Padding(18, 10, 18, 10)
            };

            _lblStatus = new Label
            {
                Text = "Listo para enviar archivos o carpetas por red local IPv4.",
                Font = new Font("Segoe UI", 8.8F),
                ForeColor = Color.FromArgb(195, 200, 224),
                Location = new Point(18, 10),
                Size = new Size(680, 20),
                AutoEllipsis = true
            };

            _lblPercent = new Label
            {
                Text = string.Empty,
                Font = new Font("Segoe UI", 8.6F, FontStyle.Bold),
                ForeColor = Color.FromArgb(167, 139, 250),
                Location = new Point(18, 52),
                AutoSize = true
            };

            _progressBar = new ProgressBar
            {
                Location = new Point(18, 34),
                Size = new Size(720, 12),
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
            };

            _btnSend = new RoundedButton
            {
                Text = "Enviar ahora",
                Size = new Size(165, 42),
                ParentSurfaceColor = headerBg,
                FillColor = Color.FromArgb(124, 77, 224),
                HoverColor = Color.FromArgb(142, 96, 242),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _btnSend.Click += BtnSend_Click;

            pnlBottom.Resize += (s, e) =>
            {
                int w = pnlBottom.ClientSize.Width;
                _btnSend.Location = new Point(w - _btnSend.Width - 18, 16);
                int barW = Math.Max(220, _btnSend.Left - 36);
                _progressBar.Width = barW;
                _lblStatus.Width = barW;
            };

            pnlBottom.Controls.AddRange(new Control[] { _lblStatus, _progressBar, _lblPercent, _btnSend });

            // Cuerpo principal dividido en 2 columnas
            var tblMain = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = BackColor,
                Padding = new Padding(14, 10, 14, 10)
            };
            tblMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
            tblMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56F));
            tblMain.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // Panel izquierdo: Destinatarios
            var cardPeers = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                ParentSurfaceColor = BackColor,
                CardColor = cardBg,
                BorderColor = borderColor,
                Margin = new Padding(0, 0, 7, 0),
                Padding = new Padding(14)
            };

            var lblPeersTitle = new Label
            {
                Text = "1. Selecciona destinatarios",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 242, 252),
                Location = new Point(14, 12),
                AutoSize = true
            };

            var btnAddManual = new RoundedButton
            {
                Text = "+ IP manual",
                Size = new Size(92, 26),
                ParentSurfaceColor = cardBg,
                FillColor = subtleBtnBg,
                HoverColor = subtleBtnHover,
                Font = new Font("Segoe UI", 8.2F, FontStyle.Bold),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnAddManual.Click += (s, e) => AddManualPeerDialog();

            _txtSearchPeers = new TextBox
            {
                Location = new Point(14, 44),
                Size = new Size(240, 24),
                BackColor = Color.FromArgb(30, 33, 50),
                ForeColor = Color.FromArgb(246, 247, 252),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9F),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _txtSearchPeers.TextChanged += (s, e) => RefreshPeersList();

            _chkOnlyOnline = new CheckBox
            {
                Text = "En línea",
                ForeColor = Color.FromArgb(176, 182, 210),
                BackColor = cardBg,
                Font = new Font("Segoe UI", 8.5F),
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _chkOnlyOnline.CheckedChanged += (s, e) => RefreshPeersList();

            _lvPeers = new ListView
            {
                Location = new Point(14, 76),
                Size = new Size(370, 340),
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(18, 20, 32),
                ForeColor = Color.FromArgb(242, 244, 252),
                Font = new Font("Segoe UI", 9F),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            _lvPeers.Columns.Add("Persona / Área", 190);
            _lvPeers.Columns.Add("IPv4", 105);
            _lvPeers.Columns.Add("Estado", 75);
            _lvPeers.ItemChecked += LvPeers_ItemChecked;

            cardPeers.Resize += (s, e) =>
            {
                int cw = cardPeers.ClientSize.Width;
                int ch = cardPeers.ClientSize.Height;
                btnAddManual.Location = new Point(cw - btnAddManual.Width - 14, 11);
                _chkOnlyOnline.Location = new Point(cw - _chkOnlyOnline.Width - 14, 46);
                _txtSearchPeers.Width = Math.Max(110, _chkOnlyOnline.Left - 24);
                _lvPeers.Size = new Size(Math.Max(180, cw - 28), Math.Max(140, ch - 90));
            };

            cardPeers.Controls.AddRange(new Control[] { lblPeersTitle, btnAddManual, _txtSearchPeers, _chkOnlyOnline, _lvPeers });

            // Panel derecho: Archivos / Carpetas a enviar
            var cardFiles = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                ParentSurfaceColor = BackColor,
                CardColor = cardBg,
                BorderColor = borderColor,
                Margin = new Padding(7, 0, 0, 0),
                Padding = new Padding(14)
            };

            var lblFilesTitle = new Label
            {
                Text = "2. Archivos o carpetas a enviar (arrastra aquí)",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 242, 252),
                Location = new Point(14, 12),
                AutoSize = true
            };

            var btnAddFiles = new RoundedButton
            {
                Text = "+ Archivos",
                Location = new Point(14, 42),
                Size = new Size(104, 28),
                ParentSurfaceColor = cardBg,
                FillColor = subtleBtnBg,
                HoverColor = subtleBtnHover,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
            };
            btnAddFiles.Click += (s, e) => SelectFilesToAdd();

            var btnAddFolder = new RoundedButton
            {
                Text = "+ Carpeta",
                Location = new Point(126, 42),
                Size = new Size(100, 28),
                ParentSurfaceColor = cardBg,
                FillColor = subtleBtnBg,
                HoverColor = subtleBtnHover,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
            };
            btnAddFolder.Click += (s, e) => SelectFolderToAdd();

            var btnClearQueue = new RoundedButton
            {
                Text = "Quitar todo",
                Size = new Size(96, 28),
                ParentSurfaceColor = cardBg,
                FillColor = subtleBtnBg,
                HoverColor = Color.FromArgb(185, 58, 78),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnClearQueue.Click += (s, e) =>
            {
                if (_isSending) return;
                _queue.Clear();
                RefreshFilesListView();
            };

            _lvFiles = new ListView
            {
                Location = new Point(14, 78),
                Size = new Size(460, 305),
                View = View.Details,
                FullRowSelect = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(18, 20, 32),
                ForeColor = Color.FromArgb(242, 244, 252),
                Font = new Font("Segoe UI", 9F),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            _lvFiles.Columns.Add("Nombre", 240);
            _lvFiles.Columns.Add("Tipo", 75);
            _lvFiles.Columns.Add("Tamaño", 85, HorizontalAlignment.Right);
            _lvFiles.Columns.Add("Estado", 95);
            _lvFiles.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete && !_isSending && _lvFiles.SelectedIndices.Count > 0)
                {
                    int idx = _lvFiles.SelectedIndices[0];
                    if (idx >= 0 && idx < _queue.Count)
                    {
                        _queue.RemoveAt(idx);
                        RefreshFilesListView();
                    }
                }
            };

            _lblInboxPath = new Label
            {
                Text = "Carpeta de recibidos: ...",
                Font = new Font("Segoe UI", 8.2F),
                ForeColor = Color.FromArgb(156, 163, 194),
                AutoEllipsis = true,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            var btnChangeInbox = new RoundedButton
            {
                Text = "Cambiar carpeta",
                Size = new Size(116, 26),
                ParentSurfaceColor = cardBg,
                FillColor = subtleBtnBg,
                HoverColor = subtleBtnHover,
                Font = new Font("Segoe UI", 8.1F, FontStyle.Bold),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            btnChangeInbox.Click += (s, e) => ChangeInboxFolder();

            cardFiles.Resize += (s, e) =>
            {
                int cw = cardFiles.ClientSize.Width;
                int ch = cardFiles.ClientSize.Height;
                btnClearQueue.Location = new Point(cw - btnClearQueue.Width - 14, 42);
                btnChangeInbox.Location = new Point(cw - btnChangeInbox.Width - 14, ch - 36);
                _lblInboxPath.Location = new Point(14, ch - 31);
                _lblInboxPath.Size = new Size(Math.Max(140, btnChangeInbox.Left - 24), 20);
                _lvFiles.Size = new Size(Math.Max(200, cw - 28), Math.Max(120, ch - 122));
            };

            cardFiles.Controls.AddRange(new Control[]
            {
                lblFilesTitle, btnAddFiles, btnAddFolder, btnClearQueue,
                _lvFiles, _lblInboxPath, btnChangeInbox
            });

            tblMain.Controls.Add(cardPeers, 0, 0);
            tblMain.Controls.Add(cardFiles, 1, 0);

            Controls.AddRange(new Control[] { tblMain, pnlBottom, pnlTop });
        }

        private void BtnReceived_PaintBadge(object sender, PaintEventArgs e)
        {
            if (_unseenReceivedCount <= 0)
                return;

            string text = _unseenReceivedCount > 99 ? "99+" : _unseenReceivedCount.ToString();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int badgeSize = text.Length > 2 ? 24 : 20;
            Rectangle badgeRect = new Rectangle(_btnReceived.Width - badgeSize - 4, 3, badgeSize, 18);

            using (var fill = new SolidBrush(Color.FromArgb(239, 68, 68)))
            using (var border = new Pen(Color.FromArgb(20, 22, 34), 1.5F))
            {
                e.Graphics.FillEllipse(fill, badgeRect);
                e.Graphics.DrawEllipse(border, badgeRect);
            }

            TextRenderer.DrawText(
                e.Graphics,
                text,
                new Font("Segoe UI", 7.2F, FontStyle.Bold),
                badgeRect,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void OnUnseenCountChanged()
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    _unseenReceivedCount = TransferHistoryStore.GetUnseenReceivedCount();
                    UpdateReceivedBadge();
                }));
            }
            catch
            {
            }
        }

        private void UpdateReceivedBadge()
        {
            if (_btnReceived == null || _btnReceived.IsDisposed)
                return;

            _btnReceived.Text = _unseenReceivedCount > 0 ? "Recibidos  " : "Recibidos";
            _btnReceived.Invalidate();
        }

        private void LvPeers_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (e.Item?.Tag is PeerInfo peer && !string.IsNullOrWhiteSpace(peer.IPv4))
            {
                if (e.Item.Checked)
                    _selectedTargetIps.Add(peer.IPv4);
                else
                    _selectedTargetIps.Remove(peer.IPv4);
            }
        }

        private void RefreshPeersList()
        {
            if (_lvPeers == null || _lvPeers.IsDisposed)
                return;

            List<PeerInfo> peers = _discovery.GetActivePeers(_txtSearchPeers.Text, _chkOnlyOnline.Checked);

            _lvPeers.BeginUpdate();
            _lvPeers.ItemChecked -= LvPeers_ItemChecked;
            _lvPeers.Items.Clear();

            foreach (PeerInfo peer in peers)
            {
                bool online = PeerDiscoveryService.IsPeerOnline(peer);
                var lvi = new ListViewItem(peer.DisplayText)
                {
                    Tag = peer,
                    Checked = _selectedTargetIps.Contains(peer.IPv4),
                    ForeColor = online ? Color.FromArgb(240, 244, 255) : Color.FromArgb(145, 152, 180)
                };
                lvi.SubItems.Add(peer.IPv4);
                lvi.SubItems.Add(online ? "En línea" : "Sin respuesta");
                _lvPeers.Items.Add(lvi);
            }

            _lvPeers.ItemChecked += LvPeers_ItemChecked;
            _lvPeers.EndUpdate();
        }

        private void RefreshFilesListView()
        {
            if (_lvFiles == null || _lvFiles.IsDisposed)
                return;

            _lvFiles.BeginUpdate();
            _lvFiles.Items.Clear();

            foreach (TransferQueueItem item in _queue)
            {
                var lvi = new ListViewItem(item.DisplayName)
                {
                    Tag = item,
                    ToolTipText = item.ErrorMessage ?? item.FullPath
                };
                lvi.SubItems.Add(item.IsFolder ? "Carpeta" : "Archivo");
                lvi.SubItems.Add(FormatSize(item.SizeBytes));
                lvi.SubItems.Add(FormatQueueState(item.State));
                _lvFiles.Items.Add(lvi);
            }

            _lvFiles.EndUpdate();
        }

        private static string FormatQueueState(QueueItemDeliveryState state)
        {
            switch (state)
            {
                case QueueItemDeliveryState.Sending: return "Enviando...";
                case QueueItemDeliveryState.Sent: return "Enviado";
                case QueueItemDeliveryState.PartialError: return "Parcial";
                case QueueItemDeliveryState.Error: return "Error";
                default: return "En cola";
            }
        }

        private void SelectFilesToAdd()
        {
            if (_isSending) return;
            using (var ofd = new OpenFileDialog
            {
                Title = "Seleccionar archivos para enviar",
                Multiselect = true
            })
            {
                if (ofd.ShowDialog(this) == DialogResult.OK)
                    AddPathsToQueue(ofd.FileNames);
            }
        }

        private void SelectFolderToAdd()
        {
            if (_isSending) return;
            using (var fbd = new FolderBrowserDialog
            {
                Description = "Selecciona la carpeta que deseas enviar"
            })
            {
                if (fbd.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                    AddPathsToQueue(new[] { fbd.SelectedPath });
            }
        }

        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            if (!_isSending && e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
            else
                e.Effect = DragDropEffects.None;
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            if (_isSending || e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
                AddPathsToQueue(paths);
        }

        private void AddPathsToQueue(IEnumerable<string> paths)
        {
            foreach (string rawPath in paths)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                    continue;

                string clean = rawPath.Trim();
                if (_queue.Any(q => string.Equals(q.FullPath, clean, StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (Directory.Exists(clean))
                {
                    long dirSize = 0;
                    foreach (string f in Directory.EnumerateFiles(clean, "*", SearchOption.AllDirectories))
                    {
                        try { dirSize += new FileInfo(f).Length; }
                        catch { }
                    }

                    _queue.Add(new TransferQueueItem
                    {
                        FullPath = clean,
                        DisplayName = Path.GetFileName(clean.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                        IsFolder = true,
                        SizeBytes = dirSize
                    });
                }
                else if (File.Exists(clean))
                {
                    var fi = new FileInfo(clean);
                    _queue.Add(new TransferQueueItem
                    {
                        FullPath = clean,
                        DisplayName = fi.Name,
                        IsFolder = false,
                        SizeBytes = fi.Length
                    });
                }
            }

            RefreshFilesListView();
        }

        private async void BtnSend_Click(object sender, EventArgs e)
        {
            if (_isSending)
                return;

            List<PeerInfo> activeTargets = _lvPeers.CheckedItems
                .Cast<ListViewItem>()
                .Select(i => i.Tag as PeerInfo)
                .Where(p => p != null && FileSender.TryParseStrictIPv4(p.IPv4, out _))
                .ToList();

            if (activeTargets.Count == 0)
            {
                MessageBox.Show(this, "Selecciona al menos un equipo destinatario.", "Sin destinatario", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_queue.Count == 0)
            {
                MessageBox.Show(this, "Agrega al menos un archivo o carpeta para enviar.", "Cola vacía", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _isSending = true;
            _btnSend.Enabled = false;
            SetProgress(0, string.Empty);

            var tempArchives = new List<string>();
            var preparedPayloads = new List<PreparedPayloadEntry>();

            try
            {
                SetStatus("Preparando elementos para transferencia...");

                foreach (TransferQueueItem item in _queue)
                {
                    item.State = QueueItemDeliveryState.Pending;
                    item.ErrorMessage = null;

                    if (item.IsFolder)
                    {
                        string tempPkg = Path.Combine(
                            Path.GetTempPath(),
                            $"{item.DisplayName}_{Guid.NewGuid():N}.onkosfolder");

                        await Task.Run(() => ZipFile.CreateFromDirectory(item.FullPath, tempPkg, CompressionLevel.Fastest, false));
                        tempArchives.Add(tempPkg);

                        var pkgInfo = new FileInfo(tempPkg);
                        preparedPayloads.Add(new PreparedPayloadEntry
                        {
                            SourceItem = item,
                            PayloadPath = tempPkg,
                            DisplayName = item.DisplayName,
                            Size = pkgInfo.Length
                        });
                    }
                    else
                    {
                        var fi = new FileInfo(item.FullPath);
                        preparedPayloads.Add(new PreparedPayloadEntry
                        {
                            SourceItem = item,
                            PayloadPath = item.FullPath,
                            DisplayName = item.DisplayName,
                            Size = fi.Length
                        });
                    }
                }

                long grandTotalBytes = Math.Max(1L, preparedPayloads.Sum(p => p.Size) * activeTargets.Count);
                long sentBytesSoFar = 0;
                int successCount = 0;
                int failCount = 0;

                var itemAllSucceeded = _queue.ToDictionary(k => k, _ => true);
                var itemLastError = new Dictionary<TransferQueueItem, string>();
                var sw = Stopwatch.StartNew();

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

                foreach (TransferQueueItem item in _queue)
                {
                    if (itemAllSucceeded[item])
                    {
                        item.State = QueueItemDeliveryState.Sent;
                    }
                    else
                    {
                        item.State = successCount > 0 ? QueueItemDeliveryState.PartialError : QueueItemDeliveryState.Error;
                        if (itemLastError.TryGetValue(item, out string err))
                            item.ErrorMessage = err;
                    }
                }

                RefreshFilesListView();
                RefreshPeersList();

                if (failCount == 0)
                {
                    string resumen = $"{successCount} elemento(s) enviado(s) correctamente.";
                    SetStatus(resumen);
                    ModernToastNotifier.MostrarEnvioExitoso(resumen);
                }
                else
                {
                    string resumenErr = $"Envío finalizado: {successCount} completado(s), {failCount} con error.";
                    SetStatus(resumenErr);
                    ModernToastNotifier.MostrarNotificacion("Envío con observaciones", resumenErr, null, true, "AVISO");
                }
            }
            catch (Exception ex)
            {
                OnkosLogger.Error("MainForm", "Error general en envío de lote.", ex);
                SetStatus($"Error: {ex.Message}");
                MessageBox.Show(this, ex.Message, "Error de envío", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                foreach (string tmp in tempArchives)
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); }
                    catch { }
                }

                _isSending = false;
                _btnSend.Enabled = true;
            }
        }

        private void AddManualPeerDialog()
        {
            using (var dlg = new Form
            {
                Text = "Agregar equipo por IPv4",
                Size = new Size(360, 235),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.FromArgb(20, 22, 34),
                ForeColor = Color.FromArgb(246, 247, 252)
            })
            {
                var lblIp = new Label { Text = "Dirección IPv4 (ej. 172.16.13.173)", Location = new Point(18, 16), AutoSize = true };
                var txtIp = new TextBox { Location = new Point(18, 38), Size = new Size(306, 24), BackColor = Color.FromArgb(30, 33, 50), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
                var lblName = new Label { Text = "Nombre de persona o puesto", Location = new Point(18, 72), AutoSize = true };
                var txtName = new TextBox { Location = new Point(18, 94), Size = new Size(306, 24), BackColor = Color.FromArgb(30, 33, 50), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };

                var btnOk = new RoundedButton
                {
                    Text = "Guardar",
                    Location = new Point(214, 142),
                    Size = new Size(110, 32),
                    ParentSurfaceColor = dlg.BackColor
                };
                btnOk.Click += (s, e) =>
                {
                    if (!FileSender.TryParseStrictIPv4(txtIp.Text, out _))
                    {
                        MessageBox.Show(dlg, "Ingresa una dirección IPv4 válida.", "IPv4 inválida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    string nombre = string.IsNullOrWhiteSpace(txtName.Text) ? $"Equipo {txtIp.Text.Trim()}" : txtName.Text.Trim();
                    PeerInfo added = _discovery.RegisterManualContact(nombre, "Manual", txtIp.Text.Trim());
                    if (added != null)
                        _selectedTargetIps.Add(added.IPv4);
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                };

                dlg.Controls.AddRange(new Control[] { lblIp, txtIp, lblName, txtName, btnOk });
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    RefreshPeersList();
            }
        }

        private void EditProfile()
        {
            using (var welcome = new WelcomeRegistrationForm(_localProfile))
            {
                if (welcome.ShowDialog(this) == DialogResult.OK && welcome.RegisteredProfile != null)
                {
                    _localProfile = welcome.RegisteredProfile;
                    _lblProfileInfo.Text = $"{_localProfile.FullName}  •  {_localProfile.Area}  •  IPv4: {NetworkManager.GetLocalIPv4()}";
                    _discovery.UpdateLocalProfile(_localProfile);
                }
            }
        }

        private void ChangeInboxFolder()
        {
            using (var fbd = new FolderBrowserDialog
            {
                Description = "Selecciona la carpeta donde se guardarán los archivos recibidos",
                SelectedPath = AppSettingsStore.GetInboxFolder()
            })
            {
                if (fbd.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    AppSettingsStore.SetInboxFolder(fbd.SelectedPath);
                    RefreshInboxLabel();
                }
            }
        }

        private void RefreshInboxLabel()
        {
            _lblInboxPath.Text = "Carpeta de recibidos: " + AppSettingsStore.GetInboxFolder();
        }

        private void SetStatus(string text)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetStatus(text)));
                return;
            }

            _lblStatus.Text = text;
        }

        private void SetProgress(int percent, string eta)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => SetProgress(percent, eta)));
                return;
            }

            int safePct = Math.Max(0, Math.Min(100, percent));
            _etaText = eta ?? string.Empty;
            _progressBar.Value = safePct;
            _lblPercent.Text = string.IsNullOrWhiteSpace(_etaText)
                ? $"{safePct}%"
                : $"{safePct}%  ({ _etaText })";
        }

        private static string FormatEta(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 1)
                return "menos de 1s";
            if (seconds < 60)
                return $"{Math.Ceiling(seconds)}s restantes";
            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            return $"{(int)ts.TotalMinutes}m {ts.Seconds}s restantes";
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

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            TransferHistoryStore.UnseenReceivedCountChanged -= OnUnseenCountChanged;
            _discovery?.Dispose();
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    }
}
