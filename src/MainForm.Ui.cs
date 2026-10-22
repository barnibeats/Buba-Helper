// Главная форма: построение интерфейса. Верхняя панель с режимами, слева меню шагов, справа карточки, снизу полоса действий.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinInstaller
{
    partial class MainForm
    {
        static readonly string[] InstallSteps = { "Образ", "Диск", "Разметка", "Настройки", "Установка" };
        static readonly string[] CaptureSteps = { "Раздел", "Файл образа", "XML и запуск" };

        // ------------------------------------------------------------ помощники раскладки

        T Add<T>(Control parent, T c, int x, int y, int w, int h) where T : Control
        {
            c.Left = x; c.Top = y; c.Width = w; c.Height = h;
            parent.Controls.Add(c);
            return c;
        }

        /// <summary>Маленькая приглушённая подпись над полем.</summary>
        Label Cap(Control parent, string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text; l.Font = Theme.Small; l.ForeColor = Theme.Muted; l.AutoSize = true; l.Location = new Point(x, y);
            parent.Controls.Add(l);
            return l;
        }

        /// <summary>Поясняющий текст (приглушённый, с переносом). right >= 0 - растягивать до правого края с этим отступом.</summary>
        Label Hint(Control parent, string text, int x, int y, int w, int h, int right)
        {
            Label l = new Label();
            l.Text = text; l.Font = Theme.Small; l.ForeColor = Theme.Muted; l.SetBounds(x, y, w, h);
            parent.Controls.Add(l);
            if (right >= 0) Fluid.Stretch(l, right);
            return l;
        }

        /// <summary>Поле ввода в рамке; right >= 0 - растягивать вправо.</summary>
        FieldBox Input(Control parent, TextBox tb, int x, int y, int w, int right)
        {
            FieldBox fb = FieldBox.Wrap(tb);
            fb.SetBounds(x, y, w, 32);
            parent.Controls.Add(fb);
            if (right >= 0) Fluid.Stretch(fb, right);
            return fb;
        }

        UiButton Btn(Control parent, string text, string icon, int x, int y, int w, int h, int pinRight)
        {
            UiButton b = new UiButton();
            b.Text = text; b.Icon = icon;
            b.SetBounds(x, y, w, h);
            parent.Controls.Add(b);
            if (pinRight >= 0) Fluid.PinRight(b, pinRight);
            return b;
        }

        TabPage AddPage(TabControl tc, string title)
        {
            TabPage p = new TabPage(title);
            p.UseVisualStyleBackColor = false;
            p.BackColor = Theme.Bg;
            p.ForeColor = Theme.Text;
            p.Padding = new Padding(32, 26, 32, 24);
            Ui.VerticalOnly(p);
            tc.TabPages.Add(p);
            Dark.Scroll(p);
            return p;
        }

        /// <summary>Заголовок страницы: крупное название и поясняющая строка.</summary>
        void Head(TabPage pg, string title, string sub)
        {
            DPanel h = new DPanel();
            h.Dock = DockStyle.Top; h.Height = 74; h.BackColor = Theme.Bg;
            Label t = new Label(); t.Text = title; t.Font = Theme.H1; t.ForeColor = Theme.Text; t.AutoSize = true; t.Location = new Point(0, 0);
            Label s = new Label(); s.Text = sub; s.Font = Theme.UI; s.ForeColor = Theme.Muted; s.AutoSize = true; s.Location = new Point(2, 40);
            h.Controls.Add(t); h.Controls.Add(s);
            pg.Controls.Add(h);
            h.BringToFront();
        }

        UiCard NewCard(TabPage pg, string title, int h)
        {
            UiCard c = new UiCard();
            c.Text = title; c.Height = h; c.Dock = DockStyle.Top;
            pg.Controls.Add(c);
            c.BringToFront();
            return c;
        }

        void Gap(TabPage pg, int h)
        {
            DPanel s = new DPanel();
            s.Dock = DockStyle.Top; s.Height = h; s.BackColor = Theme.Bg;
            pg.Controls.Add(s);
            s.BringToFront();
        }

        /// <summary>Высота прокрутки страницы по сумме высот поставленных карточек.</summary>
        void FinishPage(TabPage pg)
        {
            int h = pg.Padding.Vertical;
            foreach (Control c in pg.Controls) h += c.Height;
            pg.AutoScrollMinSize = new Size(0, h);
        }

        // ------------------------------------------------------------ интерфейс

        void BuildUi()
        {
            Text = "Buba-Helper" + (Program.DryRun ? "  [DRY-RUN]" : "");
            Font = Theme.UI;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            DoubleBuffered = true;
            Icon = Icons.AppIcon();
            ClientSize = new Size(1080, 720);
            MinimumSize = new Size(960, 640);
            StartPosition = FormStartPosition.CenterScreen;

            // корневая раскладка: сначала то, что занимает остаток (оно «докуется» последним), затем нижние и верхняя полосы
            body = new DPanel(); body.Dock = DockStyle.Fill; body.BackColor = Theme.Bg;
            Controls.Add(body);
            pnlLog = new DPanel(); pnlLog.Dock = DockStyle.Bottom; pnlLog.Height = 180; pnlLog.BackColor = Theme.Side;
            pnlLog.Padding = new Padding(24, 12, 24, 14); pnlLog.Visible = false;
            Controls.Add(pnlLog);
            footer = new DPanel(); footer.Dock = DockStyle.Bottom; footer.Height = 108; footer.BackColor = Theme.Side;
            footer.Paint += delegate(object s, PaintEventArgs e) { using (Pen p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, 0, 0, footer.Width, 0); };
            Controls.Add(footer);
            header = new UiHeader(); header.Dock = DockStyle.Top; header.Height = 60;
            Controls.Add(header);

            // режимы
            rbModeInstall = new UiTab { Text = "Установка Windows", Icon = "disc", Checked = true };
            rbModeCapture = new UiTab { Text = "Захват образа", Icon = "camera" };
            rbModeXml = new UiTab { Text = "Файл ответов (XML)", Icon = "script" };
            int tx = 232;
            foreach (UiTab t in new UiTab[] { (UiTab)rbModeInstall, (UiTab)rbModeCapture, (UiTab)rbModeXml })
            {
                t.SetBounds(tx, 0, t.PreferredW(), header.Height - 1);
                header.Controls.Add(t);
                tx += t.Width + 6;
            }

            // шаги мастера: вкладки без полосы заголовков + боковое меню
            tabInstall = new StepTabs(); tabInstall.Dock = DockStyle.Fill;
            tabCapture = new StepTabs(); tabCapture.Dock = DockStyle.Fill; tabCapture.Visible = false;
            body.Controls.Add(tabInstall);
            body.Controls.Add(tabCapture);
            navSteps = new NavList(); navSteps.Dock = DockStyle.Left; navSteps.Width = 232; navSteps.Numbered = true;
            body.Controls.Add(navSteps);

            TabPage pgImage = AddPage(tabInstall, "Образ");
            TabPage pgDisk = AddPage(tabInstall, "Диск");
            TabPage pgLayout = AddPage(tabInstall, "Разметка");
            TabPage pgExtra = AddPage(tabInstall, "Настройки");
            TabPage pgRun = AddPage(tabInstall, "Установка");

            // 1. образ
            Head(pgImage, "Образ Windows", "Выберите ISO, WIM или ESD. Образы на всех дисках находятся автоматически.");
            UiCard g1 = NewCard(pgImage, "Источник", 226);
            Cap(g1, "Файл образа", 18, 52);
            cmbSource = Add(g1, new UiCombo(), 18, 74, 400, 32);
            btnScan = Btn(g1, "Поиск", "search", 0, 74, 94, 32, 18);
            btnBrowse = Btn(g1, "Обзор…", "folder", 0, 74, 108, 32, 18 + 94 + 8);
            Fluid.Stretch(cmbSource, 18 + 94 + 8 + 108 + 8);
            Cap(g1, "Редакция", 18, 122);
            cmbEdition = Add(g1, new UiCombo(), 18, 144, 400, 32);
            Fluid.Stretch(cmbEdition, 18);
            lblSrcStatus = Hint(g1, "", 18, 190, 600, 20, 18);
            FinishPage(pgImage);

            // 2. диск
            Head(pgDisk, "Диск для установки", "Сначала выберите режим, затем диск. Диск, на котором лежит образ, выбрать нельзя.");
            grpDisk = (UiCard)NewCard(pgDisk, "Диск для установки (ВСЕ ДАННЫЕ НА НЁМ БУДУТ УДАЛЕНЫ)", 380);
            // режим выбирается до диска: от него зависит, какие диски доступны
            rbWipe = Add(grpDisk, new UiRadio { Text = "Стереть весь диск и разметить заново" }, 18, 52, 340, 24);
            rbKeep = Add(grpDisk, new UiRadio { Text = "Только раздел Windows (остальное не трогать)" }, 370, 52, 400, 24);
            rbWipe.Checked = true;
            lvDisks = Add(grpDisk, new UiList(), 18, 90, 600, 232);
            lvDisks.Columns.Add("№", 52);
            lvDisks.Columns.Add("Модель", 250);
            lvDisks.Columns.Add("Размер", 96);
            lvDisks.Columns.Add("Шина", 76);
            lvDisks.Columns.Add("Стиль", 70);
            lvDisks.Columns.Add("Разделы (тома)", 300);
            Fluid.Stretch(lvDisks, 18);
            btnRefresh = Btn(grpDisk, "Обновить", "refresh", 18, 332, 122, 34, -1);
            lblDiskHint = Hint(grpDisk, "", 154, 340, 400, 20, 18);
            FinishPage(pgDisk);

            // 3. разметка
            Head(pgLayout, "Разметка и размер", "Таблица разделов и размер раздела Windows. Режим BIOS/UEFI определён автоматически.");
            UiCard g3 = NewCard(pgLayout, "Разметка", 312);
            Cap(g3, "Таблица разделов", 18, 52);
            rbGpt = Add(g3, new UiRadio { Text = "GPT (UEFI)" }, 18, 74, 130, 24);
            rbMbr = Add(g3, new UiRadio { Text = "MBR (BIOS / Legacy)" }, 160, 74, 210, 24);
            lblFw = Hint(g3, "", 390, 77, 300, 20, 18);
            lblSize = Cap(g3, "Размер раздела Windows, МБ", 18, 118);
            nudSize = new NumericUpDown { Minimum = 20480, Maximum = 4194304, Increment = 1024, Value = 51200 };
            nudSizeBox = Add(g3, FieldBox.Wrap(nudSize), 18, 140, 176, 32);
            chkAll = Add(g3, new UiCheck { Text = "весь диск" }, 212, 144, 120, 24);
            lblSizeHint = Hint(g3, "1 ГБ = 1024 МБ  ·  51200 МБ = 50 ГБ", 344, 148, 340, 20, 18);
            lblPart = Cap(g3, "Раздел, в который ставить Windows", 18, 118);
            cmbPart = Add(g3, new UiCombo(), 18, 140, 500, 32);
            Fluid.Stretch(cmbPart, 18);
            lblPart.Visible = false; cmbPart.Visible = false;
            nudSize.ValueChanged += delegate { UpdatePlanText(); };
            chkAll.CheckedChanged += delegate { nudSize.Enabled = !chkAll.Checked; UpdatePlanText(); };
            chkData = Add(g3, new UiCheck { Text = "Оставшееся место отдать под отдельный раздел «Data» (NTFS)" }, 18, 192, 640, 24);
            lblPlan = Add(g3, new Label(), 18, 228, 600, 64);
            lblPlan.ForeColor = Theme.AccentHi; lblPlan.Font = Theme.UI;
            Fluid.Stretch(lblPlan, 18);
            FinishPage(pgLayout);

            // 4. дополнительно
            Head(pgExtra, "Дополнительно", "Драйверы, файл ответов и действия после установки.");
            UiCard g4 = NewCard(pgExtra, "Параметры установки", 316);
            chkDrivers = Add(g4, new UiCheck { Text = "Добавить драйверы из папки" }, 18, 52, 320, 24);
            txtDrivers = new TextBox();
            Input(g4, txtDrivers, 18, 80, 500, 18 + 112 + 8);
            btnDrivers = Btn(g4, "Обзор…", "folder", 0, 80, 112, 32, 18);
            string drv = Path.Combine(Util.AppDir(), "Drivers");
            if (Directory.Exists(drv)) { txtDrivers.Text = drv; chkDrivers.Checked = true; }
            Cap(g4, "XML файл ответов (пусто = без XML)", 18, 128);
            txtInstXml = new TextBox();
            Input(g4, txtInstXml, 18, 150, 500, 18 + 112 + 8);
            btnInstXml = Btn(g4, "Обзор…", "folder", 0, 150, 112, 32, 18);
            chkReboot = Add(g4, new UiCheck { Text = "Перезагрузить ПК после установки" }, 18, 202, 320, 24);
            chkReboot.Checked = !Program.DryRun;
            chkVerify = Add(g4, new UiCheck { Text = "Проверять SHA-256 (.sha256)", Checked = true }, 360, 202, 300, 24);
            btnSaveProf = Btn(g4, "Сохранить профиль", "save", 18, 254, 196, 34, -1);
            btnLoadProf = Btn(g4, "Загрузить профиль", "folder", 224, 254, 196, 34, -1);
            FinishPage(pgExtra);

            // 5. итог: сводка выбранного перед запуском
            Head(pgRun, "Всё готово к установке", "Проверьте параметры. Перед стартом программа попросит подтверждение.");
            UiCard g5 = NewCard(pgRun, "Сводка", 360);
            lblSummary = Add(g5, new Label(), 18, 52, 600, 292);
            lblSummary.Font = Theme.Mono; lblSummary.ForeColor = Theme.Text;
            Fluid.Stretch(lblSummary, 18);
            FinishPage(pgRun);

            BuildCaptureUi();

            // нижняя полоса: прогресс и статус, журнал, навигация мастера и запуск
            progress = new UiProgress();
            progress.SetBounds(24, 14, 600, 8);
            footer.Controls.Add(progress);
            Fluid.Stretch(progress, 24);
            lblStatus = new Label(); lblStatus.Text = "Готово к работе"; lblStatus.ForeColor = Theme.Text; lblStatus.AutoEllipsis = true;
            lblStatus.SetBounds(24, 28, 600, 22); lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            footer.Controls.Add(lblStatus);
            Fluid.Stretch(lblStatus, 24 + 130);
            btnLog = Btn(footer, "Журнал", "log", 0, 26, 122, 26, 24);
            btnLog.Ghost = true;
            btnBack = Btn(footer, "Назад", "arrowL", 24, 58, 120, 40, -1);
            UiButton nx = Btn(footer, "Далее", "arrowR", 0, 58, 150, 40, 24);
            nx.Primary = true; nx.IconRight = true;
            btnNext = nx;
            UiButton ins = Btn(footer, "УСТАНОВИТЬ WINDOWS", "go", 0, 58, 300, 40, 24);
            ins.Primary = true;
            btnInstall = ins;
            btnInstall.Visible = false;
            btnInstall.Font = Theme.Big;
            lblNavHint = new Label(); lblNavHint.ForeColor = Theme.Warn; lblNavHint.TextAlign = ContentAlignment.MiddleLeft; lblNavHint.AutoEllipsis = true;
            lblNavHint.SetBounds(160, 58, 500, 40);
            footer.Controls.Add(lblNavHint);
            Fluid.Stretch(lblNavHint, 24 + 310);

            txtLog = new TextBox();
            txtLog.Multiline = true; txtLog.ReadOnly = true; txtLog.ScrollBars = ScrollBars.Vertical; txtLog.BorderStyle = BorderStyle.None;
            txtLog.BackColor = Theme.Card; txtLog.ForeColor = Theme.C(176, 186, 206); txtLog.Font = new Font("Consolas", 9f);
            txtLog.Dock = DockStyle.Fill;
            Dark.Scroll(txtLog);
            pnlLog.Controls.Add(txtLog);

            // события
            btnLog.Click += delegate { ShowLog(!logOpen); };
            btnBrowse.Click += delegate { BrowseSource(); };
            btnScan.Click += delegate { ScanSources(); };
            btnRefresh.Click += delegate { RefreshDisks(); };
            btnDrivers.Click += delegate
            {
                using (FolderBrowserDialog fb = new FolderBrowserDialog())
                    if (fb.ShowDialog(this) == DialogResult.OK) { txtDrivers.Text = fb.SelectedPath; chkDrivers.Checked = true; }
            };
            cmbSource.SelectionChangeCommitted += delegate { BeginInvoke((Action)delegate { LoadSourceAsync(cmbSource.Text); }); };
            rbGpt.CheckedChanged += delegate { UpdatePlanText(); };
            rbMbr.CheckedChanged += delegate { UpdatePlanText(); };
            chkData.CheckedChanged += delegate { UpdatePlanText(); };
            lvDisks.SelectedIndexChanged += delegate { UpdatePlanText(); };
            cmbEdition.SelectedIndexChanged += delegate { UpdateNav(); };
            lvVols.SelectedIndexChanged += delegate { UpdateNav(); };
            txtOut.TextChanged += delegate { UpdateNav(); };
            foreach (TabControl tc in new TabControl[] { tabInstall, tabCapture })
            {
                TabControl tab = tc;
                tab.Selecting += delegate(object s, TabControlCancelEventArgs e)
                {
                    // вперёд - только через заполненные шаги (назад можно всегда)
                    for (int k = tab.SelectedIndex; k < e.TabPageIndex; k++)
                    {
                        string pr = StepProblem(k);
                        if (pr != null) { e.Cancel = true; lblNavHint.Text = pr; return; }
                    }
                };
                tab.SelectedIndexChanged += delegate { if (tab == CurrentTabs()) navSteps.SelectSilently(tab.SelectedIndex); UpdateNav(); };
            }
            navSteps.SelectedIndexChanged += delegate
            {
                TabControl tc = CurrentTabs();
                int want = navSteps.SelectedIndex;
                if (want >= 0 && want < tc.TabCount) tc.SelectedIndex = want;
                if (tc.SelectedIndex != want) navSteps.SelectSilently(tc.SelectedIndex);   // переход не разрешён - возвращаем выделение
            };
            btnBack.Click += delegate
            {
                if (rbModeXml.Checked) { if (genEditor != null) genEditor.Prev(); return; }
                CurrentTabs().SelectedIndex = Math.Max(0, CurrentTabs().SelectedIndex - 1);
            };
            btnNext.Click += delegate
            {
                if (rbModeXml.Checked) { if (genEditor != null) genEditor.Next(); return; }
                CurrentTabs().SelectedIndex = Math.Min(CurrentTabs().TabCount - 1, CurrentTabs().SelectedIndex + 1);
            };
            btnInstall.Click += delegate { if (rbModeCapture.Checked) StartCapture(); else StartInstall(false); };
            rbKeep.CheckedChanged += delegate { ApplyModeLayout(); };
            cmbPart.SelectedIndexChanged += delegate { UpdatePlanText(); };
            btnInstXml.Click += delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Filter = "XML (*.xml)|*.xml|Все файлы|*.*";
                    if (ofd.ShowDialog(this) == DialogResult.OK) txtInstXml.Text = ofd.FileName;
                }
            };
            btnSaveProf.Click += delegate
            {
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Filter = "Профиль (*.ini)|*.ini"; sfd.DefaultExt = "ini"; sfd.FileName = "profile.ini"; sfd.InitialDirectory = Util.AppDir();
                    if (sfd.ShowDialog(this) != DialogResult.OK) return;
                    try { SaveProfile(sfd.FileName); Log("Профиль сохранён: " + sfd.FileName); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "Профиль"); }
                }
            };
            btnLoadProf.Click += delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Filter = "Профиль (*.ini)|*.ini|Все файлы|*.*"; ofd.InitialDirectory = Util.AppDir();
                    if (ofd.ShowDialog(this) != DialogResult.OK) return;
                    autoStart = false;
                    ApplyProfile(ofd.FileName);
                    ResolvePendingSource(lastFound);
                }
            };
            rbModeInstall.CheckedChanged += delegate { SwitchMode(); };
            rbModeCapture.CheckedChanged += delegate { SwitchMode(); };
            rbModeXml.CheckedChanged += delegate { SwitchMode(); };
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (installing) { e.Cancel = true; MessageBox.Show("Идёт операция - закрывать нельзя."); return; }
                iso.Dismount();
                CleanupExtracted();
            };
            uiReady = true;
            RebuildNav();
            LayoutFooter(false);
            UpdateNav();
        }

        /// <summary>Заполняет боковое меню шагами текущего режима.</summary>
        void RebuildNav()
        {
            string[] steps = rbModeCapture.Checked ? CaptureSteps : InstallSteps;
            navSteps.ClearItems();
            foreach (string s in steps) navSteps.Add(s, null);
            navSteps.SelectSilently(CurrentTabs().SelectedIndex);
        }
    }
}
