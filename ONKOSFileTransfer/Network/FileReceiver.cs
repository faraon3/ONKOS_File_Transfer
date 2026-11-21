using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ONKOSFileTransfer.Network
{
    public class FileReceiver
    {
        private TcpListener listener;
        private static readonly object SenderFolderLock = new object();

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
            _ = AcceptLoop(folder);
        }

        private async Task AcceptLoop(string folder)
        {
            try
            {
                while (IsRunning)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => ReceiveFile(client, folder));
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
            catch (Exception ex)
            {
                if (IsRunning)
                    Error?.Invoke(ex.Message);
            }
        }

        private static (bool Valid, string FullName, string Area) ReadLocalProfileSafe()
        {
            try
            {
                string profilePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ONKOS",
                    "perfil.txt");

                if (File.Exists(profilePath))
                {
                    string[] lines = File.ReadAllLines(profilePath, Encoding.UTF8);
                    string name = lines.Length > 0 ? lines[0].Trim() : string.Empty;
                    string area = lines.Length > 1 ? lines[1].Trim() : string.Empty;
                    string upper = name.ToUpperInvariant();

                    if (!string.IsNullOrWhiteSpace(name) &&
                        !string.IsNullOrWhiteSpace(area) &&
                        upper != "SYSTEM" &&
                        upper != "NT AUTHORITY\\SYSTEM" &&
                        upper != "LOCAL SERVICE" &&
                        upper != "NETWORK SERVICE")
                    {
                        return (true, name, area);
                    }
                }
            }
            catch
            {
            }

            return (false, string.Empty, string.Empty);
        }

        private static string GetLocalIPv4Safe()
        {
            try
            {
                foreach (IPAddress addr in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (addr.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr))
                        return addr.ToString();
                }
            }
            catch
            {
            }

            return "127.0.0.1";
        }

        private async Task ReceiveFile(TcpClient client, string folder)
        {
            string destination = null;
            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                using (BinaryReader reader = new BinaryReader(
                    stream, Encoding.UTF8, true))
                {
                    string firstToken = reader.ReadString();

                    if (string.Equals(firstToken, "ONKOS_WHOAMI_V1", StringComparison.Ordinal))
                    {
                        var profile = ReadLocalProfileSafe();
                        string localIp = GetLocalIPv4Safe();
                        string machine = Environment.MachineName;

                        using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true))
                        {
                            if (profile.Valid)
                            {
                                writer.Write("ONKOS_IDENTITY_V1");
                                writer.Write(profile.FullName);
                                writer.Write(profile.Area);
                                writer.Write(localIp);
                                writer.Write(machine);
                            }
                            else
                            {
                                writer.Write("ONKOS_UNREGISTERED_V1");
                            }
                            writer.Flush();
                        }
                        return;
                    }

                    string name = Path.GetFileName(firstToken);
                    long size = reader.ReadInt64();

                    if (string.IsNullOrWhiteSpace(name) || name == "." || name == ".." ||
                        size < 0 || size > 20L * 1024 * 1024 * 1024)
                        throw new InvalidDataException("Archivo no válido o tamaño fuera del límite.");

                    string senderFolder = GetSenderFolder(client, folder);
                    destination = Path.Combine(senderFolder, name);
                    if (File.Exists(destination))
                    {
                        destination = Path.Combine(senderFolder,
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

                    if (string.Equals(Path.GetExtension(destination), ".onkosfolder", StringComparison.OrdinalIgnoreCase))
                    {
                        string folderName = Path.GetFileNameWithoutExtension(destination);
                        int guidSeparator = folderName.LastIndexOf('_');
                        Guid packageId;
                        if (guidSeparator > 0 && Guid.TryParse(folderName.Substring(guidSeparator + 1), out packageId))
                            folderName = folderName.Substring(0, guidSeparator);
                        if (string.IsNullOrWhiteSpace(folderName))
                            folderName = "Carpeta_recibida_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        string extractRoot = Path.Combine(senderFolder, folderName);
                        int suffix = 1;
                        while (Directory.Exists(extractRoot))
                            extractRoot = Path.Combine(senderFolder, folderName + "_" + suffix++);

                        Directory.CreateDirectory(extractRoot);
                        string rootFull = Path.GetFullPath(extractRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                        using (ZipArchive archive = ZipFile.OpenRead(destination))
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
                                    if (!Directory.Exists(parent)) Directory.CreateDirectory(parent);
                                    entry.ExtractToFile(target, false);
                                }
                            }
                        }
                        File.Delete(destination);
                        destination = extractRoot;
                    }

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

        private static string GetSenderFolder(TcpClient client, string rootFolder)
        {
            string senderIp = "IP_desconocida";
            try
            {
                IPEndPoint remote = client.Client.RemoteEndPoint as IPEndPoint;
                if (remote != null && remote.Address != null)
                    senderIp = remote.Address.ToString().Replace(':', '_');
            }
            catch
            {
            }

            string dayFolder = Path.Combine(rootFolder, DateTime.Now.ToString("yyyy-MM-dd"));
            lock (SenderFolderLock)
            {
                Directory.CreateDirectory(dayFolder);

                string prefix = senderIp + "_";
                string existing = null;
                try
                {
                    foreach (string candidate in Directory.GetDirectories(dayFolder, prefix + "*"))
                    {
                        if (existing == null ||
                            Directory.GetCreationTime(candidate) < Directory.GetCreationTime(existing))
                            existing = candidate;
                    }
                }
                catch
                {
                }

                if (!string.IsNullOrEmpty(existing))
                    return existing;

                string folderName = senderIp + "_" + DateTime.Now.ToString("HH-mm-ss");
                string senderFolder = Path.Combine(dayFolder, folderName);
                int suffix = 1;
                while (Directory.Exists(senderFolder))
                    senderFolder = Path.Combine(dayFolder, folderName + "_" + suffix++);
                Directory.CreateDirectory(senderFolder);
                return senderFolder;
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
