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

        static readonly Color DefaultAccent = Color.FromArgb(142, 36, 44);
        static readonly Color DefaultTitleColor = Color.FromArgb(230, 210, 181);
        static readonly Color DefaultBlurColor = Color.FromArgb(27, 30, 36);

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string iniPath;
        TextBox tTitle, tSub, tInfo, tFooter, tBg, tPayload, tOut;
        Button bAccent, bTitleColor, bBlurColor, bDefaultColors, bPreviewMode, bBuild;
        Color accent = DefaultAccent;
        Color titleColor = DefaultTitleColor;
        Color blurColor = DefaultBlurColor;
        CheckBox chkDivider;
        TrackBar nPanel, nFade, nTitleW, nTextW;
        PictureBox preview;
        Bitmap canvas, sharpCanvas;
        bool previewInstallMode;
        ListView list;
        ProgressBar bar;
        Label lblStatus;
        List<Control> lockables = new List<Control>();
        bool busy;

        public AdminForm()
        {
            iniPath = Path.Combine(baseDir, "admin.ini");
            Text = "Vic3 Installer — Админ-панель";
            ClientSize = new Size(940, 890);
            MinimumSize = new Size(956, 920);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font("Segoe UI", 9f);

            Label head = new Label();
            head.Text = "Админ-панель установщика Victoria 3";
            head.Font = new Font("Segoe UI Semibold", 15f);
            head.ForeColor = Color.White;
            head.SetBounds(20, 10, 700, 30);
            Controls.Add(head);

            // ---- Левая колонка: оформление ----
            int x = 20, w = 420, y = 48;
            Section("ОФОРМЛЕНИЕ", x, y); y += 20;
            tTitle = Row("Заголовок (текущее DLC)", x, ref y, w, 0);
            tSub = Row("Подзаголовок", x, ref y, w, 0);
            tInfo = Row("Информация", x, ref y, w, 62);
            tFooter = Row("Подпись внизу", x, ref y, w, 34);

            Section("ЦВЕТА И РАЗМЕРЫ", x, y);
            bDefaultColors = Btn("По умолчанию", x + w - 118, y - 3, 118, 22, false);
            bDefaultColors.Font = new Font("Segoe UI", 8.25f);
            bDefaultColors.Click += delegate { ResetDefaultColors(); };
            y += 22;

            // Компактный ряд кнопок выбора цветов
            int cw = (w - 12) / 3;
            bAccent = ColorBtn("Кнопки (акцент)", x, y, cw, 28);
            bTitleColor = ColorBtn("Цвет заголовка", x + cw + 6, y, cw, 28);
            bBlurColor = ColorBtn("Цвет размытия", x + (cw + 6) * 2, y, w - (cw + 6) * 2, 28);
            bAccent.Click += delegate { PickColor(); };
            bTitleColor.Click += delegate
            {
                using (ColorDialog d = new ColorDialog())
                {
                    d.Color = titleColor; d.FullOpen = true;
                    if (d.ShowDialog(this) == DialogResult.OK) { titleColor = d.Color; UpdateColorButtons(); preview.Invalidate(); }
                }
            };
            bBlurColor.Click += delegate
            {
                using (ColorDialog d = new ColorDialog())
                {
                    d.Color = blurColor; d.FullOpen = true;
                    if (d.ShowDialog(this) == DialogResult.OK) { blurColor = d.Color; UpdateColorButtons(); LoadBg(); }
                }
            };
            y += 34;

            TableLayoutPanel tl = new TableLayoutPanel();
            tl.SetBounds(x, y, w, 5 * 32);
            tl.ColumnCount = 3; tl.RowCount = 5;
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            for (int i = 0; i < 5; i++) tl.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            Controls.Add(tl);

            chkDivider = TCheck(tl, 0, "Тип размытия", "Закрытое (с полоской)", true);
            chkDivider.CheckedChanged += delegate
            {
                chkDivider.Text = chkDivider.Checked ? "Закрытое (с полоской)" : "Открытое (плавный край)";
                LoadBg();
            };
            nPanel = TSlider(tl, 1, "Ширина размытия, %", 5, 100, 46, "%");
            nFade = TSlider(tl, 2, "Ширина градиента, px", 0, 250, 0, "");
            nTitleW = TSlider(tl, 3, "Длина заголовка, px", 120, 590, 264, "");
            nTextW = TSlider(tl, 4, "Длина текста, px", 120, 590, 264, "");
            nPanel.ValueChanged += delegate { LoadBg(); };
            nFade.ValueChanged += delegate { LoadBg(); };
            nTitleW.ValueChanged += delegate { preview.Invalidate(); };
            nTextW.ValueChanged += delegate { preview.Invalidate(); };
            y += 5 * 32 + 6;

            tBg = Row("Фон (картинка)", x, ref y, w - 90, 0);
            Button bBg = Btn("Обзор…", x + w - 82, y - 27, 82, 26, false);
            bBg.Click += delegate { PickBg(); };

            Section("ПРЕДПРОСМОТР УСТАНОВЩИКА (1:1)", x, y);
            bPreviewMode = Btn("Вид: Приветствие", x + w - 138, y - 3, 138, 22, false);
            bPreviewMode.Font = new Font("Segoe UI", 8.25f);
            bPreviewMode.Click += delegate
            {
                previewInstallMode = !previewInstallMode;
                bPreviewMode.Text = previewInstallMode ? "Вид: Установка" : "Вид: Приветствие";
                preview.Invalidate();
            };
            y += 22;

            preview = new PictureBox();
            preview.SetBounds(x, y, 360, 270);
            preview.BackColor = Bg;
            preview.Paint += PreviewPaint;
            Controls.Add(preview);

            // ---- Правая колонка: содержимое и сборка ----
            int rx = 470, rw = 450;
            y = 48;
            Section("СОДЕРЖИМОЕ (то, что попадёт в папку с игрой)", rx, y); y += 20;
            tPayload = Row("Папка с файлами (в ней: dlc и binaries)", rx, ref y, rw - 90, 0);
            Button bP = Btn("Обзор…", rx + rw - 82, y - 27, 82, 26, false);
            bP.Click += delegate { PickPayload(); };

            list = new ListView();
            list.SetBounds(rx, y + 2, rw, 280);
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
            y += 288;

            Button bAdd = Btn("+ Добавить DLC…", rx, y, 140, 30, true);
            Button bBin = Btn("Файлы проверки…", rx + 146, y, 140, 30, false);
            Button bDel = Btn("Удалить", rx + 292, y, 70, 30, false);
            Button bOpen = Btn("Папка", rx + 368, y, 82, 30, false);
            bAdd.Click += delegate { AddDlc(); };
            bBin.Click += delegate { AddBinaries(); };
            bDel.Click += delegate { DeleteSel(); };
            bOpen.Click += delegate { OpenPayload(); };
            y += 46;

            Section("СБОРКА", rx, y); y += 20;
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
            lblStatus.SetBounds(rx, y, rw, 48);
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
            l.SetBounds(x, y, 320, 18);
            Controls.Add(l);
        }

        Label Cap(string t, int x, int y)
        {
            Label l = new Label();
            l.Text = t;
            l.ForeColor = Dim;
            l.SetBounds(x, y, 440, 17);
            Controls.Add(l);
            return l;
        }

        TextBox Row(string cap, int x, ref int y, int w, int h)
        {
            Cap(cap, x, y);
            y += 18;
            TextBox t = new TextBox();
            t.BorderStyle = BorderStyle.FixedSingle;
            t.BackColor = Field;
            t.ForeColor = Fg;
            if (h > 0) { t.Multiline = true; t.ScrollBars = ScrollBars.Vertical; t.SetBounds(x, y, w, h); y += h + 6; }
            else { t.SetBounds(x, y, w, 23); y += 29; }
            Controls.Add(t);
            lockables.Add(t);
            return t;
        }

        Button ColorBtn(string text, int x, int y, int w, int h)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(90, 96, 110);
            b.BackColor = Field;
            b.ForeColor = Color.White;
            b.Cursor = Cursors.Hand;
            b.Font = new Font("Segoe UI", 8.25f);
            b.SetBounds(x, y, w, h);
            Controls.Add(b);
            lockables.Add(b);
            return b;
        }

        void UpdateColorButtons()
        {
            bAccent.BackColor = accent;
            bAccent.ForeColor = Look.IsDark(accent) ? Color.White : Color.Black;
            bTitleColor.BackColor = titleColor;
            bTitleColor.ForeColor = Look.IsDark(titleColor) ? Color.White : Color.Black;
            bBlurColor.BackColor = blurColor;
            bBlurColor.ForeColor = Look.IsDark(blurColor) ? Color.White : Color.Black;
        }

        void ResetDefaultColors()
        {
            accent = DefaultAccent;
            titleColor = DefaultTitleColor;
            blurColor = DefaultBlurColor;
            UpdateColorButtons();
            LoadBg();
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
            c.Margin = new Padding(4, 2, 3, 2);
            tl.Controls.Add(c, 1, row);
            tl.SetColumnSpan(c, 2);
            lockables.Add(c);
            return c;
        }

        TrackBar TSlider(TableLayoutPanel tl, int row, string text, int min, int max, int val, string unit)
        {
            Label l = new Label();
            l.Text = text; l.ForeColor = Dim; l.Dock = DockStyle.Fill; l.TextAlign = ContentAlignment.MiddleLeft;
            tl.Controls.Add(l, 0, row);
            TrackBar t = new TrackBar();
            t.Minimum = min; t.Maximum = max; t.Value = val;
            t.TickStyle = TickStyle.None;
            t.SmallChange = 1; t.LargeChange = Math.Max(1, (max - min) / 10);
            t.Dock = DockStyle.Fill; t.Margin = new Padding(3, 2, 3, 0);
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
            accent = Look.ParseColor(Get(d, "accent", "#8E242C"), DefaultAccent);
            titleColor = Look.ParseColor(Get(d, "titlecolor", "#E6D2B5"), DefaultTitleColor);
            blurColor = Look.ParseColor(Get(d, "blurcolor", "#1B1E24"), DefaultBlurColor);
            UpdateColorButtons();
            bool div = Get(d, "divider", "1") != "0";
            chkDivider.Checked = div;
            chkDivider.Text = div ? "Закрытое (с полоской)" : "Открытое (плавный край)";
            nPanel.Value = (int)Clamp(Get(d, "panel", "46"), 5, 100, 46);
            nFade.Value = (int)Clamp(Get(d, "fadewidth", "0"), 0, 250, 0);
            nTitleW.Value = (int)Clamp(Get(d, "titlew", "264"), 120, 590, 264);
            nTextW.Value = (int)Clamp(Get(d, "textw", "264"), 120, 590, 264);
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
                if (sharpCanvas != null) sharpCanvas.Dispose();
                canvas = Look.Build(img, out sharpCanvas, (float)nPanel.Value, blurColor, chkDivider != null && chkDivider.Checked, nFade != null ? nFade.Value : 0);
                if (img != null) img.Dispose();
            }
            catch { }
            if (preview != null) preview.Invalidate();
        }

        void PreviewPaint(object s, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Bitmap activeBg = previewInstallMode ? sharpCanvas : canvas;
            if (activeBg == null) return;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            float k = preview.Width / (float)Look.W;
            using (GraphicsPath clipPath = Rounded(new RectangleF(0, 0, preview.Width - 1, preview.Height - 1), 16 * k))
            {
                Region oldClip = g.Clip;
                g.SetClip(clipPath);
                g.DrawImage(activeBg, 0, 0, preview.Width, preview.Height);

                if (previewInstallMode)
                {
                    // Предпросмотр второго экрана (экрана распаковки/установки) 1:1 с Installer.cs
                    RectangleF bot = new RectangleF(0, 360 * k, Look.W * k, 120 * k);
                    using (LinearGradientBrush gb = new LinearGradientBrush(bot, Color.FromArgb(0, 10, 12, 16), Color.FromArgb(230, 10, 12, 16), LinearGradientMode.Vertical))
                        g.FillRectangle(gb, bot);

                    using (Font f = new Font("Segoe UI Semibold", 9.5f * k))
                    {
                        DrawShadowString(g, "Распаковка файлов… 64%  (dlc014_sphere_of_influence)", f,
                            Color.FromArgb(240, 232, 218), Color.FromArgb(160, 0, 0, 0),
                            new RectangleF(32 * k, 412 * k, (Look.W - 64) * k, 22 * k));
                    }

                    RectangleF rBar = new RectangleF(32 * k, 438 * k, (Look.W - 64) * k, 8 * k);
                    using (SolidBrush bTrack = new SolidBrush(Color.FromArgb(160, 20, 22, 28)))
                        g.FillRectangle(bTrack, rBar);
                    float fillW = rBar.Width * 0.64f;
                    Color accentLight = Color.FromArgb(Math.Min(255, accent.R + 28), Math.Min(255, accent.G + 28), Math.Min(255, accent.B + 28));
                    using (LinearGradientBrush brFill = new LinearGradientBrush(new RectangleF(rBar.X, rBar.Y, fillW, rBar.Height), accent, accentLight, LinearGradientMode.Horizontal))
                        g.FillRectangle(brFill, rBar.X, rBar.Y, fillW, rBar.Height);
                    using (Pen pBar = new Pen(Color.FromArgb(140, 190, 156, 98), 1f))
                        g.DrawRectangle(pBar, rBar.X, rBar.Y, rBar.Width - 1, rBar.Height - 1);
                }
                else
                {
                    // Предпросмотр главного экрана 1:1 с Installer.cs
                    bool isDark = Look.IsDark(blurColor);
                    Color bodyColor = isDark ? Color.FromArgb(218, 210, 198) : Color.FromArgb(52, 36, 28);
                    Color subColor = isDark ? Color.FromArgb(204, 166, 108) : accent;
                    Color capColor = isDark ? Color.FromArgb(168, 152, 130) : Color.FromArgb(105, 80, 66);
                    Color footColor = isDark ? Color.FromArgb(182, 168, 148) : Color.FromArgb(88, 68, 56);
                    Color okColor = isDark ? Color.FromArgb(168, 214, 152) : Color.FromArgb(46, 104, 46);

                    float wTitle = Math.Max(120f, Math.Min(Look.W - 48f, (float)nTitleW.Value));
                    float wTxt = Math.Max(120f, Math.Min(Look.W - 48f, (float)nTextW.Value));
                    float wCtrl = Math.Min(wTxt, 272f);

                    RectangleF rTitle = new RectangleF(24 * k, 28 * k, wTitle * k, 74 * k);
                    float titleSize = 20f;
                    using (Font f = FitFont(g, tTitle.Text, rTitle, ref titleSize, k))
                        DrawShadowString(g, tTitle.Text, f, titleColor, Color.FromArgb(140, 0, 0, 0), rTitle);

                    using (Font f = new Font("Georgia", 10.25f * k, FontStyle.Bold))
                        DrawShadowString(g, tSub.Text, f, subColor, Color.FromArgb(110, 0, 0, 0), new RectangleF(24 * k, 106 * k, wTxt * k, 22 * k));

                    RectangleF rInfo = new RectangleF(24 * k, 134 * k, wTxt * k, 136 * k);
                    using (Font f = FitInfoFont(g, tInfo.Text, rInfo, k))
                        DrawShadowString(g, tInfo.Text, f, bodyColor, Color.FromArgb(120, 0, 0, 0), rInfo);

                    using (Font f = new Font("Segoe UI Semibold", 8.25f * k))
                    using (SolidBrush b = new SolidBrush(capColor))
                        g.DrawString("Папка Victoria 3", f, b, new RectangleF(24 * k, 274 * k, wCtrl * k, 18 * k));

                    // Поле ввода и кнопка "Обзор"
                    float boxW = Math.Max(60f, wCtrl - 74f);
                    RectangleF rBox = new RectangleF(24 * k, 294 * k, boxW * k, 28 * k);
                    RectangleF rBrw = new RectangleF((24 + wCtrl - 68f) * k, 294 * k, 68 * k, 28 * k);
                    Color boxFill = isDark ? Color.FromArgb(230, 32, 36, 44) : Color.FromArgb(235, 244, 238, 228);
                    Color boxBorder = isDark ? Color.FromArgb(145, 148, 122, 80) : Color.FromArgb(150, 140, 118, 92);
                    Color boxText = isDark ? Color.FromArgb(235, 230, 220) : Color.FromArgb(44, 32, 24);
                    using (GraphicsPath p = Rounded(rBox, 5 * k))
                    {
                        using (SolidBrush b = new SolidBrush(boxFill)) g.FillPath(b, p);
                        using (Pen pen = new Pen(boxBorder, 1f)) g.DrawPath(pen, p);
                    }
                    using (Font f = new Font("Segoe UI", 8.25f * k))
                    using (SolidBrush b = new SolidBrush(boxText))
                    {
                        StringFormat sfPath = new StringFormat(StringFormatFlags.NoWrap);
                        sfPath.LineAlignment = StringAlignment.Center;
                        sfPath.Trimming = StringTrimming.EllipsisPath;
                        RectangleF rBoxText = new RectangleF(rBox.X + 6 * k, rBox.Y, Math.Max(10f, rBox.Width - 12 * k), rBox.Height);
                        g.DrawString(@"C:\Program Files (x86)\Steam\steamapps\common\Victoria 3", f, b, rBoxText, sfPath);
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

                    // Статус проверки папки игры
                    using (Font f = new Font("Segoe UI", 8.25f * k))
                        DrawShadowString(g, "Найдена Steam-версия игры. Нажмите «Установить».", f,
                            okColor, Color.FromArgb(110, 0, 0, 0), new RectangleF(24 * k, 330 * k, wCtrl * k, 46 * k));

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
                        DrawShadowString(g, tFooter.Text, f, footColor, Color.FromArgb(100, 0, 0, 0), new RectangleF(24 * k, 438 * k, wTxt * k, 32 * k));
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

                g.Clip = oldClip;
                using (Pen pb = new Pen(Color.FromArgb(160, 148, 120, 78), 1f))
                    g.DrawPath(pb, clipPath);
            }
        }

        static void DrawShadowString(Graphics g, string text, Font f, Color fore, Color shadow, RectangleF box)
        {
            StringFormat sf = new StringFormat();
            sf.LineAlignment = StringAlignment.Near;
            RectangleF sBox = new RectangleF(box.X + 1f, box.Y + 1f, box.Width, box.Height);
            using (SolidBrush sb = new SolidBrush(shadow)) g.DrawString(text, f, sb, sBox, sf);
            using (SolidBrush fb = new SolidBrush(fore)) g.DrawString(text, f, fb, box, sf);
        }

        static Font FitFont(Graphics g, string text, RectangleF box, ref float size, float k)
        {
            while (size > 11f)
            {
                Font f = new Font("Georgia", size * k, FontStyle.Bold);
                SizeF m = g.MeasureString(text, f, Math.Max(1, (int)box.Width));
                if (m.Height <= box.Height) return f;
                f.Dispose();
                size -= 1f;
            }
            return new Font("Georgia", 11f * k, FontStyle.Bold);
        }

        static Font FitInfoFont(Graphics g, string text, RectangleF box, float k)
        {
            float size = 8.75f;
            while (size > 6.75f)
            {
                Font f = new Font("Segoe UI", size * k);
                SizeF m = g.MeasureString(text, f, Math.Max(1, (int)box.Width));
                if (m.Height <= box.Height) return f;
                f.Dispose();
                size -= 0.5f;
            }
            return new Font("Segoe UI", 6.75f * k);
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
                if (d.ShowDialog(this) == DialogResult.OK) { accent = d.Color; UpdateColorButtons(); preview.Invalidate(); }
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
                string resBg = Path.Combine(cache, "bg");
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
                        cp.EmbeddedResources.Add(resBg);
                        cp.EmbeddedResources.Add(resCfg);

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
