using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;
using ONKOS.Core.Logging;
using ONKOS.Core.Models;
using ONKOS.Core.Storage;

namespace ONKOS.UI
{
    public sealed class WelcomeRegistrationForm : Form
    {
        private readonly TextBox _txtFullName;
        private readonly TextBox _txtArea;
        private readonly Label _lblError;

        public PeerInfo RegisteredProfile { get; private set; }

        public WelcomeRegistrationForm(PeerInfo existingProfile = null)
        {
            Text = existingProfile == null
                ? "ONKOS File Transfer 2.0 — Registro"
                : "ONKOS File Transfer 2.0 — Mi Perfil";

            Size = new Size(440, 300);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(14, 15, 24);
            ForeColor = Color.FromArgb(246, 247, 252);
            Font = new Font("Segoe UI", 9.5F);

            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "onkos.ico");
                if (File.Exists(icoPath))
                    Icon = new Icon(icoPath);
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn(
                    "UI.Welcome",
                    $"No se pudo cargar icono en WelcomeRegistrationForm ({ex.GetType().Name}: {ex.Message}).");
            }

            var lblTitle = new Label
            {
                Text = existingProfile == null ? "Configura tu equipo" : "Editar mi perfil",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(184, 148, 255),
                Location = new Point(24, 18),
                AutoSize = true
            };

            var lblName = new Label
            {
                Text = "Nombre completo",
                Location = new Point(24, 62),
                Size = new Size(376, 20)
            };

            _txtFullName = new TextBox
            {
                Location = new Point(24, 86),
                Size = new Size(376, 26),
                BackColor = Color.FromArgb(28, 31, 48),
                ForeColor = Color.FromArgb(246, 247, 252),
                BorderStyle = BorderStyle.FixedSingle,
                Text = existingProfile != null ? (existingProfile.FullName ?? string.Empty) : string.Empty
            };

            var lblArea = new Label
            {
                Text = "Área / Departamento",
                Location = new Point(24, 124),
                Size = new Size(376, 20)
            };

            _txtArea = new TextBox
            {
                Location = new Point(24, 148),
                Size = new Size(376, 26),
                BackColor = Color.FromArgb(28, 31, 48),
                ForeColor = Color.FromArgb(246, 247, 252),
                BorderStyle = BorderStyle.FixedSingle,
                Text = existingProfile != null ? (existingProfile.Area ?? string.Empty) : string.Empty
            };

            _lblError = new Label
            {
                Text = string.Empty,
                ForeColor = Color.FromArgb(255, 110, 130),
                Location = new Point(24, 182),
                Size = new Size(376, 24)
            };

            var btnCancel = new Button
            {
                Text = "Cancelar",
                Location = new Point(198, 216),
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

            var btnSave = new Button
            {
                Text = "Guardar",
                Location = new Point(304, 216),
                Size = new Size(96, 32),
                BackColor = Color.FromArgb(124, 77, 224),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnSave.Click += BtnSave_Click;

            AcceptButton = btnSave;
            CancelButton = btnCancel;
            Controls.AddRange(new Control[] { lblTitle, lblName, _txtFullName, lblArea, _txtArea, _lblError, btnCancel, btnSave });
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            string fullName = UserProfileStore.SanitizeField(_txtFullName.Text);
            string area = UserProfileStore.SanitizeField(_txtArea.Text);

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
                    IPv4 = GetLocalIPv4Safe(),
                    LastSeenUtc = DateTime.UtcNow
                };
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Error("UI.Welcome", "Error guardando perfil de usuario.", ex);
                _lblError.Text = "No se pudo guardar el perfil. Revisa los logs.";
            }
        }

        private static string GetLocalIPv4Safe()
        {
            try
            {
                foreach (IPAddress addr in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                {
                    if (addr.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr))
                        return addr.ToString();
                }
            }
            catch (SocketException)
            {
            }

            return "127.0.0.1";
        }
    }
}
