using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ONKOSFileTransfer.Network
{
    public class PeerDiscoveryService : IDisposable
    {
        private const int DiscoveryPort = 5051;
        private const int TransferPort = 5050;
        private const int BroadcastIntervalMs = 2000;
        private const int SubnetScanIntervalMs = 15000;

        private static readonly string ContactsPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ONKOS", "contactos.txt");

        private readonly object _sync = new object();
        private readonly Dictionary<string, PeerInfo> _peersByKey =
            new Dictionary<string, PeerInfo>(StringComparer.OrdinalIgnoreCase);

        private UdpClient _listener;
        private CancellationTokenSource _cts;
        private PeerInfo _localProfile;
        private bool _running;

        public event Action PeersUpdated;

        public void Start(PeerInfo localProfile)
        {
            LoadSavedContacts();

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
            _ = Task.Run(() => SubnetScanLoop(_cts.Token));
        }

        private static string BuildPeerKey(string name, string ip, string machine)
        {
            if (!string.IsNullOrWhiteSpace(ip) && ip != "127.0.0.1")
                return "IP:" + ip.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(machine))
                return "PC:" + machine.Trim().ToLowerInvariant();
            return "NAME:" + (name ?? string.Empty).Trim().ToLowerInvariant();
        }

        private void LoadSavedContacts()
        {
            try
            {
                if (!File.Exists(ContactsPath))
                    return;

                bool hadInvalid = false;
                foreach (string line in File.ReadAllLines(ContactsPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    string[] parts = line.Split('|');
                    if (parts.Length < 3)
                        continue;

                    string name = parts[0].Trim();
                    string area = parts[1].Trim();
                    string ip = parts[2].Trim();
                    string machine = parts.Length > 3 ? parts[3].Trim() : string.Empty;

                    if (UserProfileStore.IsIgnoredSystemName(name) || string.IsNullOrWhiteSpace(ip))
                    {
                        hadInvalid = true;
                        continue;
                    }

                    string key = BuildPeerKey(name, ip, machine);
                    lock (_sync)
                    {
                        _peersByKey[key] = new PeerInfo
                        {
                            FullName = name,
                            Area = area,
                            IPv4 = ip,
                            MachineName = machine,
                            LastSeenUtc = DateTime.UtcNow
                        };
                    }
                }

                if (hadInvalid)
                {
                    lock (_sync)
                        SaveContactsLocked();
                }
            }
            catch
            {
            }
        }

        private void SaveContactsLocked()
        {
            try
            {
                string dir = Path.GetDirectoryName(ContactsPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var lines = _peersByKey.Values
                    .Where(p => !UserProfileStore.IsIgnoredSystemName(p.FullName))
                    .OrderBy(p => p.FullName)
                    .Select(p => string.Join("|",
                        (p.FullName ?? string.Empty).Replace("|", "/"),
                        (p.Area ?? string.Empty).Replace("|", "/"),
                        p.IPv4 ?? string.Empty,
                        p.MachineName ?? string.Empty))
                    .ToArray();

                File.WriteAllLines(ContactsPath, lines, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private bool UpsertPeer(string name, string area, string ip, string machine)
        {
            if (UserProfileStore.IsIgnoredSystemName(name) || string.IsNullOrWhiteSpace(ip))
                return false;

            string localIp = NetworkManager.GetLocalIPv4();
            bool isSameMachine = !string.IsNullOrWhiteSpace(machine) &&
                string.Equals(machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
            bool isSameIp = string.Equals(ip, localIp, StringComparison.OrdinalIgnoreCase);

            if (isSameMachine || isSameIp)
                return false;

            string cleanName = name.Replace("|", "/").Trim();
            string cleanArea = (area ?? string.Empty).Replace("|", "/").Trim();
            string cleanIp = ip.Trim();
            string cleanMachine = (machine ?? string.Empty).Trim();
            string key = BuildPeerKey(cleanName, cleanIp, cleanMachine);

            bool changed = false;
            lock (_sync)
            {
                bool existed = _peersByKey.TryGetValue(key, out PeerInfo existing);
                if (!existed ||
                    existing.FullName != cleanName ||
                    existing.Area != cleanArea ||
                    existing.IPv4 != cleanIp ||
                    existing.MachineName != cleanMachine)
                {
                    changed = true;
                }

                _peersByKey[key] = new PeerInfo
                {
                    FullName = cleanName,
                    Area = cleanArea,
                    IPv4 = cleanIp,
                    MachineName = cleanMachine,
                    LastSeenUtc = DateTime.UtcNow
                };

                if (changed)
                    SaveContactsLocked();
            }

            if (changed)
                PeersUpdated?.Invoke();

            return changed;
        }

        public PeerInfo RegisterManualContact(string fullName, string area, string ipv4)
        {
            string cleanName = (fullName ?? string.Empty).Replace("|", "/").Trim();
            string cleanArea = string.IsNullOrWhiteSpace(area) ? "Manual" : area.Replace("|", "/").Trim();
            string cleanIp = (ipv4 ?? string.Empty).Trim();

            if (UserProfileStore.IsIgnoredSystemName(cleanName) ||
                !IPAddress.TryParse(cleanIp, out IPAddress parsed) ||
                parsed.AddressFamily != AddressFamily.InterNetwork)
            {
                return null;
            }

            var peer = new PeerInfo
            {
                FullName = cleanName,
                Area = cleanArea,
                IPv4 = parsed.ToString(),
                MachineName = string.Empty,
                LastSeenUtc = DateTime.UtcNow
            };

            string key = BuildPeerKey(cleanName, peer.IPv4, string.Empty);
            lock (_sync)
            {
                _peersByKey[key] = peer;
                SaveContactsLocked();
            }

            PeersUpdated?.Invoke();
            return peer;
        }

        public void UpdateLocalProfile(PeerInfo profile)
        {
            _localProfile = profile;
            if (_localProfile != null && UserProfileStore.IsIgnoredSystemName(_localProfile.FullName))
                _localProfile = null;

            string localIp = NetworkManager.GetLocalIPv4();
            lock (_sync)
            {
                var keysToRemove = _peersByKey
                    .Where(kv =>
                        string.Equals(kv.Value.IPv4, localIp, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(kv.Value.MachineName) &&
                         string.Equals(kv.Value.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase)) ||
                        UserProfileStore.IsIgnoredSystemName(kv.Value.FullName))
                    .Select(kv => kv.Key)
                    .ToList();

                foreach (string k in keysToRemove)
                    _peersByKey.Remove(k);

                SaveContactsLocked();
            }

            PeersUpdated?.Invoke();

            _ = Task.Run(async () =>
            {
                await BroadcastOnce();
                await Task.Delay(250);
                await BroadcastOnce();
            });
        }

        public List<PeerInfo> GetActivePeers(string filterText = null)
        {
            List<PeerInfo> snapshot;
            lock (_sync)
            {
                snapshot = _peersByKey.Values
                    .Where(p => !UserProfileStore.IsIgnoredSystemName(p.FullName))
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

        public PeerInfo FindPeerByNameOrIp(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            string clean = input.Trim();
            List<PeerInfo> active = GetActivePeers();

            PeerInfo exact = active.FirstOrDefault(p =>
                string.Equals(p.FullName, clean, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.DisplayText, clean, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            PeerInfo byIp = active.FirstOrDefault(p =>
                string.Equals(p.IPv4, clean, StringComparison.OrdinalIgnoreCase));
            if (byIp != null)
                return byIp;

            return active.FirstOrDefault(p =>
                (!string.IsNullOrEmpty(p.FullName) && p.FullName.IndexOf(clean, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrEmpty(p.Area) && p.Area.IndexOf(clean, StringComparison.OrdinalIgnoreCase) >= 0));
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

                    UpsertPeer(name, area, ip, machine);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(300, token);
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
                if (_localProfile == null || UserProfileStore.IsIgnoredSystemName(_localProfile.FullName))
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
                        string subnetBroadcast = octets[0] + "." + octets[1] + "." + octets[2] + ".255";
                        if (IPAddress.TryParse(subnetBroadcast, out IPAddress subIp))
                            await sender.SendAsync(data, data.Length, new IPEndPoint(subIp, DiscoveryPort));
                    }
                }
            }
            catch
            {
            }
        }

        private async Task SubnetScanLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _running)
            {
                try
                {
                    string localIp = NetworkManager.GetLocalIPv4();
                    string[] octets = localIp.Split('.');
                    if (octets.Length == 4)
                    {
                        string prefix = octets[0] + "." + octets[1] + "." + octets[2] + ".";
                        var semaphore = new SemaphoreSlim(32);
                        var tasks = new List<Task>();

                        for (int host = 1; host <= 254; host++)
                        {
                            if (token.IsCancellationRequested || !_running)
                                break;

                            string candidateIp = prefix + host;
                            if (string.Equals(candidateIp, localIp, StringComparison.OrdinalIgnoreCase))
                                continue;

                            tasks.Add(Task.Run(async () =>
                            {
                                await semaphore.WaitAsync(token);
                                try
                                {
                                    var id = await FileSender.QueryPeerIdentityAsync(candidateIp, TransferPort, 350);
                                    if (!string.IsNullOrWhiteSpace(id.FullName) &&
                                        !UserProfileStore.IsIgnoredSystemName(id.FullName))
                                    {
                                        UpsertPeer(id.FullName, id.Area, id.IPv4 ?? candidateIp, id.MachineName);
                                    }
                                }
                                finally
                                {
                                    semaphore.Release();
                                }
                            }, token));
                        }

                        await Task.WhenAll(tasks);
                    }
                }
                catch
                {
                }

                try
                {
                    await Task.Delay(SubnetScanIntervalMs, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
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
}
