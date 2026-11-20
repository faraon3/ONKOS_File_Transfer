using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ONKOSFileTransfer.Network;

namespace ONKOSFileTransfer
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool esModoBandeja = args != null && args.Any(a =>
                string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "/tray", StringComparison.OrdinalIgnoreCase));

            string nombreMutex = esModoBandeja
                ? "Global\\ONKOSFileTransfer_Tray_Mutex"
                : "Global\\ONKOSFileTransfer_UI_Mutex";

            using (var mutex = new Mutex(true, nombreMutex, out bool esPrimeraInstancia))
            {
                if (!esPrimeraInstancia)
                    return;

                PeerInfo profile = null;
                if (!esModoBandeja && !UserProfileStore.TryLoad(out profile))
                {
                    using (var welcome = new WelcomeRegistrationForm())
                    {
                        if (welcome.ShowDialog() != DialogResult.OK || welcome.RegisteredProfile == null)
                            return;
                        profile = welcome.RegisteredProfile;
                    }
                }

                if (esModoBandeja)
                {
                    ModernToastNotifier.Inicializar();
                    using (var monitor = new InboxMonitor())
                    {
                        Application.Run(new TrayHostContext());
                    }
                }
                else
                {
                    Application.Run(new MainForm(profile));
                }
            }
        }
    }

    internal sealed class TrayHostContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;

        public TrayHostContext()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir ventana de envío", null, (s, e) => AbrirVentanaPrincipal());
            menu.Items.Add("Abrir carpeta de recibidos", null, (s, e) =>
            {
                string rutaInbox = InboxMonitor.CarpetaRecibidos;
                Directory.CreateDirectory(rutaInbox);
                System.Diagnostics.Process.Start("explorer.exe", rutaInbox);
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir", null, (s, e) =>
            {
                _notifyIcon.Visible = false;
                Application.ExitThread();
            });

            _notifyIcon = new NotifyIcon
            {
                Text = "ONKOS File Transfer (Activo)",
                Icon = SystemIcons.Application,
                ContextMenuStrip = menu,
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => AbrirVentanaPrincipal();
        }

        private void AbrirVentanaPrincipal()
        {
            if (!UserProfileStore.TryLoad(out PeerInfo profile))
            {
                using (var welcome = new WelcomeRegistrationForm())
                {
                    if (welcome.ShowDialog() != DialogResult.OK || welcome.RegisteredProfile == null)
                        return;
                    profile = welcome.RegisteredProfile;
                }
            }

            var form = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
            if (form == null)
            {
                form = new MainForm(profile);
                form.Show();
            }
            else
            {
                if (form.WindowState == FormWindowState.Minimized)
                    form.WindowState = FormWindowState.Normal;
                form.BringToFront();
                form.Activate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public class PeerInfo
    {
        public string FullName { get; set; }
        public string Area { get; set; }
        public string IPv4 { get; set; }
        public string MachineName { get; set; }
        public DateTime LastSeenUtc { get; set; }

        public string DisplayText => $"{FullName} — {Area} ({IPv4})";

        public override string ToString() => DisplayText;
    }

    public static class UserProfileStore
    {
        private static readonly string ProfilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ONKOS", "perfil.txt");

        public static bool TryLoad(out PeerInfo profile)
        {
            profile = null;
            try
            {
                if (!File.Exists(ProfilePath))
                    return false;

                var lines = File.ReadAllLines(ProfilePath, Encoding.UTF8);
                if (lines.Length < 2)
                    return false;

                profile = new PeerInfo
                {
                    FullName = lines[0].Trim(),
                    Area = lines[1].Trim(),
                    MachineName = Environment.MachineName,
                    IPv4 = NetworkManager.GetLocalIPv4()
                };

                return !string.IsNullOrWhiteSpace(profile.FullName) &&
                       !string.IsNullOrWhiteSpace(profile.Area);
            }
            catch
            {
                return false;
            }
        }

        public static void Save(string fullName, string area)
        {
            string dir = Path.GetDirectoryName(ProfilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string cleanName = (fullName ?? string.Empty).Replace("|", "/").Trim();
            string cleanArea = (area ?? string.Empty).Replace("|", "/").Trim();

            File.WriteAllLines(
                ProfilePath,
                new[] { cleanName, cleanArea },
                Encoding.UTF8);
        }
    }

    public class PeerDiscoveryService : IDisposable
    {
        private const int DiscoveryPort = 5051;
        private const int BroadcastIntervalMs = 3500;
        private const int PeerTimeoutSeconds = 18;

        private readonly object _sync = new object();
        private readonly Dictionary<string, PeerInfo> _peersByIp =
            new Dictionary<string, PeerInfo>(StringComparer.OrdinalIgnoreCase);

        private UdpClient _listener;
        private CancellationTokenSource _cts;
        private PeerInfo _localProfile;
        private bool _running;

        public event Action PeersUpdated;

        public void Start(PeerInfo localProfile)
        {
            if (_running)
            {
                UpdateLocalProfile(localProfile);
                return;
            }

            _localProfile = localProfile;
            _cts = new CancellationTokenSource();
            _running = true;

            _listener = new UdpClient(AddressFamily.InterNetwork);
            _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.EnableBroadcast = true;
            _listener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

            _ = Task.Run(() => ListenLoop(_cts.Token));
            _ = Task.Run(() => AnnounceLoop(_cts.Token));
            _ = Task.Run(() => CleanupLoop(_cts.Token));
        }

        public void UpdateLocalProfile(PeerInfo profile)
        {
            _localProfile = profile;
            _ = Task.Run(() => BroadcastOnce());
        }

        public List<PeerInfo> GetActivePeers(string filterText = null)
        {
            List<PeerInfo> snapshot;
            lock (_sync)
            {
                snapshot = _peersByIp.Values
                    .Where(p => (DateTime.UtcNow - p.LastSeenUtc).TotalSeconds <= PeerTimeoutSeconds)
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(filterText))
            {
                string q = filterText.Trim().ToLowerInvariant();
                snapshot = snapshot
                    .Where(p =>
                        (p.FullName ?? string.Empty).ToLowerInvariant().Contains(q) ||
                        (p.Area ?? string.Empty).ToLowerInvariant().Contains(q) ||
                        (p.IPv4 ?? string.Empty).ToLowerInvariant().Contains(q) ||
                        (p.MachineName ?? string.Empty).ToLowerInvariant().Contains(q))
                    .ToList();
            }

            return snapshot
                .OrderBy(p => p.FullName)
                .ThenBy(p => p.Area)
                .ToList();
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _running)
            {
                try
                {
                    UdpReceiveResult result = await _listener.ReceiveAsync();
                    string msg = Encoding.UTF8.GetString(result.Buffer);
                    string[] parts = msg.Split('|');
                    if (parts.Length < 3)
                        continue;

                    string name = parts[0].Trim();
                    string area = parts[1].Trim();
                    string ip = parts[2].Trim();
                    string machine = parts.Length > 3 ? parts[3].Trim() : string.Empty;

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ip))
                        continue;

                    if (string.Equals(ip, NetworkManager.GetLocalIPv4(), StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool changed = false;
                    lock (_sync)
                    {
                        bool existed = _peersByIp.TryGetValue(ip, out PeerInfo existing);
                        if (!existed || existing.FullName != name || existing.Area != area)
                            changed = true;

                        _peersByIp[ip] = new PeerInfo
                        {
                            FullName = name,
                            Area = area,
                            IPv4 = ip,
                            MachineName = machine,
                            LastSeenUtc = DateTime.UtcNow
                        };
                    }

                    if (changed)
                        PeersUpdated?.Invoke();
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(500, token);
                }
            }
        }

        private async Task AnnounceLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _running)
            {
                await BroadcastOnce();
                try
                {
                    await Task.Delay(BroadcastIntervalMs, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private async Task BroadcastOnce()
        {
            try
            {
                if (_localProfile == null)
                    return;

                string localIp = NetworkManager.GetLocalIPv4();
                string payload = string.Join("|",
                    _localProfile.FullName,
                    _localProfile.Area,
                    localIp,
                    Environment.MachineName);

                byte[] data = Encoding.UTF8.GetBytes(payload);
                using (UdpClient sender = new UdpClient(AddressFamily.InterNetwork))
                {
                    sender.EnableBroadcast = true;
                    await sender.SendAsync(data, data.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

                    string[] octets = localIp.Split('.');
                    if (octets.Length == 4)
                    {
                        string subnetBroadcast = $"{octets[0]}.{octets[1]}.{octets[2]}.255";
                        if (IPAddress.TryParse(subnetBroadcast, out IPAddress subIp))
                            await sender.SendAsync(data, data.Length, new IPEndPoint(subIp, DiscoveryPort));
                    }
                }
            }
            catch
            {
            }
        }

        private async Task CleanupLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _running)
            {
                try
                {
                    await Task.Delay(5000, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                bool removed = false;
                lock (_sync)
                {
                    var expired = _peersByIp
                        .Where(kv => (DateTime.UtcNow - kv.Value.LastSeenUtc).TotalSeconds > PeerTimeoutSeconds)
                        .Select(kv => kv.Key)
                        .ToList();

                    foreach (string key in expired)
                    {
                        _peersByIp.Remove(key);
                        removed = true;
                    }
                }

                if (removed)
                    PeersUpdated?.Invoke();
            }
        }

        public void Dispose()
        {
            _running = false;
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Close(); } catch { }
            try { _listener?.Dispose(); } catch { }
            try { _cts?.Dispose(); } catch { }
        }
    }

    public class WelcomeRegistrationForm : Form
    {
        private readonly TextBox txtFullName;
        private readonly TextBox txtArea;

        public PeerInfo RegisteredProfile { get; private set; }

        public WelcomeRegistrationForm(PeerInfo existing = null)
        {
            Text = "Bienvenido a ONKOS File Transfer";
            ClientSize = new Size(460, 260);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(24, 26, 40);
            ForeColor = Color.FromArgb(246, 247, 252);
            Font = new Font("Segoe UI", 9.5F);

            Controls.Add(new Label
            {
                Text = "BIENVENIDO — REGISTRO DEL EQUIPO",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(184, 148, 255),
                Location = new Point(24, 18),
                Size = new Size(412, 28)
            });

            Controls.Add(new Label
            {
                Text = "Ingresa tus datos una sola vez para que otros equipos te ubiquen por nombre.",
                ForeColor = Color.FromArgb(152, 160, 182),
                Location = new Point(24, 48),
                Size = new Size(412, 22)
            });

            Controls.Add(new Label
            {
                Text = "NOMBRE COMPLETO",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(152, 160, 182),
                Location = new Point(24, 82),
                Size = new Size(412, 18)
            });

            txtFullName = new TextBox
            {
                Text = existing?.FullName ?? string.Empty,
                Location = new Point(24, 102),
                Size = new Size(412, 28),
                BackColor = Color.FromArgb(20, 22, 35),
                ForeColor = Color.FromArgb(246, 247, 252),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(txtFullName);

            Controls.Add(new Label
            {
                Text = "ÁREA",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(152, 160, 182),
                Location = new Point(24, 140),
                Size = new Size(412, 18)
            });

            txtArea = new TextBox
            {
                Text = existing?.Area ?? string.Empty,
                Location = new Point(24, 160),
                Size = new Size(412, 28),
                BackColor = Color.FromArgb(20, 22, 35),
                ForeColor = Color.FromArgb(246, 247, 252),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(txtArea);

            Button btnSave = new Button
            {
                Text = "Guardar y continuar",
                Location = new Point(266, 206),
                Size = new Size(170, 34),
                BackColor = Color.FromArgb(124, 77, 224),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += OnSaveClick;
            Controls.Add(btnSave);
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            string fullName = txtFullName.Text.Trim();
            string area = txtArea.Text.Trim();

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(area))
            {
                MessageBox.Show(
                    "Ingresa tu nombre completo y tu área para continuar.",
                    "ONKOS File Transfer 2.0",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            UserProfileStore.Save(fullName, area);
            UserProfileStore.TryLoad(out PeerInfo loaded);
            RegisteredProfile = loaded ?? new PeerInfo
            {
                FullName = fullName,
                Area = area,
                MachineName = Environment.MachineName,
                IPv4 = NetworkManager.GetLocalIPv4()
            };

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
