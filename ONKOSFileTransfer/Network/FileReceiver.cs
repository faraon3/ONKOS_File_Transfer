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
            catch (Exception ex)
            {
                if (IsRunning)
                    Error?.Invoke(ex.Message);
            }
        }

        private async Task ReceiveFile(TcpClient client, string folder)
        {
            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                using (BinaryReader reader = new BinaryReader(
                    stream, System.Text.Encoding.UTF8, true))
                {
                    string name = Path.GetFileName(reader.ReadString());
                    long size = reader.ReadInt64();

                    if (string.IsNullOrWhiteSpace(name) ||
                        size < 0 || size > 20L * 1024 * 1024 * 1024)
                        throw new InvalidDataException("Archivo no válido.");

                    string destination = Path.Combine(folder, name);

                    if (File.Exists(destination))
                    {
                        destination = Path.Combine(
                            folder,
                            Path.GetFileNameWithoutExtension(name) + "_" +
                            DateTime.Now.ToString("yyyyMMdd_HHmmssfff") +
                            Path.GetExtension(name));
                    }

                    using (FileStream output = File.Create(destination))
                    {
                        byte[] buffer = new byte[81920];
                        long remaining = size;

                        while (remaining > 0)
                        {
                            int count = (int)Math.Min(buffer.Length, remaining);
                            int received = await stream.ReadAsync(buffer, 0, count);

                            if (received == 0)
                                throw new EndOfStreamException(
                                    "Transferencia incompleta.");

                            await output.WriteAsync(buffer, 0, received);
                            remaining -= received;
                        }
                    }

                    FileReceived?.Invoke(destination);
                }
            }
            catch (Exception ex)
            {
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
