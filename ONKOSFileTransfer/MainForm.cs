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
    internal enum QueueItemDeliveryState
    {
        Pending,
        Sending,
        Sent,
        Failed
    }

    internal sealed class QueuedTransferItem
    {
        public string FullPath { get; set; }
        public QueueItemDeliveryState State { get; set; } = QueueItemDeliveryState.Pending;
        public string ErrorDetail { get; set; } = string.Empty;
    }

    public class MainForm : Form
    {
        private const int TransferPort = 5050;

        private readonly PeerDiscoveryService _discovery;
        private readonly List<PeerInfo> _selectedRecipients = new List<PeerInfo>();
        private readonly List<QueuedTransferItem> _queuedItems = new List<QueuedTransferItem>();
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
        private Label _lblTotalWeight;
        private RoundedButton _btnAttach;
        private RoundedButton _btnRemoveSelected;
        private ReceivedBadgeButton _btnOpenInbox;

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
            Size = new Size(800, 565);
            MinimumSize = new Size(760, 525);
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
                Width = 300,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 6, 0, 0),
                BackColor = headerBg
            };

            _btnOpenInbox = new ReceivedBadgeButton
            {
                Text = "Recibidos",
                Size = new Size(102, 34),
                ParentSurfaceColor = headerBg,
                FillColor = subtleBtnBg,
                HoverColor = subtleBtnHover,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Margin = new Padding(6, 0, 0, 0),
                UnseenCount = TransferHistoryStore.GetUnseenReceivedCount()
            };
            _btnOpenInbox.Click += (s, e) => OpenInboxFolder();

            var btnHistory = new RoundedButton
            {
                Text = "Historial",
                Size = new Size(84, 34),
                ParentSurfaceColor = headerBg,
                FillColor = subtleBtnBg,
                HoverColor = subtleBtnHover,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Margin = new Padding(6, 0, 0, 0)
            };
            btnHistory.Click += (s, e) =>
            {
                using (var dlg = new TransferHistoryForm())
                    dlg.ShowDialog(this);
                RefreshUnseenReceivedBadge();
            };

            var btnEditProfile = new RoundedButton
            {
                Text = "Mi Perfil",
                Size = new Size(84, 34),
                ParentSurfaceColor = headerBg,
                FillColor = subtleBtnBg,
                HoverColor = subtleBtnHover,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Margin = new Padding(6, 0, 0, 0)
            };
            btnEditProfile.Click += BtnEditProfile_Click;

            flpTopActions.Controls.AddRange(new Control[]
            {
                _btnOpenInbox,
                btnHistory,
                btnEditProfile
            });
            pnlHeader.Controls.AddRange(new Control[] { lblBrand, _lblIdentity, flpTopActions });

            var tblMain = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(14, 12, 14, 10),
                BackColor = BackColor
            };
            tblMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            tblMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
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
                Width = 152,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _lblTotalWeight = new Label
            {
                Text = "Total: 0 B",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(184, 148, 255),
                BackColor = cardBg,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true
            };

            _btnAttach = new RoundedButton
            {
                Text = "+ Adjuntar",
                Dock = DockStyle.Right,
                Width = 98,
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
                Width = 64,
                ParentSurfaceColor = cardBg,
                FillColor = subtleBtnBg,
                HoverColor = Color.FromArgb(185, 58, 78),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                CornerRadius = 10,
                Visible = false
            };
            _btnRemoveSelected.Click += (s, e) => RemoveSelectedFiles();

            var spacerFilesBtn = new Panel { Dock = DockStyle.Right, Width = 6, BackColor = cardBg };
            var spacerWeightBtn = new Panel { Dock = DockStyle.Right, Width = 8, BackColor = cardBg };
            pnlFilesTop.Controls.AddRange(new Control[]
            {
                _lblTotalWeight,
                spacerWeightBtn,
                _btnRemoveSelected,
                spacerFilesBtn,
                _btnAttach,
                lblFilesHeader
            });

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
                OwnerDraw = true,
                ShowItemToolTips = true,
                AllowDrop = true
            };
            _lvFiles.Columns.Add("Archivo", 210);
            _lvFiles.Columns.Add("Tamaño", 74, HorizontalAlignment.Right);
            _lvFiles.Columns.Add("Estado", 98, HorizontalAlignment.Center);

            _lvFiles.DrawColumnHeader += (s, e) => e.DrawDefault = true;
            _lvFiles.DrawItem += (s, e) => { };
            _lvFiles.DrawSubItem += LvFiles_DrawSubItem;

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

        private void LvFiles_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            bool selected = (e.ItemState & ListViewItemStates.Selected) == ListViewItemStates.Selected;
            Color rowBg = selected ? Color.FromArgb(42, 36, 72) : Color.FromArgb(18, 20, 32);

            using (var bgBrush = new SolidBrush(rowBg))
                e.Graphics.FillRectangle(bgBrush, e.Bounds);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            if (e.ColumnIndex == 0)
            {
                int leftPad = e.Bounds.X + 8;
                if (e.Item.ImageIndex >= 0 && _shellIcons != null && e.Item.ImageIndex < _shellIcons.Images.Count)
                {
                    int iconY = e.Bounds.Y + Math.Max(0, (e.Bounds.Height - _shellIcons.ImageSize.Height) / 2);
                    _shellIcons.Draw(e.Graphics, leftPad, iconY, e.Item.ImageIndex);
                    leftPad += _shellIcons.ImageSize.Width + 6;
                }

                Rectangle textBounds = new Rectangle(
                    leftPad,
                    e.Bounds.Y,
                    Math.Max(10, e.Bounds.Right - leftPad - 4),
                    e.Bounds.Height);

                TextRenderer.DrawText(
                    e.Graphics,
                    e.Item.Text,
                    _lvFiles.Font,
                    textBounds,
                    Color.FromArgb(242, 244, 252),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            else if (e.ColumnIndex == 1)
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    e.SubItem.Text,
                    new Font("Segoe UI", 8.3F),
                    e.Bounds,
                    Color.FromArgb(155, 162, 192),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            else if (e.ColumnIndex == 2)
            {
                var qItem = e.Item.Tag as QueuedTransferItem;
                QueueItemDeliveryState state = qItem != null ? qItem.State : QueueItemDeliveryState.Pending;

                string badgeText;
                Color pillBg;
                Color pillBorder;
                Color pillFg;

                switch (state)
                {
                    case QueueItemDeliveryState.Sent:
                        badgeText = "Enviado";
                        pillBg = Color.FromArgb(20, 62, 46);
                        pillBorder = Color.FromArgb(34, 197, 94);
                        pillFg = Color.FromArgb(110, 231, 183);
                        break;
                    case QueueItemDeliveryState.Failed:
                        badgeText = "Error";
                        pillBg = Color.FromArgb(68, 24, 36);
                        pillBorder = Color.FromArgb(244, 63, 94);
                        pillFg = Color.FromArgb(253, 164, 175);
                        break;
                    case QueueItemDeliveryState.Sending:
                        badgeText = "Enviando";
                        pillBg = Color.FromArgb(52, 38, 86);
                        pillBorder = Color.FromArgb(139, 92, 246);
                        pillFg = Color.FromArgb(216, 180, 254);
                        break;
                    default:
                        badgeText = "En cola";
                        pillBg = Color.FromArgb(34, 38, 56);
                        pillBorder = Color.FromArgb(72, 78, 108);
                        pillFg = Color.FromArgb(180, 186, 212);
                        break;
                }

                int pillW = Math.Min(82, Math.Max(54, e.Bounds.Width - 10));
                int pillH = Math.Min(19, Math.Max(14, e.Bounds.Height - 4));
                int pillX = e.Bounds.X + (e.Bounds.Width - pillW) / 2;
                int pillY = e.Bounds.Y + (e.Bounds.Height - pillH) / 2;

                Rectangle pillRect = new Rectangle(pillX, pillY, pillW, pillH);
                using (GraphicsPath path = RoundedPanel.CreateRoundRect(pillRect, pillH / 2))
                using (SolidBrush fill = new SolidBrush(pillBg))
                using (Pen borderPen = new Pen(pillBorder, 1F))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(borderPen, path);
                }

                TextRenderer.DrawText(
                    e.Graphics,
                    badgeText,
                    new Font("Segoe UI", 7.6F, FontStyle.Bold),
                    pillRect,
                    pillFg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
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
                RefreshUnseenReceivedBadge();
            };

            FormClosing += (s, e) =>
            {
                TransferHistoryStore.UnseenReceivedCountChanged -= OnUnseenReceivedCountChangedSafe;
                _discovery.Dispose();
            };

            Resize += (s, e) =>
            {
                UpdateProgressVisual();
                AdjustListViewColumns();
            };

            TransferHistoryStore.UnseenReceivedCountChanged += OnUnseenReceivedCountChangedSafe;
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

        private void OnUnseenReceivedCountChangedSafe()
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke(new Action(RefreshUnseenReceivedBadge));
            }
            catch
            {
            }
        }

        private void RefreshUnseenReceivedBadge()
        {
            if (_btnOpenInbox == null || _btnOpenInbox.IsDisposed)
                return;

            _btnOpenInbox.UnseenCount = TransferHistoryStore.GetUnseenReceivedCount();
        }

        private void OpenInboxFolder()
        {
            TransferHistoryStore.MarkReceivedAsSeen();
            RefreshUnseenReceivedBadge();

            string folder = AppSettingsStore.GetInboxFolder();
            AppSettingsStore.EnsureInboxFolderReady(folder);
            Process.Start("explorer.exe", folder);
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
                bool reachable = await FileSender.CheckPeerReachableAsync(peer.IPv4, TransferPort, 1800);
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
                peer.LastSeenUtc = DateTime.UtcNow;
                RefreshPeersList();
            }
            else if (allowManualIpProbe)
            {
                bool reachable = await FileSender.CheckPeerReachableAsync(peer.IPv4, TransferPort, 1800);
                if (!reachable)
                {
                    SetStatus($"'{peer.FullName}' no responde en el puerto {TransferPort}.");
                    return false;
                }
                peer.LastSeenUtc = DateTime.UtcNow;
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
                    var identity = await FileSender.QueryPeerIdentityAsync(parsed.ToString(), TransferPort, 1200);
                    string finalName = !string.IsNullOrWhiteSpace(identity.FullName) && !UserProfileStore.IsIgnoredSystemName(identity.FullName)
                        ? identity.FullName
                        : manualName;
                    string finalArea = !string.IsNullOrWhiteSpace(identity.Area) ? identity.Area : "Vinculado";

                    PeerInfo registered = _discovery.RegisterManualContact(finalName, finalArea, parsed.ToString());
                    if (registered != null)
                    {
                        registered.LastSeenUtc = DateTime.UtcNow;
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
                    var id = await FileSender.QueryPeerIdentityAsync(ipOnly.ToString(), TransferPort, 1200);
                    string pName = !string.IsNullOrWhiteSpace(id.FullName) && !UserProfileStore.IsIgnoredSystemName(id.FullName)
                        ? id.FullName
                        : "Equipo " + ipOnly;
                    string pArea = !string.IsNullOrWhiteSpace(id.Area) ? id.Area : "Red local";

                    PeerInfo reg = _discovery.RegisterManualContact(pName, pArea, ipOnly.ToString());
                    if (reg != null)
                    {
                        reg.LastSeenUtc = DateTime.UtcNow;
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

                QueuedTransferItem existing = _queuedItems.FirstOrDefault(x =>
                    string.Equals(x.FullPath, path, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    _queuedItems.Add(new QueuedTransferItem
                    {
                        FullPath = path,
                        State = QueueItemDeliveryState.Pending,
                        ErrorDetail = string.Empty
                    });
                    addedAny = true;
                }
                else if (existing.State == QueueItemDeliveryState.Failed)
                {
                    existing.State = QueueItemDeliveryState.Pending;
                    existing.ErrorDetail = string.Empty;
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

            var toRemove = new HashSet<QueuedTransferItem>();
            foreach (ListViewItem item in _lvFiles.SelectedItems)
            {
                if (item.Tag is QueuedTransferItem qItem)
                    toRemove.Add(qItem);
            }

            _queuedItems.RemoveAll(p => toRemove.Contains(p));
            RefreshFilesListView();
            UpdateSendButtonText();
        }

        private long CalculateQueuedTotalBytes()
        {
            long totalBytes = 0;

            foreach (QueuedTransferItem item in _queuedItems)
            {
                string path = item.FullPath;
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                try
                {
                    if (File.Exists(path))
                    {
                        totalBytes += new FileInfo(path).Length;
                    }
                    else if (Directory.Exists(path))
                    {
                        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                totalBytes += new FileInfo(file).Length;
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            return totalBytes;
        }

        private void UpdateTotalWeightLabel()
        {
            if (_lblTotalWeight == null)
                return;

            long total = CalculateQueuedTotalBytes();
            _lblTotalWeight.Text = "Total: " + FormatFileSize(total);
        }

        private void RefreshFilesListView()
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(RefreshFilesListView));
                return;
            }

            _lvFiles.BeginUpdate();
            _lvFiles.Items.Clear();

            foreach (QueuedTransferItem item in _queuedItems)
            {
                string path = item.FullPath;
                bool isDir = Directory.Exists(path);
                string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(name))
                    name = path;

                string meta = isDir
                    ? "Carpeta"
                    : (File.Exists(path) ? FormatFileSize(new FileInfo(path).Length) : "0 B");

                int iconIndex = GetSystemIconIndex(path, isDir);

                string badgeText = item.State == QueueItemDeliveryState.Sent
                    ? "Enviado"
                    : (item.State == QueueItemDeliveryState.Failed
                        ? "Error"
                        : (item.State == QueueItemDeliveryState.Sending ? "Enviando" : "En cola"));

                var lvi = new ListViewItem(name, iconIndex)
                {
                    Tag = item,
                    ToolTipText = string.IsNullOrWhiteSpace(item.ErrorDetail)
                        ? path
                        : $"{path} — {item.ErrorDetail}"
                };
                lvi.SubItems.Add(meta);
                lvi.SubItems.Add(badgeText);
                _lvFiles.Items.Add(lvi);
            }

            _lvFiles.EndUpdate();
            _lblDropHint.Visible = _queuedItems.Count == 0;
            _btnRemoveSelected.Visible = false;
            UpdateTotalWeightLabel();
            AdjustListViewColumns();
        }

        private void AdjustListViewColumns()
        {
            if (_lvFiles.Columns.Count == 3 && _lvFiles.ClientSize.Width > 200)
            {
                int statusW = 94;
                int sizeW = 72;
                _lvFiles.Columns[2].Width = statusW;
                _lvFiles.Columns[1].Width = sizeW;
                _lvFiles.Columns[0].Width = Math.Max(80, _lvFiles.ClientSize.Width - statusW - sizeW - 6);
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
            List<QueuedTransferItem> items = _queuedItems.ToList();
            List<string> tempPackages = new List<string>();

            foreach (QueuedTransferItem qItem in items)
            {
                qItem.State = QueueItemDeliveryState.Pending;
                qItem.ErrorDetail = string.Empty;
            }
            RefreshFilesListView();

            int successCount = 0;
            int failCount = 0;

            try
            {
                var activeTargets = new List<PeerInfo>();
                var offlineTargets = new List<PeerInfo>();

                foreach (PeerInfo target in targets)
                {
                    bool canReach = await FileSender.CheckPeerReachableAsync(target.IPv4, TransferPort, 2000);
                    if (canReach)
                    {
                        target.LastSeenUtc = DateTime.UtcNow;
                        activeTargets.Add(target);
                    }
                    else
                    {
                        offlineTargets.Add(target);
                        failCount += items.Count;
                        SetStatus($"Destino no disponible ({target.FullName}): apagado, suspendido o sin respuesta.");
                    }
                }

                if (activeTargets.Count == 0)
                {
                    foreach (QueuedTransferItem qItem in items)
                    {
                        qItem.State = QueueItemDeliveryState.Failed;
                        qItem.ErrorDetail = "Destino apagado, suspendido o sin respuesta.";
                    }
                    RefreshFilesListView();
                    RefreshPeersList();
                    SetProgress(0, string.Empty);
                    SetStatus("Ningún destinatario seleccionado está disponible en este momento.");
                    ModernToastNotifier.MostrarNotificacion(
                        "Destino no disponible",
                        "El equipo destino está apagado, suspendido o sin conexión.",
                        null,
                        true,
                        "AVISO");
                    return;
                }

                var preparedPayloads = new List<(QueuedTransferItem SourceItem, string PayloadPath, string DisplayName, long Size)>();
                long singleBatchBytes = 0;

                foreach (QueuedTransferItem item in items)
                {
                    string path = item.FullPath;
                    try
                    {
                        if (Directory.Exists(path))
                        {
                            string folderName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                            if (string.IsNullOrWhiteSpace(folderName))
                                folderName = "Carpeta";

                            string pkgPath = Path.Combine(Path.GetTempPath(), $"{folderName}_{Guid.NewGuid():N}.onkosfolder");
                            if (File.Exists(pkgPath))
                                File.Delete(pkgPath);

                            ZipFile.CreateFromDirectory(path, pkgPath, CompressionLevel.Fastest, false);
                            long pkgSize = new FileInfo(pkgPath).Length;
                            tempPackages.Add(pkgPath);
                            preparedPayloads.Add((item, pkgPath, folderName, pkgSize));
                            singleBatchBytes += pkgSize;
                        }
                        else if (File.Exists(path))
                        {
                            long fileSize = new FileInfo(path).Length;
                            preparedPayloads.Add((item, path, Path.GetFileName(path), fileSize));
                            singleBatchBytes += fileSize;
                        }
                        else
                        {
                            item.State = QueueItemDeliveryState.Failed;
                            item.ErrorDetail = "El archivo o carpeta ya no existe en la ruta original.";
                            failCount += targets.Count;
                        }
                    }
                    catch (Exception prepEx)
                    {
                        item.State = QueueItemDeliveryState.Failed;
                        item.ErrorDetail = "No se pudo preparar: " + prepEx.Message;
                        failCount += targets.Count;
                    }
                }

                RefreshFilesListView();

                if (preparedPayloads.Count == 0)
                {
                    SetProgress(0, string.Empty);
                    SetStatus("No se pudo preparar ningún archivo para envío.");
                    return;
                }

                var itemAllSucceeded = preparedPayloads.ToDictionary(p => p.SourceItem, _ => offlineTargets.Count == 0);
                var itemLastError = new Dictionary<QueuedTransferItem, string>();
                if (offlineTargets.Count > 0)
                {
                    string offlineNames = string.Join(", ", offlineTargets.Select(t => t.FullName));
                    foreach (var p in preparedPayloads)
                        itemLastError[p.SourceItem] = $"Sin conexión con: {offlineNames}";
                }

                long grandTotalBytes = Math.Max(1, singleBatchBytes * activeTargets.Count);
                long sentBytesSoFar = 0;
                Stopwatch sw = Stopwatch.StartNew();

                foreach (PeerInfo target in activeTargets)
                {
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
                                });

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

                foreach (var kv in itemAllSucceeded)
                {
                    if (kv.Value)
                    {
                        kv.Key.State = QueueItemDeliveryState.Sent;
                        kv.Key.ErrorDetail = string.Empty;
                    }
                    else
                    {
                        kv.Key.State = QueueItemDeliveryState.Failed;
                        kv.Key.ErrorDetail = itemLastError.TryGetValue(kv.Key, out string err)
                            ? err
                            : "Error durante la transferencia.";
                    }
                }

                sw.Stop();
                RefreshPeersList();

                if (failCount == 0 && successCount > 0)
                {
                    string summary = activeTargets.Count == 1
                        ? $"Se enviaron {items.Count} elemento(s) a {activeTargets[0].FullName}."
                        : $"Se enviaron {items.Count} elemento(s) a {activeTargets.Count} destinatarios.";

                    _queuedItems.Clear();
                    RefreshFilesListView();
                    _selectedRecipients.Clear();
                    RefreshRecipientChips();
                    SetProgress(0, string.Empty);

                    SetStatus("Envío completado correctamente.");
                    ModernToastNotifier.MostrarEnvioExitoso(summary);
                }
                else if (successCount > 0)
                {
                    RefreshFilesListView();
                    SetProgress(0, string.Empty);
                    SetStatus($"Completado con {failCount} error(es). Revisa los elementos marcados en rojo.");
                    ModernToastNotifier.MostrarNotificacion(
                        "Envío con novedades",
                        $"Enviados: {successCount} • Con error: {failCount}. Revisa los estados en la lista.",
                        null,
                        true,
                        "AVISO");
                }
                else
                {
                    RefreshFilesListView();
                    SetProgress(0, string.Empty);
                    SetStatus("No se pudo completar el envío. Revisa los elementos marcados en rojo.");
                    ModernToastNotifier.MostrarNotificacion(
                        "Envío no completado",
                        "Ningún archivo pudo entregarse. Revisa el estado en rojo en la lista.",
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

    internal sealed class ReceivedBadgeButton : RoundedButton
    {
        private int _unseenCount;

        public int UnseenCount
        {
            get => _unseenCount;
            set
            {
                int safe = Math.Max(0, value);
                if (_unseenCount != safe)
                {
                    _unseenCount = safe;
                    Invalidate();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (_unseenCount <= 0)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            string badgeText = _unseenCount > 99 ? "99+" : _unseenCount.ToString();
            int diameter = badgeText.Length >= 3 ? 22 : 18;
            int badgeX = Width - diameter - 2;
            int badgeY = 1;

            Rectangle badgeRect = new Rectangle(badgeX, badgeY, diameter, diameter);
            Color badgeRed = Color.FromArgb(239, 68, 68);
            Color badgeBorder = Parent != null ? Parent.BackColor : ParentSurfaceColor;

            using (GraphicsPath path = RoundedPanel.CreateRoundRect(badgeRect, diameter / 2))
            using (SolidBrush fill = new SolidBrush(badgeRed))
            using (Pen border = new Pen(badgeBorder, 1.5F))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }

            TextRenderer.DrawText(
                e.Graphics,
                badgeText,
                new Font("Segoe UI", 7F, FontStyle.Bold),
                badgeRect,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
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
