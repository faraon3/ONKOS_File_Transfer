using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ONKOS.Core.Logging;
using ONKOS.Core.Storage;

namespace ONKOS.UI
{
    public sealed class SettingsForm : Form
    {
        private readonly TextBox _txtInboxPath;
        private readonly Label _lblInfo;

        public SettingsForm()
        {
            Text = "ONKOS File Transfer 2.0 — Configuración";
            Size = new Size(560, 230);
            MinimumSize = new Size(500, 210);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(14, 15, 24);
            ForeColor = Color.FromArgb(246, 247, 252);
            Font = new Font("Segoe UI", 9.5F);

            var lblTitle = new Label
            {
                Text = "Carpeta de archivos recibidos",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(184, 148, 255),
                Location = new Point(18, 16),
                AutoSize = true
            };

            _txtInboxPath = new TextBox
            {
                Location = new Point(18, 48),
                Size = new Size(410, 26),
                BackColor = Color.FromArgb(28, 31, 48),
                ForeColor = Color.FromArgb(246, 247, 252),
                BorderStyle = BorderStyle.FixedSingle,
                Text = AppSettingsStore.GetInboxFolder()
            };

            var btnBrowse = new Button
            {
                Text = "Examinar...",
                Location = new Point(436, 47),
                Size = new Size(96, 28),
                BackColor = Color.FromArgb(38, 41, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnBrowse.Click += BtnBrowse_Click;

            _lblInfo = new Label
            {
                Text = "El servicio aplica la carpeta configurada sin necesidad de reiniciarse.",
                ForeColor = Color.FromArgb(165, 171, 198),
                Location = new Point(18, 84),
                Size = new Size(514, 36)
            };

            var btnOpenLogs = new Button
            {
                Text = "Abrir logs",
                Location = new Point(18, 138),
                Size = new Size(100, 32),
                BackColor = Color.FromArgb(38, 41, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnOpenLogs.Click += (s, e) => OpenLogsFolder();

            var btnSave = new Button
            {
                Text = "Guardar",
                Location = new Point(332, 138),
                Size = new Size(96, 32),
                BackColor = Color.FromArgb(124, 77, 224),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnSave.Click += BtnSave_Click;

            var btnCancel = new Button
            {
                Text = "Cancelar",
                Location = new Point(436, 138),
                Size = new Size(96, 32),
                BackColor = Color.FromArgb(38, 41, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            Controls.AddRange(new Control[] { lblTitle, _txtInboxPath, btnBrowse, _lblInfo, btnOpenLogs, btnSave, btnCancel });
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Selecciona la carpeta de destino para archivos recibidos";
                dlg.SelectedPath = Directory.Exists(_txtInboxPath.Text)
                    ? _txtInboxPath.Text
                    : AppSettingsStore.GetInboxFolder();

                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                    _txtInboxPath.Text = dlg.SelectedPath;
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            try
            {
                AppSettingsStore.SetInboxFolder(_txtInboxPath.Text);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("UI.Settings", "No se pudo guardar carpeta de recibidos.", ex);
                _lblInfo.ForeColor = Color.FromArgb(255, 110, 130);
                _lblInfo.Text = ex.Message;
            }
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
                FileLogger.Instance.Warn("UI.Settings", "No se pudo abrir carpeta de logs.", ex);
            }
        }
    }
}
