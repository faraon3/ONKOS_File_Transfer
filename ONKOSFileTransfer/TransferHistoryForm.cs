using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private readonly RoundedButton _btnClearHistory;

        public TransferHistoryForm()
        {
            Text = "ONKOS File Transfer 2.0 — Historial";
            Size = new Size(900, 510);
            MinimumSize = new Size(780, 420);
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
                Location = new Point(385, 16),
                Size = new Size(125, 26)
            };
            _cmbFilter.Items.AddRange(new object[] { "TODOS", "ENVIADO", "RECIBIDO" });
            _cmbFilter.SelectedIndex = 0;

            _txtFilter = new TextBox
            {
                BackColor = Color.FromArgb(31, 34, 52),
                ForeColor = Color.FromArgb(246, 247, 252),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(522, 17),
                Size = new Size(225, 24)
            };

            var btnRefresh = new RoundedButton
            {
                Text = "Actualizar",
                Location = new Point(759, 15),
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
                Font = new Font("Segoe UI", 8.8F),
                OwnerDraw = true,
                ShowItemToolTips = true
            };

            _lvHistory.Columns.Add("Fecha / Hora", 140);
            _lvHistory.Columns.Add("Tipo", 96, HorizontalAlignment.Center);
            _lvHistory.Columns.Add("Archivo / Carpeta", 240);
            _lvHistory.Columns.Add("Peso", 80, HorizontalAlignment.Right);
            _lvHistory.Columns.Add("Equipo / Persona", 175);
            _lvHistory.Columns.Add("Estado", 104, HorizontalAlignment.Center);

            _lvHistory.DrawColumnHeader += LvHistory_DrawColumnHeader;
            _lvHistory.DrawItem += (s, e) => { };
            _lvHistory.DrawSubItem += LvHistory_DrawSubItem;

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
                Text = "0 registros  •  Doble clic en un recibido para abrir su carpeta",
                Font = new Font("Segoe UI", 8.6F),
                ForeColor = Color.FromArgb(170, 176, 204),
                Location = new Point(16, 16),
                AutoSize = true
            };

            _btnClearHistory = new RoundedButton
            {
                Text = "Limpiar historial",
                Location = new Point(735, 11),
                Size = new Size(135, 30),
                ParentSurfaceColor = headerBg,
                FillColor = subtleBtnBg,
                HoverColor = Color.FromArgb(185, 58, 78),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold)
            };

            pnlBottom.Controls.AddRange(new Control[] { _lblSummary, _btnClearHistory });

            Controls.AddRange(new Control[] { pnlBody, pnlBottom, pnlTop });

            _cmbFilter.SelectedIndexChanged += (s, e) => LoadHistory();
            _txtFilter.TextChanged += (s, e) => LoadHistory();
            _btnClearHistory.Click += BtnClearHistory_Click;
            _lvHistory.DoubleClick += (s, e) => OpenReceivedLocationOnDoubleClick();

            Resize += (s, e) => AdjustColumns();
            Shown += (s, e) =>
            {
                ApplyDarkTitleBar();
                AdjustColumns();
                LoadHistory();
            };
        }

        private void LvHistory_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var bgBrush = new SolidBrush(Color.FromArgb(26, 29, 46)))
            using (var borderPen = new Pen(Color.FromArgb(40, 44, 68), 1F))
            {
                e.Graphics.FillRectangle(bgBrush, e.Bounds);
                e.Graphics.DrawLine(borderPen, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4);
                e.Graphics.DrawLine(borderPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }

            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            if (e.ColumnIndex == 1 || e.ColumnIndex == 5)
                flags |= TextFormatFlags.HorizontalCenter;
            else if (e.ColumnIndex == 3)
                flags |= TextFormatFlags.Right;
            else
                flags |= TextFormatFlags.Left;

            Rectangle textRect = new Rectangle(
                e.Bounds.X + 8,
                e.Bounds.Y,
                Math.Max(10, e.Bounds.Width - 14),
                e.Bounds.Height);

            TextRenderer.DrawText(
                e.Graphics,
                e.Header.Text,
                new Font("Segoe UI", 8.6F, FontStyle.Bold),
                textRect,
                Color.FromArgb(184, 148, 255),
                flags);
        }

        private void LvHistory_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            bool selected = (e.ItemState & ListViewItemStates.Selected) == ListViewItemStates.Selected;
            Color rowBg = selected ? Color.FromArgb(42, 36, 72) : Color.FromArgb(18, 20, 32);

            using (var bgBrush = new SolidBrush(rowBg))
                e.Graphics.FillRectangle(bgBrush, e.Bounds);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var entry = e.Item.Tag as TransferHistoryEntry;

            if (e.ColumnIndex == 1)
            {
                bool isReceived = string.Equals(e.SubItem.Text, "RECIBIDO", StringComparison.OrdinalIgnoreCase);
                Color pillBg = isReceived ? Color.FromArgb(20, 56, 62) : Color.FromArgb(46, 38, 78);
                Color pillBorder = isReceived ? Color.FromArgb(45, 212, 191) : Color.FromArgb(139, 92, 246);
                Color pillFg = isReceived ? Color.FromArgb(153, 246, 228) : Color.FromArgb(216, 180, 254);

                DrawPill(e.Graphics, e.Bounds, e.SubItem.Text, pillBg, pillBorder, pillFg);
                return;
            }

            if (e.ColumnIndex == 5)
            {
                bool isError = string.Equals(e.SubItem.Text, "Error", StringComparison.OrdinalIgnoreCase);
                Color pillBg = isError ? Color.FromArgb(68, 24, 36) : Color.FromArgb(20, 62, 46);
                Color pillBorder = isError ? Color.FromArgb(244, 63, 94) : Color.FromArgb(34, 197, 94);
                Color pillFg = isError ? Color.FromArgb(253, 164, 175) : Color.FromArgb(110, 231, 183);

                DrawPill(e.Graphics, e.Bounds, e.SubItem.Text, pillBg, pillBorder, pillFg);
                return;
            }

            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            Color textColor = Color.FromArgb(236, 240, 252);

            if (e.ColumnIndex == 3)
            {
                flags |= TextFormatFlags.Right;
                textColor = Color.FromArgb(165, 172, 202);
            }
            else
            {
                flags |= TextFormatFlags.Left;
                if (entry != null && string.Equals(entry.Status, "Error", StringComparison.OrdinalIgnoreCase))
                    textColor = Color.FromArgb(253, 164, 175);
            }

            Rectangle rect = new Rectangle(
                e.Bounds.X + 8,
                e.Bounds.Y,
                Math.Max(10, e.Bounds.Width - 14),
                e.Bounds.Height);

            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, _lvHistory.Font, rect, textColor, flags);
        }

        private static void DrawPill(Graphics g, Rectangle bounds, string text, Color bg, Color border, Color fg)
        {
            int pillW = Math.Min(88, Math.Max(60, bounds.Width - 10));
            int pillH = Math.Min(19, Math.Max(14, bounds.Height - 6));
            int pillX = bounds.X + (bounds.Width - pillW) / 2;
            int pillY = bounds.Y + (bounds.Height - pillH) / 2;

            Rectangle pillRect = new Rectangle(pillX, pillY, pillW, pillH);
            using (GraphicsPath path = RoundedPanel.CreateRoundRect(pillRect, pillH / 2))
            using (SolidBrush fill = new SolidBrush(bg))
            using (Pen pen = new Pen(border, 1F))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(
                g,
                text,
                new Font("Segoe UI", 7.5F, FontStyle.Bold),
                pillRect,
                fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void AdjustColumns()
        {
            if (_lvHistory == null || _lvHistory.Columns.Count < 6)
                return;

            int usable = Math.Max(640, _lvHistory.ClientSize.Width - 4);
            _lvHistory.Columns[0].Width = 140;
            _lvHistory.Columns[1].Width = 96;
            _lvHistory.Columns[3].Width = 80;
            _lvHistory.Columns[5].Width = 104;

            int remaining = Math.Max(220, usable - 140 - 96 - 80 - 104);
            _lvHistory.Columns[2].Width = (int)(remaining * 0.56);
            _lvHistory.Columns[4].Width = Math.Max(120, remaining - _lvHistory.Columns[2].Width);
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

                _lvHistory.Items.Add(lvi);
            }

            _lvHistory.EndUpdate();
            AdjustColumns();
            _lblSummary.Text = $"{entries.Count} registro(s)  •  Doble clic en un recibido para abrir su carpeta";
        }

        private void OpenReceivedLocationOnDoubleClick()
        {
            if (_lvHistory.SelectedItems.Count == 0)
                return;

            if (!(_lvHistory.SelectedItems[0].Tag is TransferHistoryEntry entry))
                return;

            if (!string.Equals(entry.Direction, "RECIBIDO", StringComparison.OrdinalIgnoreCase))
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
            catch
            {
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
