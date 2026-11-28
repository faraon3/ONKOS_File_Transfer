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

        public TransferHistoryForm()
        {
            Text = "ONKOS File Transfer 2.0 — Historial y Auditoría";
            Size = new Size(980, 600);
            MinimumSize = new Size(820, 480);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(14, 15, 24);
            ForeColor = Color.FromArgb(246, 247, 252);
            Font = new Font("Segoe UI", 9.5F);

            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 56,
                BackColor = Color.FromArgb(20, 22, 34),
                Padding = new Padding(12, 12, 12, 10)
            };

            _cmbFilter = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(12, 14),
                Size = new Size(115, 28),
                BackColor = Color.FromArgb(31, 34, 52),
                ForeColor = Color.White
            };
            _cmbFilter.Items.AddRange(new object[] { "TODOS", "ENVIADO", "RECIBIDO" });
            _cmbFilter.SelectedIndex = 0;
            _cmbFilter.SelectedIndexChanged += (s, e) => LoadEntries();

            _cmbStatus = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(134, 14),
                Size = new Size(125, 28),
                BackColor = Color.FromArgb(31, 34, 52),
                ForeColor = Color.White
            };
            _cmbStatus.Items.AddRange(new object[] { "TODOS", "Completado", "Error", "Cancelado" });
            _cmbStatus.SelectedIndex = 0;
            _cmbStatus.SelectedIndexChanged += (s, e) => LoadEntries();

            _dtpFrom = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Location = new Point(266, 15),
                Size = new Size(105, 26),
                Value = DateTime.Today.AddDays(-30)
            };
            _dtpFrom.ValueChanged += (s, e) => LoadEntries();

            _dtpTo = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Location = new Point(377, 15),
                Size = new Size(105, 26),
                Value = DateTime.Today
            };
            _dtpTo.ValueChanged += (s, e) => LoadEntries();

            _txtSearch = new TextBox
            {
                Location = new Point(490, 15),
                Size = new Size(195, 26),
                BackColor = Color.FromArgb(31, 34, 52),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _txtSearch.TextChanged += (s, e) => LoadEntries();

            var btnExport = new Button
            {
                Text = "Exportar CSV",
                Location = new Point(695, 14),
                Size = new Size(115, 28),
                BackColor = Color.FromArgb(38, 41, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnExport.Click += BtnExport_Click;

            var btnClear = new Button
            {
                Text = "Limpiar historial",
                Location = new Point(818, 14),
                Size = new Size(135, 28),
                BackColor = Color.FromArgb(140, 42, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnClear.Click += BtnClear_Click;

            pnlTop.Controls.AddRange(new Control[] { _cmbFilter, _cmbStatus, _dtpFrom, _dtpTo, _txtSearch, btnExport, btnClear });

            _lvHistory = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                BackColor = Color.FromArgb(18, 20, 32),
                ForeColor = Color.FromArgb(242, 244, 252),
                BorderStyle = BorderStyle.None
            };
            _lvHistory.Columns.Add("Fecha / Hora", 145);
            _lvHistory.Columns.Add("Tipo", 90);
            _lvHistory.Columns.Add("Elemento", 220);
            _lvHistory.Columns.Add("Tamaño", 85, HorizontalAlignment.Right);
            _lvHistory.Columns.Add("Remitente / Destino", 160);
            _lvHistory.Columns.Add("Estado", 95);
            _lvHistory.Columns.Add("SHA-256 / Detalle", 240);
            _lvHistory.SelectedIndexChanged += (s, e) => ShowSelectedDetail();
            _lvHistory.DoubleClick += (s, e) => OpenSelectedTarget();

            _txtDetail = new TextBox
            {
                Dock = DockStyle.Bottom,
                Height = 86,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(22, 25, 40),
                ForeColor = Color.FromArgb(215, 222, 245),
                BorderStyle = BorderStyle.FixedSingle,
                Text = "Selecciona un registro para ver sus detalles (hash SHA-256, ruta, origen/destino y observaciones)."
            };

            var pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                BackColor = Color.FromArgb(20, 22, 34),
                Padding = new Padding(12, 8, 12, 6)
            };

            _lblSummary = new Label
            {
                Text = "0 registro(s)",
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(170, 176, 204)
            };
            pnlBottom.Controls.Add(_lblSummary);

            Controls.AddRange(new Control[] { _lvHistory, _txtDetail, pnlBottom, pnlTop });
            Shown += (s, e) => LoadEntries();
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
            _lblSummary.Text = $"{entries.Count} registro(s) — Doble clic en un recibido para abrir su ubicación";
        }

        private void ShowSelectedDetail()
        {
            if (_lvHistory.SelectedItems.Count == 0)
                return;

            var entry = _lvHistory.SelectedItems[0].Tag as TransferHistoryEntry;
            if (entry == null)
                return;

            _txtDetail.Text =
                $"Elemento: {entry.ItemName} | Tipo: {entry.Direction} | Estado: {entry.Status} | Tamaño: {entry.SizeBytes} bytes ({FormatBytes(entry.SizeBytes)})\r\n" +
                $"Contraparte: {entry.Counterparty} | Ruta destino: {entry.TargetPath}\r\n" +
                $"SHA-256: {entry.Sha256} | Observaciones: {entry.Notes}";
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
                        _lblSummary.Text = "Historial exportado correctamente a CSV.";
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
                FileLogger.Instance.Warn(
                    "UI.History",
                    $"No se pudo abrir la ubicación del elemento recibido ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        private void BtnClear_Click(object sender, EventArgs e)
        {
            DialogResult res = MessageBox.Show(
                this,
                "¿Deseas limpiar todos los registros del historial local? (Requiere privilegios de administrador)",
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
