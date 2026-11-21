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

        private FileSystemWatcher _inboxWatcher;
        private DateTime _lastToastUtc = DateTime.MinValue;
        private string _lastToastFile = string.Empty;

        private Label _lblIdentity;
        private Label _lblInboxPath;
        private TextBox _txtSearch;
        private ListBox _lstPeers;
        private RoundedButton _btnAddRecipient;
        private FlowLayoutPanel _flpChips;

        private ListView _lvFiles;
        private Label _lblDropHint;
        private RoundedButton _btnAttach;
        private ContextMenuStrip _attachMenu;
        private RoundedButton _btnRemoveSelected;

        private Panel _pnlProgressTrack;
        private Panel _pnlProgressFill;
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
            WireEvents();
            StartInboxArrivalWatcher();
        }

        private void InitializeComponent()
        {
            Text = "ONKOS File Transfer 2.0";
            Size = new Size(780, 560);
            MinimumSize = new Size(740, 520);
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

            // --- BARRA SUPERIOR COMPACTA ---
            var pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 66,
                BackColor = Color.FromArgb(20, 22, 34),
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
                BackColor = Color.Transparent
            };

            var btnChangeInbox = new RoundedButton
            {
                Text = "Ruta destino",
                Size = new Size(102, 30),
                FillColor = Color.FromArgb(36, 39, 58),
                HoverColor = Color.FromArgb(50, 54, 78),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Margin = new Padding(6, 0, 0, 0)
            };
            btnChangeInbox.Click += (s, e) => ChangeInboxFolder();

            var btnOpenInbox = new RoundedButton
            {
                Text = "Recibidos",
                Size = new Size(88, 30),
                FillColor = Color.FromArgb(36, 39, 58),
                HoverColor = Color.FromArgb(50, 54, 78),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Margin = new Padding(6, 0, 0, 0)
            };
            btnOpenInbox.Click += (s, e) => OpenInboxFolder();

            var btnEditProfile = new RoundedButton
            {
                Text = "Mi Perfil",
                Size = new Size(84, 30),
                FillColor = Color.FromArgb(36, 39, 58),
                HoverColor = Color.FromArgb(50, 54, 78),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Margin = new Padding(6, 0, 0, 0)
            };
            btnEditProfile.Click += BtnEditProfile_Click;

            flpTopActions.Controls.AddRange(new Control[] { btnChangeInbox, btnOpenInbox, btnEditProfile });
            pnlHeader.Controls.AddRange(new Control[] { lblBrand, _lblIdentity, flpTopActions });

            // --- CONTENIDO PRINCIPAL (2 TARJETAS REDONDEADAS) ---
            var tblMain = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(14, 12, 14, 10),
                BackColor = Color.Transparent
            };
            tblMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47F));
            tblMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53F));
            tblMain.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // TARJETA IZQUIERDA: DESTINATARIOS
            var cardPeers = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(4),
                Padding = new Padding(14),
                CardColor = Color.FromArgb(22, 24, 38),
                BorderColor = Color.FromArgb(42, 46, 70),
                CornerRadius = 16
            };

            var lblPeersHeader = new Label
            {
                Text = "Para quién",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(242, 244, 252),
                Dock = DockStyle.Top,
                Height = 24
            };

            var pnlSearchRow = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                Padding = new Padding(0, 2, 0, 4)
            };

            var pnlSearchBox = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                CardColor = Color.FromArgb(31, 34, 52),
                BorderColor = Color.FromArgb(52, 56, 84),
                CornerRadius = 10,
                Padding = new Padding(10, 5, 10, 4)
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
                FillColor = Color.FromArgb(124, 77, 224),
                HoverColor = Color.FromArgb(142, 96, 242),
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                CornerRadius = 10,
                Margin = new Padding(6, 0, 0, 0)
            };

            var spacerSearch = new Panel { Dock = DockStyle.Right, Width = 6 };
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
                Padding = new Padding(0, 8, 0, 0)
            };

            var lblSelectedTitle = new Label
            {
                Text = "Seleccionados (clic en × para quitar):",
                Font = new Font("Segoe UI", 8.2F, FontStyle.Bold),
                ForeColor = Color.FromArgb(165, 142, 235),
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

            // TARJETA DERECHA: ARCHIVOS Y DRAG & DROP
            var cardFiles = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(4),
                Padding = new Padding(14),
                CardColor = Color.FromArgb(22, 24, 38),
                BorderColor = Color.FromArgb(42, 46, 70),
                CornerRadius = 16,
                AllowDrop = true
            };

            var pnlFilesTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 34
            };

            var lblFilesHeader = new Label
            {
                Text = "Archivos y carpetas",
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(242, 244, 252),
                Dock = DockStyle.Left,
                Width = 165,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _attachMenu = new ContextMenuStrip();
            _attachMenu.Items.Add("Seleccionar archivos...", null, BtnAddFiles_Click);
            _attachMenu.Items.Add("Seleccionar carpeta...", null, BtnAddFolder_Click);

            _btnAttach = new RoundedButton
            {
                Text = "+ Adjuntar ▾",
                Dock = DockStyle.Right,
                Width = 104,
                FillColor = Color.FromArgb(124, 77, 224),
                HoverColor = Color.FromArgb(142, 96, 242),
                Font = new Font("Segoe UI", 8.8F, FontStyle.Bold),
                CornerRadius = 10
            };
            _btnAttach.Click += (s, e) => _attachMenu.Show(_btnAttach, new Point(0, _btnAttach.Height + 4));

            _btnRemoveSelected = new RoundedButton
            {
                Text = "Quitar",
                Dock = DockStyle.Right,
                Width = 68,
                FillColor = Color.FromArgb(42, 46, 68),
                HoverColor = Color.FromArgb(185, 58, 78),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                CornerRadius = 10,
                Visible = false
            };
            _btnRemoveSelected.Click += (s, e) => RemoveSelectedFiles();

            var spacerFilesBtn = new Panel { Dock = DockStyle.Right, Width = 6 };
            pnlFilesTop.Controls.AddRange(new Control[] { lblFilesHeader, _btnRemoveSelected, spacerFilesBtn, _btnAttach });

            var pnlFilesArea = new Panel
            {
                Dock = DockStyle.Fill,
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
                Text = "Arrastra archivos o carpetas aquí\no haz clic en «+ Adjuntar»",
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(135, 142, 172),
                BackColor = Color.FromArgb(18, 20, 32),
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                AllowDrop = true,
                Cursor = Cursors.Hand
            };
            _lblDropHint.Click += (s, e) => _attachMenu.Show(_btnAttach, new Point(0, _btnAttach.Height + 4));

            pnlFilesArea.Controls.Add(_lvFiles);
            pnlFilesArea.Controls.Add(_lblDropHint);
            _lblDropHint.BringToFront();

            cardFiles.Controls.AddRange(new Control[] { pnlFilesArea, pnlFilesTop });

            tblMain.Controls.Add(cardPeers, 0, 0);
            tblMain.Controls.Add(cardFiles, 1, 0);

            // --- BARRA INFERIOR MINIMALISTA ---
            var pnlFooter = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 76,
                BackColor = Color.FromArgb(20, 22, 34),
                Padding = new Padding(18, 8, 18, 10)
            };

            _pnlProgressTrack = new Panel
            {
                Dock = DockStyle.Top,
                Height = 4,
                BackColor = Color.FromArgb(36, 40, 60)
            };

            _pnlProgressFill = new Panel
            {
                Location = new Point(0, 0),
                Height = 4,
                Width = 0,
                BackColor = Color.FromArgb(139, 92, 246)
            };
            _pnlProgressTrack.Controls.Add(_pnlProgressFill);

            _btnSend = new RoundedButton
            {
                Text = "Enviar (0)",
                Dock = DockStyle.Right,
                Width = 156,
                FillColor = Color.FromArgb(124, 77, 224),
                HoverColor = Color.FromArgb(142, 96, 242),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                CornerRadius = 12,
                Margin = new Padding(10, 8, 0, 2)
            };

            var pnlStatusInfo = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 8, 12, 0)
            };

            _lblStatus = new Label
            {
                Text = "Listo para enviar. Arrastra archivos o elige destinatarios.",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(215, 220, 240),
                Dock = DockStyle.Top,
                Height = 20,
                AutoEllipsis = true
            };

            _lblInboxPath = new Label
            {
                Text = "Destino recibidos: " + AppSettingsStore.GetInboxFolder(),
                Font = new Font("Segoe UI", 8F),
                ForeColor = Color.FromArgb(135, 142, 172),
                Dock = DockStyle.Top,
                Height = 18,
                AutoEllipsis = true,
                Cursor = Cursors.Hand
            };
            _lblInboxPath.Click += (s, e) => OpenInboxFolder();

            pnlStatusInfo.Controls.AddRange(new Control[] { _lblInboxPath, _lblStatus });

            var pnlFooterBody = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
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
                _discovery.Start(_localProfile);
                RefreshPeersList();
            };

            FormClosing += (s, e) =>
            {
                _discovery.Dispose();
                _inboxWatcher?.Dispose();
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

        private void StartInboxArrivalWatcher()
        {
            try
            {
                _inboxWatcher?.Dispose();
                string folder = AppSettingsStore.GetInboxFolder();
                AppSettingsStore.EnsureInboxFolderReady(folder);

                _inboxWatcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true
                };

                _inboxWatcher.Created += (s, e) =>
                {
                    string name = Path.GetFileName(e.FullPath);
                    if (string.IsNullOrWhiteSpace(name) ||
                        name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("recibidos.ico", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".onkosfolder", StringComparison.OrdinalIgnoreCase))
                        return;

                    // Evita notificar carpetas intermedias YYYY-MM-DD o IP_hora si contienen archivo interno
                    string parent = Path.GetDirectoryName(e.FullPath);
                    if (string.Equals(parent, folder, StringComparison.OrdinalIgnoreCase))
                        return;

                    DateTime now = DateTime.UtcNow;
                    if ((now - _lastToastUtc).TotalSeconds < 2 &&
                        string.Equals(_lastToastFile, name, StringComparison.OrdinalIgnoreCase))
                        return;

                    _lastToastUtc = now;
                    _lastToastFile = name;

                    ModernToastNotifier.MostrarNotificacion(
                        "Nuevo archivo recibido",
                        $"Recibiste '{name}'. Haz clic para abrir.",
                        e.FullPath);
                };
            }
            catch
            {
            }
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
                    StartInboxArrivalWatcher();
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
                TextRenderer.DrawText(
                    e.Graphics,
                    "●  " + peer.DisplayText,
                    new Font("Segoe UI", 9F, selected ? FontStyle.Bold : FontStyle.Regular),
                    new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 4, e.Bounds.Width - 16, e.Bounds.Height - 4),
                    selected ? Color.White : Color.FromArgb(225, 229, 245),
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
                        SetStatus($"Agregado: {registered.DisplayText}");
                        return;
                    }
                }

                SetStatus("Usa el formato: Nombre = 192.168.1.50");
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
                var chip = new RoundedPanel
                {
                    Size = new Size(292, 26),
                    CardColor = Color.FromArgb(52, 40, 88),
                    BorderColor = Color.FromArgb(115, 82, 205),
                    CornerRadius = 12,
                    Margin = new Padding(2)
                };

                var lbl = new Label
                {
                    Text = recipient.DisplayText,
                    ForeColor = Color.White,
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
                ofd.Title = "Seleccionar archivos";
                if (ofd.ShowDialog() == DialogResult.OK)
                    AddItemsToQueue(ofd.FileNames);
            }
        }

        private void BtnAddFolder_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Seleccionar carpeta";
                if (fbd.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                    AddItemsToQueue(new[] { fbd.SelectedPath });
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
                : Color.FromArgb(52, 55, 78);
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

            _progressPercent = Math.Max(0, Math.Min(100, percentage));
            UpdateProgressVisual();
        }

        private void UpdateProgressVisual()
        {
            if (_pnlProgressTrack != null && _pnlProgressFill != null)
            {
                _pnlProgressFill.Width = (int)Math.Round(_pnlProgressTrack.Width * (_progressPercent / 100.0));
            }
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

            int completedOperations = 0;
            int successCount = 0;
            int failCount = 0;

            try
            {
                List<string> preparedPayloads = new List<string>();
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
                    }
                    else if (File.Exists(item))
                    {
                        preparedPayloads.Add(item);
                    }
                }

                int totalOperations = Math.Max(1, targets.Count * preparedPayloads.Count);

                foreach (PeerInfo target in targets)
                {
                    foreach (string payload in preparedPayloads)
                    {
                        string displayName = Path.GetFileName(payload);
                        SetStatus($"Enviando '{displayName}' a {target.FullName}...");

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
                            SetStatus($"Error enviando a {target.FullName}: {ex.Message}");
                        }

                        completedOperations++;
                        SetProgress((int)Math.Round((double)completedOperations / totalOperations * 100.0));
                    }
                }

                if (failCount == 0)
                {
                    string summary = targets.Count == 1
                        ? $"Se enviaron {items.Count} elemento(s) a {targets[0].FullName}."
                        : $"Se enviaron {items.Count} elemento(s) a {targets.Count} destinatarios.";

                    SetStatus("Envío completado correctamente.");
                    ModernToastNotifier.MostrarNotificacion("Envío exitoso", summary);
                }
                else
                {
                    SetStatus($"Completado con {failCount} error(es).");
                    ModernToastNotifier.MostrarNotificacion(
                        "Envío con novedades",
                        $"Enviados: {successCount} • Con error: {failCount}.",
                        null,
                        true);
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

        // --- INTEROP ICONOS REALES DE WINDOWS ---
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

    internal class RoundedPanel : Panel
    {
        public Color CardColor { get; set; } = Color.FromArgb(22, 24, 38);
        public Color BorderColor { get; set; } = Color.FromArgb(44, 48, 72);
        public int CornerRadius { get; set; } = 14;

        public RoundedPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = CreateRoundRect(rect, CornerRadius))
            using (SolidBrush fill = new SolidBrush(CardColor))
            using (Pen pen = new Pen(BorderColor, 1))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
            base.OnPaint(e);
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

    internal class RoundedButton : Control
    {
        private bool _hovered;

        public Color FillColor { get; set; } = Color.FromArgb(124, 77, 224);
        public Color HoverColor { get; set; } = Color.FromArgb(142, 96, 242);
        public int CornerRadius { get; set; } = 10;

        public RoundedButton()
        {
            DoubleBuffered = true;
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
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg = !Enabled ? Color.FromArgb(48, 51, 72) : (_hovered ? HoverColor : FillColor);

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
                Enabled ? ForeColor : Color.FromArgb(140, 145, 170),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
