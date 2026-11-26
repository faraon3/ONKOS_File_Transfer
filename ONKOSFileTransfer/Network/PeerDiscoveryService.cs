using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private const int BroadcastIntervalMs = 2500;
        private const int SubnetScanIntervalMs = 15000;
        private const int PresenceCheckIntervalMs = 4000;
        private const string ContactsMutexName = "Global\\ONKOS_ContactsStore_Mutex";
        private static readonly TimeSpan PeerOnlineTimeout = TimeSpan.FromSeconds(60);

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

        public static bool IsPeerOnline(PeerInfo peer)
        {
            if (peer == null)
                return false;

            if (peer.LastSeenUtc == DateTime.MinValue)
                return false;

            return (DateTime.UtcNow - peer.LastSeenUtc) <= PeerOnlineTimeout;
        }

        private static void WithContactsFileLock(Action action)
        {
            using (var mutex = new Mutex(false, ContactsMutexName))
            {
                bool acquired = false;
                try
                {
                    try
                    {
                        acquired = mutex.WaitOne(TimeSpan.FromSeconds(4));
                    }
                    catch (AbandonedMutexException)
                    {
                        acquired = true;
                    }

                    if (acquired)
                        action();
                }
                finally
                {
                    if (acquired)
                    {
                        try { mutex.ReleaseMutex(); }
                        catch (Exception ex) { OnkosLogger.Warn("PeerDiscoveryService", "Error liberando Mutex de contactos.", ex); }
                    }
                }
            }
        }

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
            _ = Task.Run(() => PresenceMonitorLoop(_cts.Token));
        }

        private static string BuildPeerKey(string ip)
        {
            return "IP:" + (ip ?? string.Empty).Trim().ToLowerInvariant();
        }

        private void LoadSavedContacts()
        {
            try
            {
                string[] lines = Array.Empty<string>();
                WithContactsFileLock(() =>
                {
                    if (File.Exists(ContactsPath))
                        lines = File.ReadAllLines(ContactsPath, Encoding.UTF8);
                });

                if (lines.Length == 0)
                    return;

                bool hadInvalid = false;
                foreach (string line in lines)
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

                    if (UserProfileStore.IsIgnoredSystemName(name) || !FileSender.TryParseStrictIPv4(ip, out IPAddress parsedIp))
                    {
                        hadInvalid = true;
                        continue;
                    }

                    string cleanIp = parsedIp.ToString();
                    string key = BuildPeerKey(cleanIp);
                    lock (_sync)
                    {
                        if (!_peersByKey.ContainsKey(key))
                        {
                            _peersByKey[key] = new PeerInfo
                            {
                                FullName = name,
                                Area = area,
                                IPv4 = cleanIp,
                                MachineName = machine,
                                LastSeenUtc = DateTime.MinValue
                            };
                        }
                    }
                }

                if (hadInvalid)
                {
                    lock (_sync)
                        SaveContactsLocked();
                }
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("PeerDiscoveryService", "Error cargando contactos guardados.", ex);
            }
        }

        private void SaveContactsLocked()
        {
            try
            {
                string[] lines = _peersByKey.Values
                    .Where(p => !UserProfileStore.IsIgnoredSystemName(p.FullName) &&
                                FileSender.TryParseStrictIPv4(p.IPv4, out _))
                    .OrderBy(p => p.FullName)
                    .Select(p => string.Join("|",
                        (p.FullName ?? string.Empty).Replace("|", "/"),
                        (p.Area ?? string.Empty).Replace("|", "/"),
                        p.IPv4 ?? string.Empty,
                        p.MachineName ?? string.Empty))
                    .ToArray();

                WithContactsFileLock(() =>
                {
                    string dir = Path.GetDirectoryName(ContactsPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    string tempFile = $"{ContactsPath}.{Process.GetCurrentProcess().Id}.{Guid.NewGuid():N}.tmp";
                    try
                    {
                        File.WriteAllLines(tempFile, lines, new UTF8Encoding(false));
                        if (File.Exists(ContactsPath))
                            File.Replace(tempFile, ContactsPath, null);
                        else
                            File.Move(tempFile, ContactsPath);
                    }
                    finally
                    {
                        if (File.Exists(tempFile))
                        {
                            try { File.Delete(tempFile); } catch { }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("PeerDiscoveryService", "Error guardando contactos.", ex);
            }
        }

        private bool UpsertPeer(string name, string area, string ip, string machine)
        {
            if (UserProfileStore.IsIgnoredSystemName(name) || !FileSender.TryParseStrictIPv4(ip, out IPAddress parsedIp))
                return false;

            string cleanIp = parsedIp.ToString();
            string localIp = NetworkManager.GetLocalIPv4();
            bool isSameMachine = !string.IsNullOrWhiteSpace(machine) &&
                string.Equals(machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
            bool isSameIp = string.Equals(cleanIp, localIp, StringComparison.OrdinalIgnoreCase);

            if (isSameMachine || isSameIp)
                return false;

            string cleanName = name.Replace("|", "/").Trim();
            string cleanArea = (area ?? string.Empty).Replace("|", "/").Trim();
            string cleanMachine = (machine ?? string.Empty).Trim();
            string key = BuildPeerKey(cleanIp);

            bool changed = false;
            lock (_sync)
            {
                bool existed = _peersByKey.TryGetValue(key, out PeerInfo existing);
                bool wasOnline = existed && IsPeerOnline(existing);

                if (!existed ||
                    !wasOnline ||
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

                if (!existed ||
                    existing.FullName != cleanName ||
                    existing.Area != cleanArea ||
                    existing.IPv4 != cleanIp ||
                    existing.MachineName != cleanMachine)
                {
                    SaveContactsLocked();
                }
            }

            if (changed)
                PeersUpdated?.Invoke();

            return changed;
        }

        public PeerInfo RegisterManualContact(string fullName, string area, string ipv4)
        {
            string cleanName = (fullName ?? string.Empty).Replace("|", "/").Trim();
            string cleanArea = string.IsNullOrWhiteSpace(area) ? "Manual" : area.Replace("|", "/").Trim();

            if (UserProfileStore.IsIgnoredSystemName(cleanName) ||
                !FileSender.TryParseStrictIPv4(ipv4, out IPAddress parsed))
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

            string key = BuildPeerKey(peer.IPv4);
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
                        UserProfileStore.IsIgnoredSystemName(kv.Value.FullName) ||
                        !FileSender.TryParseStrictIPv4(kv.Value.IPv4, out _))
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

        public List<PeerInfo> GetActivePeers(string filterText = null, bool onlyOnline = false)
        {
            List<PeerInfo> snapshot;
            lock (_sync)
            {
                snapshot = _peersByKey.Values
                    .Where(p => !UserProfileStore.IsIgnoredSystemName(p.FullName) &&
                                FileSender.TryParseStrictIPv4(p.IPv4, out _))
                    .Where(p => !onlyOnline || IsPeerOnline(p))
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
                .OrderByDescending(p => IsPeerOnline(p))
                .ThenBy(p => p.FullName)
                .ThenBy(p => p.Area)
                .ToList();
        }

        public PeerInfo FindPeerByNameOrIp(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            string clean = input.Trim();
            List<PeerInfo> candidates = GetActivePeers();

            PeerInfo exact = candidates.FirstOrDefault(p =>
                string.Equals(p.FullName, clean, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.DisplayText, clean, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            PeerInfo byIp = candidates.FirstOrDefault(p =>
                string.Equals(p.IPv4, clean, StringComparison.OrdinalIgnoreCase));
            if (byIp != null)
                return byIp;

            return candidates.FirstOrDefault(p =>
                (!string.IsNullOrEmpty(p.FullName) && p.FullName.IndexOf(clean, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrEmpty(p.Area) && p.Area.IndexOf(clean, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private async Task PresenceMonitorLoop(CancellationToken token)
        {
            Dictionary<string, bool> previousState = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            while (!token.IsCancellationRequested && _running)
            {
                try
                {
                    List<PeerInfo> candidates;
                    lock (_sync)
                    {
                        candidates = _peersByKey.Values
                            .Where(p => FileSender.TryParseStrictIPv4(p.IPv4, out _) &&
                                        !UserProfileStore.IsIgnoredSystemName(p.FullName))
                            .ToList();
                    }

                    foreach (PeerInfo p in candidates)
                    {
                        if (token.IsCancellationRequested || !_running)
                            break;

                        bool reachable = await FileSender.CheckPeerReachableAsync(p.IPv4, TransferPort, 1500);
                        if (reachable)
                        {
                            lock (_sync)
                            {
                                string key = BuildPeerKey(p.IPv4);
                                if (_peersByKey.TryGetValue(key, out PeerInfo current))
                                    current.LastSeenUtc = DateTime.UtcNow;
                            }
                        }
                    }

                    bool stateChanged = false;
                    lock (_sync)
                    {
                        foreach (var kv in _peersByKey)
                        {
                            bool currentOnline = IsPeerOnline(kv.Value);
                            if (!previousState.TryGetValue(kv.Key, out bool wasOnline) || wasOnline != currentOnline)
                            {
                                previousState[kv.Key] = currentOnline;
                                stateChanged = true;
                            }
                        }
                    }

                    if (stateChanged)
                        PeersUpdated?.Invoke();
                }
                catch (Exception ex)
                {
                    OnkosLogger.Warn("PeerDiscoveryService", "Error en PresenceMonitorLoop.", ex);
                }

                try
                {
                    await Task.Delay(PresenceCheckIntervalMs, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
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
                catch (Exception ex)
                {
                    OnkosLogger.Warn("PeerDiscoveryService", "Error en ListenLoop UDP.", ex);
                    try { await Task.Delay(300, token); } catch (TaskCanceledException) { break; }
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
                if (!FileSender.TryParseStrictIPv4(localIp, out _))
                    return;

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
            catch (Exception ex)
            {
                OnkosLogger.Warn("PeerDiscoveryService", "Error en BroadcastOnce.", ex);
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
                    if (octets.Length == 4 && FileSender.TryParseStrictIPv4(localIp, out _))
                    {
                        string prefix = octets[0] + "." + octets[1] + "." + octets[2] + ".";
                        using (var semaphore = new SemaphoreSlim(32))
                        {
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
                                        var id = await FileSender.QueryPeerIdentityAsync(candidateIp, TransferPort, 800);
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
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnkosLogger.Warn("PeerDiscoveryService", "Error en SubnetScanLoop.", ex);
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
            try { _cts?.Cancel(); } catch (Exception ex) { OnkosLogger.Warn("PeerDiscoveryService", "Error cancelando CTS.", ex); }
            try { _listener?.Close(); } catch (Exception ex) { OnkosLogger.Warn("PeerDiscoveryService", "Error cerrando UDP.", ex); }
            try { _listener?.Dispose(); } catch (Exception ex) { OnkosLogger.Warn("PeerDiscoveryService", "Error disponiendo UDP.", ex); }
            try { _cts?.Dispose(); } catch (Exception ex) { OnkosLogger.Warn("PeerDiscoveryService", "Error disponiendo CTS.", ex); }
        }
    }
}
