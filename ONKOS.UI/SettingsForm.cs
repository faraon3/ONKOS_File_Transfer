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
using ONKOS.Core.Security;
using ONKOS.Core.Storage;

namespace ONKOS.UI
{
    public sealed class SettingsForm : Form
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "ONKOSFileTransfer";

        private readonly TextBox _txtInbox;
        private readonly ComboBox _cmbTheme;
        private readonly CheckBox _chkAutoStart;
        private readonly CheckBox _chkAutoAccept;
        private readonly Panel _pnlAdvanced;
        private readonly TextBox _txtDiag;
        private readonly Timer _fadeTimer;
        private bool _advExpanded;

        public SettingsForm()
        {
            ThemePalette p = ThemeManager.GetCurrentPalette();

            Text = "Configuración";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Size = new Size(480, 560);
            MinimumSize = new Size(400, 420);
            StartPosition = FormStartPosition.CenterParent;
            Opacity = ThemeManager.AreAnimationsEnabled() ? 0.0 : 1.0;
            BackColor = p.WindowBg;
            ForeColor = p.TextPrimary;
            Font = ThemeManager.BodyFont;

            var root = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(18),
                BackColor = p.WindowBg
            };

            var lblTitle = new Label
            {
                Text = "Configuración",
                Font = ThemeManager.SectionFont,
                ForeColor = p.TextPrimary,
                Location = new Point(18, 14),
                AutoSize = true
            };

            // Recibidos
            var lblInbox = new Label
            {
                Text = "Recibidos",
                Font = ThemeManager.SecondaryFont,
                ForeColor = p.TextSecondary,
                Location = new Point(18, 52),
                AutoSize = true
            };
            _txtInbox = new TextBox
            {
                Text = AppSettingsStore.GetInboxFolder(),
                Location = new Point(18, 74),
                Size = new Size(320, 28),
                BackColor = p.SurfaceBg,
                ForeColor = p.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };
            _txtInbox.Leave += (s, e) => SaveInboxSafe();

