
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
        private readonly System.Windows.Forms.Timer _fadeInTimer;
        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _trayMenu;
        private readonly ContextMenuStrip _addMenu;
        private readonly ContextMenuStrip _moreMenu;

        private PeerInfo _localProfile;
        private ThemePalette _palette;
        private long _cachedSelectionBytes;
        private int _lastSeenUnseenCount = -1;
        private string _lastShownNotificationId;
        private string _lastTechnicalError = string.Empty;

        private readonly Label _lblIdentity;
        private readonly Label _lblInboxPath;
        private readonly Label _lblStatus;
        private readonly Label _lblSelectionSize;
        private readonly ComboBox _cmbGroupOrArea;
        private readonly Button _btnSelectGroup;
        private readonly Button _btnSelectAllPeers;
        private readonly Button _btnAddManualPeer;
        private readonly CheckedListBox _clbPeers;
        private readonly ListBox _lstFiles;
        private readonly ListBox _lstQueue;
        private readonly Button _btnAddItem;
        private readonly Button _btnRemoveItem;
        private readonly Button _btnSend;
        private readonly Button _btnDetails;
        private readonly Button _btnMore;
        private readonly Button _btnPauseResume;
        private readonly Button _btnCancelQueue;
        private readonly Button _btnRetryFailed;
        private readonly Button _btnNotifications;
        private readonly Button _btnInbox;
        private readonly Button _btnHistory;

        public MainForm(PeerInfo profile)
        {
            _localProfile = profile;
            _queue.SetLocalProfile(_localProfile);
            _palette = ThemeManager.GetCurrentPalette();

            Text = UiResources.AppTitle;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Size = new Size(920, 680);
            MinimumSize = new Size(760, 560);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            AllowDrop = true;
            Opacity = 0.0;
            BackColor = _palette.WindowBg;
            ForeColor = _palette.TextPrimary;
            Font = ThemeManager.BaseFont;

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

            // Cabecera limpia con acciones clave y menú desplegable
            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 66,
                BackColor = _palette.HeaderBg,
                Padding = new Padding(16, 8, 16, 8)
            };

            var lblTitle = new Label
            {
                Text = UiResources.AppTitle,
                Font = ThemeManager.HeaderTitleFont,
                ForeColor = _palette.PrimaryAccent,
                Location = new Point(16, 8),
                AutoSize = true
            };

            _lblIdentity = new Label
            {
                Text = BuildIdentityText(),
                ForeColor = _palette.TextSecondary,
                Location = new Point(17, 36),
                AutoSize = true
            };

            _moreMenu = new ContextMenuStrip();
            _moreMenu.Items.Add("Mi perfil", null, (s, e) => OpenProfileWizard());
            _moreMenu.Items.Add("Configuración y diagnóstico", null, (s, e) => OpenSettings());
            var themeSub = new ToolStripMenuItem("Tema visual");
            themeSub.DropDownItems.Add("Oscuro", null, (s, e) => ApplyTheme(AppThemeMode.Dark));
            themeSub.DropDownItems.Add("Claro", null, (s, e) => ApplyTheme(AppThemeMode.Light));
            themeSub.DropDownItems.Add("Alto contraste", null, (s, e) => ApplyTheme(AppThemeMode.HighContrast));
            _moreMenu.Items.Add(themeSub);
            _moreMenu.Items.Add("Ayuda y atajos (F1)", null, (s, e) => ShowHelpDialog());

            var topButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(0, 6, 0, 0)
            };

            _btnHistory = new Button { Text = "Historial", Size = new Size(88, 32), AccessibleName = "Abrir historial" };
            ThemeManager.StyleButton(_btnHistory, _palette);
            _btnHistory.Click += (s, e) =>
            {
                using (var dlg = new TransferHistoryForm())
                    dlg.ShowDialog(this);
                RefreshInboxBadgeText(force: true);
            };

            _btnNotifications = new Button { Text = "Avisos", Size = new Size(92, 32), AccessibleName = "Abrir centro de avisos" };
            ThemeManager.StyleButton(_btnNotifications, _palette);
            _btnNotifications.Click += (s, e) =>
            {
                using (var dlg = new NotificationCenterForm())
                    dlg.ShowDialog(this);
                RefreshInboxBadgeText(force: true);
            };

            _btnInbox = new Button { Text = "Recibidos", Size = new Size(106, 32), AccessibleName = "Abrir carpeta de recibidos" };
            ThemeManager.StyleButton(_btnInbox, _palette);
            _btnInbox.Click += (s, e) => OpenInboxFolder();

            _btnMore = new Button { Text = "Más ▾", Size = new Size(76, 32), AccessibleName = "Más opciones y tema" };
            ThemeManager.StyleButton(_btnMore, _palette);
            _btnMore.Click += (s, e) => _moreMenu.Show(_btnMore, new Point(0, _btnMore.Height + 4));

            topButtons.Controls.AddRange(new Control[] { _btnHistory, _btnNotifications, _btnInbox, _btnMore });
            pnlTop.Controls.Add(topButtons);
            pnlTop.Controls.Add(lblTitle);
            pnlTop.Controls.Add(_lblIdentity);

            // Grilla principal en 3 pasos esenciales
            var mainGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(16, 10, 16, 10)
            };
            mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 32F));
            mainGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 32F));
            mainGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 36F));

            // Paso 1: Botón único "+ Agregar" (archivo o carpeta)
            _addMenu = new ContextMenuStrip();
            _addMenu.Items.Add("Seleccionar archivos...", null, (s, e) => PickFilesDialog());
            _addMenu.Items.Add("Seleccionar carpeta...", null, (s, e) => PickFolderDialog());

            var step1Header = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 2, 0, 6) };
            step1Header.Controls.Add(new Label
            {
                Text = UiResources.Step1Title,
                Font = ThemeManager.SectionTitleFont,
                ForeColor = _palette.PrimaryAccent,
                AutoSize = true,
                Padding = new Padding(0, 6, 12, 0)
            });

            _btnAddItem = new Button { Text = UiResources.BtnAddItem, Size = new Size(105, 32), AccessibleName = "Agregar archivo o carpeta" };
            ThemeManager.StyleButton(_btnAddItem, _palette, isPrimary: true);
            _btnAddItem.Click += (s, e) => _addMenu.Show(_btnAddItem, new Point(0, _btnAddItem.Height + 4));

            _btnRemoveItem = new Button { Text = UiResources.BtnRemoveItem, Size = new Size(84, 32), AccessibleName = "Quitar seleccionados" };
            ThemeManager.StyleButton(_btnRemoveItem, _palette);
            _btnRemoveItem.Click += BtnRemoveItem_Click;

            _lblSelectionSize = new Label
            {
                Text = "0 elementos · 0 B",
                ForeColor = _palette.TextSecondary,
                AutoSize = true,
                Padding = new Padding(10, 8, 0, 0)
            };

            step1Header.Controls.AddRange(new Control[] { _btnAddItem, _btnRemoveItem, _lblSelectionSize });

            _lstFiles = new ListBox
            {
                Dock = DockStyle.Fill,
                SelectionMode = SelectionMode.MultiExtended,
                BackColor = _palette.CardBg,
                ForeColor = _palette.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = "Archivos y carpetas a enviar"
            };

            // Paso 2: Destinatarios simplificado
            var step2Header = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 10, 0, 6) };
            step2Header.Controls.Add(new Label
            {
                Text = UiResources.Step2Title,
                Font = ThemeManager.SectionTitleFont,
                ForeColor = _palette.PrimaryAccent,
                AutoSize = true,
                Padding = new Padding(0, 6, 12, 0)
            });

            _btnAddManualPeer = new Button { Text = UiResources.BtnAddManualPeer, Size = new Size(128, 32), AccessibleName = "Agregar destinatario por IP" };
            ThemeManager.StyleButton(_btnAddManualPeer, _palette);
            _btnAddManualPeer.Click += BtnAddManualPeer_Click;

            _btnSelectAllPeers = new Button { Text = UiResources.BtnSelectAllPeers, Size = new Size(76, 32), AccessibleName = "Seleccionar todos" };
            ThemeManager.StyleButton(_btnSelectAllPeers, _palette);
            _btnSelectAllPeers.Click += BtnSelectAllPeers_Click;

            _cmbGroupOrArea = new ComboBox
            {
                Width = 165,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = _palette.InputBg,
                ForeColor = _palette.TextPrimary,
                AccessibleName = "Área o grupo"
            };
            step2Header.Controls.Add(_cmbGroupOrArea);

            _btnSelectGroup = new Button { Text = UiResources.BtnSelectByGroup, Size = new Size(112, 32), AccessibleName = "Marcar área o grupo" };
            ThemeManager.StyleButton(_btnSelectGroup, _palette);
            _btnSelectGroup.Click += BtnSelectGroup_Click;

            step2Header.Controls.AddRange(new Control[] { _btnAddManualPeer, _btnSelectAllPeers, _btnSelectGroup });

            _clbPeers = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                BackColor = _palette.CardBg,
                ForeColor = _palette.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = "Equipos en red"
            };
            _clbPeers.ItemCheck += (s, e) => BeginInvoke(new Action(UpdateSendButtonState));

            // Paso 3: Cola limpia
            var step3Header = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 10, 0, 6) };
            step3Header.Controls.Add(new Label
            {
                Text = UiResources.Step3Title,
                Font = ThemeManager.SectionTitleFont,
                ForeColor = _palette.PrimaryAccent,
                AutoSize = true,
                Padding = new Padding(0, 5, 12, 0)
            });

            _btnPauseResume = new Button { Text = "Pausar", Size = new Size(92, 30), AccessibleName = "Pausar o reanudar" };
            ThemeManager.StyleButton(_btnPauseResume, _palette);
            _btnPauseResume.Click += BtnPauseResume_Click;

            _btnCancelQueue = new Button { Text = "Cancelar", Size = new Size(92, 30), AccessibleName = "Cancelar envío" };
            ThemeManager.StyleButton(_btnCancelQueue, _palette, isDanger: true);
            _btnCancelQueue.Click += (s, e) => _queue.CancelAllActive();

            _btnRetryFailed = new Button { Text = "Reintentar", Size = new Size(102, 30), AccessibleName = "Reintentar fallidos" };
            ThemeManager.StyleButton(_btnRetryFailed, _palette);
            _btnRetryFailed.Click += (s, e) => _queue.RetryFailedJobs();

            step3Header.Controls.AddRange(new Control[] { _btnPauseResume, _btnCancelQueue, _btnRetryFailed });

            _lstQueue = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = _palette.CardBg,
                ForeColor = _palette.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = "Cola de transferencias"
            };

            mainGrid.Controls.Add(step1Header, 0, 0);
            mainGrid.Controls.Add(_lstFiles, 0, 1);
            mainGrid.Controls.Add(step2Header, 0, 2);
            mainGrid.Controls.Add(_clbPeers, 0, 3);
            mainGrid.Controls.Add(step3Header, 0, 4);
            mainGrid.Controls.Add(_lstQueue, 0, 5);

            // Barra inferior concisa
            var pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 76,
                BackColor = _palette.HeaderBg,
                Padding = new Padding(16, 10, 16, 10)
            };

            _lblStatus = new Label
            {
                Text = "Listo · TLS 1.2+ · Avisos visuales sin sonido",
                Location = new Point(16, 12),
                Size = new Size(540, 20),
                ForeColor = _palette.TextPrimary
            };

            _lblInboxPath = new Label
            {
                Text = "Destino: " + AppSettingsStore.GetInboxFolder(),
                Location = new Point(16, 38),
                Size = new Size(540, 20),
                ForeColor = _palette.TextSecondary
            };

            var actionDock = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(0, 10, 0, 0)
            };

            _btnDetails = new Button { Text = "Detalles", Size = new Size(92, 38), Visible = false, AccessibleName = "Ver detalle" };
            ThemeManager.StyleButton(_btnDetails, _palette);
            _btnDetails.Click += BtnDetails_Click;

            _btnSend = new Button { Text = UiResources.SendNeedFiles, Size = new Size(255, 38), AccessibleName = "Enviar" };
            ThemeManager.StyleButton(_btnSend, _palette, isPrimary: true);
            _btnSend.Click += BtnSend_Click;

            actionDock.Controls.Add(_btnDetails);
            actionDock.Controls.Add(_btnSend);
            pnlBottom.Controls.Add(actionDock);
            pnlBottom.Controls.Add(_lblStatus);
            pnlBottom.Controls.Add(_lblInboxPath);

            // Bandeja del sistema
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Abrir " + UiResources.AppTitle, null, (s, e) => BringToFrontFromSecondInstance());
            _trayMenu.Items.Add("Abrir Recibidos", null, (s, e) => OpenInboxFolder());
            _trayMenu.Items.Add("Centro de Avisos", null, (s, e) =>
            {
                using (var dlg = new NotificationCenterForm())
                    dlg.ShowDialog(this);
            });
            _trayMenu.Items.Add("Configuración", null, (s, e) => OpenSettings());

            _trayIcon = new NotifyIcon
            {
                Icon = loadedIcon ?? SystemIcons.Application,
                Text = UiResources.AppTitle,
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _trayIcon.DoubleClick += (s, e) => BringToFrontFromSecondInstance();

            _fadeInTimer = new System.Windows.Forms.Timer { Interval = 20 };
            _fadeInTimer.Tick += OnFadeInTick;

            _liveSyncTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            _liveSyncTimer.Tick += OnLiveSyncTimerTick;

            _discovery.PeersUpdated += OnDiscoveryPeersUpdated;
            _discovery.NewPeerDiscovered += OnNewPeerDiscovered;
            PeerAuthorizationStore.PendingRequestReceived += OnPendingAuthorizationReceived;
            _queue.QueueChanged += OnQueueChanged;
            _queue.BatchCompleted += OnQueueBatchCompleted;

            KeyDown += OnMainFormKeyDown;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            Load += OnMainFormLoad;
            FormClosed += OnMainFormClosed;

            Controls.Add(mainGrid);
            Controls.Add(pnlBottom);
            Controls.Add(pnlTop);

            UpdateSendButtonState();
        }

        private void OnFadeInTick(object sender, EventArgs e)
        {
            if (IsDisposed || Disposing)
                return;

            if (Opacity < 1.0)
                Opacity = Math.Min(1.0, Opacity + 0.14);
            else
                _fadeInTimer.Stop();
        }

        private void ApplyTheme(AppThemeMode mode)
        {
            ThemeManager.SaveMode(mode);
            _palette = ThemeManager.GetPalette(mode);
            BackColor = _palette.WindowBg;
            ForeColor = _palette.TextPrimary;
            _lstFiles.BackColor = _palette.CardBg;
            _lstFiles.ForeColor = _palette.TextPrimary;
            _clbPeers.BackColor = _palette.CardBg;
            _clbPeers.ForeColor = _palette.TextPrimary;
            _lstQueue.BackColor = _palette.CardBg;
            _lstQueue.ForeColor = _palette.TextPrimary;
            _cmbGroupOrArea.BackColor = _palette.InputBg;
            _cmbGroupOrArea.ForeColor = _palette.TextPrimary;
            _lblStatus.ForeColor = _palette.TextPrimary;
            _lblInboxPath.ForeColor = _palette.TextSecondary;
            _lblSelectionSize.ForeColor = _palette.TextSecondary;
            _lblIdentity.ForeColor = _palette.TextSecondary;

            ThemeManager.StyleButton(_btnAddItem, _palette, isPrimary: true);
            ThemeManager.StyleButton(_btnRemoveItem, _palette);
            ThemeManager.StyleButton(_btnAddManualPeer, _palette);
            ThemeManager.StyleButton(_btnSelectAllPeers, _palette);
            ThemeManager.StyleButton(_btnSelectGroup, _palette);
            ThemeManager.StyleButton(_btnPauseResume, _palette);
            ThemeManager.StyleButton(_btnCancelQueue, _palette, isDanger: true);
            ThemeManager.StyleButton(_btnRetryFailed, _palette);
            ThemeManager.StyleButton(_btnDetails, _palette);
            ThemeManager.StyleButton(_btnSend, _palette, isPrimary: true);
            ThemeManager.StyleButton(_btnHistory, _palette);
            ThemeManager.StyleButton(_btnNotifications, _palette);
            ThemeManager.StyleButton(_btnInbox, _palette);
            ThemeManager.StyleButton(_btnMore, _palette);
            SetFriendlyStatus($"Tema cambiado a {mode}.");
        }

        private void OnMainFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.O)
            {
                e.Handled = true;
                PickFilesDialog();
            }
            else if (e.KeyCode == Keys.Delete && _lstFiles.Focused)
            {
                e.Handled = true;
                BtnRemoveItem_Click(this, EventArgs.Empty);
            }
            else if (e.Control && e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                BtnSend_Click(this, EventArgs.Empty);
            }
            else if (e.KeyCode == Keys.F1)
            {
                e.Handled = true;
                ShowHelpDialog();
            }
        }

        private void ShowHelpDialog()
        {
            MessageBox.Show(this, UiResources.HelpMessage, "Ayuda — " + UiResources.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnMainFormLoad(object sender, EventArgs e)
        {
            RefreshInboxBadgeText(force: true);
            _queue.EnsureStarted();
            _discovery.Start(_localProfile);
            RefreshGroupsCombo();
            RefreshPeersList();
            RefreshQueueList();
            UpdateTrayTooltip();
            _fadeInTimer.Start();
            _liveSyncTimer.Start();
        }

        private void OnMainFormClosed(object sender, FormClosedEventArgs e)
        {
            _fadeInTimer.Stop();
            _fadeInTimer.Dispose();
            _liveSyncTimer.Stop();
            _liveSyncTimer.Dispose();

            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayMenu.Dispose();
            _addMenu.Dispose();
            _moreMenu.Dispose();

            try { _uiCts.Cancel(); }
            catch (ObjectDisposedException) { }

            _discovery.PeersUpdated -= OnDiscoveryPeersUpdated;
            _discovery.NewPeerDiscovered -= OnNewPeerDiscovered;
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

            RefreshInboxBadgeText(force: false);
            CheckNewUnreadNotifications();
            UpdateTrayTooltip();
        }

        private void UpdateTrayTooltip()
        {
            string svc = "Activo";
            try
            {
                using (var sc = new ServiceController("ONKOSTransferService"))
                {
                    if (sc.Status != ServiceControllerStatus.Running)
                        svc = sc.Status.ToString();
                }
            }
            catch (Exception)
            {
            }

            int unseen = Math.Max(0, _lastSeenUnseenCount);
            string tip = $"{UiResources.AppTitle} · {svc} · Nuevos: {unseen}";
            _trayIcon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        private void CheckNewUnreadNotifications()
        {
            List<NotificationRecord> unread = NotificationCenterStore.GetAll(onlyUnread: true);
            int unreadCount = unread.Count;
            _btnNotifications.Text = unreadCount > 0 ? $"Avisos ({unreadCount})" : "Avisos";

            if (unread.Count == 0)
                return;

            NotificationRecord latest = unread[0];
            if (string.Equals(latest.Id, _lastShownNotificationId, StringComparison.Ordinal))
                return;

            _lastShownNotificationId = latest.Id;
            NotificationPreferences prefs = NotificationCenterStore.LoadPreferences();
            if (NotificationCenterStore.IsCategoryEnabled(prefs, latest.Category))
            {
                var toast = new VisualToastForm(latest, prefs.ToastDurationSeconds);
                toast.Show();
            }
        }

        private void OnDiscoveryPeersUpdated()
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => { RefreshGroupsCombo(); RefreshPeersList(); })); }
                catch (ObjectDisposedException) { }
                return;
            }

            RefreshGroupsCombo();
            RefreshPeersList();
        }

        private void OnNewPeerDiscovered(PeerInfo peer)
        {
            if (IsDisposed || Disposing || peer == null)
                return;

            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => SetFriendlyStatus($"Nuevo en red: {peer.FullName} ({peer.IPv4})"))); }
                catch (ObjectDisposedException) { }
                return;
            }

            SetFriendlyStatus($"Nuevo en red: {peer.FullName} ({peer.IPv4})");
        }

        private void OnPendingAuthorizationReceived(AuthorizedPeerRecord record)
        {
            if (IsDisposed || Disposing || record == null)
                return;

            NotificationCenterStore.PublishGrouped(
                NotificationCategory.AuthorizationRequest,
                "auth-" + record.FingerprintSha256,
                "Autorización pendiente",
                $"{record.DisplayName} ({record.LastIp})",
                $"Huella: {record.FingerprintSha256}");

            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => SetFriendlyStatus($"Autorización pendiente: {record.DisplayName}"))); }
                catch (ObjectDisposedException) { }
                return;
            }

            SetFriendlyStatus($"Autorización pendiente: {record.DisplayName}");
        }

        private void OnQueueChanged()
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(RefreshQueueList)); }
                catch (ObjectDisposedException) { }
                return;
            }

            RefreshQueueList();
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
                "Envío finalizado",
                $"{ok} ok · {fail} con incidencia",
                $"Lote: {snapshot.Count} elemento(s)");

            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => ShowBatchSummary(snapshot))); }
                catch (ObjectDisposedException) { }
                return;
            }

            ShowBatchSummary(snapshot);
        }

        private void ShowBatchSummary(List<TransferJob> snapshot)
        {
            int ok = snapshot.Count(j => j.State == QueueJobState.Completed);
            int fail = snapshot.Count(j => j.State == QueueJobState.Failed || j.State == QueueJobState.Cancelled);

            string msg = $"Completados: {ok} · Fallidos/cancelados: {fail}";
            if (fail > 0)
            {
                TransferJob firstFail = snapshot.FirstOrDefault(j => j.State == QueueJobState.Failed || j.State == QueueJobState.Cancelled);
                if (firstFail != null)
                {
                    _lastTechnicalError = $"Destino: {firstFail.TargetLabel} | Elemento: {firstFail.ItemName} | Detalle: {firstFail.ErrorMessage}";
                    _btnDetails.Visible = true;
                    if (!string.IsNullOrWhiteSpace(firstFail.SuggestedAction))
                        msg += " · " + firstFail.SuggestedAction;
                }
            }

            SetFriendlyStatus(msg);
        }

        private void SetFriendlyStatus(string message)
        {
            if (IsDisposed || Disposing)
                return;
            _lblStatus.Text = message;
        }

        private void SetActionableError(string friendlyMessage, Exception ex)
        {
            _lastTechnicalError = ex != null
                ? $"{ex.GetType().Name}: {ex.Message}\r\n{ex.StackTrace}"
                : friendlyMessage;
            _btnDetails.Visible = true;
            SetFriendlyStatus(friendlyMessage);
        }

        private void BtnDetails_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_lastTechnicalError))
                return;

            MessageBox.Show(
                this,
                _lastTechnicalError + "\r\n\r\nLogs: " + FileLogger.Instance.LogsFolder,
                "Detalle técnico",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void RefreshGroupsCombo()
        {
            if (IsDisposed || Disposing)
                return;

            string current = _cmbGroupOrArea.SelectedItem != null ? _cmbGroupOrArea.SelectedItem.ToString() : null;
            List<string> groups = AddressBookStore.GetGroupsAndAreas(_discovery.GetPeers());

            _cmbGroupOrArea.BeginUpdate();
            _cmbGroupOrArea.Items.Clear();
            foreach (string g in groups)
                _cmbGroupOrArea.Items.Add(g);

            if (!string.IsNullOrEmpty(current))
            {
                int idx = _cmbGroupOrArea.Items.IndexOf(current);
                if (idx >= 0)
                    _cmbGroupOrArea.SelectedIndex = idx;
            }
            else if (_cmbGroupOrArea.Items.Count > 0)
            {
                _cmbGroupOrArea.SelectedIndex = 0;
            }
            _cmbGroupOrArea.EndUpdate();
        }

        private void RefreshPeersList()
        {
            if (IsDisposed || Disposing)
                return;

            var checkedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object checkedObj in _clbPeers.CheckedItems)
            {
                if (checkedObj is PeerListItem pli && pli.Peer != null)
                    checkedKeys.Add(PeerContactsStore.BuildContactKey(pli.Peer));
            }

            List<PeerInfo> peers = _discovery.GetPeers();
            _clbPeers.BeginUpdate();
            _clbPeers.Items.Clear();

            foreach (PeerInfo p in peers)
            {
                var item = new PeerListItem(p);
                string key = PeerContactsStore.BuildContactKey(p);
                bool isChecked = checkedKeys.Contains(key);
                _clbPeers.Items.Add(item, isChecked);
            }

            _clbPeers.EndUpdate();
            UpdateSendButtonState();
        }

        private void RefreshQueueList()
        {
            if (IsDisposed || Disposing)
                return;

            List<TransferJob> jobs = _queue.GetSnapshot();
            _btnPauseResume.Text = _queue.IsPaused ? "Reanudar" : "Pausar";

            _lstQueue.BeginUpdate();
            _lstQueue.Items.Clear();

            foreach (TransferJob j in jobs)
            {
                double pct = j.TotalBytes > 0
                    ? Math.Min(100.0, (j.SentBytes * 100.0) / j.TotalBytes)
                    : (j.State == QueueJobState.Completed ? 100.0 : 0.0);

                _lstQueue.Items.Add($"{j.ItemName} -> {j.TargetLabel} · {pct:0}% · {j.State}");
            }

            _lstQueue.EndUpdate();
            UpdateSendButtonState();
        }

        private void BtnAddManualPeer_Click(object sender, EventArgs e)
        {
            using (var dlg = new ManualPeerDialog())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.CreatedPeer != null)
                {
                    PeerInfo peer = dlg.CreatedPeer;
                    if (dlg.SaveToAddressBook)
                        AddressBookStore.UpsertContact(peer, peer.Area);

                    _discovery.UpsertPeer(peer.FullName, peer.Area, peer.IPv4, peer.MachineName, peer.FingerprintSha256);
                    RefreshPeersList();

                    for (int i = 0; i < _clbPeers.Items.Count; i++)
                    {
                        if (_clbPeers.Items[i] is PeerListItem pli && pli.Peer != null &&
                            string.Equals(pli.Peer.IPv4, peer.IPv4, StringComparison.OrdinalIgnoreCase))
                        {
                            _clbPeers.SetItemChecked(i, true);
                            break;
                        }
                    }

                    UpdateSendButtonState();
                    SetFriendlyStatus($"Agregado: {peer.FullName} ({peer.IPv4})");
                }
            }
        }

        private void BtnSelectAllPeers_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < _clbPeers.Items.Count; i++)
                _clbPeers.SetItemChecked(i, true);
            UpdateSendButtonState();
        }

        private void BtnSelectGroup_Click(object sender, EventArgs e)
        {
            string groupName = _cmbGroupOrArea.SelectedItem != null ? _cmbGroupOrArea.SelectedItem.ToString() : string.Empty;
            if (string.IsNullOrWhiteSpace(groupName))
                return;

            List<AddressBookEntry> book = AddressBookStore.LoadAll();
            var groupIps = new HashSet<string>(
                book.Where(b => string.Equals(b.GroupName, groupName, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(b.Area, groupName, StringComparison.OrdinalIgnoreCase))
                    .Select(b => b.IPv4 ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

            int marked = 0;
            for (int i = 0; i < _clbPeers.Items.Count; i++)
            {
                if (_clbPeers.Items[i] is PeerListItem item && item.Peer != null)
                {
                    bool matchArea = string.Equals(item.Peer.Area, groupName, StringComparison.OrdinalIgnoreCase);
                    bool matchBook = !string.IsNullOrWhiteSpace(item.Peer.IPv4) && groupIps.Contains(item.Peer.IPv4.Trim());
                    if (matchArea || matchBook)
                    {
                        _clbPeers.SetItemChecked(i, true);
                        marked++;
                    }
                }
            }

            UpdateSendButtonState();
            SetFriendlyStatus(marked > 0
                ? $"Marcados {marked} de '{groupName}'"
                : $"Sin activos en '{groupName}'");
        }

        private void UpdateSendButtonState()
        {
            if (IsDisposed || Disposing)
                return;

            int fileCount = _lstFiles.Items.Count;
            int peerCount = _clbPeers.CheckedItems.Count;

            if (fileCount == 0)
            {
                _btnSend.Enabled = false;
                _btnSend.Text = UiResources.SendNeedFiles;
                return;
            }

            if (peerCount == 0)
            {
                _btnSend.Enabled = false;
                _btnSend.Text = UiResources.SendNeedTargets;
                return;
            }

            _btnSend.Enabled = true;
            _btnSend.Text = $"{UiResources.SendReady} ({fileCount} · {peerCount})";
        }

        private void BtnPauseResume_Click(object sender, EventArgs e)
        {
            if (_queue.IsPaused)
                _queue.ResumeAll();
            else
                _queue.PauseAll();
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
            string ip = _discovery.GetLocalIPv4Cached();
            return $"{name} · {area} · {ip}";
        }

        private void RefreshInboxBadgeText(bool force)
        {
            int unseen = TransferHistoryStore.GetUnseenReceivedCount();
            if (force || unseen != _lastSeenUnseenCount)
            {
                _lastSeenUnseenCount = unseen;
                _btnInbox.Text = unseen > 0 ? $"Recibidos ({unseen})" : "Recibidos";
            }

            _lblInboxPath.Text = "Destino: " + AppSettingsStore.GetInboxFolder();
        }

        private void OpenInboxFolder()
        {
            try
            {
                string folder;
                string err;
                if (!AppSettingsStore.TryEnsureInboxReady(out folder, out err))
                {
                    SetFriendlyStatus("Revisa la carpeta de recibidos o sus permisos.");
                    return;
                }

                TransferHistoryStore.MarkReceivedAsSeen();
                RefreshInboxBadgeText(force: true);
                Process.Start("explorer.exe", folder);
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("UI.MainForm", $"Error al abrir carpeta de recibidos ({ex.Message}).");
                SetActionableError("No se pudo abrir la carpeta de recibidos.", ex);
            }
        }

        private void OpenSettings()
        {
            using (var dlg = new SettingsForm())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    ApplyTheme(dlg.SelectedTheme);
                    RefreshInboxBadgeText(force: true);
                    SetFriendlyStatus("Configuración guardada.");
                }
            }
        }

        private void OpenProfileWizard()
        {
            using (var dlg = new FirstRunWizardForm())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.RegisteredProfile != null)
                {
                    _localProfile = dlg.RegisteredProfile;
                    _queue.SetLocalProfile(_localProfile);
                    _discovery.UpdateLocalProfile(_localProfile);
                    _lblIdentity.Text = BuildIdentityText();
                    SetFriendlyStatus("Perfil actualizado.");
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
                    _ = AddPathsAsync(dropped);
            }
        }

        private void PickFilesDialog()
        {
            using (var ofd = new OpenFileDialog { Multiselect = true, Title = "Seleccionar archivos" })
            {
                if (ofd.ShowDialog(this) == DialogResult.OK)
                    _ = AddPathsAsync(ofd.FileNames);
            }
        }

        private void PickFolderDialog()
        {
            using (var fbd = new FolderBrowserDialog { Description = "Seleccionar carpeta" })
            {
                if (fbd.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                    _ = AddPathsAsync(new[] { fbd.SelectedPath });
            }
        }

        private async Task AddPathsAsync(IEnumerable<string> paths)
        {
            try
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

                UpdateSendButtonState();
                await RecalculateSelectionSizeAsync().ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("UI.MainForm", $"Error calculando tamaños ({ex.Message}).");
            }
        }

        private void BtnRemoveItem_Click(object sender, EventArgs e)
        {
            var selected = _lstFiles.SelectedItems.Cast<string>().ToList();
            foreach (string item in selected)
            {
                PathSizeCache.Invalidate(item);
                _lstFiles.Items.Remove(item);
            }
            UpdateSendButtonState();
            _ = RecalculateSelectionSizeAsync();
        }

        private async Task RecalculateSelectionSizeAsync()
        {
            List<string> items = _lstFiles.Items.Cast<string>().ToList();
            long total = 0L;
            foreach (string p in items)
            {
                PathSizeSummary summary = await PathSizeCache.GetOrComputeAsync(p, _uiCts.Token).ConfigureAwait(true);
                total += summary.TotalBytes;
            }

            _cachedSelectionBytes = total;
            if (!IsDisposed && !Disposing)
                _lblSelectionSize.Text = $"{items.Count} elementos · {FormatBytes(_cachedSelectionBytes)}";
        }

        private async void BtnSend_Click(object sender, EventArgs e)
        {
            try
            {
                List<PeerInfo> targets = CollectCheckedTargets();
                if (targets.Count == 0)
                {
                    SetFriendlyStatus("Elige al menos un destinatario.");
                    return;
                }

                List<string> items = _lstFiles.Items.Cast<string>().ToList();
                if (items.Count == 0)
                {
                    SetFriendlyStatus("Agrega al menos un archivo o carpeta.");
                    return;
                }

                foreach (PeerInfo t in targets)
                    AddressBookStore.UpsertContact(t);

                List<TransferJob> jobs = _queue.EnqueueBatch(items, targets);
                _lstFiles.Items.Clear();
                _cachedSelectionBytes = 0L;
                UpdateSendButtonState();
                await RecalculateSelectionSizeAsync().ConfigureAwait(true);

                _btnDetails.Visible = false;
                SetFriendlyStatus($"Encolados {jobs.Count} envío(s).");
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Error("UI.Send", "Error al encolar transferencia.", ex);
                SetActionableError("No se pudo iniciar el envío.", ex);
            }
        }

        private List<PeerInfo> CollectCheckedTargets()
        {
            var result = new List<PeerInfo>();
            var seenIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (object checkedObj in _clbPeers.CheckedItems)
            {
                if (checkedObj is PeerListItem pli && pli.Peer != null && !string.IsNullOrWhiteSpace(pli.Peer.IPv4))
                {
                    string ip = pli.Peer.IPv4.Trim();
                    if (seenIps.Add(ip))
                        result.Add(pli.Peer);
                }
            }

            return result;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024L) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024.0) return kb.ToString("0.#") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024.0) return mb.ToString("0.#") + " MB";
            return (mb / 1024.0).ToString("0.##") + " GB";
        }

        private sealed class PeerListItem
        {
            public PeerInfo Peer { get; }

            public PeerListItem(PeerInfo peer)
            {
                Peer = peer;
            }

            public override string ToString()
            {
                if (Peer == null)
                    return string.Empty;

                string presence = LanDiscoveryService.FormatLastSeen(Peer);
                string areaPart = string.IsNullOrWhiteSpace(Peer.Area) ? string.Empty : $" · {Peer.Area}";
                return $"{Peer.FullName}{areaPart} ({Peer.IPv4}) · {presence}";
            }
        }
    }
}
