using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ONKOSFileTransfer
{
    public class TransferHistoryForm : Form
    {
        private readonly ComboBox _cmbFilter;
        private readonly TextBox _txtFilter;
        private readonly ListView _lvHistory;
        private readonly Label _lblSummary;
        private readonly RoundedButton _btnOpenLocation;
        private readonly RoundedButton _btnClearHistory;

        public TransferHistoryForm()
        {
            Text = "ONKOS File Transfer 2.0 — Historial";
            Size = new Size(860, 500);
            MinimumSize = new Size(720, 400);
            StartPosition = FormStartPosition.CenterParent;
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

            Color headerBg = Color.FromArgb(20, 22, 34);
            Color cardBg = Color.FromArgb(22, 24, 38);
            Color subtleBtnBg = Color.FromArgb(34, 37, 56);
            Color subtleBtnHover = Color.FromArgb(48, 52, 76);
            Color accentPurple = Color.FromArgb(124, 77, 224);
            Color accentPurpleHover = Color.FromArgb(142, 96, 242);

            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 62,
                BackColor = headerBg,
                Padding = new Padding(16, 12, 16, 10)
            };

            var lblTitle = new Label
            {
                Text = "Historial de envíos y recepciones",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(184, 148, 255),
                Location = new Point(16, 16),
                AutoSize = true
            };

            _cmbFilter = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(31, 34, 52),
                ForeColor = Color.FromArgb(246, 247, 252),
                Font = new Font("Segoe UI", 9F),
                Location = new Point(360, 16),
                Size = new Size(130, 26)
            };
            _cmbFilter.Items.AddRange(new object[] { "TODOS", "ENVIADO", "RECIBIDO" });
            _cmbFilter.SelectedIndex = 0;

            _txtFilter = new TextBox
            {
                BackColor = Color.FromArgb(31, 34, 52),
                ForeColor = Color.FromArgb(246, 247, 252),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(502, 17),
                Size = new Size(210, 24)
            };

            var btnRefresh = new RoundedButton
            {
                Text = "Actualizar",
                Location = new Point(724, 15),
                Size = new Size(105, 28),
                ParentSurfaceColor = headerBg,
                FillColor = subtleBtnBg,
                HoverColor = subtleBtnHover,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
            };
            btnRefresh.Click += (s, e) => LoadHistory();

            pnlTop.Controls.AddRange(new Control[] { lblTitle, _cmbFilter, _txtFilter, btnRefresh });

            var pnlBody = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = BackColor,
                Padding = new Padding(14, 10, 14, 10)
            };

            var card = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                ParentSurfaceColor = BackColor,
                CardColor = cardBg,
                BorderColor = Color.FromArgb(42, 46, 70),
                CornerRadius = 14,
                Padding = new Padding(10)
            };

            _lvHistory = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(18, 20, 32),
                ForeColor = Color.FromArgb(242, 244, 252),
                Font = new Font("Segoe UI", 8.8F)
            };

            _lvHistory.Columns.Add("Fecha / Hora", 132);
            _lvHistory.Columns.Add("Tipo", 88);
            _lvHistory.Columns.Add("Archivo / Carpeta", 230);
            _lvHistory.Columns.Add("Peso", 85, HorizontalAlignment.Right);
            _lvHistory.Columns.Add("Equipo / Persona", 170);
            _lvHistory.Columns.Add("Estado", 95);

            card.Controls.Add(_lvHistory);
            pnlBody.Controls.Add(card);

            var pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = headerBg,
                Padding = new Padding(16, 10, 16, 10)
            };

            _lblSummary = new Label
            {
                Text = "0 registros",
                Font = new Font("Segoe UI", 8.8F),
                ForeColor = Color.FromArgb(170, 176, 204),
                Location = new Point(16, 15),
                AutoSize = true
            };

            _btnClearHistory = new RoundedButton
            {
                Text = "Limpiar historial",
                Location = new Point(560, 10),
                Size = new Size(125, 30),
                ParentSurfaceColor = headerBg,
                FillColor = subtleBtnBg,
                HoverColor = Color.FromArgb(185, 58, 78),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
            };

            _btnOpenLocation = new RoundedButton
            {
                Text = "Abrir carpeta",
                Location = new Point(695, 10),
                Size = new Size(134, 30),
                ParentSurfaceColor = headerBg,
                FillColor = accentPurple,
                HoverColor = accentPurpleHover,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
            };

            pnlBottom.Controls.AddRange(new Control[] { _lblSummary, _btnClearHistory, _btnOpenLocation });

            Controls.AddRange(new Control[] { pnlBody, pnlBottom, pnlTop });

            _cmbFilter.SelectedIndexChanged += (s, e) => LoadHistory();
            _txtFilter.TextChanged += (s, e) => LoadHistory();
            _btnClearHistory.Click += BtnClearHistory_Click;
            _btnOpenLocation.Click += (s, e) => OpenSelectedLocation();
            _lvHistory.DoubleClick += (s, e) => OpenSelectedLocation();

            Shown += (s, e) =>
            {
                ApplyDarkTitleBar();
                LoadHistory();
            };
        }

        private void ApplyDarkTitleBar()
        {
            try
            {
                if (!IsHandleCreated)
                    return;

                int useDark = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref useDark, sizeof(int));
            }
            catch
            {
            }
        }

        private void LoadHistory()
        {
            string dirFilter = _cmbFilter.SelectedItem != null ? _cmbFilter.SelectedItem.ToString() : "TODOS";
            List<TransferHistoryEntry> entries = TransferHistoryStore.LoadEntries(dirFilter, _txtFilter.Text);

            _lvHistory.BeginUpdate();
            _lvHistory.Items.Clear();

            foreach (TransferHistoryEntry item in entries)
            {
                var lvi = new ListViewItem(item.TimestampLocal.ToString("yyyy-MM-dd HH:mm:ss"))
                {
                    Tag = item,
                    ToolTipText = string.IsNullOrWhiteSpace(item.Notes)
                        ? item.TargetPath
                        : item.Notes
                };

                lvi.SubItems.Add(item.Direction);
                lvi.SubItems.Add(item.ItemName);
                lvi.SubItems.Add(FormatSize(item.SizeBytes));
                lvi.SubItems.Add(item.Counterparty);
                lvi.SubItems.Add(item.Status);

                if (string.Equals(item.Status, "Error", StringComparison.OrdinalIgnoreCase))
                    lvi.ForeColor = Color.FromArgb(251, 113, 133);
                else if (string.Equals(item.Direction, "RECIBIDO", StringComparison.OrdinalIgnoreCase))
                    lvi.ForeColor = Color.FromArgb(94, 234, 212);

                _lvHistory.Items.Add(lvi);
            }

            _lvHistory.EndUpdate();
            _lblSummary.Text = $"{entries.Count} registro(s) mostrado(s)";
        }

        private void OpenSelectedLocation()
        {
            if (_lvHistory.SelectedItems.Count == 0)
                return;

            if (_lvHistory.SelectedItems[0].Tag is TransferHistoryEntry entry && !string.IsNullOrWhiteSpace(entry.TargetPath))
            {
                try
                {
                    if (Directory.Exists(entry.TargetPath))
                        Process.Start("explorer.exe", entry.TargetPath);
                    else if (File.Exists(entry.TargetPath))
                        Process.Start("explorer.exe", "/select,\"" + entry.TargetPath + "\"");
                    else
                    {
                        string fallback = AppSettingsStore.GetInboxFolder();
                        if (Directory.Exists(fallback))
                            Process.Start("explorer.exe", fallback);
                    }
                }
                catch
                {
                }
            }
        }

        private void BtnClearHistory_Click(object sender, EventArgs e)
        {
            DialogResult res = MessageBox.Show(
                this,
                "¿Deseas borrar todos los registros del historial local?",
                "Confirmar limpieza",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (res == DialogResult.Yes)
            {
                TransferHistoryStore.ClearAll();
                LoadHistory();
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.#") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.#") + " MB";
            return (mb / 1024.0).ToString("0.##") + " GB";
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    }
}
