using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ONKOSFileTransfer.Network
{
    public sealed class PeerIdentityResult
    {
        public string FullName { get; set; }
        public string Area { get; set; }
        public string IPv4 { get; set; }
        public string MachineName { get; set; }
    }

    public static class FileSender
    {
        private const string DefaultClinicToken = "ONKOS_CLINICA_2026_KEY_9F4A";

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
                OnkosLogger.Warn("FileSender", "No se pudo leer clave_red.txt; usando clave predeterminada.", ex);
            }

            return DefaultClinicToken;
        }

        private static string ComputeSha256Hex(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920))
            {
                byte[] hash = sha.ComputeHash(fs);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        public static bool TryParseStrictIPv4(string host, out IPAddress ipv4)
        {
            ipv4 = null;
            if (string.IsNullOrWhiteSpace(host))
                return false;

            string clean = host.Trim();
            if (!IPAddress.TryParse(clean, out IPAddress parsed))
                return false;

            if (parsed.AddressFamily != AddressFamily.InterNetwork)
                return false;

            if (IPAddress.IsLoopback(parsed) || parsed.Equals(IPAddress.Any) || parsed.Equals(IPAddress.Broadcast))
                return false;

            ipv4 = parsed;
            return true;
        }

        private static void ObserveFaulted(Task task)
        {
            if (task == null)
                return;

            task.ContinueWith(
                t => { var _ = t.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        public static async Task<bool> CheckPeerReachableAsync(string host, int port = 5050, int timeoutMs = 2000)
        {
            if (!TryParseStrictIPv4(host, out IPAddress targetIp))
                return false;

            try
            {
                using (var client = new TcpClient(AddressFamily.InterNetwork))
                {
                    Task connectTask = client.ConnectAsync(targetIp, port);
                    ObserveFaulted(connectTask);

                    Task completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
                    if (completed != connectTask || !client.Connected)
                        return false;

                    await connectTask;
                    return client.Connected;
                }
            }
            catch
            {
                return false;
            }
        }

        public static async Task<PeerIdentityResult> QueryPeerIdentityAsync(string host, int port, int timeoutMs = 400)
        {
            var result = new PeerIdentityResult();
            if (!TryParseStrictIPv4(host, out IPAddress targetIp))
                return result;

            try
            {
                using (var client = new TcpClient(AddressFamily.InterNetwork))
                {
                    Task connectTask = client.ConnectAsync(targetIp, port);
                    ObserveFaulted(connectTask);

                    Task completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
                    if (completed != connectTask || !client.Connected)
                        return result;

                    await connectTask;
                    client.SendTimeout = timeoutMs;
                    client.ReceiveTimeout = timeoutMs;

                    using (NetworkStream stream = client.GetStream())
                    using (var writer = new BinaryWriter(stream, Encoding.UTF8, false))
                    using (var reader = new BinaryReader(stream, Encoding.UTF8, false))
                    {
                        writer.Write("ONKOS_WHOAMI_V1");
                        writer.Flush();

                        Task<string> tokenTask = Task.Run(() => reader.ReadString());
                        ObserveFaulted(tokenTask);

                        Task tokenCompleted = await Task.WhenAny(tokenTask, Task.Delay(timeoutMs));
                        if (tokenCompleted != tokenTask)
                            return result;

                        string responseToken = await tokenTask;
                        if (!string.Equals(responseToken, "ONKOS_IDENTITY_V1", StringComparison.Ordinal))
                            return result;

                        result.FullName = reader.ReadString();
                        result.Area = reader.ReadString();
                        result.IPv4 = reader.ReadString();
                        result.MachineName = reader.ReadString();
                        return result;
                    }
                }
            }
            catch
            {
                return new PeerIdentityResult();
            }
        }

        public static async Task SendFileAsync(
            string host,
            int port,
            string filePath,
            Action<long, long> onProgress = null)
        {
            if (!TryParseStrictIPv4(host, out IPAddress targetIp))
                throw new ArgumentException("Solo se permiten direcciones IPv4 válidas para enviar archivos.", nameof(host));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("No se encontró el archivo a enviar.", filePath);

            bool reachable = await CheckPeerReachableAsync(targetIp.ToString(), port, 2000);
            if (!reachable)
                throw new IOException("El equipo destino no responde o está apagado/suspendido.");

            FileInfo fileInfo = new FileInfo(filePath);
            long totalBytes = fileInfo.Length;
            string sha256Hex = await Task.Run(() => ComputeSha256Hex(filePath));
            string authToken = GetNetworkAuthToken();

            using (TcpClient client = new TcpClient(AddressFamily.InterNetwork))
            {
                Task connectTask = client.ConnectAsync(targetIp, port);
                ObserveFaulted(connectTask);

                Task completed = await Task.WhenAny(connectTask, Task.Delay(5000));
                if (completed != connectTask || !client.Connected)
                    throw new SocketException((int)SocketError.TimedOut);

                await connectTask;
                client.NoDelay = true;

                using (NetworkStream stream = client.GetStream())
                using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true))
                using (FileStream input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                {
                    writer.Write("ONKOS_FILE_V2");
                    writer.Write(authToken);
                    writer.Write(fileInfo.Name);
                    writer.Write(totalBytes);
                    writer.Write(sha256Hex);
                    writer.Flush();

                    byte[] buffer = new byte[81920];
                    long sentTotal = 0;

                    while (sentTotal < totalBytes)
                    {
                        int read = await input.ReadAsync(buffer, 0, buffer.Length);
                        if (read <= 0)
                            break;

                        await stream.WriteAsync(buffer, 0, read);
                        sentTotal += read;
                        onProgress?.Invoke(sentTotal, totalBytes);
                    }

                    await stream.FlushAsync();

                    byte[] ackBuffer = new byte[1];
                    int ackRead = await stream.ReadAsync(ackBuffer, 0, 1);
                    if (ackRead != 1)
                        throw new IOException("El destino no confirmó la recepción completa del archivo.");

                    if (ackBuffer[0] == 2)
                        throw new UnauthorizedAccessException("El equipo destino rechazó la clave de red clínica.");

                    if (ackBuffer[0] == 3)
                        throw new IOException("Error de integridad (SHA-256 no coincide). Intenta reenviar el archivo.");

                    if (ackBuffer[0] != 1)
                        throw new IOException("El destino devolvió una confirmación desconocida.");
                }
            }
        }
    }
}
