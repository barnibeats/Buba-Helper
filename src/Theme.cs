// Тема в духе Windows 7 (Aero): глянцевые кнопки, вкладки, группы, зелёный индикатор и значки.
// Всё рисуется кодом (GDI+), без внешних файлов, поэтому выглядит одинаково и в WinPE, где системной темы нет.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinInstaller
{
    static class Theme
    {
        public static readonly Color Ink = Color.FromArgb(30, 57, 91);       // тёмно-синий текст заголовков
        public static readonly Color Face = Color.FromArgb(240, 240, 240);   // цвет окна Windows 7
        public static readonly Color TabBorder = Color.FromArgb(137, 140, 149);

        public static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2;
            if (rad <= 0 || r.Width < d || r.Height < d) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>Фигура с закруглёнными только верхними углами (для вкладок).</summary>
        public static GraphicsPath TopRound(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddLine(r.Right, r.Y + rad, r.Right, r.Bottom);
            p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
            p.CloseFigure();
            return p;
        }

        public static LinearGradientBrush Grad(RectangleF r, Color a, Color b, float angle)
        {
            if (r.Width < 1) r.Width = 1;
            if (r.Height < 1) r.Height = 1;
            return new LinearGradientBrush(r, a, b, angle);
        }

        /// <summary>Глянцевая заливка: верхняя половина светлее нижней, внутри тонкая светлая рамка.</summary>
        public static void Gloss(Graphics g, Rectangle r, Color top1, Color top2, Color bot1, Color bot2, Color border, int rad)
        {
            if (r.Width < 4 || r.Height < 4) return;
            RectangleF b = new RectangleF(r.X, r.Y, r.Width - 1, r.Height - 1);
            using (GraphicsPath path = Round(b, rad))
            using (LinearGradientBrush br = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, r.Height), top1, bot2, 90f))
            {
                ColorBlend cb = new ColorBlend(4);
                cb.Colors = new Color[] { top1, top2, bot1, bot2 };
                cb.Positions = new float[] { 0f, 0.5f, 0.5f, 1f };
                br.InterpolationColors = cb;
                g.FillPath(br, path);
                using (Pen pen = new Pen(border)) g.DrawPath(pen, path);
                RectangleF inner = new RectangleF(b.X + 1, b.Y + 1, b.Width - 2, b.Height - 2);
                using (GraphicsPath ip = Round(inner, Math.Max(0, rad - 1)))
                using (Pen hp = new Pen(Color.FromArgb(120, 255, 255, 255))) g.DrawPath(hp, ip);
            }
        }

        /// <summary>Рисует кнопку в стиле Aero: обычная, под курсором, нажатая/выбранная, недоступная, главная (синяя).</summary>
        public static void PaintButton(Graphics g, Rectangle r, string text, Font font, bool enabled, bool hot, bool down,
            bool selected, bool primary, bool focused, Bitmap glyph, bool glyphRight)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color t1, t2, b1, b2, bd, fg = Color.Black;
            if (!enabled)
            {
                t1 = t2 = b1 = b2 = C(244, 244, 244); bd = C(173, 178, 181); fg = C(131, 131, 131);
            }
            else if (primary)
            {
                fg = Color.White; bd = C(33, 86, 148);
                if (down) { t1 = C(70, 135, 205); t2 = C(50, 115, 190); b1 = C(35, 95, 170); b2 = C(45, 110, 185); }
                else if (hot) { t1 = C(150, 203, 246); t2 = C(100, 168, 236); b1 = C(60, 145, 225); b2 = C(84, 170, 240); }
                else { t1 = C(120, 180, 236); t2 = C(76, 147, 219); b1 = C(46, 127, 208); b2 = C(63, 149, 224); }
            }
            else if (down || selected)
            {
                t1 = C(229, 244, 252); t2 = C(196, 229, 246); b1 = C(152, 209, 239); b2 = C(104, 179, 219); bd = C(44, 98, 139);
            }
            else if (hot)
            {
                t1 = C(234, 246, 253); t2 = C(217, 240, 252); b1 = C(190, 230, 253); b2 = C(167, 217, 245); bd = C(60, 127, 177);
            }
            else
            {
                t1 = C(242, 242, 242); t2 = C(235, 235, 235); b1 = C(221, 221, 221); b2 = C(207, 207, 207);
                bd = focused ? C(51, 153, 255) : C(112, 112, 112);
            }
            Gloss(g, r, t1, t2, b1, b2, bd, 3);

            // значок и текст по центру
            int gw = glyph == null ? 0 : 16 + 6;
            TextFormatFlags fl = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            Size ts = TextRenderer.MeasureText(g, text ?? "", font, new Size(int.MaxValue, int.MaxValue), fl);
            int avail = Math.Max(10, r.Width - 12 - gw);
            int tw = Math.Min(ts.Width, avail);
            int x = r.X + (r.Width - (tw + gw)) / 2;
            if (down && enabled) x += 1;
            int gx = glyphRight ? x + tw + 6 : x;
            int tx = glyphRight ? x : x + gw;
            if (glyph != null)
            {
                int gy = r.Y + (r.Height - 16) / 2 + (down && enabled ? 1 : 0);
                if (enabled) g.DrawImage(glyph, gx, gy, 16, 16);
                else ControlPaint.DrawImageDisabled(g, glyph, gx, gy, t1);
            }
            Rectangle tr = new Rectangle(tx, r.Y + (down && enabled ? 1 : 0), tw + 1, r.Height);
            TextRenderer.DrawText(g, text ?? "", font, tr, fg, fl);
        }
    }

    /// <summary>Кнопка в стиле Windows 7.</summary>
    class AeroButton : Button
    {
        bool hot, down;
        public bool Primary;          // главная кнопка (синяя)
        public Bitmap Glyph;          // значок 16x16
        public bool GlyphRight;

        public AeroButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Face);
            Theme.PaintButton(e.Graphics, ClientRectangle, Text, Font, Enabled, hot, down, false, Primary, Focused, Glyph, GlyphRight);
        }
    }

    /// <summary>Переключатель режима в виде кнопки (выбранный выглядит нажатым).</summary>
    class AeroToggle : RadioButton
    {
        bool hot;
        public Bitmap Glyph;

        public AeroToggle()
        {
            Appearance = Appearance.Button;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : Theme.Face);
            using (Font f = new Font(Font, Checked ? FontStyle.Bold : FontStyle.Regular))
                Theme.PaintButton(e.Graphics, ClientRectangle, Text, f, Enabled, hot, false, Checked, false, false, Glyph, false);
        }
    }

    /// <summary>Группа с тонкой скруглённой рамкой и тёмно-синим заголовком.</summary>
    class AeroGroupBox : GroupBox
    {
        public AeroGroupBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            TextFormatFlags fl = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            Size ts = TextRenderer.MeasureText(g, Text, Font, new Size(int.MaxValue, int.MaxValue), fl);
            int y = ts.Height / 2;
            RectangleF b = new RectangleF(0, y, Width - 1, Height - 1 - y);
            using (GraphicsPath p = Theme.Round(b, 4))
            using (Pen pen = new Pen(Theme.C(213, 223, 229))) g.DrawPath(pen, p);
            if (Text.Length > 0)
            {
                using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, 8, 0, ts.Width + 6, ts.Height);
                TextRenderer.DrawText(g, Text, Font, new Point(11, 0), Theme.Ink, fl);
            }
        }
    }

    /// <summary>Вкладки в духе Windows 7: светлые градиенты, значок слева от названия, подсветка под курсором.</summary>
    class AeroTabControl : TabControl
    {
        int hot = -1;

        public AeroTabControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(140, 28);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = -1;
            for (int i = 0; i < TabCount; i++) if (GetTabRect(i).Contains(e.Location)) h = i;
            if (h != hot) { hot = h; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnSelectedIndexChanged(EventArgs e) { Invalidate(); base.OnSelectedIndexChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Face);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (TabCount == 0) return;
            int lineY = GetTabRect(0).Bottom;
            using (Pen bp = new Pen(Theme.TabBorder))
                g.DrawRectangle(bp, 0, lineY, Width - 1, Height - 1 - lineY);
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < TabCount; i++)
                {
                    bool sel = i == SelectedIndex;
                    if (sel != (pass == 1)) continue;   // выбранная вкладка рисуется последней, поверх рамки
                    DrawTab(g, i, sel, lineY);
                }
        }
        void DrawTab(Graphics g, int i, bool sel, int lineY)
        {
            Rectangle r = GetTabRect(i);
            r.Width -= 2;
            if (!sel) { r.Y += 3; r.Height -= 3; } else { r.Y += 1; r.Height = lineY - r.Y + 1; }
            Color c1, c2, bd;
            if (sel) { c1 = Color.White; c2 = Theme.C(247, 250, 254); bd = Theme.TabBorder; }
            else if (i == hot) { c1 = Theme.C(234, 246, 253); c2 = Theme.C(190, 230, 253); bd = Theme.C(60, 127, 177); }
            else { c1 = Theme.C(246, 246, 246); c2 = Theme.C(222, 222, 222); bd = Theme.TabBorder; }
            using (GraphicsPath p = Theme.TopRound(r, 3))
            using (LinearGradientBrush br = Theme.Grad(r, c1, c2, 90f))
            using (Pen pen = new Pen(bd))
            {
                g.FillPath(br, p);
                g.DrawPath(pen, p);
            }
            if (sel)
            {
                using (Pen hp = new Pen(Color.FromArgb(170, 255, 255, 255))) g.DrawLine(hp, r.X + 3, r.Y + 1, r.Right - 3, r.Y + 1);
                using (Pen op = new Pen(Color.White)) g.DrawLine(op, r.X + 1, lineY, r.Right - 1, lineY);   // «открываем» рамку под вкладкой
            }
            int x = r.X + 10;
            TabPage pg = TabPages[i];
            if (ImageList != null && pg.ImageIndex >= 0 && pg.ImageIndex < ImageList.Images.Count)
            {
                g.DrawImage(ImageList.Images[pg.ImageIndex], x, r.Y + (r.Height - 16) / 2 + 1, 16, 16);
                x += 22;
            }
            TextFormatFlags fl = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            using (Font f = new Font(Font, sel ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(g, pg.Text, f, new Rectangle(x, r.Y + 1, Math.Max(10, r.Right - x - 4), r.Height), sel ? Theme.Ink : Color.Black, fl);
        }
    }

    /// <summary>Зелёный глянцевый индикатор выполнения, как в Windows 7.</summary>
    class AeroProgress : ProgressBar
    {
        public AeroProgress()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Face);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF b = new RectangleF(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Round(b, 3))
            using (LinearGradientBrush br = Theme.Grad(b, Theme.C(228, 228, 228), Theme.C(250, 250, 250), 90f))
            using (Pen pen = new Pen(Theme.C(140, 140, 140)))
            {
                g.FillPath(br, p);
                g.DrawPath(pen, p);
            }
            double frac = Maximum > Minimum ? (double)(Value - Minimum) / (Maximum - Minimum) : 0;
            int w = (int)((Width - 4) * Math.Max(0, Math.Min(1, frac)));
            if (w > 3)
                Theme.Gloss(g, new Rectangle(2, 2, w, Height - 4), Theme.C(177, 240, 163), Theme.C(94, 222, 80), Theme.C(28, 184, 44), Theme.C(76, 216, 86), Theme.C(52, 148, 52), 2);
        }
    }

    /// <summary>Левая панель навигации в стиле проводника Windows 7: значок и название, выбранный пункт - голубое стекло.</summary>
    class AeroNav : ListBox
    {
        int hot = -1;
        public ImageList Icons;

        public AeroNav()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 30;
            BorderStyle = BorderStyle.None;
            BackColor = Color.White;
            IntegralHeight = false;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = IndexFromPoint(e.Location);
            if (h != hot) { hot = h; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool sel = e.Index == SelectedIndex;
            using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, e.Bounds);
            Rectangle r = new Rectangle(e.Bounds.X + 3, e.Bounds.Y + 1, e.Bounds.Width - 7, e.Bounds.Height - 3);
            if (sel) Theme.Gloss(g, r, Theme.C(235, 244, 253), Theme.C(220, 235, 252), Theme.C(193, 219, 252), Theme.C(205, 227, 252), Theme.C(125, 162, 206), 3);
            else if (e.Index == hot) Theme.Gloss(g, r, Theme.C(250, 251, 253), Theme.C(238, 244, 252), Theme.C(224, 236, 250), Theme.C(232, 241, 252), Theme.C(184, 214, 251), 3);
            int x = r.X + 8;
            if (Icons != null && e.Index < Icons.Images.Count)
            {
                g.DrawImage(Icons.Images[e.Index], x, r.Y + (r.Height - 16) / 2, 16, 16);
                x += 24;
            }
            TextFormatFlags fl = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            using (Font f = new Font(Font, sel ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(g, Convert.ToString(Items[e.Index]), f, new Rectangle(x, r.Y, r.Right - x - 4, r.Height), Color.Black, fl);
        }
    }

    /// <summary>Верхняя полоса с голубым стеклянным градиентом.</summary>
    class AeroHeader : Panel
    {
        Bitmap logo = Icons.Make("app", 28);
        public string Title = "Buba-Helper";

        public AeroHeader()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = ClientRectangle;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (LinearGradientBrush b = Theme.Grad(r, Theme.C(247, 250, 254), Theme.C(201, 219, 241), 90f)) g.FillRectangle(b, r);
            using (Pen p = new Pen(Color.FromArgb(200, 255, 255, 255))) g.DrawLine(p, 0, 0, Width, 0);
            using (Pen p = new Pen(Theme.C(160, 182, 210))) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            g.DrawImage(logo, Width - 36, (Height - 28) / 2, 28, 28);
            using (Font f = new Font("Segoe UI", 11f, FontStyle.Bold))
                TextRenderer.DrawText(g, Title, f, new Rectangle(Width - 300, 0, 258, Height), Theme.Ink,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>Значки в стиле Windows 7, нарисованные кодом (без внешних файлов).</summary>
    static class Icons
    {
        static Color C(int r, int g, int b) { return Theme.C(r, g, b); }

        static void Poly(Graphics g, PointF[] pts, Color a, Color b, Color border)
        {
            RectangleF bb = new RectangleF(0, 0, 16, 16);
            using (LinearGradientBrush br = Theme.Grad(bb, a, b, 90f)) g.FillPolygon(br, pts);
            using (Pen p = new Pen(border, 0.9f)) { p.LineJoin = LineJoin.Round; g.DrawPolygon(p, pts); }
        }

        static void Gloss(Graphics g, RectangleF r)
        {
            using (LinearGradientBrush br = Theme.Grad(r, Color.FromArgb(170, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                g.FillEllipse(br, r);
        }

        /// <summary>Значок name размером size x size на прозрачном фоне. Рисуется в сетке 16x16.</summary>
        public static Bitmap Make(string name, int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(size / 16f, size / 16f);
                RectangleF all = new RectangleF(0, 0, 16, 16);
                switch (name)
                {
                    case "disc":
                        using (LinearGradientBrush br = Theme.Grad(new RectangleF(1, 1, 14, 14), Color.White, C(150, 165, 180), 45f)) g.FillEllipse(br, 1, 1, 14, 14);
                        using (Pen p = new Pen(C(95, 110, 128), 0.9f)) g.DrawEllipse(p, 1, 1, 14, 14);
                        using (SolidBrush br = new SolidBrush(C(236, 241, 247))) g.FillEllipse(br, 5.8f, 5.8f, 4.4f, 4.4f);
                        using (Pen p = new Pen(C(120, 135, 152), 0.8f)) g.DrawEllipse(p, 5.8f, 5.8f, 4.4f, 4.4f);
                        using (Pen p = new Pen(Color.FromArgb(190, 255, 255, 255), 1f)) g.DrawArc(p, 3f, 3f, 10f, 10f, 200, 60);
                        break;
                    case "drive":
                        using (GraphicsPath gp = Theme.Round(new RectangleF(1, 4, 14, 8), 1.5f))
                        using (LinearGradientBrush br = Theme.Grad(new RectangleF(1, 4, 14, 8), C(232, 237, 242), C(150, 162, 176), 90f))
                        using (Pen p = new Pen(C(88, 100, 114), 0.9f)) { g.FillPath(br, gp); g.DrawPath(p, gp); }
                        using (SolidBrush br = new SolidBrush(C(110, 122, 136))) g.FillRectangle(br, 2.2f, 9.3f, 11.6f, 1.3f);
                        using (SolidBrush br = new SolidBrush(C(60, 220, 60))) g.FillEllipse(br, 11.2f, 9.4f, 1.7f, 1.7f);
                        using (Pen p = new Pen(Color.FromArgb(190, 255, 255, 255), 0.8f)) g.DrawLine(p, 2.5f, 5.1f, 13.5f, 5.1f);
                        break;
                    case "layout":
                        using (GraphicsPath gp = Theme.Round(new RectangleF(1, 3, 14, 10), 1.2f))
                        using (SolidBrush br = new SolidBrush(Color.White))
                        using (Pen p = new Pen(C(95, 105, 120), 0.9f)) { g.FillPath(br, gp); g.DrawPath(p, gp); }
                        using (LinearGradientBrush br = Theme.Grad(new RectangleF(2, 4, 4, 8), C(110, 170, 235), C(50, 110, 200), 90f)) g.FillRectangle(br, 2f, 4f, 4f, 8f);
                        using (LinearGradientBrush br = Theme.Grad(new RectangleF(6.5f, 4, 5, 8), C(130, 215, 110), C(50, 160, 50), 90f)) g.FillRectangle(br, 6.5f, 4f, 5f, 8f);
                        using (LinearGradientBrush br = Theme.Grad(new RectangleF(12, 4, 2, 8), C(250, 200, 100), C(230, 140, 30), 90f)) g.FillRectangle(br, 12f, 4f, 2f, 8f);
                        break;
                    case "gear":
                        {
                            List<PointF> pts = new List<PointF>();
                            for (int k = 0; k < 8; k++)
                            {
                                double a = k * Math.PI / 4;
                                double[] da = new double[] { -0.2, -0.12, 0.12, 0.2 };
                                double[] rr = new double[] { 5.0, 7.2, 7.2, 5.0 };
                                for (int m = 0; m < 4; m++)
                                    pts.Add(new PointF((float)(8 + rr[m] * Math.Cos(a + da[m])), (float)(8 + rr[m] * Math.Sin(a + da[m]))));
                            }
                            Poly(g, pts.ToArray(), C(205, 214, 226), C(112, 127, 147), C(70, 85, 105));
                            using (SolidBrush br = new SolidBrush(C(245, 248, 252))) g.FillEllipse(br, 5.8f, 5.8f, 4.4f, 4.4f);
                            using (Pen p = new Pen(C(90, 105, 125), 0.8f)) g.DrawEllipse(p, 5.8f, 5.8f, 4.4f, 4.4f);
                        }
                        break;
                    case "go":
                        using (LinearGradientBrush br = Theme.Grad(new RectangleF(1, 1, 14, 14), C(126, 219, 106), C(40, 150, 40), 90f)) g.FillEllipse(br, 1, 1, 14, 14);
                        using (Pen p = new Pen(C(31, 122, 31), 0.9f)) g.DrawEllipse(p, 1, 1, 14, 14);
                        Gloss(g, new RectangleF(2.5f, 1.5f, 11, 6.5f));
                        using (SolidBrush br = new SolidBrush(Color.White)) g.FillPolygon(br, new PointF[] { new PointF(5.6f, 4.6f), new PointF(11.8f, 8f), new PointF(5.6f, 11.4f) });
                        break;
                    case "camera":
                        using (GraphicsPath gp = Theme.Round(new RectangleF(1, 4.5f, 14, 9), 1.5f))
                        using (LinearGradientBrush br = Theme.Grad(new RectangleF(1, 4.5f, 14, 9), C(120, 130, 146), C(50, 58, 70), 90f))
                        using (Pen p = new Pen(C(35, 40, 50), 0.9f)) { g.FillPath(br, gp); g.DrawPath(p, gp); }
                        using (SolidBrush br = new SolidBrush(C(80, 90, 105))) g.FillRectangle(br, 5f, 2.8f, 6f, 2.2f);
                        using (LinearGradientBrush br = Theme.Grad(new RectangleF(5, 6, 6, 6), C(160, 208, 255), C(30, 80, 170), 90f)) g.FillEllipse(br, 5f, 6f, 6f, 6f);
                        using (Pen p = new Pen(C(20, 30, 50), 0.8f)) g.DrawEllipse(p, 5f, 6f, 6f, 6f);
                        using (SolidBrush br = new SolidBrush(Color.FromArgb(220, 255, 255, 255))) g.FillEllipse(br, 6.6f, 7f, 1.7f, 1.7f);
                        break;
                    case "doc":
                        Poly(g, new PointF[] { new PointF(3, 1), new PointF(10, 1), new PointF(13, 4), new PointF(13, 15), new PointF(3, 15) }, Color.White, C(226, 232, 240), C(110, 120, 135));
                        using (SolidBrush br = new SolidBrush(C(205, 214, 226))) g.FillPolygon(br, new PointF[] { new PointF(10, 1), new PointF(10, 4), new PointF(13, 4) });
                        using (Pen p = new Pen(C(70, 130, 200), 1f)) { g.DrawLine(p, 5f, 7f, 11f, 7f); g.DrawLine(p, 5f, 9.5f, 11f, 9.5f); g.DrawLine(p, 5f, 12f, 9f, 12f); }
                        break;
                    case "arrowR":
                        Poly(g, new PointF[] { new PointF(2, 5.5f), new PointF(8, 5.5f), new PointF(8, 2), new PointF(14, 8), new PointF(8, 14), new PointF(8, 10.5f), new PointF(2, 10.5f) }, C(120, 180, 240), C(36, 96, 186), C(25, 70, 140));
                        break;
                    case "arrowL":
                        Poly(g, new PointF[] { new PointF(14, 5.5f), new PointF(8, 5.5f), new PointF(8, 2), new PointF(2, 8), new PointF(8, 14), new PointF(8, 10.5f), new PointF(14, 10.5f) }, C(120, 180, 240), C(36, 96, 186), C(25, 70, 140));
                        break;
                    case "star":
                        {
                            PointF[] st = new PointF[10];
                            for (int k = 0; k < 10; k++)
                            {
                                double a = -Math.PI / 2 + k * Math.PI / 5, rr = k % 2 == 0 ? 7.4 : 3.1;
                                st[k] = new PointF((float)(8 + rr * Math.Cos(a)), (float)(8.5 + rr * Math.Sin(a)));
                            }
                            Poly(g, st, C(255, 232, 120), C(240, 170, 20), C(170, 110, 10));
                        }
                        break;
                    case "globe":
                        using (LinearGradientBrush br = Theme.Grad(new RectangleF(1, 1, 14, 14), C(150, 205, 250), C(30, 100, 190), 90f)) g.FillEllipse(br, 1, 1, 14, 14);
                        using (SolidBrush br = new SolidBrush(C(90, 190, 90)))
                        {
                            g.FillPolygon(br, new PointF[] { new PointF(4, 4), new PointF(7.5f, 3.5f), new PointF(8, 6.5f), new PointF(5.5f, 8.5f), new PointF(3.5f, 7f) });
                            g.FillPolygon(br, new PointF[] { new PointF(9.5f, 8f), new PointF(12.5f, 7.5f), new PointF(12f, 11f), new PointF(10f, 12.5f) });
                        }
                        using (Pen p = new Pen(C(20, 70, 140), 0.9f)) g.DrawEllipse(p, 1, 1, 14, 14);
                        Gloss(g, new RectangleF(2.5f, 1.5f, 11, 6.5f));
                        break;
                    case "monitor":
                        using (GraphicsPath gp = Theme.Round(new RectangleF(1, 2, 14, 9.5f), 1.2f))
                        using (SolidBrush br = new SolidBrush(C(70, 80, 95)))
                        using (Pen p = new Pen(C(40, 48, 60), 0.9f)) { g.FillPath(br, gp); g.DrawPath(p, gp); }
                        using (LinearGradientBrush br = Theme.Grad(new RectangleF(2.4f, 3.4f, 11.2f, 6.7f), C(150, 205, 250), C(40, 110, 200), 90f)) g.FillRectangle(br, 2.4f, 3.4f, 11.2f, 6.7f);
                        using (SolidBrush br = new SolidBrush(C(120, 130, 145))) { g.FillRectangle(br, 6.5f, 11.5f, 3f, 1.8f); g.FillRectangle(br, 4.5f, 13.2f, 7f, 1.3f); }
                        break;
                    case "user":
                        using (LinearGradientBrush br = Theme.Grad(new RectangleF(4.5f, 1.5f, 7, 7), C(255, 220, 190), C(220, 160, 120), 90f)) g.FillEllipse(br, 4.8f, 1.5f, 6.4f, 6.4f);
                        using (LinearGradientBrush br = Theme.Grad(new RectangleF(2, 9, 12, 6), C(110, 170, 235), C(40, 100, 190), 90f)) g.FillPie(br, 2f, 8.6f, 12f, 12f, 180, 180);
                        using (Pen p = new Pen(C(70, 90, 120), 0.8f)) { g.DrawEllipse(p, 4.8f, 1.5f, 6.4f, 6.4f); g.DrawArc(p, 2f, 8.6f, 12f, 12f, 180, 180); }
                        break;
                    case "shield":
                        Poly(g, new PointF[] { new PointF(8, 1), new PointF(14, 3), new PointF(14, 8), new PointF(8, 15), new PointF(2, 8), new PointF(2, 3) }, C(130, 190, 245), C(30, 95, 185), C(20, 65, 130));
                        using (Pen p = new Pen(Color.White, 1.6f)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLines(p, new PointF[] { new PointF(5.2f, 7.8f), new PointF(7.3f, 10f), new PointF(11f, 5.4f) }); }
                        break;
                    case "net":
                        using (Pen p = new Pen(C(80, 100, 130), 1.2f)) { g.DrawLine(p, 8f, 4f, 3.5f, 11.5f); g.DrawLine(p, 8f, 4f, 12.5f, 11.5f); g.DrawLine(p, 3.5f, 11.5f, 12.5f, 11.5f); }
                        foreach (PointF c in new PointF[] { new PointF(8, 3.6f), new PointF(3.5f, 11.8f), new PointF(12.5f, 11.8f) })
                        {
                            using (LinearGradientBrush br = Theme.Grad(new RectangleF(c.X - 2.4f, c.Y - 2.4f, 4.8f, 4.8f), C(150, 205, 250), C(30, 100, 190), 90f)) g.FillEllipse(br, c.X - 2.4f, c.Y - 2.4f, 4.8f, 4.8f);
                            using (Pen p = new Pen(C(20, 70, 140), 0.8f)) g.DrawEllipse(p, c.X - 2.4f, c.Y - 2.4f, 4.8f, 4.8f);
                        }
                        break;
                    case "key":
                        using (Pen p = new Pen(C(200, 150, 20), 2.2f)) { g.DrawLine(p, 8.5f, 8f, 14f, 13.5f); g.DrawLine(p, 12f, 11.5f, 13.4f, 10.2f); }
                        using (LinearGradientBrush br = Theme.Grad(new RectangleF(1.5f, 1.5f, 8, 8), C(255, 232, 120), C(230, 165, 20), 90f)) g.FillEllipse(br, 1.5f, 1.5f, 8f, 8f);
                        using (Pen p = new Pen(C(160, 105, 10), 0.9f)) g.DrawEllipse(p, 1.5f, 1.5f, 8f, 8f);
                        using (SolidBrush br = new SolidBrush(Color.White)) g.FillEllipse(br, 3.6f, 3.6f, 3.8f, 3.8f);
                        break;
                    case "trash":
                        using (GraphicsPath gp = Theme.Round(new RectangleF(3, 5, 10, 10), 1.2f))
                        using (LinearGradientBrush br = Theme.Grad(new RectangleF(3, 5, 10, 10), C(235, 240, 246), C(140, 155, 172), 0f))
                        using (Pen p = new Pen(C(90, 105, 122), 0.9f)) { g.FillPath(br, gp); g.DrawPath(p, gp); }
                        using (Pen p = new Pen(C(90, 105, 122), 1.3f)) { g.DrawLine(p, 2f, 4f, 14f, 4f); g.DrawLine(p, 6f, 2.2f, 10f, 2.2f); g.DrawLine(p, 6f, 2.2f, 6f, 4f); g.DrawLine(p, 10f, 2.2f, 10f, 4f); }
                        using (Pen p = new Pen(C(110, 125, 142), 1f)) { g.DrawLine(p, 6f, 7f, 6f, 13f); g.DrawLine(p, 8f, 7f, 8f, 13f); g.DrawLine(p, 10f, 7f, 10f, 13f); }
                        break;
                    case "box":
                        Poly(g, new PointF[] { new PointF(8, 1.5f), new PointF(14, 4.5f), new PointF(8, 7.5f), new PointF(2, 4.5f) }, C(250, 215, 150), C(235, 180, 100), C(150, 105, 40));
                        Poly(g, new PointF[] { new PointF(2, 4.5f), new PointF(8, 7.5f), new PointF(8, 14.5f), new PointF(2, 11.5f) }, C(225, 170, 95), C(190, 130, 60), C(150, 105, 40));
                        Poly(g, new PointF[] { new PointF(14, 4.5f), new PointF(8, 7.5f), new PointF(8, 14.5f), new PointF(14, 11.5f) }, C(205, 150, 80), C(165, 110, 45), C(150, 105, 40));
                        break;
                    case "script":
                        Poly(g, new PointF[] { new PointF(3, 1), new PointF(13, 1), new PointF(13, 15), new PointF(3, 15) }, C(70, 80, 95), C(35, 42, 52), C(20, 25, 32));
                        using (Pen p = new Pen(C(120, 230, 120), 1.2f)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLines(p, new PointF[] { new PointF(5f, 5f), new PointF(7.4f, 7.2f), new PointF(5f, 9.4f) }); g.DrawLine(p, 8.4f, 10.4f, 11.2f, 10.4f); }
                        break;
                    case "clock":
                        using (LinearGradientBrush br = Theme.Grad(new RectangleF(1, 1, 14, 14), Color.White, C(205, 215, 228), 90f)) g.FillEllipse(br, 1, 1, 14, 14);
                        using (Pen p = new Pen(C(70, 90, 120), 1f)) g.DrawEllipse(p, 1, 1, 14, 14);
                        using (Pen p = new Pen(C(40, 60, 95), 1.3f)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLine(p, 8f, 8f, 8f, 3.8f); g.DrawLine(p, 8f, 8f, 11f, 9.6f); }
                        break;
                    case "check":
                        using (LinearGradientBrush br = Theme.Grad(new RectangleF(1, 1, 14, 14), C(126, 219, 106), C(40, 150, 40), 90f)) g.FillEllipse(br, 1, 1, 14, 14);
                        using (Pen p = new Pen(C(31, 122, 31), 0.9f)) g.DrawEllipse(p, 1, 1, 14, 14);
                        Gloss(g, new RectangleF(2.5f, 1.5f, 11, 6.5f));
                        using (Pen p = new Pen(Color.White, 1.9f)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round; g.DrawLines(p, new PointF[] { new PointF(4.4f, 8.2f), new PointF(7f, 10.8f), new PointF(11.8f, 5.4f) }); }
                        break;
                    default:   // "app": синий шар со стрелкой вниз к диску
                        using (LinearGradientBrush br = Theme.Grad(new RectangleF(0.5f, 0.5f, 15, 15), C(127, 196, 245), C(27, 101, 184), 90f)) g.FillEllipse(br, 0.5f, 0.5f, 15, 15);
                        using (Pen p = new Pen(C(19, 74, 138), 0.8f)) g.DrawEllipse(p, 0.5f, 0.5f, 15, 15);
                        Gloss(g, new RectangleF(2.2f, 1.2f, 11.6f, 7f));
                        using (SolidBrush br = new SolidBrush(Color.White))
                        {
                            g.FillPolygon(br, new PointF[] { new PointF(6.5f, 3.4f), new PointF(9.5f, 3.4f), new PointF(9.5f, 7.2f), new PointF(12f, 7.2f), new PointF(8f, 11.2f), new PointF(4f, 7.2f), new PointF(6.5f, 7.2f) });
                            g.FillRectangle(br, 3.6f, 12.2f, 8.8f, 1.5f);
                        }
                        break;
                }
            }
            return bmp;
        }

        public static ImageList List(params string[] names)
        {
            ImageList il = new ImageList();
            il.ColorDepth = ColorDepth.Depth32Bit;
            il.ImageSize = new Size(16, 16);
            foreach (string n in names) il.Images.Add(Make(n, 16));
            return il;
        }

        public static Icon AppIcon()
        {
            using (Bitmap b = Make("app", 32)) return Icon.FromHandle(b.GetHicon());
        }

        /// <summary>Записывает значок приложения (16/24/32/48 px) в .ico - для /win32icon при сборке exe.</summary>
        public static void WriteIco(string path)
        {
            int[] sizes = new int[] { 16, 24, 32, 48 };
            List<byte[]> frames = new List<byte[]>();
            foreach (int s in sizes)
            {
                byte[] px = new byte[s * s * 4];
                using (Bitmap b = Make("app", s))
                {
                    BitmapData d = b.LockBits(new Rectangle(0, 0, s, s), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    Marshal.Copy(d.Scan0, px, 0, px.Length);
                    b.UnlockBits(d);
                }
                int maskRow = ((s + 31) / 32) * 4;
                using (MemoryStream ms = new MemoryStream())
                using (BinaryWriter w = new BinaryWriter(ms))
                {
                    w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32);
                    w.Write(0); w.Write(px.Length + maskRow * s); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                    for (int y = s - 1; y >= 0; y--) w.Write(px, y * s * 4, s * 4);   // BMP хранится снизу вверх
                    w.Write(new byte[maskRow * s]);
                    w.Flush();
                    frames.Add(ms.ToArray());
                }
            }
            using (FileStream fs = new FileStream(path, FileMode.Create))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)sizes[i]); w.Write((byte)sizes[i]); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32); w.Write(frames[i].Length); w.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (byte[] f in frames) w.Write(f);
            }
        }
    }
}
