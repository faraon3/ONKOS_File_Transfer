using System.ServiceProcess;
using ONKOS.Core.Logging;

namespace ONKOS.Service
{
    internal static class Program
    {
        private static void Main()
        {
            System.AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var ex = args.ExceptionObject as System.Exception;
                FileLogger.Instance.Error("Service.Program", "Excepción no controlada en el servicio.", ex);
            };

            ServiceBase.Run(new ServiceBase[]
            {
                new TransferWindowsService()
            });
        }
    }
}
