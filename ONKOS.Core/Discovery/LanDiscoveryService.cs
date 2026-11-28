using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ONKOS.Core.Logging;
using ONKOS.Core.Models;
using ONKOS.Core.Security;
using ONKOS.Core.Storage;

namespace ONKOS.Core.Discovery
{
    public sealed class SubnetInfo
    {
        public IPAddress LocalIp { get; set; }
        public IPAddress SubnetMask { get; set; }
        public IPAddress BroadcastAddress { get; set; }
    }

    public sealed class LanDiscoveryService : IDisposable
    {
        public const int DiscoveryPort = 5051;
        private const string MagicPrefix = "ONKOS_ANN_V2";
        private const int AnnounceIntervalMs = 4000;
        private const int PresenceCheckIntervalMs = 6000;
        private static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(25);
        private static readonly TimeSpan LocalIpCacheTtl = TimeSpan.FromSeconds(10);

        private readonly object _sync = new object();
        private readonly Dictionary<string, PeerInfo> _peersByKey =
            new Dictionary<string, PeerInfo>(StringComparer.OrdinalIgnoreCase);

        private PeerInfo _localProfile;
        private string _localFingerprint;
        private string _cachedIp = "127.0.0.1";
        private DateTime _cachedIpAtUtc = DateTime.MinValue;
        private List<SubnetInfo> _cachedSubnets = new List<SubnetInfo>();

        private UdpClient _listener;
        private CancellationTokenSource _cts;
        private bool _running;

        public event Action PeersUpdated;
        public event Action<PeerInfo> NewPeerDiscovered;

        public static bool IsPeerOnline(PeerInfo peer)
        {
            if (peer == null || peer.LastSeenUtc == DateTime.MinValue)
                return false;

            return (DateTime.UtcNow - peer.LastSeenUtc) <= OnlineWindow;
        }

        public static string FormatLastSeen(PeerInfo peer)
        {
            if (peer == null || peer.LastSeenUtc == DateTime.MinValue)
                return "sin conexión reciente";

            TimeSpan delta = DateTime.UtcNow - peer.LastSeenUtc;
            if (delta.TotalSeconds < 10)
                return "en línea";
            if (delta.TotalMinutes < 1)
                return $"visto hace {(int)delta.TotalSeconds}s";
            if (delta.TotalHours < 1)
                return $"visto hace {(int)delta.TotalMinutes}m";
            return $"visto hace {(int)delta.TotalHours}h";
        }

        public string GetLocalIPv4Cached()
        {
            lock (_sync)
            {
                if ((DateTime.UtcNow - _cachedIpAtUtc) <= LocalIpCacheTtl && !string.IsNullOrEmpty(_cachedIp))
                    return _cachedIp;

                List<SubnetInfo> interfaces = DetectActiveLanSubnets();
                _cachedSubnets = interfaces;
                _cachedIp = interfaces.Count > 0 ? interfaces[0].LocalIp.ToString() : "127.0.0.1";
                _cachedIpAtUtc = DateTime.UtcNow;
                return _cachedIp;
            }
        }

        public static List<SubnetInfo> DetectActiveLanSubnets()
        {
            var list = new List<SubnetInfo>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up)
                        continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                        continue;

                    string desc = (nic.Description ?? string.Empty).ToLowerInvariant();
                    string name = (nic.Name ?? string.Empty).ToLowerInvariant();
                    if (desc.Contains("virtual") || desc.Contains("vmware") || desc.Contains("vbox") ||
                        desc.Contains("hyper-v") || desc.Contains("wsl") || desc.Contains("tailscale") ||
                        desc.Contains("zerotier") || desc.Contains("vpn") || desc.Contains("tap-") ||
                        name.Contains("veth"))
                    {
                        continue;
                    }

