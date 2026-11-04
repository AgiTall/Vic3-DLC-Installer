using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// Установщик. Архив с файлами дописан в конец этого exe:
// [exe][zip][long длина zip][long магическое число]
namespace Vic3Installer
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    class SubStream : Stream
    {
        readonly Stream b; readonly long off, len; long pos;
        public SubStream(Stream b, long off, long len) { this.b = b; this.off = off; this.len = len; }
        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return len; } }
        public override long Position { get { return pos; } set { pos = value; } }
        public override void Flush() { }
        public override int Read(byte[] buf, int o, int c)
        {
            long rem = len - pos;
            if (rem <= 0) return 0;
            if (c > rem) c = (int)rem;
            b.Position = off + pos;
            int n = b.Read(buf, o, c);
            pos += n;
            return n;
        }
        public override long Seek(long o, SeekOrigin so)
        {
            if (so == SeekOrigin.Begin) pos = o;
            else if (so == SeekOrigin.Current) pos += o;
            else pos = len + o;
            return pos;
        }
        public override void SetLength(long v) { throw new NotSupportedException(); }
        public override void Write(byte[] buf, int o, int c) { throw new NotSupportedException(); }
    }

    class Cfg
    {
        public string Title = "Victoria 3", Subtitle = "", Info = "", Footer = "", Accent = "#7A1F25", TitleColor = "#5A1A1E";
        public static Cfg Load()
        {
            Cfg c = new Cfg();
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("config.txt"))
            {
                if (s == null) return c;
                using (StreamReader r = new StreamReader(s, System.Text.Encoding.UTF8))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        int i = line.IndexOf('=');
                        if (i <= 0) continue;
                        string k = line.Substring(0, i), v = line.Substring(i + 1).Replace("\\n", "\n");
                        if (k == "title") c.Title = v;
                        else if (k == "subtitle") c.Subtitle = v;
                        else if (k == "info") c.Info = v;
                        else if (k == "footer") c.Footer = v;
                        else if (k == "accent") c.Accent = v;
                        else if (k == "titlecolor") c.TitleColor = v;
                    }
                }
            }
            return c;
        }
    }

    static class Steam
    {
        const string AppId = "529340"; // Victoria 3

        static void AddRoot(List<string> l, string p)
        {
            if (string.IsNullOrEmpty(p)) return;
            try
            {
                p = p.Replace('/', '\\').TrimEnd('\\');
                if (Directory.Exists(p) && !l.Contains(p, StringComparer.OrdinalIgnoreCase)) l.Add(p);
            }
            catch { }
        }

        static bool Contains(this List<string> l, string v, StringComparer c)
        {
            foreach (string s in l) if (c.Equals(s, v)) return true;
            return false;
        }

        public static string FindVic3()
        {
            List<string> steamRoots = new List<string>();
            try { AddRoot(steamRoots, Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string); } catch { }
            try { AddRoot(steamRoots, Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Valve\Steam", "InstallPath", null) as string); } catch { }
            try { AddRoot(steamRoots, Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string); } catch { }

            List<string> libs = new List<string>();
            foreach (string sr in steamRoots)
            {
                AddRoot(libs, sr);
                string vdf = Path.Combine(sr, @"steamapps\libraryfolders.vdf");
                try
                {
                    if (File.Exists(vdf))
                    {
                        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                            AddRoot(libs, m.Groups[1].Value.Replace("\\\\", "\\"));
                    }
                }
                catch { }
            }
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (!d.IsReady) continue;
                    string r = d.RootDirectory.FullName;
                    AddRoot(libs, r + "SteamLibrary");
                    AddRoot(libs, r + "Steam");
                    AddRoot(libs, r + @"Program Files (x86)\Steam");
                    AddRoot(libs, r + @"Games\Steam");
                }
            }
            catch { }

            foreach (string lib in libs)
            {
                string common = Path.Combine(lib, @"steamapps\common");
                string manifest = Path.Combine(lib, @"steamapps\appmanifest_" + AppId + ".acf");
                try
                {
                    if (File.Exists(manifest))
                    {
                        Match m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                        if (m.Success)
                        {
                            string p = Path.Combine(common, m.Groups[1].Value);
                            if (IsGameDir(p)) return p;
                        }
                    }
                }
                catch { }
                string def = Path.Combine(common, "Victoria 3");
                if (IsGameDir(def)) return def;
            }
            return null;
        }

        public static bool IsGameDir(string p)
        {
            try { return !string.IsNullOrEmpty(p) && Directory.Exists(Path.Combine(p, "binaries")) && Directory.Exists(Path.Combine(p, "game")); }
            catch { return false; }
        }
    }

    class FlatButton : Control
    {
        bool hover, down;
        public Color Accent = Color.Crimson;
        public bool Primary = true;
        public Color TextColor = Color.White;
        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Semibold", 10.5f);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill;
            if (!Enabled) fill = Color.FromArgb(150, 70, 72, 80);
            else if (Primary) fill = down ? ControlPaint.Dark(Accent, 0.1f) : (hover ? ControlPaint.Light(Accent, 0.25f) : Accent);
            else fill = Color.FromArgb(down ? 235 : (hover ? 225 : 205), 240, 228, 208);
            using (GraphicsPath p = Round(r, 8))
            using (SolidBrush b = new SolidBrush(fill))
                g.FillPath(b, p);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height),
                Enabled ? (Primary ? Color.White : TextColor) : Color.FromArgb(160, 160, 165),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
        public static GraphicsPath Round(Rectangle r, int d)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    class GfxProgress : Control
    {
        int val;
        public Color Accent = Color.Crimson;
        public int Value { get { return val; } set { val = Math.Max(0, Math.Min(1000, value)); Invalidate(); } }
        public GfxProgress()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = FlatButton.Round(r, Height - 1))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(70, 70, 45, 35)))
                g.FillPath(b, p);
            int w = (int)((Width - 1) * (val / 1000.0));
            if (w > Height)
            {
                using (GraphicsPath p = FlatButton.Round(new Rectangle(0, 0, w, Height - 1), Height - 1))
                using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(w, 2), Height), ControlPaint.Dark(Accent, 0.1f), ControlPaint.Light(Accent, 0.3f), 0f))
                    g.FillPath(b, p);
            }
        }
    }

    class MainForm : Form
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, int w, int l);

        Cfg cfg;
        Color accent, titleColor;
        Bitmap sharp, blurred;
        float blurAmt = 1f, blurTarget = 1f;
        System.Windows.Forms.Timer anim;
        Label lblTitle, lblSub, lblInfo, lblPathCap, lblStatus, lblFooter;
        TextBox txtPath;
        FlatButton btnBrowse, btnInstall, btnStop, btnCancel;
        GfxProgress bar;
        bool busy, done, paused, cancel, forceClose;
        ManualResetEvent runEv = new ManualResetEvent(true);

        static readonly Color InkDark = Color.FromArgb(62, 42, 34);
        const int CX = 24, CW = 250;

        public MainForm()
        {
            cfg = Cfg.Load();
            accent = Look.ParseColor(cfg.Accent, Color.FromArgb(122, 31, 37));
            titleColor = Look.ParseColor(cfg.TitleColor, Color.FromArgb(90, 26, 30));

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Look.W, Look.H);
            DoubleBuffered = true;
            Text = cfg.Title;
            BackColor = Color.Black;
            Font = new Font("Segoe UI", 9f);

            Image src = null;
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("bg"))
                {
                    if (s != null)
                    {
                        MemoryStream ms = new MemoryStream();
                        s.CopyTo(ms);
                        ms.Position = 0;
                        src = Image.FromStream(ms);
                    }
                }
            }
            catch { }
            blurred = Look.Build(src, out sharp);

            lblTitle = MakeLabel(cfg.Title, new Font("Segoe UI Semibold", 20f), titleColor);
            lblSub = MakeLabel(cfg.Subtitle, new Font("Segoe UI Semibold", 10.5f), accent);
            lblInfo = MakeLabel(cfg.Info, new Font("Segoe UI", 8.75f), InkDark);
            lblPathCap = MakeLabel("Папка Victoria 3", new Font("Segoe UI", 8.5f), Color.FromArgb(120, 92, 76));
            lblStatus = MakeLabel("Поиск Steam-версии игры…", new Font("Segoe UI", 8.75f), InkDark);
            lblFooter = MakeLabel(cfg.Footer, new Font("Segoe UI", 7.5f), Color.FromArgb(120, 98, 84));

            txtPath = new TextBox();
            txtPath.BorderStyle = BorderStyle.FixedSingle;
            txtPath.BackColor = Color.FromArgb(250, 244, 232);
            txtPath.ForeColor = InkDark;
            txtPath.Font = new Font("Segoe UI", 9f);
            Controls.Add(txtPath);

            btnBrowse = MakeBtn("Обзор", false);
            btnBrowse.Font = new Font("Segoe UI", 8.75f);
            btnBrowse.Click += delegate { Browse(); };

            bar = new GfxProgress();
            bar.Accent = accent;
            Controls.Add(bar);

            btnInstall = MakeBtn("Установить", true);
            btnInstall.Click += delegate { OnMainClick(); };
            btnStop = MakeBtn("Стоп", false);
            btnStop.Click += delegate { TogglePause(); };
            btnCancel = MakeBtn("Отмена", false);
            btnCancel.Click += delegate { AskCancel(); };

            FlatButton close = MakeBtn("✕", false);
            close.SetBounds(Look.W - 34, 10, 28, 24); close.Font = new Font("Segoe UI", 9f); 
            close.Click += delegate { if (busy) AskCancel(); else Close(); };
            FlatButton min = MakeBtn("–", false);
            min.SetBounds(Look.W - 66, 10, 28, 24); min.Font = new Font("Segoe UI", 9f); 
            min.Click += delegate { WindowState = FormWindowState.Minimized; };

            MouseDown += Drag;
            foreach (Control c in new Control[] { lblTitle, lblSub, lblInfo, lblPathCap, lblStatus, lblFooter }) c.MouseDown += Drag;

            anim = new System.Windows.Forms.Timer();
            anim.Interval = 15;
            anim.Tick += delegate
            {
                float step = 0.045f;
                if (Math.Abs(blurAmt - blurTarget) <= step) { blurAmt = blurTarget; anim.Stop(); }
                else blurAmt += blurAmt < blurTarget ? step : -step;
                Invalidate();
            };

            SetLayout(false);
            Shown += delegate { Detect(); };
        }

        FlatButton MakeBtn(string t, bool primary)
        {
            FlatButton b = new FlatButton();
            b.Primary = primary; b.Accent = accent; b.Text = t;
            if (!primary) b.TextColor = InkDark;
            Controls.Add(b);
            return b;
        }

        Label MakeLabel(string text, Font f, Color c)
        {
            Label l = new Label();
            l.AutoSize = false;
            l.BackColor = Color.Transparent;
            l.ForeColor = c;
            l.Font = f;
            l.Text = text;
            Controls.Add(l);
            return l;
        }

        // Два режима: ввод параметров (размытая панель слева) и установка (чистый фон).
        void SetLayout(bool inst)
        {
            lblSub.Visible = lblInfo.Visible = lblPathCap.Visible = txtPath.Visible = btnBrowse.Visible = !inst;
            bar.Visible = btnStop.Visible = btnCancel.Visible = inst;
            if (!inst)
            {
                lblTitle.SetBounds(CX, 44, CW, 80);
                lblTitle.TextAlign = ContentAlignment.BottomLeft;
                FitTitle(CW, 80);
                lblSub.SetBounds(CX, 128, CW, 22);
                lblInfo.SetBounds(CX, 156, CW, 116);
                lblPathCap.SetBounds(CX, 276, CW, 18);
                txtPath.SetBounds(CX, 296, 180, 26);
                btnBrowse.SetBounds(CX + 186, 296, 64, 25);
                lblStatus.SetBounds(CX, 328, CW, 50);
                btnInstall.SetBounds(CX, 392, CW, 42);
                lblFooter.SetBounds(CX, 444, CW, 30);
                btnInstall.Visible = true;
            }
            else
            {
                lblTitle.SetBounds(CX, 16, 520, 44);
                lblTitle.TextAlign = ContentAlignment.TopLeft;
                FitTitle(520, 44);
                lblStatus.SetBounds(CX, 390, 592, 18);
                bar.SetBounds(CX, 412, 592, 8);
                btnStop.SetBounds(CX, 430, 100, 30);
                btnCancel.SetBounds(CX + 108, 430, 100, 30);
                btnInstall.SetBounds(CX, 430, 120, 30);
                lblFooter.SetBounds(CX, 464, 592, 14);
                btnInstall.Visible = false;
            }
        }

        void SetTarget(float t) { blurTarget = t; anim.Start(); }

        void FitTitle(int w, int h)
        {
            float size = 22f;
            while (size > 11f)
            {
                using (Font f = new Font("Segoe UI Semibold", size))
                {
                    Size s = TextRenderer.MeasureText(lblTitle.Text, f, new Size(w, 1000), TextFormatFlags.WordBreak);
                    if (s.Height <= h) { lblTitle.Font = new Font("Segoe UI Semibold", size); return; }
                }
                size -= 1f;
            }
            lblTitle.Font = new Font("Segoe UI Semibold", 11f);
        }

        void Drag(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, 2, 0);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.DrawImageUnscaled(sharp, 0, 0);
            if (blurAmt > 0.004f)
            {
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ColorMatrix cm = new ColorMatrix();
                    cm.Matrix33 = blurAmt;
                    ia.SetColorMatrix(cm);
                    g.DrawImage(blurred, new Rectangle(0, 0, Look.W, Look.H), 0, 0, Look.W, Look.H, GraphicsUnit.Pixel, ia);
                }
            }
            float k = 1f - blurAmt;
            if (k > 0.01f)
            {
                Rectangle top = new Rectangle(0, 0, Look.W, 80);
                using (LinearGradientBrush b = new LinearGradientBrush(top, Color.FromArgb((int)(150 * k), 238, 226, 206), Color.FromArgb(0, 238, 226, 206), 90f))
                    g.FillRectangle(b, top);
                Rectangle bot = new Rectangle(0, Look.H - 100, Look.W, 100);
                using (LinearGradientBrush b = new LinearGradientBrush(bot, Color.FromArgb(0, 238, 226, 206), Color.FromArgb((int)(195 * k), 238, 226, 206), 90f))
                    g.FillRectangle(b, bot);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (Pen p = new Pen(Color.FromArgb(110, 60, 40, 30)))
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy && !forceClose) e.Cancel = true;
            base.OnFormClosing(e);
        }

        void Detect()
        {
            string p = null;
            try { p = Steam.FindVic3(); } catch { }
            if (p != null)
            {
                txtPath.Text = p;
                SetStatus("Найдена Steam-версия игры. Нажмите «Установить».");
            }
            else SetStatus("Игра автоматически не найдена. Укажите папку Victoria 3 вручную.");
        }

        void SetStatus(string s) { lblStatus.Text = s; }

        void Browse()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Выберите корневую папку Victoria 3 (где лежат binaries и game)";
                if (Directory.Exists(txtPath.Text)) d.SelectedPath = txtPath.Text;
                if (d.ShowDialog(this) == DialogResult.OK) txtPath.Text = d.SelectedPath;
            }
        }

        void TogglePause()
        {
            if (!busy) return;
            paused = !paused;
            if (paused) { runEv.Reset(); btnStop.Text = "Продолжить"; SetStatus("Пауза. Нажмите «Продолжить» или «Отмена»."); }
            else { runEv.Set(); btnStop.Text = "Стоп"; SetStatus("Копирование файлов…"); }
        }

        void AskCancel()
        {
            if (!busy) return;
            bool wasPaused = paused;
            if (!wasPaused) runEv.Reset();
            DialogResult r = MessageBox.Show(this, "Отменить установку?\nУже скопированные файлы останутся, а старые папки dlc могли быть удалены — установку потом нужно будет повторить.",
                "Отмена установки", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes) { cancel = true; runEv.Set(); SetStatus("Отмена…"); }
            else if (!wasPaused) runEv.Set();
        }

        void OnMainClick()
        {
            if (done) { forceClose = true; Close(); return; }
            if (busy) return;
            string root = txtPath.Text.Trim().Trim('"');
            if (!Steam.IsGameDir(root))
            {
                MessageBox.Show(this, "В выбранной папке не найдены «binaries» и «game».\nУкажите корневую папку Victoria 3.", "Папка игры", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (Process.GetProcessesByName("victoria3").Length > 0)
            {
                MessageBox.Show(this, "Victoria 3 запущена. Закройте игру и повторите установку.", "Игра запущена", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            busy = true; paused = false; cancel = false;
            runEv.Set();
            btnStop.Text = "Стоп";
            bar.Value = 0;
            SetLayout(true);
            SetStatus("Подготовка…");
            SetTarget(0f);
            Thread t = new Thread(delegate() { DoInstall(root); });
            t.IsBackground = true;
            t.Start();
        }

        static List<string> Targets(string root, string name)
        {
            name = name.Replace('/', '\\').TrimStart('\\');
            List<string> l = new List<string>();
            if (name.StartsWith("dlc\\", StringComparison.OrdinalIgnoreCase))
            {
                l.Add(Path.Combine(root, "game\\" + name));
                l.Add(Path.Combine(root, name));
            }
            else l.Add(Path.Combine(root, name));
            string full = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            foreach (string t in l)
                if (!Path.GetFullPath(t).StartsWith(full, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Недопустимый путь в архиве: " + name);
            return l;
        }

        void DoInstall(string root)
        {
            try
            {
                string exe = Assembly.GetExecutingAssembly().Location;
                using (FileStream fs = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length < 16) throw new InvalidDataException("В установщике нет данных.");
                    byte[] tr = new byte[16];
                    fs.Position = fs.Length - 16;
                    fs.Read(tr, 0, 16);
                    long zlen = BitConverter.ToInt64(tr, 0);
                    long magic = BitConverter.ToInt64(tr, 8);
                    if (magic != 0x56494333494E5354L || zlen <= 0 || zlen > fs.Length - 16)
                        throw new InvalidDataException("В установщике нет данных (запустите собранный exe).");
                    using (SubStream sub = new SubStream(fs, fs.Length - 16 - zlen, zlen))
                    using (ZipArchive zip = new ZipArchive(sub, ZipArchiveMode.Read))
                    {
                        List<ZipArchiveEntry> entries = new List<ZipArchiveEntry>();
                        long total = 0;
                        bool hasDlc = false;
                        foreach (ZipArchiveEntry e in zip.Entries)
                        {
                            if (string.IsNullOrEmpty(e.Name)) continue;
                            entries.Add(e);
                            total += e.Length * Targets(root, e.FullName).Count;
                            if (e.FullName.Replace('\\', '/').StartsWith("dlc/", StringComparison.OrdinalIgnoreCase)) hasDlc = true;
                        }
                        if (total == 0) total = 1;

                        runEv.WaitOne();
                        if (cancel) throw new OperationCanceledException();
                        if (hasDlc)
                        {
                            Status("Удаление старых папок dlc…");
                            foreach (string d in new string[] { Path.Combine(root, "dlc"), Path.Combine(root, "game\\dlc") })
                                if (Directory.Exists(d)) Directory.Delete(d, true);
                        }

                        long done0 = 0;
                        int lastPm = -1;
                        byte[] buf = new byte[1024 * 1024];
                        int idx = 0;
                        foreach (ZipArchiveEntry e in entries)
                        {
                            idx++;
                            runEv.WaitOne();
                            if (cancel) throw new OperationCanceledException();
                            List<string> targets = Targets(root, e.FullName);
                            List<FileStream> outs = new List<FileStream>();
                            try
                            {
                                foreach (string t in targets)
                                {
                                    Directory.CreateDirectory(Path.GetDirectoryName(t));
                                    if (File.Exists(t)) File.SetAttributes(t, FileAttributes.Normal);
                                    outs.Add(new FileStream(t, FileMode.Create, FileAccess.Write, FileShare.None, 81920));
                                }
                                using (Stream ins = e.Open())
                                {
                                    int n;
                                    while ((n = ins.Read(buf, 0, buf.Length)) > 0)
                                    {
                                        runEv.WaitOne();
                                        if (cancel) throw new OperationCanceledException();
                                        foreach (FileStream o in outs) o.Write(buf, 0, n);
                                        done0 += n * outs.Count;
                                        int pm = (int)(done0 * 1000 / total);
                                        if (pm != lastPm) { lastPm = pm; Progress(pm, "Копирование файлов… " + (pm / 10) + "%  (" + idx + " из " + entries.Count + ")"); }
                                    }
                                }
                            }
                            finally { foreach (FileStream o in outs) o.Dispose(); }
                        }
                    }
                }
                Finish(true, "Установка завершена! Запускайте игру через Steam.");
            }
            catch (OperationCanceledException)
            {
                Finish(false, "Установка отменена. Нажмите «Повторить», чтобы установить заново.");
            }
            catch (UnauthorizedAccessException ex)
            {
                Finish(false, "Нет доступа к файлам: " + ex.Message + "\nЗакройте игру/Steam-лаунчер или запустите от администратора.");
            }
            catch (Exception ex)
            {
                Finish(false, "Ошибка: " + ex.Message);
            }
        }

        void Status(string s) { BeginInvoke(new MethodInvoker(delegate { SetStatus(s); })); }
        void Progress(int pm, string s) { BeginInvoke(new MethodInvoker(delegate { if (!paused) { bar.Value = pm; SetStatus(s); } else bar.Value = pm; })); }

        void Finish(bool ok, string msg)
        {
            BeginInvoke(new MethodInvoker(delegate
            {
                busy = false; paused = false;
                SetStatus(msg);
                if (ok)
                {
                    done = true;
                    bar.Value = 1000;
                    btnStop.Visible = btnCancel.Visible = false;
                    btnInstall.Text = "Готово";
                    btnInstall.Visible = true;
                }
                else
                {
                    SetLayout(false);
                    btnInstall.Text = "Повторить";
                    SetTarget(1f);
                }
            }));
        }
    }
}




