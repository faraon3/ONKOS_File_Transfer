using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ONKOSFileTransfer.ONKOSTray
{
    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;

        public TrayApplicationContext()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Abrir ONKOS File Transfer", null, (s, e) => OpenMainForm());
            menu.Items.Add("Abrir carpeta de recibidos", null, (s, e) => OpenInboxFolder());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir", null, (s, e) => ExitApplication());

            Icon trayIcon = SystemIcons.Application;
            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "onkos.ico");
                if (File.Exists(icoPath))
                    trayIcon = new Icon(icoPath);
            }
            catch
            {
            }

            _notifyIcon = new NotifyIcon
            {
                Text = "ONKOS File Transfer 2.0",
                Icon = trayIcon,
                ContextMenuStrip = menu,
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => OpenMainForm();
        }

        private void OpenInboxFolder()
        {
            try
            {
                string inbox = InboxMonitor.CarpetaRecibidos;
                Directory.CreateDirectory(inbox);
                System.Diagnostics.Process.Start("explorer.exe", inbox);
            }
            catch
            {
            }
        }

        private void OpenMainForm()
        {
            PeerInfo profile;
            if (!UserProfileStore.TryLoad(out profile))
            {
                using (var welcome = new WelcomeRegistrationForm())
                {
                    if (welcome.ShowDialog() != DialogResult.OK || welcome.RegisteredProfile == null)
                        return;
                    profile = welcome.RegisteredProfile;
                }
            }

            var existing = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
            if (existing == null || existing.IsDisposed)
            {
                var mainForm = new MainForm(profile);
                mainForm.Show();
            }
            else
            {
                if (existing.WindowState == FormWindowState.Minimized)
                    existing.WindowState = FormWindowState.Normal;
                existing.Show();
                existing.BringToFront();
                existing.Activate();
            }
        }

        private void ExitApplication()
        {
            _notifyIcon.Visible = false;
            Application.ExitThread();
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
