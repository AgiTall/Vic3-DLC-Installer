using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.CSharp;

// Приложение администратора: управляет содержимым, оформлением и собирает новый exe-установщик.
namespace Vic3Admin
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new AdminForm());
        }
    }

    class AdminForm : Form
    {
        static readonly Color Bg = Color.FromArgb(22, 24, 30);
        static readonly Color Card = Color.FromArgb(32, 35, 44);
        static readonly Color Field = Color.FromArgb(42, 46, 57);
        static readonly Color Fg = Color.FromArgb(230, 232, 238);
        static readonly Color Dim = Color.FromArgb(150, 156, 170);

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string iniPath;
        TextBox tTitle, tSub, tInfo, tFooter, tBg, tPayload, tOut;
        Button bAccent, bBuild;
        Color accent = Color.FromArgb(142, 36, 44);
        Color titleColor = Color.FromArgb(230, 210, 181);
        Button bTitleColor, bBlurColor;
        Color blurColor = Color.FromArgb(27, 30, 36);
        CheckBox chkDivider;
        TrackBar nPanel, nFade, nTitleW, nTextW;
        PictureBox preview;
        Bitmap canvas;
        ListView list;
        ProgressBar bar;
        Label lblStatus;
        List<Control> lockables = new List<Control>();
        bool busy;

        public AdminForm()
        {
            iniPath = Path.Combine(baseDir, "admin.ini");
            Text = "Vic3 Installer — Админ-панель";
            ClientSize = new Size(940, 900);
            MinimumSize = new Size(960, 940);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font("Segoe UI", 9f);

            Label head = new Label();
            head.Text = "Админ-панель установщика Victoria 3";
            head.Font = new Font("Segoe UI Semibold", 15f);
            head.ForeColor = Color.White;
            head.SetBounds(20, 12, 700, 32);
            Controls.Add(head);

            // ---- Левая колонка: оформление ----
            int x = 20, w = 420, y = 56;
            Section("ОФОРМЛЕНИЕ", x, y); y += 24;
            tTitle = Row("Заголовок (текущее DLC)", x, ref y, w, 0);
            tSub = Row("Подзаголовок", x, ref y, w, 0);
            tInfo = Row("Информация", x, ref y, w, 78);
            tFooter = Row("Подпись внизу", x, ref y, w, 40);

            Section("ЦВЕТА И РАЗМЕРЫ", x, y); y += 22;
            TableLayoutPanel tl = new TableLayoutPanel();
            tl.SetBounds(x, y, w, 8 * 36);
            tl.ColumnCount = 3; tl.RowCount = 8;
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            for (int i = 0; i < 8; i++) tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            Controls.Add(tl);
            bAccent = TBtn(tl, 0, "Цвет кнопок (акцент)");
            bTitleColor = TBtn(tl, 1, "Цвет заголовка");
            bBlurColor = TBtn(tl, 2, "Цвет размытия");
            bAccent.Click += delegate { PickColor(); };
            bTitleColor.Click += delegate { using (ColorDialog d = new ColorDialog()) { d.Color = titleColor; d.FullOpen = true; if (d.ShowDialog(this) == DialogResult.OK) { titleColor = d.Color; bTitleColor.BackColor = titleColor; bTitleColor.ForeColor = Look.IsDark(titleColor) ? Color.White : Color.Black; preview.Invalidate(); } } };
            bBlurColor.Click += delegate { using (ColorDialog d = new ColorDialog()) { d.Color = blurColor; d.FullOpen = true; if (d.ShowDialog(this) == DialogResult.OK) { blurColor = d.Color; bBlurColor.BackColor = blurColor; bBlurColor.ForeColor = Look.IsDark(blurColor) ? Color.White : Color.Black; LoadBg(); } } };
            chkDivider = TCheck(tl, 3, "Тип размытия", "Закрытое (с золотой полоской)", true);
            chkDivider.CheckedChanged += delegate
            {
                chkDivider.Text = chkDivider.Checked ? "Закрытое (с полоской)" : "Открытое (плавный край)";
                LoadBg();
            };
            nPanel = TSlider(tl, 4, "Ширина размытия, %", 5, 100, 46, "%");
            nFade = TSlider(tl, 5, "Ширина градиента, px", 0, 200, 75, "");
            nTitleW = TSlider(tl, 6, "Длина заголовка, px", 180, 600, 264, "");
            nTextW = TSlider(tl, 7, "Длина текста, px", 180, 600, 264, "");
            nPanel.ValueChanged += delegate { LoadBg(); };
            nFade.ValueChanged += delegate { LoadBg(); };
            nTitleW.ValueChanged += delegate { preview.Invalidate(); };
            nTextW.ValueChanged += delegate { preview.Invalidate(); };
            y += 8 * 36 + 8;
            tBg = Row("Фон (картинка)", x, ref y, w - 90, 0);
            Button bBg = Btn("Обзор…", x + w - 82, y - 27, 82, 26, false);
            bBg.Click += delegate { PickBg(); };

            preview = new PictureBox();
            preview.SetBounds(x, y + 4, 320, 240);
            preview.BackColor = Color.Black;
            preview.Paint += PreviewPaint;
            Controls.Add(preview);

            // ---- Правая колонка: содержимое и сборка ----
            int rx = 470, rw = 450;
            y = 56;
            Section("СОДЕРЖИМОЕ (то, что попадёт в папку с игрой)", rx, y); y += 24;
            tPayload = Row("Папка с файлами (в ней: dlc и binaries)", rx, ref y, rw - 90, 0);
            Button bP = Btn("Обзор…", rx + rw - 82, y - 27, 82, 26, false);
            bP.Click += delegate { PickPayload(); };

            list = new ListView();
            list.SetBounds(rx, y + 2, rw, 250);
            list.View = View.Details;
            list.FullRowSelect = true;
            list.BorderStyle = BorderStyle.None;
            list.BackColor = Card;
            list.ForeColor = Fg;
            list.HideSelection = false;
            list.Columns.Add("Элемент", 230);
            list.Columns.Add("Тип", 110);
            list.Columns.Add("Размер", 90, HorizontalAlignment.Right);
            Controls.Add(list);
            lockables.Add(list);
            y += 258;

            Button bAdd = Btn("+ Добавить DLC…", rx, y, 140, 30, true);
            Button bBin = Btn("Файлы проверки…", rx + 146, y, 140, 30, false);
            Button bDel = Btn("Удалить", rx + 292, y, 70, 30, false);
            Button bOpen = Btn("Папка", rx + 368, y, 82, 30, false);
            bAdd.Click += delegate { AddDlc(); };
            bBin.Click += delegate { AddBinaries(); };
            bDel.Click += delegate { DeleteSel(); };
            bOpen.Click += delegate { OpenPayload(); };
            y += 46;

            Section("СБОРКА", rx, y); y += 24;
            tOut = Row("Готовый установщик (.exe)", rx, ref y, rw - 90, 0);
            Button bO = Btn("Обзор…", rx + rw - 82, y - 27, 82, 26, false);
            bO.Click += delegate { PickOut(); };

            bBuild = Btn("СОЗДАТЬ УСТАНОВЩИК", rx, y + 4, rw, 44, true);
            bBuild.Font = new Font("Segoe UI Semibold", 11f);
            bBuild.Click += delegate { Build(); };
            y += 56;

            bar = new ProgressBar();
            bar.SetBounds(rx, y, rw, 14);
            bar.Maximum = 1000;
            Controls.Add(bar);
            y += 20;
            lblStatus = new Label();
            lblStatus.SetBounds(rx, y, rw, 40);
            lblStatus.ForeColor = Dim;
            lblStatus.Text = "Готово к работе.";
            Controls.Add(lblStatus);

            LoadIni();
            foreach (TextBox t in new TextBox[] { tTitle, tSub, tInfo, tFooter })
                t.TextChanged += delegate { preview.Invalidate(); };
            tBg.TextChanged += delegate { LoadBg(); };
            tPayload.TextChanged += delegate { RefreshList(); };
            LoadBg();
            RefreshList();
            FormClosing += delegate { SaveIni(); };
        }

        // ---------- UI helpers ----------
        void Section(string t, int x, int y)
        {
            Label l = new Label();
            l.Text = t;
            l.Font = new Font("Segoe UI Semibold", 8.5f);
            l.ForeColor = Color.FromArgb(220, 110, 100);
            l.SetBounds(x, y, 440, 18);
            Controls.Add(l);
        }

        Label Cap(string t, int x, int y)
        {
            Label l = new Label();
            l.Text = t;
            l.ForeColor = Dim;
            l.SetBounds(x, y, 440, 18);
            Controls.Add(l);
            return l;
        }

        TextBox Row(string cap, int x, ref int y, int w, int h)
        {
            Cap(cap, x, y);
            y += 19;
            TextBox t = new TextBox();
            t.BorderStyle = BorderStyle.FixedSingle;
            t.BackColor = Field;
            t.ForeColor = Fg;
            if (h > 0) { t.Multiline = true; t.ScrollBars = ScrollBars.Vertical; t.SetBounds(x, y, w, h); y += h + 8; }
            else { t.SetBounds(x, y, w, 24); y += 32; }
            Controls.Add(t);
            lockables.Add(t);
            return t;
        }

        CheckBox TCheck(TableLayoutPanel tl, int row, string cap, string text, bool defVal)
        {
            Label l = new Label();
            l.Text = cap; l.ForeColor = Dim; l.Dock = DockStyle.Fill; l.TextAlign = ContentAlignment.MiddleLeft;
            tl.Controls.Add(l, 0, row);
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Checked = defVal;
            c.ForeColor = Fg;
            c.Cursor = Cursors.Hand;
            c.Dock = DockStyle.Fill;
            c.TextAlign = ContentAlignment.MiddleLeft;
            c.Margin = new Padding(4, 3, 3, 3);
            tl.Controls.Add(c, 1, row);
            tl.SetColumnSpan(c, 2);
            lockables.Add(c);
            return c;
        }

        Button TBtn(TableLayoutPanel tl, int row, string text)
        {
            Label l = new Label();
            l.Text = text; l.ForeColor = Dim; l.Dock = DockStyle.Fill; l.TextAlign = ContentAlignment.MiddleLeft;
            tl.Controls.Add(l, 0, row);
            Button b = new Button();
            b.Text = "Выбрать цвет…";
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(90, 96, 110);
            b.BackColor = Field; b.ForeColor = Color.White; b.Cursor = Cursors.Hand;
            b.Dock = DockStyle.Fill; b.Margin = new Padding(3, 3, 3, 3);
            tl.Controls.Add(b, 1, row);
            tl.SetColumnSpan(b, 2);
            lockables.Add(b);
            return b;
        }

        TrackBar TSlider(TableLayoutPanel tl, int row, string text, int min, int max, int val, string unit)
        {
            Label l = new Label();
            l.Text = text; l.ForeColor = Dim; l.Dock = DockStyle.Fill; l.TextAlign = ContentAlignment.MiddleLeft;
            tl.Controls.Add(l, 0, row);
            TrackBar t = new TrackBar();
            t.Minimum = min; t.Maximum = max; t.Value = val;
            t.TickStyle = TickStyle.None;
            t.SmallChange = 1; t.LargeChange = (max - min) / 10;
            t.Dock = DockStyle.Fill; t.Margin = new Padding(3, 4, 3, 0);
            tl.Controls.Add(t, 1, row);
            Label v = new Label();
            v.ForeColor = Fg; v.Dock = DockStyle.Fill; v.TextAlign = ContentAlignment.MiddleRight;
            v.Text = val + unit;
            tl.Controls.Add(v, 2, row);
            t.ValueChanged += delegate { v.Text = t.Value + unit; };
            lockables.Add(t);
            return t;
        }
        Button Btn(string text, int x, int y, int w, int h, bool primary)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = primary ? Color.FromArgb(122, 31, 37) : Field;
            b.ForeColor = Color.White;
            b.Cursor = Cursors.Hand;
            b.SetBounds(x, y, w, h);
            Controls.Add(b);
            lockables.Add(b);
            return b;
        }

        // ---------- Settings ----------
        static string Esc(string s) { return (s ?? "").Replace("\r\n", "\\n").Replace("\n", "\\n"); }

        void LoadIni()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            if (File.Exists(iniPath))
                foreach (string line in File.ReadAllLines(iniPath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1).Replace("\\n", "\r\n");
                }
            string defPayload = Path.Combine(baseDir, "Victoria_3_-_DLC_Unlocker_1.13");
            tTitle.Text = Get(d, "title", "Victoria 3: The Great Wave");
            tSub.Text = Get(d, "subtitle", "DLC Unlocker 1.13");
            tInfo.Text = Get(d, "info", "Набор DLC предназначен только для ознакомления. В дальнейшем вы обязуетесь удалить его и приобрести лицензионный контент.\r\n\r\nУстановщик сам найдёт Steam-версию игры и заменит папки dlc и файлы в binaries.");
            tFooter.Text = Get(d, "footer", "Автор: Rotgott. Автор установщика: AgiTall");
            tBg.Text = Get(d, "bg", Path.Combine(baseDir, "assets\\background.jpg"));
            tPayload.Text = Get(d, "payload", defPayload);
            tOut.Text = Get(d, "out", Path.Combine(baseDir, "Output\\Vic3_DLC_Installer.exe"));
            accent = Look.ParseColor(Get(d, "accent", "#8E242C"), accent);
            titleColor = Look.ParseColor(Get(d, "titlecolor", "#E6D2B5"), titleColor);
            bTitleColor.BackColor = titleColor;
            bTitleColor.ForeColor = Look.IsDark(titleColor) ? Color.White : Color.Black;
            bAccent.BackColor = accent;
            bAccent.ForeColor = Look.IsDark(accent) ? Color.White : Color.Black;
            blurColor = Look.ParseColor(Get(d, "blurcolor", "#1B1E24"), blurColor);
            bBlurColor.BackColor = blurColor;
            bBlurColor.ForeColor = Look.IsDark(blurColor) ? Color.White : Color.Black;
            bool div = Get(d, "divider", "1") != "0";
            chkDivider.Checked = div;
            chkDivider.Text = div ? "Закрытое (с полоской)" : "Открытое (плавный край)";
            nPanel.Value = (int)Clamp(Get(d, "panel", "46"), 5, 100, 46);
            nFade.Value = (int)Clamp(Get(d, "fadewidth", "75"), 0, 200, 75);
            nTitleW.Value = (int)Clamp(Get(d, "titlew", "264"), 180, 600, 264);
            nTextW.Value = (int)Clamp(Get(d, "textw", "264"), 180, 600, 264);
        }

        static decimal Clamp(string s, int min, int max, int def)
        {
            decimal v;
            if (!decimal.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return def;
            return Math.Max(min, Math.Min(max, v));
        }

        string CfgExtra()
        {
            return "blurcolor=" + ColorTranslator.ToHtml(blurColor) + "\npanel=" + nPanel.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                   "\ndivider=" + (chkDivider.Checked ? "1" : "0") +
                   "\nfadewidth=" + nFade.Value +
                   "\ntitlew=" + nTitleW.Value + "\ntextw=" + nTextW.Value + "\n";
        }

        static string Get(Dictionary<string, string> d, string k, string def)
        {
            string v;
            return d.TryGetValue(k, out v) && v.Length > 0 ? v : def;
        }

        void SaveIni()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("title=" + Esc(tTitle.Text));
                sb.AppendLine("subtitle=" + Esc(tSub.Text));
                sb.AppendLine("info=" + Esc(tInfo.Text));
                sb.AppendLine("footer=" + Esc(tFooter.Text));
                sb.AppendLine("bg=" + tBg.Text);
                sb.AppendLine("payload=" + tPayload.Text);
                sb.AppendLine("out=" + tOut.Text);
                sb.AppendLine("accent=" + ColorTranslator.ToHtml(accent));
                sb.AppendLine("titlecolor=" + ColorTranslator.ToHtml(titleColor));
                sb.Append(CfgExtra().Replace("\n", Environment.NewLine));
                File.WriteAllText(iniPath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        // ---------- Preview ----------
        void LoadBg()
        {
            try
            {
                Image img = null;
                if (File.Exists(tBg.Text))
                {
                    byte[] data = File.ReadAllBytes(tBg.Text);
                    using (Image tmp = Image.FromStream(new MemoryStream(data))) img = new Bitmap(tmp);
                }
                if (canvas != null) canvas.Dispose();
                canvas = Look.Compose(img, (float)nPanel.Value, blurColor, chkDivider != null && chkDivider.Checked, nFade != null ? nFade.Value : 75);
                if (img != null) img.Dispose();
            }
            catch { }
            preview.Invalidate();
        }

        void PreviewPaint(object s, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (canvas == null) return;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.DrawImage(canvas, 0, 0, preview.Width, preview.Height);
            float k = preview.Width / (float)Look.W;
            bool isDark = Look.IsDark(blurColor);
            Color bodyColor = isDark ? Color.FromArgb(218, 210, 198) : Color.FromArgb(52, 36, 28);
            Color subColor = isDark ? Color.FromArgb(204, 166, 108) : accent;
            Color capColor = isDark ? Color.FromArgb(168, 152, 130) : Color.FromArgb(105, 80, 66);
            Color footColor = isDark ? Color.FromArgb(182, 168, 148) : Color.FromArgb(88, 68, 56);

            float wTitle = Math.Max(200f, (float)nTitleW.Value);
            float wTxt = Math.Max(200f, (float)nTextW.Value);
            float wCtrl = Math.Min(wTxt, 272f);

            RectangleF t = new RectangleF(24 * k, 28 * k, wTitle * k, 74 * k);
            StringFormat top = new StringFormat(); top.LineAlignment = StringAlignment.Near;
            float size = 20f;
            using (Font f = FitFont(g, tTitle.Text, t, ref size, k))
            using (SolidBrush tb = new SolidBrush(titleColor))
                g.DrawString(tTitle.Text, f, tb, t, top);

            using (Font f = new Font("Georgia", 10.25f * k, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(subColor))
                g.DrawString(tSub.Text, f, b, new RectangleF(24 * k, 106 * k, wTxt * k, 22 * k));

            using (Font f = new Font("Segoe UI", 8.75f * k))
            using (SolidBrush b = new SolidBrush(bodyColor))
                g.DrawString(tInfo.Text, f, b, new RectangleF(24 * k, 134 * k, wTxt * k, 136 * k));

            using (Font f = new Font("Segoe UI Semibold", 8.25f * k))
            using (SolidBrush b = new SolidBrush(capColor))
                g.DrawString("Папка Victoria 3", f, b, new RectangleF(24 * k, 274 * k, wCtrl * k, 18 * k));

            // Поле ввода и кнопка "Обзор"
            RectangleF rBox = new RectangleF(24 * k, 294 * k, (wCtrl - 74f) * k, 28 * k);
            RectangleF rBrw = new RectangleF((24 + wCtrl - 68f) * k, 294 * k, 68 * k, 28 * k);
            Color boxFill = isDark ? Color.FromArgb(230, 32, 36, 44) : Color.FromArgb(235, 244, 238, 228);
            Color boxBorder = isDark ? Color.FromArgb(145, 148, 122, 80) : Color.FromArgb(150, 140, 118, 92);
            using (GraphicsPath p = Rounded(rBox, 5 * k))
            {
                using (SolidBrush b = new SolidBrush(boxFill)) g.FillPath(b, p);
                using (Pen pen = new Pen(boxBorder, 1f)) g.DrawPath(pen, p);
            }
            using (GraphicsPath p = Rounded(rBrw, 5 * k))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(185, 42, 46, 56))) g.FillPath(b, p);
                using (Pen pen = new Pen(boxBorder, 1f)) g.DrawPath(pen, p);
            }
            using (Font f = new Font("Segoe UI Semibold", 8.5f * k))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(228, 220, 206)))
            {
                StringFormat c = new StringFormat(); c.Alignment = StringAlignment.Center; c.LineAlignment = StringAlignment.Center;
                g.DrawString("Обзор", f, b, rBrw, c);
            }

            // Главная кнопка "Установить"
            RectangleF rInst = new RectangleF(24 * k, 386 * k, wCtrl * k, 40 * k);
            using (GraphicsPath p = Rounded(rInst, 5 * k))
            {
                using (SolidBrush b = new SolidBrush(accent)) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(180, 195, 158, 100), 1f)) g.DrawPath(pen, p);
            }
            using (Font f = new Font("Segoe UI Semibold", 10f * k))
            {
                StringFormat c = new StringFormat(); c.Alignment = StringAlignment.Center; c.LineAlignment = StringAlignment.Center;
                g.DrawString("Установить", f, Brushes.White, rInst, c);
            }

            // Подвал
            using (Font f = new Font("Segoe UI", 7.75f * k))
            using (SolidBrush b = new SolidBrush(footColor))
                g.DrawString(tFooter.Text, f, b, new RectangleF(24 * k, 438 * k, wTxt * k, 32 * k));

            // Скругленная рамка предпросмотра
            using (GraphicsPath borderPath = Rounded(new RectangleF(0, 0, preview.Width - 1, preview.Height - 1), 10 * k))
            using (Pen pb = new Pen(Color.FromArgb(160, 148, 120, 78), 1f))
            {
                g.DrawPath(pb, borderPath);
            }

            // Монохромные иконки окна (свернуть / закрыть)
            using (Pen pi = new Pen(Color.FromArgb(220, 240, 236, 228), 1.2f))
            {
                float mx = (Look.W - 51) * k, my = 22 * k;
                g.DrawLine(pi, mx - 3 * k, my, mx + 3 * k, my);
                float cx = (Look.W - 23) * k, cy = 22 * k;
                g.DrawLine(pi, cx - 3 * k, cy - 3 * k, cx + 3 * k, cy + 3 * k);
                g.DrawLine(pi, cx + 3 * k, cy - 3 * k, cx - 3 * k, cy + 3 * k);
            }
        }

        static Font FitFont(Graphics g, string text, RectangleF box, ref float size, float k)
        {
            while (size > 11f)
            {
                Font f = new Font("Georgia", size * k, FontStyle.Bold);
                SizeF m = g.MeasureString(text, f, (int)box.Width);
                if (m.Height <= box.Height) return f;
                f.Dispose();
                size -= 1f;
            }
            return new Font("Georgia", 11f * k, FontStyle.Bold);
        }

        static GraphicsPath Rounded(RectangleF r, float d)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ---------- Pickers ----------
        void PickColor()
        {
            using (ColorDialog d = new ColorDialog())
            {
                d.Color = accent; d.FullOpen = true;
                if (d.ShowDialog(this) == DialogResult.OK) { accent = d.Color; bAccent.BackColor = accent; bAccent.ForeColor = Look.IsDark(accent) ? Color.White : Color.Black; preview.Invalidate(); }
            }
        }

        void PickBg()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp|Все файлы|*.*";
                if (d.ShowDialog(this) == DialogResult.OK) tBg.Text = d.FileName;
            }
        }

        void PickPayload()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Папка с содержимым (подпапки dlc и binaries)";
                if (Directory.Exists(tPayload.Text)) d.SelectedPath = tPayload.Text;
                if (d.ShowDialog(this) == DialogResult.OK) tPayload.Text = d.SelectedPath;
            }
        }

        void PickOut()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "Установщик (*.exe)|*.exe";
                d.FileName = Path.GetFileName(tOut.Text);
                if (d.ShowDialog(this) == DialogResult.OK) tOut.Text = d.FileName;
            }
        }

        void OpenPayload()
        {
            if (Directory.Exists(tPayload.Text)) Process.Start("explorer.exe", "\"" + tPayload.Text + "\"");
        }

        // ---------- Payload list ----------
        static long DirSize(string d)
        {
            long s = 0;
            try { foreach (string f in Directory.GetFiles(d, "*", SearchOption.AllDirectories)) s += new FileInfo(f).Length; } catch { }
            return s;
        }

        static string Sz(long b)
        {
            if (b >= 1L << 30) return (b / (double)(1L << 30)).ToString("0.00") + " ГБ";
            if (b >= 1L << 20) return (b / (double)(1L << 20)).ToString("0.0") + " МБ";
            return (b / 1024.0).ToString("0") + " КБ";
        }

        void RefreshList()
        {
            list.Items.Clear();
            string p = tPayload.Text;
            if (!Directory.Exists(p)) return;
            string dlc = Path.Combine(p, "dlc"), bin = Path.Combine(p, "binaries");
            if (Directory.Exists(dlc))
                foreach (string d in Directory.GetDirectories(dlc))
                    AddItem(Path.GetFileName(d), "DLC", DirSize(d), d);
            if (Directory.Exists(bin))
            {
                foreach (string f in Directory.GetFiles(bin))
                    AddItem(Path.GetFileName(f), "Файл проверки", new FileInfo(f).Length, f);
                foreach (string d in Directory.GetDirectories(bin))
                    AddItem(Path.GetFileName(d) + "\\", "binaries", DirSize(d), d);
            }
            foreach (string d in Directory.GetDirectories(p))
            {
                string n = Path.GetFileName(d).ToLowerInvariant();
                if (n != "dlc" && n != "binaries") AddItem(Path.GetFileName(d) + "\\", "Прочее", DirSize(d), d);
            }
            foreach (string f in Directory.GetFiles(p))
                AddItem(Path.GetFileName(f), "Прочее", new FileInfo(f).Length, f);
        }

        void AddItem(string name, string type, long size, string path)
        {
            ListViewItem it = new ListViewItem(name);
            it.SubItems.Add(type);
            it.SubItems.Add(Sz(size));
            it.Tag = path;
            list.Items.Add(it);
        }

        static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src))
            {
                string t = Path.Combine(dst, Path.GetFileName(f));
                if (File.Exists(t)) File.SetAttributes(t, FileAttributes.Normal);
                File.Copy(f, t, true);
            }
            foreach (string d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        bool PayloadOk()
        {
            if (!Directory.Exists(tPayload.Text))
            {
                MessageBox.Show(this, "Укажите папку с содержимым.", "Содержимое", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        void AddDlc()
        {
            if (!PayloadOk()) return;
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Выберите папку DLC (dlcXXX_name) или папку-пакет с подпапкой dlc";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string src = d.SelectedPath, payload = tPayload.Text;
                RunBusy("Копирование DLC…", delegate
                {
                    string sub = Path.Combine(src, "dlc");
                    if (Directory.Exists(sub))
                    {
                        foreach (string x in Directory.GetDirectories(sub))
                            CopyDir(x, Path.Combine(payload, "dlc\\" + Path.GetFileName(x)));
                        string sb = Path.Combine(src, "binaries");
                        if (Directory.Exists(sb)) CopyDir(sb, Path.Combine(payload, "binaries"));
                    }
                    else CopyDir(src, Path.Combine(payload, "dlc\\" + Path.GetFileName(src.TrimEnd('\\'))));
                    return null;
                });
            }
        }

        void AddBinaries()
        {
            if (!PayloadOk()) return;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Multiselect = true;
                d.Title = "Файлы проверки (попадут в binaries с заменой)";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string[] files = d.FileNames; string payload = tPayload.Text;
                RunBusy("Копирование файлов…", delegate
                {
                    string bin = Path.Combine(payload, "binaries");
                    Directory.CreateDirectory(bin);
                    foreach (string f in files)
                    {
                        string t = Path.Combine(bin, Path.GetFileName(f));
                        if (File.Exists(t)) File.SetAttributes(t, FileAttributes.Normal);
                        File.Copy(f, t, true);
                    }
                    return null;
                });
            }
        }

        void DeleteSel()
        {
            if (list.SelectedItems.Count == 0) return;
            if (MessageBox.Show(this, "Удалить выбранное из папки с содержимым (" + list.SelectedItems.Count + " шт.)?", "Удаление", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            List<string> paths = new List<string>();
            foreach (ListViewItem it in list.SelectedItems) paths.Add((string)it.Tag);
            RunBusy("Удаление…", delegate
            {
                foreach (string p in paths)
                {
                    if (Directory.Exists(p)) Directory.Delete(p, true);
                    else if (File.Exists(p)) { File.SetAttributes(p, FileAttributes.Normal); File.Delete(p); }
                }
                return null;
            });
        }

        // ---------- Background worker ----------
        void SetStatus(string s, int pm)
        {
            BeginInvoke(new MethodInvoker(delegate { lblStatus.Text = s; if (pm >= 0) bar.Value = Math.Min(1000, pm); }));
        }

        void RunBusy(string status, Func<string> work)
        {
            if (busy) return;
            busy = true;
            foreach (Control c in lockables) c.Enabled = false;
            lblStatus.Text = status;
            bar.Style = ProgressBarStyle.Marquee;
            Thread t = new Thread(delegate()
            {
                string err = null;
                try { err = work(); } catch (Exception ex) { err = ex.Message; }
                BeginInvoke(new MethodInvoker(delegate
                {
                    busy = false;
                    foreach (Control c in lockables) c.Enabled = true;
                    bar.Style = ProgressBarStyle.Continuous;
                    bar.Value = 0;
                    lblStatus.Text = err == null ? "Готово." : "Ошибка: " + err;
                    RefreshList();
                }));
            });
            t.IsBackground = true;
            t.Start();
        }

        // ---------- Build ----------
        void Build()
        {
            if (!PayloadOk()) return;
            if (!File.Exists(tBg.Text)) { MessageBox.Show(this, "Не найден файл фона.", "Фон", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (string.IsNullOrWhiteSpace(tOut.Text)) { MessageBox.Show(this, "Укажите путь для установщика.", "Сборка", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            SaveIni();

            string payload = tPayload.Text, bgPath = tBg.Text, outPath = tOut.Text;
            string cfgText = "title=" + Esc(tTitle.Text) + "\nsubtitle=" + Esc(tSub.Text) + "\ninfo=" + Esc(tInfo.Text) +
                             "\nfooter=" + Esc(tFooter.Text) + "\naccent=" + ColorTranslator.ToHtml(accent) + "\ntitlecolor=" + ColorTranslator.ToHtml(titleColor) + "\n" + CfgExtra();

            RunBusy("Сборка установщика…", delegate
            {
                string cache = Path.Combine(baseDir, "cache");
                Directory.CreateDirectory(cache);
                string zipPath = Path.Combine(cache, "payload.zip"), sigPath = Path.Combine(cache, "payload.sig");

                // 1. архив с содержимым (кэшируется, пока файлы не менялись)
                SetStatus("Анализ файлов…", 0);
                List<string> files = new List<string>(Directory.GetFiles(payload, "*", SearchOption.AllDirectories));
                files.Sort(StringComparer.OrdinalIgnoreCase);
                if (files.Count == 0) return "В папке с содержимым нет файлов.";
                StringBuilder sig = new StringBuilder();
                long total = 0;
                foreach (string f in files)
                {
                    FileInfo fi = new FileInfo(f);
                    total += fi.Length;
                    sig.Append(f.Substring(payload.TrimEnd('\\').Length + 1)).Append('|').Append(fi.Length).Append('|').Append(fi.LastWriteTimeUtc.Ticks).Append('\n');
                }
                string sigText = sig.ToString();
                bool cached = File.Exists(zipPath) && File.Exists(sigPath) && File.ReadAllText(sigPath) == sigText;
                if (!cached)
                {
                    if (File.Exists(sigPath)) File.Delete(sigPath);
                    long done = 0; int last = -1;
                    byte[] buf = new byte[1024 * 1024];
                    using (FileStream fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
                    using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Create))
                    {
                        foreach (string f in files)
                        {
                            string rel = f.Substring(payload.TrimEnd('\\').Length + 1).Replace('\\', '/');
                            ZipArchiveEntry e = za.CreateEntry(rel, CompressionLevel.Fastest);
                            using (Stream es = e.Open())
                            using (FileStream ins = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                int n;
                                while ((n = ins.Read(buf, 0, buf.Length)) > 0)
                                {
                                    es.Write(buf, 0, n);
                                    done += n;
                                    int pm = (int)(done * 600 / Math.Max(1, total));
                                    if (pm != last) { last = pm; SetStatus("Упаковка файлов… " + (pm / 6) + "%", pm); }
                                }
                            }
                        }
                    }
                    File.WriteAllText(sigPath, sigText);
                }

                // 2. компиляция оболочки установщика напрямую из папки src
                SetStatus("Компиляция установщика…", 620);
                string srcDir = Path.Combine(baseDir, "src");
                string stub = Path.Combine(cache, "stub.exe");
                string resCfg = Path.Combine(cache, "config.txt");
                string resBg = Path.Combine(cache, "bg.bin");
                string icoPath = Path.Combine(baseDir, "assets\\icon.ico");
                string manifestPath = Path.Combine(srcDir, "app.manifest");

                try
                {
                    File.WriteAllText(resCfg, cfgText, new UTF8Encoding(false));
                    File.Copy(bgPath, resBg, true);

                    using (CSharpCodeProvider provider = new CSharpCodeProvider())
                    {
                        CompilerParameters cp = new CompilerParameters();
                        cp.GenerateExecutable = true;
                        cp.OutputAssembly = stub;
                        cp.IncludeDebugInformation = false;
                        cp.CompilerOptions = "/optimize+ /platform:anycpu /target:winexe /win32manifest:\"" + manifestPath + "\"" +
                                             (File.Exists(icoPath) ? " /win32icon:\"" + icoPath + "\"" : "");
                        cp.ReferencedAssemblies.Add("System.dll");
                        cp.ReferencedAssemblies.Add("System.Core.dll");
                        cp.ReferencedAssemblies.Add("System.Drawing.dll");
                        cp.ReferencedAssemblies.Add("System.Windows.Forms.dll");
                        cp.ReferencedAssemblies.Add("System.IO.Compression.dll");
                        cp.ReferencedAssemblies.Add("System.IO.Compression.FileSystem.dll");
                        cp.EmbeddedResources.Add(resBg + ",bg");
                        cp.EmbeddedResources.Add(resCfg + ",config.txt");

                        CompilerResults cr = provider.CompileAssemblyFromFile(cp,
                            Path.Combine(srcDir, "Installer.cs"),
                            Path.Combine(srcDir, "Shared.cs"),
                            Path.Combine(srcDir, "AssemblyInfo.cs"));
                        if (cr.Errors.HasErrors)
                        {
                            StringBuilder sbErr = new StringBuilder();
                            foreach (CompilerError ce in cr.Errors) sbErr.AppendLine(ce.ToString());
                            return "Ошибка компиляции:\n" + sbErr.ToString();
                        }
                    }

                    // 3. exe = оболочка + zip + трейлер
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
                    File.Copy(stub, outPath, true);
                    long zlen = new FileInfo(zipPath).Length;
                    using (FileStream o = new FileStream(outPath, FileMode.Append, FileAccess.Write))
                    using (FileStream z = new FileStream(zipPath, FileMode.Open, FileAccess.Read))
                    {
                        byte[] buf = new byte[4 * 1024 * 1024];
                        long c = 0; int n; int last = -1;
                        while ((n = z.Read(buf, 0, buf.Length)) > 0)
                        {
                            o.Write(buf, 0, n);
                            c += n;
                            int pm = 650 + (int)(c * 350 / Math.Max(1, zlen));
                            if (pm != last) { last = pm; SetStatus("Сборка exe… " + ((pm - 650) * 100 / 350) + "%", pm); }
                        }
                        o.Write(BitConverter.GetBytes(zlen), 0, 8);
                        o.Write(BitConverter.GetBytes(0x56494333494E5354L), 0, 8);
                    }
                }
                finally
                {
                    try { if (File.Exists(stub)) File.Delete(stub); } catch { }
                    try { if (File.Exists(resCfg)) File.Delete(resCfg); } catch { }
                    try { if (File.Exists(resBg)) File.Delete(resBg); } catch { }
                }

                SetStatus("Готово: " + outPath + " (" + Sz(new FileInfo(outPath).Length) + ")", 1000);
                return null;
            });
        }
    }
}



