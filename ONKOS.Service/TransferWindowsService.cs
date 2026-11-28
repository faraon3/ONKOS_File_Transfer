using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ONKOS.Core.Logging;
using ONKOS.Core.Protocol;
using ONKOS.Core.Security;
using ONKOS.Core.Storage;

namespace ONKOS.Service
{
    public sealed class TransferWindowsService : ServiceBase
    {
        private const int DefaultListenPort = 5050;
        private const int MaxConcurrentConnections = 16;
        private const int MaxConnectionsPerIp = 4;

        private static readonly object SenderFolderLock = new object();
        private static readonly ConcurrentDictionary<string, int> ActiveByIp =
            new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private readonly TransferLimits _limits = new TransferLimits();
        private TcpListener _listener;
        private SemaphoreSlim _concurrencyGate;
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
                DeviceIdentityStore.GetOrCreateCertificate();
                TransferHistoryStore.ApplyRetention();

                string inboxFolder;
                string inboxError;
                if (!AppSettingsStore.TryEnsureInboxReady(out inboxFolder, out inboxError))
                {
                    ServiceNotifier.WriteEvent(EventLogEntryType.Error, "Service.Start", inboxError);
                    throw new IOException(inboxError);
                }

                _isStopping = false;
                _concurrencyGate = new SemaphoreSlim(MaxConcurrentConnections, MaxConcurrentConnections);
                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Any, DefaultListenPort);
                _listener.Start();

                _ = Task.Run(() => AcceptLoopAsync(_cts.Token));

