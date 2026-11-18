using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ONKOSFileTransfer.Network
{
    public static class FileSender
    {
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
                    stream, System.Text.Encoding.UTF8, true))
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

                    // The receiver confirms only after the file has been saved.
                    int confirmation = stream.ReadByte();
                    if (confirmation != 1)
                        throw new IOException("El equipo destino no confirmó que el archivo se guardó.");
                }
            }
        }
    }
}
