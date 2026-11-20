using System;
using System.Windows.Forms;
using ONKOSFileTransfer.ONKOSTray;

namespace ONKOSFileTransfer
{
    public static class ModernToastNotifier
    {
        public static void ShowNotification(string title, string message, string inboxPath = @"C:\ProgramData\ONKOS\Inbox", int durationMs = 5500, bool isSuccess = true)
        {
            try
            {
                ToastNotificationForm toast = new ToastNotificationForm(title, message, inboxPath, durationMs, isSuccess);
                toast.Show();
            }
            catch
            {
                try
                {
                    MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch
                {
                }
            }
        }
    }
}