                    IPInterfaceProperties props = nic.GetIPProperties();
                    foreach (UnicastIPAddressInformation ua in props.UnicastAddresses)
                    {
                        if (ua.Address == null || ua.Address.AddressFamily != AddressFamily.InterNetwork)
                            continue;
                        if (IPAddress.IsLoopback(ua.Address))
                            continue;

                        string ipStr = ua.Address.ToString();
                        if (ipStr.StartsWith("169.254.", StringComparison.Ordinal))
                            continue;

                        IPAddress mask = ua.IPv4Mask ?? IPAddress.Parse("255.255.255.0");
                        IPAddress broadcast = ComputeBroadcast(ua.Address, mask);
                        list.Add(new SubnetInfo
                        {
                            LocalIp = ua.Address,
                            SubnetMask = mask,
                            BroadcastAddress = broadcast
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("Discovery.Net", $"Error detectando adaptadores de red ({ex.Message}).");
            }

            return list;
        }

        public static IPAddress ComputeBroadcast(IPAddress ip, IPAddress mask)
        {
            byte[] ipBytes = ip.GetAddressBytes();
            byte[] maskBytes = mask.GetAddressBytes();
            if (ipBytes.Length != 4 || maskBytes.Length != 4)
                return IPAddress.Broadcast;

            byte[] bcast = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                bcast[i] = (byte)(ipBytes[i] | (~maskBytes[i]));
            }
            return new IPAddress(bcast);
        }

