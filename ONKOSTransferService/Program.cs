using System.ServiceProcess;

namespace ONKOS.TransferService
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