using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ONKOS.Core.Notifications;

namespace ONKOS.UI
{
    public sealed class NotificationCenterForm : Form
    {
        private readonly ListView _lvNotifs;
        private readonly CheckBox _chkUnreadOnly;
        private readonly NumericUpDown _numDuration;
        private readonly CheckBox _chkSent;
        private readonly CheckBox _chkReceived;
        private readonly CheckBox _chkErrors;
        private readonly CheckBox _chkAuth;
        private readonly CheckBox _chkSecurity;
        private readonly CheckBox _chkService;

        public NotificationCenterForm()
        {
            Text = "ONKOS File Transfer 2.0 — Centro de Notificaciones (visual, sin sonido)";
            Size = new Size(920, 540);
            MinimumSize = new Size(760, 440);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(14, 15, 24);
            ForeColor = Color.FromArgb(246, 247, 252);
            Font = new Font("Segoe UI", 9.5F);

            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 52,
                BackColor = Color.FromArgb(20, 22, 34),
                Padding = new Padding(12, 10, 12, 8)
            };

            _chkUnreadOnly = new CheckBox
            {
                Text = "Solo no leídas",
                Location = new Point(14, 14),
                Size = new Size(130, 24)
            };
            _chkUnreadOnly.CheckedChanged += (s, e) => ReloadNotifications();

            var btnMarkRead = new Button
            {
                Text = "Marcar todas leídas",
                Location = new Point(154, 12),
                Size = new Size(155, 28),
                BackColor = Color.FromArgb(38, 41, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnMarkRead.Click += (s, e) =>
            {
                NotificationCenterStore.MarkAllAsRead();
                ReloadNotifications();
            };

            var lblDur = new Label
            {
                Text = "Duración (s):",
                Location = new Point(330, 15),
                Size = new Size(90, 22)
            };

            _numDuration = new NumericUpDown
            {
                Location = new Point(422, 13),
                Size = new Size(60, 26),
                Minimum = 2,
                Maximum = 30,
                Value = 6,
                BackColor = Color.FromArgb(28, 31, 48),
                ForeColor = Color.White
            };

            var btnSavePrefs = new Button
            {
                Text = "Guardar avisos",
                Location = new Point(494, 12),
                Size = new Size(125, 28),
                BackColor = Color.FromArgb(124, 77, 224),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnSavePrefs.Click += BtnSavePrefs_Click;

            pnlTop.Controls.AddRange(new Control[] { _chkUnreadOnly, btnMarkRead, lblDur, _numDuration, btnSavePrefs });

            var pnlPrefs = new Panel
            {
                Dock = DockStyle.Top,
                Height = 38,
                BackColor = Color.FromArgb(18, 20, 32),
                Padding = new Padding(12, 6, 12, 6)
            };

            _chkSent = new CheckBox { Text = "Envíos", Location = new Point(14, 8), Size = new Size(85, 22) };
            _chkReceived = new CheckBox { Text = "Recepciones", Location = new Point(105, 8), Size = new Size(110, 22) };
            _chkErrors = new CheckBox { Text = "Errores", Location = new Point(220, 8), Size = new Size(85, 22) };
            _chkAuth = new CheckBox { Text = "Autorización", Location = new Point(310, 8), Size = new Size(115, 22) };
            _chkSecurity = new CheckBox { Text = "Seguridad", Location = new Point(430, 8), Size = new Size(100, 22) };
            _chkService = new CheckBox { Text = "Servicio", Location = new Point(535, 8), Size = new Size(90, 22) };

            pnlPrefs.Controls.AddRange(new Control[] { _chkSent, _chkReceived, _chkErrors, _chkAuth, _chkSecurity, _chkService });

            _lvNotifs = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                BackColor = Color.FromArgb(18, 20, 32),
                ForeColor = Color.FromArgb(242, 244, 252),
                BorderStyle = BorderStyle.None
            };
            _lvNotifs.Columns.Add("Hora", 140);
            _lvNotifs.Columns.Add("Estado", 85);
            _lvNotifs.Columns.Add("Tipo", 125);
            _lvNotifs.Columns.Add("Aviso", 220);
            _lvNotifs.Columns.Add("Remitente / Destino", 170);
            _lvNotifs.Columns.Add("Detalle", 240);

            Controls.AddRange(new Control[] { _lvNotifs, pnlPrefs, pnlTop });
            Load += OnFormLoad;
        }

        private void OnFormLoad(object sender, EventArgs e)
        {
            NotificationPreferences prefs = NotificationCenterStore.LoadPreferences();
            _numDuration.Value = Math.Max(_numDuration.Minimum, Math.Min(_numDuration.Maximum, prefs.ToastDurationSeconds));
            _chkSent.Checked = prefs.EnableSent;
            _chkReceived.Checked = prefs.EnableReceived;
            _chkErrors.Checked = prefs.EnableErrors;
            _chkAuth.Checked = prefs.EnableAuthorization;
            _chkSecurity.Checked = prefs.EnableSecurity;
            _chkService.Checked = prefs.EnableServiceStatus;

            ReloadNotifications();
        }

        private void BtnSavePrefs_Click(object sender, EventArgs e)
        {
            var prefs = new NotificationPreferences
            {
                ToastDurationSeconds = (int)_numDuration.Value,
                EnableSent = _chkSent.Checked,
                EnableReceived = _chkReceived.Checked,
                EnableErrors = _chkErrors.Checked,
                EnableAuthorization = _chkAuth.Checked,
                EnableSecurity = _chkSecurity.Checked,
                EnableServiceStatus = _chkService.Checked
            };

            NotificationCenterStore.SavePreferences(prefs);
            ReloadNotifications();
        }

        private void ReloadNotifications()
        {
            List<NotificationRecord> items = NotificationCenterStore.GetAll(_chkUnreadOnly.Checked);
            _lvNotifs.BeginUpdate();
            _lvNotifs.Items.Clear();

            foreach (NotificationRecord n in items)
            {
                var lvi = new ListViewItem(n.TimestampLocal.ToString("yyyy-MM-dd HH:mm:ss"));
                lvi.SubItems.Add(n.IsRead ? "Leída" : "Nueva");
                lvi.SubItems.Add(n.Category.ToString());
                lvi.SubItems.Add(n.Title ?? string.Empty);
                lvi.SubItems.Add(n.SenderOrTarget ?? string.Empty);
                lvi.SubItems.Add(n.Detail ?? string.Empty);
                _lvNotifs.Items.Add(lvi);
            }

            _lvNotifs.EndUpdate();
        }
    }
}
