using System;
using System.IO;
using System.Net.Sockets;
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
            if (string.IsNullOrWhiteSpace(host))
                return false;

            try
            {
                using (var client = new TcpClient())
                {
                    Task connectTask = client.ConnectAsync(host, port);
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
            if (string.IsNullOrWhiteSpace(host))
                return result;

            try
            {
                using (var client = new TcpClient())
                {
                    Task connectTask = client.ConnectAsync(host, port);
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
            if (string.IsNullOrWhiteSpace(host))
                throw new ArgumentException("La dirección de destino no es válida.", nameof(host));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("No se encontró el archivo a enviar.", filePath);

            bool reachable = await CheckPeerReachableAsync(host, port, 2000);
            if (!reachable)
                throw new IOException("El equipo destino no responde o está apagado/suspendido.");

            FileInfo fileInfo = new FileInfo(filePath);
            long totalBytes = fileInfo.Length;

            using (TcpClient client = new TcpClient())
            {
                Task connectTask = client.ConnectAsync(host, port);
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
                    writer.Write(fileInfo.Name);
                    writer.Write(totalBytes);
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
                    if (ackRead != 1 || ackBuffer[0] != 1)
                        throw new IOException("El destino no confirmó la recepción completa del archivo.");
                }
            }
        }
    }
}
