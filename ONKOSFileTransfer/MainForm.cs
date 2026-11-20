using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ONKOSFileTransfer.Network;

namespace ONKOSFileTransfer
{
    public class MainForm : Form
    {
        private const int Port = 5050;

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
        private readonly PeerDiscoveryService discovery;
        private readonly System.Windows.Forms.Timer animTimer;
        private readonly Stopwatch sendStopwatch = new Stopwatch();
        private PeerInfo currentProfile;

        private ComboBox cmbDestinatario;
        private Label lblPuenteInfo;
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
        private Label lblUserHeader;

        private string activeFolder;
        private readonly List<PeerInfo> resolvedTargets = new List<PeerInfo>();
        private bool isSending;
        private bool isDragOver;
        private bool updatingCombo;

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

        public MainForm(PeerInfo profile = null)
        {
            DoubleBuffered = true;
            currentProfile = profile;
            if (currentProfile == null && !UserProfileStore.TryLoad(out currentProfile))
            {
                currentProfile = new PeerInfo
                {
                    FullName = Environment.UserName,
                    Area = "General",
                    IPv4 = NetworkManager.GetLocalIPv4(),
                    MachineName = Environment.MachineName
                };
            }

            activeFolder = InboxMonitor.CarpetaRecibidos;

            receiver = new FileReceiver();
            receiver.FileReceived += path =>
                UpdateReceiverFooter("Recibido: " + Path.GetFileName(path) + "  •  Clic para abrir carpeta");
            receiver.TransferProgress += (name, received, size) =>
                UpdateReceiverFooter("Recibiendo " + name + " (" + FormatBytes(received) + " / " + FormatBytes(size) + ")");
            receiver.Error += message =>
                UpdateReceiverFooter("Error de recepción: " + message);

            discovery = new PeerDiscoveryService();
            discovery.PeersUpdated += RefreshPeersList;

            animTimer = new System.Windows.Forms.Timer { Interval = 25 };
            animTimer.Tick += OnAnimationTick;

            BuildInterface();
            animTimer.Start();
            StartReceiver(activeFolder);
            discovery.Start(currentProfile);
            RefreshPeersList();
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
            ClientSize = new Size(960, 710);
            MinimumSize = new Size(960, 710);
            MaximumSize = new Size(960, 710);
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

            lblUserHeader = new Label
            {
                Text = $"{currentProfile.FullName} • {currentProfile.Area}",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextSecondary,
                Location = new Point(28, 48),
                Size = new Size(440, 18)
            };
            Controls.Add(lblUserHeader);

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
                "⚙  Mi Perfil",
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
                Font = new Font("Segoe UI", 16.5F, FontStyle.Bold),
                ForeColor = TextPrimary,
                Location = new Point(28, 76),
                Size = new Size(460, 30)
            });

            Controls.Add(new Label
            {
                Text = "Envía a una o varias personas en simultáneo por nombre (separa con ;) o vincula con Nombre = IP.",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextSecondary,
                Location = new Point(28, 106),
                Size = new Size(906, 20)
            });

