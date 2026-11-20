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
                Text = "ONKOS File Transfer (Bandeja activa)",
                Icon = LoadAppIcon(),
                ContextMenuStrip = menu,
                Visible = true
            };

            trayIcon.DoubleClick += OnOpenMainForm;
        }

        private Icon LoadAppIcon()
        {
            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "onkos.ico");
                if (File.Exists(icoPath))
                {
                    return new Icon(icoPath);
                }

                Icon exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (exeIcon != null)
                {
                    return exeIcon;
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
            string inboxPath = @"C:\ProgramData\ONKOS\Inbox";
            if (!Directory.Exists(inboxPath))
            {
                Directory.CreateDirectory(inboxPath);
            }

            Process.Start("explorer.exe", inboxPath);
        }

        private void OnExit(object sender, EventArgs e)
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            Application.Exit();
        }
    }
}
