using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ONKOS.Core.Logging;
using ONKOS.Core.Storage;

namespace ONKOS.Service
{
    public sealed class TransferWindowsService : ServiceBase
    {
        private const int DefaultListenPort = 5050;
        private const long MaxSingleFileBytes = 20L * 1024L * 1024L * 1024L;
        private static readonly object SenderFolderLock = new object();

        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private bool _isStopping;

        public TransferWindowsService()
        {
            ServiceName = "ONKOSTransferService";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                PathAndAclHelper.EnsureBaseAndLogsFolders();

                string inboxFolder;
                string inboxError;
                if (!AppSettingsStore.TryEnsureInboxReady(out inboxFolder, out inboxError))
                {
                    ServiceNotifier.WriteEvent(EventLogEntryType.Error, "Service.Start", inboxError);
                    throw new IOException(inboxError);
                }

                _isStopping = false;
                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Any, DefaultListenPort);
                _listener.Start();

                _ = Task.Run(() => AcceptLoopAsync(_cts.Token));

                ServiceNotifier.WriteEvent(
                    EventLogEntryType.Information,
                    "Service.Start",
                    $"ONKOSTransferService iniciado en puerto {DefaultListenPort}. Carpeta de recibidos: {inboxFolder}");
            }
            catch (Exception ex)
            {
                ServiceNotifier.WriteEvent(EventLogEntryType.Error, "Service.Start", "Error al iniciar ONKOSTransferService.", ex);
                throw;
            }
        }

        protected override void OnStop()
        {
            _isStopping = true;
            try
            {
                try { _cts?.Cancel(); }
                catch (ObjectDisposedException) { }

                try { _listener?.Stop(); }
                catch (SocketException ex) { FileLogger.Instance.Warn("Service.Stop", "Error al detener TcpListener.", ex); }

                ServiceNotifier.WriteEvent(
                    EventLogEntryType.Information,
                    "Service.Stop",
                    "ONKOSTransferService detenido correctamente.");
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Error("Service.Stop", "Error durante la detención del servicio.", ex);
            }
            finally
            {
                try { _cts?.Dispose(); }
                catch (ObjectDisposedException) { }
                _cts = null;
                _listener = null;
            }
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && !_isStopping)
            {
                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleClientAsync(client, token), token);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    if (!_isStopping && !token.IsCancellationRequested)
                        FileLogger.Instance.Warn("Service.Accept", "Error de socket aceptando conexión.", ex);
                    break;
                }
                catch (Exception ex)
                {
                    if (!_isStopping && !token.IsCancellationRequested)
                        FileLogger.Instance.Error("Service.Accept", "Error inesperado en el bucle de recepción.", ex);
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            string destinationPath = null;
            string itemName = "Elemento recibido";
            string remoteIp = "IP_desconocida";
            long recordedSize = 0L;
            bool transferStarted = false;

            try
            {
                using (client)
                {
                    try
                    {
                        var remoteEp = client.Client.RemoteEndPoint as IPEndPoint;
                        if (remoteEp != null && remoteEp.Address != null)
                            remoteIp = remoteEp.Address.ToString();
                    }
                    catch (SocketException)
                    {
                    }

                    string inboxFolder;
                    string inboxError;
                    if (!AppSettingsStore.TryEnsureInboxReady(out inboxFolder, out inboxError))
                    {
                        ServiceNotifier.WriteEvent(EventLogEntryType.Error, "Service.Receive", inboxError);
                        return;
                    }

                    using (NetworkStream stream = client.GetStream())
                    using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
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

                        if (string.IsNullOrWhiteSpace(firstToken))
                            return;

                        if (string.Equals(firstToken, "ONKOS_WHOAMI_V1", StringComparison.Ordinal))
                        {
                            WriteIdentityResponse(stream, remoteIp);
                            return;
                        }

                        string safeName = Path.GetFileName(firstToken);
                        long declaredSize = reader.ReadInt64();
                        itemName = string.IsNullOrWhiteSpace(safeName) ? "Elemento recibido" : safeName;

                        if (string.IsNullOrWhiteSpace(safeName) || safeName == "." || safeName == ".." ||
                            declaredSize < 0L || declaredSize > MaxSingleFileBytes)
                        {
                            throw new InvalidDataException("Nombre de archivo no válido o tamaño fuera del límite permitido.");
                        }

                        string spaceReason;
                        if (!PathAndAclHelper.HasEnoughFreeSpace(inboxFolder, declaredSize, out spaceReason))
                            throw new IOException(spaceReason);

                        transferStarted = true;

                        string senderFolder = GetSenderFolder(remoteIp, inboxFolder);
                        destinationPath = Path.Combine(senderFolder, safeName);
                        if (File.Exists(destinationPath))
                        {
                            destinationPath = Path.Combine(
                                senderFolder,
                                Path.GetFileNameWithoutExtension(safeName) + "_" +
                                DateTime.Now.ToString("yyyyMMdd_HHmmssfff") +
                                Path.GetExtension(safeName));
                        }

                        string sha256Hex;
                        using (FileStream output = File.Create(destinationPath))
                        using (var hasher = SHA256.Create())
                        using (var cryptoStream = new CryptoStream(output, hasher, CryptoStreamMode.Write))
                        {
                            byte[] buffer = new byte[81920];
                            long receivedTotal = 0L;

                            while (receivedTotal < declaredSize)
                            {
                                token.ThrowIfCancellationRequested();
                                int toRead = (int)Math.Min(buffer.Length, declaredSize - receivedTotal);
                                int read = await stream.ReadAsync(buffer, 0, toRead, token).ConfigureAwait(false);
                                if (read <= 0)
                                    throw new EndOfStreamException("La conexión se interrumpió antes de completar la transferencia.");

                                await cryptoStream.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                                receivedTotal += read;
                            }

                            cryptoStream.FlushFinalBlock();
                            hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                            sha256Hex = BitConverter.ToString(hasher.Hash ?? Array.Empty<byte>()).Replace("-", string.Empty).ToLowerInvariant();
                        }

                        string finalStoredPath = destinationPath;
                        long finalItemSize = declaredSize;
                        string finalItemName = Path.GetFileName(destinationPath);

                        if (string.Equals(Path.GetExtension(destinationPath), ".onkosfolder", StringComparison.OrdinalIgnoreCase))
                        {
                            string extractedRoot = ExtractFolderPackage(destinationPath, senderFolder, out string cleanFolderName, out long extractedBytes);
                            finalStoredPath = extractedRoot;
                            finalItemName = cleanFolderName;
                            finalItemSize = extractedBytes;
                        }

                        recordedSize = finalItemSize;
                        destinationPath = finalStoredPath;

                        stream.WriteByte(1);
                        await stream.FlushAsync(token).ConfigureAwait(false);

                        string senderDisplay = ResolveSenderDisplay(senderFolder, remoteIp);

                        TransferHistoryStore.AppendEntry(
                            "RECIBIDO",
                            finalItemName,
                            finalItemSize,
                            senderDisplay,
                            "Completado",
                            finalStoredPath,
                            sha256Hex,
                            $"Recibido desde {remoteIp}");

                        ServiceNotifier.WriteEvent(
                            EventLogEntryType.Information,
                            "Service.Receive",
                            $"Recibido '{finalItemName}' ({finalItemSize} bytes) desde {senderDisplay} en '{finalStoredPath}'. SHA-256: {sha256Hex}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                CleanupPartialFile(destinationPath);
                if (transferStarted)
                {
                    TransferHistoryStore.AppendEntry(
                        "RECIBIDO",
                        itemName,
                        recordedSize,
                        remoteIp,
                        "Error",
                        destinationPath ?? string.Empty,
                        string.Empty,
                        "Transferencia cancelada por detención del servicio.");
                }
            }
            catch (Exception ex)
            {
                CleanupPartialFile(destinationPath);
                if (transferStarted)
                {
                    TransferHistoryStore.AppendEntry(
                        "RECIBIDO",
                        itemName,
                        recordedSize,
                        remoteIp,
                        "Error",
                        destinationPath ?? string.Empty,
                        string.Empty,
                        ex.Message);

                    ServiceNotifier.WriteEvent(
                        EventLogEntryType.Error,
                        "Service.Receive",
                        $"Error recibiendo '{itemName}' desde {remoteIp}: {ex.Message}",
                        ex);
                }
                else
                {
                    FileLogger.Instance.Warn("Service.Receive", $"Conexión entrante rechazada o interrumpida desde {remoteIp}: {ex.Message}");
                }
            }
        }

        private static void WriteIdentityResponse(NetworkStream stream, string remoteIp)
        {
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                if (ONKOS.Core.Models.PeerInfo profile = ReadProfileOrNull())
                {
                    writer.Write("ONKOS_IDENTITY_V1");
                    writer.Write(profile.FullName);
                    writer.Write(profile.Area);
                    writer.Write(remoteIp);
                    writer.Write(Environment.MachineName);
                }
                else
                {
                    writer.Write("ONKOS_UNREGISTERED_V1");
                }
                writer.Flush();
            }
        }

        private static ONKOS.Core.Models.PeerInfo ReadProfileOrNull()
        {
            ONKOS.Core.Models.PeerInfo profile;
            return UserProfileStore.TryLoad(out profile) ? profile : null;
        }

        private static string ExtractFolderPackage(string archivePath, string senderFolder, out string cleanFolderName, out long totalExtractedBytes)
        {
            string folderName = Path.GetFileNameWithoutExtension(archivePath);
            int guidSeparator = folderName.LastIndexOf('_');
            Guid packageId;
            if (guidSeparator > 0 && Guid.TryParse(folderName.Substring(guidSeparator + 1), out packageId))
                folderName = folderName.Substring(0, guidSeparator);

            if (string.IsNullOrWhiteSpace(folderName))
                folderName = "Carpeta_recibida_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

            cleanFolderName = folderName;
            string extractRoot = Path.Combine(senderFolder, folderName);
            int suffix = 1;
            while (Directory.Exists(extractRoot))
                extractRoot = Path.Combine(senderFolder, folderName + "_" + suffix++);

            Directory.CreateDirectory(extractRoot);
            string rootFull = Path.GetFullPath(extractRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            long bytesSum = 0L;

            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
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
                        if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                            Directory.CreateDirectory(parent);

                        entry.ExtractToFile(target, false);
                        try { bytesSum += new FileInfo(target).Length; }
                        catch (IOException) { }
                    }
                }
            }

            try { File.Delete(archivePath); }
            catch (IOException ex) { FileLogger.Instance.Warn("Service.Receive", "No se pudo borrar paquete temporal .onkosfolder.", ex); }

            totalExtractedBytes = bytesSum;
            return extractRoot;
        }

        private static string GetSenderFolder(string senderIp, string rootFolder)
        {
            string safeIp = string.IsNullOrWhiteSpace(senderIp) ? "IP_desconocida" : senderIp.Replace(':', '_');
            string dayFolder = Path.Combine(rootFolder, DateTime.Now.ToString("yyyy-MM-dd"));

            lock (SenderFolderLock)
            {
                Directory.CreateDirectory(dayFolder);
                string prefix = safeIp + "_";
                string existing = null;

                try
                {
                    foreach (string candidate in Directory.GetDirectories(dayFolder, prefix + "*"))
                    {
                        if (existing == null || Directory.GetCreationTime(candidate) < Directory.GetCreationTime(existing))
                            existing = candidate;
                    }
                }
                catch (IOException)
                {
                }

                if (!string.IsNullOrEmpty(existing))
                    return existing;

                string folderName = safeIp + "_" + DateTime.Now.ToString("HH-mm-ss");
                string senderFolder = Path.Combine(dayFolder, folderName);
                int suffix = 1;
                while (Directory.Exists(senderFolder))
                    senderFolder = Path.Combine(dayFolder, folderName + "_" + suffix++);

                Directory.CreateDirectory(senderFolder);
                return senderFolder;
            }
        }

        private static string ResolveSenderDisplay(string senderFolder, string remoteIp)
        {
            try
            {
                string folderName = Path.GetFileName(senderFolder ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(folderName) && folderName.Contains("_"))
                {
                    string ipPart = folderName.Split('_')[0];
                    if (!string.IsNullOrWhiteSpace(ipPart))
                        return ipPart;
                }
            }
            catch (ArgumentException)
            {
            }

            return remoteIp;
        }

        private static void CleanupPartialFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            try { File.Delete(path); }
            catch (IOException ex) { FileLogger.Instance.Warn("Service.Receive", "No se pudo eliminar archivo parcial tras error.", ex); }
        }
    }
}
