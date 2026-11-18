using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ONKOSFileTransfer.Network
{
    public static class NetworkManager
    {
        public static string GetLocalIPv4()
        {
            foreach (NetworkInterface ni in
                NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (ni.NetworkInterfaceType ==
                    NetworkInterfaceType.Loopback)
                    continue;

                foreach (UnicastIPAddressInformation ip in
                    ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily ==
                        AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ip.Address))
                        return ip.Address.ToString();
                }
            }

            return "IP no disponible";
        }
    }
}
