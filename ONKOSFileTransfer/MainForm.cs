using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ONKOSFileTransfer.Network;

namespace ONKOSFileTransfer
{
    public class MainForm : Form
    {
        private const int Port = 5050;
        private FileReceiver receiver;

        private TextBox txtIP;
        private TextBox txtFolder;
        private Label lblStatus;
        private Button btnReceive;
        private Button btnSend;

        public MainForm()
        {
            receiver = new FileReceiver();

            receiver.FileReceived += path => UpdateStatus(
                "Archivo recibido: " + Path.GetFileName(path));

            receiver.Error += message => UpdateStatus(
                "Error: " + message);

            BuildInterface();
        }

        private void BuildInterface()
        {
            Text = "ONKOS File Transfer";
            Size = new Size(650, 540);
            MinimumSize = new Size(620, 520);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(25, 26, 40);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 10);

            Label title = new Label
            {
                Text = "ONKOS FILE TRANSFER",
                Font = new Font("Segoe UI", 22, FontStyle.Bold),
                ForeColor = Color.MediumPurple,
                Location = new Point(30, 25),
                Size = new Size(570, 45)
            };
            Controls.Add(title);

            Label ip = new Label
            {
                Text = "MI DIRECCION IP: " +
                    NetworkManager.GetLocalIPv4(),
                ForeColor = Color.LightGreen,
                Location = new Point(32, 90),
                Size = new Size(570, 30)
            };
            Controls.Add(ip);

            Label receive = new Label
            {
                Text = "1. RECIBIR ARCHIVOS",
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                Location = new Point(32, 145),
                Size = new Size(550, 30)
            };
            Controls.Add(receive);

            txtFolder = new TextBox
            {
                Text = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.MyDocuments),
                    "ONKOS_Received"),
                Location = new Point(32, 185),
                Size = new Size(420, 30)
            };
            Controls.Add(txtFolder);

            Button browse = new Button
            {
                Text = "Carpeta...",
                Location = new Point(465, 183),
                Size = new Size(115, 32)
            };
            browse.Click += BrowseFolder;
            Controls.Add(browse);

            btnReceive = new Button
            {
                Text = "INICIAR RECEPCION",
                Location = new Point(32, 230),
                Size = new Size(548, 42),
                BackColor = Color.FromArgb(105, 65, 190),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnReceive.Click += ToggleReceiver;
            Controls.Add(btnReceive);

            Label send = new Label
            {
                Text = "2. ENVIAR ARCHIVOS",
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                Location = new Point(32, 305),
                Size = new Size(550, 30)
            };
            Controls.Add(send);

            txtIP = new TextBox
            {
                Location = new Point(32, 345),
                Size = new Size(548, 30)
            };
            Controls.Add(txtIP);

            btnSend = new Button
            {
                Text = "SELECCIONAR Y ENVIAR ARCHIVO",
                Location = new Point(32, 390),
                Size = new Size(548, 42),
                BackColor = Color.FromArgb(40, 140, 100),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnSend.Click += SendFile;
            Controls.Add(btnSend);

            lblStatus = new Label
            {
                Text = "Estado: listo",
                ForeColor = Color.LightSkyBlue,
                Location = new Point(32, 455),
                Size = new Size(570, 40)
            };
            Controls.Add(lblStatus);
        }

        private void BrowseFolder(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog =
                new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                    txtFolder.Text = dialog.SelectedPath;
            }
        }

        private void ToggleReceiver(object sender, EventArgs e)
        {
            try
            {
                if (!receiver.IsRunning)
                {
                    receiver.Start(Port, txtFolder.Text);
                    btnReceive.Text = "DETENER RECEPCION";
                    UpdateStatus("Esperando archivos en el puerto " + Port);
                }
                else
                {
                    receiver.Stop();
                    btnReceive.Text = "INICIAR RECEPCION";
                    UpdateStatus("Recepcion detenida.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "ONKOS");
            }
        }

        private async void SendFile(object sender, EventArgs e)
        {
            System.Net.IPAddress address;

            if (!System.Net.IPAddress.TryParse(txtIP.Text.Trim(), out address))
            {
                MessageBox.Show("Escribe una direccion IPv4 valida.");
                return;
            }

            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                btnSend.Enabled = false;

                try
                {
                    UpdateStatus("Enviando archivo...");

                    await FileSender.SendFileAsync(
                        address.ToString(), Port, dialog.FileName);

                    UpdateStatus("Archivo enviado correctamente.");
                    MessageBox.Show("Transferencia completada.");
                }
                catch (Exception ex)
                {
                    UpdateStatus("Error de transferencia.");
                    MessageBox.Show(ex.Message, "ONKOS");
                }
                finally
                {
                    btnSend.Enabled = true;
                }
            }
        }

        private void UpdateStatus(string message)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateStatus), message);
                return;
            }

            lblStatus.Text = "Estado: " + message;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            receiver.Stop();
            base.OnFormClosing(e);
        }
    }
}
