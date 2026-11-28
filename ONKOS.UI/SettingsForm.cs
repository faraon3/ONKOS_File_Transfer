using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.ServiceProcess;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using ONKOS.Core.Discovery;
using ONKOS.Core.Logging;
using ONKOS.Core.Protocol;
using ONKOS.Core.Security;
using ONKOS.Core.Storage;

namespace ONKOS.UI
{
    public sealed class SettingsForm : Form
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "ONKOSFileTransfer";

        private readonly TextBox _txtInboxPath;
        private readonly ComboBox _cmbTheme;
        private readonly CheckBox _chkAutoStart;
        private readonly CheckBox _chkAutoAccept;
        private readonly TextBox _txtNewArea;
        private readonly TextBox _txtDiagnostic;
        private readonly Label _lblFeedback;

        public AppThemeMode SelectedTheme { get; private set; }

        public SettingsForm()
        {
            ThemePalette p = ThemeManager.GetCurrentPalette();
            SelectedTheme = p.Mode;

            Text = "ONKOS File Transfer 2.0 — Configuración y Diagnóstico";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Size = new Size(760, 620);
            MinimumSize = new Size(680, 540);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = p.WindowBg;
            ForeColor = p.TextPrimary;
            Font = ThemeManager.BaseFont;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                Padding = new Padding(16)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var lblTitle = new Label
            {
                Text = "Configuración del equipo, políticas y diagnóstico",
                Font = ThemeManager.HeaderTitleFont,
                ForeColor = p.Mode == AppThemeMode.Dark ? Color.FromArgb(184, 148, 255) : p.PrimaryAccent,
                AutoSize = true
            };

            var pnlInbox = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Height = 34 };
            pnlInbox.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165F));
            pnlInbox.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            pnlInbox.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));

            pnlInbox.Controls.Add(new Label { Text = "Carpeta recibidos:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            _txtInboxPath = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = p.InputBg,
                ForeColor = p.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Text = AppSettingsStore.GetInboxFolder(),
                AccessibleName = "Carpeta de archivos recibidos"
            };
            pnlInbox.Controls.Add(_txtInboxPath, 1, 0);

            var btnBrowse = new Button { Text = "Examinar...", Dock = DockStyle.Fill };
            ThemeManager.StyleButton(btnBrowse, p);
            btnBrowse.Click += BtnBrowse_Click;
            pnlInbox.Controls.Add(btnBrowse, 2, 0);

            var pnlOptions = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 36, WrapContents = true };

            pnlOptions.Controls.Add(new Label { Text = "Tema:", AutoSize = true, Padding = new Padding(0, 5, 4, 0) });
            _cmbTheme = new ComboBox
            {
                Width = 140,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = p.InputBg,
                ForeColor = p.TextPrimary,
                AccessibleName = "Selector de tema visual"
            };
            _cmbTheme.Items.AddRange(new object[] { "Dark", "Light", "HighContrast" });
            _cmbTheme.SelectedItem = p.Mode.ToString();
            pnlOptions.Controls.Add(_cmbTheme);

            _chkAutoStart = new CheckBox
            {
                Text = "Iniciar con Windows",
                Checked = IsAutoStartEnabled(),
                AutoSize = true,
                Padding = new Padding(12, 4, 0, 0),
                AccessibleName = "Iniciar aplicación automáticamente con Windows"
            };
            pnlOptions.Controls.Add(_chkAutoStart);

            ReceiverPolicy policy = ReceiverPolicyStore.Load();
            _chkAutoAccept = new CheckBox
            {
                Text = "Autoaceptar equipos autorizados",
                Checked = policy.AutoAcceptAuthorized,
                AutoSize = true,
                Padding = new Padding(12, 4, 0, 0),
                AccessibleName = "Autoaceptar transferencias de equipos autorizados"
            };
            pnlOptions.Controls.Add(_chkAutoAccept);

            var pnlAreas = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Height = 34 };
            pnlAreas.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165F));
            pnlAreas.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            pnlAreas.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140F));

            pnlAreas.Controls.Add(new Label { Text = "Agregar área/grupo:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            _txtNewArea = new TextBox
            {
                Dock = DockStyle.Fill,
                MaxLength = UserProfileStore.MaxFieldLength,
                BackColor = p.InputBg,
                ForeColor = p.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = "Nueva área o departamento"
            };
            pnlAreas.Controls.Add(_txtNewArea, 1, 0);

            var btnAddArea = new Button { Text = "Guardar área", Dock = DockStyle.Fill };
            ThemeManager.StyleButton(btnAddArea, p);
            btnAddArea.Click += BtnAddArea_Click;
            pnlAreas.Controls.Add(btnAddArea, 2, 0);

            var pnlDiagHeader = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 34 };
            pnlDiagHeader.Controls.Add(new Label
            {
                Text = "Diagnóstico del sistema y red:",
                Font = ThemeManager.BoldFont,
                AutoSize = true,
                Padding = new Padding(0, 6, 12, 0)
            });

            var btnRefreshDiag = new Button { Text = "Actualizar diagnóstico", Size = new Size(165, 28) };
            ThemeManager.StyleButton(btnRefreshDiag, p);
            btnRefreshDiag.Click += (s, e) => _txtDiagnostic.Text = BuildDiagnosticReport(_txtInboxPath.Text);
            pnlDiagHeader.Controls.Add(btnRefreshDiag);

            var btnCopyDiag = new Button { Text = "Copiar informe", Size = new Size(130, 28) };
            ThemeManager.StyleButton(btnCopyDiag, p);
            btnCopyDiag.Click += BtnCopyDiag_Click;
            pnlDiagHeader.Controls.Add(btnCopyDiag);

            var btnOpenLogs = new Button { Text = "Abrir carpeta logs", Size = new Size(140, 28) };
            ThemeManager.StyleButton(btnOpenLogs, p);
            btnOpenLogs.Click += (s, e) => OpenLogsFolder();
            pnlDiagHeader.Controls.Add(btnOpenLogs);

            _txtDiagnostic = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                BackColor = p.CardBg,
                ForeColor = p.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = "Informe de diagnóstico del sistema"
            };

            var pnlFooter = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Height = 54 };
            pnlFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            pnlFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));

            var limits = new TransferLimits();
            _lblFeedback = new Label
            {
                Text = $"Límites activos: máx. {limits.MaxSingleFileBytes / (1024L * 1024L * 1024L)} GB/archivo, {limits.MaxFileCount} elementos/envío.",
                ForeColor = p.TextSecondary,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlFooter.Controls.Add(_lblFeedback, 0, 0);

            var flowSave = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var btnSave = new Button { Text = "Guardar", Size = new Size(105, 34) };
            ThemeManager.StyleButton(btnSave, p, isPrimary: true);
            btnSave.Click += BtnSave_Click;

            var btnCancel = new Button { Text = "Cancelar", Size = new Size(105, 34) };
            ThemeManager.StyleButton(btnCancel, p);
            btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            flowSave.Controls.Add(btnSave);
            flowSave.Controls.Add(btnCancel);
            pnlFooter.Controls.Add(flowSave, 1, 0);

            root.Controls.Add(lblTitle, 0, 0);
            root.Controls.Add(pnlInbox, 0, 1);
            root.Controls.Add(pnlOptions, 0, 2);
            root.Controls.Add(pnlAreas, 0, 3);
            root.Controls.Add(pnlDiagHeader, 0, 4);
            root.Controls.Add(_txtDiagnostic, 0, 5);
            root.Controls.Add(pnlFooter, 0, 6);

            AcceptButton = btnSave;
            CancelButton = btnCancel;
            Controls.Add(root);
            Load += (s, e) => _txtDiagnostic.Text = BuildDiagnosticReport(_txtInboxPath.Text);
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Selecciona la carpeta de destino para archivos recibidos";
                dlg.SelectedPath = Directory.Exists(_txtInboxPath.Text) ? _txtInboxPath.Text : AppSettingsStore.GetInboxFolder();
                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                    _txtInboxPath.Text = dlg.SelectedPath;
            }
        }

        private void BtnAddArea_Click(object sender, EventArgs e)
        {
            string clean = UserProfileStore.SanitizeField(_txtNewArea.Text);
            if (string.IsNullOrWhiteSpace(clean))
            {
                _lblFeedback.Text = "Escribe un nombre de área válido.";
                return;
            }

            UserProfileStore.AddConfiguredArea(clean);
            _txtNewArea.Clear();
            _lblFeedback.Text = $"Área '{clean}' guardada.";
        }

        private void BtnCopyDiag_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_txtDiagnostic.Text))
            {
                Clipboard.SetText(_txtDiagnostic.Text);
                _lblFeedback.Text = "Informe de diagnóstico copiado al portapapeles.";
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            try
            {
                AppSettingsStore.SetInboxFolder(_txtInboxPath.Text);

                ReceiverPolicy policy = ReceiverPolicyStore.Load();
                policy.AutoAcceptAuthorized = _chkAutoAccept.Checked;
                ReceiverPolicyStore.Save(policy);

                SetAutoStartEnabled(_chkAutoStart.Checked);

                if (_cmbTheme.SelectedItem != null &&
                    Enum.TryParse(_cmbTheme.SelectedItem.ToString(), ignoreCase: true, out AppThemeMode chosenTheme))
                {
                    ThemeManager.SaveMode(chosenTheme);
                    SelectedTheme = chosenTheme;
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("UI.Settings", $"Error guardando configuración ({ex.Message}).");
                _lblFeedback.Text = "No se pudo guardar la configuración. Revisa los permisos de la carpeta.";
            }
        }

        private static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false))
                {
                    object val = rk?.GetValue(RunValueName);
                    return val != null && !string.IsNullOrWhiteSpace(val.ToString());
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void SetAutoStartEnabled(bool enable)
        {
            try
            {
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
                {
                    if (rk == null)
                        return;

                    if (enable)
                        rk.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                    else
                        rk.DeleteValue(RunValueName, throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("UI.Settings", $"No se pudo actualizar inicio automático ({ex.Message}).");
            }
        }

        private static string BuildDiagnosticReport(string inboxCandidate)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== DIAGNÓSTICO ONKOS FILE TRANSFER 2.0 ===");
            sb.AppendLine("Fecha local: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Equipo: " + Environment.MachineName + " | SO: " + Environment.OSVersion);

            string svcStatus = "No instalado / Sin acceso";
            try
            {
                using (var sc = new ServiceController("ONKOSTransferService"))
                {
                    svcStatus = sc.Status.ToString();
                }
            }
            catch (Exception ex)
            {
                svcStatus = "Error (" + ex.Message + ")";
            }
            sb.AppendLine("Servicio ONKOSTransferService: " + svcStatus);
            sb.AppendLine("Puerto de transferencia cifrada (TLS 1.2+): TCP 5050");

            List<SubnetInfo> subnets = LanDiscoveryService.DetectActiveLanSubnets();
            if (subnets.Count > 0)
            {
                foreach (SubnetInfo s in subnets)
                    sb.AppendLine($"Adaptador LAN: IP {s.LocalIp} | Máscara {s.SubnetMask} | Broadcast {s.BroadcastAddress}");
            }
            else
            {
                sb.AppendLine("Adaptador LAN: No se detectaron interfaces activas no virtuales.");
            }

            string thumb = "No disponible";
            try
            {
                thumb = DeviceIdentityStore.GetOrCreateThumbprintSha256();
            }
            catch (Exception ex)
            {
                thumb = "Error (" + ex.Message + ")";
            }
            sb.AppendLine("Huella SHA-256 identidad local: " + thumb);

            string folder = string.IsNullOrWhiteSpace(inboxCandidate) ? AppSettingsStore.GetInboxFolder() : inboxCandidate.Trim();
            sb.AppendLine("Carpeta de recibidos: " + folder);
            string spaceReason;
            bool okSpace = PathAndAclHelper.HasEnoughFreeSpace(folder, 0L, out spaceReason);
            sb.AppendLine("Espacio libre suficiente: " + (okSpace ? "Sí" : "No — " + spaceReason));
            sb.AppendLine("Carpeta de logs: " + FileLogger.Instance.LogsFolder);

            return sb.ToString();
        }

        private static void OpenLogsFolder()
        {
            try
            {
                PathAndAclHelper.EnsureBaseAndLogsFolders();
                Process.Start("explorer.exe", FileLogger.Instance.LogsFolder);
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("UI.Settings", $"No se pudo abrir logs ({ex.Message}).");
            }
        }
    }
}