            // --- TARJETA DESTINO ---
            pnlDestCard = new Panel
            {
                Location = new Point(28, 132),
                Size = new Size(906, 74),
                BackColor = BgCard
            };
            pnlDestCard.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, pnlDestCard.Width - 1, pnlDestCard.Height - 1);
                Color border = (cmbDestinatario != null && cmbDestinatario.Focused) ? AccentPurpleLight : BorderSubtle;
                using (GraphicsPath path = GetRoundedRect(rect, 10))
                using (SolidBrush fill = new SolidBrush(BgCard))
                using (Pen pen = new Pen(border, (cmbDestinatario != null && cmbDestinatario.Focused) ? 1.5f : 1f))
                {
                    pnlDestCard.Region = new Region(path);
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(pen, path);
                }
            };

            pnlDestCard.Controls.Add(new Label
            {
                Text = "ENVIAR A",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextSecondary,
                Location = new Point(16, 17),
                Size = new Size(88, 20)
            });

            cmbDestinatario = new ComboBox
            {
                Location = new Point(110, 12),
                Size = new Size(778, 30),
                Font = new Font("Segoe UI", 10F),
                DropDownStyle = ComboBoxStyle.DropDown,
                BackColor = BgInput,
                ForeColor = TextPrimary
            };
            cmbDestinatario.SelectedIndexChanged += OnDestinatarioChanged;
            cmbDestinatario.TextChanged += OnDestinatarioTextChanged;
            cmbDestinatario.GotFocus += (s, e) => pnlDestCard.Invalidate();
            cmbDestinatario.LostFocus += (s, e) => pnlDestCard.Invalidate();
            pnlDestCard.Controls.Add(cmbDestinatario);

            lblPuenteInfo = new Label
            {
                Text = "Ej: ANAMARIA; CARLOS   |   Para agregar: ANAMARIA HERRERA = 172.16.13.45",
                Font = new Font("Segoe UI", 8.2F),
                ForeColor = TextSecondary,
                Location = new Point(112, 46),
                Size = new Size(776, 18)
            };
            pnlDestCard.Controls.Add(lblPuenteInfo);
            Controls.Add(pnlDestCard);

            // --- ZONA DRAG & DROP ---
            Controls.Add(new Label
            {
                Text = "ARCHIVOS Y CARPETAS  •  ARRASTRA AQUÍ PARA AÑADIR",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextSecondary,
                Location = new Point(28, 218),
                Size = new Size(500, 18)
            });

            pnlDropZone = new Panel
            {
                Location = new Point(28, 240),
                Size = new Size(906, 104),
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

            // --- TABLA DE ARCHIVOS ---
            Panel pnlTableHeader = new Panel
            {
                Location = new Point(28, 356),
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
                Location = new Point(28, 384),
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
                new Point(28, 508),
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

            // --- PROGRESO ANIMADO ---
            Controls.Add(new Label
            {
                Text = "PROGRESO",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TextSecondary,
                Location = new Point(28, 560),
                Size = new Size(120, 18)
            });

            lblPercent = new Label
            {
                Text = "0%",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = AccentPurpleLight,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(854, 560),
                Size = new Size(80, 18)
            };
            Controls.Add(lblPercent);

            pnlProgressBar = new Panel
            {
                Location = new Point(28, 582),
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
                Location = new Point(28, 603),
                Size = new Size(650, 20)
            };
            Controls.Add(lblStatus);

            lblRemaining = new Label
            {
                Text = "Tiempo restante: —",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextSecondary,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(684, 603),
                Size = new Size(250, 20)
            };
            Controls.Add(lblRemaining);

            lblReceiverFooter = new Label
            {
                Text = "Recepción automática activa.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextLink,
                Location = new Point(28, 634),
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

        private void RefreshPeersList()
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(RefreshPeersList));
                return;
            }

            List<PeerInfo> allPeers = discovery.GetActivePeers();
            string typedText = cmbDestinatario.Text ?? string.Empty;
            int caretPos = cmbDestinatario.SelectionStart;

            string lastSegment = typedText.Split(new[] { ';', ',' }, StringSplitOptions.None).LastOrDefault()?.Trim() ?? string.Empty;

            List<PeerInfo> matching = string.IsNullOrWhiteSpace(lastSegment)
                ? allPeers
                : allPeers.Where(p =>
                    (!string.IsNullOrEmpty(p.FullName) && p.FullName.IndexOf(lastSegment, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(p.Area) && p.Area.IndexOf(lastSegment, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(p.DisplayText) && p.DisplayText.IndexOf(lastSegment, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(p.IPv4) && p.IPv4.IndexOf(lastSegment, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

            updatingCombo = true;
            cmbDestinatario.BeginUpdate();
            cmbDestinatario.Items.Clear();
            foreach (PeerInfo p in matching)
                cmbDestinatario.Items.Add(p);

            if (!string.IsNullOrEmpty(typedText))
            {
                cmbDestinatario.Text = typedText;
                cmbDestinatario.SelectionStart = Math.Min(caretPos, cmbDestinatario.Text.Length);
                cmbDestinatario.SelectionLength = 0;
            }
            cmbDestinatario.EndUpdate();
            updatingCombo = false;

            ResolveCurrentTargets(persistManual: false);
        }

        private void OnDestinatarioChanged(object sender, EventArgs e)
        {
            if (updatingCombo) return;
            if (cmbDestinatario.SelectedItem is PeerInfo peer)
            {
                string current = cmbDestinatario.Text ?? string.Empty;
                int sepIndex = current.LastIndexOfAny(new[] { ';', ',' });
                string updatedText = sepIndex >= 0
                    ? current.Substring(0, sepIndex + 1).TrimEnd() + " " + peer.FullName + "; "
                    : peer.FullName + "; ";

                updatingCombo = true;
                cmbDestinatario.Text = updatedText;
                cmbDestinatario.SelectionStart = cmbDestinatario.Text.Length;
                cmbDestinatario.SelectionLength = 0;
                updatingCombo = false;

                ResolveCurrentTargets(persistManual: false);
            }
        }

        private void OnDestinatarioTextChanged(object sender, EventArgs e)
        {
            if (updatingCombo) return;
            RefreshPeersList();

            string raw = (cmbDestinatario.Text ?? string.Empty).Trim();
            string lastSegment = raw.Split(new[] { ';', ',' }, StringSplitOptions.None).LastOrDefault()?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(lastSegment) &&
                !lastSegment.Contains("=") &&
                cmbDestinatario.Items.Count > 0 &&
                cmbDestinatario.Focused)
            {
                if (!cmbDestinatario.DroppedDown)
                    cmbDestinatario.DroppedDown = true;
            }
        }

        private void ResolveCurrentTargets(bool persistManual)
        {
            resolvedTargets.Clear();
            string raw = (cmbDestinatario.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(raw))
            {
                lblPuenteInfo.Text = "Ej: ANAMARIA; CARLOS   |   Para agregar: ANAMARIA HERRERA = 172.16.13.45";
                lblPuenteInfo.ForeColor = TextSecondary;
                return;
            }

            string[] tokens = raw.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> unresolved = new List<string>();
            var seenIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string rawToken in tokens)
            {
                string token = rawToken.Trim();
                if (string.IsNullOrWhiteSpace(token))
                    continue;

                // Formato "Nombre = IP" o "Nombre - Area = IP"
                int eqIndex = token.IndexOf('=');
                if (eqIndex > 0)
                {
                    string left = token.Substring(0, eqIndex).Trim();
                    string rightIp = token.Substring(eqIndex + 1).Trim();

                    if (IPAddress.TryParse(rightIp, out IPAddress manualIp) && manualIp.AddressFamily == AddressFamily.InterNetwork)
                    {
                        string namePart = left;
                        string areaPart = "Agenda";
                        int dashIdx = left.IndexOf("—");
                        if (dashIdx < 0) dashIdx = left.IndexOf(" - ");
                        if (dashIdx > 0)
                        {
                            namePart = left.Substring(0, dashIdx).Trim();
                            areaPart = left.Substring(dashIdx + 1).Trim(' ', '—', '-');
                        }

                        PeerInfo peer = persistManual
                            ? discovery.RegisterManualContact(namePart, areaPart, manualIp.ToString())
                            : new PeerInfo { FullName = namePart, Area = areaPart, IPv4 = manualIp.ToString() };

                        if (peer != null && seenIps.Add(peer.IPv4))
                            resolvedTargets.Add(peer);
                        continue;
                    }
                }

                // Búsqueda por nombre en contactos/red
                PeerInfo match = discovery.FindPeerByNameOrIp(token);
                if (match != null)
                {
                    if (seenIps.Add(match.IPv4))
                        resolvedTargets.Add(match);
                    continue;
                }

                // IP directa
                if (IPAddress.TryParse(token, out IPAddress parsedIp) && parsedIp.AddressFamily == AddressFamily.InterNetwork)
                {
                    string ipStr = parsedIp.ToString();
                    if (seenIps.Add(ipStr))
                    {
                        resolvedTargets.Add(new PeerInfo
                        {
                            FullName = ipStr,
                            Area = "Directo",
                            IPv4 = ipStr
                        });
                    }
                    continue;
                }

                unresolved.Add(token);
            }

            if (resolvedTargets.Count > 0 && unresolved.Count == 0)
            {
                string resumen = string.Join("  •  ", resolvedTargets.Select(t => $"{t.FullName} ({t.IPv4})"));
                lblPuenteInfo.Text = $"Listos ({resolvedTargets.Count}): {resumen}";
                lblPuenteInfo.ForeColor = StatusGreen;
            }
            else if (resolvedTargets.Count > 0 && unresolved.Count > 0)
            {
                lblPuenteInfo.Text = $"Listos: {resolvedTargets.Count}  |  Sin encontrar: {string.Join(", ", unresolved)} (usa Nombre = IP para agregar)";
                lblPuenteInfo.ForeColor = StatusAmber;
            }
            else
            {
                lblPuenteInfo.Text = $"No encontrado: {string.Join(", ", unresolved)}. Para agregarlo escribe: {unresolved[0]} = IP_DEL_EQUIPO";
                lblPuenteInfo.ForeColor = StatusAmber;
            }
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
