using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace ONKOS.UI
{
    public enum LineIconKind
    {
        Bell,
        User,
        Lock,
        Close,
        FileDoc,
        FileImage,
        FilePdf,
        Folder,
        CheckCircle,
        PauseIcon,
        ErrorCircle,
        Search,
        Plus,
        MoreDots
    }

    public static class LineIcons
    {
        public static LineIconKind DetectFileKind(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return LineIconKind.FileDoc;

            if (Directory.Exists(path))
                return LineIconKind.Folder;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".pdf":
                    return LineIconKind.FilePdf;
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".gif":
                case ".bmp":
                case ".webp":
                    return LineIconKind.FileImage;
                default:
                    return LineIconKind.FileDoc;
            }
        }

        public static void DrawIcon(Graphics g, LineIconKind kind, Rectangle bounds, Color color, float strokeWidth = 1.75F)
        {
            if (g == null || bounds.Width < 4 || bounds.Height < 4)
                return;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, strokeWidth))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                float cx = bounds.X + bounds.Width / 2F;
                float cy = bounds.Y + bounds.Height / 2F;
                float s = Math.Min(bounds.Width, bounds.Height);

                switch (kind)
                {
                    case LineIconKind.Bell:
                        var bellRect = new RectangleF(cx - s * 0.28F, cy - s * 0.28F, s * 0.56F, s * 0.52F);
                        g.DrawArc(pen, bellRect, 180F, 180F);
                        g.DrawLine(pen, cx - s * 0.28F, cy + s * 0.24F, cx + s * 0.28F, cy + s * 0.24F);
                        g.DrawArc(pen, new RectangleF(cx - s * 0.1F, cy + s * 0.22F, s * 0.2F, s * 0.16F), 0F, 180F);
                        break;

                    case LineIconKind.User:
                        float headR = s * 0.18F;
                        g.DrawEllipse(pen, cx - headR, cy - s * 0.34F, headR * 2F, headR * 2F);
                        g.DrawArc(pen, new RectangleF(cx - s * 0.34F, cy + s * 0.04F, s * 0.68F, s * 0.44F), 180F, 180F);
                        break;

                    case LineIconKind.Lock:
                        var bodyRect = new RectangleF(cx - s * 0.28F, cy - s * 0.04F, s * 0.56F, s * 0.4F);
                        g.DrawRectangle(pen, bodyRect.X, bodyRect.Y, bodyRect.Width, bodyRect.Height);
                        g.DrawArc(pen, new RectangleF(cx - s * 0.17F, cy - s * 0.34F, s * 0.34F, s * 0.34F), 180F, 180F);
                        break;

                    case LineIconKind.Close:
                        float d = s * 0.24F;
                        g.DrawLine(pen, cx - d, cy - d, cx + d, cy + d);
                        g.DrawLine(pen, cx + d, cy - d, cx - d, cy + d);
                        break;

                    case LineIconKind.Folder:
                        var fRect = new RectangleF(cx - s * 0.36F, cy - s * 0.22F, s * 0.72F, s * 0.52F);
                        g.DrawLine(pen, fRect.X, fRect.Y, fRect.X + fRect.Width * 0.4F, fRect.Y);
                        g.DrawLine(pen, fRect.X + fRect.Width * 0.4F, fRect.Y, fRect.X + fRect.Width * 0.52F, fRect.Y + s * 0.08F);
                        g.DrawLine(pen, fRect.X + fRect.Width * 0.52F, fRect.Y + s * 0.08F, fRect.Right, fRect.Y + s * 0.08F);
                        g.DrawRectangle(pen, fRect.X, fRect.Y + s * 0.08F, fRect.Width, fRect.Height - s * 0.08F);
                        break;

                    case LineIconKind.FileDoc:
                    case LineIconKind.FilePdf:
                        float fw = s * 0.54F;
                        float fh = s * 0.72F;
                        float fx = cx - fw / 2F;
                        float fy = cy - fh / 2F;
                        float fold = s * 0.16F;
                        g.DrawLine(pen, fx, fy, fx + fw - fold, fy);
                        g.DrawLine(pen, fx + fw - fold, fy, fx + fw, fy + fold);
                        g.DrawLine(pen, fx + fw, fy + fold, fx + fw, fy + fh);
                        g.DrawLine(pen, fx + fw, fy + fh, fx, fy + fh);
                        g.DrawLine(pen, fx, fy + fh, fx, fy);
                        if (kind == LineIconKind.FilePdf)
                        {
                            g.DrawLine(pen, fx + fw * 0.22F, cy + s * 0.08F, fx + fw * 0.78F, cy + s * 0.08F);
                        }
                        break;

                    case LineIconKind.FileImage:
                        var imgRect = new RectangleF(cx - s * 0.34F, cy - s * 0.28F, s * 0.68F, s * 0.56F);
                        g.DrawRectangle(pen, imgRect.X, imgRect.Y, imgRect.Width, imgRect.Height);
                        g.DrawEllipse(pen, imgRect.X + imgRect.Width * 0.18F, imgRect.Y + imgRect.Height * 0.2F, s * 0.12F, s * 0.12F);
                        g.DrawLine(pen, imgRect.X + imgRect.Width * 0.15F, imgRect.Bottom - imgRect.Height * 0.2F, cx, cy);
                        g.DrawLine(pen, cx, cy, imgRect.Right - imgRect.Width * 0.15F, imgRect.Bottom - imgRect.Height * 0.25F);
                        break;

                    case LineIconKind.CheckCircle:
                        float r = s * 0.36F;
                        g.DrawEllipse(pen, cx - r, cy - r, r * 2F, r * 2F);
                        g.DrawLine(pen, cx - s * 0.15F, cy + s * 0.02F, cx - s * 0.03F, cy + s * 0.15F);
                        g.DrawLine(pen, cx - s * 0.03F, cy + s * 0.15F, cx + s * 0.18F, cy - s * 0.12F);
                        break;

                    case LineIconKind.PauseIcon:
                        float pr = s * 0.36F;
                        g.DrawEllipse(pen, cx - pr, cy - pr, pr * 2F, pr * 2F);
                        g.DrawLine(pen, cx - s * 0.09F, cy - s * 0.14F, cx - s * 0.09F, cy + s * 0.14F);
                        g.DrawLine(pen, cx + s * 0.09F, cy - s * 0.14F, cx + s * 0.09F, cy + s * 0.14F);
                        break;

                    case LineIconKind.ErrorCircle:
                        float er = s * 0.36F;
                        g.DrawEllipse(pen, cx - er, cy - er, er * 2F, er * 2F);
                        g.DrawLine(pen, cx - s * 0.12F, cy - s * 0.12F, cx + s * 0.12F, cy + s * 0.12F);
                        g.DrawLine(pen, cx + s * 0.12F, cy - s * 0.12F, cx - s * 0.12F, cy + s * 0.12F);
                        break;

                    case LineIconKind.Search:
                        float sr = s * 0.24F;
                        g.DrawEllipse(pen, cx - s * 0.32F, cy - s * 0.32F, sr * 2F, sr * 2F);
                        g.DrawLine(pen, cx + s * 0.06F, cy + s * 0.06F, cx + s * 0.32F, cy + s * 0.32F);
                        break;

                    case LineIconKind.Plus:
                        float prLen = s * 0.24F;
                        g.DrawLine(pen, cx - prLen, cy, cx + prLen, cy);
                        g.DrawLine(pen, cx, cy - prLen, cx, cy + prLen);
                        break;

                    case LineIconKind.MoreDots:
                        float dotR = Math.Max(1.3F, s * 0.05F);
                        using (var dotBrush = new SolidBrush(color))
                        {
                            g.FillEllipse(dotBrush, cx - s * 0.24F - dotR, cy - dotR, dotR * 2F, dotR * 2F);
                            g.FillEllipse(dotBrush, cx - dotR, cy - dotR, dotR * 2F, dotR * 2F);
                            g.FillEllipse(dotBrush, cx + s * 0.24F - dotR, cy - dotR, dotR * 2F, dotR * 2F);
                        }
                        break;
                }
            }
        }
    }
}
