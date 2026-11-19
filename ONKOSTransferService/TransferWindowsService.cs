using System;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace ONKOSTransferService
{
    public partial class TransferWindowsService : ServiceBase
    {
        private const string DefaultInboxFolder = @"C:\ProgramData\ONKOS\Inbox";

        private CancellationTokenSource _cts;
        private Task _receiverTask;

        public TransferWindowsService()
        {
            ServiceName = "ONKOSTransferService";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                Directory.CreateDirectory(DefaultInboxFolder);

                _cts = new CancellationTokenSource();
                _receiverTask = Task.Run(() => RunReceiverLoopAsync(_cts.Token));

                ServiceNotifier.SendInfo(
                    "Servicio iniciado",
                    "ONKOSTransferService está activo y listo para recibir archivos."
                );
            }
            catch (Exception ex)
            {
                ServiceNotifier.SendError("Error al iniciar servicio", ex.Message);
                throw;
            }
        }

        private async Task RunReceiverLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Llama aquí al método principal de recepción de tu clase FileReceiver.cs
                    // pasando DefaultInboxFolder y el CancellationToken 'token'.
                    await Task.Delay(1000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ServiceNotifier.SendError("Error en recepción", ex.Message);
                    await Task.Delay(2000, token);
                }
            }
        }

        protected override void OnStop()
        {
            try
            {
                _cts?.Cancel();
                _receiverTask?.Wait(TimeSpan.FromSeconds(5));

                ServiceNotifier.SendWarning(
                    "Servicio detenido",
                    "ONKOSTransferService se ha detenido."
                );
            }
            catch
            {
                // Evita bloquear la detención del servicio de Windows
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }
    }
}
