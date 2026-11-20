using System;
using System.Drawing;
using System.Windows.Forms;
using ONKOSFileTransfer.Network;

namespace ONKOSFileTransfer
{
    public class WelcomeRegistrationForm : Form
    {
        private readonly TextBox txtFullName;
        private readonly TextBox txtArea;

        public PeerInfo RegisteredProfile { get; private set; }

        public WelcomeRegistrationForm(PeerInfo existing = null)
        {
            Text = "Bienvenido a ONKOS File Transfer";
            ClientSize = new Size(460, 260);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(24, 26, 40);
            ForeColor = Color.FromArgb(246, 247, 252);
            Font = new Font("Segoe UI", 9.5F);

            Controls.Add(new Label
            {
                Text = "BIENVENIDO — REGISTRO DEL EQUIPO",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(184, 148, 255),
                Location = new Point(24, 18),
                Size = new Size(412, 28)
            });

            Controls.Add(new Label
            {
                Text = "Ingresa tus datos una sola vez para que otros equipos te ubiquen por nombre.",
                ForeColor = Color.FromArgb(152, 160, 182),
                Location = new Point(24, 48),
                Size = new Size(412, 22)
            });

            Controls.Add(new Label
            {
                Text = "NOMBRE COMPLETO",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(152, 160, 182),
                Location = new Point(24, 82),
                Size = new Size(412, 18)
            });

            txtFullName = new TextBox
            {
                Text = existing?.FullName ?? string.Empty,
                Location = new Point(24, 102),
                Size = new Size(412, 28),
                BackColor = Color.FromArgb(20, 22, 35),
                ForeColor = Color.FromArgb(246, 247, 252),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(txtFullName);

            Controls.Add(new Label
            {
                Text = "ÁREA",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(152, 160, 182),
                Location = new Point(24, 140),
                Size = new Size(412, 18)
            });

            txtArea = new TextBox
            {
                Text = existing?.Area ?? string.Empty,
                Location = new Point(24, 160),
                Size = new Size(412, 28),
                BackColor = Color.FromArgb(20, 22, 35),
                ForeColor = Color.FromArgb(246, 247, 252),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(txtArea);

            Button btnSave = new Button
            {
                Text = "Guardar y continuar",
                Location = new Point(266, 206),
                Size = new Size(170, 34),
                BackColor = Color.FromArgb(124, 77, 224),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += OnSaveClick;
            Controls.Add(btnSave);
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            string fullName = txtFullName.Text.Trim();
            string area = txtArea.Text.Trim();

            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(area))
            {
                MessageBox.Show(
                    "Ingresa tu nombre completo y tu área para continuar.",
                    "ONKOS File Transfer 2.0",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            UserProfileStore.Save(fullName, area);
            UserProfileStore.TryLoad(out PeerInfo loaded);
            RegisteredProfile = loaded ?? new PeerInfo
            {
                FullName = fullName,
                Area = area,
                MachineName = Environment.MachineName,
                IPv4 = NetworkManager.GetLocalIPv4()
            };

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
