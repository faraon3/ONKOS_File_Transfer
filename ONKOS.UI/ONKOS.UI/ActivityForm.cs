using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ONKOS.Core.Models;
using ONKOS.Core.Notifications;
using ONKOS.Core.Storage;

namespace ONKOS.UI
{
    public sealed class ActivityForm : Form
    {
        private readonly ThemePalette _palette;
        private readonly FlowLayoutPanel _pnlTabs;
        private readonly TextBox _txtSearch;
        private readonly FlowLayoutPanel _pnlCards;
        private readonly ContextMenuStrip _moreMenu;
        private readonly Timer _fadeTimer;
        private string _currentTab = "Todo";

        public ActivityForm(string initialTab = "Todo")
        {
            _currentTab = string.IsNullOrWhiteSpace(initialTab) ? "Todo" : initialTab;
            _palette = ThemeManager.GetCurrentPalette();

            Text = "Actividad";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Size = new Size(560, 680);
            MinimumSize = new Size(420, 480);
            StartPosition = FormStartPosition.CenterParent;
            Opacity = ThemeManager.AreAnimationsEnabled() ? 0.0 : 1.0;
            BackColor = _palette.WindowBg;
            ForeColor = _palette.TextPrimary;
            Font = ThemeManager.BodyFont;

            var topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 56,
                BackColor = _palette.WindowBg,
                Padding = new Padding(16, 12, 16, 6)
            };

            var lblTitle = new Label
            {
                Text = "Actividad",
                Font = ThemeManager.SectionFont,
                ForeColor = _palette.TextPrimary,
                Location = new Point(16, 14),
                AutoSize = true
            };

            _moreMenu = new ContextMenuStrip();
            _moreMenu.Items.Add("Exportar CSV", null, (s, e) => ExportCsv());
            _moreMenu.Items.Add("Marcar avisos leídos", null, (s, e) =>
            {
                NotificationCenterStore.MarkAllAsRead();
                RebuildCards();
            });

            var btnMore = new Button
            {
                Text = "⋯",
                Size = new Size(34, 30),
                Location = new Point(Width - 54, 13),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            ThemeManager.ConfigureSmoothButton(btnMore, _palette, ButtonVariant.Secondary, 10);
            btnMore.Click += (s, e) => _moreMenu.Show(btnMore, new Point(0, btnMore.Height + 4));

            topBar.Controls.AddRange(new Control[] { lblTitle, btnMore });

            _pnlTabs = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(16, 4, 16, 4),
                BackColor = _palette.WindowBg
            };

            _txtSearch = new TextBox
            {
                Dock = DockStyle.Top,
                Height = 30,
                BackColor = _palette.SurfaceBg,
                ForeColor = _palette.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle
            };
            var searchWrap = new Panel
            {
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(16, 4, 16, 6),
                BackColor = _palette.WindowBg
            };
            searchWrap.Controls.Add(_txtSearch);
            _txtSearch.TextChanged += (s, e) => RebuildCards();

            _pnlCards = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(16, 6, 16, 16),
                BackColor = _palette.WindowBg
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

            Controls.AddRange(new Control[] { _pnlCards, searchWrap, _pnlTabs, topBar });
            Load += (s, e) =>
            {
                TransferHistoryStore.MarkReceivedAsSeen();
                RebuildTabs();
                RebuildCards();
                _fadeTimer.Start();
            };
            FormClosed += (s, e) =>
            {
                _fadeTimer.Stop();
                _fadeTimer.Dispose();
                _moreMenu.Dispose();
            };
        }

        private void RebuildTabs()
        {
            _pnlTabs.Controls.Clear();
            foreach (string tab in new[] { "Todo", "Enviados", "Recibidos", "Alertas" })
            {
                bool active = string.Equals(_currentTab, tab, StringComparison.OrdinalIgnoreCase);
                var b = new Button
                {
                    Text = tab,
                    Height = 28,
                    Width = Math.Max(64, TextRenderer.MeasureText(tab, ThemeManager.SecondaryFont).Width + 22),
                    Margin = new Padding(0, 2, 6, 2)
                };
                ThemeManager.ConfigureSmoothButton(b, _palette, active ? ButtonVariant.Primary : ButtonVariant.Secondary, 14);
                string captured = tab;
                b.Click += (s, e) =>
                {
                    _currentTab = captured;
                    RebuildTabs();
                    RebuildCards();
                };
                _pnlTabs.Controls.Add(b);
            }
        }

