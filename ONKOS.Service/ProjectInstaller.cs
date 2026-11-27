using System.Collections;
using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics;
using System.ServiceProcess;

namespace ONKOS.Service
{
    [RunInstaller(true)]
    public sealed class ProjectInstaller : Installer
    {
        private readonly ServiceProcessInstaller _processInstaller;
        private readonly ServiceInstaller _serviceInstaller;

        public ProjectInstaller()
        {
            _processInstaller = new ServiceProcessInstaller
            {
                Account = ServiceAccount.LocalService
            };

            _serviceInstaller = new ServiceInstaller
            {
                ServiceName = "ONKOSTransferService",
                DisplayName = "ONKOS Transfer Service",
                Description = "Servicio local de recepción de archivos de ONKOS en puerto TCP 5050.",
                StartType = ServiceStartMode.Automatic,
                DelayedAutoStart = true
            };

            Installers.Add(_processInstaller);
            Installers.Add(_serviceInstaller);
        }

        public override void Install(IDictionary stateSaver)
        {
            base.Install(stateSaver);

            if (!EventLog.SourceExists("ONKOS Transfer Service"))
                EventLog.CreateEventSource("ONKOS Transfer Service", "Application");
        }
    }
}