            var btnPick = new Button { Text = "Elegir", Size = new Size(88, 28), Location = new Point(346, 74) };
            ThemeManager.ConfigureSmoothButton(btnPick, p, ButtonVariant.Secondary, 10);
            btnPick.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.SelectedPath = Directory.Exists(_txtInbox.Text) ? _txtInbox.Text : AppSettingsStore.GetInboxFolder();
                    if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                    {
                        _txtInbox.Text = dlg.SelectedPath;
                        SaveInboxSafe();
                    }
                }
            };

            // Apariencia
            var lblTheme = new Label
            {
                Text = "Apariencia",
                Font = ThemeManager.SecondaryFont,
                ForeColor = p.TextSecondary,
                Location = new Point(18, 116),
                AutoSize = true
            };
            _cmbTheme = new ComboBox
            {
                Location = new Point(18, 138),
                Width = 170,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = p.SurfaceBg,
                ForeColor = p.TextPrimary
            };
            _cmbTheme.Items.AddRange(new object[] { "Claro", "Oscuro", "Automático" });
            _cmbTheme.SelectedIndex = p.RequestedMode == AppThemeMode.Dark ? 1 : (p.RequestedMode == AppThemeMode.Auto ? 2 : 0);
            _cmbTheme.SelectedIndexChanged += (s, e) =>
            {
                AppThemeMode mode = _cmbTheme.SelectedIndex == 1
                    ? AppThemeMode.Dark
                    : (_cmbTheme.SelectedIndex == 2 ? AppThemeMode.Auto : AppThemeMode.Light);
                ThemeManager.SaveMode(mode);
            };

            // Toggles (Appearance.Normal con estilo interruptor)
            _chkAutoStart = new CheckBox
            {
                Text = "Iniciar con Windows",
                Appearance = Appearance.Button,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(210, 34),
                Location = new Point(18, 184),
                Checked = IsAutoStartEnabled()
            };
            _chkAutoStart.FlatAppearance.BorderSize = 1;
            _chkAutoStart.FlatAppearance.BorderColor = p.BorderSoft;
            _chkAutoStart.CheckedChanged += (s, e) => SetAutoStartEnabled(_chkAutoStart.Checked);

            ReceiverPolicy pol = ReceiverPolicyStore.Load();
            _chkAutoAccept = new CheckBox
            {
                Text = "Autoaceptar autorizados",
                Appearance = Appearance.Button,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(210, 34),
                Location = new Point(18, 226),
                Checked = pol.AutoAcceptAuthorized
            };
            _chkAutoAccept.FlatAppearance.BorderSize = 1;
            _chkAutoAccept.FlatAppearance.BorderColor = p.BorderSoft;
            _chkAutoAccept.CheckedChanged += (s, e) =>
            {
                ReceiverPolicy cur = ReceiverPolicyStore.Load();
                cur.AutoAcceptAuthorized = _chkAutoAccept.Checked;
                ReceiverPolicyStore.Save(cur);
            };

            // Avanzado plegado
            var lnkAdv = new LinkLabel
            {
                Text = "Avanzado",
                Font = ThemeManager.SecondaryFont,
                LinkColor = p.AccentStart,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Location = new Point(18, 276),
                AutoSize = true
            };
            lnkAdv.Click += (s, e) =>
            {
                _advExpanded = !_advExpanded;
                _pnlAdvanced.Visible = _advExpanded;
                if (_advExpanded && string.IsNullOrEmpty(_txtDiag.Text))
                    _txtDiag.Text = BuildDiagnostic();
            };

            _pnlAdvanced = new Panel
            {
                Location = new Point(18, 302),
                Size = new Size(416, 195),
                Visible = false
            };

            var btnCopy = new Button { Text = "Copiar informe", Size = new Size(125, 28), Location = new Point(0, 0) };
            ThemeManager.ConfigureSmoothButton(btnCopy, p, ButtonVariant.Secondary, 8);
            btnCopy.Click += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(_txtDiag.Text))
                    Clipboard.SetText(_txtDiag.Text);
            };

            var btnLogs = new Button { Text = "Ver logs", Size = new Size(92, 28), Location = new Point(132, 0) };
            ThemeManager.ConfigureSmoothButton(btnLogs, p, ButtonVariant.Secondary, 8);
            btnLogs.Click += (s, e) =>
            {
                PathAndAclHelper.EnsureBaseAndLogsFolders();
                Process.Start("explorer.exe", FileLogger.Instance.LogsFolder);
            };

            _txtDiag = new TextBox
            {
                Location = new Point(0, 34),
                Size = new Size(416, 155),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                BackColor = p.SurfaceBg,
                ForeColor = p.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };

            _pnlAdvanced.Controls.AddRange(new Control[] { btnCopy, btnLogs, _txtDiag });
            root.Controls.AddRange(new Control[] { lblTitle, lblInbox, _txtInbox, btnPick, lblTheme, _cmbTheme, _chkAutoStart, _chkAutoAccept, lnkAdv, _pnlAdvanced });

            _fadeTimer = new Timer { Interval = 18 };
            _fadeTimer.Tick += (s, e) =>
            {
                if (!ThemeManager.AreAnimationsEnabled() || Opacity >= 1.0)
                {
                    Opacity = 1.0;
                    _fadeTimer.Stop();
                    return;
                }
                Opacity = Math.Min(1.0, Opacity + 0.16);
            };

            Controls.Add(root);
            Load += (s, e) => _fadeTimer.Start();
            FormClosed += (s, e) =>
            {
                _fadeTimer.Stop();
                _fadeTimer.Dispose();
                DialogResult = DialogResult.OK;
            };
        }

        private void SaveInboxSafe()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_txtInbox.Text))
                    AppSettingsStore.SetInboxFolder(_txtInbox.Text.Trim());
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("UI.Settings", $"Error guardando carpeta ({ex.Message}).");
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
                    if (rk == null) return;
                    if (enable)
                        rk.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                    else
                        rk.DeleteValue(RunValueName, throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("UI.Settings", $"Error actualizando inicio automático ({ex.Message}).");
            }
        }

        private static string BuildDiagnostic()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Equipo: " + Environment.MachineName + " · SO: " + Environment.OSVersion);
            try
            {
                using (var sc = new ServiceController("ONKOSTransferService"))
                    sb.AppendLine("Servicio: " + sc.Status + " · TCP 5050");
            }
            catch (Exception)
            {
                sb.AppendLine("Servicio: no disponible · TCP 5050");
            }

            foreach (SubnetInfo s in LanDiscoveryService.DetectActiveLanSubnets())
                sb.AppendLine($"Red: {s.LocalIp} / {s.SubnetMask}");

            try
            {
                sb.AppendLine("Huella SHA-256: " + DeviceIdentityStore.GetOrCreateThumbprintSha256());
            }
            catch (Exception)
            {
            }

            sb.AppendLine("Carpeta recibidos: " + AppSettingsStore.GetInboxFolder());
            sb.AppendLine("Logs: " + FileLogger.Instance.LogsFolder);
            return sb.ToString();
        }
    }
}