        public void Start(PeerInfo localProfile)
        {
            _localProfile = localProfile;
            try
            {
                _localFingerprint = DeviceIdentityStore.GetOrCreateThumbprintSha256();
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("Discovery.Init", $"No se pudo leer huella local ({ex.Message}).");
                _localFingerprint = string.Empty;
            }

            LoadInitialContacts();

            if (_running)
                return;

            _cts = new CancellationTokenSource();
            _running = true;

            try
            {
                _listener = new UdpClient(AddressFamily.InterNetwork);
                _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listener.EnableBroadcast = true;
                _listener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

                _ = Task.Run(() => ListenLoopAsync(_cts.Token));
                _ = Task.Run(() => AnnounceLoopAsync(_cts.Token));
                _ = Task.Run(() => PresenceLoopAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Error("Discovery.Start", "Error iniciando servicio de descubrimiento UDP.", ex);
            }
        }

        public void UpdateLocalProfile(PeerInfo profile)
        {
            _localProfile = profile;
            _ = Task.Run(() => BroadcastOnceAsync());
        }

        private void LoadInitialContacts()
        {
            List<PeerInfo> saved = PeerContactsStore.LoadContacts();
            lock (_sync)
            {
                _peersByKey.Clear();
                foreach (PeerInfo p in saved)
                {
                    if (IsSelf(p))
                        continue;

                    string key = PeerContactsStore.BuildContactKey(p);
                    if (!string.IsNullOrEmpty(key))
                        _peersByKey[key] = p;
                }
            }
        }

        private bool IsSelf(PeerInfo p)
        {
            if (p == null)
                return false;

            string fp = PeerAuthorizationStore.NormalizeFingerprint(p.FingerprintSha256);
            if (!string.IsNullOrWhiteSpace(_localFingerprint) &&
                !string.IsNullOrWhiteSpace(fp) &&
                string.Equals(fp, _localFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string localIp = GetLocalIPv4Cached();
            if (!string.IsNullOrWhiteSpace(p.IPv4) &&
                string.Equals(p.IPv4.Trim(), localIp, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.MachineName ?? string.Empty, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public PeerInfo UpsertPeer(string fullName, string area, string realRemoteIp, string machineName, string fingerprintSha256)
        {
            string cleanName = UserProfileStore.SanitizeField(fullName);
            if (string.IsNullOrWhiteSpace(cleanName) || UserProfileStore.IsIgnoredSystemName(cleanName))
                return null;

            string cleanArea = UserProfileStore.SanitizeField(area);
            string cleanMachine = UserProfileStore.SanitizeField(machineName);
            string cleanFp = PeerAuthorizationStore.NormalizeFingerprint(fingerprintSha256);
            string cleanIp = (realRemoteIp ?? string.Empty).Trim();

            var candidate = new PeerInfo
            {
                FullName = cleanName,
                Area = cleanArea,
                IPv4 = cleanIp,
                MachineName = cleanMachine,
                FingerprintSha256 = cleanFp,
                LastSeenUtc = DateTime.UtcNow
            };

            if (IsSelf(candidate))
                return null;

            string key = PeerContactsStore.BuildContactKey(candidate);
            if (string.IsNullOrEmpty(key))
                return null;

            bool isBrandNew = false;
            bool changed = false;

            lock (_sync)
            {
                if (!_peersByKey.TryGetValue(key, out PeerInfo existing))
                {
                    isBrandNew = true;
                    changed = true;
                    _peersByKey[key] = candidate;
                }
                else
                {
                    bool wasOnline = IsPeerOnline(existing);
                    if (!wasOnline ||
                        existing.FullName != candidate.FullName ||
                        existing.Area != candidate.Area ||
                        existing.IPv4 != candidate.IPv4 ||
                        existing.MachineName != candidate.MachineName ||
                        existing.FingerprintSha256 != candidate.FingerprintSha256)
                    {
                        changed = true;
                    }

                    existing.FullName = candidate.FullName;
                    existing.Area = candidate.Area;
                    if (!string.IsNullOrWhiteSpace(candidate.IPv4))
                        existing.IPv4 = candidate.IPv4;
                    if (!string.IsNullOrWhiteSpace(candidate.MachineName))
                        existing.MachineName = candidate.MachineName;
                    if (!string.IsNullOrWhiteSpace(candidate.FingerprintSha256))
                        existing.FingerprintSha256 = candidate.FingerprintSha256;
                    existing.LastSeenUtc = DateTime.UtcNow;
                }

                if (changed)
                    PeerContactsStore.SaveContacts(_peersByKey.Values);
            }

            if (isBrandNew)
                NewPeerDiscovered?.Invoke(candidate);

            if (changed)
                PeersUpdated?.Invoke();

            return candidate;
        }

        public List<PeerInfo> GetPeers(string filterText = null)
        {
            List<PeerInfo> snapshot;
            lock (_sync)
            {
                snapshot = _peersByKey.Values.Where(p => !IsSelf(p)).ToList();
            }

            if (!string.IsNullOrWhiteSpace(filterText))
            {
                string q = filterText.Trim().ToLowerInvariant();
                snapshot = snapshot.Where(p =>
                    (p.FullName ?? string.Empty).ToLowerInvariant().Contains(q) ||
                    (p.Area ?? string.Empty).ToLowerInvariant().Contains(q) ||
                    (p.IPv4 ?? string.Empty).ToLowerInvariant().Contains(q) ||
                    (p.MachineName ?? string.Empty).ToLowerInvariant().Contains(q) ||
                    (p.FingerprintSha256 ?? string.Empty).ToLowerInvariant().Contains(q)).ToList();
            }

            return snapshot
                .OrderByDescending(IsPeerOnline)
                .ThenBy(p => p.FullName)
                .ThenBy(p => p.MachineName)
                .ToList();
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _running)
            {
                try
                {
                    UdpReceiveResult res = await _listener.ReceiveAsync().ConfigureAwait(false);
                    string text = Encoding.UTF8.GetString(res.Buffer);
                    string[] parts = text.Split('|');
                    if (parts.Length < 4 || !string.Equals(parts[0], MagicPrefix, StringComparison.Ordinal))
                        continue;

                    string fp = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                    string name = parts.Length > 2 ? parts[2].Trim() : string.Empty;
                    string area = parts.Length > 3 ? parts[3].Trim() : string.Empty;
                    string machine = parts.Length > 4 ? parts[4].Trim() : string.Empty;

                    string sourceIp = (res.RemoteEndPoint != null && res.RemoteEndPoint.Address != null)
                        ? res.RemoteEndPoint.Address.ToString()
                        : string.Empty;

                    UpsertPeer(name, area, sourceIp, machine, fp);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    if (_running && !token.IsCancellationRequested)
                        FileLogger.Instance.Warn("Discovery.Listen", $"Error de socket UDP ({ex.Message}).");
                    await Task.Delay(500, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_running && !token.IsCancellationRequested)
                        FileLogger.Instance.Warn("Discovery.Listen", $"Error procesando anuncio UDP ({ex.Message}).");
                }
            }
        }

        private async Task AnnounceLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _running)
            {
                await BroadcastOnceAsync().ConfigureAwait(false);
                try
                {
                    await Task.Delay(AnnounceIntervalMs, token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        public async Task BroadcastOnceAsync()
        {
            try
            {
                if (_localProfile == null ||
                    string.IsNullOrWhiteSpace(_localProfile.FullName) ||
                    UserProfileStore.IsIgnoredSystemName(_localProfile.FullName))
                {
                    return;
                }

                string payload = string.Join("|",
                    MagicPrefix,
                    _localFingerprint ?? string.Empty,
                    UserProfileStore.SanitizeField(_localProfile.FullName),
                    UserProfileStore.SanitizeField(_localProfile.Area),
                    UserProfileStore.SanitizeField(Environment.MachineName));

                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                List<SubnetInfo> subnets = DetectActiveLanSubnets();

                using (var sender = new UdpClient(AddressFamily.InterNetwork))
                {
                    sender.EnableBroadcast = true;
                    foreach (SubnetInfo sub in subnets)
                    {
                        try
                        {
                            await sender.SendAsync(bytes, bytes.Length, new IPEndPoint(sub.BroadcastAddress, DiscoveryPort)).ConfigureAwait(false);
                        }
                        catch (SocketException ex)
                        {
                            FileLogger.Instance.Warn("Discovery.Broadcast", $"Fallo enviando anuncio en {sub.BroadcastAddress} ({ex.Message}).");
                        }
                    }

                    try
                    {
                        await sender.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort)).ConfigureAwait(false);
                    }
                    catch (SocketException)
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("Discovery.Broadcast", $"Error general en anuncio UDP ({ex.Message}).");
            }
        }

        private async Task PresenceLoopAsync(CancellationToken token)
        {
            Dictionary<string, bool> lastOnline = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            while (!token.IsCancellationRequested && _running)
            {
                try
                {
                    List<PeerInfo> snapshot;
                    lock (_sync)
                    {
                        snapshot = _peersByKey.Values.ToList();
                    }

                    var sem = new SemaphoreSlim(8);
                    var checks = new List<Task>();

                    foreach (PeerInfo p in snapshot)
                    {
                        if (string.IsNullOrWhiteSpace(p.IPv4))
                            continue;

                        checks.Add(Task.Run(async () =>
                        {
                            await sem.WaitAsync(token).ConfigureAwait(false);
                            try
                            {
                                bool open = await ProbeTcpPortAsync(p.IPv4, SecureTransferProtocol.DefaultOrTransferPortFallback(), 900, token).ConfigureAwait(false);
                                if (open)
                                {
                                    lock (_sync)
                                    {
                                        string key = PeerContactsStore.BuildContactKey(p);
                                        if (_peersByKey.TryGetValue(key, out PeerInfo current))
                                            current.LastSeenUtc = DateTime.UtcNow;
                                    }
                                }
                            }
                            finally
                            {
                                sem.Release();
                            }
                        }, token));
                    }

                    await Task.WhenAll(checks).ConfigureAwait(false);

                    bool statusChanged = false;
                    lock (_sync)
                    {
                        foreach (var kv in _peersByKey)
                        {
                            bool nowOnline = IsPeerOnline(kv.Value);
                            if (!lastOnline.TryGetValue(kv.Key, out bool prev) || prev != nowOnline)
                            {
                                lastOnline[kv.Key] = nowOnline;
                                statusChanged = true;
                            }
                        }
                    }

                    if (statusChanged)
                        PeersUpdated?.Invoke();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    FileLogger.Instance.Warn("Discovery.Presence", $"Error comprobando presencia ({ex.Message}).");
                }

                try
                {
                    await Task.Delay(PresenceCheckIntervalMs, token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private static async Task<bool> ProbeTcpPortAsync(string host, int port, int timeoutMs, CancellationToken token)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    Task connectTask = client.ConnectAsync(host, port);
                    Task delayTask = Task.Delay(timeoutMs, token);
                    Task completed = await Task.WhenAny(connectTask, delayTask).ConfigureAwait(false);
                    if (completed != connectTask || !client.Connected)
                        return false;

                    await connectTask.ConfigureAwait(false);
                    return client.Connected;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Dispose()
        {
            _running = false;
            try { _cts?.Cancel(); }
            catch (ObjectDisposedException) { }
            try { _listener?.Close(); }
            catch (SocketException) { }
            try { _listener?.Dispose(); }
            catch (ObjectDisposedException) { }
            try { _cts?.Dispose(); }
            catch (ObjectDisposedException) { }
        }
    }

    internal static class ProtocolPortHelper
    {
        public static int DefaultOrTransferPortFallback()
        {
            return 5050;
        }
    }
}
