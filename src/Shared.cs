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

    public static Bitmap Compose(Image src, float panelPct, Color tintColor, bool divider)
    {
        Bitmap sharp;
        Bitmap m = Build(src, out sharp, panelPct, tintColor, divider);
        sharp.Dispose();
        return m;
    }

    public static Bitmap Build(Image src, out Bitmap sharpOut, float panelPct, Color tintColor, bool divider)
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
        int tintAlpha = IsDark(tintColor) ? 214 : 199;

        using (Graphics g = Graphics.FromImage(bg))
        {
            if (divider)
            {
                // Закрытое размытие: сайдбар с чёткой границей, тенью и золотым/латунным разделителем
                int shadowW = 24;
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
            else
            {
                // Открытое размытие: мягкий плавный край без полоски и резких теней
                const int fadeW = 75;
                int solidW = Math.Max(0, panelX - fadeW / 2);
                int endX = Math.Min(W, solidW + fadeW);
                int actualFade = Math.Max(1, endX - solidW);

                // 1. Сплошная часть слева
                if (solidW > 0)
                {
                    Rectangle solidRect = new Rectangle(0, 0, solidW, H);
                    g.DrawImage(blur, solidRect, solidRect, GraphicsUnit.Pixel);
                    using (SolidBrush tintBrush = new SolidBrush(Color.FromArgb(tintAlpha, tintColor.R, tintColor.G, tintColor.B)))
                        g.FillRectangle(tintBrush, solidRect);
                }

                // 2. Плавный переход размытия и цвета вправо
                if (actualFade > 0 && solidW < W)
                {
                    Rectangle fadeRect = new Rectangle(solidW, 0, actualFade, H);
                    Bitmap fadeBmp = new Bitmap(actualFade, H, PixelFormat.Format32bppArgb);
                    using (Graphics fg = Graphics.FromImage(fadeBmp))
                    {
                        fg.DrawImage(blur, new Rectangle(0, 0, actualFade, H), fadeRect, GraphicsUnit.Pixel);
                        using (SolidBrush tintBrush = new SolidBrush(Color.FromArgb(tintAlpha, tintColor.R, tintColor.G, tintColor.B)))
                            fg.FillRectangle(tintBrush, 0, 0, actualFade, H);
                    }

                    Bitmap maskBmp = new Bitmap(actualFade, H, PixelFormat.Format32bppArgb);
                    using (Graphics mg = Graphics.FromImage(maskBmp))
                    {
                        using (LinearGradientBrush lgb = new LinearGradientBrush(new Rectangle(0, 0, actualFade, H),
                            Color.FromArgb(255, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Horizontal))
                        {
                            mg.FillRectangle(lgb, 0, 0, actualFade, H);
                        }
                    }

                    BitmapData da = fadeBmp.LockBits(new Rectangle(0, 0, actualFade, H), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                    BitmapData dm = maskBmp.LockBits(new Rectangle(0, 0, actualFade, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    int stride = da.Stride;
                    int bytes = stride * H;
                    byte[] raw = new byte[bytes];
                    byte[] mraw = new byte[bytes];
                    System.Runtime.InteropServices.Marshal.Copy(da.Scan0, raw, 0, bytes);
                    System.Runtime.InteropServices.Marshal.Copy(dm.Scan0, mraw, 0, bytes);

                    for (int y = 0; y < H; y++)
                    {
                        for (int x = 0; x < actualFade; x++)
                        {
                            int idx = y * stride + x * 4;
                            int alpha = mraw[idx];
                            raw[idx + 3] = (byte)((raw[idx + 3] * alpha) / 255);
                        }
                    }

                    System.Runtime.InteropServices.Marshal.Copy(raw, 0, da.Scan0, bytes);
                    fadeBmp.UnlockBits(da);
                    maskBmp.UnlockBits(dm);
                    maskBmp.Dispose();

                    g.DrawImage(fadeBmp, solidW, 0);
                    fadeBmp.Dispose();
                }
            }
        }

        blur.Dispose();
        return bg;
    }
}
