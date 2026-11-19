using System;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace ONKOSTransferService
{
    public partial class TransferWindowsService : ServiceBase
    {
        private CancellationTokenSource _cts;
        private Task _listenerTask;

        public TransferWindowsService()
        {
            ServiceName = "ONKOSTransferService";
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            _cts = new CancellationTokenSource();
            _listenerTask = Task.Run(() => RunReceiverLoopAsync(_cts.Token));

            ServiceNotifier.SendInfo(
                "Servicio iniciado",
                "ONKOSTransferService está listo para recibir archivos."
            );
        }

        private async Task RunReceiverLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Aquí conectas tu lógica de escucha/recepción en segundo plano
                    // Ejemplo:
                    // await FileReceiver.ListenOnceAsync(token, onFileReceived: fileName =>
                    // {
                    //     ServiceNotifier.SendSuccess("Archivo recibido", $"Se guardó {fileName}.");
                    // },
                    // onRejected: reason =>
                    // {
                    //     ServiceNotifier.SendWarning("Transferencia bloqueada", reason);
                    // });

                    await Task.Delay(500, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ServiceNotifier.SendError("Error en servicio", ex.Message);
                    await Task.Delay(2000, token);
                }
            }
        }

        protected override void OnStop()
        {
            try
            {
                _cts?.Cancel();
                _listenerTask?.Wait(TimeSpan.FromSeconds(5));

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
            }
        }
    }
}
