using System;
using System.Threading;
using System.Windows.Forms;

namespace ONKOSFileTransfer
{
    internal static class Program
    {
        private static Mutex singleInstanceMutex;

        [STAThread]
        static void Main()
        {
            bool createdNew;
            singleInstanceMutex = new Mutex(
                true,
                @"Local\ONKOSFileTransfer_SingleInstance",
                out createdNew);

            if (!createdNew)
            {
                MessageBox.Show(
                    "ONKOS File Transfer ya está abierto en este usuario.\r\n\r\nBusca la ventana existente antes de iniciar otra instancia.",
                    "ONKOS File Transfer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                singleInstanceMutex.Dispose();
                return;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            finally
            {
                singleInstanceMutex.ReleaseMutex();
                singleInstanceMutex.Dispose();
            }
        }
    }
}
