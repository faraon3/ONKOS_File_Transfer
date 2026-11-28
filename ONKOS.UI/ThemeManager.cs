using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using ONKOS.Core.Storage;

namespace ONKOS.UI
{
    public enum AppThemeMode
    {
        Auto,
        Light,
        Dark,
        HighContrast
    }

    public enum ButtonVariant
    {
        Primary,
        Secondary,
        Danger
    }

    public sealed class ThemePalette
    {
        public AppThemeMode RequestedMode { get; set; }
        public AppThemeMode EffectiveMode { get; set; }
        public Color WindowBg { get; set; }
        public Color SurfaceBg { get; set; }
        public Color ElevatedSurfaceBg { get; set; }
        public Color HoverSurfaceBg { get; set; }
        public Color AccentStart { get; set; }
        public Color AccentEnd { get; set; }
        public Color AccentHoverStart { get; set; }
        public Color AccentHoverEnd { get; set; }
        public Color DangerBg { get; set; }
        public Color DangerHoverBg { get; set; }
        public Color TextPrimary { get; set; }
        public Color TextSecondary { get; set; }
        public Color TextOnAccent { get; set; }
        public Color BorderSoft { get; set; }
        public Color StatusSuccess { get; set; }
        public Color StatusWarning { get; set; }
        public Color StatusError { get; set; }
    }

    public static class ThemeManager
    {
        private const string ThemeMutex = @"Global\ONKOS_Theme_Lock";
        private static readonly string ThemeFilePath = Path.Combine(
            PathAndAclHelper.BaseDataFolder,
            "tema_ui.txt");

        private static readonly string FontFamilyName = ResolveFontFamily();

        public static Font TitleFont { get; } = new Font(FontFamilyName, 20F, FontStyle.Regular);
        public static Font SectionFont { get; } = new Font(FontFamilyName, 12F, FontStyle.Regular);
        public static Font BodyFont { get; } = new Font(FontFamilyName, 10.5F, FontStyle.Regular);
        public static Font SecondaryFont { get; } = new Font(FontFamilyName, 9F, FontStyle.Regular);
        public static Font AvatarFont { get; } = new Font(FontFamilyName, 9.5F, FontStyle.Regular);

        private static string ResolveFontFamily()
        {
            try
            {
                using (var test = new Font("Segoe UI Variable", 10F, FontStyle.Regular))
                {
                    if (string.Equals(test.FontFamily.Name, "Segoe UI Variable", StringComparison.OrdinalIgnoreCase))
                        return "Segoe UI Variable";
                }
            }
            catch (Exception)
            {
            }

            return "Segoe UI";
        }

        public static bool AreAnimationsEnabled()
        {
            try
            {
                return SystemInformation.UIEffectsEnabled;
            }
            catch (Exception)
            {
                return true;
            }
        }

        public static AppThemeMode LoadSavedMode()
        {
            try
            {
                if (!File.Exists(ThemeFilePath))
                    return AppThemeMode.Light;

                string raw = AtomicFileStore.ReadAllTextShared(ThemeFilePath, Encoding.UTF8, ThemeMutex).Trim();
                if (Enum.TryParse(raw, ignoreCase: true, out AppThemeMode parsed))
                    return parsed;
            }
            catch (Exception)
            {
            }

            return AppThemeMode.Light;
        }

        public static void SaveMode(AppThemeMode mode)
        {
            PathAndAclHelper.EnsureBaseAndLogsFolders();
            AtomicFileStore.WriteAllTextAtomic(ThemeFilePath, mode.ToString(), Encoding.UTF8, ThemeMutex);
        }

        public static AppThemeMode ResolveEffectiveMode(AppThemeMode requested)
        {
            try
            {
                if (SystemInformation.HighContrast)
                    return AppThemeMode.HighContrast;
            }
            catch (Exception)
            {
            }

            if (requested == AppThemeMode.Auto)
                return IsWindowsLightThemePreferred() ? AppThemeMode.Light : AppThemeMode.Dark;

            if (requested == AppThemeMode.HighContrast)
                return AppThemeMode.HighContrast;

            return requested == AppThemeMode.Dark ? AppThemeMode.Dark : AppThemeMode.Light;
        }

