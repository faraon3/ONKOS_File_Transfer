using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        // Paleta moderna oscura con acentos violeta
        private static readonly Color BgWindow = Color.FromArgb(15, 16, 26);
        private static readonly Color BgCard = Color.FromArgb(24, 26, 40);
        private static readonly Color BgSurface = Color.FromArgb(30, 33, 51);
        private static readonly Color BgInput = Color.FromArgb(20, 22, 35);
        private static readonly Color BgProgressTrack = Color.FromArgb(42, 46, 70);
        private static readonly Color BgHeaderTable = Color.FromArgb(35, 39, 60);
        private static readonly Color BorderSubtle = Color.FromArgb(54, 59, 86);
        private static readonly Color BorderProgress = Color.FromArgb(76, 82, 118);
        private static readonly Color AccentPurple = Color.FromArgb(124, 77, 224);
        private static readonly Color AccentPurpleHover = Color.FromArgb(145, 98, 245);
        private static readonly Color AccentPurpleLight = Color.FromArgb(184, 148, 255);
        private static readonly Color StatusGreen = Color.FromArgb(74, 222, 128);
        private static readonly Color StatusAmber = Color.FromArgb(251, 191, 36);
        private static readonly Color StatusRed = Color.FromArgb(248, 113, 113);
        private static readonly Color TextPrimary = Color.FromArgb(246, 247, 252);
        private static readonly Color TextSecondary = Color.FromArgb(152, 160, 182);
        private static readonly Color TextLink = Color.FromArgb(106, 174, 255);

        private readonly FileReceiver receiver;
        private readonly Timer animTimer;
        private readonly Stopwatch sendStopwatch = new Stopwatch();

        private TextBox txtIP;
        private Panel pnlDestCard;
        private Panel pnlDropZone;
        private ListView listFiles;
        private ImageList fileIcons;
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

        // Estados de animación
        private double targetProgress;
        private double animatedProgress;
        private float shimmerOffset;
        private float pulsePhase;
        private float dropFlashAlpha;
        private int rowFlashSteps;
        private bool adminHover;
        private bool adminPressed;
        private bool sendHover;
        private bool sendPressed;

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

            animTimer = new Timer { Interval = 25 };
            animTimer.Tick += OnAnimationTick;

            BuildInterface();
            animTimer.Start();
            StartReceiver(activeFolder);
        }

        private void OnAnimationTick(object sender, EventArgs e)
        {
            bool needsProgressRedraw = false;
            bool needsDropRedraw = false;

            pulsePhase += 0.08f;
            if (pulsePhase > (float)(Math.PI * 2))
                pulsePhase -= (float)(Math.PI * 2);

            double diff = targetProgress - animatedProgress;
            if (Math.Abs(diff) > 0.15)
            {
                animatedProgress += diff * 0.18;
                needsProgressRedraw = true;
            }
            else if (Math.Abs(animatedProgress - targetProgress) > 0.001)
            {
                animatedProgress = targetProgress;
                needsProgressRedraw = true;
            }

            if (isSending)
            {
                shimmerOffset += 0.035f;
                if (shimmerOffset > 1.3f)
                    shimmerOffset = -0.3f;
                needsProgressRedraw = true;
            }

            int shownPercent = (int)Math.Round(animatedProgress);
            string pctText = shownPercent + "%";
            if (lblPercent.Text != pctText)
                lblPercent.Text = pctText;

            if (needsProgressRedraw)
                pnlProgressBar.Invalidate();

            if (dropFlashAlpha > 0.02f)
            {
                dropFlashAlpha *= 0.86f;
                needsDropRedraw = true;
            }
            else
            {
                dropFlashAlpha = 0f;
            }

            if (isDragOver || needsDropRedraw)
                pnlDropZone.Invalidate();

            if (rowFlashSteps > 0)
            {
                rowFlashSteps--;
                if (rowFlashSteps == 0)
                {
                    foreach (ListViewItem item in listFiles.Items)
                        item.BackColor = BgSurface;
                }
            }
        }

        private void BuildInterface()
        {
            Text = "ONKOS File Transfer 2.0";
            ClientSize = new Size(960, 690);
            MinimumSize = new Size(960, 690);
            MaximumSize = new Size(960, 690);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgWindow;
            ForeColor = TextPrimary;
            Font = new Font("Segoe UI", 9.5F);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            // --- ENCABEZADO ---
            Panel pnlLogo = new Panel
            {
                Location = new Point(26, 18),
                Size = new Size(30, 30),
                BackColor = Color.Transparent
            };
            pnlLogo.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Point[] outer = { new Point(15, 3), new Point(27, 15), new Point(15, 27), new Point(3, 15) };
                Point[] inner = { new Point(15, 8), new Point(22, 15), new Point(15, 22), new Point(8, 15) };
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
                Font = new Font("Segoe UI", 16.5F, FontStyle.Bold),
                ForeColor = AccentPurpleLight,
                Location = new Point(60, 15),
                Size = new Size(140, 30)
            });

            Controls.Add(new Label
            {
                Text = "FILE TRANSFER 2.0  •  ENVÍA Y RECIBE EN TU RED LOCAL",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextSecondary,
                Location = new Point(28, 48),
                Size = new Size(400, 18)
            });

            Panel pnlIpPill = CreateRoundedPanel(new Point(470, 19), new Size(310, 38), BgCard, BorderSubtle, 19);
            pnlIpPill.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float glow = 0.55f + 0.45f * (float)Math.Sin(pulsePhase);
                int alpha = (int)(70 + 110 * glow);
                using (SolidBrush halo = new SolidBrush(Color.FromArgb(alpha, StatusGreen)))
                    e.Graphics.FillEllipse(halo, 125, 12, 13, 13);
                using (SolidBrush dot = new SolidBrush(StatusGreen))
                    e.Graphics.FillEllipse(dot, 128, 15, 7, 7);
            };
            pnlIpPill.Controls.Add(new Label
            {
                Text = "IP " + NetworkManager.GetLocalIPv4(),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextPrimary,
                Location = new Point(16, 9),
                Size = new Size(108, 20)
            });
            pnlIpPill.Controls.Add(new Label
            {
                Text = "Recepción automática",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = StatusGreen,
                Location = new Point(142, 9),
                Size = new Size(158, 20)
            });
            Controls.Add(pnlIpPill);

            btnAdmin = MakeAnimatedButton(
                "⚙  Administración",
                new Point(790, 19),
                new Size(144, 38),
                BgCard,
                BgSurface,
                BorderSubtle,
                9,
                8.5F,
                () => adminHover,
                v => adminHover = v,
                () => adminPressed,
                v => adminPressed = v);
            btnAdmin.Click += OpenAdminOptions;
            Controls.Add(btnAdmin);

            // --- TÍTULO PRINCIPAL ---
            Controls.Add(new Label
            {
                Text = "Centro de transferencias",
                Font = new Font("Segoe UI", 17F, FontStyle.Bold),
                ForeColor = TextPrimary,
                Location = new Point(28, 82),
                Size = new Size(460, 32)
            });

            Controls.Add(new Label
            {
                Text = "Envía archivos o carpetas a otros computadores y recibe transferencias en esta estación.",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextSecondary,
                Location = new Point(28, 114),
                Size = new Size(700, 20)
            });

            // --- TARJETA DESTINO ---
            pnlDestCard = new Panel
            {
                Location = new Point(28, 146),
                Size = new Size(906, 54),
                BackColor = BgCard
            };
            pnlDestCard.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, pnlDestCard.Width - 1, pnlDestCard.Height - 1);
                Color border = (txtIP != null && txtIP.Focused) ? AccentPurpleLight : BorderSubtle;
                using (GraphicsPath path = GetRoundedRect(rect, 10))
                using (SolidBrush fill = new SolidBrush(BgCard))
                using (Pen pen = new Pen(border, (txtIP != null && txtIP.Focused) ? 1.5f : 1f))
                {
                    pnlDestCard.Region = new Region(path);
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(pen, path);
                }
            };

            pnlDestCard.Controls.Add(new Label
            {
                Text = "DESTINO",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextSecondary,
                Location = new Point(16, 17),
                Size = new Size(80, 20)
            });

            Panel pnlInputWrap = CreateRoundedPanel(new Point(102, 10), new Size(788, 34), BgInput, BorderSubtle, 7);
            txtIP = new TextBox
            {
                Location = new Point(12, 7),
                Size = new Size(764, 20),
                Font = new Font("Segoe UI", 10F),
                BackColor = BgInput,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.None
            };
            txtIP.GotFocus += (s, e) => pnlDestCard.Invalidate();
            txtIP.LostFocus += (s, e) => pnlDestCard.Invalidate();
            pnlInputWrap.Controls.Add(txtIP);
            pnlDestCard.Controls.Add(pnlInputWrap);
            Controls.Add(pnlDestCard);

            // --- ZONA DRAG & DROP ANIMADA ---
            Controls.Add(new Label
            {
                Text = "ARCHIVOS Y CARPETAS  •  ARRASTRA AQUÍ PARA AÑADIR",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextSecondary,
                Location = new Point(28, 212),
                Size = new Size(500, 18)
            });

            pnlDropZone = new Panel
            {
                Location = new Point(28, 234),
                Size = new Size(906, 106),
                BackColor = BgCard,
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
                dropFlashAlpha = 0.85f;
                pnlDropZone.Invalidate();
                if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
                    AddPathsToQueue(dropped);
            };
            Controls.Add(pnlDropZone);

            // --- TABLA DE ARCHIVOS (COLUMNAS 554, 160, 185) ---
            Panel pnlTableHeader = new Panel
            {
                Location = new Point(28, 352),
                Size = new Size(906, 28),
                BackColor = BgHeaderTable
            };
            pnlTableHeader.Controls.Add(MakeTableHeaderLabel("ARCHIVO", 26, 524));
            pnlTableHeader.Controls.Add(MakeTableHeaderLabel("TAMAÑO", 554, 160));
            pnlTableHeader.Controls.Add(MakeTableHeaderLabel("ESTADO", 714, 185));
            Controls.Add(pnlTableHeader);

            fileIcons = new ImageList
            {
                ImageSize = new Size(20, 20),
                ColorDepth = ColorDepth.Depth32Bit
            };

            listFiles = new ListView
            {
                Location = new Point(28, 380),
                Size = new Size(906, 110),
                View = View.Details,
                HeaderStyle = ColumnHeaderStyle.None,
                SmallImageList = fileIcons,
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
                        e.Graphics.DrawString("Sin archivos ni carpetas en cola — haz clic arriba o arrastra elementos.",
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

            // --- BOTÓN ENVIAR ---
            btnSend = MakeAnimatedButton(
                "Enviar ahora  →",
                new Point(28, 504),
                new Size(906, 40),
                AccentPurple,
                AccentPurpleHover,
                AccentPurpleLight,
                9,
                10F,
                () => sendHover,
                v => sendHover = v,
                () => sendPressed,
                v => sendPressed = v);
            btnSend.Click += SendFiles;
            Controls.Add(btnSend);

            // --- PROGRESO ANIMADO CON MAYOR CONTRASTE ---
            Controls.Add(new Label
            {
                Text = "PROGRESO",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextSecondary,
                Location = new Point(28, 556),
                Size = new Size(120, 18)
            });

            lblPercent = new Label
            {
                Text = "0%",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = AccentPurpleLight,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(854, 556),
                Size = new Size(80, 18)
            };
            Controls.Add(lblPercent);

            pnlProgressBar = new Panel
            {
                Location = new Point(28, 578),
                Size = new Size(906, 13),
                BackColor = BgProgressTrack
            };
            pnlProgressBar.Paint += PaintAnimatedProgressBar;
            Controls.Add(pnlProgressBar);

            // --- ESTADOS INFERIORES ---
            lblStatus = new Label
            {
                Text = "Listo para enviar o recibir archivos.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextPrimary,
                Location = new Point(28, 599),
                Size = new Size(650, 20)
            };
            Controls.Add(lblStatus);

            lblRemaining = new Label
            {
                Text = "Tiempo restante: —",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextSecondary,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(684, 599),
                Size = new Size(250, 20)
            };
            Controls.Add(lblRemaining);

            lblReceiverFooter = new Label
            {
                Text = "Recepción automática activa.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextLink,
                Location = new Point(28, 628),
                Size = new Size(906, 22),
                Cursor = Cursors.Hand
            };
            lblReceiverFooter.Click += (s, e) =>
            {
                if (Directory.Exists(activeFolder))
                    Process.Start("explorer.exe", activeFolder);
            };
            Controls.Add(lblReceiverFooter);
        }

        private string GetOrAddIconKey(string path, bool isDirectory)
        {
            if (fileIcons == null)
                return null;

            string key = isDirectory
                ? "__folder__"
                : (Path.GetExtension(path) ?? "__file__").ToLowerInvariant();

            if (string.IsNullOrEmpty(key))
                key = "__file__";

            if (fileIcons.Images.ContainsKey(key))
                return key;

            try
            {
                if (isDirectory)
                {
                    using (Bitmap bmp = new Bitmap(20, 20))
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        using (GraphicsPath tab = GetRoundedRect(new Rectangle(2, 3, 9, 6), 2))
                        using (SolidBrush bTab = new SolidBrush(AccentPurple))
                            g.FillPath(bTab, tab);

                        using (GraphicsPath body = GetRoundedRect(new Rectangle(2, 6, 16, 11), 2))
                        using (SolidBrush bBody = new SolidBrush(AccentPurpleLight))
                            g.FillPath(bBody, body);

                        fileIcons.Images.Add(key, (Image)bmp.Clone());
                        return key;
                    }
                }

                using (Icon sysIcon = Icon.ExtractAssociatedIcon(path))
                {
                    if (sysIcon != null)
                    {
                        using (Bitmap icoBmp = sysIcon.ToBitmap())
                        using (Bitmap resized = new Bitmap(20, 20))
                        using (Graphics g = Graphics.FromImage(resized))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.DrawImage(icoBmp, 0, 0, 20, 20);
                            fileIcons.Images.Add(key, (Image)resized.Clone());
                            return key;
                        }
                    }
                }
            }
            catch
            {
                // Continúa sin ícono si no se puede extraer.
            }

            return null;
        }

        private void PaintAnimatedProgressBar(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle track = new Rectangle(0, 0, pnlProgressBar.Width - 1, pnlProgressBar.Height - 1);
            using (GraphicsPath trackPath = GetRoundedRect(track, 6))
            using (SolidBrush trackBrush = new SolidBrush(BgProgressTrack))
            using (Pen trackPen = new Pen(BorderProgress, 1.2f))
            {
                pnlProgressBar.Region = new Region(trackPath);
                g.FillPath(trackBrush, trackPath);
                g.DrawPath(trackPen, trackPath);

                int fillWidth = (int)Math.Round(pnlProgressBar.Width * (Math.Max(0, Math.Min(100, animatedProgress)) / 100.0));
                if (fillWidth > 4)
                {
                    Rectangle fillRect = new Rectangle(0, 0, fillWidth, pnlProgressBar.Height);
                    using (GraphicsPath fillPath = GetRoundedRect(fillRect, 6))
                    using (LinearGradientBrush grad = new LinearGradientBrush(
                        new Point(0, 0),
                        new Point(Math.Max(fillWidth, 10), 0),
                        AccentPurple,
                        AccentPurpleLight))
                    {
                        g.FillPath(grad, fillPath);

                        if (isSending)
                        {
                            int bandCenter = (int)(fillWidth * shimmerOffset);
                            Rectangle clip = new Rectangle(0, 0, fillWidth, pnlProgressBar.Height);
                            g.SetClip(clip);
                            using (SolidBrush sheen = new SolidBrush(Color.FromArgb(65, Color.White)))
                                g.FillRectangle(sheen, bandCenter - 28, 0, 56, pnlProgressBar.Height);
                            g.ResetClip();
                        }
                    }
                }
            }
        }

        private void PaintDropZone(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(1, 1, pnlDropZone.Width - 3, pnlDropZone.Height - 3);
            float pulse = 0.5f + 0.5f * (float)Math.Sin(pulsePhase * 1.4f);
            Color borderCol = isDragOver ? AccentPurpleLight : BorderSubtle;

            using (GraphicsPath path = GetRoundedRect(rect, 10))
            {
                if (dropFlashAlpha > 0.01f)
                {
                    int a = (int)(dropFlashAlpha * 55);
                    using (SolidBrush flash = new SolidBrush(Color.FromArgb(a, AccentPurpleLight)))
                        g.FillPath(flash, path);
                }
                else if (isDragOver)
                {
                    using (SolidBrush hoverFill = new SolidBrush(Color.FromArgb(24, AccentPurpleLight)))
                        g.FillPath(hoverFill, path);
                }

                using (Pen dashPen = new Pen(borderCol, isDragOver ? 2f : 1.5f) { DashStyle = DashStyle.Dash })
                    g.DrawPath(dashPen, path);
            }

            int cx = pnlDropZone.Width / 2;
            int lift = isDragOver ? (int)(-3 * pulse) : 0;
            using (Pen iconPen = new Pen(AccentPurpleLight, 2f))
            {
                g.DrawLine(iconPen, cx, 15 + lift, cx, 31 + lift);
                g.DrawLine(iconPen, cx - 6, 21 + lift, cx, 15 + lift);
                g.DrawLine(iconPen, cx + 6, 21 + lift, cx, 15 + lift);
                g.DrawLine(iconPen, cx - 10, 35 + lift, cx + 10, 35 + lift);
            }

            using (Font fTitle = new Font("Segoe UI", 10F, FontStyle.Bold))
            using (Font fSub = new Font("Segoe UI", 8.5F))
            using (Brush bTitle = new SolidBrush(TextPrimary))
            using (Brush bSub = new SolidBrush(TextSecondary))
            {
                StringFormat sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString(isDragOver ? "¡Suelta aquí para añadir a la cola!" : "Arrastra aquí tus archivos o haz clic para seleccionarlos",
                    fTitle, bTitle, new RectangleF(20, 44, pnlDropZone.Width - 40, 22), sf);
                g.DrawString("Puedes adjuntar varios archivos o carpetas a la vez. Pulsa Supr para quitar seleccionados.",
                    fSub, bSub, new RectangleF(20, 69, pnlDropZone.Width - 40, 20), sf);
            }
        }

        private static Button MakeAnimatedButton(
            string text,
            Point loc,
            Size size,
            Color normalColor,
            Color hoverColor,
            Color borderColor,
            int radius,
            float fontSize,
            Func<bool> getHover,
            Action<bool> setHover,
            Func<bool> getPressed,
            Action<bool> setPressed)
        {
            Button btn = new Button
            {
                Text = text,
                Location = loc,
                Size = size,
                BackColor = normalColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", fontSize, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;

            btn.MouseEnter += (s, e) => { setHover(true); btn.Invalidate(); };
            btn.MouseLeave += (s, e) => { setHover(false); setPressed(false); btn.Invalidate(); };
            btn.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { setPressed(true); btn.Invalidate(); } };
            btn.MouseUp += (s, e) => { setPressed(false); btn.Invalidate(); };

            btn.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                bool hov = getHover();
                bool pr = getPressed();

                Color bg = pr ? AccentPurple : (hov ? hoverColor : normalColor);
                int yOffset = pr ? 1 : 0;

                Rectangle rect = new Rectangle(0, yOffset, btn.Width - 1, btn.Height - 2);
                using (GraphicsPath path = GetRoundedRect(rect, radius))
                using (SolidBrush brush = new SolidBrush(bg))
                using (Pen pen = new Pen(hov ? borderColor : BorderSubtle, 1f))
                {
                    btn.Region = new Region(path);
                    g.FillPath(brush, path);
                    g.DrawPath(pen, path);
                }

                TextRenderer.DrawText(
                    g,
                    btn.Text,
                    btn.Font,
                    new Rectangle(0, yOffset, btn.Width, btn.Height - 1),
                    btn.ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };

            return btn;
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
                {
                    dropFlashAlpha = 0.75f;
                    AddPathsToQueue(dialog.FileNames);
                }
            }
        }

        private void AddPathsToQueue(IEnumerable<string> paths)
        {
            int addedCount = 0;

            foreach (string path in paths)
            {
                if (IsAlreadyQueued(path))
                    continue;

                if (File.Exists(path))
                {
                    FileInfo info = new FileInfo(path);
                    string iconKey = GetOrAddIconKey(path, false);
                    ListViewItem item = new ListViewItem(info.Name, iconKey);
                    item.SubItems.Add(FormatBytes(info.Length));
                    item.SubItems.Add("Pendiente");
                    item.BackColor = Color.FromArgb(46, 38, 76);
                    item.Tag = path;
                    listFiles.Items.Add(item);
                    addedCount++;
                }
                else if (Directory.Exists(path))
                {
                    string folderName = new DirectoryInfo(path).Name;
                    long dirSize = GetDirectorySize(path);
                    string iconKey = GetOrAddIconKey(path, true);
                    ListViewItem item = new ListViewItem(folderName, iconKey);
                    item.SubItems.Add(FormatBytes(dirSize));
                    item.SubItems.Add("Pendiente");
                    item.BackColor = Color.FromArgb(46, 38, 76);
                    item.Tag = path;
                    listFiles.Items.Add(item);
                    addedCount++;
                }
            }

            if (addedCount > 0)
            {
                rowFlashSteps = 18;
                lblStatus.Text = "Se añadieron " + addedCount + " elemento(s) a la cola.";
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
            btnSend.Text = "Enviando...";
            targetProgress = 0;
            animatedProgress = 0;
            lblRemaining.Text = "Tiempo restante: calculando...";
            sendStopwatch.Restart();

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
                        item.SubItems[2].ForeColor = StatusAmber;
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
                    item.SubItems[2].ForeColor = AccentPurpleLight;
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
                    item.SubItems[2].ForeColor = StatusGreen;
                }
                catch (Exception ex)
                {
                    item.SubItems[2].Text = "Error";
                    item.SubItems[2].ForeColor = StatusRed;
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

            sendStopwatch.Stop();
            isSending = false;
            btnSend.Enabled = true;
            btnSend.Text = "Enviar ahora  →";

            int finalPercent = totalBytes <= 0 ? 100 : (int)Math.Min(100, totalSent * 100.0 / totalBytes);
            targetProgress = failures.Count == 0 ? 100 : finalPercent;
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
                targetProgress = 0;
                animatedProgress = 0;
                lblPercent.Text = "0%";
                pnlProgressBar.Invalidate();
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
            targetProgress = Math.Max(0, Math.Min(100, percent));
            lblStatus.Text = "Enviando " + name + " (" + FormatBytes(fileSent) + " / " + FormatBytes(fileSize) + ")";

            double elapsedSeconds = sendStopwatch.Elapsed.TotalSeconds;
            if (sent > 0 && total > sent && elapsedSeconds > 0.35)
            {
                double bytesPerSec = sent / elapsedSeconds;
                if (bytesPerSec > 1)
                {
                    double remainingSeconds = (total - sent) / bytesPerSec;
                    lblRemaining.Text = "Tiempo restante: " + FormatDuration(remainingSeconds);
                }
            }
            else if (sent >= total && total > 0)
            {
                lblRemaining.Text = "Tiempo restante: 0 s";
            }
        }

        private static string FormatDuration(double seconds)
        {
            if (seconds < 1) return "menos de 1 s";
            TimeSpan ts = TimeSpan.FromSeconds(Math.Ceiling(seconds));
            if (ts.TotalHours >= 1)
                return string.Format("{0} h {1} min", (int)ts.TotalHours, ts.Minutes);
            if (ts.TotalMinutes >= 1)
                return string.Format("{0} min {1} s", (int)ts.TotalMinutes, ts.Seconds);
            return string.Format("{0} s", ts.Seconds);
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
            animTimer.Stop();
            animTimer.Dispose();
            if (fileIcons != null)
            {
                fileIcons.Dispose();
                fileIcons = null;
            }
            receiver.Stop();
            base.OnFormClosing(e);
        }
    }
}
