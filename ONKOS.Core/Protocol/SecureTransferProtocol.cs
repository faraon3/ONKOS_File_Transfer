using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ONKOS.Core.Logging;
using ONKOS.Core.Security;

namespace ONKOS.Core.Protocol
{
    public sealed class TransferLimits
    {
        public long MaxSingleFileBytes { get; set; } = 2L * 1024L * 1024L * 1024L;
        public long MaxTotalBatchBytes { get; set; } = 5L * 1024L * 1024L * 1024L;
        public int MaxFileCount { get; set; } = 500;
        public long MaxUncompressedBytes { get; set; } = 5L * 1024L * 1024L * 1024L;
        public double MaxCompressionRatio { get; set; } = 25.0;
        public int IoTimeoutMs { get; set; } = 30000;
        public HashSet<string> BlockedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bat", ".cmd", ".com", ".cpl", ".hta", ".js", ".jse", ".msc", ".msi", ".msp", ".pif", ".ps1", ".reg", ".scr", ".vbs", ".vbe", ".wsf", ".wsh"
        };
    }

    public sealed class ManifestItem
    {
        public string RelativePath { get; set; }
        public long SizeBytes { get; set; }
        public string Sha256Hex { get; set; }
        public bool IsDirectory { get; set; }
    }

    public static class SecureTransferProtocol
    {
        public const string ProtocolMagic = "ONKOS_TLS_V2";
        private static readonly string[] ReservedWindowsNames = new[]
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        public static SslProtocols GetSupportedTlsProtocols()
        {
            if (DeviceIdentityStore.IsWindows7OrOlder())
                throw new PlatformNotSupportedException("Windows 7 o anterior no está habilitado para TLS 1.2+ seguro en ONKOS File Transfer 2.0.");

#pragma warning disable CS0618
            return SslProtocols.Tls12 | SslProtocols.Tls13;
#pragma warning restore CS0618
        }

        public static string ComputeFileSha256Hex(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan))
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(fs);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        public static string NormalizeAndValidateRelativePath(string rawRelativePath, TransferLimits limits)
        {
            if (string.IsNullOrWhiteSpace(rawRelativePath))
                throw new InvalidDataException("La ruta relativa del elemento está vacía.");

            string normalized = rawRelativePath.Replace('\\', '/').Trim();
            if (normalized.StartsWith("/", StringComparison.Ordinal) ||
                normalized.StartsWith("//", StringComparison.Ordinal) ||
                normalized.Contains(":"))
            {
                throw new InvalidDataException("Ruta relativa no permitida.");
            }

            if (normalized.Length > 260)
                throw new InvalidDataException("La ruta relativa excede la longitud máxima permitida.");

            char[] invalidChars = Path.GetInvalidFileNameChars();
            string[] segments = normalized.Split(new[] { '/' }, StringSplitOptions.None);
            var cleanSegments = new List<string>(segments.Length);

            foreach (string seg in segments)
            {
                if (string.IsNullOrEmpty(seg) || seg == "." || seg == "..")
                    throw new InvalidDataException("Secuencia de ruta inválida (path traversal).");

                if (seg.IndexOfAny(invalidChars) >= 0)
                    throw new InvalidDataException("Nombre de archivo o carpeta con caracteres no válidos.");

                string upperNoExt = Path.GetFileNameWithoutExtension(seg).ToUpperInvariant();
                if (ReservedWindowsNames.Contains(upperNoExt))
                    throw new InvalidDataException("Nombre reservado de Windows no permitido.");

                cleanSegments.Add(seg);
            }

            string finalRel = string.Join(Path.DirectorySeparatorChar.ToString(), cleanSegments);
            string ext = Path.GetExtension(finalRel);
            if (!string.IsNullOrEmpty(ext) && limits != null && limits.BlockedExtensions.Contains(ext))
                throw new InvalidDataException($"Extensión bloqueada por política: {ext}");

            return finalRel;
        }

        public static string ResolveSafeDestinationPath(string rootFolder, string safeRelativePath)
        {
            string rootFull = Path.GetFullPath(rootFolder);
            if (!rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                rootFull += Path.DirectorySeparatorChar;

            string combined = Path.GetFullPath(Path.Combine(rootFull, safeRelativePath));
            if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Intento de escritura fuera de la carpeta de recepción.");

            return combined;
        }

        public static string GetNonOverwritingPath(string candidatePath)
        {
            if (!File.Exists(candidatePath) && !Directory.Exists(candidatePath))
                return candidatePath;

            string dir = Path.GetDirectoryName(candidatePath) ?? string.Empty;
            string nameNoExt = Path.GetFileNameWithoutExtension(candidatePath);
            string ext = Path.GetExtension(candidatePath);

            for (int i = 1; i <= 10000; i++)
            {
                string next = Path.Combine(dir, $"{nameNoExt} ({i}){ext}");
                if (!File.Exists(next) && !Directory.Exists(next))
                    return next;
            }

            return Path.Combine(dir, $"{nameNoExt}_{Guid.NewGuid().ToString("N").Substring(0, 8)}{ext}");
        }

        public static async Task<SslStream> AuthenticateServerAsync(Stream rawStream, CancellationToken token)
        {
            SslProtocols protocols = GetSupportedTlsProtocols();
            X509Certificate2 serverCert = DeviceIdentityStore.GetOrCreateCertificate();

            var ssl = new SslStream(
                rawStream,
                leaveInnerStreamOpen: false,
                userCertificateValidationCallback: (sender, cert, chain, errors) => cert != null);

            ssl.ReadTimeout = 30000;
            ssl.WriteTimeout = 30000;

            try
            {
                using (token.Register(() => { try { ssl.Close(); } catch (ObjectDisposedException) { } }))
                {
                    await ssl.AuthenticateAsServerAsync(
                        serverCertificate: serverCert,
                        clientCertificateRequired: true,
                        enabledSslProtocols: protocols,
                        checkCertificateRevocation: false).ConfigureAwait(false);
                }

                if (ssl.RemoteCertificate == null)
                {
                    ssl.Dispose();
                    throw new AuthenticationException("El cliente no presentó certificado de identidad.");
                }

                return ssl;
            }
            catch
            {
                ssl.Dispose();
                throw;
            }
        }

        public static async Task<SslStream> AuthenticateClientAsync(Stream rawStream, string targetHost, CancellationToken token)
        {
            SslProtocols protocols = GetSupportedTlsProtocols();
            X509Certificate2 clientCert = DeviceIdentityStore.GetOrCreateCertificate();
            var certCollection = new X509Certificate2Collection { clientCert };

            var ssl = new SslStream(
                rawStream,
                leaveInnerStreamOpen: false,
                userCertificateValidationCallback: (sender, cert, chain, errors) => cert != null);

            ssl.ReadTimeout = 30000;
            ssl.WriteTimeout = 30000;

            try
            {
                using (token.Register(() => { try { ssl.Close(); } catch (ObjectDisposedException) { } }))
                {
                    await ssl.AuthenticateAsClientAsync(
                        targetHost: string.IsNullOrWhiteSpace(targetHost) ? "ONKOS-PEER" : targetHost,
                        clientCertificates: certCollection,
                        enabledSslProtocols: protocols,
                        checkCertificateRevocation: false).ConfigureAwait(false);
                }

                if (ssl.RemoteCertificate == null)
                {
                    ssl.Dispose();
                    throw new AuthenticationException("El servidor no presentó certificado de identidad.");
                }

                return ssl;
            }
            catch
            {
                ssl.Dispose();
                throw;
            }
        }

        public static string GetRemoteFingerprintSha256(SslStream sslStream)
        {
            if (sslStream == null || sslStream.RemoteCertificate == null)
                return string.Empty;

            using (var remoteCert = new X509Certificate2(sslStream.RemoteCertificate))
            {
                return DeviceIdentityStore.ComputeThumbprintSha256(remoteCert);
            }
        }

        public static void WriteHeader(BinaryWriter writer, string senderName, string senderArea, string machineName, int itemCount)
        {
            writer.Write(ProtocolMagic);
            writer.Write(senderName ?? string.Empty);
            writer.Write(senderArea ?? string.Empty);
            writer.Write(machineName ?? string.Empty);
            writer.Write(itemCount);
            writer.Flush();
        }

        public static void ReadAndValidateHeader(
            BinaryReader reader,
            out string senderName,
            out string senderArea,
            out string machineName,
            out int itemCount,
            TransferLimits limits)
        {
            string magic = reader.ReadString();
            if (!string.Equals(magic, ProtocolMagic, StringComparison.Ordinal))
                throw new InvalidDataException("Versión o cabecera de protocolo no compatible.");

            senderName = (reader.ReadString() ?? string.Empty).Trim();
            senderArea = (reader.ReadString() ?? string.Empty).Trim();
            machineName = (reader.ReadString() ?? string.Empty).Trim();
            itemCount = reader.ReadInt32();

            if (itemCount <= 0 || itemCount > limits.MaxFileCount)
                throw new InvalidDataException("Cantidad de elementos fuera del límite permitido.");
        }

        public static void WriteManifestItem(BinaryWriter writer, ManifestItem item)
        {
            writer.Write(item.RelativePath ?? string.Empty);
            writer.Write(item.SizeBytes);
            writer.Write(item.Sha256Hex ?? string.Empty);
            writer.Write(item.IsDirectory);
            writer.Flush();
        }

        public static ManifestItem ReadAndValidateManifestItem(BinaryReader reader, TransferLimits limits, long accumulatedBatchBytes)
        {
            string rawRel = reader.ReadString();
            long sizeBytes = reader.ReadInt64();
            string sha256 = (reader.ReadString() ?? string.Empty).Trim().ToLowerInvariant();
            bool isDir = reader.ReadBoolean();

            string safeRel = NormalizeAndValidateRelativePath(rawRel, limits);

            if (sizeBytes < 0L || sizeBytes > limits.MaxSingleFileBytes)
                throw new InvalidDataException("Tamaño de archivo fuera del límite individual permitido.");

            if (accumulatedBatchBytes + sizeBytes > limits.MaxTotalBatchBytes)
                throw new InvalidDataException("El tamaño total del envío excede el límite configurado.");

            if (!isDir && (string.IsNullOrWhiteSpace(sha256) || sha256.Length != 64))
                throw new InvalidDataException("Hash SHA-256 faltante o con formato inválido.");

            return new ManifestItem
            {
                RelativePath = safeRel,
                SizeBytes = sizeBytes,
                Sha256Hex = sha256,
                IsDirectory = isDir
            };
        }

        public static void ValidateArchiveSafety(string zipPath, long declaredCompressedBytes, TransferLimits limits)
        {
            using (var archive = System.IO.Compression.ZipFile.OpenRead(zipPath))
            {
                long totalUncompressed = 0L;
                int entriesCount = 0;

                foreach (var entry in archive.Entries)
                {
                    entriesCount++;
                    if (entriesCount > limits.MaxFileCount)
                        throw new InvalidDataException("La carpeta comprimida excede el límite de archivos.");

                    NormalizeAndValidateRelativePath(entry.FullName.TrimEnd('/'), limits);
                    totalUncompressed += entry.Length;

                    if (totalUncompressed > limits.MaxUncompressedBytes)
                        throw new InvalidDataException("El tamaño descomprimido excede el límite permitido (posible zip-bomb).");
                }

                if (declaredCompressedBytes > 0L)
                {
                    double ratio = (double)totalUncompressed / declaredCompressedBytes;
                    if (ratio > limits.MaxCompressionRatio)
                        throw new InvalidDataException($"Relación de compresión sospechosa ({ratio.ToString("0.0", CultureInfo.InvariantCulture)}x).");
                }
                else if (totalUncompressed > 0L)
                {
                    throw new InvalidDataException("Paquete comprimido con tamaño cero inválido.");
                }
            }
        }
    }
}
