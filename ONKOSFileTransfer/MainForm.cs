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

        private static readonly Color BgWindow = Color.FromArgb(18, 19, 30);
        private static readonly Color BgSurface = Color.FromArgb(28, 30, 46);
        private static readonly Color BgInput = Color.FromArgb(32, 34, 52);
        private static readonly Color BgHeaderTable = Color.FromArgb(36, 39, 58);
        private static readonly Color BorderSubtle = Color.FromArgb(58, 62, 88);
        private static readonly Color AccentPurple = Color.FromArgb(124, 77, 224);
        private static readonly Color AccentPurpleHover = Color.FromArgb(140, 94, 240);
        private static readonly Color AccentPurpleLight = Color.FromArgb(177, 139, 255);
        private static readonly Color StatusGreen = Color.FromArgb(74, 222, 128);
        private static readonly Color TextPrimary = Color.FromArgb(245, 246, 250);
        private static readonly Color TextSecondary = Color.FromArgb(156, 163, 184);
        private static readonly Color TextLink = Color.FromArgb(96, 165, 250);

        private readonly FileReceiver receiver;
        private TextBox txtIP;
        private Panel pnlDestBox;
        private Panel pnlDropZone;
        private ListView listFiles;
        private Button btnSend;
        private Button btnAdmin;
        private Panel pnlProgressBar;
        private Label lblPercent;
        private Label lblStatus;
        private Label lblRemaining;
        private Label lblReceiverFooter;
        private string activeFolder;
        private bool isSending;
        private bool isDragOver;
        private int progressPercent;

        public MainForm()
        {
            DoubleBuffered = true;
            activeFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ONKOS_Received");

            receiver = new FileReceiver();
            receiver.FileReceived += path =>
                UpdateReceiverFooter("Recibido: " + Path.GetFileName(path) + "  •  Clic para abrir carpeta");
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
                Location = new Point(26, 18),
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
                Location = new Point(58, 15),
                Size = new Size(140, 30)
            });

            Controls.Add(new Label
            {
                Text = "FILE TRANSFER 2.0  •  ENVÍA Y RECIBE EN TU RED",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextSecondary,
                Location = new Point(28, 47),
                Size = new Size(380, 18)
            });

            Panel pnlIpPill = CreateRoundedPanel(new Point(475, 20), new Size(305, 36), BgSurface, BorderSubtle, 18);
            pnlIpPill.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush dot = new SolidBrush(StatusGreen))
                    e.Graphics.FillEllipse(dot, 126, 14, 7, 7);
            };
            pnlIpPill.Controls.Add(new Label
            {
                Text = "IP " + NetworkManager.GetLocalIPv4(),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextPrimary,
                Location = new Point(16, 8),
                Size = new Size(106, 20)
            });
            pnlIpPill.Controls.Add(new Label
            {
                Text = "Recepción automática",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = StatusGreen,
                Location = new Point(139, 8),
                Size = new Size(154, 20)
            });
            Controls.Add(pnlIpPill);

            btnAdmin = MakeStyledButton("⚙  Administración", new Point(792, 20), new Size(142, 36),
                BgSurface, BgHeaderTable, BorderSubtle, 8, 8.5F);
            btnAdmin.Click += OpenAdminOptions;
            Controls.Add(btnAdmin);

            // --- TÍTULO DE SECCIÓN ---
            Controls.Add(new Label
            {
                Text = "Centro de transferencias",
                Font = new Font("Segoe UI", 16.5F, FontStyle.Bold),
                ForeColor = TextPrimary,
                Location = new Point(28, 84),
                Size = new Size(450, 30)
            });

            Controls.Add(new Label
            {
                Text = "Envía archivos o carpetas a otros computadores y recibe transferencias en esta estación.",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextSecondary,
                Location = new Point(28, 115),
                Size = new Size(680, 20)
            });

            // --- CAMPO DESTINO ---
            Controls.Add(new Label
            {
                Text = "DESTINO",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextSecondary,
                Location = new Point(28, 152),
                Size = new Size(76, 20)
            });

            pnlDestBox = new Panel
            {
                Location = new Point(112, 145),
                Size = new Size(822, 34),
                BackColor = BgInput
            };
            pnlDestBox.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, pnlDestBox.Width - 1, pnlDestBox.Height - 1);
                Color borderColor = (txtIP != null && txtIP.Focused) ? AccentPurpleLight : BorderSubtle;
                using (GraphicsPath path = GetRoundedRect(rect, 8))
                using (Pen pen = new Pen(borderColor, 1.2f))
                {
                    pnlDestBox.Region = new Region(path);
                    e.Graphics.DrawPath(pen, path);
                }
            };

            txtIP = new TextBox
            {
                Location = new Point(14, 7),
                Size = new Size(794, 20),
                Font = new Font("Segoe UI", 10F),
                BackColor = BgInput,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.None
            };
            txtIP.GotFocus += (s, e) => pnlDestBox.Invalidate();
            txtIP.LostFocus += (s, e) => pnlDestBox.Invalidate();
            pnlDestBox.Controls.Add(txtIP);
            Controls.Add(pnlDestBox);

            // --- ZONA DRAG & DROP ---
            Controls.Add(new Label
            {
                Text = "ARCHIVOS Y CARPETAS  •  ARRASTRA AQUÍ PARA AÑADIR",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextSecondary,
                Location = new Point(28, 192),
                Size = new Size(500, 18)
            });

            pnlDropZone = new Panel
            {
                Location = new Point(28, 215),
                Size = new Size(906, 108),
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

            // --- TABLA DE ARCHIVOS ---
            Panel pnlTableHeader = new Panel
            {
                Location = new Point(28, 336),
                Size = new Size(906, 28),
                BackColor = BgHeaderTable
            };
            pnlTableHeader.Controls.Add(MakeTableHeaderLabel("ARCHIVO", 16, 540));
            pnlTableHeader.Controls.Add(MakeTableHeaderLabel("TAMAÑO", 560, 150));
            pnlTableHeader.Controls.Add(MakeTableHeaderLabel("ESTADO", 720, 170));
            Controls.Add(pnlTableHeader);

            listFiles = new ListView
            {
                Location = new Point(28, 364),
                Size = new Size(906, 116),
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
            listFiles.Paint += (s, e) =>
            {
                if (listFiles.Items.Count == 0)
                {
                    using (Font f = new Font("Segoe UI", 9F))
                    using (Brush b = new SolidBrush(TextSecondary))
                    {
                        StringFormat sf = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        };
                        e.Graphics.DrawString("Sin archivos ni carpetas en cola.",
                            f, b, new RectangleF(0, 0, listFiles.Width, listFiles.Height), sf);
                    }
                }
            };
            listFiles.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete && !isSending)
                {
                    RemoveSelectedItems();
                    listFiles.Invalidate();
                }
            };
            Controls.Add(listFiles);

            // --- BOTÓN PRINCIPAL ENVIAR AHORA ---
            btnSend = MakeStyledButton("Enviar ahora  →", new Point(28, 494), new Size(906, 38),
                AccentPurple, AccentPurpleHover, AccentPurple, 8, 9.5F);
            btnSend.Click += SendFiles;
            Controls.Add(btnSend);

            // --- PROGRESO Y ESTADO INFERIOR ---
            Controls.Add(new Label
            {
                Text = "PROGRESO",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextSecondary,
                Location = new Point(28, 544),
                Size = new Size(120, 18)
            });

            lblPercent = new Label
            {
                Text = "0%",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextSecondary,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(854, 544),
                Size = new Size(80, 18)
            };
            Controls.Add(lblPercent);

            pnlProgressBar = new Panel
            {
                Location = new Point(28, 566),
                Size = new Size(906, 10),
                BackColor = BgInput
            };
            pnlProgressBar.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle track = new Rectangle(0, 0, pnlProgressBar.Width - 1, pnlProgressBar.Height - 1);
                using (GraphicsPath trackPath = GetRoundedRect(track, 5))
                using (SolidBrush trackBrush = new SolidBrush(BgInput))
                {
                    pnlProgressBar.Region = new Region(trackPath);
                    e.Graphics.FillPath(trackBrush, trackPath);

                    int clamped = Math.Max(0, Math.Min(100, progressPercent));
                    int fillWidth = (int)Math.Round(pnlProgressBar.Width * (clamped / 100.0));
                    if (fillWidth > 2)
                    {
                        Rectangle fillRect = new Rectangle(0, 0, fillWidth, pnlProgressBar.Height);
                        using (GraphicsPath fillPath = GetRoundedRect(fillRect, 5))
                        using (SolidBrush fillBrush = new SolidBrush(AccentPurpleLight))
                        {
                            e.Graphics.FillPath(fillBrush, fillPath);
                        }
                    }
                }
            };
            Controls.Add(pnlProgressBar);

            lblStatus = new Label
            {
                Text = "Listo para enviar o recibir archivos.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextPrimary,
                Location = new Point(28, 586),
                Size = new Size(640, 20)
            };
            Controls.Add(lblStatus);

            lblRemaining = new Label
            {
                Text = "Tiempo restante: —",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextSecondary,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(714, 586),
                Size = new Size(220, 20)
            };
            Controls.Add(lblRemaining);

            lblReceiverFooter = new Label
            {
                Text = "Recepción automática activa.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextLink,
                Location = new Point(28, 616),
                Size = new Size(906, 22),
                Cursor = Cursors.Hand
            };
            lblReceiverFooter.Click += (s, e) =>
            {
                if (Directory.Exists(activeFolder))
                    System.Diagnostics.Process.Start("explorer.exe", activeFolder);
            };
            Controls.Add(lblReceiverFooter);
        }

        private static Button MakeStyledButton(string text, Point loc, Size size,
            Color baseColor, Color hoverColor, Color borderColor, int radius, float fontSize)
        {
            Button btn = new Button
            {
                Text = text,
                Location = loc,
                Size = size,
                BackColor = baseColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", fontSize, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.MouseEnter += (s, e) => btn.BackColor = hoverColor;
            btn.MouseLeave += (s, e) => btn.BackColor = baseColor;
            btn.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, btn.Width - 1, btn.Height - 1);
                using (GraphicsPath path = GetRoundedRect(rect, radius))
                using (Pen pen = new Pen(borderColor, 1f))
                {
                    btn.Region = new Region(path);
                    e.Graphics.DrawPath(pen, path);
                }
            };
            return btn;
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

            int cx = pnlDropZone.Width / 2;
            using (Pen iconPen = new Pen(AccentPurpleLight, 2f))
            {
                g.DrawLine(iconPen, cx, 16, cx, 32);
                g.DrawLine(iconPen, cx - 6, 22, cx, 16);
                g.DrawLine(iconPen, cx + 6, 22, cx, 16);
                g.DrawLine(iconPen, cx - 10, 36, cx + 10, 36);
            }

            using (Font fTitle = new Font("Segoe UI", 10F, FontStyle.Bold))
            using (Font fSub = new Font("Segoe UI", 8.5F))
            using (Brush bTitle = new SolidBrush(TextPrimary))
            using (Brush bSub = new SolidBrush(TextSecondary))
            {
                StringFormat sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString("Arrastra aquí tus archivos o haz clic para seleccionarlos",
                    fTitle, bTitle, new RectangleF(20, 45, pnlDropZone.Width - 40, 22), sf);
                g.DrawString("Puedes adjuntar varios archivos o carpetas a la vez. Pulsa Supr para quitar de la lista.",
                    fSub, bSub, new RectangleF(20, 70, pnlDropZone.Width - 40, 20), sf);
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
            int r = Math.Max(1, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
            int d = r * 2;
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
                Size = new Size(width, 16)
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
                UpdateReceiverFooter("Recepción automática activa  •  Carpeta: " + targetFolder + "  •  Clic para abrir");
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
            listFiles.Invalidate();
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

        private void SetProgress(int value)
        {
            progressPercent = Math.Max(0, Math.Min(100, value));
            lblPercent.Text = progressPercent + "%";
            pnlProgressBar.Invalidate();
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
            SetProgress(0);

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

            int finalPercent = totalBytes <= 0 ? 100 : (int)Math.Min(100, totalSent * 100.0 / totalBytes);
            SetProgress(failures.Count == 0 ? 100 : finalPercent);
            lblRemaining.Text = "Tiempo restante: —";

            string summary = "Transferencia finalizada.\r\n\r\nCompletados: " + sentCount + " de " + items.Count + ".";
            if (failures.Count > 0)
                summary += "\r\n\r\nErrores:\r\n" + string.Join("\r\n", failures.ToArray());

            lblStatus.Text = "Proceso terminado: " + sentCount + " de " + items.Count + " enviados.";
            MessageBox.Show(summary, "ONKOS File Transfer 2.0", MessageBoxButtons.OK,
                failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            if (failures.Count == 0)
            {
                listFiles.Items.Clear();
                listFiles.Invalidate();
                SetProgress(0);
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
            SetProgress(percent);
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
