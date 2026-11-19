using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;
using ONKOSFileTransfer.Network;

namespace ONKOSFileTransfer
{
    public class MainForm : Form
    {
        private const int Port = 5050;

        // Paleta exacta de ONKOS File Transfer 2.0
        private static readonly Color BgWindow = Color.FromArgb(18, 19, 30);
        private static readonly Color BgSurface = Color.FromArgb(28, 30, 46);
        private static readonly Color BgInput = Color.FromArgb(32, 34, 52);
        private static readonly Color BgHeaderTable = Color.FromArgb(36, 39, 58);
        private static readonly Color BorderSubtle = Color.FromArgb(58, 62, 88);
        private static readonly Color AccentPurple = Color.FromArgb(124, 77, 224);
        private static readonly Color AccentPurpleLight = Color.FromArgb(177, 139, 255);
        private static readonly Color StatusGreen = Color.FromArgb(74, 222, 128);
        private static readonly Color TextPrimary = Color.FromArgb(245, 246, 250);
        private static readonly Color TextSecondary = Color.FromArgb(156, 163, 184);
        private static readonly Color TextLink = Color.FromArgb(96, 165, 250);

        private readonly FileReceiver receiver;
        private TextBox txtIP;
        private Panel pnlDropZone;
        private ListView listFiles;
        private Button btnSend;
        private ProgressBar progressBar;
        private Label lblPercent;
        private Label lblStatus;
        private Label lblRemaining;
        private Label lblReceiverFooter;
        private string activeFolder;
        private bool isSending;
        private bool isDragOver;

        public MainForm()
        {
            DoubleBuffered = true;
            activeFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ONKOS_Received");

            receiver = new FileReceiver();
            receiver.FileReceived += path =>
                UpdateReceiverFooter("Recibido: " + Path.GetFileName(path));
            receiver.TransferProgress += (name, received, size) =>
                UpdateReceiverFooter("Recibiendo " + name + " (" + FormatBytes(received) + " / " + FormatBytes(size) + ")");
            receiver.Error += message =>
                UpdateReceiverFooter("Error de recepción: " + message);

            BuildInterface();
            StartReceiver(activeFolder);
        }

        private void BuildInterface()
        {
            Text = "ONKOS File Transfer 2.0";
            ClientSize = new Size(960, 680);
            MinimumSize = new Size(960, 680);
            MaximumSize = new Size(960, 680);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgWindow;
            ForeColor = TextPrimary;
            Font = new Font("Segoe UI", 9.5F);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            // --- ENCABEZADO SUPERIOR ---
            Panel pnlLogo = new Panel
            {
                Location = new Point(26, 20),
                Size = new Size(28, 28),
                BackColor = Color.Transparent
            };
            pnlLogo.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Point[] outer = { new Point(14, 3), new Point(25, 14), new Point(14, 25), new Point(3, 14) };
                Point[] inner = { new Point(14, 8), new Point(20, 14), new Point(14, 20), new Point(8, 14) };
                using (Pen p = new Pen(AccentPurpleLight, 2f))
                {
                    e.Graphics.DrawPolygon(p, outer);
                    e.Graphics.DrawPolygon(p, inner);
                }
            };
            Controls.Add(pnlLogo);

            Controls.Add(new Label
            {
                Text = "ONKOS",
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = AccentPurpleLight,
                Location = new Point(58, 17),
                Size = new Size(140, 30)
            });

            Controls.Add(new Label
            {
                Text = "FILE TRANSFER 2.0  •  ENVÍA Y RECIBE EN TU RED",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextSecondary,
                Location = new Point(28, 50),
                Size = new Size(360, 20)
            });

