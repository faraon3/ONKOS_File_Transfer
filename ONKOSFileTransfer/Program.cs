using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ONKOSFileTransfer.Network;

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
                    return;

                PeerInfo profile = null;
                if (!esModoBandeja && !UserProfileStore.TryLoad(out profile))
                {
                    using (var welcome = new WelcomeRegistrationForm())
                    {
                        if (welcome.ShowDialog() != DialogResult.OK || welcome.RegisteredProfile == null)
                            return;
                        profile = welcome.RegisteredProfile;
                    }
                }

                if (esModoBandeja)
                {
                    ModernToastNotifier.Inicializar();
                    using (var monitor = new InboxMonitor())
                    {
                        Application.Run(new TrayHostContext());
                    }
                }
                else
                {
                    Application.Run(new MainForm(profile));
                }
            }
        }
    }

    internal sealed class TrayHostContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;

        public TrayHostContext()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir ventana de envío", null, (s, e) => AbrirVentanaPrincipal());
            menu.Items.Add("Abrir carpeta de recibidos", null, (s, e) =>
            {
                string rutaInbox = InboxMonitor.CarpetaRecibidos;
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
                Text = "ONKOS File Transfer (Activo)",
                Icon = System.Drawing.SystemIcons.Application,
                ContextMenuStrip = menu,
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => AbrirVentanaPrincipal();
        }

        private void AbrirVentanaPrincipal()
        {
            if (!UserProfileStore.TryLoad(out PeerInfo profile))
            {
                using (var welcome = new WelcomeRegistrationForm())
                {
                    if (welcome.ShowDialog() != DialogResult.OK || welcome.RegisteredProfile == null)
                        return;
                    profile = welcome.RegisteredProfile;
                }
            }

            var form = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
            if (form == null)
            {
                form = new MainForm(profile);
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
            if (disposing && _notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
