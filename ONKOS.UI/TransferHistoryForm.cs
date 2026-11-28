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
        private readonly TextBox _txtSearch;
        private readonly ListView _lvHistory;
        private readonly Label _lblSummary;

        public TransferHistoryForm()
        {
            Text = "ONKOS File Transfer 2.0 — Historial";
            Size = new Size(920, 500);
            MinimumSize = new Size(760, 400);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(14, 15, 24);
            ForeColor = Color.FromArgb(246, 247, 252);
            Font = new Font("Segoe UI", 9.5F);

            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 56,
                BackColor = Color.FromArgb(20, 22, 34),
                Padding = new Padding(14, 12, 14, 10)
            };

            _cmbFilter = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(14, 14),
                Size = new Size(130, 28),
                BackColor = Color.FromArgb(31, 34, 52),
                ForeColor = Color.White
            };
            _cmbFilter.Items.AddRange(new object[] { "TODOS", "ENVIADO", "RECIBIDO" });
            _cmbFilter.SelectedIndex = 0;
            _cmbFilter.SelectedIndexChanged += (s, e) => LoadEntries();

            _txtSearch = new TextBox
            {
                Location = new Point(156, 15),
                Size = new Size(250, 26),
                BackColor = Color.FromArgb(31, 34, 52),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _txtSearch.TextChanged += (s, e) => LoadEntries();

            var btnRefresh = new Button
            {
                Text = "Actualizar",
                Location = new Point(418, 14),
                Size = new Size(100, 28),
                BackColor = Color.FromArgb(38, 41, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnRefresh.Click += (s, e) => LoadEntries();

            var btnClear = new Button
            {
                Text = "Limpiar historial",
                Location = new Point(775, 14),
                Size = new Size(120, 28),
                BackColor = Color.FromArgb(140, 42, 62),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnClear.Click += BtnClear_Click;

            pnlTop.Controls.AddRange(new Control[] { _cmbFilter, _txtSearch, btnRefresh, btnClear });

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
            _lvHistory.Columns.Add("Remitente / Destino", 150);
            _lvHistory.Columns.Add("Estado", 95);
            _lvHistory.Columns.Add("SHA-256 / Detalle", 220);
            _lvHistory.DoubleClick += (s, e) => OpenSelectedTarget();

            var pnlBottom = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                BackColor = Color.FromArgb(20, 22, 34),
                Padding = new Padding(14, 10, 14, 8)
            };

            _lblSummary = new Label
            {
                Text = "0 registro(s)",
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(170, 176, 204)
            };
            pnlBottom.Controls.Add(_lblSummary);

            Controls.AddRange(new Control[] { _lvHistory, pnlBottom, pnlTop });
            Shown += (s, e) => LoadEntries();
        }

        private void LoadEntries()
        {
            string filter = _cmbFilter.SelectedItem != null ? _cmbFilter.SelectedItem.ToString() : "TODOS";
            List<TransferHistoryEntry> entries = TransferHistoryStore.LoadEntries(filter, _txtSearch.Text);

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
                "¿Deseas limpiar todos los registros del historial local?",
                "Confirmar",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (res == DialogResult.Yes)
            {
                TransferHistoryStore.ClearAll();
                LoadEntries();
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
