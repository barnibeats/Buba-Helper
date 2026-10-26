// Главная форма: интеграция файлов ответов в ISO (корень диска) и в WIM (Windows\Panther).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WinInstaller
{
    partial class MainForm
    {
        static readonly string[] IntegrateSteps = { "Образ", "Файлы ответов", "Запуск" };

        RadioButton rbModeIntegrate;
        TabControl tabIntegrate;
        TextBox txtIntSrc, txtIntXml1, txtIntXml2, txtIntCmd, txtIntOut, txtIntLabel, txtIntWork;
        Button btnIntSrc, btnIntXml1, btnIntXml2, btnIntCmd, btnIntOut, btnIntWork, btnIntGen;
        ComboBox cmbIntDest1, cmbIntDest2;
        RadioButton rbIntAll, rbIntSel;
        ListView lvIntEd;
        CheckBox chkIntNoPrompt, chkIntFolder;
        Label lblIntSrc, lblIntOutCap, lblIntSummary;
        List<ImageInfo> intImages;
        string intLoadedSrc;
        bool intLoading, intEsd, intIsWim, intAutoOut = true;

        static readonly string[] IntDestIso = { "Не использовать", "В корень ISO как Autounattend.xml", "Внутрь WIM (Windows\\Panther\\Unattend)", "В корень ISO и внутрь WIM" };
        static readonly string[] IntDestWim = { "Не использовать", "Внутрь WIM (Windows\\Panther\\Unattend)" };

        void BuildIntegrateUi()
        {
            TabPage pgSrc = AddPage(tabIntegrate, "Образ");
            TabPage pgFiles = AddPage(tabIntegrate, "Файлы ответов");
            TabPage pgRun = AddPage(tabIntegrate, "Запуск");

            // 1. образ и редакции
            Head(pgSrc, "ISO или WIM", "Куда будут вшиты файлы ответов: в установочный ISO или прямо в WIM.");
            UiCard c1 = NewCard(pgSrc, "Источник", 160);
            Cap(c1, "Файл образа (ISO или WIM)", 18, 52);
            txtIntSrc = new TextBox();
            Input(c1, txtIntSrc, 18, 74, 500, 18 + 112 + 8);
            btnIntSrc = Btn(c1, "Обзор…", "folder", 0, 74, 112, 32, 18);
            lblIntSrc = Hint(c1, "", 18, 118, 600, 32, 18);
            Gap(pgSrc, 10);
            UiCard c2 = NewCard(pgSrc, "Редакции WIM (для файла «внутрь WIM»)", 330);
            rbIntAll = Add(c2, new UiRadio { Text = "Во все редакции", Checked = true }, 18, 52, 240, 24);
            rbIntSel = Add(c2, new UiRadio { Text = "Только в выбранные (Ctrl/Shift - несколько)" }, 270, 52, 460, 24);
            lvIntEd = Add(c2, new UiList(), 18, 90, 600, 170);
            lvIntEd.MultiSelect = true;
            lvIntEd.Columns.Add("№", 52);
            lvIntEd.Columns.Add("Редакция", 480);
            lvIntEd.Columns.Add("Размер", 110);
            Fluid.Stretch(lvIntEd, 18);
            Hint(c2, "Файл в корне ISO общий для всех редакций. Внутрь WIM он кладётся в каждую выбранную редакцию отдельно "
                + "(Windows\\Panther\\Unattend\\Unattend.xml и Windows\\Panther\\unattend.xml), поэтому чем больше редакций, тем дольше работа.", 18, 270, 600, 50, 18);
            FinishPage(pgSrc);

            // 2. файлы и места назначения
            Head(pgFiles, "Файлы ответов", "Для каждого файла выберите, куда его поместить: в корень ISO, внутрь WIM или туда и туда.");
            UiCard f1 = NewCard(pgFiles, "Файл 1 (обычно Autounattend.xml - чистая установка)", 190);
            Cap(f1, "Путь к XML (пусто = не использовать)", 18, 52);
            txtIntXml1 = new TextBox();
            Input(f1, txtIntXml1, 18, 74, 500, 18 + 112 + 8);
            btnIntXml1 = Btn(f1, "Обзор…", "folder", 0, 74, 112, 32, 18);
            Cap(f1, "Куда поместить", 18, 122);
            cmbIntDest1 = Add(f1, new UiCombo(), 18, 144, 460, 32);
            Gap(pgFiles, 10);
            UiCard f2 = NewCard(pgFiles, "Файл 2 (обычно unattend.xml - образ / Sysprep)", 190);
            Cap(f2, "Путь к XML (пусто = не использовать)", 18, 52);
            txtIntXml2 = new TextBox();
            Input(f2, txtIntXml2, 18, 74, 500, 18 + 112 + 8);
            btnIntXml2 = Btn(f2, "Обзор…", "folder", 0, 74, 112, 32, 18);
            Cap(f2, "Куда поместить", 18, 122);
            cmbIntDest2 = Add(f2, new UiCombo(), 18, 144, 460, 32);
            Gap(pgFiles, 10);
            UiCard fc = NewCard(pgFiles, "Start-Sysprep.cmd (необязательно, только внутрь WIM)", 190);
            Cap(fc, "Путь к Start-Sysprep.cmd (пусто = не добавлять)", 18, 52);
            txtIntCmd = new TextBox();
            Input(fc, txtIntCmd, 18, 74, 500, 18 + 112 + 8);
            btnIntCmd = Btn(fc, "Обзор…", "folder", 0, 74, 112, 32, 18);
            Hint(fc, "Кладётся в Windows\\System32\\Sysprep внутри WIM, рядом с unattend.xml. Если файл ответов для Sysprep (без этапа windowsPE) идёт внутрь WIM, "
                + "он дополнительно копируется туда же как unattend.xml - его читает Start-Sysprep.cmd.", 18, 122, 600, 56, 18);
            Gap(pgFiles, 10);
            UiCard f3 = NewCard(pgFiles, "Нет файла?", 120);
            btnIntGen = Btn(f3, "Создать в генераторе…", "script", 18, 52, 250, 36, -1);
            Hint(f3, "В генераторе кнопка «В ISO / WIM» подставит готовый XML сюда.", 284, 60, 400, 24, 18);
            FinishPage(pgFiles);
            FillIntDest(false);

            // 3. результат и запуск
            Head(pgRun, "Запуск", "Куда сохранить результат. Исходный образ не изменяется (кроме случая «WIM поверх самого себя»).");
            UiCard r1 = NewCard(pgRun, "Результат", 372);
            lblIntOutCap = Cap(r1, "Сохранить ISO как", 18, 52);
            txtIntOut = new TextBox();
            Input(r1, txtIntOut, 18, 74, 500, 18 + 112 + 8);
            btnIntOut = Btn(r1, "Обзор…", "folder", 0, 74, 112, 32, 18);
            Cap(r1, "Метка тома ISO (пусто = как у исходного)", 18, 122);
            txtIntLabel = new TextBox();
            Input(r1, txtIntLabel, 18, 144, 320, -1);
            chkIntNoPrompt = Add(r1, new UiCheck { Text = "UEFI: без запроса «Нажмите любую клавишу» при загрузке с DVD", Checked = true }, 18, 192, 640, 24);
            chkIntFolder = Add(r1, new UiCheck { Text = "Не собирать ISO - оставить подготовленную папку (результат = папка)" }, 18, 224, 640, 24);
            Cap(r1, "Рабочая папка (нужно места примерно с размер ISO; пусто = рядом с результатом)", 18, 264);
            txtIntWork = new TextBox();
            Input(r1, txtIntWork, 18, 286, 500, 18 + 112 + 8);
            btnIntWork = Btn(r1, "Обзор…", "folder", 0, 286, 112, 32, 18);
            Gap(pgRun, 10);
            UiCard r2 = NewCard(pgRun, "Сводка", 280);
            lblIntSummary = Add(r2, new Label(), 18, 52, 600, 214);
            lblIntSummary.Font = Theme.Mono; lblIntSummary.ForeColor = Theme.Text;
            Fluid.Stretch(lblIntSummary, 18);
            FinishPage(pgRun);

            // события
            btnIntSrc.Click += delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Filter = "Образы Windows (*.iso;*.wim)|*.iso;*.wim|Все файлы|*.*";
                    if (ofd.ShowDialog(this) != DialogResult.OK) return;
                    txtIntSrc.Text = ofd.FileName;
                    LoadIntSource(ofd.FileName);
                }
            };
            txtIntSrc.Leave += delegate { LoadIntSource(txtIntSrc.Text); };
            txtIntSrc.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; LoadIntSource(txtIntSrc.Text); } };
            rbIntSel.CheckedChanged += delegate { UpdateNav(); };
            lvIntEd.SelectedIndexChanged += delegate { if (lvIntEd.SelectedItems.Count > 0) rbIntSel.Checked = true; UpdateNav(); };   // выбор строки = «только выбранные»
            btnIntXml1.Click += delegate { BrowseXml(txtIntXml1); };
            btnIntXml2.Click += delegate { BrowseXml(txtIntXml2); };
            btnIntCmd.Click += delegate
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Filter = "Start-Sysprep.cmd (*.cmd)|*.cmd|Все файлы|*.*";
                    if (ofd.ShowDialog(this) == DialogResult.OK) txtIntCmd.Text = ofd.FileName;
                }
            };
            txtIntCmd.TextChanged += delegate { UpdateNav(); };
            txtIntXml1.TextChanged += delegate { UpdateNav(); };
            txtIntXml2.TextChanged += delegate { UpdateNav(); };
            cmbIntDest1.SelectedIndexChanged += delegate { UpdateNav(); };
            cmbIntDest2.SelectedIndexChanged += delegate { UpdateNav(); };
            btnIntGen.Click += delegate { rbModeXml.Checked = true; };
            txtIntOut.TextChanged += delegate { if (txtIntOut.Focused) intAutoOut = false; UpdateNav(); };
            chkIntFolder.CheckedChanged += delegate { ApplyIntFolderMode(); };
            btnIntOut.Click += delegate
            {
                if (chkIntFolder.Checked)
                {
                    using (FolderBrowserDialog fb = new FolderBrowserDialog())
                    {
                        fb.Description = "Папка для содержимого ISO (должна быть пустой)";
                        if (fb.ShowDialog(this) == DialogResult.OK) { txtIntOut.Text = fb.SelectedPath; intAutoOut = false; }
                    }
                    return;
                }
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    string ext = intIsWim ? "wim" : "iso";
                    sfd.Filter = intIsWim ? "WIM-образ (*.wim)|*.wim" : "ISO-образ (*.iso)|*.iso"; sfd.DefaultExt = ext; sfd.OverwritePrompt = false;
                    if (txtIntOut.Text.Length > 3) { try { sfd.InitialDirectory = Path.GetDirectoryName(txtIntOut.Text); sfd.FileName = Path.GetFileName(txtIntOut.Text); } catch { } }
                    if (sfd.ShowDialog(this) == DialogResult.OK) { txtIntOut.Text = sfd.FileName; intAutoOut = false; }
                }
            };
            btnIntWork.Click += delegate
            {
                using (FolderBrowserDialog fb = new FolderBrowserDialog())
                    if (fb.ShowDialog(this) == DialogResult.OK) txtIntWork.Text = fb.SelectedPath;
            };
        }

        void BrowseXml(TextBox target)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "XML (*.xml)|*.xml|Все файлы|*.*";
                if (ofd.ShowDialog(this) == DialogResult.OK) target.Text = ofd.FileName;
            }
        }

        // ------------------------------------------------------------ места назначения

        static void DestFlags(ComboBox c, bool wimSource, out bool root, out bool wim)
        {
            int i = c.SelectedIndex;
            if (wimSource) { root = false; wim = i == 1; return; }
            root = i == 1 || i == 3; wim = i == 2 || i == 3;
        }

        static int DestIndex(bool wimSource, bool root, bool wim)
        {
            if (wimSource) return root || wim ? 1 : 0;
            return root && wim ? 3 : root ? 1 : wim ? 2 : 0;
        }

        /// <summary>Наполняет списки «Куда поместить» под тип источника, сохраняя прежний выбор (по умолчанию: файл 1 в корень ISO, файл 2 в WIM).</summary>
        void FillIntDest(bool wimSource)
        {
            ComboBox[] cs = { cmbIntDest1, cmbIntDest2 };
            bool[] roots = new bool[2], wims = new bool[2];
            for (int k = 0; k < 2; k++)
            {
                if (cs[k].Items.Count == 0) { roots[k] = k == 0; wims[k] = k == 1; }
                else DestFlags(cs[k], intIsWim, out roots[k], out wims[k]);
            }
            for (int k = 0; k < 2; k++)
            {
                cs[k].Items.Clear();
                cs[k].Items.AddRange(wimSource ? IntDestWim : IntDestIso);
                cs[k].SelectedIndex = DestIndex(wimSource, roots[k], wims[k]);
            }
            intIsWim = wimSource;
        }

        // ------------------------------------------------------------ источник

        void LoadIntSource(string path)
        {
            path = (path ?? "").Trim().Trim('"');
            if (path == intLoadedSrc && (intImages != null || intLoading)) return;
            intImages = null; intEsd = false; intLoadedSrc = path;
            lvIntEd.Items.Clear();
            if (path.Length == 0) { lblIntSrc.Text = ""; UpdateNav(); return; }
            if (!File.Exists(path)) { lblIntSrc.Text = "Файл не найден: " + path; UpdateNav(); return; }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".iso" && ext != ".wim") { lblIntSrc.Text = "Нужен файл .iso или .wim (ESD изменять нельзя)."; UpdateNav(); return; }
            bool isIso = ext == ".iso";
            if (isIso == intIsWim) { FillIntDest(!isIso); ApplyIntFolderMode(); }
            intLoading = true;
            lblIntSrc.ForeColor = Theme.Muted;
            lblIntSrc.Text = "Чтение образа...";
            UpdateNav();
            Thread t = new Thread(delegate()
            {
                List<ImageInfo> list = null;
                string err = null, label = null;
                bool hasAuto = false, esd = false;
                try
                {
                    if (isIso)
                    {
                        IsoMount m = new IsoMount();
                        string e;
                        if (!m.Mount(path, out e)) throw new Exception("не удалось подключить ISO: " + e);
                        try
                        {
                            try { label = new DriveInfo(m.Letter).VolumeLabel; } catch { }
                            hasAuto = File.Exists(m.Letter + "\\" + IsoIntegrator.RootName);
                            string wim = m.Letter + "\\sources\\install.wim";
                            if (!File.Exists(wim))
                            {
                                wim = m.Letter + "\\sources\\install.esd";
                                if (!File.Exists(wim)) throw new Exception("в ISO нет sources\\install.wim (это не установочный образ Windows?)");
                                esd = true;
                            }
                            list = ReadImages(wim);
                        }
                        finally { m.Dismount(); }
                    }
                    else list = ReadImages(path);
                }
                catch (Exception ex) { err = ex.Message; }
                UI(delegate
                {
                    if (path != intLoadedSrc) return;   // за время чтения выбран другой файл
                    intLoading = false;
                    if (err != null || list == null || list.Count == 0)
                    {
                        lblIntSrc.ForeColor = Theme.Danger;
                        lblIntSrc.Text = "Ошибка: " + (err ?? "в образе не найдено ни одной редакции");
                        intLoadedSrc = null;
                        UpdateNav();
                        return;
                    }
                    intImages = list; intEsd = esd;
                    foreach (ImageInfo im in list)
                    {
                        ListViewItem it = new ListViewItem(im.Index.ToString());
                        it.SubItems.Add(im.Name);
                        it.SubItems.Add(im.Bytes > 0 ? Util.FmtSize(im.Bytes) : "");
                        it.Tag = im;
                        lvIntEd.Items.Add(it);
                    }
                    lblIntSrc.ForeColor = Theme.Muted;
                    lblIntSrc.Text = (isIso ? "ISO" : "WIM") + ", редакций: " + list.Count + (label != null && label.Length > 0 ? ", метка: " + label : "")
                        + (hasAuto ? ". В корне ISO уже есть Autounattend.xml - он будет заменён." : "")
                        + (esd ? ". Внутри install.esd: внутрь образа файл положить нельзя, только в корень ISO." : "");
                    if (label != null && txtIntLabel.Text.Length == 0) txtIntLabel.Text = label;
                    if (intAutoOut || txtIntOut.Text.Trim().Length == 0) { txtIntOut.Text = DefaultIntOut(path); intAutoOut = true; }
                    Log("Интеграция: источник " + path + ", редакций: " + list.Count);
                    UpdateNav();
                });
            });
            t.IsBackground = true; t.Start();
        }

        string DefaultIntOut(string src)
        {
            try
            {
                string dir = Path.GetDirectoryName(src), name = Path.GetFileNameWithoutExtension(src) + "_unattend";
                return Path.Combine(dir, chkIntFolder.Checked ? name : name + (intIsWim ? ".wim" : ".iso"));
            }
            catch { return ""; }
        }

        /// <summary>Режим «только папка» доступен для ISO; для WIM результат всегда файл.</summary>
        void ApplyIntFolderMode()
        {
            if (intIsWim) chkIntFolder.Checked = false;
            chkIntFolder.Enabled = !intIsWim;
            chkIntNoPrompt.Enabled = !intIsWim && !chkIntFolder.Checked;
            txtIntLabel.Enabled = !intIsWim && !chkIntFolder.Checked;
            lblIntOutCap.Text = intIsWim ? "Сохранить WIM как (то же имя = изменить исходный WIM)" : chkIntFolder.Checked ? "Папка для содержимого ISO" : "Сохранить ISO как";
            if (intAutoOut && txtIntSrc.Text.Trim().Length > 0) txtIntOut.Text = DefaultIntOut(txtIntSrc.Text.Trim().Trim('"'));
            UpdateNav();
        }

        // ------------------------------------------------------------ план и проверки

        IntegratePlan BuildIntegratePlan()
        {
            IntegratePlan p = new IntegratePlan();
            p.Source = txtIntSrc.Text.Trim().Trim('"');
            p.Out = txtIntOut.Text.Trim().Trim('"');
            p.Label = txtIntLabel.Text.Trim().Length > 0 ? txtIntLabel.Text.Trim() : null;
            p.WorkDir = txtIntWork.Text.Trim().Trim('"');
            p.NoPrompt = chkIntNoPrompt.Checked;
            p.FolderOnly = chkIntFolder.Checked && !intIsWim;
            p.DryRun = Program.DryRun;
            TextBox[] xs = { txtIntXml1, txtIntXml2 };
            ComboBox[] cs = { cmbIntDest1, cmbIntDest2 };
            for (int k = 0; k < 2; k++)
            {
                string x = xs[k].Text.Trim().Trim('"');
                if (x.Length == 0) continue;
                AnswerFile f = new AnswerFile { Xml = x };
                DestFlags(cs[k], intIsWim, out f.ToIsoRoot, out f.ToWim);
                if (!f.ToIsoRoot && !f.ToWim) continue;
                p.Files.Add(f);
            }
            string cmd = txtIntCmd.Text.Trim().Trim('"');
            if (cmd.Length > 0) p.SysprepFiles.Add(cmd);
            if (rbIntSel.Checked)
            {
                p.Indexes = new List<int>();
                foreach (ListViewItem it in lvIntEd.SelectedItems) p.Indexes.Add(((ImageInfo)it.Tag).Index);
            }
            return p;
        }

        string IntegrateProblem(int step)
        {
            if (step == 0)
            {
                if (intLoading) return "Идёт чтение образа...";
                if (intImages == null) return "Выберите образ ISO или WIM.";
                if (rbIntSel.Checked && lvIntEd.SelectedItems.Count == 0) return "Выберите хотя бы одну редакцию или включите «Во все редакции».";
                return null;
            }
            IntegratePlan p = BuildIntegratePlan();
            if (step == 1)
            {
                if (p.Files.Count == 0) return "Укажите файл ответов и место, куда его поместить.";
                foreach (AnswerFile f in p.Files)
                    if (!File.Exists(f.Xml)) return "Файл не найден: " + f.Xml;
                foreach (string sf in p.SysprepFiles)
                    if (!File.Exists(sf)) return "Файл не найден: " + sf;
                if (intEsd && p.AnyWim) return "В ISO install.esd: внутрь образа файл не положить. Выберите «В корень ISO».";
                return null;
            }
            if (p.Out.Length == 0) return "Укажите, куда сохранить результат.";
            return null;
        }

        string BuildIntegrateSummary()
        {
            IntegratePlan p = BuildIntegratePlan();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(KV("Источник", p.Source + (intIsWim ? "  (WIM)" : "  (ISO)")));
            foreach (AnswerFile f in p.Files)
                sb.AppendLine(KV("Файл", Path.GetFileName(f.Xml) + "  ->  " + (f.ToIsoRoot ? "корень ISO" : "") + (f.ToIsoRoot && f.ToWim ? " + " : "") + (f.ToWim ? "внутрь WIM" : "")));
            foreach (string sf in p.SysprepFiles)
                sb.AppendLine(KV("Файл", Path.GetFileName(sf) + "  ->  внутрь WIM, Windows\\System32\\Sysprep"));
            if (p.AnyWim)
            {
                int n = p.Indexes != null ? p.Indexes.Count : (intImages == null ? 0 : intImages.Count);
                sb.AppendLine(KV("Редакции WIM", p.Indexes == null ? "все (" + n + ")" : p.Indexes.Count + " выбрано"));
            }
            sb.AppendLine(KV(p.FolderOnly ? "Папка" : intIsWim ? "WIM" : "ISO", p.Out));
            if (!intIsWim && !p.FolderOnly)
                sb.AppendLine(KV("Загрузка", p.NoPrompt ? "UEFI без запроса «любая клавиша»" : "UEFI с запросом «любая клавиша»") + ";  BIOS - да");
            if (intIsWim && string.Equals(p.Source, p.Out, StringComparison.OrdinalIgnoreCase))
                sb.AppendLine().AppendLine("ВНИМАНИЕ: исходный WIM будет изменён.");
            return sb.ToString().TrimEnd();
        }

        // ------------------------------------------------------------ запуск

        void StartIntegrate()
        {
            if (installing) return;
            IntegratePlan p = BuildIntegratePlan();
            string err = IsoIntegrator.Validate(p);
            if (err != null) { MessageBox.Show(this, err, "Интеграция", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            bool exists = p.FolderOnly ? Directory.Exists(p.Out) && Directory.GetFileSystemEntries(p.Out).Length > 0 : File.Exists(p.Out);
            bool inPlace = !p.IsIso && string.Equals(Path.GetFullPath(p.Source), Path.GetFullPath(p.Out), StringComparison.OrdinalIgnoreCase);
            string msg = BuildIntegrateSummary() + (p.DryRun ? "\n\n[DRY-RUN - реально ничего не будет сделано]" : "");
            if (inPlace) msg += "\n\nИсходный WIM будет изменён прямо в файле. Рекомендуется сохранить копию.";
            else if (exists) msg += "\n\nФайл уже существует и будет перезаписан.";
            msg += "\n\nНачать?";
            if (MessageBox.Show(this, msg, "Подтверждение", MessageBoxButtons.YesNo, inPlace || exists ? MessageBoxIcon.Warning : MessageBoxIcon.Question, inPlace || exists ? MessageBoxDefaultButton.Button2 : MessageBoxDefaultButton.Button1) != DialogResult.Yes) return;
            SetWorking(true);
            Thread t = new Thread(delegate() { RunIntegrate(p); });
            t.IsBackground = true; t.Start();
        }

        void RunIntegrate(IntegratePlan p)
        {
            try
            {
                UI(delegate { lblStatus.ForeColor = Theme.Text; });
                IsoIntegrator ig = new IsoIntegrator();
                ig.Log = Log;
                ig.Progress = delegate(int pc, string text) { SetProgress(pc); UI(delegate { lblStatus.Text = text; }); };
                string err;
                if (!ig.Run(p, out err)) { Fail(err); return; }
                Beep(true);
                SetStatus("ГОТОВО. " + p.Out);
                UI(delegate
                {
                    MessageBox.Show(this, p.DryRun ? "DRY-RUN завершён: команды смотрите в логе." : "Файлы ответов вшиты.\n" + p.Out, "Готово");
                    FinishUi();
                });
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        /// <summary>Генератор отдал готовый XML: для Sysprep (unattend.xml) - в файл 2, для чистой установки (autounattend.xml) - в файл 1.</summary>
        void OnUseXmlIso(string path, bool isSys, string cmdPath)
        {
            (isSys ? txtIntXml2 : txtIntXml1).Text = path;
            if (cmdPath != null) txtIntCmd.Text = cmdPath;
            rbModeIntegrate.Checked = true;
            tabIntegrate.SelectedIndex = 1;
            Log("Файл ответов из генератора выбран для интеграции в ISO/WIM: " + path);
        }
    }
}
