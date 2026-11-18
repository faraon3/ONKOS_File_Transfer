using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ONKOSFileTransfer.Network
{
    public static class FileSender
    {
        public static async Task SendFileAsync(
            string ip, int port, string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Archivo no encontrado.");

            using (TcpClient client = new TcpClient())
            {
                await client.ConnectAsync(ip, port);

                using (NetworkStream stream = client.GetStream())
                using (BinaryWriter writer = new BinaryWriter(
                    stream, System.Text.Encoding.UTF8, true))
                using (FileStream file = File.OpenRead(filePath))
                {
                    writer.Write(Path.GetFileName(filePath));
                    writer.Write(file.Length);
                    writer.Flush();

                    await file.CopyToAsync(stream);
                    await stream.FlushAsync();
                }
            }
        }
    }
}
