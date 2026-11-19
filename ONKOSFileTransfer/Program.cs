using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using ONKOSFileTransfer.ONKOSTray;

namespace ONKOSFileTransfer
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool trayMode = args != null && args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));
            string mutexName = trayMode
                ? "Global\\ONKOSFileTransfer_Tray_Mutex"
                : "Global\\ONKOSFileTransfer_MainForm_Mutex";

            using (var mutex = new Mutex(true, mutexName, out bool isNewInstance))
            {
                if (!isNewInstance)
                    return;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                if (trayMode)
                {
                    Application.Run(new TrayApplicationContext());
                }
                else
                {
                    Application.Run(new MainForm());
                }
            }
        }
    }
}
