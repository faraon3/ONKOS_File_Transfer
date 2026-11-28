using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ONKOS.Core.Logging;
using ONKOS.Core.Models;
using ONKOS.Core.Storage;

namespace ONKOS.UI
{
    public sealed class TransferHistoryForm : Form
    {
        private readonly ComboBox _cmbFilter;
        private readonly ComboBox _cmbStatus;
        private readonly DateTimePicker _dtpFrom;
        private readonly DateTimePicker _dtpTo;
        private readonly TextBox _txtSearch;
        private readonly ListView _lvHistory;
        private readonly TextBox _txtDetail;
        private readonly Label _lblSummary;
        private readonly Timer _fadeTimer;

        public TransferHistoryForm()
        {
            ThemePalette p = ThemeManager.GetCurrentPalette();

            Text = "Historial";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Size = new Size(960, 580);
            MinimumSize = new Size(780, 460);
            StartPosition = FormStartPosition.CenterParent;
            Opacity = 0.0;
            BackColor = p.WindowBg;
            ForeColor = p.TextPrimary;
            Font = ThemeManager.BaseFont;

            var pnlTop = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = p.HeaderBg,
                Padding = new Padding(12, 9, 12, 8)
            };

            _cmbFilter = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 105,
                BackColor = p.InputBg,
                ForeColor = p.TextPrimary
            };
            _cmbFilter.Items.AddRange(new object[] { "TODOS", "ENVIADO", "RECIBIDO" });
            _cmbFilter.SelectedIndex = 0;
            _cmbFilter.SelectedIndexChanged += (s, e) => LoadEntries();
            pnlTop.Controls.Add(_cmbFilter);

