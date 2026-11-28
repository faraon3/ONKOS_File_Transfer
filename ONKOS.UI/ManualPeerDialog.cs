using System;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;
using ONKOS.Core.Models;
using ONKOS.Core.Storage;

namespace ONKOS.UI
{
    public sealed class ManualPeerDialog : Form
    {
        private readonly TextBox _txtIp;
        private readonly Label _lblError;
        private readonly Timer _fadeTimer;

        public PeerInfo CreatedPeer { get; private set; }
        public bool SaveToAddressBook => true;

        public ManualPeerDialog()
        {
            ThemePalette p = ThemeManager.GetCurrentPalette();

            Text = "Agregar equipo";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Size = new Size(360, 190);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Opacity = ThemeManager.AreAnimationsEnabled() ? 0.0 : 1.0;
            BackColor = p.WindowBg;
            ForeColor = p.TextPrimary;
            Font = ThemeManager.BodyFont;

            var lblTitle = new Label
            {
                Text = "Dirección IPv4",
                Font = ThemeManager.SecondaryFont,
                ForeColor = p.TextSecondary,
                Location = new Point(18, 18),
                AutoSize = true
            };

            _txtIp = new TextBox
            {
                Location = new Point(18, 42),
                Size = new Size(308, 30),
                BackColor = p.SurfaceBg,
                ForeColor = p.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };

            _lblError = new Label
            {
                Text = string.Empty,
                Font = ThemeManager.SecondaryFont,
                ForeColor = p.StatusError,
                Location = new Point(18, 76),
                Size = new Size(308, 20)
            };

            var btnAdd = new Button { Text = "Agregar", Size = new Size(96, 34), Location = new Point(230, 104) };
            ThemeManager.ConfigureSmoothButton(btnAdd, p, ButtonVariant.Primary, 10);
            btnAdd.Click += BtnAdd_Click;

            var btnCancel = new Button { Text = "Cancelar", Size = new Size(88, 34), Location = new Point(134, 104) };
            ThemeManager.ConfigureSmoothButton(btnCancel, p, ButtonVariant.Secondary, 10);
            btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

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

            AcceptButton = btnAdd;
            CancelButton = btnCancel;
            Controls.AddRange(new Control[] { lblTitle, _txtIp, _lblError, btnCancel, btnAdd });
            Load += (s, e) => _fadeTimer.Start();
            FormClosed += (s, e) =>
            {
                _fadeTimer.Stop();
                _fadeTimer.Dispose();
            };
        }

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            string raw = (_txtIp.Text ?? string.Empty).Trim();
            IPAddress ip;
            if (!IPAddress.TryParse(raw, out ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            {
                _lblError.Text = "IPv4 no válida.";
                return;
            }

            string area = "General";
            try
            {
                PeerInfo local;
                if (UserProfileStore.TryLoad(out local) && !string.IsNullOrWhiteSpace(local.Area))
                    area = local.Area;
            }
            catch (Exception)
            {
            }

            CreatedPeer = new PeerInfo
            {
                FullName = "Equipo " + ip,
                Area = area,
                IPv4 = ip.ToString(),
                MachineName = ip.ToString(),
                LastSeenUtc = DateTime.UtcNow
            };

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
