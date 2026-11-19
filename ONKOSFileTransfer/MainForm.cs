using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;
using ONKOSFileTransfer.Network;

namespace ONKOSFileTransfer
{
    public class MainForm : Form
    {
        private const int Port = 5050;
        private readonly FileReceiver receiver;
        private TextBox txtIP;
        private Label lblStatus;
        private Label lblProgress;
        private RoundedProgressBar progressBar;
        private ListView listFiles;
        private AttachmentDropZone dropZone;
        private ImageList fileIcons;
        private bool isLoadingAttachments;
        private CheckBox chkSelectAll;
        private bool updatingChecks;
        private Button btnSend;
        private long totalBytes;
        private long totalSent;
        private bool isSending;
        private Label lblCurrentAction;
        private Label lblEta;
        private Stopwatch transferClock;
        private NotifyIcon notifier;
        private Timer entranceTimer;

        public MainForm()
        {
            receiver = new FileReceiver();
            receiver.FileReceived += path =>
            {
                UpdateStatus("Recepción completada: " + Path.GetFileName(path));
                ShowNotification("Archivo recibido", Path.GetFileName(path) + " se guardó correctamente.", ToolTipIcon.Info);
            };
            receiver.TransferProgress += (name, received, size) =>
            {
                UpdateStatus("Recibiendo " + name + " (" + FormatBytes(received) + " / " + FormatBytes(size) + ")");
                UpdateCurrentAction("Recibiendo: " + name);
            };
            receiver.Error += message =>
            {
                UpdateStatus("Error al recibir: " + message);
                ShowNotification("Error al recibir", message, ToolTipIcon.Error);
            };

            notifier = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "ONKOS File Transfer 2.0"
            };
            BuildInterface();
            Shown += StartEntranceAnimation;
            StartReceiver();
        }

        private Panel pageHost;
        private Button activeNavButton;

        private void BuildInterface()
        {
            Text = "ONKOS File Transfer 2.0";
            Size = new Size(980, 700);
            MinimumSize = new Size(820, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(17, 19, 31);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9F);
            AllowDrop = true;
            DragEnter += MainDragEnter;
            DragDrop += MainDragDrop;

            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 76,
                BackColor = Color.FromArgb(23, 25, 40),
                Padding = new Padding(24, 12, 24, 10)
            };
            Label brand = new Label
            {
                Text = "◈  ONKOS",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(190, 160, 255),
                Location = new Point(22, 8),
                AutoSize = true
            };
            header.Controls.Add(brand);
            Label product = new Label
            {
                Text = "FILE TRANSFER 2.0  ·  ENVÍA Y RECIBE EN TU RED",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(165, 170, 192),
                Location = new Point(26, 45),
                AutoSize = true
            };
            header.Controls.Add(product);

