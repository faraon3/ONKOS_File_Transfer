using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ONKOS.Core.Discovery;
using ONKOS.Core.Logging;
using ONKOS.Core.Models;
using ONKOS.Core.Notifications;
using ONKOS.Core.Security;
using ONKOS.Core.Storage;
using ONKOS.Core.Transfers;

namespace ONKOS.UI
{
    public sealed class MainForm : Form
    {
        private readonly LanDiscoveryService _discovery = new LanDiscoveryService();
        private readonly TransferQueueManager _queue = new TransferQueueManager();
        private readonly CancellationTokenSource _uiCts = new CancellationTokenSource();
        private readonly System.Windows.Forms.Timer _liveSyncTimer;
        private readonly System.Windows.Forms.Timer _fadeTimer;
        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _trayMenu;
        private readonly ContextMenuStrip _avatarMenu;

        private PeerInfo _localProfile;
        private ThemePalette _palette;
        private long _cachedSelectionBytes;
        private int _lastSeenUnseenCount = -1;
        private string _activeAreaFilter = "Todos";
        private bool _isSendingMode;
        private bool _isDragHovering;
        private int _hoveredFileIndex = -1;
        private string _hoveredQueueKey = string.Empty;

        private readonly List<string> _selectedPaths = new List<string>();
        private readonly HashSet<string> _selectedPeerKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _expandedQueueErrors = new Dictionary<string, string>(StringComparer.Ordinal);

        // Cabecera
        private readonly Panel _pnlHeader;
        private readonly Panel _pnlAvatarBtn;
        private readonly Panel _pnlBellBtn;
        private readonly Label _lblUserName;
        private readonly Label _lblUserArea;
        private bool _hasUnreadNotifications;

        // Estado 1: Vacío
        private readonly Panel _pnlEmptyState;
        private readonly Panel _pnlDropZone;
        private readonly LinkLabel _lnkChooseFolder;

        // Estado 2: Con archivos y destinatarios
        private readonly Panel _pnlComposeState;
        private readonly Button _btnAddMoreBar;
        private readonly Label _lblFilesSummary;
        private readonly Panel _pnlFilesList;
        private readonly FlowLayoutPanel _pnlAreaChips;
        private readonly TextBox _txtPeerSearch;
        private readonly Panel _pnlPeersList;

        // Estado 3: Enviando / Cola
        private readonly Panel _pnlSendingState;
        private readonly Label _lblSendSummary;
        private readonly LinkLabel _lnkEditBatch;
        private readonly Label _lblSendingProgressTitle;
        private readonly Panel _pnlQueueList;

        // Pie
        private readonly Panel _pnlFooter;
        private readonly Panel _pnlLockBadge;
        private readonly Panel _pnlServiceDot;
        private readonly Label _lblServiceStatus;
        private readonly Button _btnNewReceivedChip;
        private readonly Button _btnSendPill;

        public MainForm(PeerInfo profile)
        {
            _localProfile = profile;
            _queue.SetLocalProfile(_localProfile);
            _palette = ThemeManager.GetCurrentPalette();

            Text = UiResources.AppTitle;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Size = new Size(460, 760);
            MinimumSize = new Size(400, 620);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            AllowDrop = true;
            Opacity = ThemeManager.AreAnimationsEnabled() ? 0.0 : 1.0;
            BackColor = _palette.WindowBg;
            ForeColor = _palette.TextPrimary;
            Font = ThemeManager.BodyFont;

            Icon loadedIcon = null;
            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "onkos.ico");
                if (File.Exists(icoPath))
                {
                    loadedIcon = new Icon(icoPath);
                    Icon = loadedIcon;
                }
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("UI.MainForm", $"No se pudo cargar icono ({ex.Message}).");
            }

