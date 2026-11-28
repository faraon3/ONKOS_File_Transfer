using System;
using System.Drawing;
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
        public Color ButtonNeutralBg { get; set; }
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
        public static Font SectionTitleFont { get; } = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        public static Font HeaderTitleFont { get; } = new Font("Segoe UI", 13F, FontStyle.Bold);

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
                        WindowBg = Color.FromArgb(244, 246, 250),
                        HeaderBg = Color.FromArgb(230, 234, 242),
                        CardBg = Color.White,
                        InputBg = Color.White,
                        PrimaryAccent = Color.FromArgb(94, 53, 177),
                        ButtonNeutralBg = Color.FromArgb(224, 229, 238),
                        ButtonDangerBg = Color.FromArgb(183, 28, 28),
                        TextPrimary = Color.FromArgb(22, 26, 36),
                        TextSecondary = Color.FromArgb(75, 84, 102),
                        TextOnAccent = Color.White,
                        BorderColor = Color.FromArgb(190, 198, 212)
                    };

                case AppThemeMode.HighContrast:
                    return new ThemePalette
                    {
                        Mode = AppThemeMode.HighContrast,
                        WindowBg = Color.Black,
                        HeaderBg = Color.FromArgb(12, 12, 12),
                        CardBg = Color.Black,
                        InputBg = Color.Black,
                        PrimaryAccent = Color.FromArgb(255, 235, 59),
                        ButtonNeutralBg = Color.FromArgb(28, 28, 28),
                        ButtonDangerBg = Color.FromArgb(255, 82, 82),
                        TextPrimary = Color.White,
                        TextSecondary = Color.FromArgb(230, 230, 230),
                        TextOnAccent = Color.Black,
                        BorderColor = Color.White
                    };

                default:
                    return new ThemePalette
                    {
                        Mode = AppThemeMode.Dark,
                        WindowBg = Color.FromArgb(14, 15, 24),
                        HeaderBg = Color.FromArgb(20, 22, 34),
                        CardBg = Color.FromArgb(18, 20, 32),
                        InputBg = Color.FromArgb(28, 31, 48),
                        PrimaryAccent = Color.FromArgb(124, 77, 224),
                        ButtonNeutralBg = Color.FromArgb(38, 41, 62),
                        ButtonDangerBg = Color.FromArgb(140, 42, 62),
                        TextPrimary = Color.FromArgb(246, 247, 252),
                        TextSecondary = Color.FromArgb(176, 184, 212),
                        TextOnAccent = Color.White,
                        BorderColor = Color.FromArgb(58, 64, 92)
                    };
            }
        }

        public static void StyleButton(Button btn, ThemePalette p, bool isPrimary = false, bool isDanger = false)
        {
            if (btn == null || p == null)
                return;

            btn.FlatStyle = FlatStyle.Flat;
            btn.Font = isPrimary ? BoldFont : BaseFont;
            btn.TabStop = true;

            if (isPrimary)
            {
                btn.BackColor = p.PrimaryAccent;
                btn.ForeColor = p.TextOnAccent;
                btn.FlatAppearance.BorderColor = p.PrimaryAccent;
                btn.FlatAppearance.BorderSize = 1;
            }
            else if (isDanger)
            {
                btn.BackColor = p.ButtonDangerBg;
                btn.ForeColor = Color.White;
                btn.FlatAppearance.BorderColor = p.BorderColor;
                btn.FlatAppearance.BorderSize = 1;
            }
            else
            {
                btn.BackColor = p.ButtonNeutralBg;
                btn.ForeColor = p.TextPrimary;
                btn.FlatAppearance.BorderColor = p.BorderColor;
                btn.FlatAppearance.BorderSize = 1;
            }
        }
    }
}