            // Estado del equipo en la franja superior, visible sin ocupar espacio del área de trabajo.
            RoundedPanel headerStatus = new RoundedPanel(12)
            {
                Location = new Point(0, 22),
                Size = new Size(300, 38),
                BackColor = Color.FromArgb(35, 39, 58),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Label headerIp = new Label
            {
                Text = "IP " + NetworkManager.GetLocalIPv4(),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(14, 10),
                AutoSize = true
            };
            headerStatus.Controls.Add(headerIp);
            Label headerReceive = new Label
            {
                Text = "●  Recepción automática activa",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(133, 225, 169),
                Location = new Point(headerIp.Right + 18, 11),
                AutoSize = true
            };
            headerStatus.Controls.Add(headerReceive);
            header.Controls.Add(headerStatus);

            Button admin = MakeButton("⚙ Administración", 0, 22, 150, 38, Color.FromArgb(43, 46, 66));
            admin.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            admin.Click += OpenAdministration;
            header.Controls.Add(admin);
            Action adjustHeader = delegate
            {
                admin.Left = header.ClientSize.Width - admin.Width - 24;
                admin.Top = 22;
                headerStatus.Width = 300;
                headerStatus.Left = Math.Max(320, admin.Left - headerStatus.Width - 12);
                headerStatus.Top = admin.Top;
                headerIp.Left = 12;
                headerIp.Top = (headerStatus.Height - headerIp.Height) / 2;
                headerReceive.Left = headerIp.Right + 12;
                headerReceive.Top = (headerStatus.Height - headerReceive.Height) / 2;
            };
            header.Resize += delegate { adjustHeader(); };
            adjustHeader();

            pageHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(17, 19, 31)
            };
            Controls.Add(pageHost);
            Controls.Add(header);
            ShowPage("transfer", null);
        }

        private void OpenAdministration(object sender, EventArgs e)
        {
            bool isAdministrator = false;
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    isAdministrator = principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                isAdministrator = false;
            }

            if (!isAdministrator)
            {
                MessageBox.Show(
                    "Esta sección está reservada para el técnico de sistemas.\r\n\r\n" +
                    "Cierra ONKOS y ejecútalo con una cuenta de Windows que tenga permisos de administrador.",
                    "Acceso restringido",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            ShowPage("settings", null);
        }

        private void AddNavButton(Panel sidebar, string text, string page, int top)
        {
            Button button = MakeButton(text, 8, top, 198, 42, Color.FromArgb(31, 34, 52));
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(12, 0, 0, 0);
            button.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            button.Click += delegate { ShowPage(page, button); };
            sidebar.Controls.Add(button);
        }

        private void ShowPage(string page, Button navButton)
        {
            if (pageHost == null) return;
            pageHost.SuspendLayout();
            pageHost.Controls.Clear();

            if (activeNavButton != null)
                activeNavButton.BackColor = Color.FromArgb(31, 34, 52);
            if (navButton != null)
            {
                activeNavButton = navButton;
                activeNavButton.BackColor = Color.FromArgb(91, 57, 157);
            }

            if (page == "transfer")
                BuildTransferPage();
            else
                BuildInformationPage(page);

            pageHost.ResumeLayout(true);
        }

        private void BuildTransferPage()
        {
            Panel content = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(20, 16, 20, 16),
                BackColor = Color.FromArgb(17, 19, 31)
            };
            pageHost.Controls.Add(content);

            Label title = new Label
            {
                Text = "Centro de transferencias",
                Font = new Font("Segoe UI", 20, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(22, 12),
                AutoSize = true
            };
            content.Controls.Add(title);

            Label subtitle = new Label
            {
                Text = "Envía archivos a otros computadores y recibe transferencias en esta estación.",
                ForeColor = Color.FromArgb(165, 170, 192),
                Location = new Point(24, 47),
                AutoSize = true
            };
            content.Controls.Add(subtitle);

            content.Controls.Add(MakeLabel("DESTINO", 24, 88));
            txtIP = new TextBox
            {
                Location = new Point(112, 78),
                Size = new Size(620, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Font = new Font("Segoe UI", 10.5F),
                BackColor = Color.FromArgb(35, 39, 58),
                ForeColor = Color.FromArgb(245, 246, 252),
                BorderStyle = BorderStyle.FixedSingle,
                Text = "",
                Margin = new Padding(0),
                AccessibleName = "Dirección IP del equipo de destino"
            };
            content.Controls.Add(txtIP);

            content.Controls.Add(MakeLabel("ARCHIVOS Y CARPETAS  ·  ARRASTRA AQUÍ PARA AÑADIR", 24, 128));
            listFiles = new ListView
            {
                Location = new Point(24, 250),
                Size = new Size(848, 120),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                MultiSelect = false,
                CheckBoxes = true,
                BackColor = Color.FromArgb(25, 28, 44),
                ForeColor = Color.FromArgb(235, 237, 248),
                BorderStyle = BorderStyle.None,
                OwnerDraw = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                HideSelection = false,
                Font = new Font("Segoe UI", 9F)
            };
            fileIcons = new ImageList
            {
                ImageSize = new Size(18, 18),
                ColorDepth = ColorDepth.Depth32Bit
            };
            listFiles.SmallImageList = fileIcons;
            listFiles.Columns.Add("  ARCHIVO", 450);
            listFiles.Columns.Add("TAMAÑO", 135);
            listFiles.Columns.Add("ESTADO", 170);
            listFiles.DrawColumnHeader += DrawFileColumnHeader;
            listFiles.DrawItem += DrawFileItem;
            listFiles.DrawSubItem += DrawFileSubItem;
            listFiles.ItemChecked += ListFilesItemChecked;

            chkSelectAll = new CheckBox
            {
                Text = "Seleccionar todo",
                AutoSize = true,
                Checked = true,
                Visible = false,
                ForeColor = Color.FromArgb(190, 183, 222),
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(748, 126)
            };
            chkSelectAll.CheckedChanged += SelectAllChanged;
            content.Controls.Add(chkSelectAll);

            // Aumenta la altura de cada fila para dar aire y facilitar la lectura.
            ImageList rowHeight = new ImageList
            {
                ImageSize = new Size(1, 38),
                ColorDepth = ColorDepth.Depth32Bit
            };
            rowHeight.Images.Add(new Bitmap(1, 38));
            listFiles.SmallImageList = rowHeight;
            listFiles.AllowDrop = true;
            listFiles.DragEnter += MainDragEnter;
            listFiles.DragLeave += MainDragLeave;
            listFiles.DragDrop += MainDragDrop;
            listFiles.Visible = true;
            content.Controls.Add(listFiles);

            dropZone = new AttachmentDropZone
            {
                Location = new Point(24, 152),
                Size = new Size(848, 84),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(24, 27, 43),
                ForeColor = Color.FromArgb(190, 183, 222),
                AllowDrop = true,
                Cursor = Cursors.Hand
            };
            dropZone.Click += OpenAttachmentPicker;
            dropZone.DragEnter += MainDragEnter;
            dropZone.DragLeave += MainDragLeave;
            dropZone.DragDrop += MainDragDrop;
            content.Controls.Add(dropZone);

            btnSend = MakeButton("Enviar ahora  →", 24, 386, 854, 36, Color.FromArgb(112, 70, 194));
            btnSend.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnSend.Click += SendFiles;
            content.Controls.Add(btnSend);

            content.Controls.Add(MakeLabel("PROGRESO", 24, 432));
            lblProgress = new Label
            {
                Text = "0%",
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(808, 428),
                Size = new Size(70, 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            content.Controls.Add(lblProgress);

            progressBar = new RoundedProgressBar
            {
                Location = new Point(24, 456),
                Size = new Size(848, 10),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(17, 19, 31),
                ProgressColor = Color.FromArgb(139, 92, 246),
                ProgressEndColor = Color.FromArgb(99, 102, 241)
            };
            content.Controls.Add(progressBar);

            lblCurrentAction = new Label
            {
                Text = "Listo para enviar o recibir archivos.",
                ForeColor = Color.FromArgb(218, 220, 235),
                Location = new Point(24, 480),
                Size = new Size(610, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            content.Controls.Add(lblCurrentAction);

            lblEta = new Label
            {
                Text = "Tiempo restante: —",
                ForeColor = Color.FromArgb(160, 166, 190),
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(650, 480),
                Size = new Size(222, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            content.Controls.Add(lblEta);

            lblStatus = new Label
            {
                Text = "Recepción automática activa.",
                ForeColor = Color.FromArgb(145, 195, 255),
                Location = new Point(24, 508),
                Size = new Size(848, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            content.Controls.Add(lblStatus);

            // Ajusta todos los elementos al ancho real del área de trabajo.
            Action adjustLayout = delegate
            {
                int right = Math.Max(420, content.ClientSize.Width - 24);
                int available = Math.Max(360, right - 24);

                listFiles.Width = available;
                txtIP.Width = available;
                dropZone.Width = available;
                btnSend.Left = 24;
                btnSend.Width = available;
                chkSelectAll.Left = right - chkSelectAll.Width;

                // Las tres columnas ocupan exactamente el ancho útil de la tabla.
                int tableWidth = Math.Max(360, listFiles.ClientSize.Width - 4);
                listFiles.Columns[0].Width = (int)(tableWidth * 0.60);
                listFiles.Columns[1].Width = (int)(tableWidth * 0.17);
                listFiles.Columns[2].Width = tableWidth - listFiles.Columns[0].Width - listFiles.Columns[1].Width;

                lblProgress.Left = right - lblProgress.Width;
                progressBar.Width = available;
                lblStatus.Width = available;
                lblEta.Left = right - lblEta.Width;
                lblCurrentAction.Width = Math.Max(220, lblEta.Left - lblCurrentAction.Left - 12);
            };
            content.Resize += delegate { adjustLayout(); };
            adjustLayout();
        }

        private void BuildInformationPage(string page)
        {
            string title = "Sección";
            string description = "Esta sección forma parte de la nueva interfaz de ONKOS File Transfer 2.0.";
            string[] features = new string[0];

            switch (page)
            {
                case "history":
                    title = "Historial de transferencias";
                    description = "Consulta los archivos enviados y recibidos desde este computador.";
                    features = new string[] { "Registro local de envíos y recepciones", "Fecha, hora, equipo y usuario", "Resultado de cada archivo", "Búsqueda y filtros por estado" };
                    break;
                case "devices":
                    title = "Equipos autorizados";
                    description = "Administra qué computadores pueden conectarse con esta estación.";
                    features = new string[] { "Registrar un equipo por nombre e IP", "Aprobar o rechazar solicitudes", "Consultar equipos autorizados", "Revocar el acceso de un equipo" };
                    break;
                case "groups":
                    title = "Grupos por área";
                    description = "Organiza los computadores de la clínica para facilitar los envíos múltiples.";
                    features = new string[] { "Crear grupos por área", "Agregar o quitar equipos", "Seleccionar varios destinatarios", "Enviar a un grupo completo" };
                    break;
                case "notifications":
                    title = "Centro de notificaciones";
                    description = "Revisa los avisos de actividad, transferencias y seguridad.";
                    features = new string[] { "Transferencias enviadas y recibidas", "Errores y fallos de conexión", "Eventos de autorización y seguridad", "Avisos visuales, sin sonido" };
                    break;
                case "settings":
                    title = "Administración de ONKOS";
                    description = "Panel reservado al técnico de sistemas para administrar esta estación y sus permisos.";
                    features = new string[] { "Equipos autorizados y solicitudes de acceso", "Grupos de computadores por área", "Configuración de red: Wi-Fi y Ethernet", "Seguridad, certificados y permisos", "Historial y registros de actividad", "Estado del servicio de recepción" };
                    break;
            }

            Panel content = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(34, 30, 34, 24),
                BackColor = Color.FromArgb(17, 19, 31)
            };
            pageHost.Controls.Add(content);

            Label heading = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 21, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(30, 24),
                AutoSize = true
            };
            content.Controls.Add(heading);

            Label sub = new Label
            {
                Text = description,
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = Color.FromArgb(165, 170, 192),
                Location = new Point(32, 68),
                AutoSize = true
            };
            content.Controls.Add(sub);

            RoundedPanel card = new RoundedPanel(20)
            {
                Location = new Point(30, 125),
                Size = new Size(790, 330),
                BackColor = Color.FromArgb(27, 30, 47),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            content.Controls.Add(card);
            Action adjustCard = delegate
            {
                card.Width = Math.Max(360, content.ClientSize.Width - 60);
            };
            content.Resize += delegate { adjustCard(); };
            adjustCard();

            Label status = new Label
            {
                Text = "EN PREPARACIÓN",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(183, 148, 255),
                Location = new Point(25, 24),
                AutoSize = true
            };
            card.Controls.Add(status);

            Label note = new Label
            {
                Text = "La navegación visual ya está preparada. Las funciones de este módulo se implementarán y probarán en las siguientes etapas.",
                ForeColor = Color.FromArgb(190, 194, 211),
                Location = new Point(25, 58),
                Size = new Size(720, 48)
            };
            card.Controls.Add(note);

            Label listTitle = new Label
            {
                Text = "LO QUE INCLUIRÁ ESTA SECCIÓN",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(130, 136, 160),
                Location = new Point(25, 125),
                AutoSize = true
            };
            card.Controls.Add(listTitle);

            int y = 160;
            foreach (string feature in features)
            {
                Label item = new Label
                {
                    Text = "✦    " + feature,
                    Font = new Font("Segoe UI", 10.5F),
                    ForeColor = Color.FromArgb(225, 226, 236),
                    Location = new Point(28, y),
                    AutoSize = true
                };
                card.Controls.Add(item);
                y += 36;
            }
        }

        private Label MakeLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 202, 218),
                Location = new Point(x, y),
                Size = new Size(650, 23)
            };
        }

        private Button MakeButton(string text, int x, int y, int width, int height, Color color)
        {
            Button button = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                BackColor = color,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = color;
            button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(color, 0.12F);
            Color normal = color;
            Color hover = ControlPaint.Light(color, 0.16F);
            Timer hoverTimer = new Timer { Interval = 15 };
            int animationStep = 0;
            bool hovering = false;
            button.MouseEnter += delegate { hovering = true; animationStep = 0; hoverTimer.Start(); };
            button.MouseLeave += delegate { hovering = false; animationStep = 0; hoverTimer.Start(); };
            hoverTimer.Tick += delegate
            {
                if (button.IsDisposed) { hoverTimer.Stop(); hoverTimer.Dispose(); return; }
                animationStep = Math.Min(8, animationStep + 1);
                float amount = animationStep / 8F;
                Color target = hovering ? hover : normal;
                button.BackColor = Color.FromArgb(
                    (int)(normal.R + (target.R - normal.R) * amount),
                    (int)(normal.G + (target.G - normal.G) * amount),
                    (int)(normal.B + (target.B - normal.B) * amount));
                if (animationStep >= 8) hoverTimer.Stop();
            };
            button.Disposed += delegate { hoverTimer.Stop(); hoverTimer.Dispose(); };
            if (button.Width > 1 && button.Height > 1) button.Region = RoundedGeometry.Create(new Rectangle(0, 0, button.Width, button.Height), 10);
            button.Resize += delegate { if (button.IsDisposed || button.Width < 2 || button.Height < 2) return; Region oldRegion = button.Region; button.Region = RoundedGeometry.Create(new Rectangle(0, 0, button.Width, button.Height), 10); if (oldRegion != null) oldRegion.Dispose(); };
            return button;
        }

        private static string GetReceiveFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ONKOS_Received");
        }

        private void StartReceiver()
        {
            try
            {
                receiver.Start(Port, GetReceiveFolder());
                UpdateStatus("Recepción automática activa. Puerto " + Port + ".");
            }
            catch (Exception ex)
            {
                UpdateStatus("No se pudo activar la recepción: " + ex.Message);
                MessageBox.Show(
                    "No se pudo iniciar la recepción automática. Comprueba que el puerto 5050 esté libre y que tengas permisos para la carpeta.\r\n\r\n" + ex.Message,
                    "ONKOS File Transfer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SelectAllChanged(object sender, EventArgs e)
        {
            if (listFiles == null || updatingChecks) return;
            updatingChecks = true;
            foreach (ListViewItem item in listFiles.Items)
                item.Checked = chkSelectAll.Checked;
            updatingChecks = false;
        }

        private void ListFilesItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (updatingChecks || chkSelectAll == null) return;
            updatingChecks = true;
            chkSelectAll.Checked = listFiles.Items.Count > 0 &&
                listFiles.CheckedItems.Count == listFiles.Items.Count;
            updatingChecks = false;
        }

        private void DrawFileColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush background = new SolidBrush(Color.FromArgb(38, 42, 62)))
            using (Pen separator = new Pen(Color.FromArgb(65, 71, 98)))
            using (SolidBrush text = new SolidBrush(Color.FromArgb(177, 184, 211)))
            using (Font headerFont = new Font("Segoe UI Semibold", 8F, FontStyle.Bold))
            {
                e.Graphics.FillRectangle(background, e.Bounds);
                e.Graphics.DrawLine(separator, e.Bounds.Left, e.Bounds.Bottom - 1,
                    e.Bounds.Right, e.Bounds.Bottom - 1);
                Rectangle textBounds = new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 1,
                    Math.Max(0, e.Bounds.Width - 18), e.Bounds.Height - 2);
                e.Graphics.DrawString(e.Header.Text, headerFont, text, textBounds,
                    new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter });
            }
        }

        private void DrawFileItem(object sender, DrawListViewItemEventArgs e)
        {
            // El dibujo completo se realiza en DrawSubItem para controlar cada columna.
            if (e.Item.SubItems.Count == 0)
                e.DrawBackground();
        }

        private void DrawFileSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            bool selected = e.Item.Selected;
            Color rowColor = selected
                ? Color.FromArgb(63, 49, 91)
                : (e.ItemIndex % 2 == 0 ? Color.FromArgb(27, 30, 46) : Color.FromArgb(24, 27, 43));
            using (SolidBrush background = new SolidBrush(rowColor))
            using (SolidBrush foreground = new SolidBrush(
                selected ? Color.White :
                (e.ColumnIndex == 2 && string.Equals(e.SubItem.Text, "Pendiente", StringComparison.OrdinalIgnoreCase)
                    ? Color.FromArgb(179, 169, 218) : Color.FromArgb(225, 228, 241))))
            using (Pen separator = new Pen(Color.FromArgb(43, 47, 67)))
            using (Font rowFont = new Font("Segoe UI", 9F, FontStyle.Regular))
            {
                e.Graphics.FillRectangle(background, e.Bounds);
                if (e.ColumnIndex > 0)
                    e.Graphics.DrawLine(separator, e.Bounds.Left, e.Bounds.Top + 7,
                        e.Bounds.Left, e.Bounds.Bottom - 7);

                if (e.ColumnIndex == 0 && !string.IsNullOrEmpty(e.Item.ImageKey) &&
                    fileIcons != null && fileIcons.Images.ContainsKey(e.Item.ImageKey))
                {
                    Image fileIcon = fileIcons.Images[e.Item.ImageKey];
                    e.Graphics.DrawImage(fileIcon, e.Bounds.X + 31,
                        e.Bounds.Y + (e.Bounds.Height - 18) / 2, 18, 18);
                }
                Rectangle textBounds = new Rectangle(e.Bounds.X + (e.ColumnIndex == 0 ? 55 : 12),
                    e.Bounds.Y + 1, Math.Max(0, e.Bounds.Width - 22), e.Bounds.Height - 2);
                e.Graphics.DrawString(e.SubItem.Text, rowFont, foreground, textBounds,
                    new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter });
            }
        }

        private void OpenAttachmentPicker(object sender, EventArgs e)
        {
            if (isLoadingAttachments || isSending) return;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Selecciona los archivos que deseas enviar";
                dialog.Multiselect = true;
                dialog.CheckFileExists = true;
                dialog.CheckPathExists = true;
                dialog.Filter = "Todos los archivos (*.*)|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    AddPaths(dialog.FileNames);
            }
        }

        private void MainDragEnter(object sender, DragEventArgs e)
        {
            bool acceptsFiles = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effect = acceptsFiles ? DragDropEffects.Copy : DragDropEffects.None;
            if (dropZone != null) dropZone.DragActive = acceptsFiles;
        }

        private void MainDragLeave(object sender, EventArgs e)
        {
            if (dropZone != null) dropZone.DragActive = false;
        }

        private void MainDragDrop(object sender, DragEventArgs e)
        {
            if (dropZone != null) dropZone.DragActive = false;
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null) return;
            AddPaths(paths);
        }

        private void AddFolders(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Selecciona la carpeta completa que deseas enviar";
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog() == DialogResult.OK)
                    AddPaths(new string[] { dialog.SelectedPath });
            }
        }

        private async void AddPaths(IEnumerable<string> paths)
        {
            if (isLoadingAttachments || isSending) return;

            List<string> pending = new List<string>();
            foreach (string path in paths)
            {
                if (!File.Exists(path) && !Directory.Exists(path)) continue;
                bool exists = false;
                foreach (ListViewItem existing in listFiles.Items)
                    if (string.Equals(existing.Tag as string, path, StringComparison.OrdinalIgnoreCase))
                        exists = true;
                if (!exists) pending.Add(path);
            }
            if (pending.Count == 0)
            {
                UpdateStatus("Los elementos seleccionados ya están en la lista.");
                return;
            }

            isLoadingAttachments = true;
            btnSend.Enabled = false;
            progressBar.Progress = 0;
            lblProgress.Text = "0%";
            lblEta.Text = "Preparando adjuntos...";
            UpdateCurrentAction("Cargando archivos y carpetas seleccionados...");

            int loaded = 0;
            try
            {
                foreach (string path in pending)
                {
                    string displayName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    UpdateCurrentAction("Leyendo: " + displayName);
                    UpdateStatus("Preparando adjunto " + (loaded + 1) + " de " + pending.Count + "...");

                    long bytes = await Task.Run(() => CalculatePathSize(path));
                    bool isDirectory = Directory.Exists(path);
                    string size = isDirectory ? FormatBytes(bytes) + " · carpeta" : FormatBytes(bytes);
                    string iconKey = GetFileIconKey(path, isDirectory);

                    ListViewItem item = new ListViewItem(displayName);
                    item.ImageKey = iconKey;
                    item.SubItems.Add(size);
                    item.SubItems.Add("Pendiente");
                    item.Tag = path;
                    item.Checked = true;
                    item.BackColor = Color.FromArgb(67, 49, 103);
                    listFiles.Items.Add(item);
                    // Al incorporar el primer elemento, cambia el área vacía por la tabla.
                    listFiles.Visible = true;
                    dropZone.Visible = true;
                    chkSelectAll.Visible = true;
                    listFiles.EnsureVisible(item.Index);
                    Timer rowHighlight = new Timer { Interval = 45 };
                    int fadeStep = 0;
                    rowHighlight.Tick += delegate
                    {
                        if (item.ListView == null || item.ListView.IsDisposed)
                        {
                            rowHighlight.Stop();
                            rowHighlight.Dispose();
                            return;
                        }
                        fadeStep++;
                        float mix = Math.Min(1F, fadeStep / 8F);
                        Color from = Color.FromArgb(67, 49, 103);
                        Color to = item.Index % 2 == 0 ? Color.FromArgb(27, 30, 46) : Color.FromArgb(24, 27, 43);
                        item.BackColor = Color.FromArgb(
                            (int)(from.R + (to.R - from.R) * mix),
                            (int)(from.G + (to.G - from.G) * mix),
                            (int)(from.B + (to.B - from.B) * mix));
                        if (fadeStep >= 8) { rowHighlight.Stop(); rowHighlight.Dispose(); }
                    };
                    rowHighlight.Start();

                    loaded++;
                    int percent = (int)Math.Round(loaded * 100.0 / pending.Count);
                    progressBar.Progress = percent;
                    lblProgress.Text = percent + "%";
                    lblEta.Text = "Adjuntos: " + loaded + " de " + pending.Count;
                }
                UpdateStatus(loaded + " elemento(s) listo(s) para enviar.");
                UpdateCurrentAction("Adjuntos preparados. Puedes revisar la selección y enviar.");
            }
            catch (Exception ex)
            {
                UpdateStatus("No se pudieron preparar todos los adjuntos.");
                ShowNotification("Error al adjuntar", ex.Message, ToolTipIcon.Error);
            }
            finally
            {
                isLoadingAttachments = false;
                btnSend.Enabled = true;
                // Mantiene sincronizadas la tabla, el área para soltar y la selección.
                bool hasAttachments = listFiles.Items.Count > 0;
                listFiles.Visible = true;
                dropZone.Visible = true;
                chkSelectAll.Visible = hasAttachments;

                // La carga de adjuntos y el envío son etapas independientes.
                // Al terminar de preparar, reinicia la barra para medir el envío desde cero.
                progressBar.Progress = 0;
                lblProgress.Text = "0%";
                lblEta.Text = "Tiempo restante: —";
                if (loaded == pending.Count && loaded > 0)
                    UpdateCurrentAction("Adjuntos preparados. Listo para iniciar el envío.");
            }
        }

        private static long CalculatePathSize(string path)
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            long total = 0;
            foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                total += new FileInfo(file).Length;
            return total;
        }

        private string GetFileIconKey(string path, bool isDirectory)
        {
            string key = isDirectory ? "__folder" : Path.GetExtension(path).ToLowerInvariant();
            if (string.IsNullOrEmpty(key)) key = "__file";
            if (fileIcons.Images.ContainsKey(key)) return key;

            Icon icon = null;
            try
            {
                if (isDirectory)
                    icon = Icon.ExtractAssociatedIcon(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                else
                    icon = Icon.ExtractAssociatedIcon(path);
            }
            catch { }

            if (icon == null) icon = isDirectory ? SystemIcons.WinLogo : SystemIcons.Application;
            fileIcons.Images.Add(key, (Icon)icon.Clone());
            if (!ReferenceEquals(icon, SystemIcons.WinLogo) && !ReferenceEquals(icon, SystemIcons.Application))
                icon.Dispose();
            return key;
        }

        private static string CreateFolderPackage(string folderPath)
        {
            string folderName = new DirectoryInfo(folderPath).Name;
            string temp = Path.Combine(Path.GetTempPath(),
                folderName + "_" + Guid.NewGuid().ToString("N") + ".onkosfolder");
            using (FileStream zipStream = new FileStream(temp, FileMode.CreateNew))
            using (System.IO.Compression.ZipArchive archive =
                new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create))
            {
                string root = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (string file in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/');
                    archive.CreateEntryFromFile(file, folderName + "/" + relative,
                        System.IO.Compression.CompressionLevel.Fastest);
                }
                if (Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories).Length == 0)
                    archive.CreateEntry(folderName + "/");
            }
            return temp;
        }

        private void AddFiles(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Selecciona los archivos que deseas enviar";
                dialog.Multiselect = true;
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                AddPaths(dialog.FileNames);
            }
        }

        private void RemoveSelected(object sender, EventArgs e)
        {
            if (!isSending) foreach (ListViewItem item in listFiles.SelectedItems) listFiles.Items.Remove(item);
            listFiles.Visible = true;
            dropZone.Visible = true;
            chkSelectAll.Visible = listFiles.Items.Count > 0;
        }

        private async void SendFiles(object sender, EventArgs e)
        {
            IPAddress address;
            if (!IPAddress.TryParse(txtIP.Text.Trim(), out address) ||
                address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                MessageBox.Show("Escribe una dirección IPv4 válida.", "ONKOS File Transfer",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtIP.Focus();
                return;
            }

            if (listFiles.Items.Count == 0)
            {
                MessageBox.Show("Agrega al menos un archivo para enviar.", "ONKOS File Transfer",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<ListViewItem> items = new List<ListViewItem>();
            foreach (ListViewItem item in listFiles.Items)
                if (item.Checked) items.Add(item);
            if (items.Count == 0)
            {
                MessageBox.Show("Selecciona al menos un archivo o carpeta para enviar.", "ONKOS File Transfer",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            totalBytes = 0;
            totalSent = 0;
            int sentCount = 0;
            List<string> failures = new List<string>();

            foreach (ListViewItem item in items)
            {
                try
                {
                    string selectedPath = item.Tag as string;
                    if (File.Exists(selectedPath)) totalBytes += new FileInfo(selectedPath).Length;
                    else if (Directory.Exists(selectedPath))
                        foreach (string file in Directory.GetFiles(selectedPath, "*", SearchOption.AllDirectories))
                            totalBytes += new FileInfo(file).Length;
                }
                catch { }
            }

            isSending = true;
            btnSend.Enabled = false;
            progressBar.Progress = 0;
            lblProgress.Text = "0%";
            transferClock = Stopwatch.StartNew();
            lblEta.Text = "Calculando tiempo restante...";

            foreach (ListViewItem item in items)
            {
                string path = item.Tag as string;
                string name = Path.GetFileName(path);
                string transferPath = path;
                bool temporaryPackage = false;
                try
                {
                    if (!File.Exists(path) && !Directory.Exists(path))
                        throw new FileNotFoundException("El archivo o carpeta ya no existe.");

                    if (Directory.Exists(path))
                    {
                        item.SubItems[2].Text = "Preparando carpeta...";
                        UpdateCurrentAction("Preparando carpeta: " + name);
                        transferPath = CreateFolderPackage(path);
                        temporaryPackage = true;
                    }

                    item.SubItems[2].Text = "Conectando...";
                    UpdateStatus("Conectando con " + address + "...");
                    UpdateCurrentAction("Enviando: " + name);
                    long fileSize = new FileInfo(transferPath).Length;
                    long fileSent = 0;

                    await FileSender.SendFileAsync(address.ToString(), Port, transferPath,
                        (sent, size) =>
                        {
                            long delta = sent - fileSent;
                            fileSent = sent;
                            totalSent += delta;
                            UpdateProgress(totalSent, totalBytes, name, sent, size);
                        });

                    sentCount++;
                    item.SubItems[2].Text = "Enviado";
                    if (temporaryPackage && File.Exists(transferPath)) File.Delete(transferPath);
                    UpdateStatus("Enviado: " + name);
                    UpdateCurrentAction("Completado: " + name);
                    ShowNotification("Envío completado", name + " se envió correctamente.", ToolTipIcon.Info);
                }
                catch (Exception ex)
                {
                    item.SubItems[2].Text = "Error";
                    if (temporaryPackage && File.Exists(transferPath))
                    {
                        try { File.Delete(transferPath); } catch { }
                    }
                    failures.Add(name + ": " + ex.Message);
                    ShowNotification("No se pudo enviar", name + ": " + ex.Message, ToolTipIcon.Error);
                }
            }

            isSending = false;
            if (transferClock != null) transferClock.Stop();
            btnSend.Enabled = true;
            progressBar.Progress = 100;
            lblProgress.Text = "100%";

            string summary = "Transferencia finalizada.\r\n\r\nCorrectos: " + sentCount +
                " de " + items.Count + ".";
            if (failures.Count > 0)
                summary += "\r\n\r\nArchivos con error:\r\n" + string.Join("\r\n", failures.ToArray());

            UpdateStatus("Proceso terminado: " + sentCount + " de " + items.Count + " enviados.");
            UpdateCurrentAction(failures.Count == 0 ? "Todas las transferencias finalizaron correctamente." : "Transferencia finalizada con elementos pendientes.");
            if (lblEta != null) lblEta.Text = "Tiempo restante: 0 s";
            ShowNotification(failures.Count == 0 ? "Transferencia finalizada" : "Transferencia con advertencias", sentCount + " de " + items.Count + " elementos enviados.", failures.Count == 0 ? ToolTipIcon.Info : ToolTipIcon.Warning);
            MessageBox.Show(summary, "Resumen de transferencia",
                MessageBoxButtons.OK,
                failures.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void UpdateProgress(long sent, long total, string name, long fileSent, long fileSize)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action<long, long, string, long, long>(UpdateProgress),
                    sent, total, name, fileSent, fileSize);
                return;
            }

            int percent = total <= 0 ? 100 : (int)Math.Min(100, sent * 100.0 / total);
            progressBar.Progress = Math.Max(0, Math.Min(100, percent));
            lblProgress.Text = percent + "%";
            lblStatus.Text = "Enviando " + name + " — " + FormatBytes(fileSent) + " / " + FormatBytes(fileSize);
            UpdateCurrentAction("Enviando: " + name + " · " + FormatBytes(fileSent) + " de " + FormatBytes(fileSize));
            if (transferClock != null && transferClock.Elapsed.TotalSeconds >= 1 && sent > 0)
            {
                double bytesPerSecond = sent / transferClock.Elapsed.TotalSeconds;
                long remainingBytes = Math.Max(0, total - sent);
                double seconds = bytesPerSecond > 0 ? remainingBytes / bytesPerSecond : 0;
                lblEta.Text = "Restante aprox.: " + FormatDuration(seconds);
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
            lblStatus.Text = message;
        }

        private void UpdateCurrentAction(string message)
        {
            if (IsDisposed || !IsHandleCreated || lblCurrentAction == null) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(UpdateCurrentAction), message);
                return;
            }
            lblCurrentAction.Text = message;
        }

        private void ShowNotification(string title, string message, ToolTipIcon icon)
        {
            if (notifier == null || IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string, string, ToolTipIcon>(ShowNotification), title, message, icon);
                return;
            }
            notifier.BalloonTipTitle = title;
            notifier.BalloonTipText = message.Length > 220 ? message.Substring(0, 217) + "..." : message;
            notifier.BalloonTipIcon = icon;
            notifier.ShowBalloonTip(3500);
        }

        private void StartEntranceAnimation(object sender, EventArgs e)
        {
            Opacity = 0;
            entranceTimer = new Timer { Interval = 15 };
            entranceTimer.Tick += delegate
            {
                Opacity = Math.Min(1, Opacity + 0.08);
                if (Opacity >= 1)
                {
                    entranceTimer.Stop();
                    entranceTimer.Dispose();
                    entranceTimer = null;
                }
            };
            entranceTimer.Start();
        }

        private static string FormatDuration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) return "calculando...";
            TimeSpan time = TimeSpan.FromSeconds(Math.Min(seconds, 359999));
            if (time.TotalHours >= 1) return ((int)time.TotalHours) + " h " + time.Minutes + " min";
            if (time.TotalMinutes >= 1) return time.Minutes + " min " + time.Seconds + " s";
            return Math.Max(1, time.Seconds) + " s";
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
            if (entranceTimer != null) { entranceTimer.Stop(); entranceTimer.Dispose(); }
            if (notifier != null) { notifier.Visible = false; notifier.Dispose(); }
            base.OnFormClosing(e);
        }
    }

    internal sealed class OnkosMenuColors : ProfessionalColorTable
    {
        public override Color MenuBorder { get { return Color.FromArgb(75, 81, 111); } }
        public override Color MenuItemSelected { get { return Color.FromArgb(83, 66, 126); } }
        public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(83, 66, 126); } }
        public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(83, 66, 126); } }
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(35, 39, 58); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(35, 39, 58); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(35, 39, 58); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(35, 39, 58); } }
    }

    internal static class NativeMethods
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, string lParam);
    }

    internal sealed class AttachmentDropZone : Panel
    {
        private bool dragActive;
        public bool DragActive
        {
            get { return dragActive; }
            set { if (dragActive == value) return; dragActive = value; Invalidate(); }
        }

        public AttachmentDropZone()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
            using (GraphicsPath panelPath = RoundedGeometry.Path(bounds, 13))
            using (SolidBrush fill = new SolidBrush(dragActive ? Color.FromArgb(47, 37, 72) : Color.FromArgb(24, 27, 43)))
            using (Pen border = new Pen(dragActive ? Color.FromArgb(177, 146, 255) : Color.FromArgb(91, 78, 135), dragActive ? 2.2F : 1.4F))
            {
                border.DashPattern = new float[] { 5F, 4F };
                e.Graphics.FillPath(fill, panelPath);
                e.Graphics.DrawPath(border, panelPath);
            }

            int center = Width / 2;
            using (Font iconFont = new Font("Segoe UI Symbol", 28F, FontStyle.Regular))
            using (Font titleFont = new Font("Segoe UI Semibold", 11F, FontStyle.Bold))
            using (Font hintFont = new Font("Segoe UI", 9F))
            using (SolidBrush accent = new SolidBrush(Color.FromArgb(177, 146, 255)))
            using (SolidBrush title = new SolidBrush(Color.FromArgb(238, 237, 248)))
            using (SolidBrush hint = new SolidBrush(Color.FromArgb(155, 162, 190)))
            {
                StringFormat centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                e.Graphics.DrawString("⇧", iconFont, accent, new Rectangle(center - 40, Height / 2 - 62, 80, 52), centered);
                e.Graphics.DrawString(dragActive ? "Suelta los archivos para adjuntarlos" : "Arrastra aquí tus archivos o haz clic para seleccionarlos", titleFont, title,
                    new Rectangle(15, Height / 2 - 4, Width - 30, 28), centered);
                e.Graphics.DrawString("Puedes adjuntar varios archivos a la vez · También puedes soltarlos aquí",
                    hintFont, hint, new Rectangle(15, Height / 2 + 27, Width - 30, 24), centered);
            }
        }
    }

    internal sealed class RoundedProgressBar : Control
    {
        private int progress;
        public Color ProgressColor { get; set; }
        public Color ProgressEndColor { get; set; }

        public int Progress
        {
            get { return progress; }
            set
            {
                int next = Math.Max(0, Math.Min(100, value));
                if (progress == next) return;
                progress = next;
                Invalidate();
            }
        }

        public RoundedProgressBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 10;
            ProgressColor = Color.FromArgb(139, 92, 246);
            ProgressEndColor = Color.FromArgb(99, 102, 241);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int radius = Math.Max(2, Height / 2);
            Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));

            using (GraphicsPath track = RoundedGeometry.Path(bounds, radius))
            using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(48, 52, 73)))
            {
                e.Graphics.FillPath(trackBrush, track);
            }

            if (progress <= 0 || Width <= 2) return;

            int fillWidth = Math.Max(1, (int)Math.Round((Width - 1) * progress / 100.0));
            Rectangle fillBounds = new Rectangle(0, 0, Math.Min(Width - 1, fillWidth), Math.Max(1, Height - 1));
            using (GraphicsPath fillPath = RoundedGeometry.Path(fillBounds, radius))
            using (LinearGradientBrush fillBrush = new LinearGradientBrush(
                fillBounds, ProgressColor, ProgressEndColor, LinearGradientMode.Horizontal))
            {
                e.Graphics.FillPath(fillBrush, fillPath);
            }

            // Brillo sutil en el extremo del progreso para dar profundidad sin distraer.
            if (progress < 100 && fillWidth > 5)
            {
                using (Pen highlight = new Pen(Color.FromArgb(95, Color.White), 1F))
                    e.Graphics.DrawLine(highlight, fillWidth - 2, 3, fillWidth - 2, Height - 4);
            }
        }
    }

    internal sealed class FocusInputPanel : Panel
    {
        private bool isFocused;
        public bool IsFocused
        {
            get { return isFocused; }
            set { isFocused = value; Invalidate(); }
        }

        public FocusInputPanel()
        {
            DoubleBuffered = true;
            Padding = new Padding(1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color border = isFocused
                ? Color.FromArgb(151, 112, 245)
                : Color.FromArgb(78, 84, 112);
            using (GraphicsPath path = RoundedGeometry.Path(
                new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 10))
            using (SolidBrush fill = new SolidBrush(BackColor))
            using (Pen pen = new Pen(border, isFocused ? 1.8F : 1F))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
            base.OnPaint(e);
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        private readonly int radius;
        public RoundedPanel(int cornerRadius)
        {
            radius = cornerRadius;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width > 0 && Height > 0)
                Region = RoundedGeometry.Create(new Rectangle(0, 0, Width, Height), radius);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedGeometry.Path(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), radius))
            using (Pen pen = new Pen(Color.FromArgb(75, 83, 112), 1F))
                e.Graphics.DrawPath(pen, path);
            base.OnPaint(e);
        }
    }

    internal static class RoundedGeometry
    {
        public static GraphicsPath Path(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (bounds.Width < 2 || bounds.Height < 2) { path.AddRectangle(bounds); return path; }
            int diameter = Math.Min(Math.Max(2, radius * 2), Math.Min(bounds.Width, bounds.Height));
            if (diameter < 2) { path.AddRectangle(bounds); return path; }
            int right = bounds.Right - diameter;
            int bottom = bounds.Bottom - diameter;
            path.StartFigure();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180F, 90F);
            path.AddArc(right, bounds.Y, diameter, diameter, 270F, 90F);
            path.AddArc(right, bottom, diameter, diameter, 0F, 90F);
            path.AddArc(bounds.X, bottom, diameter, diameter, 90F, 90F);
            path.CloseFigure();
            return path;
        }
        public static Region Create(Rectangle bounds, int radius)
        {
            using (GraphicsPath path = Path(bounds, radius))
                return new Region(path);
        }
    }

}
