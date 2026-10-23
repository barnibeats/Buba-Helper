// Редактор файла ответов: списки (пользователи, записи реестра, команды, задачи) и страница «Результат».
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Xml;

namespace WinInstaller
{
    partial class GenEditor
    {
        // ------------------------------------------------------------ списки

        class ItemView
        {
            public UiCard Panel;
            public RowCtx Ctx = new RowCtx();
            public Control First;
            public List<Control> Hdr = new List<Control>();   // кнопки в заголовке элемента, справа налево
        }

        class ListInfo
        {
            public string Key, AddCaption, EmptyText;
            public GRow Holder;
            public List<ItemView> Items = new List<ItemView>();
            public UiButton Add;
            public Label Empty;
            public int W = 600;
        }

        readonly Dictionary<string, ListInfo> lists = new Dictionary<string, ListInfo>();

        static JObj NewItem(string key)
        {
            switch (key)
            {
                case "users": return GenState.DefaultUser();
                case "regs": return GenState.DefaultReg();
                case "cmds": return GenState.DefaultCmd();
                default: return GenState.DefaultTask();
            }
        }

        static Control FirstInput(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is TextBox || c is ComboBox || c is NumericUpDown || c is CheckBox) return c;
                Control f = FirstInput(c);
                if (f != null) return f;
            }
            return null;
        }

        void ListEditor(GCard card, string key, string addCaption, string emptyText)
        {
            ListInfo li = new ListInfo();
            li.Key = key; li.AddCaption = addCaption; li.EmptyText = emptyText;
            GRow row = NewRow(card.Ctx, false, null);
            li.Holder = row;
            li.Add = new UiButton();
            li.Add.Text = addCaption;
            li.Add.Width = 290; li.Add.Height = 34;
            string k = key;
            li.Add.Click += delegate { St.A(k).Add(NewItem(k)); RebuildList(k); Changed(); };
            row.Host.Controls.Add(li.Add);
            li.Empty = MakeLabel(emptyText, hintFont, Theme.Muted, 0, 0, 100);
            row.Host.Controls.Add(li.Empty);
            lists[key] = li;
            Reg(key, li.Add);
            row.Fit = delegate(int w) { li.W = w; return LayoutList(li); };
            LayoutList(li);
        }

        void RebuildLists() { foreach (string k in new List<string>(lists.Keys)) RebuildList(k); }

        /// <summary>Заново строит элементы списка по состоянию (после добавления, удаления, перестановки, загрузки).</summary>
        void RebuildList(string key)
        {
            ListInfo li = lists[key];
            foreach (ItemView iv in li.Items) { li.Holder.Host.Controls.Remove(iv.Panel); iv.Panel.Dispose(); }
            li.Items.Clear();
            List<string> stale = new List<string>();
            foreach (string t in targets.Keys) if (t.StartsWith(key + ":")) stale.Add(t);
            foreach (string t in stale) targets.Remove(t);

            bool prev = loading;
            loading = true;
            try
            {
                List<JObj> arr = St.Objs(key);
                for (int i = 0; i < arr.Count; i++)
                {
                    ItemView iv = BuildItem(li, i, arr[i], arr.Count);
                    li.Holder.Host.Controls.Add(iv.Panel);
                    li.Items.Add(iv);
                    if (iv.First != null) targets[key + ":" + i] = iv.First;
                }
            }
            finally { loading = prev; }
            LayoutList(li);
        }

        /// <summary>Раскладывает элементы списка под ширину li.W и возвращает высоту всего списка.</summary>
        int LayoutList(ListInfo li)
        {
            int y = 0, w = li.W;
            li.Empty.Visible = li.Items.Count == 0 && li.EmptyText.Length > 0;
            if (li.Empty.Visible) { Wrap(li.Empty, w); li.Empty.Left = 0; li.Empty.Top = 0; y = li.Empty.Height + 8; }
            foreach (ItemView iv in li.Items)
            {
                UiCard p = iv.Panel;
                p.Width = w;
                iv.Ctx.InnerW = w - 2 * p.Pad;
                p.Height = LayoutRows(iv.Ctx.Rows, iv.Ctx.InnerW, p.Pad, p.BodyTop) + 8;
                p.Left = 0; p.Top = y;
                int bx = w - p.Pad;
                foreach (Control b in iv.Hdr) { b.Left = bx - b.Width; b.Top = 10; bx -= b.Width + 6; }
                y += p.Height + 10;
            }
            li.Add.Left = 0; li.Add.Top = y;
            li.Holder.Host.Height = y + li.Add.Height;
            return li.Holder.Host.Height;
        }

        void RelayoutLists() { foreach (ListInfo li in lists.Values) LayoutList(li); }

        void MoveItem(string key, int i, int delta)
        {
            List<object> a = St.A(key);
            int j = i + delta;
            if (j < 0 || j >= a.Count) return;
            object t = a[i]; a[i] = a[j]; a[j] = t;
            RebuildList(key);
            Changed();
        }

        ItemView BuildItem(ListInfo li, int index, JObj item, int count)
        {
            string key = li.Key;
            ItemView iv = new ItemView();
            UiCard p = new UiCard();
            p.BackColor = Theme.Card2;
            p.Pad = 14;
            p.Text = (key == "users" ? "Пользователь " : key == "regs" ? "Параметр " : key == "cmds" ? "Команда " : "Задача ") + (index + 1);
            p.Width = 600;
            iv.Panel = p;
            iv.Ctx.Parent = p; iv.Ctx.InnerW = p.Width - 2 * p.Pad; iv.Ctx.Track = false;

            Action<string, int, Action> hb = delegate(string cap, int w, Action act)
            {
                UiButton b = new UiButton();
                b.Text = cap; b.Ghost = true; b.SetBounds(0, 10, w, 28);
                b.Click += delegate { act(); };
                p.Controls.Add(b);
                iv.Hdr.Add(b);
            };
            int idx = index;
            hb("Удалить", 84, delegate { St.A(key).RemoveAt(idx); RebuildList(key); Changed(); });
            if (key != "users")
            {
                hb("Вниз", 64, delegate { MoveItem(key, idx, 1); });
                hb("Вверх", 70, delegate { MoveItem(key, idx, -1); });
            }

            RowCtx x = iv.Ctx;
            switch (key)
            {
                case "users":
                    Txt(x, BI(item, "name"), "Имя (логин)", half: true, ph: "User");
                    Txt(x, BI(item, "display"), "Отображаемое имя", half: true);
                    Txt(x, BI(item, "pass"), "Пароль", half: true, ph: "пусто = без пароля");
                    Sel(x, BI(item, "group"), "Группа", Opts("Administrators", "Администратор", "Users", "Обычный пользователь"), half: true);
                    Txt(x, BI(item, "desc"), "Описание", half: true);
                    Chk(x, BI(item, "neverExp"), "Срок действия пароля не ограничен", half: true);
                    Chk(x, BI(item, "hidden"), "Скрыть с экрана входа", half: true);
                    break;
                case "regs":
                    Txt(x, BI(item, "key"), "Раздел (ключ)", half: true, ph: "HKLM\\SOFTWARE\\…");
                    Txt(x, BI(item, "name"), "Имя параметра", half: true);
                    List<Opt> types = new List<Opt>();
                    foreach (string t in Cat.RegTypes) types.Add(new Opt(t, t));
                    Sel(x, BI(item, "type"), "Тип", types, half: true);
                    Txt(x, BI(item, "data"), "Значение", half: true);
                    break;
                case "cmds":
                    Txt(x, BI(item, "desc"), "Описание", half: true, ph: "Что делает");
                    Sel(x, BI(item, "phase"), "Когда выполнять", Opts("specialize", "Specialize (SYSTEM, до пользователей)", "firstlogon", "FirstLogon (первый вход)"), half: true);
                    Sel(x, BI(item, "kind"), "Тип", Opts("ps", "PowerShell-скрипт", "cmd", "CMD / batch", "exe", "Командная строка (программа с аргументами)"), half: true);
                    Txt(x, BI(item, "body"), "Код PowerShell (можно несколько строк)", lines: 5, vis: delegate() { return item.S("kind") == "ps"; });
                    Txt(x, BI(item, "body"), "Команды CMD (можно несколько строк)", lines: 5, vis: delegate() { return item.S("kind") == "cmd"; });
                    Txt(x, BI(item, "body"), "Командная строка", lines: 2, vis: delegate() { return item.S("kind") == "exe"; }, ph: "msiexec /i D:\\app.msi /qn");
                    break;
                default:   // tasks
                    Func<string> tr = delegate() { return item.S("trigger"); };
                    Txt(x, BI(item, "name"), "Имя задачи", half: true, ph: "MyTask");
                    Txt(x, BI(item, "program"), "Программа", half: true, ph: "C:\\Tools\\app.exe или powershell.exe");
                    Txt(x, BI(item, "args"), "Аргументы", half: true, ph: "-NoProfile -File C:\\Tools\\job.ps1");
                    Txt(x, BI(item, "workdir"), "Рабочая папка", half: true);
                    Sel(x, BI(item, "trigger"), "Запускать", Opts("logon", "При входе в систему", "startup", "При запуске компьютера", "daily", "Ежедневно", "weekly", "По дням недели", "once", "Один раз через N минут", "interval", "Каждые N минут"), half: true);
                    Txt(x, BI(item, "time"), "Время (ЧЧ:ММ)", half: true, ph: "03:00", vis: delegate() { return tr() == "daily" || tr() == "weekly"; });
                    Num(x, BI(item, "n"), "Минут", 1, 1000000, null, true, delegate() { return tr() == "once" || tr() == "interval"; });
                    Sel(x, BI(item, "runAs"), "От имени", Opts("system", "SYSTEM", "users", "Любой пользователь (группа «Пользователи»)", "current", "Пользователь первого входа", "user", "Указанный пользователь + пароль"), half: true);
                    Txt(x, BI(item, "user"), "Пользователь", half: true, vis: delegate() { return item.S("runAs") == "user"; });
                    Txt(x, BI(item, "pass"), "Пароль", half: true, vis: delegate() { return item.S("runAs") == "user"; });
                    DaysRow(x, item, delegate() { return tr() == "weekly"; });
                    Chk(x, BI(item, "highest"), "Наивысшие права", half: true);
                    Chk(x, BI(item, "runNow"), "Сразу запустить после создания", half: true);
                    Txt(x, BI(item, "folder"), "Папка в планировщике (пусто — корень)", half: true, ph: "IT");
                    Num(x, BI(item, "limit"), "Ограничить время выполнения, мин (0 — без ограничения)", 0, 100000, null, true);
                    Chk(x, BI(item, "restart"), "При сбое перезапускать (каждую минуту, до 3 раз)", half: true);
                    Chk(x, BI(item, "ignoreNew"), "Не запускать вторую копию, пока работает прошлая", half: true);
                    Chk(x, BI(item, "needExe"), "Создавать, только если файл программы уже есть", half: true);
                    break;
            }
            foreach (GRow r in x.Rows) if (r.Load != null) r.Load();
            // первое поле элемента - цель для перехода по предупреждению
            iv.First = x.Rows.Count > 0 ? FirstInput(x.Rows[0].Host) : null;
            return iv;
        }

        GRow DaysRow(RowCtx x, JObj item, Func<bool> vis)
        {
            GRow r = NewRow(x, false, vis);
            List<CheckBox> cbs = new List<CheckBox>();
            for (int i = 0; i < Cat.Days.Count; i++)
            {
                UiCheck cb = new UiCheck();
                cb.AutoSize = false;
                cb.Text = Cat.Days[i][1];
                r.Host.Controls.Add(cb);
                cbs.Add(cb);
                cb.CheckedChanged += delegate
                {
                    if (loading) return;
                    List<object> days = new List<object>();
                    for (int k = 0; k < cbs.Count; k++) if (cbs[k].Checked) days.Add(Cat.Days[k][0]);
                    item["days"] = days;
                    Changed();
                };
            }
            r.Fit = delegate(int w)
            {
                int per = Math.Max(1, Math.Min(7, w / 84)), cw = w / per;
                for (int i = 0; i < cbs.Count; i++) cbs[i].SetBounds((i % per) * cw, (i / per) * 28, cw - 4, 24);
                return ((cbs.Count + per - 1) / per) * 28;
            };
            r.Load = delegate
            {
                List<object> days = item.A("days") ?? new List<object>();
                for (int i = 0; i < cbs.Count; i++) { bool v = days.Contains(Cat.Days[i][0]); if (cbs[i].Checked != v) cbs[i].Checked = v; }
            };
            return r;
        }

        // ------------------------------------------------------------ результат

        ListBox warnBox;
        TextBox preview;
        Label warnHdr, xmlStatus;
        UiButton btnUseInstall, btnUseCapture, btnLauncher;

        void PageResult()
        {
            GPage p = AddPage("result", "Результат", "check");
            p.AutoScroll = false;
            p.Padding = new Padding(GPage.PageMargin, 20, GPage.PageMargin, 20);

            preview = new TextBox();
            preview.Multiline = true; preview.ReadOnly = true; preview.WordWrap = false;
            preview.ScrollBars = ScrollBars.Both;
            preview.Font = new Font("Consolas", 9.5f);
            FieldBox previewBox = FieldBox.Wrap(preview);
            previewBox.Dock = DockStyle.Fill;
            preview.BackColor = Theme.Field;
            p.Controls.Add(previewBox);

            xmlStatus = new Label();
            xmlStatus.Dock = DockStyle.Top; xmlStatus.Height = 30; xmlStatus.TextAlign = ContentAlignment.MiddleLeft;
            p.Controls.Add(xmlStatus);

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top; bar.Height = 54; bar.AutoSize = true; bar.AutoSizeMode = AutoSizeMode.GrowAndShrink; bar.MinimumSize = new Size(0, 46); bar.Padding = new Padding(0, 4, 0, 0);
            bar.BackColor = Theme.Bg;
            Action<string, int, Action> mk = delegate(string cap, int w, Action act)
            {
                UiButton b = new UiButton();
                b.Text = cap; b.Size = new Size(w, 36); b.Margin = new Padding(0, 0, 10, 8);
                b.Click += delegate { act(); };
                bar.Controls.Add(b);
            };
            mk("Сохранить XML…", 150, SaveXmlDialog);
            mk("Копировать XML", 150, CopyXml);
            mk("Сохранить комплект…", 180, SaveBundleDialog);
            btnLauncher = new UiButton(); btnLauncher.Text = "Start-Sysprep.cmd…"; btnLauncher.Size = new Size(170, 36); btnLauncher.Margin = new Padding(0, 0, 10, 8);
            btnLauncher.Click += delegate { SaveLauncherDialog(); };
            bar.Controls.Add(btnLauncher);
            btnUseInstall = new UiButton(); btnUseInstall.Text = "Использовать для установки"; btnUseInstall.Size = new Size(240, 36); btnUseInstall.Primary = true; btnUseInstall.Margin = new Padding(0, 0, 10, 8);
            btnUseInstall.Click += delegate { UseForInstall(false); };
            bar.Controls.Add(btnUseInstall);
            btnUseCapture = new UiButton(); btnUseCapture.Text = "Использовать для захвата образа"; btnUseCapture.Size = new Size(270, 36); btnUseCapture.Primary = true; btnUseCapture.Margin = new Padding(0, 0, 10, 8);
            btnUseCapture.Click += delegate { UseForInstall(true); };
            bar.Controls.Add(btnUseCapture);
            p.Controls.Add(bar);

            warnBox = new ListBox();
            warnBox.Dock = DockStyle.Top; warnBox.Height = 140;
            warnBox.DrawMode = DrawMode.OwnerDrawVariable;
            warnBox.BorderStyle = BorderStyle.None;
            warnBox.BackColor = Theme.Card;
            warnBox.IntegralHeight = false;
            Dark.Scroll(warnBox);
            warnBox.MeasureItem += delegate(object s, MeasureItemEventArgs e)
            {
                Warn w = warnBox.Items[e.Index] as Warn;
                e.ItemHeight = TextH("• " + (w == null ? "" : w.Text), warnBox.Font, warnBox.ClientSize.Width - 28) + 10;
            };
            warnBox.DrawItem += delegate(object s, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                Warn w = (Warn)warnBox.Items[e.Index];
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (SolidBrush b = new SolidBrush(sel ? Theme.AccentSoft : Theme.Card)) e.Graphics.FillRectangle(b, e.Bounds);
                Color c = w.Tab != null ? Theme.AccentHi : Theme.Text;
                TextRenderer.DrawText(e.Graphics, "• " + w.Text, warnBox.Font, new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 4, e.Bounds.Width - 24, e.Bounds.Height - 4), c, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            };
            warnBox.MouseClick += delegate(object s, MouseEventArgs e)
            {
                int i = warnBox.IndexFromPoint(e.Location);
                if (i < 0) return;
                Warn w = (Warn)warnBox.Items[i];
                if (w.Tab != null) GoTo(w.Tab, w.Key);
            };
            p.Controls.Add(warnBox);

            warnHdr = new Label();
            warnHdr.Dock = DockStyle.Top; warnHdr.Height = 34; warnHdr.TextAlign = ContentAlignment.MiddleLeft;
            warnHdr.Font = boldFont; warnHdr.ForeColor = Theme.Text;
            p.Controls.Add(warnHdr);

            Label title = new Label();
            title.Text = "Результат"; title.Font = Theme.H1; title.ForeColor = Theme.Text;
            title.Dock = DockStyle.Top; title.Height = 50; title.TextAlign = ContentAlignment.TopLeft;
            p.Controls.Add(title);
            previewBox.BringToFront();
        }

        void ShowResult()
        {
            if (last == null) last = Generator.Generate(St);
            int n = last.Warnings.Count;
            warnHdr.Text = n == 0 ? "Предупреждений нет" : "Предупреждения (" + n + ") — щёлкните, чтобы перейти к нужному полю";
            warnHdr.ForeColor = n == 0 ? Theme.Ok : Theme.Warn;
            warnBox.BeginUpdate();
            warnBox.Items.Clear();
            foreach (Warn w in last.Warnings) warnBox.Items.Add(w);
            warnBox.EndUpdate();
            warnBox.Visible = n > 0;
            string text = last.Xml.Replace("\n", "\r\n");
            if (preview.Text != text) preview.Text = text;
            bool ok = true; string err = "";
            try { XmlDocument d = new XmlDocument(); d.LoadXml(last.Xml); }
            catch (Exception ex) { ok = false; err = ex.Message; }
            xmlStatus.ForeColor = ok ? Theme.Ok : Theme.Danger;
            xmlStatus.Text = ok ? "XML корректен · " + (Encoding.UTF8.GetByteCount(last.Xml) / 1024.0).ToString("0.0") + " КБ · " + (last.IsSys ? "unattend.xml" : "autounattend.xml")
                                : "XML содержит ошибки: " + err;
            btnLauncher.Visible = last.IsSys;
            btnUseCapture.Visible = last.IsSys;
            btnUseInstall.Visible = !last.IsSys;
        }

        // ------------------------------------------------------------ действия

        string XmlName { get { return last != null && last.IsSys ? "unattend.xml" : "autounattend.xml"; } }

        void LoadSettingsDialog()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "Настройки (*.json)|*.json|Все файлы|*.*";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    List<string> P = Import(File.ReadAllText(d.FileName, Encoding.UTF8));
                    if (P.Count > 0)
                    {
                        string msg = "Настройки загружены, но " + P.Count + " знач. не прошли проверку и заменены значениями по умолчанию:\n\n";
                        for (int i = 0; i < P.Count && i < 12; i++) msg += P[i] + "\n";
                        if (P.Count > 12) msg += "…";
                        MessageBox.Show(this, msg, "Загрузка настроек");
                    }
                }
                catch (Exception ex) { MessageBox.Show(this, "Не удалось прочитать файл настроек: " + ex.Message, "Загрузка настроек"); }
            }
        }

        void SaveSettingsDialog()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "Настройки (*.json)|*.json"; d.DefaultExt = "json"; d.FileName = "autounattend-settings.json";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(d.FileName, Export(), new UTF8Encoding(false)); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Сохранение настроек"); }
            }
        }

        void ResetDialog()
        {
            if (MessageBox.Show(this, "Сбросить все настройки к значениям по умолчанию?", "Сброс", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) ResetState();
        }

        void SaveXmlDialog()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "XML (*.xml)|*.xml"; d.DefaultExt = "xml"; d.FileName = XmlName;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(d.FileName, last.Xml, new UTF8Encoding(false)); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Сохранение XML"); }
            }
        }

        void CopyXml()
        {
            try { Clipboard.SetText(last.Xml); xmlStatus.Text = "Скопировано в буфер обмена"; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Копирование"); }
        }

        void SaveLauncherDialog()
        {
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "CMD (*.cmd)|*.cmd"; d.DefaultExt = "cmd"; d.FileName = "Start-Sysprep.cmd";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(d.FileName, GenLauncher.Launcher(St), new UTF8Encoding(false)); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Сохранение"); }
            }
        }

        /// <summary>Сохраняет комплект как в оригинале: XML, README, настройки, скрипты (и Start-Sysprep.cmd для образа).</summary>
        void SaveBundleDialog()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Папка для комплекта файлов (будет создана вложенная папка autounattend-bundle)";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string dir = Path.Combine(d.SelectedPath, "autounattend-bundle");
                    Directory.CreateDirectory(Path.Combine(dir, "scripts"));
                    UTF8Encoding plain = new UTF8Encoding(false), bom = new UTF8Encoding(true);
                    File.WriteAllText(Path.Combine(dir, XmlName), last.Xml, plain);
                    File.WriteAllText(Path.Combine(dir, "README.txt"), GenLauncher.Readme(St, last), bom);
                    File.WriteAllText(Path.Combine(dir, "settings.json"), Export(), plain);
                    if (last.IsSys) File.WriteAllText(Path.Combine(dir, "Start-Sysprep.cmd"), GenLauncher.Launcher(St), plain);
                    foreach (GenFile f in last.Files)
                    {
                        string n = f.Path.Substring(f.Path.LastIndexOf('\\') + 1);
                        File.WriteAllText(Path.Combine(dir, "scripts\\" + n), f.Content, n.EndsWith(".ps1") ? bom : plain);
                    }
                    MessageBox.Show(this, "Комплект сохранён:\n" + dir, "Готово");
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Сохранение комплекта"); }
            }
        }

        void UseForInstall(bool forCapture)
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "Buba-Helper");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, XmlName);
                File.WriteAllText(path, last.Xml, new UTF8Encoding(false));
                if (UseXml != null) UseXml(path, forCapture);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Файл ответов"); }
        }
    }
}
