using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.ServiceProcess;
using System.Windows.Forms;
using ONKOS.Core.Discovery;
using ONKOS.Core.Logging;
using ONKOS.Core.Models;
using ONKOS.Core.Storage;

namespace ONKOS.UI
{
    public sealed class FirstRunWizardForm : Form
    {
        private readonly TextBox _txtName;
        private readonly ComboBox _cmbArea;
        private readonly TextBox _txtInbox;
        private readonly Label _lblDiag;
        private readonly Label _lblError;

        public PeerInfo RegisteredProfile { get; private set; }

        public FirstRunWizardForm()
        {
            ThemePalette p = ThemeManager.GetCurrentPalette();

            Text = "ONKOS File Transfer 2.0 — Configuración inicial";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Size = new Size(560, 440);
            MinimumSize = new Size(500, 400);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = p.WindowBg;
            ForeColor = p.TextPrimary;
            Font = ThemeManager.BaseFont;

            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "onkos.ico");
                if (File.Exists(icoPath))
                    Icon = new Icon(icoPath);
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("UI.Wizard", $"No se pudo cargar icono ({ex.Message}).");
            }

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 9,
                Padding = new Padding(20)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var lblTitle = new Label
            {
                Text = "Bienvenido a ONKOS File Transfer 2.0",
                Font = ThemeManager.HeaderTitleFont,
                ForeColor = p.Mode == AppThemeMode.Dark ? Color.FromArgb(184, 148, 255) : p.PrimaryAccent,
                AutoSize = true
            };

            var lblSub = new Label
            {
                Text = "Completa estos 3 pasos rápidos para dejar listo el equipo en la red clínica:",
                ForeColor = p.TextSecondary,
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 10)
            };

            var lblStep1 = new Label { Text = "1. Tu nombre visible y área de trabajo:", Font = ThemeManager.BoldFont, AutoSize = true };

            var pnlUser = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Height = 34 };
            pnlUser.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            pnlUser.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));

            _txtName = new TextBox
            {
                Dock = DockStyle.Fill,
                MaxLength = UserProfileStore.MaxFieldLength,
                BackColor = p.InputBg,
                ForeColor = p.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = "Nombre completo del usuario"
            };

            _cmbArea = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems,
                BackColor = p.InputBg,
                ForeColor = p.TextPrimary,
                AccessibleName = "Área o departamento"
            };
            List<string> areas = UserProfileStore.GetConfiguredAreas();
            foreach (string a in areas)
                _cmbArea.Items.Add(a);
            if (_cmbArea.Items.Count > 0)
                _cmbArea.SelectedIndex = 0;

            pnlUser.Controls.Add(_txtName, 0, 0);
            pnlUser.Controls.Add(_cmbArea, 1, 0);

            var lblStep2 = new Label
            {
                Text = "2. Carpeta donde se guardarán los archivos recibidos:",
                Font = ThemeManager.BoldFont,
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0)
            };

            var pnlInbox = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Height = 34 };
            pnlInbox.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 78F));
            pnlInbox.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22F));

            _txtInbox = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = p.InputBg,
                ForeColor = p.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Text = AppSettingsStore.GetInboxFolder(),
                AccessibleName = "Ruta de carpeta de recibidos"
            };

            var btnBrowse = new Button { Text = "Examinar...", Dock = DockStyle.Fill };
            ThemeManager.StyleButton(btnBrowse, p);
            btnBrowse.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog { SelectedPath = _txtInbox.Text })
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                        _txtInbox.Text = dlg.SelectedPath;
                }
            };

            pnlInbox.Controls.Add(_txtInbox, 0, 0);
            pnlInbox.Controls.Add(btnBrowse, 1, 0);

            var lblStep3 = new Label
            {
                Text = "3. Comprobación rápida de red y servicio local:",
                Font = ThemeManager.BoldFont,
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0)
            };

            _lblDiag = new Label
            {
                Text = RunQuickCheck(),
                ForeColor = p.TextSecondary,
                AutoSize = true
            };

            _lblError = new Label
            {
                Text = string.Empty,
                ForeColor = Color.FromArgb(255, 110, 130),
                AutoSize = true
            };

            var pnlActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true
            };

            var btnFinish = new Button { Text = "Finalizar y abrir", Size = new Size(145, 34) };
            ThemeManager.StyleButton(btnFinish, p, isPrimary: true);
            btnFinish.Click += BtnFinish_Click;

            var btnCancel = new Button { Text = "Cancelar", Size = new Size(100, 34) };
            ThemeManager.StyleButton(btnCancel, p);
            btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            pnlActions.Controls.Add(btnFinish);
            pnlActions.Controls.Add(btnCancel);

            layout.Controls.Add(lblTitle, 0, 0);
            layout.Controls.Add(lblSub, 0, 1);
            layout.Controls.Add(lblStep1, 0, 2);
            layout.Controls.Add(pnlUser, 0, 3);
            layout.Controls.Add(lblStep2, 0, 4);
            layout.Controls.Add(pnlInbox, 0, 5);
            layout.Controls.Add(lblStep3, 0, 6);
            layout.Controls.Add(_lblDiag, 0, 7);

            var footer = new Panel { Dock = DockStyle.Fill, Height = 64 };
            _lblError.Dock = DockStyle.Top;
            pnlActions.Dock = DockStyle.Bottom;
            footer.Controls.Add(pnlActions);
            footer.Controls.Add(_lblError);
            layout.Controls.Add(footer, 0, 8);

            AcceptButton = btnFinish;
            CancelButton = btnCancel;
            Controls.Add(layout);
        }

        private static string RunQuickCheck()
        {
            string svcState = "No detectado";
            try
            {
                using (var sc = new ServiceController("ONKOSTransferService"))
                {
                    svcState = sc.Status.ToString();
                }
            }
            catch (Exception)
            {
            }

            List<SubnetInfo> nets = LanDiscoveryService.DetectActiveLanSubnets();
            string ipInfo = nets.Count > 0
                ? $"{nets[0].LocalIp} (máscara {nets[0].SubnetMask})"
                : "Sin adaptador LAN activo detectado";

            return $"Servicio de recepción: {svcState}  •  Red LAN: {ipInfo}";
        }

        private void BtnFinish_Click(object sender, EventArgs e)
        {
            string name = UserProfileStore.SanitizeField(_txtName.Text);
            string area = UserProfileStore.SanitizeField(_cmbArea.Text);

            if (string.IsNullOrWhiteSpace(name) || UserProfileStore.IsIgnoredSystemName(name))
            {
                _lblError.Text = "Escribe tu nombre completo para identificarte en la red.";
                _txtName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(area))
            {
                _lblError.Text = "Selecciona o escribe tu área.";
                _cmbArea.Focus();
                return;
            }

            try
            {
                AppSettingsStore.SetInboxFolder(_txtInbox.Text);
                UserProfileStore.Save(name, area);

                RegisteredProfile = new PeerInfo
                {
                    FullName = name,
                    Area = area,
                    MachineName = Environment.MachineName,
                    IPv4 = new LanDiscoveryService().GetLocalIPv4Cached(),
                    LastSeenUtc = DateTime.UtcNow
                };

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Error("UI.Wizard", "Error completando asistente inicial.", ex);
                _lblError.Text = "No se pudo guardar la configuración inicial. Revisa los permisos de la carpeta.";
            }
        }
    }
}
