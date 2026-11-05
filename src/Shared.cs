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

    public static Bitmap Compose(Image src, float panelPct, Color tintColor, bool divider, int fadeWidth)
    {
        Bitmap sharp;
        Bitmap m = Build(src, out sharp, panelPct, tintColor, divider, fadeWidth);
        sharp.Dispose();
        return m;
    }

    public static Bitmap Build(Image src, out Bitmap sharpOut, float panelPct, Color tintColor, bool divider, int fadeWidth)
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

        int panelX = (int)Math.Round(W * Math.Max(5f, Math.Min(100f, panelPct)) / 100f);
        int fadeW = Math.Max(0, Math.Min(300, fadeWidth));
        int tintAlpha = IsDark(tintColor) ? 214 : 199;

        // Готовим слой размытия с тонировкой
        using (Bitmap panelBmp = new Bitmap(W, H, PixelFormat.Format32bppArgb))
        {
            using (Graphics pg = Graphics.FromImage(panelBmp))
            {
                pg.DrawImageUnscaled(blur, 0, 0);
                using (SolidBrush tintBrush = new SolidBrush(Color.FromArgb(tintAlpha, tintColor.R, tintColor.G, tintColor.B)))
                    pg.FillRectangle(tintBrush, 0, 0, W, H);
            }

            using (Graphics g = Graphics.FromImage(bg))
            {
                int solidW = Math.Max(0, panelX - fadeW);
                int endX = divider ? panelX : Math.Min(W, Math.Max(panelX, solidW + fadeW));
                int actualFade = Math.Max(0, endX - solidW);

                // 1. Сплошная область размытия слева
                if (solidW > 0)
                {
                    Rectangle solidRect = new Rectangle(0, 0, Math.Min(W, solidW), H);
                    g.DrawImage(panelBmp, solidRect, solidRect, GraphicsUnit.Pixel);
                }

                // 2. Плавный градиент размытие -> прозрачность (smoothstep)
                if (actualFade > 0 && solidW < W)
                {
                    using (ImageAttributes ia = new ImageAttributes())
                    {
                        ColorMatrix cm = new ColorMatrix();
                        for (int x = 0; x < actualFade && (solidW + x) < W; x++)
                        {
                            float t = (x + 0.5f) / actualFade;
                            float keep = 1f - t * t * (3f - 2f * t);
                            cm.Matrix33 = keep;
                            ia.SetColorMatrix(cm);
                            int px = solidW + x;
                            g.DrawImage(panelBmp, new Rectangle(px, 0, 1, H), px, 0, 1, H, GraphicsUnit.Pixel, ia);
                        }
                    }
                }

                // 3. Если включено закрытое размытие — рисуем латунный разделитель и падающую тень
                if (divider && panelX < W)
                {
                    if (fadeW == 0 && panelX >= 40)
                    {
                        Rectangle edgeRect = new Rectangle(panelX - 40, 0, 40, H);
                        using (LinearGradientBrush edgeBrush = new LinearGradientBrush(edgeRect, Color.FromArgb(0, 0, 0, 0), Color.FromArgb(16, 0, 0, 0), LinearGradientMode.Horizontal))
                            g.FillRectangle(edgeBrush, edgeRect);
                    }

                    int sw = Math.Min(24, W - panelX);
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
                        if (panelX >= 2) g.DrawLine(pInner, panelX - 2, 0, panelX - 2, H);
                        g.DrawLine(pBrass, panelX - 1, 0, panelX - 1, H);
                    }
                }
            }
        }

        blur.Dispose();
        return bg;
    }
}
