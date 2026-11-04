using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

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
        Rectangle r = new Rectangle(0, 0, W, H);
        BitmapData da = bg.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        BitmapData db = blur.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int stride = da.Stride;
        byte[] A = new byte[stride * H];
        byte[] B = new byte[stride * H];
        Marshal.Copy(da.Scan0, A, 0, A.Length);
        Marshal.Copy(db.Scan0, B, 0, B.Length);

        float[] tint = new float[] { tintColor.B, tintColor.G, tintColor.R };
        int panelX = (int)Math.Round(W * Math.Max(15f, Math.Min(100f, panelPct)) / 100f);
        int shadowW = 24;
        float tintMix = IsDark(tintColor) ? 0.84f : 0.78f;
        float blurMix = 1f - tintMix;

        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                int i = y * stride + x * 4;
                if (x < panelX)
                {
                    // Лёгкое внутреннее оттенение к правому краю сайдбара
                    float edge = Math.Max(0f, 1f - (panelX - x) / 40f) * 0.06f;
                    for (int k = 0; k < 3; k++)
                    {
                        float bl = (B[i + k] * blurMix + tint[k] * tintMix) * (1f - edge);
                        A[i + k] = (byte)Math.Max(0f, Math.Min(255f, bl));
                    }
                }
                else if (x < panelX + shadowW && panelX < W)
                {
                    // Мягкая падающая тень справа от чёткой границы сайдбара
                    float st = 1f - (x - panelX) / (float)shadowW;
                    float dark = 1f - 0.45f * st * st;
                    for (int k = 0; k < 3; k++)
                        A[i + k] = (byte)(A[i + k] * dark);
                }
            }
        }
        Marshal.Copy(A, 0, da.Scan0, A.Length);
        bg.UnlockBits(da);
        blur.UnlockBits(db);
        blur.Dispose();

        // Викторианский латунный разделитель на границе сайдбара и тонкая внутренняя рамка
        if (panelX < W)
        {
            using (Graphics g = Graphics.FromImage(bg))
            {
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
        return bg;
    }
}
