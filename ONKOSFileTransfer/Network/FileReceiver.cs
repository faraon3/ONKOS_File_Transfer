using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ONKOSFileTransfer.Network
{
    public class FileReceiver
    {
        private TcpListener listener;

        public bool IsRunning { get; private set; }
        public event Action<string> FileReceived;
        public event Action<string> Error;
        public event Action<string, long, long> TransferProgress;

        public void Start(int port, string folder)
        {
            if (IsRunning)
                return;

            Directory.CreateDirectory(folder);
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            IsRunning = true;
            AcceptLoop(folder);
        }

        private async void AcceptLoop(string folder)
        {
            try
            {
                while (IsRunning)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    Task.Run(() => ReceiveFile(client, folder));
                }
            }
            catch (ObjectDisposedException)
            {
                // Listener closed intentionally.
            }
            catch (SocketException)
            {
                // Listener closed intentionally.
            }
            catch (Exception ex)
            {
                if (IsRunning)
                    Error?.Invoke(ex.Message);
            }
        }

        private async Task ReceiveFile(TcpClient client, string folder)
        {
            string destination = null;
            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                using (BinaryReader reader = new BinaryReader(
                    stream, System.Text.Encoding.UTF8, true))
                {
                    string name = Path.GetFileName(reader.ReadString());
                    long size = reader.ReadInt64();

                    if (string.IsNullOrWhiteSpace(name) || name == "." || name == ".." ||
                        size < 0 || size > 20L * 1024 * 1024 * 1024)
                        throw new InvalidDataException("Archivo no válido o tamaño fuera del límite.");

                    destination = Path.Combine(folder, name);
                    if (File.Exists(destination))
                    {
                        destination = Path.Combine(folder,
                            Path.GetFileNameWithoutExtension(name) + "_" +
                            DateTime.Now.ToString("yyyyMMdd_HHmmssfff") +
                            Path.GetExtension(name));
                    }

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

                            await output.WriteAsync(buffer, 0, received);
                            receivedTotal += received;
                            TransferProgress?.Invoke(name, receivedTotal, size);
                        }
                    }

                    // Confirm successful disk write to the sender.
                    stream.WriteByte(1);
                    await stream.FlushAsync();
                    FileReceived?.Invoke(destination);
                }
            }
            catch (Exception ex)
            {
                if (destination != null && File.Exists(destination))
                {
                    try { File.Delete(destination); }
                    catch { }
                }
                Error?.Invoke(ex.Message);
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
