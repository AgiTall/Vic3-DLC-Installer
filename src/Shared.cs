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
        float tintMix = IsDark(tintColor) ? 0.84f : 0.78f;
        float blurMix = 1f - tintMix;

        const int Fade = 80; // ширина плавного размытого края
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float t = (panelX + Fade - x) / (float)Fade;
                if (t <= 0f) continue;
                if (t > 1f) t = 1f;
                t = t * t * (3f - 2f * t);
                int i = y * stride + x * 4;
                for (int k = 0; k < 3; k++)
                {
                    float bl = B[i + k] * blurMix + tint[k] * tintMix;
                    A[i + k] = (byte)(A[i + k] + (bl - A[i + k]) * t);
                }
            }
        }        Marshal.Copy(A, 0, da.Scan0, A.Length);
        bg.UnlockBits(da);
        blur.UnlockBits(db);
        blur.Dispose();

        return bg;
    }
}
