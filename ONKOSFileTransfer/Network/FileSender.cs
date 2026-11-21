using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ONKOSFileTransfer.Network
{
    public static class FileSender
    {
        public static async Task<(string FullName, string Area, string IPv4, string MachineName)> QueryPeerIdentityAsync(
            string ip, int port, int timeoutMs = 600)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return (null, null, null, null);

            try
            {
                using (TcpClient client = new TcpClient())
                using (var cts = new CancellationTokenSource(timeoutMs))
                {
                    var connectTask = client.ConnectAsync(ip, port);
                    var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs, cts.Token));
                    if (completed != connectTask || !client.Connected)
                        return (null, null, null, null);

                    await connectTask;

                    using (NetworkStream stream = client.GetStream())
                    {
                        stream.ReadTimeout = timeoutMs;
                        stream.WriteTimeout = timeoutMs;

                        using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true))
                        using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, true))
                        {
                            writer.Write("ONKOS_WHOAMI_V1");
                            writer.Flush();

                            string header = reader.ReadString();
                            if (!string.Equals(header, "ONKOS_IDENTITY_V1", StringComparison.Ordinal))
                                return (null, null, null, null);

                            string fullName = reader.ReadString();
                            string area = reader.ReadString();
                            string reportedIp = reader.ReadString();
                            string machine = reader.ReadString();

                            return (
                                fullName?.Trim(),
                                area?.Trim(),
                                string.IsNullOrWhiteSpace(reportedIp) ? ip : reportedIp.Trim(),
                                machine?.Trim());
                        }
                    }
                }
            }
            catch
            {
                return (null, null, null, null);
            }
        }

        public static async Task SendFileAsync(string ip, int port, string filePath)
        {
            await SendFileAsync(ip, port, filePath, null);
        }

        public static async Task SendFileAsync(
            string ip, int port, string filePath, Action<long, long> progress)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Archivo no encontrado.", filePath);

            using (TcpClient client = new TcpClient())
            {
                await client.ConnectAsync(ip, port);

                using (NetworkStream stream = client.GetStream())
                using (BinaryWriter writer = new BinaryWriter(
                    stream, Encoding.UTF8, true))
                using (FileStream file = File.OpenRead(filePath))
                {
                    long length = file.Length;
                    writer.Write(Path.GetFileName(filePath));
                    writer.Write(length);
                    writer.Flush();

                    byte[] buffer = new byte[81920];
                    long sent = 0;
                    int read;
                    while ((read = await file.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await stream.WriteAsync(buffer, 0, read);
                        sent += read;
                        if (progress != null)
                            progress(sent, length);
                    }
                    await stream.FlushAsync();

                    int confirmation = stream.ReadByte();
                    if (confirmation != 1)
                        throw new IOException("El equipo destino no confirmó que el archivo se guardó.");
                }
            }
        }
    }
}