        private void RebuildCards()
        {
            _pnlCards.SuspendLayout();
            _pnlCards.Controls.Clear();

            string query = (_txtSearch.Text ?? string.Empty).Trim();
            var rows = new List<ActivityRowItem>();

            if (!string.Equals(_currentTab, "Alertas", StringComparison.OrdinalIgnoreCase))
            {
                string dir = string.Equals(_currentTab, "Enviados", StringComparison.OrdinalIgnoreCase)
                    ? "ENVIADO"
                    : (string.Equals(_currentTab, "Recibidos", StringComparison.OrdinalIgnoreCase) ? "RECIBIDO" : "TODOS");

                foreach (TransferHistoryEntry h in TransferHistoryStore.LoadEntries(dir, query, "TODOS", DateTime.Today.AddDays(-365), DateTime.Today))
                {
                    rows.Add(new ActivityRowItem
                    {
                        TimeLocal = h.TimestampLocal,
                        Title = h.ItemName ?? string.Empty,
                        Subtitle = $"{(string.Equals(h.Direction, "RECIBIDO", StringComparison.OrdinalIgnoreCase) ? "Recibido" : "Enviado")} · {h.Counterparty}",
                        Detail = $"SHA-256: {h.Sha256}\nDestino: {h.TargetPath}\n{h.Notes}",
                        Kind = string.Equals(h.Status, "Error", StringComparison.OrdinalIgnoreCase) ? LineIconKind.ErrorCircle : LineIconKind.CheckCircle
                    });
                }
            }

            if (string.Equals(_currentTab, "Todo", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_currentTab, "Alertas", StringComparison.OrdinalIgnoreCase))
            {
                foreach (NotificationRecord n in NotificationCenterStore.GetAll(onlyUnread: false))
                {
                    if (!string.IsNullOrEmpty(query) &&
                        (n.Title ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                        (n.Detail ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    rows.Add(new ActivityRowItem
                    {
                        TimeLocal = n.TimestampLocal,
                        Title = n.Title ?? "Aviso",
                        Subtitle = n.SenderOrTarget ?? string.Empty,
                        Detail = n.Detail ?? string.Empty,
                        Kind = n.Category == NotificationCategory.Error ? LineIconKind.ErrorCircle : LineIconKind.Bell
                    });
                }
            }

            var grouped = rows.OrderByDescending(r => r.TimeLocal).GroupBy(r => r.TimeLocal.Date);
            int w = Math.Max(260, _pnlCards.ClientSize.Width - 40);

            foreach (var grp in grouped)
            {
                var lblDay = new Label
                {
                    Text = FormatDayHeading(grp.Key),
                    Font = ThemeManager.SecondaryFont,
                    ForeColor = _palette.TextSecondary,
                    AutoSize = true,
                    Margin = new Padding(0, 8, 0, 4)
                };
                _pnlCards.Controls.Add(lblDay);

                foreach (ActivityRowItem row in grp)
                {
                    bool expanded = false;
                    var card = new Panel
                    {
                        Width = w,
                        Height = 54,
                        BackColor = _palette.SurfaceBg,
                        Margin = new Padding(0, 0, 0, 8),
                        Cursor = Cursors.Hand
                    };

                    card.Click += (s, e) =>
                    {
                        expanded = !expanded;
                        card.Height = expanded ? 110 : 54;
                        card.Invalidate();
                    };

                    card.Paint += (s, e) =>
                    {
                        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        var r = new RectangleF(0.5F, 0.5F, card.Width - 1F, card.Height - 1F);
                        using (var gp = ThemeManager.CreateRoundedRectangle(r, 12F))
                        using (var b = new SolidBrush(card.BackColor))
                        using (var p = new Pen(_palette.BorderSoft, 1F))
                        {
                            e.Graphics.FillPath(b, gp);
                            e.Graphics.DrawPath(p, gp);
                        }

                        Color col = row.Kind == LineIconKind.ErrorCircle ? _palette.StatusError : _palette.StatusSuccess;
                        LineIcons.DrawIcon(e.Graphics, row.Kind, new Rectangle(12, 15, 20, 20), col, 1.7F);

                        TextRenderer.DrawText(e.Graphics, row.Title, ThemeManager.BodyFont, new Rectangle(40, 8, card.Width - 125, 20), _palette.TextPrimary, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                        TextRenderer.DrawText(e.Graphics, row.Subtitle, ThemeManager.SecondaryFont, new Rectangle(40, 28, card.Width - 125, 18), _palette.TextSecondary, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                        TextRenderer.DrawText(e.Graphics, FormatRelativeTime(row.TimeLocal), ThemeManager.SecondaryFont, new Rectangle(card.Width - 115, 10, 103, 18), _palette.TextSecondary, TextFormatFlags.Right);

                        if (expanded && !string.IsNullOrWhiteSpace(row.Detail))
                        {
                            TextRenderer.DrawText(e.Graphics, row.Detail, ThemeManager.SecondaryFont, new Rectangle(14, 54, card.Width - 28, 50), _palette.TextSecondary, TextFormatFlags.EndEllipsis);
                        }
                    };

                    _pnlCards.Controls.Add(card);
                }
            }

            _pnlCards.ResumeLayout();
        }

        private void ExportCsv()
        {
            using (var sfd = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"actividad_onkos_{DateTime.Now:yyyyMMdd}.csv"
            })
            {
                if (sfd.ShowDialog(this) == DialogResult.OK)
                {
                    List<TransferHistoryEntry> entries = TransferHistoryStore.LoadEntries("TODOS", _txtSearch.Text, "TODOS", DateTime.Today.AddDays(-365), DateTime.Today);
                    TransferHistoryStore.ExportToCsv(sfd.FileName, entries);
                }
            }
        }

        private static string FormatDayHeading(DateTime d)
        {
            if (d == DateTime.Today) return "Hoy";
            if (d == DateTime.Today.AddDays(-1)) return "Ayer";
            return d.ToString("d 'de' MMMM");
        }

        private static string FormatRelativeTime(DateTime t)
        {
            TimeSpan diff = DateTime.Now - t;
            if (diff.TotalMinutes < 1) return "Ahora";
            if (diff.TotalMinutes < 60) return $"Hace {(int)diff.TotalMinutes} min";
            if (diff.TotalHours < 24) return $"Hace {(int)diff.TotalHours} h";
            return t.ToString("hh:mm tt");
        }

        private sealed class ActivityRowItem
        {
            public DateTime TimeLocal { get; set; }
            public string Title { get; set; }
            public string Subtitle { get; set; }
            public string Detail { get; set; }
            public LineIconKind Kind { get; set; }
        }
    }
}
