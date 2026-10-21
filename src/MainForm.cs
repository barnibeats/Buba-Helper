// Главная форма: поля, конструктор, построение интерфейса и мастер шагов.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace WinInstaller
{
    partial class MainForm : Form
    {
        // источник
        ComboBox cmbSource, cmbEdition;
        Button btnBrowse, btnScan;
        Label lblSrcStatus;
        // диск
        ListView lvDisks;
        Button btnRefresh;
        Label lblDiskHint;
        // разметка
        RadioButton rbGpt, rbMbr;
        Label lblFw, lblPlan;
        NumericUpDown nudSize;
        CheckBox chkAll;
        CheckBox chkData;
        // дополнительно
        CheckBox chkDrivers, chkReboot;
        TextBox txtDrivers;
        Button btnDrivers;
        // режим
        RadioButton rbModeInstall, rbModeCapture, rbModeXml;
        GenEditor genEditor;                      // генератор файла ответов; создаётся при первом открытии вкладки
        TabControl tabInstall, tabCapture;
        // мастер: кнопки шагов и итоговая сводка
        Button btnBack, btnNext;
        Label lblNavHint, lblSummary;
        bool uiReady;
        // захват
        ListView lvVols;
        Button btnVolRefresh, btnOut, btnXml, btnXmlClear;
        TextBox txtOut, txtName, txtDesc, txtXml;
        ComboBox cmbCompress;
        Label lblVolHint;
        // режим диска / профили / XML / проверка
        GroupBox grpDisk;
        RadioButton rbWipe, rbKeep;
        Label lblSize, lblSizeHint, lblPart;
        ComboBox cmbPart;
        TextBox txtInstXml;
        Button btnInstXml, btnSaveProf, btnLoadProf;
        CheckBox chkVerify, chkHash;
        // выполнение
        Button btnInstall;
        ProgressBar progress;
        Label lblStatus;
        TextBox txtLog;

        string firmware = "?";
        string imageFile;                        // реальный путь к wim/esd, из которого ставим
        List<ImageInfo> images = new List<ImageInfo>();
        List<int> sourceDisks = new List<int>(); // диски, на которых лежит источник
        IsoMount iso = new IsoMount();
        string extractedTmp;                     // временный wim, извлечённый через 7z
        bool busy;
        bool installing;
        string logFile;
        string sourcePath;                       // выбранный пользователем файл (ISO/WIM)
        List<char> sourceLetters = new List<char>();
        List<string> lastFound = new List<string>();
        int partsDisk = -1;
        // профиль / авторежим
        string pendingSource, pendingEdition, pendingDisk;
        bool autoStart, autoYes;

        const int EfiMB = 300, MsrMB = 16;

        public MainForm() : this(null, false, false) { }

        public MainForm(string profilePath, bool auto, bool yes)
        {
            BuildUi();
            logFile = Path.Combine(Util.AppDir(), "install.log");
            firmware = Native.FirmwareMode();
            lblFw.Text = "ПК загружен в режиме: " + firmware;
            if (firmware == "BIOS") rbMbr.Checked = true; else rbGpt.Checked = true;
            Log("WinInstaller запущен" + (Program.DryRun ? " (DRY-RUN: команды не выполняются)" : "") + ". Режим прошивки: " + firmware);
            if (profilePath == null)
            {
                string def = Path.Combine(Util.AppDir(), "profile.ini");
                if (File.Exists(def)) profilePath = def;
            }
            else profilePath = ResolvePath(profilePath);
            if (profilePath != null)
            {
                autoStart = auto; autoYes = yes;
                ApplyProfile(profilePath);
            }
            Load += delegate
            {
                RefreshDisks(); ScanSources(); UpdatePlanText();
                if (Program.StartMode == "xml") { rbModeXml.Checked = true; if (genEditor != null) genEditor.Go(Program.StartPage); }
                else if (Program.StartMode == "capture") rbModeCapture.Checked = true;
            };
        }

        // ------------------------------------------------------------ UI

        T Add<T>(Control parent, T c, int x, int y, int w, int h) where T : Control
        {
            c.Left = x; c.Top = y; c.Width = w; c.Height = h;
            parent.Controls.Add(c);
            return c;
        }

        void BuildUi()
        {
            Text = "Buba-Helper" + (Program.DryRun ? "  [DRY-RUN]" : "");
            Font = new Font("Segoe UI", 9f);
            BackColor = Theme.Face;
            DoubleBuffered = true;
            Icon = Icons.AppIcon();
            ClientSize = new Size(900, 604);
            MinimumSize = new Size(916, 640);
            StartPosition = FormStartPosition.CenterScreen;

            // верхняя полоса в стиле Windows 7 с переключателем режима
            AeroHeader header = Add(this, new AeroHeader(), 0, 0, 900, 42);
            header.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            rbModeInstall = Add(header, new AeroToggle { Text = "Установка Windows", TextAlign = ContentAlignment.MiddleCenter, Checked = true, Glyph = Icons.Make("disc", 16) }, 12, 6, 190, 30);
            rbModeCapture = Add(header, new AeroToggle { Text = "Захват образа (sysprep)", TextAlign = ContentAlignment.MiddleCenter, Glyph = Icons.Make("camera", 16) }, 208, 6, 200, 30);
            rbModeXml = Add(header, new AeroToggle { Text = "Файл ответов (XML)", TextAlign = ContentAlignment.MiddleCenter, Glyph = Icons.Make("script", 16) }, 414, 6, 190, 30);

            // пошаговый мастер: каждый шаг - своя вкладка, вперёд можно только после заполнения шага
            tabInstall = Add(this, new AeroTabControl(), 0, 48, 900, 302);
            tabInstall.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            tabInstall.ImageList = Icons.List("disc", "drive", "layout", "gear", "go");
            tabCapture = Add(this, new AeroTabControl(), 0, 48, 900, 302);
            tabCapture.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            tabCapture.ImageList = Icons.List("drive", "doc", "go");
            tabCapture.Visible = false;
            TabPage pgImage = AddPage(tabInstall, "1. Образ", 0);
            TabPage pgDisk = AddPage(tabInstall, "2. Диск", 1);
            TabPage pgLayout = AddPage(tabInstall, "3. Разметка", 2);
            TabPage pgExtra = AddPage(tabInstall, "4. Настройки", 3);
            TabPage pgRun = AddPage(tabInstall, "5. Установка", 4);

            // 1. образ
            GroupBox g1 = Add(pgImage, new AeroGroupBox(), 8, 8, 876, 116);
            g1.Text = "1. Образ (ISO / WIM / ESD)"; g1.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Add(g1, new Label { Text = "Файл:" }, 12, 28, 120, 22);
            cmbSource = Add(g1, new ComboBox(), 140, 24, 600, 26);
            cmbSource.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnBrowse = Add(g1, new AeroButton { Text = "Обзор..." }, 748, 22, 56, 28);
            btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right; btnBrowse.Width = 58;
            btnScan = Add(g1, new AeroButton { Text = "Поиск" }, 810, 22, 56, 28);
            btnScan.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Add(g1, new Label { Text = "Редакция:" }, 12, 62, 120, 22);
            cmbEdition = Add(g1, new ComboBox(), 140, 58, 726, 26);
            cmbEdition.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbEdition.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblSrcStatus = Add(g1, new Label(), 140, 88, 726, 20);
            lblSrcStatus.ForeColor = Color.DimGray;

            // 2. диск
            grpDisk = Add(pgDisk, new AeroGroupBox(), 8, 8, 876, 190);
            GroupBox g2 = grpDisk;
            g2.Text = "2. Диск для установки (ВСЕ ДАННЫЕ НА НЁМ БУДУТ УДАЛЕНЫ)";
            g2.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            // режим выбирается до диска: от него зависит, какие диски доступны
            // каждая пара радиокнопок в своём контейнере, иначе они образуют одну группу
            Panel pnlMode = Add(g2, new Panel(), 12, 20, 852, 28);
            rbWipe = Add(pnlMode, new RadioButton { Text = "Стереть весь диск и разметить заново" }, 0, 2, 340, 24);
            rbKeep = Add(pnlMode, new RadioButton { Text = "Только раздел Windows (остальное не трогать)" }, 340, 2, 400, 24);
            rbWipe.Checked = true;
            lvDisks = Add(g2, new ListView(), 12, 52, 852, 96);
            lvDisks.View = View.Details; lvDisks.FullRowSelect = true; lvDisks.MultiSelect = false; lvDisks.HideSelection = false;
            lvDisks.GridLines = true;
            lvDisks.Columns.Add("№", 36);
            lvDisks.Columns.Add("Модель", 230);
            lvDisks.Columns.Add("Размер", 80);
            lvDisks.Columns.Add("Шина", 60);
            lvDisks.Columns.Add("Стиль", 50);
            lvDisks.Columns.Add("Разделы (тома)", 380);
            lvDisks.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnRefresh = Add(g2, new AeroButton { Text = "Обновить" }, 12, 154, 100, 28);
            lblDiskHint = Add(g2, new Label(), 124, 159, 740, 22);
            lblDiskHint.ForeColor = Color.DimGray;
            lblDiskHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // 3. разметка
            GroupBox g3 = Add(pgLayout, new AeroGroupBox(), 8, 8, 876, 156);
            g3.Text = "3. Разметка и размер"; g3.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Add(g3, new Label { Text = "Таблица разделов:" }, 12, 28, 125, 22);
            Panel pnlScheme = Add(g3, new Panel(), 140, 24, 320, 28);
            rbGpt = Add(pnlScheme, new RadioButton { Text = "GPT (UEFI)" }, 0, 2, 120, 24);
            rbMbr = Add(pnlScheme, new RadioButton { Text = "MBR (BIOS / Legacy)" }, 130, 2, 180, 24);
            lblFw = Add(g3, new Label(), 470, 28, 390, 22);
            lblFw.ForeColor = Color.DimGray;
            lblSize = Add(g3, new Label { Text = "Раздел Windows:" }, 12, 62, 125, 22);
            nudSize = Add(g3, new NumericUpDown { Minimum = 20480, Maximum = 4194304, Increment = 1024, Value = 51200 }, 140, 58, 100, 26);
            lblSizeHint = Add(g3, new Label { Text = "МБ   (1 ГБ = 1024 МБ,  51200 МБ = 50 ГБ)" }, 246, 62, 300, 22);
            chkAll = Add(g3, new CheckBox { Text = "весь диск" }, 560, 60, 120, 24);
            lblPart = Add(g3, new Label { Text = "В раздел:" }, 12, 62, 125, 22);
            cmbPart = Add(g3, new ComboBox(), 140, 58, 520, 26);
            cmbPart.DropDownStyle = ComboBoxStyle.DropDownList;
            lblPart.Visible = false; cmbPart.Visible = false;
            nudSize.ValueChanged += delegate { UpdatePlanText(); };
            chkAll.CheckedChanged += delegate { nudSize.Enabled = !chkAll.Checked; UpdatePlanText(); };
            chkData = Add(g3, new CheckBox { Text = "Оставшееся место отдать под отдельный раздел «Data» (NTFS)" }, 14, 90, 600, 24);
            lblPlan = Add(g3, new Label(), 14, 118, 850, 34);
            lblPlan.ForeColor = Color.FromArgb(0, 70, 140);
            lblPlan.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            // 4. дополнительно
            GroupBox g4 = Add(pgExtra, new AeroGroupBox(), 8, 8, 876, 116);
            g4.Text = "4. Дополнительно"; g4.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            chkDrivers = Add(g4, new CheckBox { Text = "Добавить драйверы из папки:" }, 14, 22, 220, 24);
            txtDrivers = Add(g4, new TextBox(), 240, 22, 540, 26);
            txtDrivers.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnDrivers = Add(g4, new AeroButton { Text = "Обзор..." }, 788, 20, 78, 28);
            btnDrivers.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            string drv = Path.Combine(Util.AppDir(), "Drivers");
            if (Directory.Exists(drv)) { txtDrivers.Text = drv; chkDrivers.Checked = true; }
            Add(g4, new Label { Text = "XML ответов (пусто = без XML):" }, 14, 58, 224, 22);
            txtInstXml = Add(g4, new TextBox(), 240, 54, 540, 26);
            txtInstXml.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            btnInstXml = Add(g4, new AeroButton { Text = "Обзор..." }, 788, 52, 78, 28);
            btnInstXml.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            chkReboot = Add(g4, new CheckBox { Text = "Перезагрузить ПК после установки" }, 14, 86, 280, 24);
            chkReboot.Checked = !Program.DryRun;
            chkVerify = Add(g4, new CheckBox { Text = "Проверять SHA-256 (.sha256)", Checked = true }, 296, 86, 250, 24);
            btnSaveProf = Add(g4, new AeroButton { Text = "Сохранить профиль" }, 560, 82, 150, 28);
            btnLoadProf = Add(g4, new AeroButton { Text = "Загрузить профиль" }, 716, 82, 150, 28);
            btnSaveProf.Anchor = AnchorStyles.Top | AnchorStyles.Right; btnLoadProf.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            // 5. итог: сводка выбранного перед запуском
            lblSummary = Add(pgRun, new Label(), 14, 10, 860, 244);
            lblSummary.Font = new Font("Segoe UI", 10f);
            lblSummary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            BuildCaptureUi();

            // навигация мастера и запуск
            btnBack = Add(this, new AeroButton { Text = "Назад", Glyph = Icons.Make("arrowL", 16) }, 12, 358, 130, 40);
            btnNext = Add(this, new AeroButton { Text = "Далее", Glyph = Icons.Make("arrowR", 16), GlyphRight = true }, 758, 358, 130, 40);
            btnNext.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
            lblNavHint = Add(this, new Label(), 152, 358, 596, 40);
            lblNavHint.TextAlign = ContentAlignment.MiddleLeft;
            lblNavHint.ForeColor = Color.Firebrick;
            btnInstall = Add(this, new AeroButton { Text = "УСТАНОВИТЬ WINDOWS", Visible = false, Primary = true, Glyph = Icons.Make("go", 16) }, 458, 358, 430, 40);
            btnInstall.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
            progress = Add(this, new AeroProgress(), 12, 406, 876, 20);
            progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            lblStatus = Add(this, new Label { Text = "Готово к работе" }, 12, 430, 876, 22);
            lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtLog = Add(this, new TextBox(), 12, 456, 876, 136);
            txtLog.Multiline = true; txtLog.ReadOnly = true; txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Font = new Font("Consolas", 8.5f);
            txtLog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            // события
            btnBrowse.Click += delegate { BrowseSource(); };
            btnScan.Click += delegate { ScanSources(); };
            btnRefresh.Click += delegate { RefreshDisks(); };
            btnDrivers.Click += delegate
            {
                using (FolderBrowserDialog fb = new FolderBrowserDialog())
                    if (fb.ShowDialog(this) == DialogResult.OK) { txtDrivers.Text = fb.SelectedPath; chkDrivers.Checked = true; }
            };
            cmbSource.SelectionChangeCommitted += delegate { BeginInvoke((Action)delegate { LoadSourceAsync(cmbSource.Text); }); };
            cmbSource.Click += delegate { if (cmbSource.Items.Count > 0 && !cmbSource.DroppedDown) cmbSource.DroppedDown = true; };
            cmbSource.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; LoadSourceAsync(cmbSource.Text); }
            };
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
                tab.SelectedIndexChanged += delegate { UpdateNav(); };
            }
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
            UpdateNav();
        }

        TabPage AddPage(TabControl tc, string title, int image)
        {
            TabPage p = new TabPage(title);
            p.UseVisualStyleBackColor = false;
            p.BackColor = Color.White;
            p.ImageIndex = image;
            tc.TabPages.Add(p);
            // до создания окна страница имеет случайный размер, и блоки с якорями «растягиваются» при первом показе
            p.Size = tc.DisplayRectangle.Size;
            return p;
        }

        TabControl CurrentTabs() { return rbModeCapture.Checked ? tabCapture : tabInstall; }

        /// <summary>Что мешает уйти с шага step вперёд; null - шаг заполнен.</summary>
        string StepProblem(int step)
        {
            if (rbModeCapture.Checked)
            {
                if (step == 0 && lvVols.SelectedItems.Count == 0) return "Выберите Windows-раздел для захвата.";
                if (step == 1 && txtOut.Text.Trim().Length == 0) return "Укажите файл образа.";
                return null;
            }
            switch (step)
            {
                case 0:
                    if (imageFile == null || cmbEdition.SelectedItem == null) return "Выберите образ и редакцию.";
                    break;
                case 1:
                    DiskInfo d = SelectedDisk();
                    if (d == null) return "Выберите диск для установки.";
                    if (!rbKeep.Checked && d.Blocked) return "На этом диске лежит образ - выберите другой диск.";
                    break;
                case 2:
                    if (rbKeep.Checked && cmbPart.SelectedItem == null) return "Выберите раздел, в который ставить Windows.";
                    break;
            }
            return null;
        }

        /// <summary>Кнопки «Назад/Далее/Установить» и подсказка под текущий шаг.</summary>
        void UpdateNav()
        {
            if (!uiReady) return;
            if (rbModeXml.Checked)
            {
                int gi = genEditor == null ? 0 : genEditor.Index, gl = genEditor == null ? 0 : genEditor.Count - 1;
                btnBack.Enabled = gi > 0;
                btnNext.Visible = gi < gl; btnNext.Enabled = true;
                btnInstall.Visible = false;
                int wc = genEditor == null ? 0 : genEditor.WarningCount;
                lblNavHint.Text = wc > 0 ? "Предупреждений: " + wc + " — подробности на странице «Результат»" : "";
                return;
            }
            TabControl tc = CurrentTabs();
            int i = tc.SelectedIndex, last = tc.TabCount - 1;
            bool idle = !busy && !installing;
            btnBack.Visible = true;
            btnBack.Enabled = i > 0 && idle;
            btnNext.Visible = i < last;
            btnInstall.Visible = i == last;
            string pr = i < last ? StepProblem(i) : null;
            btnNext.Enabled = pr == null && idle;
            if (!installing) lblNavHint.Text = pr ?? "";
            if (i == last && !rbModeCapture.Checked) lblSummary.Text = BuildSummary();
        }

        static string SecureBootState()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State"))
                {
                    object v = k == null ? null : k.GetValue("UEFISecureBootEnabled");
                    if (v == null) return "не определён";
                    return Convert.ToInt32(v) == 1 ? "включён" : "выключен";
                }
            }
            catch { return "не определён"; }
        }

        /// <summary>Подсказка про требования Windows 11 (для Windows 10 - null).</summary>
        string Win11Notes()
        {
            ImageInfo im = cmbEdition.SelectedItem as ImageInfo;
            if (im == null || im.Name.IndexOf("Windows 11", StringComparison.OrdinalIgnoreCase) < 0) return null;
            string s = "Windows 11 официально требует UEFI + GPT, Secure Boot и TPM 2.0. Сейчас ПК: " + firmware + ", Secure Boot " + SecureBootState()
                + ". TPM из WinPE проверить нельзя - убедитесь в настройках BIOS.";
            if (firmware != "UEFI") s += " Режим BIOS/Legacy для Windows 11 не подходит.";
            return s;
        }

        /// <summary>Итоговая сводка на последнем шаге установки.</summary>
        string BuildSummary()
        {
            StringBuilder sb = new StringBuilder();
            ImageInfo im = cmbEdition.SelectedItem as ImageInfo;
            sb.AppendLine("Образ:       " + (sourcePath ?? "-"));
            sb.AppendLine("Редакция:    " + (im == null ? "-" : im.ToString()));
            DiskInfo d = SelectedDisk();
            sb.AppendLine("Диск:           " + (d == null ? "-" : d.Number + ":  " + d.Model + "  [" + Util.FmtSize(d.Size) + ", " + d.Bus + ", " + d.Style + "]"));
            sb.AppendLine("Режим:        " + (rbKeep.Checked ? "только раздел Windows" : "стереть весь диск"));
            sb.AppendLine();
            sb.AppendLine(lblPlan.Text);
            sb.AppendLine();
            string w11 = Win11Notes();
            if (w11 != null) { sb.AppendLine(w11); sb.AppendLine(); }
            sb.AppendLine("Драйверы:  " + (chkDrivers.Checked && txtDrivers.Text.Trim().Length > 0 ? txtDrivers.Text.Trim() : "нет"));
            sb.AppendLine("XML ответов:  " + (txtInstXml.Text.Trim().Length > 0 ? txtInstXml.Text.Trim() : "без XML"));
            sb.AppendLine("После установки:  " + (chkReboot.Checked ? "перезагрузить ПК" : "не перезагружать") + ";  проверка SHA-256: " + (chkVerify.Checked ? "да" : "нет"));
            sb.AppendLine();
            sb.Append(rbKeep.Checked ? "Будет отформатирован только выбранный раздел." : "ВСЕ ДАННЫЕ НА ДИСКЕ БУДУТ УНИЧТОЖЕНЫ.");
            sb.Append("  Перед стартом программа попросит подтверждение.");
            return sb.ToString();
        }

        void UI(Action a)
        {
            if (IsDisposed) return;
            try { if (InvokeRequired) BeginInvoke(a); else a(); } catch { }
        }

        void Log(string s)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + s;
            try { File.AppendAllText(logFile, line + Environment.NewLine, Encoding.UTF8); } catch { }
            UI(delegate
            {
                txtLog.AppendText(line + Environment.NewLine);
            });
        }

        void SetStatus(string s) { UI(delegate { lblStatus.Text = s; }); Log(s); }
        void SetProgress(int pct) { UI(delegate { progress.Value = Math.Max(0, Math.Min(100, pct)); }); }

        void SetBusy(bool b)
        {
            busy = b;
            cmbSource.Enabled = !b; btnBrowse.Enabled = !b; btnScan.Enabled = !b;
            btnInstall.Enabled = !b && !installing;
            UseWaitCursor = b;
            UpdateNav();
        }
    }
}
