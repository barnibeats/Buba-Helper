// Редактор файла ответов (вкладка «Файл ответов» программы): левое меню шагов и страницы настроек.
// Здесь каркас: строки формы, карточки, страницы, привязка полей к состоянию, пересчёт XML.
// Сами страницы - в GenEditor.Pages.cs, списки (пользователи, команды…) и результат - в GenEditor.Lists.cs.
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
        public bool Shown { get { return !Filtered && (VisibleIf == null || VisibleIf()); } }
    }

    /// <summary>Контекст для добавления строк: куда класть и какой ширины.</summary>
    class RowCtx
    {
        public Control Parent;
        public List<GRow> Rows = new List<GRow>();
        public int InnerW;
        public bool Track = true;   // строки попадают в общий список для загрузки состояния в поля
        public int RowW(bool half) { return half ? (InnerW - 12) / 2 : InnerW; }
    }

    class GCard : AeroGroupBox
    {
        public RowCtx Ctx = new RowCtx();
        public Func<bool> VisibleIf;
        public bool Shown { get { return VisibleIf == null || VisibleIf(); } }
        public bool AnyRow() { foreach (GRow r in Ctx.Rows) if (r.Shown) return true; return false; }
        public void DoLayout() { Height = GenEditor.LayoutRows(Ctx.Rows, Ctx.InnerW, 12, 24) + 4; }
    }

    class GPage : Panel
    {
        public string Id, Title, Icon;
        public List<GCard> Cards = new List<GCard>();

        public GPage()
        {
            AutoScroll = true;
            BackColor = Color.White;
        }
        public void Relayout()
        {
            if (Cards.Count == 0) return;
            int y = 8;
            foreach (GCard c in Cards)
            {
                bool show = c.Shown && c.AnyRow();
                c.Visible = show;
                if (!show) continue;
                c.DoLayout();
                c.Left = 8; c.Top = y;
                y += c.Height + 8;
            }
            AutoScrollMinSize = new Size(0, y);
        }
    }

    partial class GenEditor : Panel
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);

        public const int NavW = 196, CardW = 664, InnerW = 640;
        public JObj St = GenState.Defaults();
        public event Action IndexChanged, Regenerated;
        public event Action<string, bool> UseXml;     // путь к сохранённому XML, это файл для Sysprep?
        public string StatePath;

        bool loading;
        readonly AeroNav nav = new AeroNav();
        readonly List<GPage> pages = new List<GPage>();
        readonly List<GRow> allRows = new List<GRow>();
        readonly Dictionary<string, Control> targets = new Dictionary<string, Control>();
        readonly List<Action> refreshers = new List<Action>();
        readonly Timer regen = new Timer();
        Font hintFont, boldFont;
        GenResult last;

        public int Index { get { return nav.SelectedIndex; } }
        public int Count { get { return pages.Count; } }
        public int WarningCount { get { return last == null ? 0 : last.Warnings.Count; } }

        public GenEditor()
        {
            BackColor = Color.White;
            hintFont = new Font("Segoe UI", 8.25f);
            boldFont = new Font("Segoe UI", 9f, FontStyle.Bold);
            nav.Dock = DockStyle.Left;
            nav.Width = NavW;
            Controls.Add(nav);
            regen.Interval = 350;
            regen.Tick += delegate { regen.Stop(); Regenerate(); };
            BuildPages();
            nav.Icons = Icons.List(IconNames());
            foreach (GPage p in pages) nav.Items.Add(p.Title);
            nav.SelectedIndexChanged += delegate { ShowPage(nav.SelectedIndex); };
            LoadAll();
            nav.SelectedIndex = 0;
        }

        string[] IconNames()
        {
            List<string> n = new List<string>();
            foreach (GPage p in pages) n.Add(p.Icon);
            return n.ToArray();
        }

        // ------------------------------------------------------------ страницы и навигация

        GPage AddPage(string id, string title, string icon)
        {
            GPage p = new GPage();
            p.Id = id; p.Title = title; p.Icon = icon;
            p.Dock = DockStyle.Fill;
            p.Visible = false;
            Controls.Add(p);
            p.BringToFront();
            pages.Add(p);
            return p;
        }

        GCard AddCard(GPage p, string title, Func<bool> vis)
        {
            GCard c = new GCard();
            c.Text = title; c.VisibleIf = vis;
            c.Width = CardW;
            c.BackColor = Color.White;
            c.Ctx.Parent = c; c.Ctx.InnerW = InnerW;
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
            int y = y0, halfW = (innerW - 12) / 2;
            GRow left = null;
            foreach (GRow r in rows)
            {
                bool show = r.Shown;
                if (show && !r.WasVisible && r.Load != null) r.Load();
                r.WasVisible = show;
                if (r.Host.Visible != show) r.Host.Visible = show;
                if (!show) continue;
                if (r.Half)
                {
                    if (left == null) { r.Host.Left = x0; r.Host.Top = y; left = r; }
                    else
                    {
                        r.Host.Left = x0 + halfW + 12; r.Host.Top = y;
                        y += Math.Max(left.Host.Height, r.Host.Height) + 6;
                        left = null;
                    }
                }
                else
                {
                    if (left != null) { y += left.Host.Height + 6; left = null; }
                    r.Host.Left = x0; r.Host.Top = y;
                    y += r.Host.Height + 6;
                }
            }
            if (left != null) y += left.Host.Height + 6;
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
            string title = pages[ri].Title + (n > 0 ? " (" + n + ")" : "");
            if (ri < nav.Items.Count && Convert.ToString(nav.Items[ri]) != title)
            {
                int sel = nav.SelectedIndex;
                nav.Items[ri] = title;
                if (nav.SelectedIndex != sel) nav.SelectedIndex = sel;
            }
            if (Index == ri) ShowResult();
            SaveState();
            if (Regenerated != null) Regenerated();
        }

        // ------------------------------------------------------------ строки формы

        GRow NewRow(RowCtx x, bool half, Func<bool> vis)
        {
            GRow r = new GRow();
            r.Host = new Panel();
            r.Host.BackColor = Color.White;
            r.Host.Width = x.RowW(half);
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
            l.Text = text; l.Font = f; l.ForeColor = color; l.BackColor = Color.White;
            l.SetBounds(x, y, w, TextH(text, f, w) + 2);
            return l;
        }

        void Reg(string key, Control c) { if (!string.IsNullOrEmpty(key)) targets[key] = c; }

        /// <summary>Добавляет подсказку под полем и возвращает новую высоту строки.</summary>
        int AddHint(GRow r, string hint, int y, int x, int w)
        {
            if (string.IsNullOrEmpty(hint)) return y;
            Label h = MakeLabel(hint, hintFont, Color.DimGray, x, y + 2, w);
            r.Host.Controls.Add(h);
            return h.Bottom;
        }

        GRow Txt(RowCtx x, Bind b, string label, string hint = null, bool half = false, Func<bool> vis = null, string ph = null, int lines = 1)
        {
            GRow r = NewRow(x, half, vis);
            int w = x.RowW(half);
            Label l = MakeLabel(label, Font, Color.Black, 0, 0, w);
            TextBox tb = new TextBox();
            tb.SetBounds(0, l.Bottom + 1, w, lines > 1 ? lines * 16 + 8 : 23);
            if (lines > 1) { tb.Multiline = true; tb.ScrollBars = ScrollBars.Vertical; tb.AcceptsReturn = true; tb.Font = new Font("Consolas", 9f); }
            if (ph != null) tb.HandleCreated += delegate { SendMessage(tb.Handle, 0x1501, new IntPtr(1), ph); };   // EM_SETCUEBANNER
            r.Host.Controls.Add(l); r.Host.Controls.Add(tb);
            r.Host.Height = AddHint(r, hint, tb.Bottom, 0, w);
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
            int w = x.RowW(half);
            Label l = MakeLabel(label, Font, Color.Black, 0, 0, w);
            NumericUpDown nud = new NumericUpDown();
            nud.Minimum = min; nud.Maximum = max;
            nud.SetBounds(0, l.Bottom + 1, Math.Min(w, 140), 23);
            r.Host.Controls.Add(l); r.Host.Controls.Add(nud);
            r.Host.Height = AddHint(r, hint, nud.Bottom, 0, w);
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
            int w = x.RowW(half);
            Label l = MakeLabel(label, Font, Color.Black, 0, 0, w);
            ComboBox cb = new ComboBox();
            cb.DropDownStyle = ComboBoxStyle.DropDownList;
            cb.SetBounds(0, l.Bottom + 1, w, 24);
            foreach (Opt o in opts) cb.Items.Add(o);
            r.Host.Controls.Add(l); r.Host.Controls.Add(cb);
            r.Host.Height = AddHint(r, hint, cb.Bottom, 0, w);
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
            int w = x.RowW(half);
            string text = label + (tags ?? "");
            CheckBox cb = new CheckBox();
            cb.AutoSize = false;
            cb.Text = text;
            cb.BackColor = Color.White;
            cb.SetBounds(0, 0, w, TextH(text, Font, w - 22) + 6);
            r.Host.Controls.Add(cb);
            int y = cb.Bottom;
            y = AddHint(r, hint, y - 2, 20, w - 20);
            if (!string.IsNullOrEmpty(warn))
            {
                Label wl = MakeLabel("⚠ " + warn, hintFont, Color.Firebrick, 20, y + 2, w - 20);
                r.Host.Controls.Add(wl);
                y = wl.Bottom;
            }
            r.Host.Height = y;
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
            Label l = MakeLabel(text, hintFont, Color.DimGray, 0, 0, x.RowW(false));
            r.Host.Controls.Add(l);
            r.Host.Height = l.Height;
            return r;
        }

        GRow Alert(RowCtx x, string text, Func<bool> vis = null)
        {
            GRow r = NewRow(x, false, vis);
            int w = x.RowW(false);
            Label l = MakeLabel(text, hintFont, Color.FromArgb(120, 40, 20), 6, 5, w - 12);
            l.BackColor = Color.FromArgb(255, 244, 229);
            Panel box = new Panel();
            box.BackColor = Color.FromArgb(255, 244, 229);
            box.SetBounds(0, 0, w, l.Height + 10);
            box.Controls.Add(l);
            r.Host.Controls.Add(box);
            r.Host.Height = box.Height;
            return r;
        }

        GRow Sub(RowCtx x, string text, Func<bool> vis = null)
        {
            GRow r = NewRow(x, false, vis);
            Label l = MakeLabel(text, boldFont, Theme.Ink, 0, 2, x.RowW(false));
            r.Host.Controls.Add(l);
            r.Host.Height = l.Height + 4;
            return r;
        }

        GRow Buttons(RowCtx x, string[] caps, Action[] acts, Func<bool> vis = null, int bw = 120)
        {
            GRow r = NewRow(x, false, vis);
            for (int i = 0; i < caps.Length; i++)
            {
                Action act = acts[i];
                AeroButton bt = new AeroButton();
                bt.Text = caps[i];
                bt.SetBounds(i * (bw + 8), 0, bw, 28);
                bt.Click += delegate { act(); };
                r.Host.Controls.Add(bt);
            }
            r.Host.Height = 28;
            return r;
        }
    }
}
