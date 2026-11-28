using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using ONKOS.Core.Logging;
using ONKOS.Core.Storage;

namespace ONKOS.Core.Security
{
    public static class DeviceIdentityStore
    {
        private const string IdentityMutex = @"Global\ONKOS_Identity_Lock";
        private static readonly byte[] EntropyBytes = new byte[] { 79, 78, 75, 79, 83, 84, 76, 83, 50 };

        public static bool IsWindows7OrOlder()
        {
            Version v = Environment.OSVersion.Version;
            return Environment.OSVersion.Platform == PlatformID.Win32NT &&
                   (v.Major < 6 || (v.Major == 6 && v.Minor <= 1));
        }

        public static string GetOrCreateThumbprintSha256()
        {
            X509Certificate2 cert = GetOrCreateCertificate();
            return ComputeThumbprintSha256(cert);
        }

        public static string ComputeThumbprintSha256(X509Certificate2 cert)
        {
            if (cert == null)
                return string.Empty;

            byte[] raw = cert.RawData;
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(raw);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToUpperInvariant();
            }
        }

        public static X509Certificate2 GetOrCreateCertificate()
        {
            bool createdNew;
            using (var mutex = new Mutex(false, IdentityMutex, out createdNew))
            {
                bool acquired = false;
                try
                {
                    acquired = mutex.WaitOne(TimeSpan.FromSeconds(10));
                    PathAndAclHelper.EnsureBaseAndLogsFolders();

                    string pfxPath = PathAndAclHelper.IdentityPfxFilePath;
                    string secretPath = PathAndAclHelper.IdentitySecretFilePath;

                    if (File.Exists(pfxPath) && File.Exists(secretPath))
                    {
                        try
                        {
                            string password = UnprotectSecret(File.ReadAllBytes(secretPath));
                            return new X509Certificate2(
                                pfxPath,
                                password,
                                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
                        }
                        catch (CryptographicException ex)
                        {
                            FileLogger.Instance.Warn(
                                "Security.Identity",
                                $"No se pudo abrir certificado existente; se regenerará ({ex.Message}).");
                        }
                    }

                    string generatedPassword = GenerateRandomPassword();
                    X509Certificate2 created = CreateSelfSignedCertificate(Environment.MachineName);
                    byte[] pfxBytes = created.Export(X509ContentType.Pfx, generatedPassword);
                    byte[] protectedSecret = ProtectSecret(generatedPassword);

                    string tempPfx = pfxPath + ".tmp";
                    string tempSecret = secretPath + ".tmp";

                    File.WriteAllBytes(tempPfx, pfxBytes);
                    File.WriteAllBytes(tempSecret, protectedSecret);

                    if (File.Exists(pfxPath))
                        File.Replace(tempPfx, pfxPath, null, true);
                    else
                        File.Move(tempPfx, pfxPath);

                    if (File.Exists(secretPath))
                        File.Replace(tempSecret, secretPath, null, true);
                    else
                        File.Move(tempSecret, secretPath);

                    PathAndAclHelper.TryRestrictFileToAdminsAndService(pfxPath);
                    PathAndAclHelper.TryRestrictFileToAdminsAndService(secretPath);

                    string thumb = ComputeThumbprintSha256(created);
                    FileLogger.Instance.Info("Security.Identity", $"Identidad criptográfica generada. Huella SHA-256: {thumb}");

                    return new X509Certificate2(
                        pfxPath,
                        generatedPassword,
                        X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
                }
                finally
                {
                    if (acquired)
                    {
                        try { mutex.ReleaseMutex(); }
                        catch (ApplicationException) { }
                    }
                }
            }
        }

        private static X509Certificate2 CreateSelfSignedCertificate(string machineName)
        {
            string safeSubject = string.IsNullOrWhiteSpace(machineName) ? "ONKOS-NODE" : machineName.Replace(",", " ");
            using (RSA rsa = RSA.Create(2048))
            {
                var req = new CertificateRequest(
                    "CN=" + safeSubject + ", O=Clinica ONKOS",
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

                req.CertificateExtensions.Add(
                    new X509KeyUsageExtension(
                        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                        critical: true));

                DateTimeOffset now = DateTimeOffset.UtcNow;
                using (X509Certificate2 certWithoutKey = req.CreateSelfSigned(now.AddDays(-1), now.AddYears(5)))
                {
                    return new X509Certificate2(
                        certWithoutKey.Export(X509ContentType.Pkcs12),
                        string.Empty,
                        X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
                }
            }
        }

        private static string GenerateRandomPassword()
        {
            byte[] bytes = new byte[24];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes);
        }

        private static byte[] ProtectSecret(string plainText)
        {
            byte[] raw = System.Text.Encoding.UTF8.GetBytes(plainText);
            return ProtectedData.Protect(raw, EntropyBytes, DataProtectionScope.LocalMachine);
        }

        private static string UnprotectSecret(byte[] cipherBytes)
        {
            byte[] raw = ProtectedData.Unprotect(cipherBytes, EntropyBytes, DataProtectionScope.LocalMachine);
            return System.Text.Encoding.UTF8.GetString(raw);
        }
    }
}