            _cmbStatus = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 120,
                BackColor = p.InputBg,
                ForeColor = p.TextPrimary
            };
            _cmbStatus.Items.AddRange(new object[] { "TODOS", "Completado", "Error", "Cancelado" });
            _cmbStatus.SelectedIndex = 0;
            _cmbStatus.SelectedIndexChanged += (s, e) => LoadEntries();
            pnlTop.Controls.Add(_cmbStatus);

            _dtpFrom = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 102, Value = DateTime.Today.AddDays(-30) };
            _dtpFrom.ValueChanged += (s, e) => LoadEntries();
            pnlTop.Controls.Add(_dtpFrom);

            _dtpTo = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 102, Value = DateTime.Today };
            _dtpTo.ValueChanged += (s, e) => LoadEntries();
            pnlTop.Controls.Add(_dtpTo);

            _txtSearch = new TextBox
            {
                Width = 185,
                BackColor = p.InputBg,
                ForeColor = p.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = "Buscar en historial"
            };
            _txtSearch.TextChanged += (s, e) => LoadEntries();
            pnlTop.Controls.Add(_txtSearch);

            var btnExport = new Button { Text = "CSV", Size = new Size(72, 30) };
            ThemeManager.StyleButton(btnExport, p);
            btnExport.Click += BtnExport_Click;
            pnlTop.Controls.Add(btnExport);

            var btnClear = new Button { Text = "Limpiar", Size = new Size(86, 30) };
            ThemeManager.StyleButton(btnClear, p, isDanger: true);
            btnClear.Click += BtnClear_Click;
            pnlTop.Controls.Add(btnClear);

            _lvHistory = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                BackColor = p.CardBg,
                ForeColor = p.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };
            _lvHistory.Columns.Add("Fecha", 145);
            _lvHistory.Columns.Add("Tipo", 88);
            _lvHistory.Columns.Add("Elemento", 230);
            _lvHistory.Columns.Add("Tamaño", 85, HorizontalAlignment.Right);
            _lvHistory.Columns.Add("Contacto", 170);
            _lvHistory.Columns.Add("Estado", 95);
            _lvHistory.Columns.Add("SHA-256 / Nota", 230);
            _lvHistory.SelectedIndexChanged += (s, e) => ShowSelectedDetail();
            _lvHistory.DoubleClick += (s, e) => OpenSelectedTarget();

            _txtDetail = new TextBox
            {
                Dock = DockStyle.Bottom,
                Height = 76,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = p.InputBg,
                ForeColor = p.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Text = "Selecciona un registro para ver detalles."
            };

            var pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 34,
                BackColor = p.HeaderBg,
                Padding = new Padding(12, 7, 12, 6)
            };

            _lblSummary = new Label
            {
                Text = "0 registros",
                Dock = DockStyle.Fill,
                ForeColor = p.TextSecondary
            };
            pnlBottom.Controls.Add(_lblSummary);

            _fadeTimer = new Timer { Interval = 20 };
            _fadeTimer.Tick += (s, e) =>
            {
                if (Opacity < 1.0)
                    Opacity = Math.Min(1.0, Opacity + 0.16);
                else
                    _fadeTimer.Stop();
            };

            Controls.AddRange(new Control[] { _lvHistory, _txtDetail, pnlBottom, pnlTop });
            Shown += (s, e) =>
            {
                LoadEntries();
                _fadeTimer.Start();
            };
            FormClosed += (s, e) =>
            {
                _fadeTimer.Stop();
                _fadeTimer.Dispose();
            };
        }

        private void LoadEntries()
        {
            string dirFilter = _cmbFilter.SelectedItem != null ? _cmbFilter.SelectedItem.ToString() : "TODOS";
            string stFilter = _cmbStatus.SelectedItem != null ? _cmbStatus.SelectedItem.ToString() : "TODOS";

            List<TransferHistoryEntry> entries = TransferHistoryStore.LoadEntries(
                dirFilter,
                _txtSearch.Text,
                stFilter,
                _dtpFrom.Value.Date,
                _dtpTo.Value.Date);

            _lvHistory.BeginUpdate();
            _lvHistory.Items.Clear();

            foreach (TransferHistoryEntry item in entries)
            {
                var lvi = new ListViewItem(item.TimestampLocal.ToString("yyyy-MM-dd HH:mm:ss"))
                {
                    Tag = item,
                    ToolTipText = string.IsNullOrWhiteSpace(item.Notes) ? item.TargetPath : item.Notes
                };
                lvi.SubItems.Add(item.Direction ?? string.Empty);
                lvi.SubItems.Add(item.ItemName ?? string.Empty);
                lvi.SubItems.Add(FormatBytes(item.SizeBytes));
                lvi.SubItems.Add(item.Counterparty ?? string.Empty);
                lvi.SubItems.Add(item.Status ?? string.Empty);
                lvi.SubItems.Add(!string.IsNullOrWhiteSpace(item.Sha256) ? item.Sha256 : (item.Notes ?? string.Empty));
                _lvHistory.Items.Add(lvi);
            }

            _lvHistory.EndUpdate();
            _lblSummary.Text = $"{entries.Count} registro(s) · Doble clic en recibido abre su ubicación";
        }

        private void ShowSelectedDetail()
        {
            if (_lvHistory.SelectedItems.Count == 0)
                return;

            var entry = _lvHistory.SelectedItems[0].Tag as TransferHistoryEntry;
            if (entry == null)
                return;

            _txtDetail.Text =
                $"{entry.ItemName} · {entry.Direction} · {entry.Status} · {FormatBytes(entry.SizeBytes)}\r\n" +
                $"Contacto: {entry.Counterparty} · Destino: {entry.TargetPath}\r\n" +
                $"SHA-256: {entry.Sha256} · {entry.Notes}";
        }

        private void BtnExport_Click(object sender, EventArgs e)
        {
            try
            {
                string dirFilter = _cmbFilter.SelectedItem != null ? _cmbFilter.SelectedItem.ToString() : "TODOS";
                string stFilter = _cmbStatus.SelectedItem != null ? _cmbStatus.SelectedItem.ToString() : "TODOS";
                List<TransferHistoryEntry> entries = TransferHistoryStore.LoadEntries(
                    dirFilter,
                    _txtSearch.Text,
                    stFilter,
                    _dtpFrom.Value.Date,
                    _dtpTo.Value.Date);

                using (var sfd = new SaveFileDialog
                {
                    Filter = "Archivo CSV (*.csv)|*.csv",
                    FileName = $"historial_onkos_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
                })
                {
                    if (sfd.ShowDialog(this) == DialogResult.OK)
                    {
                        TransferHistoryStore.ExportToCsv(sfd.FileName, entries);
                        _lblSummary.Text = "Exportado a CSV.";
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("UI.History", $"Error exportando historial ({ex.Message}).");
                _lblSummary.Text = ex.Message;
            }
        }

        private void OpenSelectedTarget()
        {
            if (_lvHistory.SelectedItems.Count == 0)
                return;

            var entry = _lvHistory.SelectedItems[0].Tag as TransferHistoryEntry;
            if (entry == null || !string.Equals(entry.Direction, "RECIBIDO", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                if (!string.IsNullOrWhiteSpace(entry.TargetPath))
                {
                    if (Directory.Exists(entry.TargetPath))
                    {
                        Process.Start("explorer.exe", entry.TargetPath);
                        return;
                    }
                    if (File.Exists(entry.TargetPath))
                    {
                        Process.Start("explorer.exe", "/select,\"" + entry.TargetPath + "\"");
                        return;
                    }
                }

                string fallback = AppSettingsStore.GetInboxFolder();
                if (Directory.Exists(fallback))
                    Process.Start("explorer.exe", fallback);
            }
            catch (Exception ex)
            {
                FileLogger.Instance.Warn("UI.History", $"No se pudo abrir ubicación ({ex.Message}).");
            }
        }

        private void BtnClear_Click(object sender, EventArgs e)
        {
            DialogResult res = MessageBox.Show(
                this,
                "¿Limpiar el historial local? (Requiere administrador)",
                "Confirmar",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (res == DialogResult.Yes)
            {
                try
                {
                    TransferHistoryStore.ClearAll();
                    LoadEntries();
                }
                catch (Exception ex)
                {
                    FileLogger.Instance.Warn("UI.History", $"No se pudo limpiar historial ({ex.Message}).");
                    _lblSummary.Text = ex.Message;
                }
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024L) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024.0) return kb.ToString("0.#") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024.0) return mb.ToString("0.#") + " MB";
            return (mb / 1024.0).ToString("0.##") + " GB";
        }
    }
}
