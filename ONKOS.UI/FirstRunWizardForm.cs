using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
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
        private readonly TextBox _txtFolder;
        private readonly Button _btnNextOrFinish;
        private readonly Button _btnBack;
        private readonly Panel _pnlStep1;
        private readonly Panel _pnlStep2;
        private readonly Timer _fadeTimer;
        private int _step;

        public PeerInfo RegisteredProfile { get; private set; }

        public FirstRunWizardForm()
        {
            ThemePalette p = ThemeManager.GetCurrentPalette();

            Text = UiResources.AppTitle;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Size = new Size(420, 270);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Opacity = ThemeManager.AreAnimationsEnabled() ? 0.0 : 1.0;
            BackColor = p.WindowBg;
            ForeColor = p.TextPrimary;
            Font = ThemeManager.BodyFont;

            _pnlStep1 = new Panel { Dock = DockStyle.Fill, BackColor = p.WindowBg };
            var lblName = new Label { Text = "Nombre", Font = ThemeManager.SecondaryFont, ForeColor = p.TextSecondary, Location = new Point(20, 24), AutoSize = true };
            _txtName = new TextBox { Location = new Point(20, 46), Size = new Size(360, 28), BackColor = p.SurfaceBg, ForeColor = p.TextPrimary, BorderStyle = BorderStyle.FixedSingle };

            var lblArea = new Label { Text = "Área", Font = ThemeManager.SecondaryFont, ForeColor = p.TextSecondary, Location = new Point(20, 86), AutoSize = true };
            _cmbArea = new ComboBox { Location = new Point(20, 108), Size = new Size(360, 28), DropDownStyle = ComboBoxStyle.DropDown, BackColor = p.SurfaceBg, ForeColor = p.TextPrimary };
            foreach (string a in UserProfileStore.GetConfiguredAreas())
                _cmbArea.Items.Add(a);
            if (_cmbArea.Items.Count > 0)
                _cmbArea.SelectedIndex = 0;

            _pnlStep1.Controls.AddRange(new Control[] { lblName, _txtName, lblArea, _cmbArea });

            _pnlStep2 = new Panel { Dock = DockStyle.Fill, BackColor = p.WindowBg, Visible = false };
            var lblFolder = new Label { Text = "Carpeta de recibidos", Font = ThemeManager.SecondaryFont, ForeColor = p.TextSecondary, Location = new Point(20, 38), AutoSize = true };
            _txtFolder = new TextBox
            {
                Text = AppSettingsStore.GetInboxFolder(),
                Location = new Point(20, 62),
                Size = new Size(272, 28),
                BackColor = p.SurfaceBg,
                ForeColor = p.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };
            var btnBrowse = new Button { Text = "Elegir", Size = new Size(80, 28), Location = new Point(300, 62) };
            ThemeManager.ConfigureSmoothButton(btnBrowse, p, ButtonVariant.Secondary, 10);
            btnBrowse.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.SelectedPath = Directory.Exists(_txtFolder.Text) ? _txtFolder.Text : AppSettingsStore.GetInboxFolder();
                    if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                        _txtFolder.Text = dlg.SelectedPath;
                }
            };
            _pnlStep2.Controls.AddRange(new Control[] { lblFolder, _txtFolder, btnBrowse });

            _btnBack = new Button { Text = "Atrás", Size = new Size(90, 34), Location = new Point(192, 182), Visible = false };
            ThemeManager.ConfigureSmoothButton(_btnBack, p, ButtonVariant.Secondary, 10);
            _btnBack.Click += (s, e) => SetStep(0);

            _btnNextOrFinish = new Button { Text = "Siguiente", Size = new Size(96, 34), Location = new Point(284, 182) };
            ThemeManager.ConfigureSmoothButton(_btnNextOrFinish, p, ButtonVariant.Primary, 10);
            _btnNextOrFinish.Click += BtnNextOrFinish_Click;

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

            AcceptButton = _btnNextOrFinish;
            Controls.AddRange(new Control[] { _pnlStep1, _pnlStep2, _btnBack, _btnNextOrFinish });
            Load += (s, e) =>
            {
                PreloadExistingProfile();
                _fadeTimer.Start();
            };
            FormClosed += (s, e) =>
            {
                _fadeTimer.Stop();
                _fadeTimer.Dispose();
            };
        }

        private void PreloadExistingProfile()
        {
            PeerInfo existing;
            if (UserProfileStore.TryLoad(out existing) && existing != null)
            {
                _txtName.Text = existing.FullName ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(existing.Area))
                    _cmbArea.Text = existing.Area;
            }
        }

        private void SetStep(int target)
        {
            _step = target;
            _pnlStep1.Visible = (_step == 0);
            _pnlStep2.Visible = (_step == 1);
            _btnBack.Visible = (_step == 1);
            _btnNextOrFinish.Text = (_step == 1) ? "Listo" : "Siguiente";
        }

        private void BtnNextOrFinish_Click(object sender, EventArgs e)
        {
            if (_step == 0)
            {
                if (string.IsNullOrWhiteSpace(UserProfileStore.SanitizeField(_txtName.Text)))
                {
                    _txtName.Focus();
                    return;
                }
                SetStep(1);
                return;
            }

            try
            {
                string area = UserProfileStore.SanitizeField(_cmbArea.Text);
                if (string.IsNullOrWhiteSpace(area))
                    area = "General";

                UserProfileStore.AddConfiguredArea(area);
                PeerInfo saved = UserProfileStore.Save(_txtName.Text, area);

                if (!string.IsNullOrWhiteSpace(_txtFolder.Text))
                    AppSettingsStore.SetInboxFolder(_txtFolder.Text.Trim());

                _ = Task.Run(() =>
                {
                    try { LanDiscoveryService.DetectActiveLanSubnets(); }
                    catch (Exception) { }
                });

                RegisteredProfile = saved;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("UI.Wizard", $"Error guardando perfil inicial ({ex.Message}).");
            }
        }
    }
}
