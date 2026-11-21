using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ONKOSFileTransfer
{
    public class WelcomeRegistrationForm : Form
    {
        private readonly TextBox _txtFullName;
        private readonly TextBox _txtArea;
        private readonly Label _lblError;
        private readonly RoundedButton _btnSave;

        public PeerInfo RegisteredProfile { get; private set; }

        public WelcomeRegistrationForm(PeerInfo existingProfile = null)
        {
            Text = existingProfile == null
                ? "ONKOS File Transfer 2.0 — Registro"
                : "ONKOS File Transfer 2.0 — Mi Perfil";

            Size = new Size(440, 315);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(14, 15, 24);
            ForeColor = Color.FromArgb(246, 247, 252);
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);

            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "onkos.ico");
                if (File.Exists(icoPath))
                    Icon = new Icon(icoPath);
            }
            catch
            {
            }

            var lblTitle = new Label
            {
                Text = existingProfile == null ? "Configura tu equipo" : "Editar mi perfil",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(184, 148, 255),
                Location = new Point(24, 18),
                Size = new Size(380, 28)
            };

            var lblSubtitle = new Label
            {
                Text = "Así aparecerás en la red local para tus compañeros.",
                ForeColor = Color.FromArgb(165, 171, 198),
                Location = new Point(24, 48),
                Size = new Size(380, 20)
            };

            var lblName = new Label
            {
                Text = "Nombre completo",
                ForeColor = Color.FromArgb(228, 232, 246),
                Location = new Point(24, 80),
                Size = new Size(380, 20)
            };

            var pnlNameBox = new RoundedPanel
            {
                Location = new Point(24, 102),
                Size = new Size(376, 32),
                CardColor = Color.FromArgb(28, 31, 48),
                BorderColor = Color.FromArgb(52, 56, 84),
                CornerRadius = 10,
                Padding = new Padding(10, 6, 10, 4)
            };

            _txtFullName = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(28, 31, 48),
                ForeColor = Color.FromArgb(246, 247, 252),
                BorderStyle = BorderStyle.None,
                Text = existingProfile != null ? (existingProfile.FullName ?? string.Empty) : string.Empty
            };
            pnlNameBox.Controls.Add(_txtFullName);

            var lblArea = new Label
            {
                Text = "Área / Departamento",
                ForeColor = Color.FromArgb(228, 232, 246),
                Location = new Point(24, 144),
                Size = new Size(380, 20)
            };

            var pnlAreaBox = new RoundedPanel
            {
                Location = new Point(24, 166),
                Size = new Size(376, 32),
                CardColor = Color.FromArgb(28, 31, 48),
                BorderColor = Color.FromArgb(52, 56, 84),
                CornerRadius = 10,
                Padding = new Padding(10, 6, 10, 4)
            };

            _txtArea = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(28, 31, 48),
                ForeColor = Color.FromArgb(246, 247, 252),
                BorderStyle = BorderStyle.None,
                Text = existingProfile != null ? (existingProfile.Area ?? string.Empty) : string.Empty
            };
            pnlAreaBox.Controls.Add(_txtArea);

            _lblError = new Label
            {
                Text = string.Empty,
                ForeColor = Color.FromArgb(255, 110, 130),
                Location = new Point(24, 204),
                Size = new Size(376, 20)
            };

            var btnCancel = new RoundedButton
            {
                Text = "Cancelar",
                Location = new Point(168, 232),
                Size = new Size(92, 34),
                FillColor = Color.FromArgb(38, 41, 62),
                HoverColor = Color.FromArgb(52, 56, 82),
                CornerRadius = 10
            };
            btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            _btnSave = new RoundedButton
            {
                Text = existingProfile == null ? "Continuar" : "Guardar",
                Location = new Point(268, 232),
                Size = new Size(132, 34),
                FillColor = Color.FromArgb(124, 77, 224),
                HoverColor = Color.FromArgb(142, 96, 242),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                CornerRadius = 10
            };
            _btnSave.Click += BtnSave_Click;

            Controls.AddRange(new Control[]
            {
                lblTitle, lblSubtitle,
                lblName, pnlNameBox,
                lblArea, pnlAreaBox,
                _lblError, btnCancel, _btnSave
            });
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            string fullName = (_txtFullName.Text ?? string.Empty).Trim();
            string area = (_txtArea.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(fullName))
            {
                _lblError.Text = "Escribe tu nombre completo.";
                _txtFullName.Focus();
                return;
            }

            if (UserProfileStore.IsIgnoredSystemName(fullName))
            {
                _lblError.Text = "Ese nombre está reservado para el sistema.";
                _txtFullName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(area))
            {
                _lblError.Text = "Escribe tu área o departamento.";
                _txtArea.Focus();
                return;
            }

            try
            {
                UserProfileStore.Save(fullName, area);
                RegisteredProfile = new PeerInfo
                {
                    FullName = fullName,
                    Area = area,
                    MachineName = Environment.MachineName,
                    IPv4 = Network.NetworkManager.GetLocalIPv4(),
                    LastSeenUtc = DateTime.UtcNow
                };
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                _lblError.Text = "Error guardando perfil: " + ex.Message;
            }
        }
    }
}
