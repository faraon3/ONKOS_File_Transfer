using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ONKOSFileTransfer.ONKOSTray
{
    public class TrayApplicationContext : ApplicationContext
    {
        private const string ServiceName = "ONKOSTransferService";
        private const string PipeName = "ONKOS_Transfer_Events";
        private readonly string _inboxPath = @"C:\ProgramData\ONKOS\Inbox";

        private readonly NotifyIcon _trayIcon;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public TrayApplicationContext()
        {
            Directory.CreateDirectory(_inboxPath);

            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir ONKOS File Transfer", null, (s, e) => OpenMainForm());
            menu.Items.Add("Abrir carpeta de recibidos", null, (s, e) => OpenInboxFolder());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Estado del servicio", null, (s, e) => ShowServiceStatus());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Cerrar ícono de bandeja", null, (s, e) => ExitTray());

            _trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "ONKOS File Transfer - En espera",
                ContextMenuStrip = menu,
                Visible = true
            };

            _trayIcon.DoubleClick += (s, e) => OpenMainForm();

            Task.Run(() => ListenServiceEventsAsync(_cts.Token));
        }

        private async Task ListenServiceEventsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using (var pipeServer = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Message,
                        PipeOptions.Asynchronous))
                    {
                        await pipeServer.WaitForConnectionAsync(token);

                        using (var reader = new StreamReader(pipeServer, Encoding.UTF8))
                        {
                            string payload = await reader.ReadToEndAsync();
                            HandleEventPayload(payload);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(1000, token);
                }
            }
        }

        private void HandleEventPayload(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return;

            // Formato esperado: TIPO|TITULO|MENSAJE
            string[] parts = payload.Split(new[] { '|' }, 3);
            if (parts.Length < 3)
                return;

            ToastType type = ParseToastType(parts[0]);
            string title = parts[1].Trim();
            string message = parts[2].Trim();

            SynchronizationContext.Current?.Post(_ =>
            {
                ToastNotificationForm.ShowToast(title, message, type, () => OpenInboxFolder());
            }, null);
        }

        private static ToastType ParseToastType(string rawType)
        {
            if (string.Equals(rawType, "SUCCESS", StringComparison.OrdinalIgnoreCase))
                return ToastType.Success;
            if (string.Equals(rawType, "WARNING", StringComparison.OrdinalIgnoreCase))
                return ToastType.Warning;
            if (string.Equals(rawType, "ERROR", StringComparison.OrdinalIgnoreCase))
                return ToastType.Error;

            return ToastType.Info;
        }

        private void OpenMainForm()
        {
            try
            {
                string exePath = Application.ExecutablePath;
                Process.Start(exePath);
            }
            catch (Exception ex)
            {
                ToastNotificationForm.ShowToast(
                    "No se pudo abrir la ventana",
                    ex.Message,
                    ToastType.Error);
            }
        }

        private void OpenInboxFolder()
        {
            try
            {
                Directory.CreateDirectory(_inboxPath);
                Process.Start("explorer.exe", _inboxPath);
            }
            catch (Exception ex)
            {
                ToastNotificationForm.ShowToast(
                    "Error al abrir carpeta",
                    ex.Message,
                    ToastType.Error);
            }
        }

        private void ShowServiceStatus()
        {
            try
            {
                using (var controller = new ServiceController(ServiceName))
                {
                    if (controller.Status == ServiceControllerStatus.Running)
                    {
                        ToastNotificationForm.ShowToast(
                            "Servicio activo",
                            $"{ServiceName} está ejecutándose en segundo plano.",
                            ToastType.Success);
                    }
                    else
                    {
                        ToastNotificationForm.ShowToast(
                            "Servicio no activo",
                            $"Estado actual de {ServiceName}: {controller.Status}",
                            ToastType.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                ToastNotificationForm.ShowToast(
                    "Error consultando servicio",
                    ex.Message,
                    ToastType.Error);
            }
        }

        private void ExitTray()
        {
            _cts.Cancel();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            Application.ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _cts.Cancel();
                _cts.Dispose();
                _trayIcon?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
