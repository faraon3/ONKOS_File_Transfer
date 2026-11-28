using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ONKOS.Core.Logging;
using ONKOS.Core.Models;
using ONKOS.Core.Protocol;
using ONKOS.Core.Storage;

namespace ONKOS.Core.Transfers
{
    public enum QueueJobState
    {
        Queued,
        Running,
        Paused,
        Completed,
        Failed,
        Cancelled
    }

    public sealed class TransferJob
    {
        public string JobId { get; set; } = Guid.NewGuid().ToString("N");
        public string SourcePath { get; set; }
        public string ItemName { get; set; }
        public string TargetIp { get; set; }
        public string TargetLabel { get; set; }
        public long TotalBytes { get; set; }
        public long SentBytes { get; set; }
        public double BytesPerSecond { get; set; }
        public TimeSpan? EstimatedRemaining { get; set; }
        public QueueJobState State { get; set; } = QueueJobState.Queued;
        public string ErrorMessage { get; set; }
        public string SuggestedAction { get; set; }
        public string Sha256Hex { get; set; }
        public int RetryAttempts { get; set; }
    }

    public sealed class TransferQueueManager : IDisposable
    {
        private const int TransferPort = 5050;
        private const int MaxAutoRetries = 2;

        private readonly object _sync = new object();
        private readonly List<TransferJob> _jobs = new List<TransferJob>();
        private readonly TransferLimits _limits = new TransferLimits();
        private readonly ManualResetEventSlim _pauseGate = new ManualResetEventSlim(true);

        private PeerInfo _localProfile;
        private CancellationTokenSource _runnerCts;
        private Task _runnerTask;
        private bool _disposed;

        public event Action QueueChanged;
        public event Action<List<TransferJob>> BatchCompleted;

        public bool IsPaused => !_pauseGate.IsSet;

        public void SetLocalProfile(PeerInfo profile)
        {
            _localProfile = profile;
        }

