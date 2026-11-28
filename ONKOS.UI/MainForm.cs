using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ONKOS.Core.Discovery;
using ONKOS.Core.Logging;
using ONKOS.Core.Models;
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

        private PeerInfo _localProfile;
        private long _cachedSelectionBytes;

        private readonly Label _lblIdentity;
        private readonly Label _lblInboxPath;
        private readonly Label _lblStatus;
        private readonly Label _lblSelectionSize;
        private readonly TextBox _txtRecipientIp;
        private readonly ComboBox _cmbGroupOrArea;
        private readonly Button _btnSelectGroup;
        private readonly CheckedListBox _clbPeers;
        private readonly ListBox _lstFiles;
        private readonly ListBox _lstQueue;
        private readonly Button _btnAddFiles;
        private readonly Button _btnRemoveFile;
        private readonly Button _btnSend;
        private readonly Button _btnPauseResume;
        private readonly Button _btnCancelQueue;
        private readonly Button _btnRetryFailed;
        private readonly Button _btnInbox;
        private readonly Button _btnHistory;
        private readonly Button _btnSettings;
        private readonly Button _btnProfile;

        public MainForm(PeerInfo profile)
        {
            _localProfile = profile;
            _queue.SetLocalProfile(_localProfile);

            Text = "ONKOS File Transfer 2.0";
            Size = new Size(860, 650);
            MinimumSize = new Size(760, 560);
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
                FileLogger.Instance.Warn(
                    "UI.MainForm",
                    $"No se pudo cargar icono principal ({ex.GetType().Name}: {ex.Message}).");
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
                Location = new Point(500, 16),
                Size = new Size(80, 30),
                BackColor = Color.FromArgb(38, 41, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnProfile.Click += BtnProfile_Click;

            _btnHistory = new Button
            {
                Text = "Historial",
                Location = new Point(586, 16),
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
                Location = new Point(676, 16),
                Size = new Size(92, 30),
                BackColor = Color.FromArgb(38, 41, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnSettings.Click += BtnSettings_Click;

            _btnInbox = new Button
            {
                Text = "Recibidos",
                Location = new Point(774, 16),
                Size = new Size(68, 30),
                BackColor = Color.FromArgb(38, 41, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnInbox.Click += (s, e) => OpenInboxFolder();

            pnlTop.Controls.AddRange(new Control[] { lblTitle, _lblIdentity, _btnProfile, _btnHistory, _btnSettings, _btnInbox });

            var lblTarget = new Label
            {
                Text = "IP / Destino manual:",
                Location = new Point(18, 76),
                Size = new Size(135, 22)
            };

            _txtRecipientIp = new TextBox
            {
                Location = new Point(156, 74),
                Size = new Size(180, 26),
                BackColor = Color.FromArgb(28, 31, 48),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            _cmbGroupOrArea = new ComboBox
            {
                Location = new Point(348, 74),
                Size = new Size(190, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(28, 31, 48),
                ForeColor = Color.White
            };

            _btnSelectGroup = new Button
            {
                Text = "Marcar área/grupo",
                Location = new Point(546, 73),
                Size = new Size(135, 28),
                BackColor = Color.FromArgb(38, 41, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnSelectGroup.Click += BtnSelectGroup_Click;

            var lblPeers = new Label
            {
                Text = "Destinatarios en LAN (marca uno o varios):",
                Location = new Point(18, 108),
                Size = new Size(420, 20),
                ForeColor = Color.FromArgb(184, 148, 255)
            };

            _clbPeers = new CheckedListBox
            {
                Location = new Point(18, 130),
                Size = new Size(818, 105),
                CheckOnClick = true,
                BackColor = Color.FromArgb(18, 20, 32),
                ForeColor = Color.FromArgb(242, 244, 252),
                BorderStyle = BorderStyle.FixedSingle
            };

            _lblSelectionSize = new Label
            {
                Text = "Selección: 0 elemento(s) (0 B)",
                Location = new Point(18, 244),
                Size = new Size(340, 20),
                ForeColor = Color.FromArgb(165, 172, 202)
            };

            _btnAddFiles = new Button
            {
                Text = "+ Adjuntar",
                Location = new Point(630, 241),
                Size = new Size(100, 28),
                BackColor = Color.FromArgb(124, 77, 224),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnAddFiles.Click += BtnAddFiles_Click;

            _btnRemoveFile = new Button
            {
                Text = "Quitar",
                Location = new Point(738, 241),
                Size = new Size(98, 28),
                BackColor = Color.FromArgb(38, 41, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnRemoveFile.Click += BtnRemoveFile_Click;

            _lstFiles = new ListBox
            {
                Location = new Point(18, 274),
                Size = new Size(818, 92),
                SelectionMode = SelectionMode.MultiExtended,
                BackColor = Color.FromArgb(18, 20, 32),
                ForeColor = Color.FromArgb(242, 244, 252),
                BorderStyle = BorderStyle.FixedSingle
            };

            var lblQueue = new Label
            {
                Text = "Cola de transferencias (progreso, velocidad, ETA y estado):",
                Location = new Point(18, 372),
                Size = new Size(460, 20),
                ForeColor = Color.FromArgb(184, 148, 255)
            };

            _btnPauseResume = new Button
            {
                Text = "Pausar",
                Location = new Point(526, 369),
                Size = new Size(92, 26),
                BackColor = Color.FromArgb(38, 41, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnPauseResume.Click += BtnPauseResume_Click;

            _btnCancelQueue = new Button
            {
                Text = "Cancelar",
                Location = new Point(626, 369),
                Size = new Size(96, 26),
                BackColor = Color.FromArgb(120, 42, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnCancelQueue.Click += (s, e) => _queue.CancelAllActive();

            _btnRetryFailed = new Button
            {
                Text = "Reintentar fallidos",
                Location = new Point(730, 369),
                Size = new Size(106, 26),
                BackColor = Color.FromArgb(38, 41, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnRetryFailed.Click += (s, e) => _queue.RetryFailedJobs();

            _lstQueue = new ListBox
            {
                Location = new Point(18, 399),
                Size = new Size(818, 120),
                BackColor = Color.FromArgb(18, 20, 32),
                ForeColor = Color.FromArgb(225, 232, 250),
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
                Text = "Listo. Cola activa en segundo plano y canal cifrado TLS 1.2+.",
                Location = new Point(16, 10),
                Size = new Size(640, 22),
                ForeColor = Color.FromArgb(215, 220, 240)
            };

            _lblInboxPath = new Label
            {
                Text = "Carpeta recibidos: " + AppSettingsStore.GetInboxFolder(),
                Location = new Point(16, 36),
                Size = new Size(640, 22),
                ForeColor = Color.FromArgb(155, 162, 192)
            };

            _btnSend = new Button
            {
                Text = "Enviar",
                Location = new Point(682, 16),
                Size = new Size(154, 40),
                BackColor = Color.FromArgb(124, 77, 224),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnSend.Click += BtnSend_Click;

            pnlBottom.Controls.AddRange(new Control[] { _lblStatus, _lblInboxPath, _btnSend });

            _discovery.PeersUpdated += OnDiscoveryPeersUpdated;
            _discovery.NewPeerDiscovered += OnNewPeerDiscovered;
            PeerAuthorizationStore.PendingRequestReceived += OnPendingAuthorizationReceived;
            _queue.QueueChanged += OnQueueChanged;
            _queue.BatchCompleted += OnQueueBatchCompleted;

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            Load += OnMainFormLoad;
            FormClosed += OnMainFormClosed;

            Controls.AddRange(new Control[]
            {
                pnlTop,
                lblTarget,
                _txtRecipientIp,
                _cmbGroupOrArea,
                _btnSelectGroup,
                lblPeers,
                _clbPeers,
                _lblSelectionSize,
                _btnAddFiles,
                _btnRemoveFile,
                _lstFiles,
                lblQueue,
                _btnPauseResume,
                _btnCancelQueue,
                _btnRetryFailed,
                _lstQueue,
                pnlBottom
            });
        }

        private void OnMainFormLoad(object sender, EventArgs e)
        {
            RefreshInboxBadgeText();
            _queue.EnsureStarted();
            _discovery.Start(_localProfile);
            RefreshGroupsCombo();
            RefreshPeersList();
            RefreshQueueList();
        }

        private void OnMainFormClosed(object sender, FormClosedEventArgs e)
        {
            try { _uiCts.Cancel(); }
            catch (ObjectDisposedException) { }

            _discovery.PeersUpdated -= OnDiscoveryPeersUpdated;
            _discovery.NewPeerDiscovered -= OnNewPeerDiscovered;
            PeerAuthorizationStore.PendingRequestReceived -= OnPendingAuthorizationReceived;
            _queue.QueueChanged -= OnQueueChanged;
            _queue.BatchCompleted -= OnQueueBatchCompleted;

            _discovery.Dispose();
            _uiCts.Dispose();
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
                try { BeginInvoke(new Action(() => ShowSilentBanner($"Nuevo usuario detectado: {peer.FullName} ({peer.Area}) — {peer.IPv4}"))); }
                catch (ObjectDisposedException) { }
                return;
            }

            ShowSilentBanner($"Nuevo usuario detectado: {peer.FullName} ({peer.Area}) — {peer.IPv4}");
        }

        private void OnPendingAuthorizationReceived(AuthorizedPeerRecord record)
        {
            if (IsDisposed || Disposing || record == null)
                return;

            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => ShowSilentBanner($"Solicitud de autorización pendiente: {record.DisplayName} ({record.LastIp})"))); }
                catch (ObjectDisposedException) { }
                return;
            }

            ShowSilentBanner($"Solicitud de autorización pendiente: {record.DisplayName} ({record.LastIp})");
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

            _lblStatus.Text = $"Resumen envío: {ok} completado(s), {fail} con fallo/cancelación.";
            if (fail > 0)
            {
                TransferJob firstFail = snapshot.FirstOrDefault(j => j.State == QueueJobState.Failed || j.State == QueueJobState.Cancelled);
                if (firstFail != null && !string.IsNullOrWhiteSpace(firstFail.SuggestedAction))
                    _lblStatus.Text += $" Sugerencia: {firstFail.SuggestedAction}";
            }
        }

        private void ShowSilentBanner(string message)
        {
            if (IsDisposed || Disposing)
                return;
            _lblStatus.Text = message;
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
            foreach (var checkedObj in _clbPeers.CheckedItems)
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

            _lblStatus.Text = marked > 0
                ? $"Marcados {marked} destinatario(s) de '{groupName}'."
                : $"No hay destinatarios activos en '{groupName}'.";
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
                FileLogger.Instance.Warn(
                    "UI.MainForm",
                    $"Error al abrir carpeta de recibidos ({ex.GetType().Name}: {ex.Message}).");
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
                    _queue.SetLocalProfile(_localProfile);
                    _discovery.UpdateLocalProfile(_localProfile);
                    _lblIdentity.Text = BuildIdentityText();
                    _lblStatus.Text = "Perfil actualizado y anunciado en red.";
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
            using (var ofd = new OpenFileDialog { Multiselect = true, Title = "Seleccionar archivos" })
            {
                if (ofd.ShowDialog(this) == DialogResult.OK)
                    _ = AddPathsAsync(ofd.FileNames);
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
                List<PeerInfo> targets = CollectTargets();
                if (targets.Count == 0)
                {
                    _lblStatus.Text = "Marca al menos un destinatario o escribe una IPv4 válida.";
                    return;
                }

                List<string> items = _lstFiles.Items.Cast<string>().ToList();
                if (items.Count == 0)
                {
                    _lblStatus.Text = "Adjunta al menos un archivo o carpeta para enviar.";
                    return;
                }

                foreach (PeerInfo t in targets)
                    AddressBookStore.UpsertContact(t);

                List<TransferJob> jobs = _queue.EnqueueBatch(items, targets);
                _lstFiles.Items.Clear();
                _cachedSelectionBytes = 0L;
                await RecalculateSelectionSizeAsync().ConfigureAwait(true);

                _lblStatus.Text = $"Se agregaron {jobs.Count} transferencia(s) a la cola.";
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Error("UI.Send", "Error al encolar transferencia.", ex);
                _lblStatus.Text = "No se pudo encolar el envío: " + ex.Message;
            }
        }

        private List<PeerInfo> CollectTargets()
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

            string manual = (_txtRecipientIp.Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(manual))
            {
                if (PeerContactsStore.TryParseManualContact(manual, out string name, out string area, out string ipv4, out string err))
                {
                    if (seenIps.Add(ipv4))
                    {
                        result.Add(new PeerInfo
                        {
                            FullName = name,
                            Area = area,
                            IPv4 = ipv4,
                            MachineName = ipv4,
                            LastSeenUtc = DateTime.UtcNow
                        });
                    }
                }
                else if (result.Count == 0)
                {
                    _lblStatus.Text = err;
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
