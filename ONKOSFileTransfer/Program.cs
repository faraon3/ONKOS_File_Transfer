using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ONKOSFileTransfer
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool esModoBandeja = args != null && args.Any(a =>
                string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "/tray", StringComparison.OrdinalIgnoreCase));

            string nombreMutex = esModoBandeja
                ? "Global\\ONKOSFileTransfer_Tray_Mutex"
                : "Global\\ONKOSFileTransfer_UI_Mutex";

            using (var mutex = new Mutex(true, nombreMutex, out bool esPrimeraInstancia))
            {
                if (!esPrimeraInstancia)
                {
                    // Ya existe una instancia activa de este modo
                    return;
                }

                if (esModoBandeja)
                {
                    ModernToastNotifier.Inicializar();

                    using (var monitor = new InboxMonitor())
                    {
                        // Si ya tienes una clase TrayApplicationContext o similar, úsala aquí:
                        // Application.Run(new TrayApplicationContext());
                        Application.Run(new TrayHostContext());
                    }
                }
                else
                {
                    // Cambia "MainForm" por el nombre de tu formulario principal si se llama distinto
                    Application.Run(new MainForm());
                }
            }
        }
    }

    /// <summary>
    /// Contexto ligero de bandeja por si aún no tienes una clase TrayApplicationContext propia.
    /// Si ya tienes una, reemplaza "new TrayHostContext()" arriba por la tuya.
    /// </summary>
    internal sealed class TrayHostContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;

        public TrayHostContext()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir ventana de envío", null, (s, e) => AbrirVentanaPrincipal());
            menu.Items.Add("Abrir carpeta Inbox", null, (s, e) =>
            {
                string rutaInbox = @"C:\ProgramData\ONKOS\Inbox";
                System.IO.Directory.CreateDirectory(rutaInbox);
                System.Diagnostics.Process.Start("explorer.exe", $"\"{rutaInbox}\"");
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir", null, (s, e) =>
            {
                _notifyIcon.Visible = false;
                Application.ExitThread();
            });

            _notifyIcon = new NotifyIcon
            {
                Text = "ONKOS File Transfer (Bandeja activa)",
                Icon = System.Drawing.SystemIcons.Application, // Puedes cambiarlo por tu ícono .ico si deseas
                ContextMenuStrip = menu,
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => AbrirVentanaPrincipal();
        }

        private void AbrirVentanaPrincipal()
        {
            // Cambia "MainForm" por el nombre de tu formulario principal si se llama distinto
            var form = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
            if (form == null)
            {
                form = new MainForm();
                form.Show();
            }
            else
            {
                if (form.WindowState == FormWindowState.Minimized)
                    form.WindowState = FormWindowState.Normal;
                form.BringToFront();
                form.Activate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