        public void EnsureStarted()
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                if (_runnerTask == null || _runnerTask.IsCompleted)
                {
                    _runnerCts = new CancellationTokenSource();
                    _runnerTask = Task.Run(() => ProcessQueueAsync(_runnerCts.Token));
                }
            }
        }

        public List<TransferJob> EnqueueBatch(IEnumerable<string> sourcePaths, IEnumerable<PeerInfo> targets)
        {
            var created = new List<TransferJob>();
            List<string> paths = (sourcePaths ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            List<PeerInfo> dests = (targets ?? Enumerable.Empty<PeerInfo>()).Where(t => t != null && !string.IsNullOrWhiteSpace(t.IPv4)).ToList();

            lock (_sync)
            {
                foreach (PeerInfo target in dests)
                {
                    foreach (string p in paths)
                    {
                        string cleanName = Path.GetFileName(p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        if (string.IsNullOrWhiteSpace(cleanName))
                            cleanName = p;

                        long estSize = 0L;
                        try
                        {
                            if (File.Exists(p))
                                estSize = new FileInfo(p).Length;
                        }
                        catch (IOException)
                        {
                        }

                        var job = new TransferJob
                        {
                            SourcePath = p,
                            ItemName = cleanName,
                            TargetIp = target.IPv4.Trim(),
                            TargetLabel = string.IsNullOrWhiteSpace(target.FullName)
                                ? target.IPv4.Trim()
                                : $"{target.FullName} ({target.IPv4.Trim()})",
                            TotalBytes = estSize,
                            State = QueueJobState.Queued
                        };

                        _jobs.Add(job);
                        created.Add(job);
                    }
                }
            }

            EnsureStarted();
            QueueChanged?.Invoke();
            return created;
        }

        public void PauseAll()
        {
            _pauseGate.Reset();
            lock (_sync)
            {
                foreach (TransferJob j in _jobs)
                {
                    if (j.State == QueueJobState.Running || j.State == QueueJobState.Queued)
                        j.State = QueueJobState.Paused;
                }
            }
            QueueChanged?.Invoke();
        }

        public void ResumeAll()
        {
            lock (_sync)
            {
                foreach (TransferJob j in _jobs)
                {
                    if (j.State == QueueJobState.Paused)
                        j.State = QueueJobState.Queued;
                }
            }
            _pauseGate.Set();
            QueueChanged?.Invoke();
        }

        public void CancelAllActive()
        {
            _pauseGate.Set();
            lock (_sync)
            {
                foreach (TransferJob j in _jobs)
                {
                    if (j.State == QueueJobState.Queued || j.State == QueueJobState.Running || j.State == QueueJobState.Paused)
                    {
                        j.State = QueueJobState.Cancelled;
                        j.ErrorMessage = "Cancelado por el usuario.";
                        j.SuggestedAction = "Reintenta el envío cuando estés listo.";
                    }
                }
            }

            try { _runnerCts?.Cancel(); }
            catch (ObjectDisposedException) { }

            QueueChanged?.Invoke();
        }

        public void RetryFailedJobs()
        {
            bool any = false;
            lock (_sync)
            {
                foreach (TransferJob j in _jobs)
                {
                    if (j.State == QueueJobState.Failed || j.State == QueueJobState.Cancelled)
                    {
                        j.State = QueueJobState.Queued;
                        j.SentBytes = 0L;
                        j.BytesPerSecond = 0.0;
                        j.EstimatedRemaining = null;
                        j.ErrorMessage = string.Empty;
                        j.SuggestedAction = string.Empty;
                        j.RetryAttempts = 0;
                        any = true;
                    }
                }
            }

            if (any)
            {
                _pauseGate.Set();
                EnsureStarted();
                QueueChanged?.Invoke();
            }
        }

        public void ClearFinishedJobs()
        {
            lock (_sync)
            {
                _jobs.RemoveAll(j =>
                    j.State == QueueJobState.Completed ||
                    j.State == QueueJobState.Failed ||
                    j.State == QueueJobState.Cancelled);
            }
            QueueChanged?.Invoke();
        }

        public List<TransferJob> GetSnapshot()
        {
            lock (_sync)
            {
                return _jobs.Select(j => new TransferJob
                {
                    JobId = j.JobId,
                    SourcePath = j.SourcePath,
                    ItemName = j.ItemName,
                    TargetIp = j.TargetIp,
                    TargetLabel = j.TargetLabel,
                    TotalBytes = j.TotalBytes,
                    SentBytes = j.SentBytes,
                    BytesPerSecond = j.BytesPerSecond,
                    EstimatedRemaining = j.EstimatedRemaining,
                    State = j.State,
                    ErrorMessage = j.ErrorMessage,
                    SuggestedAction = j.SuggestedAction,
                    Sha256Hex = j.Sha256Hex,
                    RetryAttempts = j.RetryAttempts
                }).ToList();
            }
        }

        private async Task ProcessQueueAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TransferJob nextJob = null;
                lock (_sync)
                {
                    nextJob = _jobs.FirstOrDefault(j => j.State == QueueJobState.Queued);
                    if (nextJob != null)
                        nextJob.State = QueueJobState.Running;
                }

                if (nextJob == null)
                {
                    try { await Task.Delay(350, token).ConfigureAwait(false); }
                    catch (TaskCanceledException) { break; }
                    continue;
                }

                QueueChanged?.Invoke();
                await ExecuteJobWithRetryAsync(nextJob, token).ConfigureAwait(false);
                QueueChanged?.Invoke();

                bool hasPendingOrRunning;
                List<TransferJob> snapshot;
                lock (_sync)
                {
                    hasPendingOrRunning = _jobs.Any(j =>
                        j.State == QueueJobState.Queued ||
                        j.State == QueueJobState.Running ||
                        j.State == QueueJobState.Paused);
                    snapshot = GetSnapshot();
                }

                if (!hasPendingOrRunning && snapshot.Count > 0)
                    BatchCompleted?.Invoke(snapshot);
            }
        }

        private async Task ExecuteJobWithRetryAsync(TransferJob job, CancellationToken token)
        {
            for (int attempt = 0; attempt <= MaxAutoRetries; attempt++)
            {
                token.ThrowIfCancellationRequested();
                job.RetryAttempts = attempt;
                string tempArchive = null;

                try
                {
                    _pauseGate.Wait(token);

                    if (!File.Exists(job.SourcePath) && !Directory.Exists(job.SourcePath))
                        throw new FileNotFoundException("El archivo o carpeta ya no existe en el origen.", job.SourcePath);

                    PathSizeSummary sizeSummary = await PathSizeCache.GetOrComputeAsync(job.SourcePath, token).ConfigureAwait(false);
                    job.TotalBytes = Math.Max(sizeSummary.TotalBytes, 0L);

                    string fileToSend = job.SourcePath;
                    string relName = job.ItemName;

                    if (Directory.Exists(job.SourcePath))
                    {
                        tempArchive = Path.Combine(Path.GetTempPath(), $"{job.ItemName}_{Guid.NewGuid():N}.onkosfolder");
                        await Task.Run(() => ZipFile.CreateFromDirectory(job.SourcePath, tempArchive, CompressionLevel.Fastest, false), token).ConfigureAwait(false);
                        fileToSend = tempArchive;
                        relName = Path.GetFileName(tempArchive);
                        job.TotalBytes = new FileInfo(tempArchive).Length;
                    }

                    string sha = await Task.Run(() => SecureTransferProtocol.ComputeFileSha256Hex(fileToSend), token).ConfigureAwait(false);
                    string safeRel = SecureTransferProtocol.NormalizeAndValidateRelativePath(relName, _limits);
                    job.Sha256Hex = sha;

                    var manifest = new ManifestItem
                    {
                        RelativePath = safeRel,
                        SizeBytes = new FileInfo(fileToSend).Length,
                        Sha256Hex = sha,
                        IsDirectory = false
                    };

                    await SendSingleManifestItemAsync(job, fileToSend, manifest, token).ConfigureAwait(false);

                    job.State = QueueJobState.Completed;
                    job.SentBytes = job.TotalBytes;
                    job.EstimatedRemaining = TimeSpan.Zero;
                    job.ErrorMessage = string.Empty;
                    job.SuggestedAction = string.Empty;

                    TransferHistoryStore.AppendEntry(
                        "ENVIADO",
                        job.ItemName,
                        job.TotalBytes,
                        job.TargetLabel,
                        "Completado",
                        job.TargetIp,
                        job.Sha256Hex,
                        "Enviado por cola TLS 1.2+");
                    return;
                }
                catch (OperationCanceledException)
                {
                    job.State = QueueJobState.Cancelled;
                    job.ErrorMessage = "Transferencia cancelada.";
                    job.SuggestedAction = "Puedes reintentar el envío cuando lo necesites.";
                    TransferHistoryStore.AppendEntry("ENVIADO", job.ItemName, job.SentBytes, job.TargetLabel, "Cancelado", job.TargetIp, job.Sha256Hex, job.ErrorMessage);
                    return;
                }
                catch (Exception ex)
                {
                    FileLogger.Instance.Warn("TransferQueue", $"Intento {attempt + 1} fallido para '{job.ItemName}' -> {job.TargetIp} ({ex.Message}).");
                    if (attempt < MaxAutoRetries && !token.IsCancellationRequested)
                    {
                        int delayMs = (int)(1000 * Math.Pow(2, attempt));
                        try { await Task.Delay(delayMs, token).ConfigureAwait(false); }
                        catch (TaskCanceledException) { return; }
                        continue;
                    }

                    job.State = QueueJobState.Failed;
                    job.ErrorMessage = ex.Message;
                    job.SuggestedAction = BuildSuggestedAction(ex);
                    TransferHistoryStore.AppendEntry("ENVIADO", job.ItemName, job.SentBytes, job.TargetLabel, "Error", job.TargetIp, job.Sha256Hex, job.ErrorMessage);
                }
                finally
                {
                    if (!string.IsNullOrWhiteSpace(tempArchive) && File.Exists(tempArchive))
                    {
                        try { File.Delete(tempArchive); }
                        catch (IOException) { }
                    }
                }
            }
        }

        private async Task SendSingleManifestItemAsync(TransferJob job, string filePath, ManifestItem manifest, CancellationToken token)
        {
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(job.TargetIp, TransferPort).ConfigureAwait(false);
                client.NoDelay = true;

                using (NetworkStream raw = client.GetStream())
                using (SslStream ssl = await SecureTransferProtocol.AuthenticateClientAsync(raw, job.TargetIp, token).ConfigureAwait(false))
                using (var writer = new BinaryWriter(ssl, Encoding.UTF8, true))
                using (var reader = new BinaryReader(ssl, Encoding.UTF8, true))
                {
                    string senderName = _localProfile != null ? _localProfile.FullName : Environment.UserName;
                    string senderArea = _localProfile != null ? _localProfile.Area : string.Empty;

                    SecureTransferProtocol.WriteHeader(writer, senderName, senderArea, Environment.MachineName, 1);
                    SecureTransferProtocol.WriteManifestItem(writer, manifest);

                    var sw = Stopwatch.StartNew();
                    long sent = 0L;
                    byte[] buffer = new byte[81920];

                    using (var input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
                    {
                        while (sent < manifest.SizeBytes)
                        {
                            token.ThrowIfCancellationRequested();
                            if (!_pauseGate.IsSet)
                            {
                                sw.Stop();
                                job.State = QueueJobState.Paused;
                                QueueChanged?.Invoke();
                                _pauseGate.Wait(token);
                                job.State = QueueJobState.Running;
                                sw.Start();
                                QueueChanged?.Invoke();
                            }

                            int read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                            if (read <= 0)
                                break;

                            await ssl.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                            sent += read;
                            job.SentBytes = sent;

                            double elapsedSec = Math.Max(0.05, sw.Elapsed.TotalSeconds);
                            job.BytesPerSecond = sent / elapsedSec;
                            if (job.BytesPerSecond > 1.0 && manifest.SizeBytes > sent)
                            {
                                double remSec = (manifest.SizeBytes - sent) / job.BytesPerSecond;
                                job.EstimatedRemaining = TimeSpan.FromSeconds(Math.Min(86400, remSec));
                            }
                            else
                            {
                                job.EstimatedRemaining = TimeSpan.Zero;
                            }
                        }
                    }

                    await ssl.FlushAsync(token).ConfigureAwait(false);
                    string ack = reader.ReadString();
                    if (!string.Equals(ack, "OK_ITEM", StringComparison.Ordinal))
                    {
                        string detail = reader.PeekChar() >= 0 ? reader.ReadString() : ack;
                        throw new IOException($"Receptor respondió: {detail}");
                    }
                }
            }
        }

        private static string BuildSuggestedAction(Exception ex)
        {
            if (ex is SocketException)
                return "Verifica que el equipo destino esté encendido, en la red y con el puerto 5050 habilitado.";
            if (ex is IOException && ex.Message.IndexOf("autoriz", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Pide a Sistemas que apruebe la huella SHA-256 del equipo en el receptor.";
            if (ex is InvalidDataException)
                return "Revisa el nombre, extensión o tamaño del archivo/carpeta frente a las políticas de seguridad.";
            return "Reintenta el envío o revisa el registro en ProgramData\\ONKOS\\logs.";
        }

        public void Dispose()
        {
            _disposed = true;
            _pauseGate.Set();
            try { _runnerCts?.Cancel(); }
            catch (ObjectDisposedException) { }
            try { _runnerCts?.Dispose(); }
            catch (ObjectDisposedException) { }
            try { _pauseGate?.Dispose(); }
            catch (ObjectDisposedException) { }
        }
    }
}