        private static bool IsWindowsLightThemePreferred()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    writable: false))
                {
                    object val = key?.GetValue("AppsUseLightTheme");
                    if (val is int intVal)
                        return intVal != 0;
                }
            }
            catch (Exception)
            {
            }

            return true;
        }

        public static ThemePalette GetCurrentPalette()
        {
            AppThemeMode saved = LoadSavedMode();
            return GetPalette(saved);
        }

        public static ThemePalette GetPalette(AppThemeMode requested)
        {
            AppThemeMode effective = ResolveEffectiveMode(requested);

            if (effective == AppThemeMode.HighContrast)
            {
                return new ThemePalette
                {
                    RequestedMode = requested,
                    EffectiveMode = AppThemeMode.HighContrast,
                    WindowBg = SystemColors.Window,
                    SurfaceBg = SystemColors.Window,
                    ElevatedSurfaceBg = SystemColors.Control,
                    HoverSurfaceBg = SystemColors.Highlight,
                    AccentStart = SystemColors.Highlight,
                    AccentEnd = SystemColors.Highlight,
                    AccentHoverStart = SystemColors.Highlight,
                    AccentHoverEnd = SystemColors.Highlight,
                    DangerBg = Color.FromArgb(180, 20, 40),
                    DangerHoverBg = Color.FromArgb(215, 25, 45),
                    TextPrimary = SystemColors.WindowText,
                    TextSecondary = SystemColors.GrayText,
                    TextOnAccent = SystemColors.HighlightText,
                    BorderSoft = SystemColors.WindowFrame,
                    StatusSuccess = Color.FromArgb(22, 130, 72),
                    StatusWarning = Color.FromArgb(180, 115, 10),
                    StatusError = Color.FromArgb(195, 35, 50)
                };
            }

            if (effective == AppThemeMode.Dark)
            {
                return new ThemePalette
                {
                    RequestedMode = requested,
                    EffectiveMode = AppThemeMode.Dark,
                    WindowBg = Color.FromArgb(20, 22, 31),
                    SurfaceBg = Color.FromArgb(27, 30, 42),
                    ElevatedSurfaceBg = Color.FromArgb(35, 39, 54),
                    HoverSurfaceBg = Color.FromArgb(46, 51, 70),
                    AccentStart = Color.FromArgb(124, 106, 246),
                    AccentEnd = Color.FromArgb(88, 132, 248),
                    AccentHoverStart = Color.FromArgb(139, 122, 252),
                    AccentHoverEnd = Color.FromArgb(104, 146, 255),
                    DangerBg = Color.FromArgb(198, 58, 78),
                    DangerHoverBg = Color.FromArgb(218, 72, 92),
                    TextPrimary = Color.FromArgb(241, 244, 250),
                    TextSecondary = Color.FromArgb(155, 163, 184),
                    TextOnAccent = Color.White,
                    BorderSoft = Color.FromArgb(52, 58, 79),
                    StatusSuccess = Color.FromArgb(52, 196, 124),
                    StatusWarning = Color.FromArgb(236, 174, 54),
                    StatusError = Color.FromArgb(232, 82, 98)
                };
            }

            return new ThemePalette
            {
                RequestedMode = requested,
                EffectiveMode = AppThemeMode.Light,
                WindowBg = Color.FromArgb(245, 246, 250),
                SurfaceBg = Color.FromArgb(255, 255, 255),
                ElevatedSurfaceBg = Color.FromArgb(239, 241, 247),
                HoverSurfaceBg = Color.FromArgb(228, 232, 243),
                AccentStart = Color.FromArgb(118, 98, 242),
                AccentEnd = Color.FromArgb(79, 126, 247),
                AccentHoverStart = Color.FromArgb(104, 84, 232),
                AccentHoverEnd = Color.FromArgb(65, 112, 238),
                DangerBg = Color.FromArgb(205, 52, 72),
                DangerHoverBg = Color.FromArgb(184, 40, 58),
                TextPrimary = Color.FromArgb(24, 27, 38),
                TextSecondary = Color.FromArgb(106, 114, 134),
                TextOnAccent = Color.White,
                BorderSoft = Color.FromArgb(216, 221, 234),
                StatusSuccess = Color.FromArgb(24, 158, 94),
                StatusWarning = Color.FromArgb(204, 136, 16),
                StatusError = Color.FromArgb(206, 52, 68)
            };
        }

        public static GraphicsPath CreateRoundedRectangle(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Max(2F, radius * 2F);
            if (rect.Width < d || rect.Height < d)
            {
                path.AddRectangle(rect);
                return path;
            }

            path.AddArc(rect.X, rect.Y, d, d, 180F, 90F);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270F, 90F);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0F, 90F);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90F, 90F);
            path.CloseFigure();
            return path;
        }

        public static void ConfigureSmoothButton(Button btn, ThemePalette palette, ButtonVariant variant = ButtonVariant.Secondary, int cornerRadius = 11)
        {
            if (btn == null || palette == null)
                return;

            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.Transparent;
            btn.FlatAppearance.MouseDownBackColor = Color.Transparent;
            btn.Font = BodyFont;
            btn.Cursor = Cursors.Hand;
            btn.TabStop = true;
            btn.Region = null;

            var state = btn.Tag as SmoothButtonState;
            if (state == null)
            {
                state = new SmoothButtonState
                {
                    Variant = variant,
                    Palette = palette,
                    CornerRadius = cornerRadius,
                    HoverProgress = IsCursorOverControl(btn) ? 1F : 0F,
                    Pressed = false
                };
                btn.Tag = state;

                var animTimer = new Timer { Interval = 16 };
                animTimer.Tick += (s, e) =>
                {
                    if (btn.IsDisposed || btn.Disposing)
                    {
                        animTimer.Stop();
                        animTimer.Dispose();
                        return;
                    }

                    var st = btn.Tag as SmoothButtonState;
                    if (st == null)
                        return;

                    float target = (st.Hovered && btn.Enabled) ? 1F : 0F;
                    if (!AreAnimationsEnabled())
                    {
                        st.HoverProgress = target;
                        animTimer.Stop();
                        btn.Invalidate();
                        return;
                    }

                    float step = 0.14F;
                    if (Math.Abs(st.HoverProgress - target) <= step)
                    {
                        st.HoverProgress = target;
                        animTimer.Stop();
                    }
                    else
                    {
                        st.HoverProgress += (target > st.HoverProgress) ? step : -step;
                    }

                    btn.Invalidate();
                };
                state.AnimTimer = animTimer;

                btn.MouseEnter += (s, e) =>
                {
                    var st = btn.Tag as SmoothButtonState;
                    if (st == null) return;
                    st.Hovered = true;
                    st.AnimTimer.Start();
                };
                btn.MouseLeave += (s, e) =>
                {
                    var st = btn.Tag as SmoothButtonState;
                    if (st == null) return;
                    st.Hovered = false;
                    st.Pressed = false;
                    st.AnimTimer.Start();
                    btn.Invalidate();
                };
                btn.MouseDown += (s, e) =>
                {
                    if (e.Button != MouseButtons.Left) return;
                    var st = btn.Tag as SmoothButtonState;
                    if (st == null) return;
                    st.Pressed = true;
                    btn.Invalidate();
                };
                btn.MouseUp += (s, e) =>
                {
                    var st = btn.Tag as SmoothButtonState;
                    if (st == null) return;
                    st.Pressed = false;
                    btn.Invalidate();
                };
                btn.Disposed += (s, e) =>
                {
                    var st = btn.Tag as SmoothButtonState;
                    if (st != null && st.AnimTimer != null)
                    {
                        st.AnimTimer.Stop();
                        st.AnimTimer.Dispose();
                    }
                };
                btn.Paint += OnSmoothButtonPaint;
            }
            else
            {
                state.Variant = variant;
                state.Palette = palette;
                state.CornerRadius = cornerRadius;
            }

            btn.Invalidate();
        }

        private static bool IsCursorOverControl(Control ctrl)
        {
            if (ctrl == null || !ctrl.IsHandleCreated || ctrl.IsDisposed)
                return false;
            Point pt = ctrl.PointToClient(Cursor.Position);
            return ctrl.ClientRectangle.Contains(pt);
        }

        private static void OnSmoothButtonPaint(object sender, PaintEventArgs e)
        {
            var btn = sender as Button;
            var st = btn?.Tag as SmoothButtonState;
            if (btn == null || st == null || st.Palette == null)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Control parent = btn.Parent;
            Color parentColor = parent != null ? parent.BackColor : st.Palette.WindowBg;
            using (var clearBrush = new SolidBrush(parentColor))
            {
                e.Graphics.FillRectangle(clearBrush, btn.ClientRectangle);
            }

            float scale = (st.Pressed && btn.Enabled && AreAnimationsEnabled()) ? 0.98F : 1F;
            float w = btn.Width * scale;
            float h = btn.Height * scale;
            float x = (btn.Width - w) / 2F;
            float y = (btn.Height - h) / 2F;

            RectangleF bounds = new RectangleF(x + 0.5F, y + 0.5F, Math.Max(2F, w - 1F), Math.Max(2F, h - 1F));
            float radius = Math.Min(st.CornerRadius, Math.Min(bounds.Width, bounds.Height) / 2F);

            using (GraphicsPath path = CreateRoundedRectangle(bounds, radius))
            {
                if (st.Variant == ButtonVariant.Primary)
                {
                    Color c1 = LerpColor(st.Palette.AccentStart, st.Palette.AccentHoverStart, st.HoverProgress);
                    Color c2 = LerpColor(st.Palette.AccentEnd, st.Palette.AccentHoverEnd, st.HoverProgress);
                    if (!btn.Enabled)
                    {
                        c1 = LerpColor(c1, st.Palette.ElevatedSurfaceBg, 0.55F);
                        c2 = LerpColor(c2, st.Palette.ElevatedSurfaceBg, 0.55F);
                    }
                    using (var grad = new LinearGradientBrush(bounds, c1, c2, LinearGradientMode.Horizontal))
                    {
                        e.Graphics.FillPath(grad, path);
                    }
                }
                else if (st.Variant == ButtonVariant.Danger)
                {
                    Color fill = LerpColor(st.Palette.DangerBg, st.Palette.DangerHoverBg, st.HoverProgress);
                    if (!btn.Enabled)
                        fill = LerpColor(fill, st.Palette.ElevatedSurfaceBg, 0.55F);
                    using (var brush = new SolidBrush(fill))
                    {
                        e.Graphics.FillPath(brush, path);
                    }
                }
                else
                {
                    Color fill = LerpColor(Color.Transparent, st.Palette.HoverSurfaceBg, st.HoverProgress * 0.72F);
                    if (fill.A > 0)
                    {
                        using (var brush = new SolidBrush(fill))
                        {
                            e.Graphics.FillPath(brush, path);
                        }
                    }
                }
            }

            Color textColor = st.Variant == ButtonVariant.Primary
                ? (btn.Enabled ? st.Palette.TextOnAccent : st.Palette.TextSecondary)
                : (st.Variant == ButtonVariant.Danger
                    ? Color.White
                    : (btn.Enabled ? st.Palette.TextPrimary : st.Palette.TextSecondary));

            TextFormatFlags flags = TextFormatFlags.HorizontalCenter |
                                    TextFormatFlags.VerticalCenter |
                                    TextFormatFlags.SingleLine |
                                    TextFormatFlags.EndEllipsis;

            TextRenderer.DrawText(e.Graphics, btn.Text, btn.Font, Rectangle.Round(bounds), textColor, flags);
        }

        public static Color LerpColor(Color a, Color b, float t)
        {
            float clamped = Math.Max(0F, Math.Min(1F, t));
            int r = (int)Math.Round(a.R + (b.R - a.R) * clamped);
            int g = (int)Math.Round(a.G + (b.G - a.G) * clamped);
            int bl = (int)Math.Round(a.B + (b.B - a.B) * clamped);
            int al = (int)Math.Round(a.A + (b.A - a.A) * clamped);
            return Color.FromArgb(al, r, g, bl);
        }

        private sealed class SmoothButtonState
        {
            public ButtonVariant Variant { get; set; }
            public ThemePalette Palette { get; set; }
            public int CornerRadius { get; set; }
            public bool Hovered { get; set; }
            public bool Pressed { get; set; }
            public float HoverProgress { get; set; }
            public Timer AnimTimer { get; set; }
        }
    }
}
