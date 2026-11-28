using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ONKOS.Core.Storage;

namespace ONKOS.UI
{
    public enum AppThemeMode
    {
        Dark,
        Light,
        HighContrast
    }

    public sealed class ThemePalette
    {
        public AppThemeMode Mode { get; set; }
        public Color WindowBg { get; set; }
        public Color HeaderBg { get; set; }
        public Color CardBg { get; set; }
        public Color InputBg { get; set; }
        public Color PrimaryAccent { get; set; }
        public Color PrimaryHover { get; set; }
        public Color ButtonNeutralBg { get; set; }
        public Color ButtonHoverBg { get; set; }
        public Color ButtonDangerBg { get; set; }
        public Color TextPrimary { get; set; }
        public Color TextSecondary { get; set; }
        public Color TextOnAccent { get; set; }
        public Color BorderColor { get; set; }
    }

    public static class ThemeManager
    {
        private const string ThemeMutex = @"Global\ONKOS_Theme_Lock";
        private static readonly string ThemeFilePath = Path.Combine(
            PathAndAclHelper.BaseDataFolder,
            "tema_ui.txt");

        public static Font BaseFont { get; } = new Font("Segoe UI", 9.5F, FontStyle.Regular);
        public static Font BoldFont { get; } = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        public static Font SectionTitleFont { get; } = new Font("Segoe UI", 10F, FontStyle.Bold);
        public static Font HeaderTitleFont { get; } = new Font("Segoe UI", 12.5F, FontStyle.Bold);

        public static AppThemeMode LoadSavedMode()
        {
            try
            {
                if (!File.Exists(ThemeFilePath))
                    return AppThemeMode.Dark;

                string raw = AtomicFileStore.ReadAllTextShared(ThemeFilePath, Encoding.UTF8, ThemeMutex).Trim();
                if (Enum.TryParse(raw, ignoreCase: true, out AppThemeMode parsed))
                    return parsed;
            }
            catch (Exception)
            {
            }

            return AppThemeMode.Dark;
        }

        public static void SaveMode(AppThemeMode mode)
        {
            PathAndAclHelper.EnsureBaseAndLogsFolders();
            AtomicFileStore.WriteAllTextAtomic(ThemeFilePath, mode.ToString(), Encoding.UTF8, ThemeMutex);
        }

        public static ThemePalette GetCurrentPalette()
        {
            return GetPalette(LoadSavedMode());
        }

        public static ThemePalette GetPalette(AppThemeMode mode)
        {
            switch (mode)
            {
                case AppThemeMode.Light:
                    return new ThemePalette
                    {
                        Mode = AppThemeMode.Light,
                        WindowBg = Color.FromArgb(242, 245, 250),
                        HeaderBg = Color.FromArgb(255, 255, 255),
                        CardBg = Color.FromArgb(255, 255, 255),
                        InputBg = Color.FromArgb(247, 249, 252),
                        PrimaryAccent = Color.FromArgb(79, 70, 229),
                        PrimaryHover = Color.FromArgb(67, 56, 202),
                        ButtonNeutralBg = Color.FromArgb(232, 236, 244),
                        ButtonHoverBg = Color.FromArgb(218, 224, 236),
                        ButtonDangerBg = Color.FromArgb(190, 38, 58),
                        TextPrimary = Color.FromArgb(18, 22, 33),
                        TextSecondary = Color.FromArgb(92, 102, 122),
                        TextOnAccent = Color.White,
                        BorderColor = Color.FromArgb(208, 214, 226)
                    };

                case AppThemeMode.HighContrast:
                    return new ThemePalette
                    {
                        Mode = AppThemeMode.HighContrast,
                        WindowBg = Color.Black,
                        HeaderBg = Color.FromArgb(10, 10, 10),
                        CardBg = Color.FromArgb(12, 12, 12),
                        InputBg = Color.Black,
                        PrimaryAccent = Color.FromArgb(255, 235, 59),
                        PrimaryHover = Color.FromArgb(255, 245, 130),
                        ButtonNeutralBg = Color.FromArgb(26, 26, 26),
                        ButtonHoverBg = Color.FromArgb(46, 46, 46),
                        ButtonDangerBg = Color.FromArgb(255, 82, 82),
                        TextPrimary = Color.White,
                        TextSecondary = Color.FromArgb(225, 225, 225),
                        TextOnAccent = Color.Black,
                        BorderColor = Color.White
                    };

                default:
                    return new ThemePalette
                    {
                        Mode = AppThemeMode.Dark,
                        WindowBg = Color.FromArgb(12, 14, 22),
                        HeaderBg = Color.FromArgb(19, 22, 34),
                        CardBg = Color.FromArgb(22, 26, 40),
                        InputBg = Color.FromArgb(29, 34, 52),
                        PrimaryAccent = Color.FromArgb(109, 94, 248),
                        PrimaryHover = Color.FromArgb(128, 114, 255),
                        ButtonNeutralBg = Color.FromArgb(34, 39, 58),
                        ButtonHoverBg = Color.FromArgb(46, 53, 78),
                        ButtonDangerBg = Color.FromArgb(156, 44, 68),
                        TextPrimary = Color.FromArgb(245, 247, 252),
                        TextSecondary = Color.FromArgb(158, 168, 200),
                        TextOnAccent = Color.White,
                        BorderColor = Color.FromArgb(48, 56, 82)
                    };
            }
        }

        public static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(2, radius * 2);
            if (rect.Width < d || rect.Height < d)
            {
                path.AddRectangle(rect);
                return path;
            }

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void ApplyRoundedRegion(Control control, int radius)
        {
            if (control == null || control.Width <= 2 || control.Height <= 2)
                return;

            using (GraphicsPath path = CreateRoundedPath(new Rectangle(0, 0, control.Width, control.Height), radius))
            {
                Region old = control.Region;
                control.Region = new Region(path);
                if (old != null)
                    old.Dispose();
            }
        }

        public static void StyleButton(Button btn, ThemePalette p, bool isPrimary = false, bool isDanger = false, int radius = 8)
        {
            if (btn == null || p == null)
                return;

            Color baseBg = isPrimary
                ? p.PrimaryAccent
                : (isDanger ? p.ButtonDangerBg : p.ButtonNeutralBg);

            Color hoverBg = isPrimary
                ? p.PrimaryHover
                : (isDanger ? Color.FromArgb(182, 56, 82) : p.ButtonHoverBg);

            btn.FlatStyle = FlatStyle.Flat;
            btn.Font = isPrimary ? BoldFont : BaseFont;
            btn.TabStop = true;
            btn.Cursor = Cursors.Hand;
            btn.BackColor = baseBg;
            btn.ForeColor = isPrimary ? p.TextOnAccent : (isDanger ? Color.White : p.TextPrimary);
            btn.FlatAppearance.BorderColor = isPrimary ? p.PrimaryAccent : p.BorderColor;
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.MouseOverBackColor = hoverBg;
            btn.FlatAppearance.MouseDownBackColor = hoverBg;

            ApplyRoundedRegion(btn, radius);
            btn.SizeChanged -= OnRoundedButtonSizeChanged;
            btn.SizeChanged += OnRoundedButtonSizeChanged;
        }

        private static void OnRoundedButtonSizeChanged(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn != null)
                ApplyRoundedRegion(btn, 8);
        }
    }
}
