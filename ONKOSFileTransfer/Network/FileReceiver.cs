using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ONKOSFileTransfer.Network
{
    public sealed class ReceivedBatchSummary
    {
        public string SenderName { get; set; }
        public string SenderIp { get; set; }
        public string DestinationFolder { get; set; }
        public List<string> FilePaths { get; } = new List<string>();
    }

    public class FileReceiver
    {
        private const string DefaultClinicToken = "ONKOS_CLINICA_2026_KEY_9F4A";

        private sealed class PendingBatchNotification
        {
            public string SenderName;
            public string SenderIp;
            public string DestinationFolder;
            public readonly List<string> FilePaths = new List<string>();
            public CancellationTokenSource FlushCts;
        }

        private TcpListener listener;
        private static readonly object SenderFolderLock = new object();
        private readonly object _batchLock = new object();
        private readonly Dictionary<string, PendingBatchNotification> _pendingBatches =
            new Dictionary<string, PendingBatchNotification>(StringComparer.OrdinalIgnoreCase);

        public bool IsRunning { get; private set; }
        public event Action<string> FileReceived;
        public event Action<ReceivedBatchSummary> BatchReceived;
        public event Action<string> Error;
        public event Action<string, long, long> TransferProgress;

        private static string GetNetworkAuthToken()
        {
            try
            {
                string keyPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ONKOS",
                    "clave_red.txt");

                if (File.Exists(keyPath))
                {
                    string custom = File.ReadAllText(keyPath, Encoding.UTF8).Trim();
                    if (!string.IsNullOrWhiteSpace(custom))
                        return custom;
                }
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("FileReceiver", "No se pudo leer clave_red.txt; usando clave predeterminada.", ex);
            }

            return DefaultClinicToken;
        }

        public void Start(int port, string folder)
        {
            if (IsRunning)
                return;

            string initialFolder = ResolveActiveInboxFolder(folder);
            Directory.CreateDirectory(initialFolder);
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            IsRunning = true;
            _ = AcceptLoop(folder);
        }

        private static string ResolveActiveInboxFolder(string fallbackFolder)
        {
            try
            {
                string settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ONKOS",
                    "ruta_recibidos.txt");

                if (File.Exists(settingsPath))
                {
                    string configured = File.ReadAllText(settingsPath, Encoding.UTF8).Trim();
                    if (!string.IsNullOrWhiteSpace(configured))
                        return configured;
                }
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("FileReceiver", "No se pudo leer ruta_recibidos.txt; usando ruta de respaldo.", ex);
            }

            return fallbackFolder;
        }

        private async Task AcceptLoop(string fallbackFolder)
        {
            try
            {
                while (IsRunning)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => ReceiveFile(client, fallbackFolder));
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
            catch (Exception ex)
            {
                OnkosLogger.Error("FileReceiver", "Error en AcceptLoop.", ex);
                if (IsRunning)
                    Error?.Invoke(ex.Message);
            }
        }

        private static bool IsSystemAccount(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;

            string upper = name.Trim().ToUpperInvariant();
            return upper == "SYSTEM" ||
                   upper == "NT AUTHORITY\\SYSTEM" ||
                   upper == "LOCAL SERVICE" ||
                   upper == "NETWORK SERVICE";
        }

        private static (bool Valid, string FullName, string Area) ReadLocalProfileSafe()
        {
            try
            {
                string profilePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ONKOS",
                    "perfil.txt");

                if (File.Exists(profilePath))
                {
                    string[] lines = File.ReadAllLines(profilePath, Encoding.UTF8);
                    string name = lines.Length > 0 ? lines[0].Trim() : string.Empty;
                    string area = lines.Length > 1 ? lines[1].Trim() : string.Empty;

                    if (!IsSystemAccount(name) && !string.IsNullOrWhiteSpace(area))
                        return (true, name, area);
                }
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("FileReceiver", "Error leyendo perfil local.", ex);
            }

            return (false, string.Empty, string.Empty);
        }

        private static string GetLocalIPv4Safe()
        {
            try
            {
                foreach (IPAddress addr in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (addr.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr))
                        return addr.ToString();
                }
            }
            catch (Exception ex)
            {
                OnkosLogger.Warn("FileReceiver", "Error obteniendo IPv4 local.", ex);
            }

            return "127.0.0.1";
        }

        private static string ResolveSenderDisplayName(string headerSenderName, string senderIp)
        {
            if (!string.IsNullOrWhiteSpace(headerSenderName) && !IsSystemAccount(headerSenderName))
                return headerSenderName.Trim();

            try
            {
                string contactsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ONKOS",
                    "contactos.txt");

                if (File.Exists(contactsPath))
                {
                    foreach (string line in File.ReadAllLines(contactsPath, Encoding.UTF8))
                    {
                        string[] parts = line.Split('|');
                        if (parts.Length >= 3 &&
                            string.Equals(parts[2].Trim(), senderIp, StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(parts[0]))
                        {
                            return parts[0].Trim();
                        }
                    }
                }
            }
            catch
            {
            }

            return senderIp;
        }

        private void QueueBatchNotification(string batchKey, string senderName, string senderIp, string destinationPath)
        {
            PendingBatchNotification entry;
            CancellationToken token;

            lock (_batchLock)
            {
                if (!_pendingBatches.TryGetValue(batchKey, out entry))
                {
                    entry = new PendingBatchNotification
                    {
                        SenderName = senderName,
                        SenderIp = senderIp,
                        DestinationFolder = Path.GetDirectoryName(destinationPath)
                    };
                    _pendingBatches[batchKey] = entry;
                }

                if (!string.IsNullOrWhiteSpace(senderName))
                    entry.SenderName = senderName;

                entry.FilePaths.Add(destinationPath);

                try { entry.FlushCts?.Cancel(); } catch { }
                try { entry.FlushCts?.Dispose(); } catch { }
                entry.FlushCts = new CancellationTokenSource();
                token = entry.FlushCts.Token;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1200, token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                ReceivedBatchSummary summary = null;
                lock (_batchLock)
                {
                    if (_pendingBatches.TryGetValue(batchKey, out PendingBatchNotification ready) &&
                        ready.FlushCts != null &&
                        ready.FlushCts.Token == token)
                    {
                        _pendingBatches.Remove(batchKey);
                        try { ready.FlushCts.Dispose(); } catch { }

                        summary = new ReceivedBatchSummary
                        {
                            SenderName = ready.SenderName,
                            SenderIp = ready.SenderIp,
                            DestinationFolder = ready.DestinationFolder
                        };
                        summary.FilePaths.AddRange(ready.FilePaths);
                    }
                }

                if (summary != null && summary.FilePaths.Count > 0)
                    BatchReceived?.Invoke(summary);
            });
        }

        private async Task ReceiveFile(TcpClient client, string fallbackFolder)
        {
            string destination = null;
            bool transferStarted = false;

            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, true))
                {
                    string firstToken;
                    try
                    {
                        firstToken = reader.ReadString();
                    }
                    catch (EndOfStreamException)
                    {
                        return;
                    }
                    catch (IOException)
                    {
                        return;
                    }

                    if (string.IsNullOrEmpty(firstToken))
                        return;

                    if (string.Equals(firstToken, "ONKOS_WHOAMI_V1", StringComparison.Ordinal))
                    {
                        var profile = ReadLocalProfileSafe();
                        string localIp = GetLocalIPv4Safe();
                        string machine = Environment.MachineName;

                        using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true))
                        {
                            if (profile.Valid)
                            {
                                writer.Write("ONKOS_IDENTITY_V1");
                                writer.Write(profile.FullName);
                                writer.Write(profile.Area);
                                writer.Write(localIp);
                                writer.Write(machine);
                            }
                            else
                            {
                                writer.Write("ONKOS_UNREGISTERED_V1");
                            }
                            writer.Flush();
                        }
                        return;
                    }

                    if (!string.Equals(firstToken, "ONKOS_FILE_V2", StringComparison.Ordinal))
                    {
                        OnkosLogger.Warn("FileReceiver", $"Conexión rechazada: protocolo no autorizado ('{firstToken}').");
                        stream.WriteByte(2);
                        await stream.FlushAsync();
                        return;
                    }

                    string clientToken = reader.ReadString();
                    string expectedToken = GetNetworkAuthToken();
                    if (!string.Equals(clientToken, expectedToken, StringComparison.Ordinal))
                    {
                        OnkosLogger.Warn("FileReceiver", "Transferencia rechazada: clave de red clínica inválida.");
                        stream.WriteByte(2);
                        await stream.FlushAsync();
                        return;
                    }

                    string name = Path.GetFileName(reader.ReadString());
                    long size = reader.ReadInt64();
                    string expectedSha256 = (reader.ReadString() ?? string.Empty).Trim().ToLowerInvariant();
                    string headerSenderName = reader.ReadString();
                    string headerBatchId = reader.ReadString();

                    if (string.IsNullOrWhiteSpace(name) || name == "." || name == ".." ||
                        size < 0 || size > 20L * 1024 * 1024 * 1024 ||
                        expectedSha256.Length != 64)
                    {
                        throw new InvalidDataException("Encabezado de archivo no válido o tamaño fuera del límite.");
                    }

                    transferStarted = true;

                    string senderIp = "IP_desconocida";
                    try
                    {
                        if (client.Client.RemoteEndPoint is IPEndPoint remote && remote.Address != null)
                            senderIp = remote.Address.ToString().Replace(':', '_');
                    }
                    catch
                    {
                    }

                    string resolvedSenderName = ResolveSenderDisplayName(headerSenderName, senderIp);
                    string batchKey = !string.IsNullOrWhiteSpace(headerBatchId)
                        ? $"{senderIp}_{headerBatchId.Trim()}"
                        : $"{senderIp}_{DateTime.UtcNow:yyyyMMddHHmmss}";

                    string activeRootFolder = ResolveActiveInboxFolder(fallbackFolder);
                    string senderFolder = GetSenderFolder(senderIp, activeRootFolder);
                    destination = Path.Combine(senderFolder, name);
                    if (File.Exists(destination))
                    {
                        destination = Path.Combine(senderFolder,
                            Path.GetFileNameWithoutExtension(name) + "_" +
                            DateTime.Now.ToString("yyyyMMdd_HHmmssfff") +
                            Path.GetExtension(name));
                    }

                    string actualSha256;
                    using (var sha = SHA256.Create())
                    using (FileStream output = File.Create(destination))
                    {
                        byte[] buffer = new byte[81920];
                        long receivedTotal = 0;
                        while (receivedTotal < size)
                        {
                            int count = (int)Math.Min(buffer.Length, size - receivedTotal);
                            int received = await stream.ReadAsync(buffer, 0, count);
                            if (received == 0)
                                throw new EndOfStreamException("La conexión se interrumpió antes de completar el archivo.");

                            sha.TransformBlock(buffer, 0, received, null, 0);
                            await output.WriteAsync(buffer, 0, received);
                            receivedTotal += received;
                            TransferProgress?.Invoke(name, receivedTotal, size);
                        }

                        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        actualSha256 = BitConverter.ToString(sha.Hash).Replace("-", string.Empty).ToLowerInvariant();
                    }

                    if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        OnkosLogger.Error("FileReceiver", $"Integridad SHA-256 fallida para '{name}'. Esperado={expectedSha256}, Recibido={actualSha256}");
                        try { if (File.Exists(destination)) File.Delete(destination); } catch { }
                        destination = null;
                        stream.WriteByte(3);
                        await stream.FlushAsync();
                        Error?.Invoke($"El archivo '{name}' llegó corrupto (error SHA-256) y fue descartado.");
                        return;
                    }

                    if (string.Equals(Path.GetExtension(destination), ".onkosfolder", StringComparison.OrdinalIgnoreCase))
                    {
                        string folderName = Path.GetFileNameWithoutExtension(destination);
                        int guidSeparator = folderName.LastIndexOf('_');
                        Guid packageId;
                        if (guidSeparator > 0 && Guid.TryParse(folderName.Substring(guidSeparator + 1), out packageId))
                            folderName = folderName.Substring(0, guidSeparator);
                        if (string.IsNullOrWhiteSpace(folderName))
                            folderName = "Carpeta_recibida_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        string extractRoot = Path.Combine(senderFolder, folderName);
                        int suffix = 1;
                        while (Directory.Exists(extractRoot))
                            extractRoot = Path.Combine(senderFolder, folderName + "_" + suffix++);

                        Directory.CreateDirectory(extractRoot);
                        string rootFull = Path.GetFullPath(extractRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                        using (ZipArchive archive = ZipFile.OpenRead(destination))
                        {
                            foreach (ZipArchiveEntry entry in archive.Entries)
                            {
                                string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                                string target = Path.GetFullPath(Path.Combine(extractRoot, relative));
                                if (!target.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                                    throw new InvalidDataException("La carpeta contiene una ruta no válida.");
                                if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                                {
                                    Directory.CreateDirectory(target);
                                }
                                else
                                {
                                    string parent = Path.GetDirectoryName(target);
                                    if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
                                    entry.ExtractToFile(target, false);
                                }
                            }
                        }
                        File.Delete(destination);
                        destination = extractRoot;
                    }

                    stream.WriteByte(1);
                    await stream.FlushAsync();

                    FileReceived?.Invoke(destination);
                    QueueBatchNotification(batchKey, resolvedSenderName, senderIp, destination);
                }
            }
            catch (Exception ex)
            {
                OnkosLogger.Error("FileReceiver", "Error recibiendo archivo.", ex);
                if (destination != null && File.Exists(destination))
                {
                    try { File.Delete(destination); }
                    catch (Exception delEx) { OnkosLogger.Warn("FileReceiver", "No se pudo borrar parcial.", delEx); }
                }

                if (transferStarted && IsRunning)
                    Error?.Invoke(ex.Message);
            }
        }

        private static string GetSenderFolder(string senderIp, string rootFolder)
        {
            string dayFolder = Path.Combine(rootFolder, DateTime.Now.ToString("yyyy-MM-dd"));
            lock (SenderFolderLock)
            {
                Directory.CreateDirectory(dayFolder);

                string prefix = senderIp + "_";
                string existing = null;
                try
                {
                    foreach (string candidate in Directory.GetDirectories(dayFolder, prefix + "*"))
                    {
                        if (existing == null ||
                            Directory.GetCreationTime(candidate) < Directory.GetCreationTime(existing))
                            existing = candidate;
                    }
                }
                catch (Exception ex)
                {
                    OnkosLogger.Warn("FileReceiver", "Error listando carpetas de remitente.", ex);
                }

                if (!string.IsNullOrEmpty(existing))
                    return existing;

                string folderName = senderIp + "_" + DateTime.Now.ToString("HH-mm-ss");
                string senderFolder = Path.Combine(dayFolder, folderName);
                int suffix = 1;
                while (Directory.Exists(senderFolder))
                    senderFolder = Path.Combine(dayFolder, folderName + "_" + suffix++);
                Directory.CreateDirectory(senderFolder);
                return senderFolder;
            }
        }

        public void Stop()
        {
            IsRunning = false;
            if (listener != null)
            {
                listener.Stop();
                listener = null;
            }
        }
    }
}
