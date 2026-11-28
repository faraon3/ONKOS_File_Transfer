using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ONKOS.Core.Logging;
using ONKOS.Core.Models;
using ONKOS.Core.Storage;

namespace ONKOS.UI
{
    internal static class Program
    {
        private const uint WmShowFirstInstance = 0x8001;
        private static EventWaitHandle _activateEvent;

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            string sessionMutexName = GetSessionMutexName();
            string eventName = sessionMutexName + "_Activate";

            bool createdNew;
            using (var sessionMutex = new Mutex(true, sessionMutexName, out createdNew))
            {
                _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);

                if (!createdNew)
                {
                    try { _activateEvent.Set(); }
                    catch (ObjectDisposedException) { }

                    BringExistingWindowToFront();
                    return;
                }

                string activeFolder;
                string folderError;
                if (!AppSettingsStore.TryEnsureInboxReady(out activeFolder, out folderError))
                {
                    FileLogger.Instance.Warn("UI.Program", "Carpeta de recibidos con advertencia al iniciar: " + folderError);
                }

                PeerInfo profile;
                if (!UserProfileStore.TryLoad(out profile))
                {
                    using (var wizard = new FirstRunWizardForm())
                    {
                        if (wizard.ShowDialog() != DialogResult.OK || wizard.RegisteredProfile == null)
                            return;
                        profile = wizard.RegisteredProfile;
                    }
                }

                var mainForm = new MainForm(profile);
                StartActivationWatcher(mainForm);
                Application.Run(mainForm);
            }
        }

        private static string GetSessionMutexName()
        {
            int sessionId = 0;
            try
            {
                using (var proc = Process.GetCurrentProcess())
                    sessionId = proc.SessionId;
            }
            catch (InvalidOperationException)
            {
                sessionId = 0;
            }

            return $@"Local\ONKOSFileTransfer_UI_Session_{sessionId}";
        }

        private static void StartActivationWatcher(MainForm mainForm)
        {
            var watcher = new Thread(() =>
            {
                while (!mainForm.IsDisposed)
                {
                    try
                    {
                        if (_activateEvent.WaitOne(1000) && !mainForm.IsDisposed)
                        {
                            mainForm.BeginInvoke(new Action(mainForm.BringToFrontFromSecondInstance));
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        break;
                    }
                }
            });
            watcher.IsBackground = true;
            watcher.Name = "ONKOS_SingleInstance_Watcher";
            watcher.Start();
        }

        private static void BringExistingWindowToFront()
        {
            try
            {
                string currentProcName = Process.GetCurrentProcess().ProcessName;
                int currentId = Process.GetCurrentProcess().Id;
                int currentSession = Process.GetCurrentProcess().SessionId;

                foreach (Process p in Process.GetProcessesByName(currentProcName))
                {
                    using (p)
                    {
                        if (p.Id == currentId || p.SessionId != currentSession)
                            continue;

                        if (p.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindowAsync(p.MainWindowHandle, 9);
                            SetForegroundWindow(p.MainWindowHandle);
                            PostMessage(p.MainWindowHandle, WmShowFirstInstance, IntPtr.Zero, IntPtr.Zero);
                            return;
                        }
                    }
                }

                var existing = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
                if (existing != null && !existing.IsDisposed)
                    existing.BringToFrontFromSecondInstance();
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn(
                    "UI.Program",
                    $"No se pudo traer la instancia existente al frente ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            FileLogger.Instance.Error("UI.ThreadException", "Error no controlado en hilo de interfaz.", e.Exception);
            MessageBox.Show(
                "Ocurrió un error inesperado en la interfaz.",
                UiResources.AppTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            FileLogger.Instance.Error("UI.UnhandledException", "Error crítico no controlado en la aplicación.", ex);
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            FileLogger.Instance.Error("UI.UnobservedTask", "Excepción no observada en tarea en segundo plano.", e.Exception);
            e.SetObserved();
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
