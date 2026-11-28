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
        private readonly Timer _fadeTimer;

        public NotificationCenterForm()
        {
            ThemePalette p = ThemeManager.GetCurrentPalette();

            Text = "Avisos (visuales · sin sonido)";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Size = new Size(900, 520);
            MinimumSize = new Size(720, 420);
            StartPosition = FormStartPosition.CenterParent;
            Opacity = 0.0;
            BackColor = p.WindowBg;
            ForeColor = p.TextPrimary;
            Font = ThemeManager.BaseFont;

            var pnlTop = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 48,
                BackColor = p.HeaderBg,
                Padding = new Padding(12, 8, 12, 8)
            };

            _chkUnreadOnly = new CheckBox
            {
                Text = "Solo nuevas",
                AutoSize = true,
                Padding = new Padding(0, 5, 10, 0)
            };
            _chkUnreadOnly.CheckedChanged += (s, e) => ReloadNotifications();
            pnlTop.Controls.Add(_chkUnreadOnly);

            var btnMarkRead = new Button { Text = "Marcar leídas", Size = new Size(120, 30) };
            ThemeManager.StyleButton(btnMarkRead, p);
            btnMarkRead.Click += (s, e) =>
            {
                NotificationCenterStore.MarkAllAsRead();
                ReloadNotifications();
            };
            pnlTop.Controls.Add(btnMarkRead);

            pnlTop.Controls.Add(new Label { Text = "Segundos:", AutoSize = true, Padding = new Padding(16, 6, 4, 0) });

            _numDuration = new NumericUpDown
            {
                Width = 58,
                Minimum = 2,
                Maximum = 30,
                Value = 6,
                BackColor = p.InputBg,
                ForeColor = p.TextPrimary
            };
            pnlTop.Controls.Add(_numDuration);

            var btnSavePrefs = new Button { Text = "Guardar", Size = new Size(95, 30) };
            ThemeManager.StyleButton(btnSavePrefs, p, isPrimary: true);
            btnSavePrefs.Click += BtnSavePrefs_Click;
            pnlTop.Controls.Add(btnSavePrefs);

            var pnlPrefs = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = p.WindowBg,
                Padding = new Padding(12, 6, 12, 4)
            };

            _chkSent = new CheckBox { Text = "Envíos", AutoSize = true, Padding = new Padding(0, 0, 10, 0) };
            _chkReceived = new CheckBox { Text = "Recepciones", AutoSize = true, Padding = new Padding(0, 0, 10, 0) };
            _chkErrors = new CheckBox { Text = "Errores", AutoSize = true, Padding = new Padding(0, 0, 10, 0) };
            _chkAuth = new CheckBox { Text = "Autorización", AutoSize = true, Padding = new Padding(0, 0, 10, 0) };
            _chkSecurity = new CheckBox { Text = "Seguridad", AutoSize = true, Padding = new Padding(0, 0, 10, 0) };
            _chkService = new CheckBox { Text = "Servicio", AutoSize = true };

            pnlPrefs.Controls.AddRange(new Control[] { _chkSent, _chkReceived, _chkErrors, _chkAuth, _chkSecurity, _chkService });

            _lvNotifs = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                BackColor = p.CardBg,
                ForeColor = p.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };
            _lvNotifs.Columns.Add("Hora", 145);
            _lvNotifs.Columns.Add("Estado", 80);
            _lvNotifs.Columns.Add("Tipo", 120);
            _lvNotifs.Columns.Add("Aviso", 220);
            _lvNotifs.Columns.Add("Origen / Destino", 180);
            _lvNotifs.Columns.Add("Detalle", 240);

            _fadeTimer = new Timer { Interval = 20 };
            _fadeTimer.Tick += (s, e) =>
            {
                if (Opacity < 1.0)
                    Opacity = Math.Min(1.0, Opacity + 0.16);
                else
                    _fadeTimer.Stop();
            };

            Controls.AddRange(new Control[] { _lvNotifs, pnlPrefs, pnlTop });
            Load += OnFormLoad;
            FormClosed += (s, e) =>
            {
                _fadeTimer.Stop();
                _fadeTimer.Dispose();
            };
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
            _fadeTimer.Start();
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
