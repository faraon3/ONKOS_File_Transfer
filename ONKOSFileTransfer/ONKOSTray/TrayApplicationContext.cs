using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ONKOSFileTransfer.ONKOSTray
{
    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon trayIcon;
        private MainForm mainForm;

        public TrayApplicationContext()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Enviar archivos...", null, OnOpenMainForm);
            menu.Items.Add("Abrir carpeta de recibidos", null, OnOpenInboxFolder);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir", null, OnExit);

            trayIcon = new NotifyIcon
            {
                Text = "ONKOS File Transfer - Activo",
                Icon = LoadTrayIcon(),
                ContextMenuStrip = menu,
                Visible = true
            };

            trayIcon.DoubleClick += OnOpenMainForm;
        }

        private Icon LoadTrayIcon()
        {
            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "onkos.ico");
                if (File.Exists(icoPath))
                {
                    using (Icon raw = new Icon(icoPath, new Size(32, 32)))
                    {
                        return (Icon)raw.Clone();
                    }
                }
            }
            catch
            {
            }

            try
            {
                Icon assoc = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (assoc != null)
                {
                    using (Bitmap bmp = assoc.ToBitmap())
                    using (Bitmap resized = new Bitmap(bmp, new Size(32, 32)))
                    {
                        IntPtr hIcon = resized.GetHicon();
                        return Icon.FromHandle(hIcon);
                    }
                }
            }
            catch
            {
            }

            return SystemIcons.Application;
        }

        private void OnOpenMainForm(object sender, EventArgs e)
        {
            if (mainForm == null || mainForm.IsDisposed)
            {
                mainForm = new MainForm();
            }

            if (!mainForm.Visible)
            {
                mainForm.Show();
            }

            if (mainForm.WindowState == FormWindowState.Minimized)
            {
                mainForm.WindowState = FormWindowState.Normal;
            }

            mainForm.Activate();
        }

        private void OnOpenInboxFolder(object sender, EventArgs e)
        {
            string inboxPath = InboxMonitor.CarpetaRecibidos;
            if (!Directory.Exists(inboxPath))
            {
                Directory.CreateDirectory(inboxPath);
            }

            Process.Start("explorer.exe", $"\"{inboxPath}\"");
        }

        private void OnExit(object sender, EventArgs e)
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            Application.Exit();
        }
    }
}
