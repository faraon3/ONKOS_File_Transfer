using System.Net;
using System.Net.Sockets;

namespace ONKOSFileTransfer.Network
{
    public static class NetworkManager
    {
        public static string GetLocalIPv4()
        {
            try
            {
                string fallback = null;
                foreach (IPAddress addr in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (addr.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(addr))
                        continue;

                    string ip = addr.ToString();
                    if (ip.StartsWith("169.254."))
                    {
                        fallback = fallback ?? ip;
                        continue;
                    }

                    return ip;
                }

                if (!string.IsNullOrEmpty(fallback))
                    return fallback;
            }
            catch
            {
            }

            return "127.0.0.1";
        }
    }
}