            // Píldora de IP y estado
            Panel pnlIpPill = CreateRoundedPanel(new Point(495, 22), new Size(285, 38), BgSurface, BorderSubtle, 18);
            pnlIpPill.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush dot = new SolidBrush(StatusGreen))
                    e.Graphics.FillEllipse(dot, 124, 15, 7, 7);
            };
            pnlIpPill.Controls.Add(new Label
            {
                Text = "IP " + NetworkManager.GetLocalIPv4(),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextPrimary,
                Location = new Point(14, 9),
                Size = new Size(106, 20)
            });
            pnlIpPill.Controls.Add(new Label
            {
                Text = "Recepción automática activa",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = StatusGreen,
                Location = new Point(136, 9),
                Size = new Size(144, 20)
            });
            Controls.Add(pnlIpPill);

            // Botón Administración
            Button btnAdmin = new Button
            {
                Text = "⚙  Administración",
                Location = new Point(792, 22),
                Size = new Size(142, 38),
                BackColor = BgSurface,
                ForeColor = TextPrimary,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnAdmin.FlatAppearance.BorderColor = BorderSubtle;
            btnAdmin.FlatAppearance.BorderSize = 1;
            btnAdmin.Click += OpenAdminOptions;
            Controls.Add(btnAdmin);

            // --- TÍTULO DE SECCIÓN ---
            Controls.Add(new Label
            {
                Text = "Centro de transferencias",
                Font = new Font("Segoe UI", 17F, FontStyle.Bold),
                ForeColor = TextPrimary,
                Location = new Point(26, 94),
                Size = new Size(450, 32)
            });

            Controls.Add(new Label
            {
                Text = "Envía archivos o carpetas a otros computadores y recibe transferencias en esta estación.",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextSecondary,
                Location = new Point(28, 127),
                Size = new Size(650, 22)
            });

            // --- CAMPO DESTINO (CORREGIDO SIN CORTES) ---
            Controls.Add(new Label
            {
                Text = "DESTINO",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextSecondary,
                Location = new Point(28, 168),
                Size = new Size(76, 22)
            });

            Panel pnlDestBox = CreateRoundedPanel(new Point(112, 161), new Size(822, 36), BgInput, BorderSubtle, 8);
            txtIP = new TextBox
            {
                Location = new Point(14, 8),
                Size = new Size(794, 22),
                Font = new Font("Segoe UI", 10F),
                BackColor = BgInput,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.None
            };
            pnlDestBox.Controls.Add(txtIP);
            Controls.Add(pnlDestBox);

            // --- ZONA DRAG & DROP (CORREGIDA CON ALTURA SUFICIENTE) ---
            Controls.Add(new Label
            {
                Text = "ARCHIVOS Y CARPETAS  •  ARRASTRA AQUÍ PARA AÑADIR",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextSecondary,
                Location = new Point(28, 214),
                Size = new Size(500, 20)
            });

            pnlDropZone = new Panel
            {
                Location = new Point(28, 240),
                Size = new Size(906, 114),
                BackColor = BgSurface,
                Cursor = Cursors.Hand,
                AllowDrop = true
            };
            pnlDropZone.Paint += PaintDropZone;
            pnlDropZone.Click += AddFilesOrFolders;
            pnlDropZone.DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effect = DragDropEffects.Copy;
                    isDragOver = true;
                    pnlDropZone.Invalidate();
                }
            };
            pnlDropZone.DragLeave += (s, e) =>
            {
                isDragOver = false;
                pnlDropZone.Invalidate();
            };
            pnlDropZone.DragDrop += (s, e) =>
            {
                isDragOver = false;
                pnlDropZone.Invalidate();
                if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
                    AddPathsToQueue(dropped);
            };
            Controls.Add(pnlDropZone);

            // --- TABLA DE ARCHIVOS (ENCABEZADO LIMPIO + LISTVIEW) ---
            Panel pnlTableHeader = new Panel
            {
                Location = new Point(28, 368),
                Size = new Size(906, 30),
                BackColor = BgHeaderTable
            };
            pnlTableHeader.Controls.Add(MakeTableHeaderLabel("ARCHIVO", 16, 540));
            pnlTableHeader.Controls.Add(MakeTableHeaderLabel("TAMAÑO", 560, 150));
            pnlTableHeader.Controls.Add(MakeTableHeaderLabel("ESTADO", 720, 170));
            Controls.Add(pnlTableHeader);

            listFiles = new ListView
            {
                Location = new Point(28, 398),
                Size = new Size(906, 128),
                View = View.Details,
                HeaderStyle = ColumnHeaderStyle.None,
                FullRowSelect = true,
                GridLines = false,
                MultiSelect = true,
                BackColor = BgSurface,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.None
            };
            listFiles.Columns.Add("Archivo", 554);
            listFiles.Columns.Add("Tamaño", 160);
            listFiles.Columns.Add("Estado", 185);
            listFiles.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete && !isSending)
                    RemoveSelectedItems();
            };
            Controls.Add(listFiles);

            // --- BOTÓN PRINCIPAL ENVIAR AHORA ---
            btnSend = new Button
            {
                Text = "Enviar ahora  →",
                Location = new Point(28, 540),
                Size = new Size(906, 40),
                BackColor = AccentPurple,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSend.FlatAppearance.BorderSize = 0;
            btnSend.Click += SendFiles;
            Controls.Add(btnSend);

            // --- PROGRESO Y ESTADO INFERIOR ---
            Controls.Add(new Label
            {
                Text = "PROGRESO",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextSecondary,
                Location = new Point(28, 592),
                Size = new Size(120, 20)
            });

            lblPercent = new Label
            {
                Text = "0%",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextSecondary,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(854, 592),
                Size = new Size(80, 20)
            };
            Controls.Add(lblPercent);

            progressBar = new ProgressBar
            {
                Location = new Point(28, 615),
                Size = new Size(906, 10),
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Continuous
            };
            Controls.Add(progressBar);

            lblStatus = new Label
            {
                Text = "Listo para enviar o recibir archivos.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextPrimary,
                Location = new Point(28, 632),
                Size = new Size(640, 20)
            };
            Controls.Add(lblStatus);

            lblRemaining = new Label
            {
                Text = "Tiempo restante: —",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextSecondary,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(714, 632),
                Size = new Size(220, 20)
            };
            Controls.Add(lblRemaining);

            lblReceiverFooter = new Label
            {
                Text = "Recepción automática activa.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextLink,
                Location = new Point(28, 653),
                Size = new Size(906, 20),
                Cursor = Cursors.Hand
            };
            lblReceiverFooter.Click += (s, e) =>
            {
                if (Directory.Exists(activeFolder))
                    System.Diagnostics.Process.Start("explorer.exe", activeFolder);
            };
            Controls.Add(lblReceiverFooter);
        }

        private void PaintDropZone(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(1, 1, pnlDropZone.Width - 3, pnlDropZone.Height - 3);
            Color dashColor = isDragOver ? AccentPurpleLight : BorderSubtle;

            using (GraphicsPath path = GetRoundedRect(rect, 10))
            using (Pen dashPen = new Pen(dashColor, 1.5f) { DashStyle = DashStyle.Dash })
            {
                g.DrawPath(dashPen, path);
            }

            // Ícono de subida centrado con margen superior limpio
            int cx = pnlDropZone.Width / 2;
            using (Pen iconPen = new Pen(AccentPurpleLight, 2f))
            {
                g.DrawLine(iconPen, cx, 18, cx, 34);
                g.DrawLine(iconPen, cx - 6, 24, cx, 18);
                g.DrawLine(iconPen, cx + 6, 24, cx, 18);
                g.DrawLine(iconPen, cx - 10, 38, cx + 10, 38);
            }

            using (Font fTitle = new Font("Segoe UI", 10F, FontStyle.Bold))
            using (Font fSub = new Font("Segoe UI", 8.5F))
            using (Brush bTitle = new SolidBrush(TextPrimary))
            using (Brush bSub = new SolidBrush(TextSecondary))
            {
                StringFormat sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString("Arrastra aquí tus archivos o haz clic para seleccionarlos",
                    fTitle, bTitle, new RectangleF(20, 48, pnlDropZone.Width - 40, 24), sf);
                g.DrawString("Puedes adjuntar varios archivos o carpetas a la vez. Pulsa Supr para quitar de la lista.",
                    fSub, bSub, new RectangleF(20, 74, pnlDropZone.Width - 40, 22), sf);
            }
        }

        private static Panel CreateRoundedPanel(Point loc, Size size, Color fill, Color border, int radius)
        {
            Panel panel = new Panel { Location = loc, Size = size, BackColor = fill };
            panel.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);
                using (GraphicsPath path = GetRoundedRect(rect, radius))
                using (Pen pen = new Pen(border, 1f))
                {
                    panel.Region = new Region(path);
                    e.Graphics.DrawPath(pen, path);
                }
            };
            return panel;
        }

        private static GraphicsPath GetRoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Label MakeTableHeaderLabel(string text, int x, int width)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = TextSecondary,
                Location = new Point(x, 6),
                Size = new Size(width, 18)
            };
        }

        private void OpenAdminOptions(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Selecciona la carpeta donde se guardarán los archivos recibidos:\nActual: " + activeFolder;
                dialog.SelectedPath = Directory.Exists(activeFolder)
                    ? activeFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                receiver.Stop();
                StartReceiver(dialog.SelectedPath);
            }
        }

        private void StartReceiver(string targetFolder)
        {
            try
            {
                receiver.Start(Port, targetFolder);
                activeFolder = targetFolder;
                UpdateReceiverFooter("Recepción automática activa  •  Carpeta: " + targetFolder);
            }
            catch (Exception ex)
            {
                UpdateReceiverFooter("Recepción inactiva: " + ex.Message);
            }
        }

        private void AddFilesOrFolders(object sender, EventArgs e)
        {
            if (isSending) return;

            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Selecciona los archivos que deseas enviar";
                dialog.Multiselect = true;
                if (dialog.ShowDialog() == DialogResult.OK)
                    AddPathsToQueue(dialog.FileNames);
            }
        }

        private void AddPathsToQueue(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                if (IsAlreadyQueued(path))
                    continue;

                if (File.Exists(path))
                {
                    FileInfo info = new FileInfo(path);
                    ListViewItem item = new ListViewItem("  " + info.Name);
                    item.SubItems.Add(FormatBytes(info.Length));
                    item.SubItems.Add("Pendiente");
                    item.Tag = path;
                    listFiles.Items.Add(item);
                }
                else if (Directory.Exists(path))
                {
                    string folderName = new DirectoryInfo(path).Name;
                    long dirSize = GetDirectorySize(path);
                    ListViewItem item = new ListViewItem("  📁 " + folderName);
                    item.SubItems.Add(FormatBytes(dirSize));
                    item.SubItems.Add("Pendiente");
                    item.Tag = path;
                    listFiles.Items.Add(item);
                }
            }
        }

        private bool IsAlreadyQueued(string path)
        {
            foreach (ListViewItem existing in listFiles.Items)
            {
                if (string.Equals(existing.Tag as string, path, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void RemoveSelectedItems()
        {
            while (listFiles.SelectedItems.Count > 0)
                listFiles.Items.Remove(listFiles.SelectedItems[0]);
        }

        private async void SendFiles(object sender, EventArgs e)
        {
            if (!IPAddress.TryParse(txtIP.Text.Trim(), out IPAddress address) ||
                address.AddressFamily != AddressFamily.InterNetwork)
            {
                MessageBox.Show("Escribe una dirección IPv4 válida en DESTINO.", "ONKOS File Transfer 2.0",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtIP.Focus();
                return;
            }

            if (listFiles.Items.Count == 0)
            {
                MessageBox.Show("Agrega al menos un archivo o carpeta para enviar.", "ONKOS File Transfer 2.0",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<ListViewItem> items = new List<ListViewItem>();
            long totalBytes = 0;
            foreach (ListViewItem item in listFiles.Items)
            {
                items.Add(item);
                string p = item.Tag as string;
                if (File.Exists(p)) totalBytes += new FileInfo(p).Length;
                else if (Directory.Exists(p)) totalBytes += GetDirectorySize(p);
            }

            long totalSent = 0;
            int sentCount = 0;
            List<string> failures = new List<string>();

            isSending = true;
            btnSend.Enabled = false;
            progressBar.Value = 0;
            lblPercent.Text = "0%";

            foreach (ListViewItem item in items)
            {
                string originalPath = item.Tag as string;
                string displayName = Path.GetFileName(originalPath);
                string tempPackage = null;

                try
                {
                    string sendPath = originalPath;
                    if (Directory.Exists(originalPath))
                    {
                        item.SubItems[2].Text = "Empaquetando...";
                        tempPackage = Path.Combine(Path.GetTempPath(),
                            displayName + "_" + Guid.NewGuid().ToString("N") + ".onkosfolder");
                        await Task.Run(() => ZipFile.CreateFromDirectory(originalPath, tempPackage));
                        sendPath = tempPackage;
                    }
                    else if (!File.Exists(originalPath))
                    {
                        throw new FileNotFoundException("El elemento ya no existe.");
                    }

                    item.SubItems[2].Text = "Enviando...";
                    long fileSent = 0;

                    await FileSender.SendFileAsync(address.ToString(), Port, sendPath,
                        (sent, size) =>
                        {
                            long delta = sent - fileSent;
                            fileSent = sent;
                            totalSent += delta;
                            UpdateProgress(totalSent, totalBytes, displayName, sent, size);
                        });

                    sentCount++;
                    item.SubItems[2].Text = "Enviado";
                }
                catch (Exception ex)
                {
                    item.SubItems[2].Text = "Error";
                    failures.Add(displayName + ": " + ex.Message);
                }
                finally
                {
                    if (tempPackage != null && File.Exists(tempPackage))
                    {
                        try { File.Delete(tempPackage); } catch { }
                    }
                }
            }

            isSending = false;
            btnSend.Enabled = true;
            progressBar.Value = failures.Count == 0 ? 100 : progressBar.Value;
            lblPercent.Text = progressBar.Value + "%";
            lblRemaining.Text = "Tiempo restante: —";

            string summary = "Transferencia finalizada.\r\n\r\nCompletados: " + sentCount + " de " + items.Count + ".";
            if (failures.Count > 0)
                summary += "\r\n\r\nErrores:\r\n" + string.Join("\r\n", failures.ToArray());

            lblStatus.Text = "Proceso terminado: " + sentCount + " de " + items.Count + " enviados.";
            MessageBox.Show(summary, "ONKOS File Transfer 2.0", MessageBoxButtons.OK,
                failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            // Limpieza automática de lista tras el resumen (según ARQUITECTURA_V2.md)
            if (failures.Count == 0)
            {
                listFiles.Items.Clear();
                progressBar.Value = 0;
                lblPercent.Text = "0%";
                lblStatus.Text = "Listo para enviar o recibir archivos.";
            }
        }

        private void UpdateProgress(long sent, long total, string name, long fileSent, long fileSize)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<long, long, string, long, long>(UpdateProgress),
                    sent, total, name, fileSent, fileSize);
                return;
            }

            int percent = total <= 0 ? 100 : (int)Math.Min(100, sent * 100.0 / total);
            progressBar.Value = Math.Max(0, Math.Min(100, percent));
            lblPercent.Text = percent + "%";
            lblStatus.Text = "Enviando " + name + " (" + FormatBytes(fileSent) + " / " + FormatBytes(fileSize) + ")";
        }

        private void UpdateReceiverFooter(string message)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateReceiverFooter), message);
                return;
            }
            lblReceiverFooter.Text = message;
        }

        private static long GetDirectorySize(string folderPath)
        {
            long total = 0;
            try
            {
                foreach (string file in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
                    total += new FileInfo(file).Length;
            }
            catch { }
            return total;
        }

        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return value.ToString("0.##") + " " + units[unit];
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            receiver.Stop();
            base.OnFormClosing(e);
        }
    }
}
