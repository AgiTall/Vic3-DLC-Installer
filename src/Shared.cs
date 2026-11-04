using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Общий код оформления: используется и установщиком, и предпросмотром в админке.
public static class Look
{
    public const int W = 640;
    public const int H = 480;

    public static Color ParseColor(string hex, Color def)
    {
        try
        {
            if (string.IsNullOrEmpty(hex)) return def;
            return ColorTranslator.FromHtml(hex.Trim());
        }
        catch { return def; }
    }

    public static bool IsDark(Color c)
    {
        return (c.R * 299 + c.G * 587 + c.B * 114) / 1000 < 140;
    }

    static Bitmap Resize(Bitmap src, int w, int h)
    {
        Bitmap d = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(d))
        using (ImageAttributes ia = new ImageAttributes())
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            ia.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
        }
        return d;
    }

    // Фон "cover" + акриловая панель слева с чёткой латунной/золотой границей.
    public static Bitmap Compose(Image src, float panelPct, Color tintColor)
    {
        Bitmap sharp;
        Bitmap m = Build(src, out sharp, panelPct, tintColor);
        sharp.Dispose();
        return m;
    }

    // sharp - чистый фон, возвращается вариант с размытой карточкой/сайдбаром слева.
    public static Bitmap Build(Image src, out Bitmap sharpOut, float panelPct, Color tintColor)
    {
        Bitmap bg = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bg))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Black);
            if (src != null)
            {
                float sc = Math.Max((float)W / src.Width, (float)H / src.Height);
                float dw = src.Width * sc, dh = src.Height * sc;
                g.DrawImage(src, (W - dw) / 2f, (H - dh) / 2f, dw, dh);
            }
        }

        Bitmap a = Resize(bg, W / 5, H / 5);
        Bitmap b = Resize(a, W / 12, H / 12);
        Bitmap c = Resize(b, W / 5, H / 5);
        Bitmap blur = Resize(c, W, H);
        a.Dispose(); b.Dispose(); c.Dispose();

        sharpOut = bg;
        Bitmap mixed = new Bitmap(bg);
        bg = mixed;

        int panelX = (int)Math.Round(W * Math.Max(15f, Math.Min(100f, panelPct)) / 100f);
        int shadowW = 24;
        int tintAlpha = IsDark(tintColor) ? 214 : 199;

        using (Graphics g = Graphics.FromImage(bg))
        {
            if (panelX > 0)
            {
                Rectangle panelRect = new Rectangle(0, 0, panelX, H);
                g.DrawImage(blur, panelRect, panelRect, GraphicsUnit.Pixel);
                using (SolidBrush tintBrush = new SolidBrush(Color.FromArgb(tintAlpha, tintColor.R, tintColor.G, tintColor.B)))
                    g.FillRectangle(tintBrush, panelRect);

                if (panelX >= 40)
                {
                    Rectangle edgeRect = new Rectangle(panelX - 40, 0, 40, H);
                    using (LinearGradientBrush edgeBrush = new LinearGradientBrush(edgeRect, Color.FromArgb(0, 0, 0, 0), Color.FromArgb(16, 0, 0, 0), LinearGradientMode.Horizontal))
                        g.FillRectangle(edgeBrush, edgeRect);
                }
            }

            if (panelX < W)
            {
                int sw = Math.Min(shadowW, W - panelX);
                if (sw > 1)
                {
                    Rectangle shadowRect = new Rectangle(panelX, 0, sw, H);
                    using (LinearGradientBrush shadowBrush = new LinearGradientBrush(shadowRect, Color.FromArgb(115, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), LinearGradientMode.Horizontal))
                        g.FillRectangle(shadowBrush, shadowRect);
                }

                Color brass = IsDark(tintColor) ? Color.FromArgb(185, 178, 142, 86) : Color.FromArgb(165, 140, 105, 60);
                Color inner = IsDark(tintColor) ? Color.FromArgb(40, 255, 240, 200) : Color.FromArgb(90, 255, 255, 255);
                using (Pen pBrass = new Pen(brass, 1f))
                using (Pen pInner = new Pen(inner, 1f))
                {
                    g.DrawLine(pInner, panelX - 2, 0, panelX - 2, H);
                    g.DrawLine(pBrass, panelX - 1, 0, panelX - 1, H);
                }
            }
        }

        blur.Dispose();
        return bg;
    }
}
