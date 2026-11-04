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
    public const int PanelX = 240;   // конец сплошной размытой панели (слева)
    public const int Fade = 80;      // ширина плавного перехода

    public static Color ParseColor(string hex, Color def)
    {
        try
        {
            if (string.IsNullOrEmpty(hex)) return def;
            return ColorTranslator.FromHtml(hex.Trim());
        }
        catch { return def; }
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

    // Фон "cover" + размытая тёмная панель справа с плавным краем.
    public static Bitmap Compose(Image src)
    {
        Bitmap sharp;
        Bitmap m = Build(src, out sharp);
        sharp.Dispose();
        return m;
    }

    // sharp - чистый фон, возвращается размытый вариант (молочно-кремовая панель слева).
    public static Bitmap Build(Image src, out Bitmap sharpOut)
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

        Bitmap a = Resize(bg, W / 6, H / 6);
        Bitmap b = Resize(a, W / 14, H / 14);
        Bitmap c = Resize(b, W / 6, H / 6);
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
        float[] tint = new float[] { 200f, 222f, 236f }; // B,G,R (кремовый)
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float t = (PanelX + Fade - x) / (float)Fade;
                if (t <= 0f) continue;
                if (t > 1f) t = 1f;
                t = t * t * (3f - 2f * t);
                int i = y * stride + x * 4;
                for (int k = 0; k < 3; k++)
                {
                    float bl = B[i + k] * 0.30f + tint[k] * 0.70f;
                    A[i + k] = (byte)(A[i + k] + (bl - A[i + k]) * t);
                }
            }
        }
        Marshal.Copy(A, 0, da.Scan0, A.Length);
        bg.UnlockBits(da);
        blur.UnlockBits(db);
        blur.Dispose();
        return bg;
    }
}



