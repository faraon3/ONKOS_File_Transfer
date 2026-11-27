using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics;
using System.ServiceProcess;

namespace ONKOS.TransferService
{
    [RunInstaller(true)]
    public sealed class ProjectInstaller : Installer
    {
        private readonly ServiceProcessInstaller processInstaller;
        private readonly ServiceInstaller serviceInstaller;

        public ProjectInstaller()
        {
            processInstaller = new ServiceProcessInstaller
            {
                Account = ServiceAccount.LocalService
            };

            serviceInstaller = new ServiceInstaller
            {
                ServiceName = "ONKOSTransferService",
                DisplayName = "ONKOS Transfer Service",
                Description = "Servicio local de recepción de archivos de ONKOS. La recepción de red se habilitará después de implementar autenticación y cifrado.",
                StartType = ServiceStartMode.Automatic,
                DelayedAutoStart = true
            };

            Installers.Add(processInstaller);
            Installers.Add(serviceInstaller);
        }

        public override void Install(System.Collections.IDictionary stateSaver)
        {
            base.Install(stateSaver);

            if (!EventLog.SourceExists("ONKOS Transfer Service"))
                EventLog.CreateEventSource("ONKOS Transfer Service", "Application");
        }

        public override void Uninstall(System.Collections.IDictionary savedState)
        {
            base.Uninstall(savedState);
        }
    }
}
