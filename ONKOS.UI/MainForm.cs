using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text;
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
        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _trayMenu;

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
        private readonly Button _btnAddFiles;
        private readonly Button _btnAddFolder;
        private readonly Button _btnRemoveFile;
        private readonly Button _btnSend;
        private readonly Button _btnDetails;
        private readonly Button _btnHelp;
        private readonly Button _btnPauseResume;
        private readonly Button _btnCancelQueue;
        private readonly Button _btnRetryFailed;
        private readonly Button _btnNotifications;
        private readonly Button _btnInbox;
        private readonly Button _btnHistory;
        private readonly Button _btnSettings;
        private readonly Button _btnProfile;

        public MainForm(PeerInfo profile)
        {
            _localProfile = profile;
            _queue.SetLocalProfile(_localProfile);
            _palette = ThemeManager.GetCurrentPalette();

            Text = UiResources.AppTitle;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Size = new Size(940, 700);
            MinimumSize = new Size(780, 580);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            AllowDrop = true;
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

            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 68,
                BackColor = _palette.HeaderBg,
                Padding = new Padding(14, 8, 14, 8)
            };

            var lblTitle = new Label
            {
                Text = UiResources.AppTitle,
                Font = ThemeManager.HeaderTitleFont,
                ForeColor = _palette.Mode == AppThemeMode.Dark ? Color.FromArgb(184, 148, 255) : _palette.PrimaryAccent,
                Location = new Point(14, 8),
                AutoSize = true
            };

            _lblIdentity = new Label
            {
                Text = BuildIdentityText(),
                ForeColor = _palette.TextSecondary,
                Location = new Point(15, 36),
                AutoSize = true
            };

            var topButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(0, 6, 0, 0)
            };

            _btnProfile = new Button { Text = "Mi Perfil", Size = new Size(80, 32), AccessibleName = "Editar mi perfil" };
            ThemeManager.StyleButton(_btnProfile, _palette);
            _btnProfile.Click += BtnProfile_Click;

            _btnHistory = new Button { Text = "Historial", Size = new Size(84, 32), AccessibleName = "Abrir historial de transferencias" };
            ThemeManager.StyleButton(_btnHistory, _palette);
            _btnHistory.Click += (s, e) =>
            {
                using (var dlg = new TransferHistoryForm())
                    dlg.ShowDialog(this);
                RefreshInboxBadgeText(force: true);
            };

            _btnNotifications = new Button { Text = "Avisos", Size = new Size(84, 32), AccessibleName = "Abrir centro de notificaciones" };
            ThemeManager.StyleButton(_btnNotifications, _palette);
            _btnNotifications.Click += (s, e) =>
            {
                using (var dlg = new NotificationCenterForm())
                    dlg.ShowDialog(this);
                RefreshInboxBadgeText(force: true);
            };

            _btnSettings = new Button { Text = "Configuración", Size = new Size(102, 32), AccessibleName = "Abrir configuración y diagnóstico" };
            ThemeManager.StyleButton(_btnSettings, _palette);
            _btnSettings.Click += BtnSettings_Click;

            _btnInbox = new Button { Text = "Recibidos", Size = new Size(96, 32), AccessibleName = "Abrir carpeta de archivos recibidos" };
            ThemeManager.StyleButton(_btnInbox, _palette);
            _btnInbox.Click += (s, e) => OpenInboxFolder();

            _btnHelp = new Button { Text = "Ayuda (F1)", Size = new Size(92, 32), AccessibleName = "Mostrar ayuda y atajos de teclado" };
            ThemeManager.StyleButton(_btnHelp, _palette);
            _btnHelp.Click += (s, e) => ShowHelpDialog();

            topButtons.Controls.AddRange(new Control[] { _btnProfile, _btnHistory, _btnNotifications, _btnSettings, _btnInbox, _btnHelp });
            pnlTop.Controls.Add(topButtons);
            pnlTop.Controls.Add(lblTitle);
            pnlTop.Controls.Add(_lblIdentity);

            var mainGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(14, 8, 14, 8)
            };
            mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 32F));
            mainGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 32F));
            mainGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 36F));

            // Paso 1: Archivos / Carpetas
            var step1Header = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 2, 0, 4) };
            step1Header.Controls.Add(new Label
            {
                Text = UiResources.Step1Title,
                Font = ThemeManager.SectionTitleFont,
                ForeColor = _palette.Mode == AppThemeMode.Dark ? Color.FromArgb(184, 148, 255) : _palette.PrimaryAccent,
                AutoSize = true,
                Padding = new Padding(0, 5, 10, 0)
            });

            _btnAddFiles = new Button { Text = UiResources.BtnAddFiles, Size = new Size(175, 30), AccessibleName = "Agregar archivos" };
            ThemeManager.StyleButton(_btnAddFiles, _palette, isPrimary: true);
            _btnAddFiles.Click += BtnAddFiles_Click;

            _btnAddFolder = new Button { Text = UiResources.BtnAddFolder, Size = new Size(135, 30), AccessibleName = "Agregar carpeta" };
            ThemeManager.StyleButton(_btnAddFolder, _palette);
            _btnAddFolder.Click += BtnAddFolder_Click;

            _btnRemoveFile = new Button { Text = UiResources.BtnRemoveSelected, Size = new Size(125, 30), AccessibleName = "Quitar elementos seleccionados" };
            ThemeManager.StyleButton(_btnRemoveFile, _palette);
            _btnRemoveFile.Click += BtnRemoveFile_Click;

            _lblSelectionSize = new Label
            {
                Text = "Selección: 0 elemento(s) (0 B)",
                ForeColor = _palette.TextSecondary,
                AutoSize = true,
                Padding = new Padding(10, 7, 0, 0)
            };

            step1Header.Controls.AddRange(new Control[] { _btnAddFiles, _btnAddFolder, _btnRemoveFile, _lblSelectionSize });

            _lstFiles = new ListBox
            {
                Dock = DockStyle.Fill,
                SelectionMode = SelectionMode.MultiExtended,
                BackColor = _palette.CardBg,
                ForeColor = _palette.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = "Lista de archivos y carpetas seleccionados para enviar"
            };

            // Paso 2: Destinatarios
            var step2Header = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
            step2Header.Controls.Add(new Label
            {
                Text = UiResources.Step2Title,
                Font = ThemeManager.SectionTitleFont,
                ForeColor = _palette.Mode == AppThemeMode.Dark ? Color.FromArgb(184, 148, 255) : _palette.PrimaryAccent,
                AutoSize = true,
                Padding = new Padding(0, 5, 10, 0)
            });

            _btnAddManualPeer = new Button { Text = UiResources.BtnAddManualPeer, Size = new Size(195, 30), AccessibleName = "Agregar equipo manualmente" };
            ThemeManager.StyleButton(_btnAddManualPeer, _palette);
            _btnAddManualPeer.Click += BtnAddManualPeer_Click;

            _btnSelectAllPeers = new Button { Text = UiResources.BtnSelectAllPeers, Size = new Size(135, 30), AccessibleName = "Seleccionar todos los destinatarios" };
            ThemeManager.StyleButton(_btnSelectAllPeers, _palette);
            _btnSelectAllPeers.Click += BtnSelectAllPeers_Click;

            _cmbGroupOrArea = new ComboBox
            {
                Width = 175,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = _palette.InputBg,
                ForeColor = _palette.TextPrimary,
                AccessibleName = "Área o grupo de destinatarios"
            };
            step2Header.Controls.Add(_cmbGroupOrArea);

            _btnSelectGroup = new Button { Text = UiResources.BtnSelectByGroup, Size = new Size(145, 30), AccessibleName = "Marcar destinatarios por área o grupo" };
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
                AccessibleName = "Lista de equipos disponibles en la red"
            };
            _clbPeers.ItemCheck += (s, e) => BeginInvoke(new Action(UpdateSendButtonState));

            // Paso 3: Cola y controles
            var step3Header = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
            step3Header.Controls.Add(new Label
            {
                Text = UiResources.Step3Title,
                Font = ThemeManager.SectionTitleFont,
                ForeColor = _palette.Mode == AppThemeMode.Dark ? Color.FromArgb(184, 148, 255) : _palette.PrimaryAccent,
                AutoSize = true,
                Padding = new Padding(0, 5, 10, 0)
            });

            _btnPauseResume = new Button { Text = "Pausar", Size = new Size(96, 28), AccessibleName = "Pausar o reanudar cola" };
            ThemeManager.StyleButton(_btnPauseResume, _palette);
            _btnPauseResume.Click += BtnPauseResume_Click;

            _btnCancelQueue = new Button { Text = "Cancelar", Size = new Size(96, 28), AccessibleName = "Cancelar transferencias activas" };
            ThemeManager.StyleButton(_btnCancelQueue, _palette, isDanger: true);
            _btnCancelQueue.Click += (s, e) => _queue.CancelAllActive();

            _btnRetryFailed = new Button { Text = "Reintentar fallidos", Size = new Size(135, 28), AccessibleName = "Reintentar transferencias fallidas" };
            ThemeManager.StyleButton(_btnRetryFailed, _palette);
            _btnRetryFailed.Click += (s, e) => _queue.RetryFailedJobs();

            step3Header.Controls.AddRange(new Control[] { _btnPauseResume, _btnCancelQueue, _btnRetryFailed });

            _lstQueue = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = _palette.CardBg,
                ForeColor = _palette.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = "Cola de transferencias en curso y completadas"
            };

            mainGrid.Controls.Add(step1Header, 0, 0);
            mainGrid.Controls.Add(_lstFiles, 0, 1);
            mainGrid.Controls.Add(step2Header, 0, 2);
            mainGrid.Controls.Add(_clbPeers, 0, 3);
            mainGrid.Controls.Add(step3Header, 0, 4);
            mainGrid.Controls.Add(_lstQueue, 0, 5);

            var pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 86,
                BackColor = _palette.HeaderBg,
                Padding = new Padding(14, 10, 14, 10)
            };

            _lblStatus = new Label
            {
                Text = "Listo. Canal cifrado TLS 1.2+ con autenticación mutua (avisos visuales sin sonido).",
                Location = new Point(14, 10),
                Size = new Size(560, 22),
                ForeColor = _palette.TextPrimary
            };

            _lblInboxPath = new Label
            {
                Text = "Carpeta recibidos: " + AppSettingsStore.GetInboxFolder(),
                Location = new Point(14, 38),
                Size = new Size(560, 22),
                ForeColor = _palette.TextSecondary
            };

            var actionDock = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(0, 12, 0, 0)
            };

            _btnDetails = new Button { Text = "Ver detalles", Size = new Size(105, 38), Visible = false, AccessibleName = "Ver detalle técnico del error" };
            ThemeManager.StyleButton(_btnDetails, _palette);
            _btnDetails.Click += BtnDetails_Click;

            _btnSend = new Button { Text = UiResources.SendDisabledNoFiles, Size = new Size(275, 38), AccessibleName = "Enviar archivos a destinatarios seleccionados" };
            ThemeManager.StyleButton(_btnSend, _palette, isPrimary: true);
            _btnSend.Click += BtnSend_Click;

            actionDock.Controls.Add(_btnDetails);
            actionDock.Controls.Add(_btnSend);
            pnlBottom.Controls.Add(actionDock);
            pnlBottom.Controls.Add(_lblStatus);
            pnlBottom.Controls.Add(_lblInboxPath);

            // Bandeja del sistema sin sonido
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Abrir ONKOS File Transfer", null, (s, e) => BringToFrontFromSecondInstance());
            _trayMenu.Items.Add("Abrir carpeta Recibidos", null, (s, e) => OpenInboxFolder());
            _trayMenu.Items.Add("Centro de Avisos", null, (s, e) =>
            {
                using (var dlg = new NotificationCenterForm())
                    dlg.ShowDialog(this);
            });
            _trayMenu.Items.Add("Diagnóstico / Configuración", null, (s, e) => BtnSettings_Click(this, EventArgs.Empty));

            _trayIcon = new NotifyIcon
            {
                Icon = loadedIcon ?? SystemIcons.Application,
                Text = UiResources.AppTitle,
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _trayIcon.DoubleClick += (s, e) => BringToFrontFromSecondInstance();

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

        private void OnMainFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.O)
            {
                e.Handled = true;
                BtnAddFiles_Click(this, EventArgs.Empty);
            }
            else if (e.KeyCode == Keys.Delete && _lstFiles.Focused)
            {
                e.Handled = true;
                BtnRemoveFile_Click(this, EventArgs.Empty);
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
            _liveSyncTimer.Start();
        }

        private void OnMainFormClosed(object sender, FormClosedEventArgs e)
        {
            _liveSyncTimer.Stop();
            _liveSyncTimer.Dispose();

            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayMenu.Dispose();

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
            string svc = "Servicio: activo";
            try
            {
                using (var sc = new ServiceController("ONKOSTransferService"))
                {
                    if (sc.Status != ServiceControllerStatus.Running)
                        svc = "Servicio: " + sc.Status;
                }
            }
            catch (Exception)
            {
            }

            int unseen = Math.Max(0, _lastSeenUnseenCount);
            string tip = $"{UiResources.AppTitle} • {svc} • Recibidos nuevos: {unseen}";
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
                try { BeginInvoke(new Action(() => SetFriendlyStatus($"Nuevo usuario detectado: {peer.FullName} ({peer.Area}) — {peer.IPv4}"))); }
                catch (ObjectDisposedException) { }
                return;
            }

            SetFriendlyStatus($"Nuevo usuario detectado: {peer.FullName} ({peer.Area}) — {peer.IPv4}");
        }

        private void OnPendingAuthorizationReceived(AuthorizedPeerRecord record)
        {
            if (IsDisposed || Disposing || record == null)
                return;

            NotificationCenterStore.PublishGrouped(
                NotificationCategory.AuthorizationRequest,
                "auth-" + record.FingerprintSha256,
                "Solicitud de autorización pendiente",
                $"{record.DisplayName} ({record.LastIp})",
                $"Huella: {record.FingerprintSha256}");

            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => SetFriendlyStatus($"Solicitud de autorización pendiente: {record.DisplayName} ({record.LastIp})"))); }
                catch (ObjectDisposedException) { }
                return;
            }

            SetFriendlyStatus($"Solicitud de autorización pendiente: {record.DisplayName} ({record.LastIp})");
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
                $"{ok} ok / {fail} fallidos",
                $"Total en lote: {snapshot.Count} transferencia(s)");

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

            string msg = $"Resumen del envío: {ok} completado(s), {fail} con fallo o cancelación.";
            if (fail > 0)
            {
                TransferJob firstFail = snapshot.FirstOrDefault(j => j.State == QueueJobState.Failed || j.State == QueueJobState.Cancelled);
                if (firstFail != null)
                {
                    _lastTechnicalError = $"Destino: {firstFail.TargetLabel} | Elemento: {firstFail.ItemName} | Detalle: {firstFail.ErrorMessage}";
                    _btnDetails.Visible = true;
                    if (!string.IsNullOrWhiteSpace(firstFail.SuggestedAction))
                        msg += " Acción sugerida: " + firstFail.SuggestedAction;
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
                _lastTechnicalError + "\r\n\r\nRuta de logs: " + FileLogger.Instance.LogsFolder,
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

                string speed = j.BytesPerSecond > 0
                    ? FormatBytes((long)j.BytesPerSecond) + "/s"
                    : "0 B/s";

                string eta = j.EstimatedRemaining.HasValue && j.State == QueueJobState.Running
                    ? $"ETA {j.EstimatedRemaining.Value:mm\\:ss}"
                    : string.Empty;

                string errDetail = (j.State == QueueJobState.Failed || j.State == QueueJobState.Cancelled) && !string.IsNullOrWhiteSpace(j.ErrorMessage)
                    ? $" — {j.ErrorMessage}"
                    : string.Empty;

                _lstQueue.Items.Add(
                    $"[{j.State}] {j.ItemName} -> {j.TargetLabel} | {pct:0.0}% ({FormatBytes(j.SentBytes)}/{FormatBytes(j.TotalBytes)}) | {speed} {eta}{errDetail}");
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
                    SetFriendlyStatus($"Destinatario agregado: {peer.FullName} ({peer.IPv4}).");
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
                ? $"Marcados {marked} destinatario(s) de '{groupName}'."
                : $"No hay destinatarios activos en '{groupName}'.");
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
                _btnSend.Text = UiResources.SendDisabledNoFiles;
                return;
            }

            if (peerCount == 0)
            {
                _btnSend.Enabled = false;
                _btnSend.Text = UiResources.SendDisabledNoTargets;
                return;
            }

            _btnSend.Enabled = true;
            _btnSend.Text = $"{UiResources.SendReadyPrefix} ({fileCount} elem. -> {peerCount} dest.)";
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
            return $"{name} ({area})  •  IP: {ip}  •  Equipo: {Environment.MachineName}";
        }

        private void RefreshInboxBadgeText(bool force)
        {
            int unseen = TransferHistoryStore.GetUnseenReceivedCount();
            if (force || unseen != _lastSeenUnseenCount)
            {
                _lastSeenUnseenCount = unseen;
                _btnInbox.Text = unseen > 0 ? $"Recibidos ({unseen})" : "Recibidos";
            }

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
                    SetFriendlyStatus("No se pudo preparar la carpeta de recibidos. Revisa el espacio o permisos.");
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

        private void BtnSettings_Click(object sender, EventArgs e)
        {
            using (var dlg = new SettingsForm())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    RefreshInboxBadgeText(force: true);
                    SetFriendlyStatus("Configuración actualizada.");
                }
            }
        }

        private void BtnProfile_Click(object sender, EventArgs e)
        {
            using (var dlg = new FirstRunWizardForm())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.RegisteredProfile != null)
                {
                    _localProfile = dlg.RegisteredProfile;
                    _queue.SetLocalProfile(_localProfile);
                    _discovery.UpdateLocalProfile(_localProfile);
                    _lblIdentity.Text = BuildIdentityText();
                    SetFriendlyStatus("Perfil actualizado y anunciado en red.");
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

        private void BtnAddFiles_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog { Multiselect = true, Title = "Seleccionar archivos para enviar" })
            {
                if (ofd.ShowDialog(this) == DialogResult.OK)
                    _ = AddPathsAsync(ofd.FileNames);
            }
        }

        private void BtnAddFolder_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog { Description = "Seleccionar carpeta para enviar" })
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
                FileLogger.Instance.Warn("UI.MainForm", $"Error calculando tamaños de selección ({ex.Message}).");
            }
        }

        private void BtnRemoveFile_Click(object sender, EventArgs e)
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
                _lblSelectionSize.Text = $"Selección: {items.Count} elemento(s) ({FormatBytes(_cachedSelectionBytes)})";
        }

        private async void BtnSend_Click(object sender, EventArgs e)
        {
            try
            {
                List<PeerInfo> targets = CollectCheckedTargets();
                if (targets.Count == 0)
                {
                    SetFriendlyStatus("Marca al menos un destinatario o usa 'Agregar equipo manualmente'.");
                    return;
                }

                List<string> items = _lstFiles.Items.Cast<string>().ToList();
                if (items.Count == 0)
                {
                    SetFriendlyStatus("Adjunta al menos un archivo o carpeta antes de enviar.");
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
                SetFriendlyStatus($"Se encolaron {jobs.Count} transferencia(s) por canal cifrado TLS 1.2+.");
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Error("UI.Send", "Error al encolar transferencia.", ex);
                SetActionableError("No se pudo iniciar el envío. Revisa la conexión o el destino.", ex);
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
                string areaPart = string.IsNullOrWhiteSpace(Peer.Area) ? string.Empty : $" — {Peer.Area}";
                string fpShort = string.IsNullOrWhiteSpace(Peer.FingerprintSha256)
                    ? string.Empty
                    : $" [{Peer.FingerprintSha256.Substring(0, Math.Min(8, Peer.FingerprintSha256.Length))}]";

                return $"{Peer.FullName}{areaPart} ({Peer.IPv4} • {Peer.MachineName}{fpShort}) — {presence}";
            }
        }
    }
}
