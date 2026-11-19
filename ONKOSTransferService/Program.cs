using System.ServiceProcess;

namespace ONKOSTransferService
{
    internal static class Program
    {
        private static void Main()
        {
            ServiceBase.Run(new ServiceBase[]
            {
                new TransferWindowsService()
            });
        }
    }
}
