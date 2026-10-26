// Свои контролы тёмной темы. Каждый рисует себя сам с двойной буферизацией (без мерцания) и не зависит от системной темы.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinInstaller
{
    /// <summary>Панель с двойной буферизацией: перестройка раскладки не мигает.</summary>
    class DPanel : Panel
    {
        public DPanel() { DoubleBuffered = true; ResizeRedraw = true; }
    }

    static class Ui
    {
        /// <summary>Прокрутка только по вертикали: горизонтальная полоса у страниц не нужна и не должна появляться.</summary>
        public static void VerticalOnly(ScrollableControl c)
        {
            c.AutoScroll = false;
            c.HorizontalScroll.Enabled = false;
            c.HorizontalScroll.Visible = false;
            c.HorizontalScroll.Maximum = 0;
            c.AutoScroll = true;
        }
    }

    /// <summary>Привязка размеров без Anchor: растянуть или прижать вправо при изменении ширины родителя (надёжнее якорей до первого показа).</summary>
    static class Fluid
    {
        public static void Stretch(Control c, int right) { Apply(c, right, true); }
        public static void PinRight(Control c, int right) { Apply(c, right, false); }

        static void Apply(Control c, int right, bool stretch)
        {
            Control p = c.Parent;
            Action fit = delegate
            {
                int w = p.ClientSize.Width;
                if (stretch) { int nw = Math.Max(24, w - c.Left - right); if (c.Width != nw) c.Width = nw; }
                else { int nl = w - c.Width - right; if (c.Left != nl) c.Left = nl; }
            };
            p.SizeChanged += delegate { fit(); };
            fit();
        }
    }

    /// <summary>Карточка: скруглённая тёмная поверхность с заголовком.</summary>
    class UiCard : DPanel
    {
        public int Pad = 18;

        public UiCard() { BackColor = Theme.Card; ForeColor = Theme.Text; }

        /// <summary>Y, с которого начинается содержимое (под заголовком).</summary>
        public int BodyTop { get { return Text.Length > 0 ? 48 : 18; } }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.ParentBg(this));
            Theme.Smooth(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            Theme.FillRound(g, r, 10, BackColor);
            Theme.StrokeRound(g, r, 10, Theme.Line, 1f);
            if (Text.Length > 0)
                TextRenderer.DrawText(g, Text, Theme.Semi, new Rectangle(Pad, 14, Width - 2 * Pad, 24), Theme.Text, Theme.TF);
        }
    }

    /// <summary>Кнопка: обычная, главная (акцентная) и «призрачная».</summary>
    class UiButton : Button
    {
        bool hot, down;
        public bool Primary, Ghost;
        string icon;
        public bool IconRight;

        public string Icon { get { return icon; } set { icon = value; Invalidate(); } }

        public UiButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.UI;
            Cursor = Cursors.Hand;
            Height = 34;
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnVisibleChanged(EventArgs e) { hot = down = false; base.OnVisibleChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.ParentBg(this));
            Theme.Smooth(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            Color fg = Theme.Text;
            if (!Enabled)
            {
                Theme.FillRound(g, r, 8, Primary ? Theme.C(38, 44, 66) : Ghost ? Theme.ParentBg(this) : Theme.C(27, 31, 40));
                fg = Theme.Dim;
            }
            else if (Primary)
            {
                Color t = down ? Theme.AccentLo : hot ? Theme.C(138, 164, 255) : Theme.C(108, 138, 255);
                Color b = down ? Theme.C(60, 86, 210) : hot ? Theme.C(100, 130, 255) : Theme.C(78, 108, 244);
                Theme.GradRound(g, r, 8, t, b);
                fg = Color.White;
            }
            else if (Ghost)
            {
                if (down) Theme.FillRound(g, r, 8, Theme.Card2);
                else if (hot) Theme.FillRound(g, r, 8, Theme.Hover);
                fg = hot ? Theme.Text : Theme.Muted;
            }
            else
            {
                Theme.FillRound(g, r, 8, down ? Theme.Card2 : hot ? Theme.Hover : Theme.Field);
                Theme.StrokeRound(g, r, 8, hot ? Theme.LineHi : Theme.Line, 1f);
            }
            if (Enabled && Focused && ShowFocusCues)
                Theme.StrokeRound(g, new RectangleF(2.5f, 2.5f, Width - 6, Height - 6), 6, Theme.AccentHi, 1.5f);

            Font f = Primary ? Theme.Semi : Font;
            string text = Text ?? "";
            Size ts = TextRenderer.MeasureText(g, text, f, new Size(int.MaxValue, int.MaxValue), Theme.TF);
            int gw = icon == null ? 0 : 18 + (text.Length > 0 ? 8 : 0);
            int avail = Math.Max(10, Width - 16 - gw);
            int tw = Math.Min(ts.Width, avail);
            int x = (Width - (tw + gw)) / 2;
            int gx = IconRight ? x + tw + 8 : x;
            int tx = IconRight ? x : x + gw;
            if (icon != null)
                g.DrawImage(Icons.Get(icon, 18, fg), gx, (Height - 18) / 2, 18, 18);
            TextRenderer.DrawText(g, text, f, new Rectangle(tx, 0, tw + 2, Height), fg, Theme.TF);
        }
    }

    /// <summary>Вкладка режима в верхней панели (радиокнопка в виде вкладки с подчёркиванием).</summary>
    class UiTab : RadioButton
    {
        bool hot;
        public string Icon;
        public int Pad = 54;   // поля вокруг подписи (вместе со значком); сужается, если вкладки не помещаются в шапку

        public UiTab()
        {
            Appearance = Appearance.Button;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.Semi;
            Cursor = Cursors.Hand;
        }
        public int PreferredW()
        {
            return Pad + TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), Theme.TF).Width;
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.ParentBg(this));
            Theme.Smooth(g);
            Color fg = Checked ? Theme.Text : hot ? Theme.C(205, 211, 224) : Theme.Muted;
            if (!Enabled) fg = Theme.Dim;
            if (Checked)
            {
                Theme.FillRound(g, new RectangleF(2, 9, Width - 5, Height - 18), 8, Theme.C(26, 31, 42));
                using (GraphicsPath gp = Theme.Round(new RectangleF(14, Height - 4, Width - 28, 3), 1.5f))
                using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillPath(b, gp);
            }
            else if (hot) Theme.FillRound(g, new RectangleF(2, 9, Width - 5, Height - 18), 8, Theme.C(22, 26, 34));
            int tw = TextRenderer.MeasureText(g, Text, Font, new Size(int.MaxValue, int.MaxValue), Theme.TF).Width;
            int gw = Icon == null ? 0 : 26;
            int x = (Width - tw - gw) / 2;
            if (Icon != null) g.DrawImage(Icons.Get(Icon, 20, Checked ? Theme.AccentHi : fg), x, (Height - 20) / 2 - 1, 20, 20);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + gw, 0, tw + 4, Height - 2), fg, Theme.TF);
        }
    }

    /// <summary>Флажок: скруглённый квадрат, при включении заливается акцентом с галочкой.</summary>
    class UiCheck : CheckBox
    {
        bool hot;

        public UiCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.UI;
            Cursor = Cursors.Hand;
            Height = 24;
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.ParentBg(this));
            Theme.Smooth(g);
            RectangleF box = new RectangleF(1.5f, 1.5f, 17, 17);
            if (Checked)
            {
                Theme.GradRound(g, box, 5, Enabled ? Theme.C(108, 138, 255) : Theme.C(52, 62, 100), Enabled ? Theme.C(78, 108, 244) : Theme.C(44, 52, 88));
                using (Pen p = new Pen(Color.White, 2f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                    g.DrawLines(p, new PointF[] { new PointF(5.6f, 10.6f), new PointF(8.6f, 13.6f), new PointF(14.4f, 6.8f) });
                }
            }
            else
            {
                Theme.FillRound(g, box, 5, Theme.Field);
                Theme.StrokeRound(g, box, 5, hot && Enabled ? Theme.LineHi : Theme.Line, 1.3f);
            }
            if (Focused && ShowFocusCues) Theme.StrokeRound(g, new RectangleF(0.5f, 0.5f, 19, 19), 6, Theme.AccentHi, 1.2f);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(28, 1, Math.Max(10, Width - 28), Height - 1), Enabled ? Theme.Text : Theme.Dim,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.Top);
        }
    }

    /// <summary>Переключатель (радиокнопка): круг с точкой.</summary>
    class UiRadio : RadioButton
    {
        bool hot;

        public UiRadio()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.UI;
            Cursor = Cursors.Hand;
            Height = 24;
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.ParentBg(this));
            Theme.Smooth(g);
            RectangleF c = new RectangleF(1.5f, 1.5f, 17, 17);
            if (Checked)
            {
                using (SolidBrush b = new SolidBrush(Enabled ? Theme.Accent : Theme.C(52, 62, 100))) g.FillEllipse(b, c);
                using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, 6.5f, 6.5f, 7f, 7f);
            }
            else
            {
                using (SolidBrush b = new SolidBrush(Theme.Field)) g.FillEllipse(b, c);
                using (Pen p = new Pen(hot && Enabled ? Theme.LineHi : Theme.Line, 1.3f)) g.DrawEllipse(p, c);
            }
            if (Focused && ShowFocusCues) using (Pen p = new Pen(Theme.AccentHi, 1.2f)) g.DrawEllipse(p, 0.5f, 0.5f, 19, 19);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(28, 1, Math.Max(10, Width - 28), Height - 1), Enabled ? Theme.Text : Theme.Dim,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.Top);
        }
    }

    /// <summary>Выпадающий список: вся поверхность рисуется кодом (WM_PAINT), список — владельческая отрисовка.
    /// Текст можно задать и значением вне списка: оно добавится как пункт (ведёт себя как редактируемый).</summary>
    class UiCombo : ComboBox
    {
        [StructLayout(LayoutKind.Sequential)] struct PRect { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)]
        struct PAINTSTRUCT
        {
            public IntPtr hdc; public bool fErase; public PRect rc; public bool fRestore; public bool fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] reserved;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct CBI { public int cbSize; public PRect rcItem; public PRect rcButton; public int stateButton; public IntPtr hwndCombo; public IntPtr hwndEdit; public IntPtr hwndList; }
        [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr h, out PAINTSTRUCT ps);
        [DllImport("user32.dll")] static extern bool EndPaint(IntPtr h, ref PAINTSTRUCT ps);
        [DllImport("user32.dll")] static extern bool GetComboBoxInfo(IntPtr h, ref CBI info);

        bool hot;

        public UiCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            ItemHeight = 26;
            Font = Theme.UI;
            BackColor = Theme.Field;
            ForeColor = Theme.Text;
            Cursor = Cursors.Hand;
        }

        public override string Text
        {
            get { return base.Text; }
            set
            {
                if (!string.IsNullOrEmpty(value) && FindStringExact(value) < 0) Items.Add(value);
                base.Text = value;
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnSelectedIndexChanged(EventArgs e) { Invalidate(); base.OnSelectedIndexChanged(e); }
        protected override void OnDropDownClosed(EventArgs e) { Invalidate(); base.OnDropDownClosed(e); }

        protected override void OnDropDown(EventArgs e)
        {
            int w = Width;
            foreach (object it in Items)
                w = Math.Max(w, TextRenderer.MeasureText(GetItemText(it), Font).Width + 36);
            DropDownWidth = Math.Min(w, 760);
            try
            {
                CBI info = new CBI();
                info.cbSize = Marshal.SizeOf(typeof(CBI));
                if (GetComboBoxInfo(Handle, ref info) && info.hwndList != IntPtr.Zero) Dark.Scroll(info.hwndList);
            }
            catch { }
            Invalidate();
            base.OnDropDown(e);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (SolidBrush b = new SolidBrush(sel ? Theme.C(44, 56, 98) : Theme.C(28, 33, 43))) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 14, e.Bounds.Height),
                sel ? Color.White : Theme.Text, Theme.TF);
        }

        void PaintFace(Graphics g)
        {
            g.Clear(Theme.ParentBg(this));
            Theme.Smooth(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            bool open = DroppedDown;
            Theme.FillRound(g, r, 7, Enabled ? (hot && !open ? Theme.C(38, 44, 57) : Theme.Field) : Theme.C(27, 31, 40));
            Theme.StrokeRound(g, r, 7, open || (Focused && ShowFocusCues) ? Theme.Accent : hot ? Theme.LineHi : Theme.Line, 1f);
            string text = SelectedItem != null ? GetItemText(SelectedItem) : "";
            TextRenderer.DrawText(g, text, Font, new Rectangle(10, 0, Width - 34, Height), Enabled ? Theme.Text : Theme.Dim, Theme.TF);
            using (Pen p = new Pen(Enabled ? Theme.Muted : Theme.Dim, 1.8f))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                float cx = Width - 17, cy = Height / 2f;
                g.DrawLines(p, new PointF[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x14) { m.Result = (IntPtr)1; return; }   // WM_ERASEBKGND
            if (m.Msg == 0xF)                                       // WM_PAINT
            {
                PAINTSTRUCT ps;
                IntPtr hdc = BeginPaint(m.HWnd, out ps);
                try
                {
                    if (Width > 0 && Height > 0)
                        using (Bitmap bmp = new Bitmap(Width, Height))
                        {
                            using (Graphics bg = Graphics.FromImage(bmp)) PaintFace(bg);
                            using (Graphics g = Graphics.FromHdc(hdc)) g.DrawImageUnscaled(bmp, 0, 0);
                        }
                }
                finally { EndPaint(m.HWnd, ref ps); }
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }
    }

    /// <summary>Рамка поля ввода: скруглённая подложка вокруг обычного TextBox / NumericUpDown (рамка подсвечивается при фокусе).</summary>
    class FieldBox : DPanel
    {
        public readonly Control Inner;
        bool focus;

        class Spin : Control
        {
            readonly NumericUpDown nud;
            int hot;   // 1 - вверх, 2 - вниз
            public Spin(NumericUpDown n)
            {
                nud = n;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                Dock = DockStyle.Right; Width = 22; Cursor = Cursors.Hand;
            }
            int Part(Point p) { return p.Y < Height / 2 ? 1 : 2; }
            protected override void OnMouseMove(MouseEventArgs e) { int h = Part(e.Location); if (h != hot) { hot = h; Invalidate(); } base.OnMouseMove(e); }
            protected override void OnMouseLeave(EventArgs e) { hot = 0; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (!nud.Enabled) return;
                if (Part(e.Location) == 1) nud.UpButton(); else nud.DownButton();
                base.OnMouseDown(e);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Theme.Field);
                Theme.Smooth(g);
                float cx = Width / 2f;
                for (int i = 1; i <= 2; i++)
                {
                    float cy = i == 1 ? Height * 0.27f : Height * 0.73f;
                    float d = i == 1 ? -1.5f : 1.5f;
                    using (Pen p = new Pen(hot == i && nud.Enabled ? Theme.Text : Theme.Muted, 1.6f))
                    {
                        p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                        g.DrawLines(p, new PointF[] { new PointF(cx - 3.5f, cy - d), new PointF(cx, cy + d), new PointF(cx + 3.5f, cy - d) });
                    }
                }
            }
        }

        public FieldBox(Control inner)
        {
            Inner = inner;
            Padding = new Padding(3);
            DPanel host = new DPanel();
            host.Dock = DockStyle.Fill;
            host.BackColor = Theme.Field;
            host.Padding = new Padding(7, 4, 7, 4);
            Controls.Add(host);
            inner.Dock = DockStyle.Fill;
            host.Controls.Add(inner);
            NumericUpDown nud = inner as NumericUpDown;
            if (nud != null)
            {
                foreach (Control k in nud.Controls) if (!(k is TextBox)) k.Visible = false;   // свои стрелки вместо системных
                host.Padding = new Padding(7, 4, 0, 4);
                host.Controls.Add(new Spin(nud));
            }
            inner.GotFocus += delegate { focus = true; Invalidate(); };
            inner.LostFocus += delegate { focus = false; Invalidate(); };
            inner.EnabledChanged += delegate { host.BackColor = inner.Enabled ? Theme.Field : Theme.C(28, 32, 41); inner.BackColor = host.BackColor; Invalidate(); };
        }

        /// <summary>Оформляет поле и возвращает рамку, которую нужно класть в раскладку вместо самого поля.</summary>
        public static FieldBox Wrap(Control c)
        {
            c.Font = c.Font == null || c.Font.Name != "Consolas" ? Theme.UI : c.Font;
            c.BackColor = Theme.Field;
            c.ForeColor = Theme.Text;
            TextBox tb = c as TextBox;
            if (tb != null) tb.BorderStyle = BorderStyle.None;
            NumericUpDown nud = c as NumericUpDown;
            if (nud != null) { nud.BorderStyle = BorderStyle.None; nud.TextAlign = HorizontalAlignment.Left; }
            if (tb != null && tb.Multiline) Dark.Scroll(tb);
            return new FieldBox(c);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.ParentBg(this));
            Theme.Smooth(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            Theme.FillRound(g, r, 7, Inner.Enabled ? Theme.Field : Theme.C(28, 32, 41));
            Theme.StrokeRound(g, r, 7, focus ? Theme.Accent : Theme.Line, 1f);
        }
    }

    /// <summary>Тонкий индикатор выполнения с акцентной заливкой.</summary>
    class UiProgress : ProgressBar
    {
        public UiProgress()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 8;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.ParentBg(this));
            Theme.Smooth(g);
            RectangleF b = new RectangleF(0, 0, Width, Height);
            Theme.FillRound(g, b, Height / 2f, Theme.Field);
            double frac = Maximum > Minimum ? (double)(Value - Minimum) / (Maximum - Minimum) : 0;
            float w = (float)(Width * Math.Max(0, Math.Min(1, frac)));
            if (w >= Height) Theme.GradRound(g, new RectangleF(0, 0, w, Height), Height / 2f, Theme.C(124, 152, 255), Theme.C(139, 108, 255));
            else if (w > 1) Theme.FillRound(g, new RectangleF(0, 0, Math.Max(w, Height / 2f), Height), Height / 2f, Theme.Accent);
        }
    }

    class NavItem
    {
        public string Text, Icon;
        public int Badge;
        public Color BadgeColor = Theme.Warn;
    }

    /// <summary>Боковое меню шагов/страниц: значок и название, активный пункт подсвечен акцентом. Перерисовываются только затронутые пункты.</summary>
    class NavList : Control
    {
        public readonly List<NavItem> Items = new List<NavItem>();
        public bool Numbered;     // кружки с номерами шагов вместо значков
        public const int ItemH = 38, Top0 = 12;
        int sel = -1, hot = -1, scroll;

        public event EventHandler SelectedIndexChanged;

        public NavList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Font = Theme.UI;
            Cursor = Cursors.Hand;
        }

        public int SelectedIndex
        {
            get { return sel; }
            set
            {
                if (value == sel) return;
                SelectSilently(value);
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        /// <summary>Выбрать пункт без события (синхронизация с вкладками).</summary>
        public void SelectSilently(int i)
        {
            int old = sel;
            sel = i;
            Inv(old); Inv(sel);
            EnsureVisible(sel);
        }

        public NavItem Add(string text, string icon)
        {
            NavItem it = new NavItem(); it.Text = text; it.Icon = icon;
            Items.Add(it);
            Invalidate();
            return it;
        }

        public void ClearItems() { Items.Clear(); sel = hot = -1; scroll = 0; Invalidate(); }
        public void SetText(int i, string text) { if (i >= 0 && i < Items.Count && Items[i].Text != text) { Items[i].Text = text; Inv(i); } }
        public void SetBadge(int i, int n, Color c)
        {
            if (i < 0 || i >= Items.Count || (Items[i].Badge == n && Items[i].BadgeColor == c)) return;
            Items[i].Badge = n; Items[i].BadgeColor = c; Inv(i);
        }

        int MaxScroll { get { return Math.Max(0, Top0 * 2 + Items.Count * ItemH - Height); } }

        void Inv(int i) { Rectangle r = ItemRect(i); if (!r.IsEmpty) Invalidate(r); }

        Rectangle ItemRect(int i)
        {
            if (i < 0 || i >= Items.Count) return Rectangle.Empty;
            return new Rectangle(8, Top0 + i * ItemH - scroll, Width - 16, ItemH - 4);
        }

        void EnsureVisible(int i)
        {
            if (i < 0 || i >= Items.Count) return;
            Rectangle r = ItemRect(i);
            int old = scroll;
            if (r.Top < Top0) scroll = Math.Max(0, scroll + r.Top - Top0);
            else if (r.Bottom > Height - Top0) scroll = Math.Min(MaxScroll, scroll + r.Bottom - (Height - Top0));
            if (old != scroll) Invalidate();
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < Items.Count; i++) if (ItemRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != hot) { int old = hot; hot = h; Inv(old); Inv(h); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { int old = hot; hot = -1; Inv(old); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int h = HitTest(e.Location);
            if (h >= 0 && e.Button == MouseButtons.Left) SelectedIndex = h;
            base.OnMouseDown(e);
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int ns = Math.Max(0, Math.Min(MaxScroll, scroll - Math.Sign(e.Delta) * ItemH));
            if (ns != scroll) { scroll = ns; Invalidate(); }
            base.OnMouseWheel(e);
        }
        protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down && sel < Items.Count - 1) { SelectedIndex = sel + 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Up && sel > 0) { SelectedIndex = sel - 1; e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Inv(sel); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Inv(sel); base.OnLostFocus(e); }
        protected override void OnResize(EventArgs e) { scroll = Math.Min(scroll, MaxScroll); base.OnResize(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Side);
            Theme.Smooth(g);
            for (int i = 0; i < Items.Count; i++)
            {
                Rectangle r = ItemRect(i);
                if (r.Bottom < 0 || r.Top > Height || !r.IntersectsWith(e.ClipRectangle)) continue;
                bool isSel = i == sel, isHot = i == hot;
                NavItem it = Items[i];
                if (isSel) Theme.FillRound(g, r, 9, Theme.AccentSoft);
                else if (isHot) Theme.FillRound(g, r, 9, Theme.C(24, 28, 37));
                if (isSel)
                {
                    using (GraphicsPath gp = Theme.Round(new RectangleF(r.X, r.Y + 9, 3.5f, r.Height - 18), 1.7f))
                    using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillPath(b, gp);
                }
                if (isSel && Focused && ShowFocusCues) Theme.StrokeRound(g, new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 9, Theme.Accent, 1f);
                int x = r.X + 14;
                Color fg = isSel ? Theme.Text : isHot ? Theme.C(204, 210, 224) : Theme.Muted;
                if (Numbered)
                {
                    RectangleF c = new RectangleF(x, r.Y + (r.Height - 24) / 2f, 24, 24);
                    bool done = sel > i;
                    if (isSel) using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, c);
                    else if (done) using (SolidBrush b = new SolidBrush(Theme.C(26, 54, 48))) g.FillEllipse(b, c);
                    else using (Pen p = new Pen(Theme.LineHi, 1.3f)) g.DrawEllipse(p, c.X + 0.5f, c.Y + 0.5f, c.Width - 1, c.Height - 1);
                    if (done) g.DrawImage(Icons.Get("tick", 16, Theme.Ok), c.X + 4, c.Y + 4, 16, 16);
                    else TextRenderer.DrawText(g, (i + 1).ToString(), Theme.Small, Rectangle.Round(c), isSel ? Color.White : Theme.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    x += 36;
                }
                else if (it.Icon != null)
                {
                    g.DrawImage(Icons.Get(it.Icon, 20, isSel ? Theme.AccentHi : fg), x, r.Y + (r.Height - 20) / 2, 20, 20);
                    x += 32;
                }
                int right = r.Right - 10;
                if (it.Badge > 0)
                {
                    string bt = it.Badge > 99 ? "99+" : it.Badge.ToString();
                    int bw = Math.Max(20, TextRenderer.MeasureText(bt, Theme.Small).Width + 10);
                    RectangleF br = new RectangleF(right - bw, r.Y + (r.Height - 18) / 2f, bw, 18);
                    Theme.FillRound(g, br, 9, Theme.Mix(Theme.Side, it.BadgeColor, 0.22));
                    TextRenderer.DrawText(g, bt, Theme.Small, Rectangle.Round(br), it.BadgeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    right -= bw + 8;
                }
                TextRenderer.DrawText(g, it.Text, isSel ? Theme.Semi : Font, new Rectangle(x, r.Y, Math.Max(10, right - x), r.Height), isSel ? Theme.Text : fg, Theme.TF);
            }
            if (MaxScroll > 0)
            {
                float th = Math.Max(30f, (float)Height * Height / (Height + MaxScroll));
                float ty = (Height - th) * scroll / MaxScroll;
                Theme.FillRound(g, new RectangleF(Width - 4, ty, 3, th), 1.5f, Theme.Line);
            }
        }
    }

    /// <summary>Таблица (ListView) в тёмной теме: свои заголовок и строки, последняя колонка занимает остаток ширины.</summary>
    class UiList : ListView
    {
        public UiList()
        {
            View = View.Details;
            FullRowSelect = true; MultiSelect = false; HideSelection = false; GridLines = false;
            BorderStyle = BorderStyle.None;
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
            OwnerDraw = true;
            DoubleBuffered = true;
            BackColor = Theme.Card; ForeColor = Theme.Text; Font = Theme.UI;
            ImageList rows = new ImageList();
            rows.ImageSize = new Size(1, 36);   // высота строки
            SmallImageList = rows;
            Dark.Scroll(this);
        }

        void FitLast()
        {
            if (Columns.Count == 0 || ClientSize.Width <= 0) return;
            int w = 0;
            for (int i = 0; i < Columns.Count - 1; i++) w += Columns[i].Width;
            int last = Math.Max(120, ClientSize.Width - w);
            if (Columns[Columns.Count - 1].Width != last) Columns[Columns.Count - 1].Width = last;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); FitLast(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); FitLast(); }

        protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Theme.Card2)) g.FillRectangle(b, e.Bounds);
            using (Pen p = new Pen(Theme.Line)) g.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            TextRenderer.DrawText(g, e.Header.Text, Theme.Small, new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 14, e.Bounds.Height), Theme.Muted, Theme.TF);
        }

        protected override void OnDrawItem(DrawListViewItemEventArgs e) { }

        protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
        {
            Graphics g = e.Graphics;
            bool sel = e.Item.Selected;
            Rectangle r = e.Bounds;
            using (SolidBrush b = new SolidBrush(sel ? Theme.AccentSoft : Theme.Card)) g.FillRectangle(b, r);
            using (Pen p = new Pen(Theme.Mix(Theme.Card, Theme.Line, 0.7))) g.DrawLine(p, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
            if (sel && e.ColumnIndex == 0)
                using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, r.X, r.Y + 6, 3, r.Height - 13);
            Color fg = sel ? Color.White : e.Item.ForeColor;
            TextRenderer.DrawText(g, e.SubItem.Text, Font, new Rectangle(r.X + 12, r.Y, Math.Max(4, r.Width - 16), r.Height - 1), fg, Theme.TF);
        }
    }

    /// <summary>Вкладки без полосы заголовков: шаги переключаются кодом (боковым меню), а проверки перехода остаются в событии Selecting.</summary>
    class StepTabs : TabControl
    {
        public StepTabs()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            ItemSize = new Size(1, 1);
            SizeMode = TabSizeMode.Fixed;
            TabStop = false;
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x1328 && !DesignMode) { m.Result = (IntPtr)1; return; }   // TCM_ADJUSTRECT: страница занимает весь контрол
            base.WndProc(ref m);
        }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Theme.Bg); }
    }

    /// <summary>Верхняя панель: логотип и название слева, вкладки режимов, справа сведения о ПК.</summary>
    class UiHeader : DPanel
    {
        Bitmap logo = Icons.Make("app", 30);
        public string Info = "";

        public UiHeader() { BackColor = Theme.Side; }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Side);
            Theme.Smooth(g);
            using (Pen p = new Pen(Theme.Line)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            g.DrawImage(logo, 20, (Height - 30) / 2, 30, 30);
            TextRenderer.DrawText(g, "Buba-Helper", Theme.Big, new Rectangle(58, 0, 160, Height), Theme.Text, Theme.TF);
            if (Info.Length > 0)
            {
                int w = TextRenderer.MeasureText(Info, Theme.Small).Width + 22;
                RectangleF chip = new RectangleF(Width - w - 20, (Height - 26) / 2f, w, 26);
                int tabsRight = 0;
                foreach (Control c in Controls) tabsRight = Math.Max(tabsRight, c.Right);
                if (chip.Left < tabsRight + 8) return;     // узкое окно: плашка не должна налезать на вкладки режимов
                Theme.FillRound(g, chip, 13, Theme.Card);
                Theme.StrokeRound(g, chip, 13, Theme.Line, 1f);
                TextRenderer.DrawText(g, Info, Theme.Small, Rectangle.Round(chip), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}
