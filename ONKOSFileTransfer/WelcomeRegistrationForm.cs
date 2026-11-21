using System;
using System.Drawing;
using System.Windows.Forms;

namespace ONKOSFileTransfer
{
    public class WelcomeRegistrationForm : Form
    {
        private readonly TextBox _txtFullName;
        private readonly TextBox _txtArea;
        private readonly Label _lblError;
        private readonly Button _btnSave;

        public PeerInfo RegisteredProfile { get; private set; }

        public WelcomeRegistrationForm(PeerInfo existingProfile = null)
        {
            Text = existingProfile == null
                ? "ONKOS File Transfer 2.0 — Registro de equipo"
                : "ONKOS File Transfer 2.0 — Mi Perfil";

            Size = new Size(460, 310);
            MinimumSize = new Size(440, 300);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(15, 16, 26);
            ForeColor = Color.FromArgb(246, 247, 252);
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);

            try
            {
                if (System.IO.File.Exists("onkos.ico"))
                    Icon = new Icon("onkos.ico");
            }
            catch
            {
            }

            var lblTitle = new Label
            {
                Text = existingProfile == null
                    ? "Bienvenido a ONKOS File Transfer"
                    : "Actualizar perfil del equipo",
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(184, 148, 255),
                Location = new Point(24, 20),
                Size = new Size(400, 30)
            };

            var lblSubtitle = new Label
            {
                Text = "Ingresa tu nombre completo y área para identificarte en la red local.",
                ForeColor = Color.FromArgb(170, 174, 196),
                Location = new Point(24, 52),
                Size = new Size(400, 22)
            };

            var lblName = new Label
            {
                Text = "Nombre completo",
                ForeColor = Color.FromArgb(230, 232, 242),
                Location = new Point(24, 86),
                Size = new Size(380, 20)
            };

            _txtFullName = new TextBox
            {
                Location = new Point(24, 108),
                Size = new Size(396, 28),
                BackColor = Color.FromArgb(28, 30, 46),
                ForeColor = Color.FromArgb(246, 247, 252),
                BorderStyle = BorderStyle.FixedSingle,
                Text = existingProfile != null ? (existingProfile.FullName ?? string.Empty) : string.Empty
            };

            var lblArea = new Label
            {
                Text = "Área / Departamento / Consultorio",
                ForeColor = Color.FromArgb(230, 232, 242),
                Location = new Point(24, 144),
                Size = new Size(380, 20)
            };

            _txtArea = new TextBox
            {
                Location = new Point(24, 166),
                Size = new Size(396, 28),
                BackColor = Color.FromArgb(28, 30, 46),
                ForeColor = Color.FromArgb(246, 247, 252),
                BorderStyle = BorderStyle.FixedSingle,
                Text = existingProfile != null ? (existingProfile.Area ?? string.Empty) : string.Empty
            };

            _lblError = new Label
            {
                Text = string.Empty,
                ForeColor = Color.FromArgb(255, 110, 130),
                Location = new Point(24, 198),
                Size = new Size(396, 20)
            };

            _btnSave = new Button
            {
                Text = existingProfile == null ? "Guardar y continuar" : "Guardar cambios",
                Location = new Point(270, 226),
                Size = new Size(150, 34),
                BackColor = Color.FromArgb(124, 77, 224),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _btnSave.FlatAppearance.BorderSize = 0;
            _btnSave.Click += BtnSave_Click;

            var btnCancel = new Button
            {
                Text = "Cancelar",
                Location = new Point(165, 226),
                Size = new Size(96, 34),
                BackColor = Color.FromArgb(38, 41, 62),
                ForeColor = Color.FromArgb(220, 223, 238),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            AcceptButton = _btnSave;
            CancelButton = btnCancel;

            Controls.AddRange(new Control[]
            {
                lblTitle, lblSubtitle,
                lblName, _txtFullName,
                lblArea, _txtArea,
                _lblError, btnCancel, _btnSave
            });
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            string fullName = (_txtFullName.Text ?? string.Empty).Trim();
            string area = (_txtArea.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(fullName))
            {
                _lblError.Text = "Por favor escribe tu nombre completo.";
                _txtFullName.Focus();
                return;
            }

            if (UserProfileStore.IsIgnoredSystemName(fullName))
            {
                _lblError.Text = "Ese nombre está reservado para el sistema. Ingresa tu nombre real.";
                _txtFullName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(area))
            {
                _lblError.Text = "Por favor escribe el área o departamento.";
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
                _lblError.Text = "No se pudo guardar el perfil: " + ex.Message;
            }
        }
    }
}
