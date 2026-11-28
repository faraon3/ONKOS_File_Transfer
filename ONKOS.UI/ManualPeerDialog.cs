using System;
using System.Collections.Generic;
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
        private readonly TextBox _txtName;
        private readonly ComboBox _cmbArea;
        private readonly TextBox _txtIp;
        private readonly CheckBox _chkSaveToBook;
        private readonly Label _lblError;

        public PeerInfo CreatedPeer { get; private set; }
        public bool SaveToAddressBook => _chkSaveToBook.Checked;

        public ManualPeerDialog()
        {
            ThemePalette p = ThemeManager.GetCurrentPalette();

            Text = "Agregar equipo manualmente";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Size = new Size(460, 355);
            MinimumSize = new Size(420, 330);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            BackColor = p.WindowBg;
            ForeColor = p.TextPrimary;
            Font = ThemeManager.BaseFont;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 9,
                Padding = new Padding(18)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var lblHeader = new Label
            {
                Text = "Agregar destinatario por dirección IPv4",
                Font = ThemeManager.SectionTitleFont,
                ForeColor = p.Mode == AppThemeMode.Dark ? Color.FromArgb(184, 148, 255) : p.PrimaryAccent,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10)
            };

            var lblIp = new Label
            {
                Text = "Dirección IPv4 del equipo destino (obligatorio):",
                AutoSize = true
            };

            _txtIp = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = p.InputBg,
                ForeColor = p.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = "Dirección IPv4 destino",
                AccessibleDescription = "Escribe la dirección IPv4 del equipo receptor, por ejemplo 192.168.1.45"
            };

            var lblName = new Label
            {
                Text = "Nombre visible del contacto o puesto (opcional):",
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0)
            };

            _txtName = new TextBox
            {
                Dock = DockStyle.Fill,
                MaxLength = UserProfileStore.MaxFieldLength,
                BackColor = p.InputBg,
                ForeColor = p.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = "Nombre del contacto",
                AccessibleDescription = "Nombre descriptivo para identificar este equipo"
            };

            var lblArea = new Label
            {
                Text = "Área o grupo (opcional):",
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0)
            };

            _cmbArea = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems,
                BackColor = p.InputBg,
                ForeColor = p.TextPrimary,
                AccessibleName = "Área o grupo del equipo"
            };
            List<string> areas = UserProfileStore.GetConfiguredAreas();
            foreach (string a in areas)
                _cmbArea.Items.Add(a);
            if (_cmbArea.Items.Count > 0)
                _cmbArea.SelectedIndex = 0;

            _chkSaveToBook = new CheckBox
            {
                Text = "Guardar en la libreta local de contactos",
                Checked = true,
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 4),
                AccessibleName = "Guardar contacto en libreta local"
            };

            _lblError = new Label
            {
                Text = string.Empty,
                ForeColor = Color.FromArgb(255, 110, 130),
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 6)
            };

            var pnlButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true
            };

            var btnAdd = new Button
            {
                Text = "Agregar",
                Size = new Size(105, 32),
                AccessibleName = "Agregar equipo"
            };
            ThemeManager.StyleButton(btnAdd, p, isPrimary: true);
            btnAdd.Click += BtnAdd_Click;

            var btnCancel = new Button
            {
                Text = "Cancelar",
                Size = new Size(105, 32),
                AccessibleName = "Cancelar"
            };
            ThemeManager.StyleButton(btnCancel, p);
            btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            pnlButtons.Controls.Add(btnAdd);
            pnlButtons.Controls.Add(btnCancel);

            layout.Controls.Add(lblHeader, 0, 0);
            layout.Controls.Add(lblIp, 0, 1);
            layout.Controls.Add(_txtIp, 0, 2);
            layout.Controls.Add(lblName, 0, 3);
            layout.Controls.Add(_txtName, 0, 4);
            layout.Controls.Add(lblArea, 0, 5);
            layout.Controls.Add(_cmbArea, 0, 6);
            layout.Controls.Add(_chkSaveToBook, 0, 7);

            var bottomWrap = new Panel { Dock = DockStyle.Fill, Height = 62 };
            _lblError.Dock = DockStyle.Top;
            pnlButtons.Dock = DockStyle.Bottom;
            bottomWrap.Controls.Add(pnlButtons);
            bottomWrap.Controls.Add(_lblError);
            layout.Controls.Add(bottomWrap, 0, 8);

            AcceptButton = btnAdd;
            CancelButton = btnCancel;
            Controls.Add(layout);
        }

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            string ipText = (_txtIp.Text ?? string.Empty).Trim();
            IPAddress parsed;
            if (!IPAddress.TryParse(ipText, out parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
            {
                _lblError.Text = "Escribe una dirección IPv4 válida (ejemplo: 192.168.1.45).";
                _txtIp.Focus();
                return;
            }

            string cleanName = UserProfileStore.SanitizeField(_txtName.Text);
            if (string.IsNullOrWhiteSpace(cleanName) || UserProfileStore.IsIgnoredSystemName(cleanName))
                cleanName = "Equipo " + parsed;

            string cleanArea = UserProfileStore.SanitizeField(_cmbArea.Text);
            if (string.IsNullOrWhiteSpace(cleanArea))
                cleanArea = "Manual";

            CreatedPeer = new PeerInfo
            {
                FullName = cleanName,
                Area = cleanArea,
                IPv4 = parsed.ToString(),
                MachineName = parsed.ToString(),
                LastSeenUtc = DateTime.UtcNow
            };

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