            // Cabecera mínima
            _pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 68,
                BackColor = _palette.WindowBg,
                Padding = new Padding(18, 12, 18, 8)
            };
            _pnlHeader.Paint += OnHeaderPaint;

            _lblUserName = new Label
            {
                Text = UiResources.AppTitle,
                Font = ThemeManager.SectionFont,
                ForeColor = _palette.TextPrimary,
                Location = new Point(58, 10),
                AutoSize = true
            };

            _lblUserArea = new Label
            {
                Text = BuildUserSubtitle(),
                Font = ThemeManager.SecondaryFont,
                ForeColor = _palette.TextSecondary,
                Location = new Point(58, 34),
                AutoSize = true
            };

            _avatarMenu = new ContextMenuStrip();
            _avatarMenu.Items.Add("Mi perfil", null, (s, e) => OpenProfileWizard());
            _avatarMenu.Items.Add("Configuración", null, (s, e) => OpenSettings());
            _avatarMenu.Items.Add("Ayuda", null, (s, e) => ShowHelpDialog());

            _pnlAvatarBtn = new Panel
            {
                Size = new Size(34, 34),
                Location = new Point(Width - 54, 17),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand
            };
            _pnlAvatarBtn.Paint += OnAvatarPaint;
            _pnlAvatarBtn.Click += (s, e) => _avatarMenu.Show(_pnlAvatarBtn, new Point(0, _pnlAvatarBtn.Height + 6));

            _pnlBellBtn = new Panel
            {
                Size = new Size(34, 34),
                Location = new Point(Width - 94, 17),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand
            };
            _pnlBellBtn.Paint += OnBellPaint;
            _pnlBellBtn.Click += (s, e) => OpenActivityWindow("Alertas");

            _pnlHeader.Controls.AddRange(new Control[] { _lblUserName, _lblUserArea, _pnlBellBtn, _pnlAvatarBtn });

            // Estado 1: Vacío
            _pnlEmptyState = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = _palette.WindowBg,
                Padding = new Padding(18, 6, 18, 10)
            };

            _pnlDropZone = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = _palette.SurfaceBg,
                Cursor = Cursors.Hand
            };
            _pnlDropZone.Paint += OnDropZonePaint;
            _pnlDropZone.Click += (s, e) => PickFilesDialog();
            _pnlDropZone.DragEnter += OnDragEnter;
            _pnlDropZone.DragOver += OnDragOver;
            _pnlDropZone.DragLeave += OnDragLeave;
            _pnlDropZone.DragDrop += OnDragDrop;

            _lnkChooseFolder = new LinkLabel
            {
                Text = UiResources.ChooseFolderLink,
                Font = ThemeManager.SecondaryFont,
                LinkColor = _palette.AccentStart,
                ActiveLinkColor = _palette.AccentEnd,
                VisitedLinkColor = _palette.AccentStart,
                LinkBehavior = LinkBehavior.HoverUnderline,
                AutoSize = true,
                BackColor = _palette.SurfaceBg
            };
            _lnkChooseFolder.Click += (s, e) => PickFolderDialog();
            _pnlDropZone.Controls.Add(_lnkChooseFolder);
            _pnlDropZone.Resize += (s, e) => LayoutDropZoneControls();
            _pnlEmptyState.Controls.Add(_pnlDropZone);

            // Estado 2: Con archivos y destinatarios
            _pnlComposeState = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = _palette.WindowBg,
                Padding = new Padding(18, 4, 18, 8),
                Visible = false
            };

            _btnAddMoreBar = new Button
            {
                Text = "+  Suelta más archivos o haz clic",
                Dock = DockStyle.Top,
                Height = 36
            };
            ThemeManager.ConfigureSmoothButton(_btnAddMoreBar, _palette, ButtonVariant.Secondary, 10);
            _btnAddMoreBar.Click += (s, e) => PickFilesDialog();

            var pnlFilesHeader = new Panel { Dock = DockStyle.Top, Height = 28, Padding = new Padding(0, 6, 0, 0) };
            var lblForSend = new Label
            {
                Text = "Para enviar",
                Font = ThemeManager.SecondaryFont,
                ForeColor = _palette.TextSecondary,
                Dock = DockStyle.Left,
                AutoSize = true
            };
            _lblFilesSummary = new Label
            {
                Text = string.Empty,
                Font = ThemeManager.SecondaryFont,
                ForeColor = _palette.TextSecondary,
                Dock = DockStyle.Right,
                AutoSize = true
            };
            pnlFilesHeader.Controls.AddRange(new Control[] { lblForSend, _lblFilesSummary });

            _pnlFilesList = new Panel
            {
                Dock = DockStyle.Top,
                Height = 196,
                AutoScroll = true,
                BackColor = _palette.WindowBg
            };
            _pnlFilesList.Resize += (s, e) => RebuildFileCards();

            var lblSendTo = new Label
            {
                Text = UiResources.RecipientsTitle,
                Font = ThemeManager.SecondaryFont,
                ForeColor = _palette.TextSecondary,
                Dock = DockStyle.Top,
                Height = 22,
                Padding = new Padding(0, 6, 0, 0)
            };

            _pnlAreaChips = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                WrapContents = false,
                AutoScroll = true,
                BackColor = _palette.WindowBg
            };

            _txtPeerSearch = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = _palette.SurfaceBg,
                ForeColor = _palette.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };
            _txtPeerSearch.TextChanged += (s, e) => RebuildPeerCards();

            _pnlPeersList = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = _palette.WindowBg
            };
            _pnlPeersList.Resize += (s, e) => RebuildPeerCards();

            _pnlComposeState.Controls.Add(_pnlPeersList);
            _pnlComposeState.Controls.Add(_txtPeerSearch);
            _pnlComposeState.Controls.Add(_pnlAreaChips);
            _pnlComposeState.Controls.Add(lblSendTo);
            _pnlComposeState.Controls.Add(_pnlFilesList);
            _pnlComposeState.Controls.Add(pnlFilesHeader);
            _pnlComposeState.Controls.Add(_btnAddMoreBar);

            // Estado 3: Enviando / Cola
            _pnlSendingState = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = _palette.WindowBg,
                Padding = new Padding(18, 4, 18, 8),
                Visible = false
            };

            var pnlBatchBanner = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = _palette.ElevatedSurfaceBg,
                Padding = new Padding(12, 8, 12, 8)
            };
            _lblSendSummary = new Label
            {
                Text = string.Empty,
                Font = ThemeManager.SecondaryFont,
                ForeColor = _palette.TextPrimary,
                Dock = DockStyle.Left,
                AutoSize = true
            };
            _lnkEditBatch = new LinkLabel
            {
                Text = "Editar",
                Font = ThemeManager.SecondaryFont,
                LinkColor = _palette.AccentStart,
                ActiveLinkColor = _palette.AccentEnd,
                VisitedLinkColor = _palette.AccentStart,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Dock = DockStyle.Right,
                AutoSize = true
            };
            _lnkEditBatch.Click += (s, e) => ExitSendingViewToEdit();
            pnlBatchBanner.Controls.AddRange(new Control[] { _lblSendSummary, _lnkEditBatch });

            var pnlSendingHeader = new Panel { Dock = DockStyle.Top, Height = 28, Padding = new Padding(0, 8, 0, 0) };
            var lblSendingLabel = new Label
            {
                Text = "Enviando",
                Font = ThemeManager.SecondaryFont,
                ForeColor = _palette.TextSecondary,
                Dock = DockStyle.Left,
                AutoSize = true
            };
            _lblSendingProgressTitle = new Label
            {
                Text = string.Empty,
                Font = ThemeManager.SecondaryFont,
                ForeColor = _palette.TextSecondary,
                Dock = DockStyle.Right,
                AutoSize = true
            };
            pnlSendingHeader.Controls.AddRange(new Control[] { lblSendingLabel, _lblSendingProgressTitle });

            _pnlQueueList = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = _palette.WindowBg
            };
            _pnlQueueList.Resize += (s, e) => RebuildQueueCards();

            _pnlSendingState.Controls.Add(_pnlQueueList);
            _pnlSendingState.Controls.Add(pnlSendingHeader);
            _pnlSendingState.Controls.Add(pnlBatchBanner);

            // Barra inferior
            _pnlFooter = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 64,
                BackColor = _palette.WindowBg,
                Padding = new Padding(18, 10, 18, 12)
            };

            _pnlLockBadge = new Panel
            {
                Size = new Size(18, 18),
                Location = new Point(18, 22),
                Cursor = Cursors.Hand
            };
            _pnlLockBadge.Paint += (s, e) => LineIcons.DrawIcon(e.Graphics, LineIconKind.Lock, new Rectangle(1, 1, 16, 16), _palette.TextSecondary, 1.5F);
            var tip = new ToolTip();
            tip.SetToolTip(_pnlLockBadge, "Conexión cifrada");

            _pnlServiceDot = new Panel
            {
                Size = new Size(8, 8),
                Location = new Point(42, 27)
            };
            _pnlServiceDot.Paint += OnServiceDotPaint;

            _lblServiceStatus = new Label
            {
                Text = "Servicio activo",
                Font = ThemeManager.SecondaryFont,
                ForeColor = _palette.TextSecondary,
                Location = new Point(54, 22),
                AutoSize = true
            };

            _btnNewReceivedChip = new Button
            {
                Text = string.Empty,
                Size = new Size(92, 30),
                Location = new Point(136, 16),
                Visible = false
            };
            ThemeManager.ConfigureSmoothButton(_btnNewReceivedChip, _palette, ButtonVariant.Secondary, 15);
            _btnNewReceivedChip.Click += (s, e) => OpenActivityWindow("Recibidos");

            _btnSendPill = new Button
            {
                Text = UiResources.SendBaseText,
                Size = new Size(192, 40),
                Location = new Point(Width - 210, 12),
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                Enabled = false
            };
            ThemeManager.ConfigureSmoothButton(_btnSendPill, _palette, ButtonVariant.Primary, 20);
            _btnSendPill.Click += BtnSendPill_Click;

            _pnlFooter.Controls.AddRange(new Control[]
            {
                _pnlLockBadge,
                _pnlServiceDot,
                _lblServiceStatus,
                _btnNewReceivedChip,
                _btnSendPill
            });

            // Bandeja del sistema
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Abrir " + UiResources.AppTitle, null, (s, e) => BringToFrontFromSecondInstance());
            _trayMenu.Items.Add("Actividad", null, (s, e) => OpenActivityWindow("Recibidos"));
            _trayMenu.Items.Add("Configuración", null, (s, e) => OpenSettings());

            _trayIcon = new NotifyIcon
            {
                Icon = loadedIcon ?? SystemIcons.Application,
                Text = UiResources.AppTitle,
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _trayIcon.DoubleClick += (s, e) => BringToFrontFromSecondInstance();

            _fadeTimer = new System.Windows.Forms.Timer { Interval = 18 };
            _fadeTimer.Tick += OnFadeTick;

            _liveSyncTimer = new System.Windows.Forms.Timer { Interval = 1200 };
            _liveSyncTimer.Tick += OnLiveSyncTimerTick;

            _discovery.PeersUpdated += OnDiscoveryPeersUpdated;
            PeerAuthorizationStore.PendingRequestReceived += OnPendingAuthorizationReceived;
            _queue.QueueChanged += OnQueueChanged;
            _queue.BatchCompleted += OnQueueBatchCompleted;

            KeyDown += OnMainFormKeyDown;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            Load += OnMainFormLoad;
            FormClosed += OnMainFormClosed;

            Controls.Add(_pnlEmptyState);
            Controls.Add(_pnlComposeState);
            Controls.Add(_pnlSendingState);
            Controls.Add(_pnlFooter);
            Controls.Add(_pnlHeader);

            SyncVisualState();
            LayoutDropZoneControls();
        }

        private static string BuildJobKey(TransferJob job)
        {
            if (job == null)
                return string.Empty;
            return (job.ItemName ?? string.Empty) + "|" + (job.TargetLabel ?? string.Empty);
        }

        private void OnFadeTick(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing)
                return;
            if (!ThemeManager.AreAnimationsEnabled() || Opacity >= 1.0)
            {
                Opacity = 1.0;
                _fadeTimer.Stop();
                return;
            }
            Opacity = Math.Min(1.0, Opacity + 0.16);
        }

        private void OnHeaderPaint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var logoRect = new RectangleF(18, 15, 32, 32);
            using (GraphicsPath path = ThemeManager.CreateRoundedRectangle(logoRect, 9F))
            using (var grad = new LinearGradientBrush(logoRect, _palette.AccentStart, _palette.AccentEnd, LinearGradientMode.Horizontal))
            {
                e.Graphics.FillPath(grad, path);
            }
            using (var pen = new Pen(Color.White, 2F) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                e.Graphics.DrawLine(pen, 29, 25, 39, 25);
                e.Graphics.DrawLine(pen, 34, 20, 34, 30);
            }
        }

        private void OnBellPaint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            LineIcons.DrawIcon(e.Graphics, LineIconKind.Bell, new Rectangle(5, 5, 24, 24), _palette.TextPrimary, 1.7F);
            if (_hasUnreadNotifications)
            {
                using (var dotBrush = new SolidBrush(_palette.AccentStart))
                {
                    e.Graphics.FillEllipse(dotBrush, 22, 5, 8, 8);
                }
            }
        }

        private void OnAvatarPaint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(1, 1, 32, 32);
            using (var brush = new SolidBrush(_palette.ElevatedSurfaceBg))
            using (var pen = new Pen(_palette.BorderSoft, 1F))
            {
                e.Graphics.FillEllipse(brush, rect);
                e.Graphics.DrawEllipse(pen, rect);
            }
            string initials = GetInitials(_localProfile != null ? _localProfile.FullName : UiResources.AppTitle);
            TextRenderer.DrawText(
                e.Graphics,
                initials,
                ThemeManager.AvatarFont,
                Rectangle.Round(rect),
                _palette.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void OnServiceDotPaint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color c = IsServiceRunning() ? _palette.StatusSuccess : _palette.StatusWarning;
            using (var b = new SolidBrush(c))
            {
                e.Graphics.FillEllipse(b, 0, 0, 8, 8);
            }
        }

        private void OnDropZonePaint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(1, 1, _pnlDropZone.Width - 2, _pnlDropZone.Height - 2);
            using (GraphicsPath path = ThemeManager.CreateRoundedRectangle(r, 18F))
            {
                Color fill = _isDragHovering ? _palette.HoverSurfaceBg : _palette.SurfaceBg;
                using (var bg = new SolidBrush(fill))
                {
                    e.Graphics.FillPath(bg, path);
                }
                Color borderColor = _isDragHovering ? _palette.AccentStart : _palette.BorderSoft;
                using (var pen = new Pen(borderColor, _isDragHovering ? 1.8F : 1.2F))
                {
                    pen.DashStyle = DashStyle.Dash;
                    pen.DashPattern = new float[] { 5F, 4F };
                    e.Graphics.DrawPath(pen, path);
                }
            }

            int cx = _pnlDropZone.Width / 2;
            int cy = _pnlDropZone.Height / 2;

            var badgeRect = new RectangleF(cx - 24, cy - 76, 48, 48);
            using (GraphicsPath bPath = ThemeManager.CreateRoundedRectangle(badgeRect, 14F))
            using (var bBrush = new SolidBrush(_palette.ElevatedSurfaceBg))
            {
                e.Graphics.FillPath(bBrush, bPath);
            }
            using (var pen = new Pen(_palette.AccentStart, 1.8F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                e.Graphics.DrawLine(pen, cx, cy - 42, cx, cy - 62);
                e.Graphics.DrawLine(pen, cx - 7, cy - 55, cx, cy - 62);
                e.Graphics.DrawLine(pen, cx + 7, cy - 55, cx, cy - 62);
            }

            TextRenderer.DrawText(
                e.Graphics,
                UiResources.DropPromptTitle,
                ThemeManager.SectionFont,
                new Rectangle(20, cy - 12, _pnlDropZone.Width - 40, 28),
                _palette.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            TextRenderer.DrawText(
                e.Graphics,
                UiResources.DropPromptSubtitle,
                ThemeManager.SecondaryFont,
                new Rectangle(20, cy + 16, _pnlDropZone.Width - 40, 22),
                _palette.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void LayoutDropZoneControls()
        {
            if (_pnlDropZone == null || _lnkChooseFolder == null)
                return;
            int cx = _pnlDropZone.Width / 2;
            int cy = _pnlDropZone.Height / 2;
            _lnkChooseFolder.Location = new Point(cx - (_lnkChooseFolder.Width / 2) + 52, cy + 17);
        }

        private void OnMainFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.O)
            {
                e.Handled = true;
                PickFilesDialog();
            }
            else if (e.Control && e.KeyCode == Keys.Enter && _btnSendPill.Enabled && !_isSendingMode)
            {
                e.Handled = true;
                BtnSendPill_Click(this, EventArgs.Empty);
            }
            else if (e.KeyCode == Keys.F1)
            {
                e.Handled = true;
                ShowHelpDialog();
            }
        }

        private void ShowHelpDialog()
        {
            MessageBox.Show(this, UiResources.HelpMessage, UiResources.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnMainFormLoad(object sender, EventArgs e)
        {
            _queue.EnsureStarted();
            _discovery.Start(_localProfile);
            RefreshPeersAndAreas();
            RefreshActivityBadge();
            _fadeTimer.Start();
            _liveSyncTimer.Start();
        }

        private void OnMainFormClosed(object sender, FormClosedEventArgs e)
        {
            _fadeTimer.Stop();
            _fadeTimer.Dispose();
            _liveSyncTimer.Stop();
            _liveSyncTimer.Dispose();

            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayMenu.Dispose();
            _avatarMenu.Dispose();

            try { _uiCts.Cancel(); }
            catch (ObjectDisposedException) { }

            _discovery.PeersUpdated -= OnDiscoveryPeersUpdated;
            PeerAuthorizationStore.PendingRequestReceived -= OnPendingAuthorizationReceived;
            _queue.QueueChanged -= OnQueueChanged;
            _queue.BatchCompleted -= OnQueueBatchCompleted;

            _discovery.Dispose();
            _queue.Dispose();
            _uiCts.Dispose();
        }

        private void OnLiveSyncTimerTick(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing)
                return;

            RefreshActivityBadge();
            _pnlServiceDot.Invalidate();
            _lblServiceStatus.Text = IsServiceRunning() ? "Servicio activo" : "Servicio en pausa";

            if (_isSendingMode)
                RebuildQueueCards();
        }

        private void RefreshActivityBadge()
        {
            int unseen = TransferHistoryStore.GetUnseenReceivedCount();
            int unreadNotifs = NotificationCenterStore.GetAll(onlyUnread: true).Count;
            _hasUnreadNotifications = unreadNotifs > 0;
            _pnlBellBtn.Invalidate();

            if (unseen != _lastSeenUnseenCount)
            {
                _lastSeenUnseenCount = unseen;
                if (unseen > 0)
                {
                    _btnNewReceivedChip.Text = $"↓ {unseen} nuevos";
                    _btnNewReceivedChip.Visible = true;
                }
                else
                {
                    _btnNewReceivedChip.Visible = false;
                }
            }
        }

        private void OnDiscoveryPeersUpdated()
        {
            if (IsDisposed || Disposing)
                return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(RefreshPeersAndAreas)); }
                catch (ObjectDisposedException) { }
                return;
            }
            RefreshPeersAndAreas();
        }

        private void OnPendingAuthorizationReceived(AuthorizedPeerRecord record)
        {
            if (IsDisposed || Disposing || record == null)
                return;

            NotificationCenterStore.PublishGrouped(
                NotificationCategory.AuthorizationRequest,
                "auth-" + record.FingerprintSha256,
                "Solicitud de autorización",
                record.DisplayName,
                record.LastIp);
            RefreshActivityBadge();
        }

        private void OnQueueChanged()
        {
            if (IsDisposed || Disposing)
                return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(OnQueueChangedUi)); }
                catch (ObjectDisposedException) { }
                return;
            }
            OnQueueChangedUi();
        }

        private void OnQueueChangedUi()
        {
            if (_isSendingMode)
                RebuildQueueCards();
            UpdateSendPillAppearance();
        }

        private void OnQueueBatchCompleted(List<TransferJob> snapshot)
        {
            if (IsDisposed || Disposing || snapshot == null || snapshot.Count == 0)
                return;

            int ok = snapshot.Count(j => j.State == QueueJobState.Completed);
            int fail = snapshot.Count(j => j.State == QueueJobState.Failed || j.State == QueueJobState.Cancelled);

            NotificationCenterStore.PublishGrouped(
                fail > 0 ? NotificationCategory.Error : NotificationCategory.Sent,
                "batch-summary",
                fail > 0 ? "Envío con incidencias" : "Envío completado",
                $"{ok} enviados · {fail} pendientes",
                string.Empty);

            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => { RebuildQueueCards(); UpdateSendPillAppearance(); RefreshActivityBadge(); })); }
                catch (ObjectDisposedException) { }
            }
        }

        private void SyncVisualState()
        {
            if (_isSendingMode)
            {
                _pnlEmptyState.Visible = false;
                _pnlComposeState.Visible = false;
                _pnlSendingState.Visible = true;
                RebuildQueueCards();
            }
            else if (_selectedPaths.Count == 0)
            {
                _pnlEmptyState.Visible = true;
                _pnlComposeState.Visible = false;
                _pnlSendingState.Visible = false;
                LayoutDropZoneControls();
            }
            else
            {
                _pnlEmptyState.Visible = false;
                _pnlComposeState.Visible = true;
                _pnlSendingState.Visible = false;
                RebuildFileCards();
                RebuildAreaChips();
                RebuildPeerCards();
            }

            UpdateSendPillAppearance();
        }

        private void ExitSendingViewToEdit()
        {
            _isSendingMode = false;
            SyncVisualState();
        }

        private void UpdateSendPillAppearance()
        {
            if (_isSendingMode)
            {
                List<TransferJob> jobs = _queue.GetSnapshot();
                long total = jobs.Sum(j => Math.Max(0L, j.TotalBytes));
                long sent = jobs.Sum(j => Math.Max(0L, j.SentBytes));
                int pct = total > 0
                    ? (int)Math.Min(100, Math.Round((sent * 100.0) / total))
                    : (jobs.Count > 0 && jobs.All(j => j.State == QueueJobState.Completed) ? 100 : 0);

                bool anyActive = jobs.Any(j => j.State == QueueJobState.Running || j.State == QueueJobState.Queued || j.State == QueueJobState.Paused);
                _btnSendPill.Enabled = anyActive;
                _btnSendPill.Text = anyActive ? $"Enviando...  {pct} %" : $"Listo  {pct} %";
                return;
            }

            int files = _selectedPaths.Count;
            int peers = _selectedPeerKeys.Count;
            _btnSendPill.Enabled = files > 0 && peers > 0;
            _btnSendPill.Text = (files > 0 && peers > 0)
                ? $"{UiResources.SendBaseText}  {files} {(files == 1 ? "archivo" : "archivos")} → {peers} {(peers == 1 ? "equipo" : "equipos")}"
                : UiResources.SendBaseText;
        }

        private void RebuildFileCards()
        {
            _pnlFilesList.SuspendLayout();
            _pnlFilesList.Controls.Clear();

            int cardWidth = Math.Max(220, _pnlFilesList.ClientSize.Width - 6);
            int cardHeight = 52;
            int gap = 8;

            for (int i = 0; i < _selectedPaths.Count; i++)
            {
                int idx = i;
                string path = _selectedPaths[idx];
                bool isHovered = (_hoveredFileIndex == idx);

                var card = new Panel
                {
                    Width = cardWidth,
                    Height = cardHeight,
                    Location = new Point(2, i * (cardHeight + gap)),
                    BackColor = isHovered ? _palette.ElevatedSurfaceBg : _palette.SurfaceBg,
                    Cursor = Cursors.Default
                };

                card.MouseEnter += (s, e) => { _hoveredFileIndex = idx; RebuildFileCards(); };
                card.MouseLeave += (s, e) =>
                {
                    if (!card.ClientRectangle.Contains(card.PointToClient(Cursor.Position)))
                    {
                        _hoveredFileIndex = -1;
                        RebuildFileCards();
                    }
                };

                card.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    var r = new RectangleF(0.5F, 0.5F, card.Width - 1F, card.Height - 1F);
                    using (GraphicsPath gp = ThemeManager.CreateRoundedRectangle(r, 12F))
                    using (var b = new SolidBrush(card.BackColor))
                    using (var p = new Pen(isHovered ? _palette.AccentStart : _palette.BorderSoft, 1F))
                    {
                        e.Graphics.FillPath(b, gp);
                        e.Graphics.DrawPath(p, gp);
                    }

                    LineIconKind kind = LineIcons.DetectFileKind(path);
                    Color iconCol = kind == LineIconKind.Folder ? _palette.StatusWarning : _palette.AccentStart;
                    LineIcons.DrawIcon(e.Graphics, kind, new Rectangle(12, 14, 24, 24), iconCol, 1.7F);

                    string name = Path.GetFileName(path);
                    if (string.IsNullOrEmpty(name)) name = path;

                    TextRenderer.DrawText(
                        e.Graphics,
                        name,
                        ThemeManager.BodyFont,
                        new Rectangle(44, 8, card.Width - 82, 20),
                        _palette.TextPrimary,
                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

                    string sub = Directory.Exists(path) ? "Carpeta" : FormatBytes(SafeFileLength(path));
                    TextRenderer.DrawText(
                        e.Graphics,
                        sub,
                        ThemeManager.SecondaryFont,
                        new Rectangle(44, 28, card.Width - 82, 18),
                        _palette.TextSecondary,
                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                };

                if (isHovered)
                {
                    var btnRemove = new Button
                    {
                        Text = "×",
                        Size = new Size(26, 26),
                        Location = new Point(card.Width - 34, 13)
                    };
                    ThemeManager.ConfigureSmoothButton(btnRemove, _palette, ButtonVariant.Secondary, 13);
                    btnRemove.Click += (s, e) => RemovePathAt(idx);
                    card.Controls.Add(btnRemove);
                }

                _pnlFilesList.Controls.Add(card);
            }

            _pnlFilesList.ResumeLayout();
            _lblFilesSummary.Text = _selectedPaths.Count > 0
                ? $"{_selectedPaths.Count} {(_selectedPaths.Count == 1 ? "elemento" : "elementos")} · {FormatBytes(_cachedSelectionBytes)}"
                : string.Empty;
        }

        private void RebuildAreaChips()
        {
            _pnlAreaChips.SuspendLayout();
            _pnlAreaChips.Controls.Clear();

            var areas = new List<string> { "Todos" };
            foreach (string g in AddressBookStore.GetGroupsAndAreas(_discovery.GetPeers()))
            {
                if (!areas.Any(a => string.Equals(a, g, StringComparison.OrdinalIgnoreCase)))
                    areas.Add(g);
            }

            foreach (string area in areas)
            {
                bool active = string.Equals(_activeAreaFilter, area, StringComparison.OrdinalIgnoreCase);
                var chip = new Button
                {
                    Text = area,
                    Height = 28,
                    Width = Math.Max(56, TextRenderer.MeasureText(area, ThemeManager.SecondaryFont).Width + 24),
                    Margin = new Padding(0, 2, 6, 2)
                };
                ThemeManager.ConfigureSmoothButton(chip, _palette, active ? ButtonVariant.Primary : ButtonVariant.Secondary, 14);
                string captured = area;
                chip.Click += (s, e) =>
                {
                    _activeAreaFilter = captured;
                    RebuildAreaChips();
                    RebuildPeerCards();
                };
                _pnlAreaChips.Controls.Add(chip);
            }

            var plusChip = new Button
            {
                Text = UiResources.AddManualPeerChip,
                Size = new Size(30, 28),
                Margin = new Padding(0, 2, 0, 2)
            };
            ThemeManager.ConfigureSmoothButton(plusChip, _palette, ButtonVariant.Secondary, 14);
            plusChip.Click += (s, e) => AddManualPeer();
            _pnlAreaChips.Controls.Add(plusChip);

            _pnlAreaChips.ResumeLayout();
        }

        private void RefreshPeersAndAreas()
        {
            List<PeerInfo> allPeers = _discovery.GetPeers();
            _txtPeerSearch.Visible = allPeers.Count > 8;
            if (!_isSendingMode && _selectedPaths.Count > 0)
            {
                RebuildAreaChips();
                RebuildPeerCards();
            }
        }

        private void RebuildPeerCards()
        {
            _pnlPeersList.SuspendLayout();
            _pnlPeersList.Controls.Clear();

            string query = (_txtPeerSearch.Text ?? string.Empty).Trim();
            List<PeerInfo> peers = _discovery.GetPeers()
                .Where(p => string.Equals(_activeAreaFilter, "Todos", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(p.Area, _activeAreaFilter, StringComparison.OrdinalIgnoreCase))
                .Where(p => string.IsNullOrEmpty(query) ||
                            (!string.IsNullOrEmpty(p.FullName) && p.FullName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrEmpty(p.Area) && p.Area.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrEmpty(p.IPv4) && p.IPv4.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();

            int cardWidth = Math.Max(220, _pnlPeersList.ClientSize.Width - 6);
            int cardHeight = 54;
            int gap = 8;

            for (int i = 0; i < peers.Count; i++)
            {
                PeerInfo peer = peers[i];
                string key = PeerContactsStore.BuildContactKey(peer);
                bool checkedState = _selectedPeerKeys.Contains(key);

                var card = new Panel
                {
                    Width = cardWidth,
                    Height = cardHeight,
                    Location = new Point(2, i * (cardHeight + gap)),
                    BackColor = checkedState ? _palette.ElevatedSurfaceBg : _palette.SurfaceBg,
                    Cursor = Cursors.Hand
                };

                card.Click += (s, e) =>
                {
                    if (_selectedPeerKeys.Contains(key))
                        _selectedPeerKeys.Remove(key);
                    else
                        _selectedPeerKeys.Add(key);
                    RebuildPeerCards();
                    UpdateSendPillAppearance();
                };

                card.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    var r = new RectangleF(0.5F, 0.5F, card.Width - 1F, card.Height - 1F);
                    using (GraphicsPath gp = ThemeManager.CreateRoundedRectangle(r, 12F))
                    using (var b = new SolidBrush(card.BackColor))
                    using (var p = new Pen(checkedState ? _palette.AccentStart : _palette.BorderSoft, checkedState ? 1.5F : 1F))
                    {
                        e.Graphics.FillPath(b, gp);
                        e.Graphics.DrawPath(p, gp);
                    }

                    var avRect = new RectangleF(12, 11, 32, 32);
                    using (var avBrush = new SolidBrush(_palette.ElevatedSurfaceBg))
                    {
                        e.Graphics.FillEllipse(avBrush, avRect);
                    }
                    TextRenderer.DrawText(
                        e.Graphics,
                        GetInitials(peer.FullName),
                        ThemeManager.AvatarFont,
                        Rectangle.Round(avRect),
                        _palette.TextPrimary,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                    TextRenderer.DrawText(
                        e.Graphics,
                        peer.FullName ?? peer.IPv4,
                        ThemeManager.BodyFont,
                        new Rectangle(54, 8, card.Width - 96, 20),
                        _palette.TextPrimary,
                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

                    using (var dotB = new SolidBrush(_palette.StatusSuccess))
                    {
                        e.Graphics.FillEllipse(dotB, 54, 34, 7, 7);
                    }
                    TextRenderer.DrawText(
                        e.Graphics,
                        peer.Area ?? string.Empty,
                        ThemeManager.SecondaryFont,
                        new Rectangle(67, 29, card.Width - 110, 18),
                        _palette.TextSecondary,
                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

                    if (checkedState)
                    {
                        var chkRect = new RectangleF(card.Width - 34, 17, 20, 20);
                        using (var chkBrush = new SolidBrush(_palette.AccentStart))
                        {
                            e.Graphics.FillEllipse(chkBrush, chkRect);
                        }
                        using (var pen = new Pen(Color.White, 1.8F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        {
                            e.Graphics.DrawLine(pen, card.Width - 28, 27, card.Width - 25, 30);
                            e.Graphics.DrawLine(pen, card.Width - 25, 30, card.Width - 19, 24);
                        }
                    }
                };

                _pnlPeersList.Controls.Add(card);
            }

            _pnlPeersList.ResumeLayout();
        }

        private void RebuildQueueCards()
        {
            _pnlQueueList.SuspendLayout();
            _pnlQueueList.Controls.Clear();

            List<TransferJob> jobs = _queue.GetSnapshot();
            int completedCount = jobs.Count(j => j.State == QueueJobState.Completed);
            _lblSendingProgressTitle.Text = $"{completedCount} de {jobs.Count} completado";

            int cardWidth = Math.Max(220, _pnlQueueList.ClientSize.Width - 6);
            int yPos = 2;
            int gap = 8;

            foreach (TransferJob job in jobs)
            {
                string jobKey = BuildJobKey(job);
                bool isFailed = job.State == QueueJobState.Failed;
                bool isPaused = job.State == QueueJobState.Paused;
                bool isDone = job.State == QueueJobState.Completed;
                bool isRunning = job.State == QueueJobState.Running;
                bool isHovered = string.Equals(_hoveredQueueKey, jobKey, StringComparison.Ordinal);
                bool showTechDetail = _expandedQueueErrors.ContainsKey(jobKey);

                int cardHeight = isFailed ? (showTechDetail ? 106 : 78) : (isRunning ? 74 : 58);

                var card = new Panel
                {
                    Width = cardWidth,
                    Height = cardHeight,
                    Location = new Point(2, yPos),
                    BackColor = isFailed ? Color.FromArgb(24, _palette.StatusError) : _palette.SurfaceBg
                };

                string capturedKey = jobKey;
                card.MouseEnter += (s, e) => { _hoveredQueueKey = capturedKey; RebuildQueueCards(); };
                card.MouseLeave += (s, e) =>
                {
                    if (!card.ClientRectangle.Contains(card.PointToClient(Cursor.Position)))
                    {
                        _hoveredQueueKey = string.Empty;
                        RebuildQueueCards();
                    }
                };

                card.Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    var r = new RectangleF(0.5F, 0.5F, card.Width - 1F, card.Height - 1F);
                    using (GraphicsPath gp = ThemeManager.CreateRoundedRectangle(r, 12F))
                    using (var bg = new SolidBrush(card.BackColor))
                    using (var border = new Pen(isFailed ? _palette.StatusError : _palette.BorderSoft, 1F))
                    {
                        e.Graphics.FillPath(bg, gp);
                        e.Graphics.DrawPath(border, gp);
                    }

                    LineIconKind stIcon = isDone
                        ? LineIconKind.CheckCircle
                        : (isPaused ? LineIconKind.PauseIcon : (isFailed ? LineIconKind.ErrorCircle : LineIcons.DetectFileKind(job.ItemName)));
                    Color stColor = isDone
                        ? _palette.StatusSuccess
                        : (isPaused ? _palette.StatusWarning : (isFailed ? _palette.StatusError : _palette.AccentStart));
                    LineIcons.DrawIcon(e.Graphics, stIcon, new Rectangle(12, 14, 22, 22), stColor, 1.7F);

                    TextRenderer.DrawText(
                        e.Graphics,
                        job.ItemName ?? string.Empty,
                        ThemeManager.BodyFont,
                        new Rectangle(42, 8, card.Width - 140, 20),
                        _palette.TextPrimary,
                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

                    TextRenderer.DrawText(
                        e.Graphics,
                        job.TargetLabel ?? string.Empty,
                        ThemeManager.SecondaryFont,
                        new Rectangle(42, 27, card.Width - 140, 18),
                        _palette.TextSecondary,
                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

                    if (isDone)
                    {
                        TextRenderer.DrawText(
                            e.Graphics,
                            "Enviado",
                            ThemeManager.SecondaryFont,
                            new Rectangle(card.Width - 88, 16, 76, 20),
                            _palette.StatusSuccess,
                            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                    }
                    else if (job.State == QueueJobState.Queued)
                    {
                        TextRenderer.DrawText(
                            e.Graphics,
                            "En espera",
                            ThemeManager.SecondaryFont,
                            new Rectangle(card.Width - 88, 16, 76, 20),
                            _palette.TextSecondary,
                            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                    }
                    else if (isRunning || isPaused)
                    {
                        double pct = job.TotalBytes > 0 ? Math.Min(100.0, (job.SentBytes * 100.0) / job.TotalBytes) : 0.0;
                        int barY = 52;
                        int barW = card.Width - 24;
                        var track = new RectangleF(12, barY, barW, 4F);
                        using (GraphicsPath tPath = ThemeManager.CreateRoundedRectangle(track, 2F))
                        using (var tBrush = new SolidBrush(_palette.ElevatedSurfaceBg))
                        {
                            e.Graphics.FillPath(tBrush, tPath);
                        }

                        float fillW = (float)(barW * (pct / 100.0));
                        if (fillW > 2F)
                        {
                            var fillR = new RectangleF(12, barY, fillW, 4F);
                            using (GraphicsPath fPath = ThemeManager.CreateRoundedRectangle(fillR, 2F))
                            using (var fGrad = new LinearGradientBrush(fillR, _palette.AccentStart, _palette.AccentEnd, LinearGradientMode.Horizontal))
                            {
                                e.Graphics.FillPath(fGrad, fPath);
                            }
                        }

                        string speed = job.BytesPerSecond > 0 ? FormatBytes((long)job.BytesPerSecond) + "/s" : string.Empty;
                        string leftText = $"{(int)Math.Round(pct)}%{(string.IsNullOrEmpty(speed) ? string.Empty : " · " + speed)}";
                        string etaText = job.EstimatedRemaining.HasValue && isRunning
                            ? $"faltan {Math.Max(1, (int)job.EstimatedRemaining.Value.TotalSeconds)} s"
                            : (isPaused ? "En pausa" : string.Empty);

                        TextRenderer.DrawText(
                            e.Graphics,
                            leftText,
                            ThemeManager.SecondaryFont,
                            new Rectangle(12, 57, 170, 16),
                            _palette.TextSecondary,
                            TextFormatFlags.Left);

                        TextRenderer.DrawText(
                            e.Graphics,
                            etaText,
                            ThemeManager.SecondaryFont,
                            new Rectangle(card.Width - 130, 57, 118, 16),
                            _palette.TextSecondary,
                            TextFormatFlags.Right);
                    }
                    else if (isFailed)
                    {
                        string friendly = string.IsNullOrWhiteSpace(job.ErrorMessage)
                            ? "No se pudo completar el envío."
                            : job.ErrorMessage;
                        if (!string.IsNullOrWhiteSpace(job.SuggestedAction))
                            friendly += " " + job.SuggestedAction;

                        TextRenderer.DrawText(
                            e.Graphics,
                            friendly,
                            ThemeManager.SecondaryFont,
                            new Rectangle(12, 50, card.Width - 110, 22),
                            _palette.StatusError,
                            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

                        if (showTechDetail)
                        {
                            TextRenderer.DrawText(
                                e.Graphics,
                                $"Detalle: {job.ErrorMessage}",
                                ThemeManager.SecondaryFont,
                                new Rectangle(12, 76, card.Width - 24, 24),
                                _palette.TextSecondary,
                                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                        }
                    }
                };

                if ((isRunning || isPaused) && isHovered)
                {
                    var btnPause = new Button
                    {
                        Text = isPaused ? "▶" : "||",
                        Size = new Size(28, 24),
                        Location = new Point(card.Width - 72, 12)
                    };
                    ThemeManager.ConfigureSmoothButton(btnPause, _palette, ButtonVariant.Secondary, 8);
                    btnPause.Click += (s, e) =>
                    {
                        if (job.State == QueueJobState.Paused) _queue.ResumeAll();
                        else _queue.PauseAll();
                    };

                    var btnCancel = new Button
                    {
                        Text = "×",
                        Size = new Size(28, 24),
                        Location = new Point(card.Width - 40, 12)
                    };
                    ThemeManager.ConfigureSmoothButton(btnCancel, _palette, ButtonVariant.Secondary, 8);
                    btnCancel.Click += (s, e) => _queue.CancelAllActive();

                    card.Controls.AddRange(new Control[] { btnPause, btnCancel });
                }

                if (isFailed)
                {
                    var btnRetry = new Button
                    {
                        Text = "Reintentar",
                        Size = new Size(80, 24),
                        Location = new Point(card.Width - 92, 12)
                    };
                    ThemeManager.ConfigureSmoothButton(btnRetry, _palette, ButtonVariant.Secondary, 8);
                    btnRetry.Click += (s, e) => _queue.RetryFailedJobs();
                    card.Controls.Add(btnRetry);

                    var lnkTech = new LinkLabel
                    {
                        Text = showTechDetail ? "ocultar detalle" : "ver detalle técnico",
                        Font = ThemeManager.SecondaryFont,
                        LinkColor = _palette.TextSecondary,
                        LinkBehavior = LinkBehavior.HoverUnderline,
                        Location = new Point(card.Width - 122, 51),
                        AutoSize = true
                    };
                    lnkTech.Click += (s, e) =>
                    {
                        if (_expandedQueueErrors.ContainsKey(capturedKey))
                            _expandedQueueErrors.Remove(capturedKey);
                        else
                            _expandedQueueErrors[capturedKey] = job.ErrorMessage ?? string.Empty;
                        RebuildQueueCards();
                    };
                    card.Controls.Add(lnkTech);
                }

                _pnlQueueList.Controls.Add(card);
                yPos += cardHeight + gap;
            }

            _pnlQueueList.ResumeLayout();
        }

        private void AddManualPeer()
        {
            using (var dlg = new ManualPeerDialog())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.CreatedPeer != null)
                {
                    PeerInfo peer = dlg.CreatedPeer;
                    if (dlg.SaveToAddressBook)
                        AddressBookStore.UpsertContact(peer, peer.Area);

                    _discovery.UpsertPeer(peer.FullName, peer.Area, peer.IPv4, peer.MachineName, peer.FingerprintSha256);
                    _selectedPeerKeys.Add(PeerContactsStore.BuildContactKey(peer));
                    RebuildAreaChips();
                    RebuildPeerCards();
                    UpdateSendPillAppearance();
                }
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            bool valid = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effect = valid ? DragDropEffects.Copy : DragDropEffects.None;
            if (valid && !_isDragHovering)
            {
                _isDragHovering = true;
                _pnlDropZone.Invalidate();
            }
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void OnDragLeave(object sender, EventArgs e)
        {
            _isDragHovering = false;
            _pnlDropZone.Invalidate