                ServiceNotifier.WriteEvent(
                    EventLogEntryType.Information,
                    "Service.Start",
                    $"ONKOSTransferService iniciado con TLS 1.2+ en puerto {DefaultListenPort}. Carpeta: {inboxFolder}");
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
                catch (SocketException ex) { FileLogger.Instance.Warn("Service.Stop", $"Error al detener TcpListener ({ex.Message})."); }

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
                try { _concurrencyGate?.Dispose(); }
                catch (ObjectDisposedException) { }
                _cts = null;
                _listener = null;
                _concurrencyGate = null;
            }
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && !_isStopping)
            {
                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleClientWithLimitsAsync(client, token), token);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    if (!_isStopping && !token.IsCancellationRequested)
                        FileLogger.Instance.Warn("Service.Accept", $"Error de socket aceptando conexión ({ex.Message}).");
                    break;
                }
                catch (Exception ex)
                {
                    if (!_isStopping && !token.IsCancellationRequested)
                        FileLogger.Instance.Error("Service.Accept", "Error inesperado en el bucle de recepción.", ex);
                }
            }
        }

        private async Task HandleClientWithLimitsAsync(TcpClient client, CancellationToken token)
        {
            string remoteIp = "IP_desconocida";
            try
            {
                var remoteEp = client.Client.RemoteEndPoint as IPEndPoint;
                if (remoteEp != null && remoteEp.Address != null)
                    remoteIp = remoteEp.Address.ToString();
            }
            catch (SocketException)
            {
            }

            int currentIpCount = ActiveByIp.AddOrUpdate(remoteIp, 1, (k, v) => v + 1);
            if (currentIpCount > MaxConnectionsPerIp)
            {
                DecrementIpCount(remoteIp);
                FileLogger.Instance.Warn("Service.Security", $"Límite de conexiones simultáneas excedido para IP {remoteIp}.");
                client.Close();
                return;
            }

            bool enteredGate = false;
            try
            {
                await _concurrencyGate.WaitAsync(token).ConfigureAwait(false);
                enteredGate = true;
                await ProcessAuthenticatedClientAsync(client, remoteIp, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                client.Close();
            }
            finally
            {
                DecrementIpCount(remoteIp);
                if (enteredGate)
                {
                    try { _concurrencyGate?.Release(); }
                    catch (ObjectDisposedException) { }
                }
            }
        }

        private static void DecrementIpCount(string ip)
        {
            ActiveByIp.AddOrUpdate(ip, 0, (k, v) => Math.Max(0, v - 1));
        }

        private async Task ProcessAuthenticatedClientAsync(TcpClient client, string remoteIp, CancellationToken token)
        {
            string partPath = null;
            string itemName = "Elemento recibido";
            long recordedSize = 0L;
            bool transferStarted = false;

            try
            {
                using (client)
                using (NetworkStream rawStream = client.GetStream())
                using (SslStream sslStream = await SecureTransferProtocol.AuthenticateServerAsync(rawStream, token).ConfigureAwait(false))
                using (var reader = new BinaryReader(sslStream, Encoding.UTF8, true))
                using (var writer = new BinaryWriter(sslStream, Encoding.UTF8, true))
                {
                    string senderFingerprint = SecureTransferProtocol.GetRemoteFingerprintSha256(sslStream);
                    if (string.IsNullOrWhiteSpace(senderFingerprint))
                        throw new AuthenticationException("No se pudo obtener la huella SHA-256 del cliente TLS.");

                    string senderName;
                    string senderArea;
                    string machineName;
                    int itemCount;
                    SecureTransferProtocol.ReadAndValidateHeader(
                        reader,
                        out senderName,
                        out senderArea,
                        out machineName,
                        out itemCount,
                        _limits);

                    string blockReason;
                    if (ReceiverPolicyStore.IsBlocked(senderFingerprint, remoteIp, out blockReason))
                    {
                        writer.Write("ERR_BLOCKED");
                        writer.Write(blockReason);
                        writer.Flush();
                        FileLogger.Instance.Warn("Service.Policy", $"Remitente bloqueado ({remoteIp} / {senderFingerprint}): {blockReason}");
                        return;
                    }

                    ReceiverPolicy policy = ReceiverPolicyStore.Load();
                    if (!policy.AutoAcceptAuthorized)
                    {
                        writer.Write("ERR_MANUAL_CONFIRM_REQUIRED");
                        writer.Write("La aceptación automática está desactivada en este receptor.");
                        writer.Flush();
                        FileLogger.Instance.Info("Service.Policy", $"Recepción rechazada por política de confirmación manual desde {remoteIp}.");
                        return;
                    }

                    PeerTrustState trustState = PeerAuthorizationStore.GetTrustState(senderFingerprint);
                    if (trustState != PeerTrustState.Authorized)
                    {
                        PeerAuthorizationStore.RegisterOrTouchPending(
                            senderFingerprint,
                            senderName,
                            senderArea,
                            remoteIp,
                            machineName);

                        writer.Write("ERR_NOT_AUTHORIZED");
                        writer.Write($"Equipo en estado {trustState}. Requiere autorización por huella de Sistemas.");
                        writer.Flush();

                        FileLogger.Instance.Warn(
                            "Service.Auth",
                            $"Conexión rechazada por autorización ({trustState}). Huella: {senderFingerprint}, IP: {remoteIp}");
                        return;
                    }

                    string inboxFolder;
                    string inboxError;
                    if (!AppSettingsStore.TryEnsureInboxReady(out inboxFolder, out inboxError))
                        throw new IOException(inboxError);

                    string senderFolder = GetSenderFolder(senderFingerprint, remoteIp, inboxFolder);
                    string senderLabel = string.IsNullOrWhiteSpace(senderArea)
                        ? $"{senderName} ({remoteIp})"
                        : $"{senderName} — {senderArea} ({remoteIp})";

                    for (int i = 0; i < itemCount; i++)
                    {
                        token.ThrowIfCancellationRequested();

                        ManifestItem manifest = SecureTransferProtocol.ReadAndValidateManifestItem(reader, _limits, 0L);
                        itemName = manifest.RelativePath;
                        recordedSize = manifest.SizeBytes;
                        transferStarted = true;

                        string spaceReason;
                        if (!PathAndAclHelper.HasEnoughFreeSpace(inboxFolder, manifest.SizeBytes, out spaceReason))
                            throw new IOException(spaceReason);

                        string desiredDest = SecureTransferProtocol.ResolveSafeDestinationPath(senderFolder, manifest.RelativePath);
                        string finalDest = SecureTransferProtocol.GetNonOverwritingPath(desiredDest);
                        string destParent = Path.GetDirectoryName(finalDest);
                        if (!string.IsNullOrEmpty(destParent))
                            Directory.CreateDirectory(destParent);

                        if (manifest.IsDirectory)
                        {
                            Directory.CreateDirectory(finalDest);
                            writer.Write("OK_ITEM");
                            writer.Flush();

                            TransferHistoryStore.AppendEntry(
                                "RECIBIDO",
                                Path.GetFileName(finalDest),
                                0L,
                                senderLabel,
                                "Completado",
                                finalDest,
                                string.Empty,
                                $"Huella: {senderFingerprint}");
                            continue;
                        }

                        partPath = finalDest + ".part";
                        string computedSha256;

                        using (var output = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                        using (var sha = SHA256.Create())
                        using (var cryptoStream = new CryptoStream(output, sha, CryptoStreamMode.Write))
                        {
                            byte[] buffer = new byte[81920];
                            long received = 0L;

                            while (received < manifest.SizeBytes)
                            {
                                token.ThrowIfCancellationRequested();
                                int chunkSize = (int)Math.Min(buffer.Length, manifest.SizeBytes - received);
                                int read = await sslStream.ReadAsync(buffer, 0, chunkSize, token).ConfigureAwait(false);
                                if (read <= 0)
                                    throw new EndOfStreamException("Conexión cerrada antes de completar el archivo.");

                                await cryptoStream.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                                received += read;
                            }

                            cryptoStream.FlushFinalBlock();
                            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                            computedSha256 = BitConverter.ToString(sha.Hash ?? Array.Empty<byte>()).Replace("-", string.Empty).ToLowerInvariant();
                        }

                        if (!string.Equals(computedSha256, manifest.Sha256Hex, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"Fallo de integridad SHA-256 en '{itemName}'.");

                        if (File.Exists(finalDest))
                            throw new IOException("Ya existe un archivo en el destino y no se permite sobrescritura por defecto.");

                        File.Move(partPath, finalDest);
                        partPath = null;

                        string recordedName = Path.GetFileName(finalDest);
                        long recordedItemSize = manifest.SizeBytes;
                        string recordedPath = finalDest;

                        if (string.Equals(Path.GetExtension(finalDest), ".onkosfolder", StringComparison.OrdinalIgnoreCase))
                        {
                            SecureTransferProtocol.ValidateArchiveSafety(finalDest, manifest.SizeBytes, _limits);
                            recordedPath = ExtractSafeFolder(finalDest, senderFolder, out recordedName, out recordedItemSize);
                        }

                        writer.Write("OK_ITEM");
                        writer.Flush();

                        TransferHistoryStore.AppendEntry(
                            "RECIBIDO",
                            recordedName,
                            recordedItemSize,
                            senderLabel,
                            "Completado",
                            recordedPath,
                            computedSha256,
                            $"Huella: {senderFingerprint}");

                        ServiceNotifier.WriteEvent(
                            EventLogEntryType.Information,
                            "Service.Receive",
                            $"Recibido '{recordedName}' ({recordedItemSize} bytes) desde {senderLabel}. SHA-256: {computedSha256}");
                    }
                }
            }
            catch (AuthenticationException authEx)
            {
                CleanupPartialFile(partPath);
                FileLogger.Instance.Warn("Service.TLS", $"Fallo de autenticación TLS desde {remoteIp}: {authEx.Message}");
            }
            catch (Exception ex)
            {
                CleanupPartialFile(partPath);
                if (transferStarted)
                {
                    TransferHistoryStore.AppendEntry(
                        "RECIBIDO",
                        itemName,
                        recordedSize,
                        remoteIp,
                        "Error",
                        string.Empty,
                        string.Empty,
                        ex.Message);

                    ServiceNotifier.WriteEvent(
                        EventLogEntryType.Error,
                        "Service.Receive",
                        $"Error en recepción desde {remoteIp} ('{itemName}'): {ex.Message}",
                        ex);
                }
                else
                {
                    FileLogger.Instance.Warn("Service.Receive", $"Conexión rechazada o fallida desde {remoteIp}: {ex.Message}");
                }
            }
        }

        private string ExtractSafeFolder(string archivePath, string senderFolder, out string cleanFolderName, out long totalBytes)
        {
            string baseName = Path.GetFileNameWithoutExtension(archivePath);
            int sep = baseName.LastIndexOf('_');
            Guid id;
            if (sep > 0 && Guid.TryParse(baseName.Substring(sep + 1), out id))
                baseName = baseName.Substring(0, sep);

            cleanFolderName = string.IsNullOrWhiteSpace(baseName)
                ? "Carpeta_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
                : baseName;

            string extractRoot = SecureTransferProtocol.GetNonOverwritingPath(Path.Combine(senderFolder, cleanFolderName));
            Directory.CreateDirectory(extractRoot);
            long sum = 0L;

            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string safeRel = SecureTransferProtocol.NormalizeAndValidateRelativePath(entry.FullName.TrimEnd('/'), _limits);
                    string targetPath = SecureTransferProtocol.ResolveSafeDestinationPath(extractRoot, safeRel);

                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(targetPath);
                    }
                    else
                    {
                        string parent = Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrEmpty(parent))
                            Directory.CreateDirectory(parent);

                        entry.ExtractToFile(targetPath, overwrite: false);
                        try { sum += new FileInfo(targetPath).Length; }
                        catch (IOException) { }
                    }
                }
            }

            try { File.Delete(archivePath); }
            catch (IOException ex) { FileLogger.Instance.Warn("Service.Receive", $"No se pudo eliminar paquete .onkosfolder ({ex.Message})."); }

            totalBytes = sum;
            return extractRoot;
        }

        private static string GetSenderFolder(string fingerprint, string remoteIp, string rootFolder)
        {
            string tag = !string.IsNullOrWhiteSpace(fingerprint)
                ? fingerprint.Substring(0, Math.Min(12, fingerprint.Length))
                : (string.IsNullOrWhiteSpace(remoteIp) ? "IP_desconocida" : remoteIp.Replace(':', '_'));

            string dayFolder = Path.Combine(rootFolder, DateTime.Now.ToString("yyyy-MM-dd"));
            lock (SenderFolderLock)
            {
                Directory.CreateDirectory(dayFolder);
                string prefix = tag + "_";
                string existing = null;

                try
                {
                    foreach (string dir in Directory.GetDirectories(dayFolder, prefix + "*"))
                    {
                        if (existing == null || Directory.GetCreationTime(dir) < Directory.GetCreationTime(existing))
                            existing = dir;
                    }
                }
                catch (IOException)
                {
                }

                if (!string.IsNullOrEmpty(existing))
                    return existing;

                string folderName = tag + "_" + DateTime.Now.ToString("HH-mm-ss");
                string target = SecureTransferProtocol.GetNonOverwritingPath(Path.Combine(dayFolder, folderName));
                Directory.CreateDirectory(target);
                return target;
            }
        }

        private static void CleanupPartialFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            try { File.Delete(path); }
            catch (IOException ex) { FileLogger.Instance.Warn("Service.Receive", $"No se pudo borrar parcial '{path}' ({ex.Message})."); }
        }
    }
}
