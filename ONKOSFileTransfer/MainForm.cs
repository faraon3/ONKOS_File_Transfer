using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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
        private readonly ImageList _shellIcons;
        private readonly Dictionary<string, int> _iconIndexCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private PeerInfo _localProfile;
        private bool _isSending;
        private int _progressPercent;
        private string _etaText = string.Empty;

        private Label _lblIdentity;
        private Label _lblInboxPath;
        private TextBox _txtSearch;
        private ListBox _lstPeers;
        private RoundedButton _btnAddRecipient;
        private FlowLayoutPanel _flpChips;

        private ListView _lvFiles;
        private Label _lblDropHint;
        private RoundedButton _btnAttach;
        private RoundedButton _btnRemoveSelected;

        private Panel _pnlProgressTrack;
        private Panel _pnlProgressFill;
        private Label _lblProgressMeta;
        private Label _lblStatus;
        private RoundedButton _btnSend;

        public MainForm(PeerInfo localProfile)
        {
            _localProfile = localProfile;
            _discovery = new PeerDiscoveryService();
            _shellIcons = new ImageList
            {
                ImageSize = new Size(20, 20),
                ColorDepth = ColorDepth.Depth32Bit
            };

            InitializeComponent();
            ApplyDarkTitleBar();
            WireEvents();
        }

        private void ApplyDarkTitleBar()
        {
            try
            {
                if (!IsHandleCreated)
                    return;

                int useDark = 1;
                if (DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_20, ref useDark, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_19, ref useDark, sizeof(int));
                }
            }
            catch
            {
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDarkTitleBar();
        }

        private void InitializeComponent()
        {
            Text = "ONKOS File Transfer 2.0";
            Size = new Size(780, 565);
            MinimumSize = new Size(740, 525);
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
            Color accentPurple = Color.FromArgb(124, 77, 224);
            Color accentPurpleHover = Color.FromArgb(142, 96, 242);

            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 66,
                BackColor = headerBg,
                Padding = new Padding(18, 10, 18, 10)
            };

            var lblBrand = new Label
            {
                Text = "ONKOS File Transfer",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(184, 148, 255),
                Location = new Point(18, 10),
                AutoSize = true
            };

            _lblIdentity = new Label
            {
                Text = BuildIdentityText(),
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(165, 171, 198),
                Location = new Point(19, 36),
                AutoSize = true
            };

            var flpTopActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 315,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 0),
                BackColor = headerBg
            };

            var btnChangeInbox = new RoundedButton
            {
                Text = "Ruta destino",
                Size = new Size(102, 30),
                ParentSurfaceColor = headerBg,
                FillColor = subtleBtnBg,
                HoverColor = subtleBtnHover,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Margin = new Padding(6, 0, 0, 0)
            };
            btnChangeInbox.Click += (s, e) => ChangeInboxFolder();

            var btnOpenInbox = new RoundedButton
            {
                Text = "Recibidos",
                Size = new Size(88, 30),
                ParentSurfaceColor = headerBg,
                FillColor = subtleBtnBg,
                HoverColor = subtleBtnHover,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Margin = new Padding(6, 0, 0, 0)
            };
            btnOpenInbox.Click += (s, e) => OpenInboxFolder();

            var btnEditProfile = new RoundedButton
            {
                Text = "Mi Perfil",
                Size = new Size(84, 30),
                ParentSurfaceColor = headerBg,
                FillColor = subtleBtnBg,
                HoverColor = subtleBtnHover,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Margin = new Padding(6, 0, 0, 0)
            };
            btnEditProfile.Click += BtnEditProfile_Click;

            flpTopActions.Controls.AddRange(new Control[] { btnChangeInbox, btnOpenInbox, btnEditProfile });
            pnlHeader.Controls.AddRange(new Control[] { lblBrand, _lblIdentity, flpTopActions });

            var tblMain = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(14, 12, 14, 10),
                BackColor = BackColor
            };
            tblMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47F));
            tblMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53F));
            tblMain.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var cardPeers = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(4),
                Padding = new Padding(14),
                ParentSurfaceColor = BackColor,
                CardColor = cardBg,
                BorderColor = Color.FromArgb(42, 46, 70),
                CornerRadius = 16
            };

            var lblPeersHeader = new Label
            {
                Text = "Para quién",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(242, 244, 252),
                BackColor = cardBg,
                Dock = DockStyle.Top,
                Height = 24
            };

            var pnlSearchRow = new Panel
            {
                Dock = DockStyle.Top,
                Height = 38,
                BackColor = cardBg,
                Padding = new Padding(0, 2, 0, 4)
            };

            var pnlSearchBox = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                ParentSurfaceColor = cardBg,
                CardColor = Color.FromArgb(31, 34, 52),
                BorderColor = Color.FromArgb(52, 56, 84),
                CornerRadius = 10,
                Padding = new Padding(10, 6, 10, 4)
            };

            _txtSearch = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(31, 34, 52),
                ForeColor = Color.FromArgb(246, 247, 252),
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9.5F)
            };
            pnlSearchBox.Controls.Add(_txtSearch);

            _btnAddRecipient = new RoundedButton
            {
                Text = "+",
                Dock = DockStyle.Right,
                Width = 36,
                ParentSurfaceColor = cardBg,
                FillColor = accentPurple,
                HoverColor = accentPurpleHover,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                CornerRadius = 10
            };

            var spacerSearch = new Panel { Dock = DockStyle.Right, Width = 6, BackColor = cardBg };
            pnlSearchRow.Controls.AddRange(new Control[] { pnlSearchBox, spacerSearch, _btnAddRecipient });

            _lstPeers = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(18, 20, 32),
                ForeColor = Color.FromArgb(236, 239, 250),
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                ItemHeight = 26,
                DrawMode = DrawMode.OwnerDrawFixed
            };
            _lstPeers.DrawItem += LstPeers_DrawItem;

            var pnlChipsContainer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 112,
                BackColor = cardBg,
                Padding = new Padding(0, 8, 0, 0)
            };

            var lblSelectedTitle = new Label
            {
                Text = "Seleccionados (clic en × para quitar):",
                Font = new Font("Segoe UI", 8.2F, FontStyle.Bold),
                ForeColor = Color.FromArgb(165, 142, 235),
                BackColor = cardBg,
                Dock = DockStyle.Top,
                Height = 18
            };

            _flpChips = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(18, 20, 32),
                AutoScroll = true,
                Padding = new Padding(4)
            };

            pnlChipsContainer.Controls.AddRange(new Control[] { _flpChips, lblSelectedTitle });
            cardPeers.Controls.AddRange(new Control[] { _lstPeers, pnlChipsContainer, pnlSearchRow, lblPeersHeader });

            var cardFiles = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(4),
                Padding = new Padding(14),
                ParentSurfaceColor = BackColor,
                CardColor = cardBg,
                BorderColor = Color.FromArgb(42, 46, 70),
                CornerRadius = 16,
                AllowDrop = true
            };

            var pnlFilesTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 34,
                BackColor = cardBg
            };

            var lblFilesHeader = new Label
            {
                Text = "Archivos y carpetas",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(242, 244, 252),
                BackColor = cardBg,
                Dock = DockStyle.Left,
                Width = 165,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _btnAttach = new RoundedButton
            {
                Text = "+ Adjuntar",
                Dock = DockStyle.Right,
                Width = 104,
                ParentSurfaceColor = cardBg,
                FillColor = accentPurple,
                HoverColor = accentPurpleHover,
                Font = new Font("Segoe UI", 8.8F, FontStyle.Bold),
                CornerRadius = 10
            };
            _btnAttach.Click += BtnAddFiles_Click;

            _btnRemoveSelected = new RoundedButton
            {
                Text = "Quitar",
                Dock = DockStyle.Right,
                Width = 68,
                ParentSurfaceColor = cardBg,
                FillColor = subtleBtnBg,
                HoverColor = Color.FromArgb(185, 58, 78),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                CornerRadius = 10,
                Visible = false
            };
            _btnRemoveSelected.Click += (s, e) => RemoveSelectedFiles();

            var spacerFilesBtn = new Panel { Dock = DockStyle.Right, Width = 6, BackColor = cardBg };
            pnlFilesTop.Controls.AddRange(new Control[] { lblFilesHeader, _btnRemoveSelected, spacerFilesBtn, _btnAttach });

            var pnlFilesArea = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = cardBg,
                Padding = new Padding(0, 8, 0, 0),
                AllowDrop = true
            };

            _lvFiles = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                HeaderStyle = ColumnHeaderStyle.None,
                FullRowSelect = true,
                MultiSelect = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(18, 20, 32),
                ForeColor = Color.FromArgb(242, 244, 252),
                SmallImageList = _shellIcons,
                AllowDrop = true
            };
            _lvFiles.Columns.Add("Archivo", 250);
            _lvFiles.Columns.Add("Tipo / Tamaño", 95, HorizontalAlignment.Right);

            _lblDropHint = new Label
            {
                Text = "Haz clic aquí o arrastra archivos o carpetas",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(135, 142, 172),
                BackColor = Color.FromArgb(18, 20, 32),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                AllowDrop = true,
                Cursor = Cursors.Hand
            };
            _lblDropHint.Click += BtnAddFiles_Click;

            pnlFilesArea.Controls.Add(_lvFiles);
            pnlFilesArea.Controls.Add(_lblDropHint);
            _lblDropHint.BringToFront();

            cardFiles.Controls.AddRange(new Control[] { pnlFilesArea, pnlFilesTop });

            tblMain.Controls.Add(cardPeers, 0, 0);
            tblMain.Controls.Add(cardFiles, 1, 0);

            var pnlFooter = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 84,
                BackColor = headerBg,
                Padding = new Padding(18, 8, 18, 10)
            };

            _pnlProgressTrack = new Panel
            {
                Dock = DockStyle.Top,
                Height = 6,
                BackColor = Color.FromArgb(34, 38, 58)
            };

            _pnlProgressFill = new Panel
            {
                Location = new Point(0, 0),
                Height = 6,
                Width = 0,
                BackColor = Color.FromArgb(139, 92, 246)
            };
            _pnlProgressTrack.Controls.Add(_pnlProgressFill);

            _btnSend = new RoundedButton
            {
                Text = "Enviar (0)",
                Dock = DockStyle.Right,
                Width = 156,
                ParentSurfaceColor = headerBg,
                FillColor = accentPurple,
                HoverColor = accentPurpleHover,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                CornerRadius = 12,
                Margin = new Padding(12, 8, 0, 2)
            };

            var pnlStatusInfo = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = headerBg,
                Padding = new Padding(0, 6, 8, 0)
            };

            var pnlStatusTopLine = new Panel
            {
                Dock = DockStyle.Top,
                Height = 20,
                BackColor = headerBg
            };

            _lblProgressMeta = new Label
            {
                Text = "0%",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(184, 148, 255),
                BackColor = headerBg,
                Dock = DockStyle.Right,
                Width = 165,
                TextAlign = ContentAlignment.MiddleRight
            };

            _lblStatus = new Label
            {
                Text = "Listo para enviar. Arrastra archivos o elige destinatarios.",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(215, 220, 240),
                BackColor = headerBg,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            pnlStatusTopLine.Controls.Add(_lblStatus);
            pnlStatusTopLine.Controls.Add(_lblProgressMeta);

            _lblInboxPath = new Label
            {
                Text = "Destino recibidos: " + AppSettingsStore.GetInboxFolder(),
                Font = new Font("Segoe UI", 8F),
                ForeColor = Color.FromArgb(135, 142, 172),
                BackColor = headerBg,
                Dock = DockStyle.Top,
                Height = 18,
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };
            _lblInboxPath.Click += (s, e) => OpenInboxFolder();

            pnlStatusInfo.Controls.AddRange(new Control[] { _lblInboxPath, pnlStatusTopLine });

            var pnlFooterBody = new Panel { Dock = DockStyle.Fill, BackColor = headerBg, Padding = new Padding(0, 6, 0, 0) };
            pnlFooterBody.Controls.AddRange(new Control[] { pnlStatusInfo, _btnSend });

            pnlFooter.Controls.AddRange(new Control[] { pnlFooterBody, _pnlProgressTrack });

            Controls.AddRange(new Control[] { tblMain, pnlFooter, pnlHeader });

            RegisterDragDrop(this);
            RegisterDragDrop(cardFiles);
            RegisterDragDrop(pnlFilesArea);
            RegisterDragDrop(_lvFiles);
            RegisterDragDrop(_lblDropHint);

            UpdateSendButtonText();
        }

        private void RegisterDragDrop(Control ctrl)
        {
            ctrl.AllowDrop = true;
            ctrl.DragEnter += (s, e) =>
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                    e.Effect = DragDropEffects.Copy;
                else
                    e.Effect = DragDropEffects.None;
            };
            ctrl.DragDrop += (s, e) =>
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped && dropped.Length > 0)
                        AddItemsToQueue(dropped);
                }
            };
        }

        private string BuildIdentityText()
        {
            string name = _localProfile != null ? _localProfile.FullName : "Sin perfil";
            string area = _localProfile != null ? _localProfile.Area : "";
            string ip = NetworkManager.GetLocalIPv4();
            return $"{name} ({area})  •  IP: {ip}";
        }

        private void WireEvents()
        {
            Load += (s, e) =>
            {
                ModernToastNotifier.Inicializar();
                ApplyDarkTitleBar();
                _discovery.Start(_localProfile);
                RefreshPeersList();
            };

            FormClosing += (s, e) =>
            {
                _discovery.Dispose();
            };

            Resize += (s, e) =>
            {
                UpdateProgressVisual();
                AdjustListViewColumns();
            };

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

            _lvFiles.SelectedIndexChanged += (s, e) =>
            {
                _btnRemoveSelected.Visible = _lvFiles.SelectedItems.Count > 0;
            };

            _lvFiles.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete && _lvFiles.SelectedItems.Count > 0)
                {
                    e.SuppressKeyPress = true;
                    RemoveSelectedFiles();
                }
            };

            _btnSend.Click += BtnSend_Click;
        }

        private void OpenInboxFolder()
        {
            string folder = AppSettingsStore.GetInboxFolder();
            AppSettingsStore.EnsureInboxFolderReady(folder);
            Process.Start("explorer.exe", folder);
        }

        private void ChangeInboxFolder()
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Selecciona la carpeta donde deseas recibir los archivos";
                fbd.SelectedPath = AppSettingsStore.GetInboxFolder();
                if (fbd.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    AppSettingsStore.SetInboxFolder(fbd.SelectedPath);
                    _lblInboxPath.Text = "Destino recibidos: " + AppSettingsStore.GetInboxFolder();
                    SetStatus("Carpeta de recibidos actualizada con su ícono.");
                }
            }
        }

        private void LstPeers_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _lstPeers.Items.Count)
                return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color bg = selected ? Color.FromArgb(46, 38, 78) : Color.FromArgb(18, 20, 32);

            using (var brush = new SolidBrush(bg))
                e.Graphics.FillRectangle(brush, e.Bounds);

            if (_lstPeers.Items[e.Index] is PeerInfo peer)
            {
                bool online = PeerDiscoveryService.IsPeerOnline(peer);
                Color dotColor = online
                    ? Color.FromArgb(34, 197, 94)
                    : Color.FromArgb(244, 63, 94);

                int dotSize = 8;
                int dotX = e.Bounds.X + 10;
                int dotY = e.Bounds.Y + (e.Bounds.Height - dotSize) / 2;

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var dotBrush = new SolidBrush(dotColor))
                {
                    e.Graphics.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);
                }

                string labelText = online
                    ? peer.DisplayText
                    : $"{peer.DisplayText}  •  No disponible";

                Color fg = !online
                    ? Color.FromArgb(251, 113, 133)
                    : (selected ? Color.White : Color.FromArgb(225, 229, 245));

                Rectangle textRect = new Rectangle(
                    dotX + dotSize + 8,
                    e.Bounds.Y + 4,
                    Math.Max(20, e.Bounds.Width - (dotX + dotSize + 16)),
                    Math.Max(16, e.Bounds.Height - 4));

                TextRenderer.DrawText(
                    e.Graphics,
                    labelText,
                    new Font("Segoe UI", 9F, online && selected ? FontStyle.Bold : FontStyle.Regular),
                    textRect,
                    fg,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
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

        private async Task<bool> TrySelectPeerWithCheckAsync(PeerInfo peer, bool allowManualIpProbe = false)
        {
            if (peer == null || string.IsNullOrWhiteSpace(peer.IPv4))
                return false;

            bool online = PeerDiscoveryService.IsPeerOnline(peer);
            if (!online)
            {
                bool reachable = await FileSender.CheckPeerReachableAsync(peer.IPv4, TransferPort, 1200);
                if (!reachable)
                {
                    SetStatus($"'{peer.FullName}' está apagado, suspendido o sin conexión.");
                    MessageBox.Show(
                        this,
                        $"El equipo de '{peer.FullName}' no está disponible en la red (puede estar apagado, suspendido o sin conexión).",
                        "Destinatario no disponible",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return false;
                }
            }
            else if (allowManualIpProbe)
            {
                bool reachable = await FileSender.CheckPeerReachableAsync(peer.IPv4, TransferPort, 1200);
                if (!reachable)
                {
                    SetStatus($"'{peer.FullName}' no responde en el puerto {TransferPort}.");
                    return false;
                }
            }

            AddRecipientToSelection(peer);
            return true;
        }

        private async void AddRecipientFromInputOrSelection()
        {
            string raw = (_txtSearch.Text ?? string.Empty).Trim();

            if (raw.Contains("="))
            {
                string[] parts = raw.Split(new[] { '=' }, 2);
                string manualName = parts[0].Trim();
                string manualIp = parts[1].Trim();

                if (IPAddress.TryParse(manualIp, out IPAddress parsed) && parsed.AddressFamily == AddressFamily.InterNetwork)
                {
                    var identity = await FileSender.QueryPeerIdentityAsync(parsed.ToString(), TransferPort, 700);
                    string finalName = !string.IsNullOrWhiteSpace(identity.FullName) && !UserProfileStore.IsIgnoredSystemName(identity.FullName)
                        ? identity.FullName
                        : manualName;
                    string finalArea = !string.IsNullOrWhiteSpace(identity.Area) ? identity.Area : "Vinculado";

                    PeerInfo registered = _discovery.RegisterManualContact(finalName, finalArea, parsed.ToString());
                    if (registered != null)
                    {
                        registered.LastSeenUtc = !string.IsNullOrWhiteSpace(identity.FullName) ? DateTime.UtcNow : DateTime.MinValue;
                        if (await TrySelectPeerWithCheckAsync(registered, true))
                        {
                            _txtSearch.Clear();
                            SetStatus($"Agregado: {registered.DisplayText}");
                        }
                        return;
                    }
                }

                SetStatus("Usa el formato: Nombre = 192.168.1.50");
                return;
            }

            if (_lstPeers.SelectedItem is PeerInfo selectedPeer)
            {
                if (await TrySelectPeerWithCheckAsync(selectedPeer, false))
                    _txtSearch.Clear();
                return;
            }

            if (!string.IsNullOrWhiteSpace(raw))
            {
                PeerInfo found = _discovery.FindPeerByNameOrIp(raw);
                if (found != null)
                {
                    if (await TrySelectPeerWithCheckAsync(found, false))
                        _txtSearch.Clear();
                    return;
                }

                if (IPAddress.TryParse(raw, out IPAddress ipOnly) && ipOnly.AddressFamily == AddressFamily.InterNetwork)
                {
                    var id = await FileSender.QueryPeerIdentityAsync(ipOnly.ToString(), TransferPort, 700);
                    string pName = !string.IsNullOrWhiteSpace(id.FullName) && !UserProfileStore.IsIgnoredSystemName(id.FullName)
                        ? id.FullName
                        : "Equipo " + ipOnly;
                    string pArea = !string.IsNullOrWhiteSpace(id.Area) ? id.Area : "Red local";

                    PeerInfo reg = _discovery.RegisterManualContact(pName, pArea, ipOnly.ToString());
                    if (reg != null)
                    {
                        reg.LastSeenUtc = !string.IsNullOrWhiteSpace(id.FullName) ? DateTime.UtcNow : DateTime.MinValue;
                        if (await TrySelectPeerWithCheckAsync(reg, true))
                            _txtSearch.Clear();
                        return;
                    }
                }

                SetStatus($"No se encontró '{raw}'. Escribe 'Nombre = IP' para vincular.");
            }
        }

        private void AddRecipientToSelection(PeerInfo peer)
        {
            if (peer == null || string.IsNullOrWhiteSpace(peer.IPv4))
                return;

            bool exists = _selectedRecipients.Any(r =>
                string.Equals(r.IPv4, peer.IPv4, StringComparison.OrdinalIgnoreCase));

            if (!exists)
            {
                _selectedRecipients.Add(new PeerInfo
                {
                    FullName = peer.FullName,
                    Area = peer.Area,
                    IPv4 = peer.IPv4,
                    MachineName = peer.MachineName,
                    LastSeenUtc = peer.LastSeenUtc
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
                var chip = new RoundedPanel
                {
                    Size = new Size(292, 26),
                    ParentSurfaceColor = Color.FromArgb(18, 20, 32),
                    CardColor = Color.FromArgb(52, 40, 88),
                    BorderColor = Color.FromArgb(115, 82, 205),
                    CornerRadius = 12,
                    Margin = new Padding(2)
                };

                var lbl = new Label
                {
                    Text = recipient.DisplayText,
                    ForeColor = Color.White,
                    BackColor = Color.FromArgb(52, 40, 88),
                    Location = new Point(10, 4),
                    Size = new Size(252, 18),
                    Font = new Font("Segoe UI", 8.3F, FontStyle.Bold),
                    AutoEllipsis = true
                };

                var btnX = new Label
                {
                    Text = "×",
                    Location = new Point(265, 3),
                    Size = new Size(20, 19),
                    ForeColor = Color.FromArgb(220, 210, 255),
                    BackColor = Color.FromArgb(52, 40, 88),
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Cursor = Cursors.Hand,
                    Tag = recipient.IPv4
                };
                btnX.Click += (s, e) =>
                {
                    string ipToRemove = (string)((Label)s).Tag;
                    _selectedRecipients.RemoveAll(x => string.Equals(x.IPv4, ipToRemove, StringComparison.OrdinalIgnoreCase));
                    RefreshRecipientChips();
                    UpdateSendButtonText();
                };

                chip.Controls.AddRange(new Control[] { lbl, btnX });
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
                if (ofd.ShowDialog(this) == DialogResult.OK)
                    AddItemsToQueue(ofd.FileNames);
            }
        }

        private void AddItemsToQueue(IEnumerable<string> paths)
        {
            bool addedAny = false;
            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                if (!File.Exists(path) && !Directory.Exists(path))
                    continue;

                if (!_queuedItems.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    _queuedItems.Add(path);
                    addedAny = true;
                }
            }

            if (addedAny)
            {
                RefreshFilesListView();
                UpdateSendButtonText();
            }
        }

        private void RemoveSelectedFiles()
        {
            if (_lvFiles.SelectedItems.Count == 0)
                return;

            var toRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ListViewItem item in _lvFiles.SelectedItems)
            {
                if (item.Tag is string fullPath)
                    toRemove.Add(fullPath);
            }

            _queuedItems.RemoveAll(p => toRemove.Contains(p));
            RefreshFilesListView();
            UpdateSendButtonText();
        }

        private void RefreshFilesListView()
        {
            _lvFiles.BeginUpdate();
            _lvFiles.Items.Clear();

            foreach (string path in _queuedItems)
            {
                bool isDir = Directory.Exists(path);
                string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(name))
                    name = path;

                string meta = isDir ? "Carpeta" : FormatFileSize(new FileInfo(path).Length);
                int iconIndex = GetSystemIconIndex(path, isDir);

                var lvi = new ListViewItem(name, iconIndex)
                {
                    Tag = path,
                    ToolTipText = path
                };
                lvi.SubItems.Add(meta);
                _lvFiles.Items.Add(lvi);
            }

            _lvFiles.EndUpdate();
            _lblDropHint.Visible = _queuedItems.Count == 0;
            _btnRemoveSelected.Visible = false;
            AdjustListViewColumns();
        }

        private void AdjustListViewColumns()
        {
            if (_lvFiles.Columns.Count == 2 && _lvFiles.ClientSize.Width > 120)
            {
                _lvFiles.Columns[1].Width = 90;
                _lvFiles.Columns[0].Width = _lvFiles.ClientSize.Width - 96;
            }
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.#") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.#") + " MB";
            return (mb / 1024.0).ToString("0.##") + " GB";
        }

        private static string FormatEta(double remainingSeconds)
        {
            if (remainingSeconds <= 0.5)
                return "menos de 1s";
            if (remainingSeconds < 60)
                return $"~{Math.Ceiling(remainingSeconds)}s restantes";

            TimeSpan ts = TimeSpan.FromSeconds(Math.Ceiling(remainingSeconds));
            if (ts.TotalHours >= 1)
                return $"~{(int)ts.TotalHours}h {ts.Minutes}m restantes";

            return $"~{ts.Minutes}m {ts.Seconds:D2}s restantes";
        }

        private int GetSystemIconIndex(string path, bool isDirectory)
        {
            string key = isDirectory ? "<FOLDER>" : Path.GetExtension(path).ToLowerInvariant();
            if (string.IsNullOrEmpty(key))
                key = "<FILE>";

            if (_iconIndexCache.TryGetValue(key, out int existingIdx))
                return existingIdx;

            try
            {
                SHFILEINFO shfi = new SHFILEINFO();
                uint flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;
                uint attrs = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;

                IntPtr res = SHGetFileInfo(path, attrs, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
                if (res != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                {
                    using (Icon tempIcon = Icon.FromHandle(shfi.hIcon))
                    {
                        _shellIcons.Images.Add((Icon)tempIcon.Clone());
                    }
                    DestroyIcon(shfi.hIcon);

                    int newIdx = _shellIcons.Images.Count - 1;
                    _iconIndexCache[key] = newIdx;
                    return newIdx;
                }
            }
            catch
            {
            }

            return -1;
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
                    SetStatus($"Perfil actualizado: {_localProfile.DisplayText}");
                }
            }
        }

        private void UpdateSendButtonText()
        {
            int count = _selectedRecipients.Count;
            _btnSend.Text = count == 1 ? "Enviar a 1" : $"Enviar a {count}";
            _btnSend.Enabled = !_isSending && _selectedRecipients.Count > 0 && _queuedItems.Count > 0;
            _btnSend.FillColor = _btnSend.Enabled
                ? Color.FromArgb(124, 77, 224)
                : Color.FromArgb(46, 50, 72);
            _btnSend.Invalidate();
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

        private void SetProgress(int percentage, string eta = null)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action<int, string>(SetProgress), percentage, eta);
                return;
            }

            _progressPercent = Math.Max(0, Math.Min(100, percentage));
            if (eta != null)
                _etaText = eta;

            UpdateProgressVisual();
        }

        private void UpdateProgressVisual()
        {
            if (_pnlProgressTrack != null && _pnlProgressFill != null)
            {
                _pnlProgressFill.Width = (int)Math.Round(_pnlProgressTrack.Width * (_progressPercent / 100.0));
            }

            if (_lblProgressMeta != null)
            {
                _lblProgressMeta.Text = string.IsNullOrWhiteSpace(_etaText)
                    ? $"{_progressPercent}%"
                    : $"{_progressPercent}%  •  {_etaText}";
            }
        }

        private async void BtnSend_Click(object sender, EventArgs e)
        {
            if (_isSending || _selectedRecipients.Count == 0 || _queuedItems.Count == 0)
                return;

            _isSending = true;
            UpdateSendButtonText();
            SetProgress(0, string.Empty);

            List<PeerInfo> targets = _selectedRecipients.ToList();
            List<string> items = _queuedItems.ToList();
            List<string> tempPackages = new List<string>();

            int successCount = 0;
            int failCount = 0;

            try
            {
                List<string> reachableTargets = new List<string>();
                var activeTargets = new List<PeerInfo>();

                foreach (PeerInfo target in targets)
                {
                    bool canReach = await FileSender.CheckPeerReachableAsync(target.IPv4, TransferPort, 1800);
                    if (canReach)
                    {
                        activeTargets.Add(target);
                        reachableTargets.Add(target.FullName);
                    }
                    else
                    {
                        failCount += items.Count;
                        SetStatus($"Destino no disponible ({target.FullName}): apagado, suspendido o sin respuesta.");
                    }
                }

                if (activeTargets.Count == 0)
                {
                    SetStatus("Ningún destinatario seleccionado está disponible en este momento.");
                    ModernToastNotifier.MostrarNotificacion(
                        "Destino no disponible",
                        "El equipo destino está apagado, suspendido o sin conexión.",
                        null,
                        true,
                        "AVISO");
                    return;
                }

                List<string> preparedPayloads = new List<string>();
                long singleBatchBytes = 0;

                foreach (string item in items)
                {
                    if (Directory.Exists(item))
                    {
                        string folderName = Path.GetFileName(item.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        if (string.IsNullOrWhiteSpace(folderName))
                            folderName = "Carpeta";

                        string pkgPath = Path.Combine(Path.GetTempPath(), $"{folderName}_{Guid.NewGuid():N}.onkosfolder");
                        if (File.Exists(pkgPath))
                            File.Delete(pkgPath);

                        ZipFile.CreateFromDirectory(item, pkgPath, CompressionLevel.Fastest, false);
                        tempPackages.Add(pkgPath);
                        preparedPayloads.Add(pkgPath);
                        singleBatchBytes += new FileInfo(pkgPath).Length;
                    }
                    else if (File.Exists(item))
                    {
                        preparedPayloads.Add(item);
                        singleBatchBytes += new FileInfo(item).Length;
                    }
                }

                long grandTotalBytes = Math.Max(1, singleBatchBytes * activeTargets.Count);
                long sentBytesSoFar = 0;
                Stopwatch sw = Stopwatch.StartNew();

                foreach (PeerInfo target in activeTargets)
                {
                    foreach (string payload in preparedPayloads)
                    {
                        string displayName = Path.GetFileName(payload);
                        SetStatus($"Enviando '{displayName}' a {target.FullName}...");
                        long baseBytesForItem = sentBytesSoFar;

                        try
                        {
                            await FileSender.SendFileAsync(
                                target.IPv4,
                                TransferPort,
                                payload,
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
                                });

                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            SetStatus($"Error enviando a {target.FullName}: {ex.Message}");
                        }

                        sentBytesSoFar += new FileInfo(payload).Length;
                        int completedPct = (int)Math.Round((double)sentBytesSoFar / grandTotalBytes * 100.0);
                        SetProgress(completedPct, completedPct >= 100 ? "listo" : _etaText);
                    }
                }

                sw.Stop();
                SetProgress(100, string.Empty);

                if (failCount == 0 && successCount > 0)
                {
                    string summary = activeTargets.Count == 1
                        ? $"Se enviaron {items.Count} elemento(s) a {activeTargets[0].FullName}."
                        : $"Se enviaron {items.Count} elemento(s) a {activeTargets.Count} destinatarios.";

                    SetStatus("Envío completado correctamente.");
                    ModernToastNotifier.MostrarEnvioExitoso(summary);
                }
                else if (successCount > 0)
                {
                    SetStatus($"Completado con {failCount} error(es).");
                    ModernToastNotifier.MostrarNotificacion(
                        "Envío con novedades",
                        $"Enviados: {successCount} • Con error: {failCount}.",
                        null,
                        true,
                        "AVISO");
                }
                else
                {
                    SetStatus("No se pudo completar el envío.");
                    ModernToastNotifier.MostrarNotificacion(
                        "Envío no completado",
                        "Revisa si el destinatario sigue conectado a la red.",
                        null,
                        true,
                        "AVISO");
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

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_19 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_20 = 20;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_SMALLICON = 0x000000001;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }

    public class RoundedPanel : Panel
    {
        public Color ParentSurfaceColor { get; set; } = Color.FromArgb(14, 15, 24);
        public Color CardColor { get; set; } = Color.FromArgb(22, 24, 38);
        public Color BorderColor { get; set; } = Color.FromArgb(44, 48, 72);
        public int CornerRadius { get; set; } = 14;

        public RoundedPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = ParentSurfaceColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(ParentSurfaceColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = CreateRoundRect(rect, CornerRadius))
            using (SolidBrush fill = new SolidBrush(CardColor))
            using (Pen pen = new Pen(BorderColor, 1))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        internal static GraphicsPath CreateRoundRect(Rectangle r, int radius)
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
    }

    public class RoundedButton : Control
    {
        private bool _hovered;

        public Color ParentSurfaceColor { get; set; } = Color.FromArgb(22, 24, 38);
        public Color FillColor { get; set; } = Color.FromArgb(124, 77, 224);
        public Color HoverColor { get; set; } = Color.FromArgb(142, 96, 242);
        public int CornerRadius { get; set; } = 10;

        public RoundedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color surface = Parent != null ? Parent.BackColor : ParentSurfaceColor;
            e.Graphics.Clear(surface);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg = !Enabled ? Color.FromArgb(46, 50, 72) : (_hovered ? HoverColor : FillColor);

            using (GraphicsPath path = RoundedPanel.CreateRoundRect(rect, CornerRadius))
            using (SolidBrush fill = new SolidBrush(bg))
            {
                e.Graphics.FillPath(fill, path);
            }

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                rect,
                Enabled ? ForeColor : Color.FromArgb(150, 155, 180),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
