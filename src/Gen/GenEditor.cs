// Редактор файла ответов (вкладка «Файл ответов» программы): левое меню страниц и страницы настроек.
// Здесь каркас: строки формы, карточки, страницы, привязка полей к состоянию, пересчёт XML.
// Сами страницы - в GenEditor.Pages.cs, списки (пользователи, команды…) и результат - в GenEditor.Lists.cs.
// Раскладка адаптивная: карточки тянутся по ширине окна (до разумного предела), строки пересчитывают размеры через GRow.Fit.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace WinInstaller
{
    /// <summary>Элемент списка выбора: значение для состояния и подпись для человека.</summary>
    class Opt
    {
        public string Value, Text;
        public Opt(string v, string t) { Value = v; Text = t; }
        public override string ToString() { return Text; }
    }

    /// <summary>Привязка поля к состоянию: объект (по пути или элемент списка) и ключ.</summary>
    class Bind
    {
        public Func<JObj> T;
        public string Key, Reg;
    }

    /// <summary>Одна строка формы: подпись, поле и подсказка в общем контейнере.</summary>
    class GRow
    {
        public Panel Host;
        public bool Half, Filtered, WasVisible = true;
        public Func<bool> VisibleIf;
        public Action Load;
        public object Tag;
        public string Search = "";
        public ComboBox Combo;
        public Func<int, int> Fit;      // раскладывает содержимое под ширину w и возвращает высоту строки
        public int FitW = -1;           // ширина, под которую Fit выполнялся в последний раз
        public bool Shown { get { return !Filtered && (VisibleIf == null || VisibleIf()); } }
    }

    /// <summary>Контекст для добавления строк: куда класть и какой ширины.</summary>
    class RowCtx
    {
        public Control Parent;
        public List<GRow> Rows = new List<GRow>();
        public int InnerW;
        public bool Track = true;   // строки попадают в общий список для загрузки состояния в поля
    }

    class GCard : UiCard
    {
        public RowCtx Ctx = new RowCtx();
        public Func<bool> VisibleIf;
        public bool Shown { get { return VisibleIf == null || VisibleIf(); } }
        public bool AnyRow() { foreach (GRow r in Ctx.Rows) if (r.Shown) return true; return false; }
        public void DoLayout()
        {
            Ctx.InnerW = Width - 2 * Pad;
            Height = GenEditor.LayoutRows(Ctx.Rows, Ctx.InnerW, Pad, BodyTop) + 18;
        }
    }

    class GPage : DPanel
    {
        public string Id, Title, Icon;
        public List<GCard> Cards = new List<GCard>();
        public const int PageMargin = 28, MaxW = 940;
        readonly Label head = new Label();
        bool busy;

        public GPage()
        {
            Ui.VerticalOnly(this);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            head.Font = Theme.H1; head.ForeColor = Theme.Text; head.AutoSize = false; head.Visible = false;
            Controls.Add(head);
            Dark.Scroll(this);
            SizeChanged += delegate { if (Visible) Relayout(); };
        }

        public void Relayout()
        {
            if (Cards.Count == 0 || busy) return;
            busy = true;
            try
            {
                // ширину считаем от внешней ширины страницы и заранее учитываем вертикальную полосу прокрутки:
                // так карточки никогда не выходят за видимую область и горизонтальная полоса не появляется
                bool vs = VerticalScroll.Visible;
                int total = Place(vs);
                bool need = total > Height;
                if (need != vs) Place(need);
            }
            finally { busy = false; }
        }

        /// <summary>Расставляет карточки и возвращает высоту содержимого.</summary>
        int Place(bool vscroll)
        {
            int oy = AutoScrollPosition.Y;   // <= 0: текущее смещение прокрутки; позиции задаём с его учётом, поэтому страница не «прыгает» наверх
            int avail = Math.Max(320, Width - (vscroll ? SystemInformation.VerticalScrollBarWidth : 0) - 2 * PageMargin);
            int cw = Math.Min(avail, MaxW);
            int left = PageMargin + (avail - cw) / 2;
            head.Text = Title;
            head.SetBounds(left, 22 + oy, cw, 38);
            head.Visible = true;
            int y = 76;
            foreach (GCard c in Cards)
            {
                bool show = c.Shown && c.AnyRow();
                c.Visible = show;
                if (!show) continue;
                c.Width = cw;
                c.DoLayout();
                c.Left = left; c.Top = y + oy;
                y += c.Height + 14;
            }
            AutoScrollMinSize = new Size(0, y + 10);
            return y + 10;
        }
    }

    partial class GenEditor : Panel
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public const int NavW = 232;
        public JObj St = GenState.Defaults();
        public event Action IndexChanged, Regenerated;
        public event Action<string, bool> UseXml;     // путь к сохранённому XML, это файл для Sysprep?
        public event Action<string, bool, string> UseIso;   // то же для вкладки «XML в ISO/WIM»: XML, это файл для Sysprep?, путь к Start-Sysprep.cmd (или null)
        public string StatePath;

        bool loading;
        readonly NavList nav = new NavList();
        readonly List<GPage> pages = new List<GPage>();
        readonly List<GRow> allRows = new List<GRow>();
        readonly Dictionary<string, Control> targets = new Dictionary<string, Control>();
        readonly List<Action> refreshers = new List<Action>();
        readonly Timer regen = new Timer();
        Font hintFont = Theme.Small, boldFont = Theme.Semi;
        GenResult last;

        public int Index { get { return nav.SelectedIndex; } }
        public int Count { get { return pages.Count; } }
        public int WarningCount { get { return last == null ? 0 : last.Warnings.Count; } }

        public GenEditor()
        {
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.UI;
            nav.Dock = DockStyle.Left;
            nav.Width = NavW;
            Controls.Add(nav);
            regen.Interval = 350;
            regen.Tick += delegate { regen.Stop(); Regenerate(); };
            BuildPages();
            foreach (GPage p in pages) nav.Add(p.Title, p.Icon);
            nav.SelectedIndexChanged += delegate { ShowPage(nav.SelectedIndex); };
            LoadAll();
            nav.SelectedIndex = 0;
        }

        // ------------------------------------------------------------ страницы и навигация

        GPage AddPage(string id, string title, string icon)
        {
            GPage p = new GPage();
            p.Id = id; p.Title = title; p.Icon = icon;
            p.Dock = DockStyle.Fill;
            p.Visible = false;
            Controls.Add(p);
            p.BringToFront();   // заполняющая страница «докуется» после бокового меню
            pages.Add(p);
            return p;
        }

        GCard AddCard(GPage p, string title, Func<bool> vis)
        {
            GCard c = new GCard();
            c.Text = title; c.VisibleIf = vis;
            c.Width = 700;
            c.Ctx.Parent = c; c.Ctx.InnerW = c.Width - 2 * c.Pad;
            p.Controls.Add(c);
            p.Cards.Add(c);
            return c;
        }

        void ShowPage(int i)
        {
            for (int k = 0; k < pages.Count; k++) pages[k].Visible = k == i;
            if (i >= 0 && i < pages.Count)
            {
                pages[i].Relayout();
                if (pages[i].Id == "result") ShowResult();
            }
            if (IndexChanged != null) IndexChanged();
        }

        public void Go(int i) { if (i >= 0 && i < pages.Count) nav.SelectedIndex = i; }
        public void Next() { Go(Index + 1); }
        public void Prev() { Go(Index - 1); }

        /// <summary>Открывает страницу и фокусирует поле (переход по предупреждению).</summary>
        public void GoTo(string pageId, string key)
        {
            for (int i = 0; i < pages.Count; i++) if (pages[i].Id == pageId) { Go(i); break; }
            Control c;
            if (key != null && targets.TryGetValue(key, out c) && c.Visible)
            {
                GPage p = pages[Index];
                try { p.ScrollControlIntoView(c); } catch { }
                c.Focus();
            }
        }

        // ------------------------------------------------------------ раскладка строк

        public static int LayoutRows(List<GRow> rows, int innerW, int x0, int y0)
        {
            int y = y0, halfW = (innerW - 14) / 2, gap = 10;
            GRow left = null;
            foreach (GRow r in rows)
            {
                bool show = r.Shown;
                if (show && !r.WasVisible && r.Load != null) r.Load();
                r.WasVisible = show;
                if (r.Host.Visible != show) r.Host.Visible = show;
                if (!show) continue;
                int w = r.Half ? halfW : innerW;
                if (r.Host.Width != w) r.Host.Width = w;
                if (r.Fit != null && r.FitW != w) { r.FitW = w; r.Host.Height = r.Fit(w); }
                if (r.Half)
                {
                    if (left == null) { r.Host.Left = x0; r.Host.Top = y; left = r; }
                    else
                    {
                        r.Host.Left = x0 + halfW + 14; r.Host.Top = y;
                        y += Math.Max(left.Host.Height, r.Host.Height) + gap;
                        left = null;
                    }
                }
                else
                {
                    if (left != null) { y += left.Host.Height + gap; left = null; }
                    r.Host.Left = x0; r.Host.Top = y;
                    y += r.Host.Height + gap;
                }
            }
            if (left != null) y += left.Host.Height + gap;
            return y;
        }

        void RelayoutAll()
        {
            RelayoutLists();
            GPage cur = Index >= 0 && Index < pages.Count ? pages[Index] : null;
            if (cur != null) cur.Relayout();
        }

        // ------------------------------------------------------------ состояние

        Func<JObj> T(string objPath)
        {
            return delegate()
            {
                JObj o = St;
                if (objPath.Length == 0) return o;
                foreach (string k in objPath.Split('.')) o = o.O(k);
                return o;
            };
        }

        /// <summary>Привязка по пути вида «install.diskId».</summary>
        Bind B(string path)
        {
            Bind b = new Bind();
            int i = path.LastIndexOf('.');
            b.T = T(i < 0 ? "" : path.Substring(0, i));
            b.Key = i < 0 ? path : path.Substring(i + 1);
            b.Reg = path;
            return b;
        }

        /// <summary>Привязка к полю элемента списка (пользователь, команда…).</summary>
        Bind BI(JObj item, string key)
        {
            Bind b = new Bind();
            b.T = delegate() { return item; };
            b.Key = key;
            return b;
        }

        Bind BMap(string map, string key, string reg)
        {
            Bind b = new Bind();
            b.T = T(map);
            b.Key = key; b.Reg = reg;
            return b;
        }

        string Os { get { return St.S("os"); } }
        bool IsClean { get { return St.S("mode") == "clean"; } }
        bool IsSysprep { get { return St.S("mode") == "sysprep"; } }

        /// <summary>Изменили поле: пересчитать видимость, зависимые списки и (с задержкой) XML.</summary>
        void Changed()
        {
            if (loading) return;
            foreach (Action a in refreshers) a();
            RelayoutAll();
            regen.Stop(); regen.Start();
        }

        /// <summary>Заполняет все поля из состояния (после загрузки настроек, пресета, сброса).</summary>
        public void LoadAll()
        {
            loading = true;
            try
            {
                foreach (Action a in refreshers) a();
                foreach (GRow r in allRows) if (r.Load != null) r.Load();
                RebuildLists();
            }
            finally { loading = false; }
            RelayoutAll();
            Regenerate();
        }

        public void ResetState() { St = GenState.Defaults(); LoadAll(); }

        /// <summary>Загружает настройки из JSON; проблемы проверки возвращает списком.</summary>
        public List<string> Import(string json)
        {
            List<string> P = new List<string>();
            St = GenState.Sanitize(Json.Parse(json), P);
            LoadAll();
            return P;
        }

        public string Export()
        {
            JObj o = new JObj();
            o["_v"] = 2; o["_app"] = Cat.Version;
            foreach (string k in St.Keys) o[k] = St[k];
            return Json.Stringify(o);
        }

        public void LoadFromFile(string path)
        {
            try { if (File.Exists(path)) { St = GenState.Sanitize(Json.Parse(File.ReadAllText(path, Encoding.UTF8)), new List<string>()); LoadAll(); } }
            catch { }
        }

        void SaveState()
        {
            if (StatePath == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatePath));
                File.WriteAllText(StatePath, Export(), new UTF8Encoding(false));
            }
            catch { }
        }

        void Regenerate()
        {
            last = Generator.Generate(St);
            int n = last.Warnings.Count;
            int ri = pages.Count - 1;
            nav.SetBadge(ri, n, Theme.Warn);
            if (Index == ri) ShowResult();
            SaveState();
            if (Regenerated != null) Regenerated();
        }

        // ------------------------------------------------------------ строки формы

        GRow NewRow(RowCtx x, bool half, Func<bool> vis)
        {
            GRow r = new GRow();
            r.Host = new DPanel();
            r.Host.Width = Math.Max(100, half ? (x.InnerW - 14) / 2 : x.InnerW);
            r.Half = half; r.VisibleIf = vis;
            x.Parent.Controls.Add(r.Host);
            x.Rows.Add(r);
            if (x.Track) allRows.Add(r);
            return r;
        }

        static int TextH(string text, Font f, int w)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return TextRenderer.MeasureText(text, f, new Size(Math.Max(20, w), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
        }

        Label MakeLabel(string text, Font f, Color color, int x, int y, int w)
        {
            Label l = new Label();
            l.AutoSize = false;
            l.Text = text; l.Font = f; l.ForeColor = color;
            l.SetBounds(x, y, w, TextH(text, f, w) + 2);
            return l;
        }

        /// <summary>Подгоняет высоту подписи под ширину w.</summary>
        static void Wrap(Label l, int w)
        {
            l.Width = w;
            l.Height = TextH(l.Text, l.Font, w) + 2;
        }

        void Reg(string key, Control c) { if (!string.IsNullOrEmpty(key)) targets[key] = c; }

        /// <summary>Подсказка под полем (добавляется в строку; null, если подсказки нет).</summary>
        Label MakeHint(GRow r, string hint)
        {
            if (string.IsNullOrEmpty(hint)) return null;
            Label h = MakeLabel(hint, hintFont, Theme.Muted, 0, 0, 100);
            r.Host.Controls.Add(h);
            return h;
        }

        /// <summary>Ставит подсказку под y (если она есть) и возвращает новую нижнюю границу.</summary>
        static int PlaceHint(Label h, int y, int x, int w)
        {
            if (h == null) return y;
            Wrap(h, w);
            h.Left = x; h.Top = y + 4;
            return h.Bottom;
        }

        GRow Txt(RowCtx x, Bind b, string label, string hint = null, bool half = false, Func<bool> vis = null, string ph = null, int lines = 1)
        {
            GRow r = NewRow(x, half, vis);
            Label l = MakeLabel(label, Font, Theme.Text, 0, 0, 100);
            TextBox tb = new TextBox();
            if (lines > 1) { tb.Multiline = true; tb.ScrollBars = ScrollBars.Vertical; tb.AcceptsReturn = true; tb.Font = new Font("Consolas", 9.5f); }
            FieldBox fb = FieldBox.Wrap(tb);
            if (ph != null) tb.HandleCreated += delegate { SendMessage(tb.Handle, 0x1501, new IntPtr(1), ph); };   // EM_SETCUEBANNER
            r.Host.Controls.Add(l); r.Host.Controls.Add(fb);
            Label hl = MakeHint(r, hint);
            int fh = lines > 1 ? lines * 17 + 20 : 32;
            r.Fit = delegate(int w)
            {
                Wrap(l, w);
                fb.SetBounds(0, l.Bottom + 5, w, fh);
                return PlaceHint(hl, fb.Bottom, 0, w);
            };
            r.Search = label + " " + hint;
            r.Load = delegate
            {
                string v = b.T().S(b.Key);
                if (lines > 1) v = Regex.Replace(v, "\r?\n", "\r\n");
                if (tb.Text != v) tb.Text = v;
            };
            tb.TextChanged += delegate
            {
                if (loading) return;
                b.T()[b.Key] = lines > 1 ? tb.Text.Replace("\r\n", "\n") : tb.Text;
                Changed();
            };
            Reg(b.Reg, tb);
            return r;
        }

        GRow Num(RowCtx x, Bind b, string label, int min, int max, string hint = null, bool half = false, Func<bool> vis = null)
        {
            GRow r = NewRow(x, half, vis);
            Label l = MakeLabel(label, Font, Theme.Text, 0, 0, 100);
            NumericUpDown nud = new NumericUpDown();
            nud.Minimum = min; nud.Maximum = max;
            FieldBox fb = FieldBox.Wrap(nud);
            r.Host.Controls.Add(l); r.Host.Controls.Add(fb);
            Label hl = MakeHint(r, hint);
            r.Fit = delegate(int w)
            {
                Wrap(l, w);
                fb.SetBounds(0, l.Bottom + 5, Math.Min(w, 180), 32);
                return PlaceHint(hl, fb.Bottom, 0, w);
            };
            r.Search = label + " " + hint;
            r.Load = delegate
            {
                int v = Math.Max(min, Math.Min(max, b.T().I(b.Key)));
                if ((int)nud.Value != v) nud.Value = v;
            };
            nud.ValueChanged += delegate
            {
                if (loading) return;
                b.T()[b.Key] = (int)nud.Value;
                Changed();
            };
            Reg(b.Reg, nud);
            return r;
        }

        GRow Sel(RowCtx x, Bind b, string label, List<Opt> opts, string hint = null, bool half = false, Func<bool> vis = null)
        {
            GRow r = NewRow(x, half, vis);
            Label l = MakeLabel(label, Font, Theme.Text, 0, 0, 100);
            UiCombo cb = new UiCombo();
            foreach (Opt o in opts) cb.Items.Add(o);
            r.Host.Controls.Add(l); r.Host.Controls.Add(cb);
            Label hl = MakeHint(r, hint);
            r.Fit = delegate(int w)
            {
                Wrap(l, w);
                cb.SetBounds(0, l.Bottom + 5, w, cb.Height);
                return PlaceHint(hl, cb.Bottom, 0, w);
            };
            r.Search = label + " " + hint;
            r.Combo = cb;
            r.Load = delegate
            {
                string v = b.T().S(b.Key);
                for (int i = 0; i < cb.Items.Count; i++) if (((Opt)cb.Items[i]).Value == v) { if (cb.SelectedIndex != i) cb.SelectedIndex = i; return; }
                cb.SelectedIndex = -1;
            };
            cb.SelectedIndexChanged += delegate
            {
                if (loading) return;
                Opt o = cb.SelectedItem as Opt;
                if (o == null) return;
                b.T()[b.Key] = o.Value;
                Changed();
            };
            Reg(b.Reg, cb);
            return r;
        }

        static List<Opt> Opts(params string[] vt)
        {
            List<Opt> l = new List<Opt>();
            for (int i = 0; i + 1 < vt.Length; i += 2) l.Add(new Opt(vt[i], vt[i + 1]));
            return l;
        }

        static List<Opt> OptsOf(List<string[]> pairs)
        {
            List<Opt> l = new List<Opt>();
            foreach (string[] p in pairs) l.Add(new Opt(p[0], p[1]));
            return l;
        }

        GRow Chk(RowCtx x, Bind b, string label, string hint = null, bool half = false, Func<bool> vis = null, string warn = null, string tags = null, string search = null)
        {
            GRow r = NewRow(x, half, vis);
            string text = label + (tags ?? "");
            UiCheck cb = new UiCheck();
            cb.AutoSize = false;
            cb.Text = text;
            r.Host.Controls.Add(cb);
            Label hl = MakeHint(r, hint);
            Label wl = null;
            if (!string.IsNullOrEmpty(warn))
            {
                wl = MakeLabel("⚠ " + warn, hintFont, Theme.Warn, 28, 0, 100);
                r.Host.Controls.Add(wl);
            }
            r.Fit = delegate(int w)
            {
                cb.SetBounds(0, 0, w, Math.Max(24, TextH(text, Font, w - 28) + 6));
                int y = PlaceHint(hl, cb.Bottom - 2, 28, w - 28);
                if (wl != null) { Wrap(wl, w - 28); wl.Left = 28; wl.Top = y + 3; y = wl.Bottom; }
                return y;
            };
            r.Search = (search ?? label) + " " + hint + " " + warn;
            r.Load = delegate { bool v = b.T().B(b.Key); if (cb.Checked != v) cb.Checked = v; };
            cb.CheckedChanged += delegate
            {
                if (loading) return;
                b.T()[b.Key] = cb.Checked;
                Changed();
            };
            Reg(b.Reg, cb);
            return r;
        }

        GRow Note(RowCtx x, string text, Func<bool> vis = null)
        {
            GRow r = NewRow(x, false, vis);
            Label l = MakeLabel(text, hintFont, Theme.Muted, 0, 0, 100);
            r.Host.Controls.Add(l);
            r.Fit = delegate(int w) { Wrap(l, w); return l.Height; };
            return r;
        }

        GRow Alert(RowCtx x, string text, Func<bool> vis = null)
        {
            GRow r = NewRow(x, false, vis);
            Color bg = Theme.C(45, 37, 20);
            DPanel box = new DPanel();
            box.BackColor = bg;
            box.Paint += delegate(object s, PaintEventArgs e)
            {
                Theme.Smooth(e.Graphics);
                using (SolidBrush br = new SolidBrush(Theme.Warn)) e.Graphics.FillRectangle(br, 0, 0, 3, box.Height);
            };
            Label l = MakeLabel(text, hintFont, Theme.C(238, 205, 140), 16, 10, 100);
            l.BackColor = bg;
            box.Controls.Add(l);
            r.Host.Controls.Add(box);
            r.Fit = delegate(int w)
            {
                Wrap(l, w - 32); l.Left = 16; l.Top = 10;
                box.SetBounds(0, 0, w, l.Height + 20);
                return box.Height;
            };
            return r;
        }

        GRow Sub(RowCtx x, string text, Func<bool> vis = null)
        {
            GRow r = NewRow(x, false, vis);
            Label l = MakeLabel(text, boldFont, Theme.AccentHi, 0, 6, 100);
            r.Host.Controls.Add(l);
            r.Fit = delegate(int w) { Wrap(l, w); l.Top = 6; return l.Height + 8; };
            return r;
        }

        GRow Buttons(RowCtx x, string[] caps, Action[] acts, Func<bool> vis = null, int bw = 120)
        {
            GRow r = NewRow(x, false, vis);
            for (int i = 0; i < caps.Length; i++)
            {
                Action act = acts[i];
                UiButton bt = new UiButton();
                bt.Text = caps[i];
                bt.SetBounds(i * (bw + 8), 0, bw, 34);
                bt.Click += delegate { act(); };
                r.Host.Controls.Add(bt);
            }
            r.Host.Height = 34;
            return r;
        }
    }
}
