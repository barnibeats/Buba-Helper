// Главная форма: ремонт загрузчика Windows 7/8/10/11 (UEFI/GPT и BIOS/MBR). Данные на разделах не стираются:
// меняются только загрузочные файлы, BCD, MBR/загрузочный сектор и флаг «активный».
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WinInstaller
{
    /// <summary>Найденная установка Windows.</summary>
    class SysInfo
    {
        public char Letter;
        public int Disk = -1;       // -1 - том на нескольких дисках (динамический)
        public int PartNo;
        public string Name = "Windows", Version = "";
        public bool X64;
        public int Build;           // сборка ntoskrnl: 7601 = Windows 7 SP1, 9200 = 8, 19045 = 10, 22000+ = 11
        public long Size;
        public string Label = "";
        public bool Current;        // система, из которой запущена программа
    }

    class RepairPlan
    {
        public SysInfo Sys;
        public bool Gpt;
        public int BootPart;        // номер раздела, куда пишется загрузчик
        public char BootLetter;     // ' ' - буквы нет, будет временная
        public bool Rebuild, KeepBak, Fallback, WriteMbr, SetActive, MakeEsp;
        public bool RecreateEsp;    // сначала удалить существующий EFI-раздел
        public int OldEspPart;      // его номер
        public SpaceChoice Space;   // откуда взять место под новый EFI-раздел
        public TempChoice Temp;     // (Space.Reorder) где хранить копию системы на время переноса
        public bool DryRun;
    }

    /// <summary>Где хранить WIM-копию системы на время переноса: существующий том (Letter) или временный раздел (Offset/Size).</summary>
    class TempChoice
    {
        public string Text;
        public char Letter = ' ';
        public long Offset, Size;
        public override string ToString() { return Text; }
    }

    /// <summary>Перенос системы в начало диска: EFI -> Windows -> остальное. Windows сохраняется в WIM, разделы пересоздаются, WIM разворачивается обратно.</summary>
    class ReorderInfo
    {
        public PartInfo Win;
        public List<PartInfo> Delete = new List<PartInfo>();    // что удаляется: Windows, выбранный EFI, MSR в зоне переноса
        public long RegionStart, RegionEnd;                     // куда встанут новый EFI (300 МБ) и Windows
        public long UsedBytes;
        public string WinLabel = "Windows";
        public char WinLetter;
        public List<TempChoice> Temps = new List<TempChoice>();
    }

    /// <summary>Откуда взять 300 МБ под новый EFI-раздел: свободный участок диска или уменьшение раздела.</summary>
    class SpaceChoice
    {
        public string Text;
        public bool Shrink;         // true - уменьшить раздел Letter
        public char Letter;
        public bool AtStart;        // участок начинается с начала диска (EFI встанет первым)
        public ReorderInfo Reorder; // не null - перенос системы в начало диска (остальные поля не используются)
        public bool GapFront;       // ставить в начало участка (сразу за предыдущим разделом), а не в его конец
        public string Where, Source;   // для сводки: «в середине диска, сразу после C:» и «за счёт свободного места»
        public long Anchor;         // байт внутри нужного участка (после удаления/уменьшения участок сливается с соседними, ищем по нему)
        public long GapStart, GapEnd;
        public override string ToString() { return Text; }
    }

    partial class MainForm
    {
        static readonly string[] RepairSteps = { "Система", "Раздел загрузчика", "Параметры и запуск" };

        RadioButton rbModeRepair;
        TabControl tabRepair;
        ListView lvSys, lvBoot;
        Button btnSysRefresh;
        Label lblSysHint, lblRepDiag, lblBootHint, lblRepSummary;
        CheckBox chkRepRebuild, chkRepKeepBak, chkRepFallback, chkRepMbr, chkRepActive, chkRepMakeEsp;
        int repDisk = -2;                          // для какого диска заполнен список разделов
        bool repGpt, repEspMissing, repEspExists;  // выбранный диск: GPT; на нём нет EFI-раздела; есть
        int repSpaceIgnore = -2;                   // номер EFI-раздела, учтённого как удалённый в списке «откуда взять место»
        ComboBox cmbRepSpace, cmbRepTemp;
        Label capRepSpace, lblRepSpaceHint, capRepTemp;
        PartInfo repDefault;                    // раздел загрузчика по умолчанию (пока страница не показана, ListView выбор не хранит)

        // ------------------------------------------------------------ интерфейс

        void BuildRepairUi()
        {
            TabPage pgSys = AddPage(tabRepair, "Система");
            TabPage pgBoot = AddPage(tabRepair, "Раздел загрузчика");
            TabPage pgRun = AddPage(tabRepair, "Параметры и запуск");

            // 1. система
            Head(pgSys, "Какую Windows чинить", "Выберите установленную Windows (7, 8, 10, 11). Диск и вид загрузки (UEFI/BIOS) определяются по её разделу.");
            UiCard c1 = NewCard(pgSys, "Найденные системы", 252);
            lvSys = Add(c1, new UiList(), 18, 52, 600, 136);
            lvSys.Columns.Add("Раздел", 64);
            lvSys.Columns.Add("Диск", 48);
            lvSys.Columns.Add("Таблица", 70);
            lvSys.Columns.Add("Windows", 118);
            lvSys.Columns.Add("Бит", 46);
            lvSys.Columns.Add("Размер", 80);
            lvSys.Columns.Add("Метка", 100);
            lvSys.Columns.Add("Примечание", 150);
            Fluid.Stretch(lvSys, 18);
            btnSysRefresh = Btn(c1, "Обновить", "refresh", 18, 200, 122, 34, -1);
            lblSysHint = Hint(c1, "Раздел без буквы или зашифрованный BitLocker в списке не виден. В WinPE все разделы с файловой системой обычно получают буквы сами.", 154, 198, 460, 40, 18);
            Gap(pgSys, 14);
            UiCard c2 = NewCard(pgSys, "Диагностика", 232);
            lblRepDiag = Add(c2, new Label(), 18, 52, 600, 168);
            lblRepDiag.Font = Theme.Mono; lblRepDiag.ForeColor = Theme.Text;
            Fluid.Stretch(lblRepDiag, 18);
            FinishPage(pgSys);

            // 2. раздел загрузчика
            Head(pgBoot, "Куда записать загрузчик", "GPT (UEFI): EFI-раздел. MBR (BIOS): активный раздел, обычно «Зарезервировано системой». Подходящий выбран сам.");
            UiCard c3 = NewCard(pgBoot, "Разделы диска", 600);
            lvBoot = Add(c3, new UiList(), 18, 52, 600, 196);
            lvBoot.Columns.Add("№", 46);
            lvBoot.Columns.Add("Тип", 200);
            lvBoot.Columns.Add("Размер", 90);
            lvBoot.Columns.Add("Буква", 64);
            lvBoot.Columns.Add("Файловая система", 150);
            lvBoot.Columns.Add("Роль", 220);
            Fluid.Stretch(lvBoot, 18);
            lblBootHint = Hint(c3, "", 18, 256, 600, 20, 18);
            chkRepMakeEsp = Add(c3, new UiCheck { Text = "Создать EFI-раздел (300 МБ)" }, 18, 288, 700, 24);
            capRepSpace = Cap(c3, "Откуда взять 300 МБ", 18, 326);
            cmbRepSpace = Add(c3, new UiCombo(), 18, 348, 600, 32);
            Fluid.Stretch(cmbRepSpace, 18);
            capRepTemp = Cap(c3, "Где хранить копию системы на время переноса", 18, 392);
            cmbRepTemp = Add(c3, new UiCombo(), 18, 414, 600, 32);
            Fluid.Stretch(cmbRepTemp, 18);
            lblRepSpaceHint = Hint(c3, "", 18, 462, 600, 120, 18);
            FinishPage(pgBoot);

            // 3. параметры и запуск
            Head(pgRun, "Параметры и запуск", "Проверьте, что будет сделано. Перед стартом программа попросит подтверждение.");
            UiCard c4 = NewCard(pgRun, "Параметры", 290);
            chkRepRebuild = Add(c4, new UiCheck { Text = "Создать новый BCD (старый будет удалён)", Checked = true }, 18, 52, 700, 24);
            chkRepKeepBak = Add(c4, new UiCheck { Text = "Сохранить копию старого BCD рядом (BCD.bak_…)", Checked = true }, 46, 88, 672, 24);
            chkRepFallback = Add(c4, new UiCheck { Text = "Записать запасной загрузчик EFI\\Boot\\bootx64.efi (для ПК без записи в NVRAM)", Checked = true }, 18, 124, 700, 24);
            chkRepMbr = Add(c4, new UiCheck { Text = "Записать код загрузки в MBR диска (затрёт GRUB и другие загрузчики в MBR)", Checked = true }, 18, 160, 700, 24);
            chkRepActive = Add(c4, new UiCheck { Text = "Сделать раздел загрузчика активным", Checked = true }, 18, 196, 700, 24);
            Hint(c4, "Если BCD не создавать заново, запись этой Windows будет добавлена или обновлена в существующем BCD, остальные записи (мультизагрузка) останутся.", 18, 232, 700, 40, 18);
            Gap(pgRun, 14);
            UiCard c5 = NewCard(pgRun, "Что будет сделано", 300);
            lblRepSummary = Add(c5, new Label(), 18, 52, 600, 236);
            lblRepSummary.Font = Theme.Mono; lblRepSummary.ForeColor = Theme.Text;
            Fluid.Stretch(lblRepSummary, 18);
            FinishPage(pgRun);

            btnSysRefresh.Click += delegate { RefreshSystems(); };
            lvSys.SelectedIndexChanged += delegate { RepairSystemChanged(); };
            lvBoot.SelectedIndexChanged += delegate
            {
                PartInfo old = repEspExists && chkRepMakeEsp.Checked ? OldEsp() : null;
                if ((old == null ? -1 : old.Number) != repSpaceIgnore) FillSpaceChoices();     // выбран другой EFI-раздел для удаления
                UpdateNav();
            };
            chkRepMakeEsp.CheckedChanged += delegate { FillSpaceChoices(); UpdateSpaceVisibility(); UpdateNav(); };
            cmbRepSpace.SelectedIndexChanged += delegate { UpdateTempChoices(); UpdateSpaceVisibility(); UpdateNav(); };
            cmbRepTemp.SelectedIndexChanged += delegate { UpdateNav(); };
            foreach (CheckBox c in new CheckBox[] { chkRepRebuild, chkRepKeepBak, chkRepFallback, chkRepMbr, chkRepActive })
                c.CheckedChanged += delegate { UpdateNav(); };
            chkRepRebuild.CheckedChanged += delegate { chkRepKeepBak.Enabled = chkRepRebuild.Checked; };
            UpdateRepairOptions();
        }

        // ------------------------------------------------------------ поиск систем

        /// <summary>Название по номеру сборки ntoskrnl (старшая/младшая версии в файле у Windows 10 показывают 6.2, им верить нельзя).</summary>
        static string WinName(int build)
        {
            if (build >= 22000) return "Windows 11";
            if (build >= 10240) return "Windows 10";
            if (build >= 9600) return "Windows 8.1";
            if (build >= 9200) return "Windows 8";
            if (build >= 7600) return "Windows 7";
            if (build >= 6000) return "Windows Vista";
            return "Windows (сборка " + build + ")";
        }

        void RefreshSystems()
        {
            if (lvSys == null) return;
            SysInfo prev = SelectedSys();
            lvSys.Items.Clear();
            char sysL = char.ToUpper((Environment.GetEnvironmentVariable("SystemDrive") ?? "X:")[0]);
            Dictionary<int, string> styles = new Dictionary<int, string>();
            try { foreach (DiskInfo di in Native.EnumDisks()) styles[di.Number] = di.Style; }
            catch (Exception ex) { Log("Ошибка чтения дисков: " + ex.Message); }
            int count = 0;
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                char c = char.ToUpper(d.Name[0]);
                if (c == 'A' || c == 'B' || (c == 'X' && sysL == 'X')) continue;      // X: - сама WinPE
                try
                {
                    string krn = c + ":\\Windows\\System32\\ntoskrnl.exe";
                    if (!d.IsReady || !File.Exists(krn)) continue;
                    FileVersionInfo fv = FileVersionInfo.GetVersionInfo(krn);
                    if (fv.FileBuildPart < 6000) continue;                             // XP и старше грузятся через boot.ini
                    SysInfo s = new SysInfo();
                    s.Letter = c; s.Size = d.TotalSize; s.Label = d.VolumeLabel;
                    s.Build = fv.FileBuildPart;
                    s.Name = WinName(s.Build);
                    s.Version = "сборка " + s.Build;
                    s.X64 = Directory.Exists(c + ":\\Windows\\SysWOW64");
                    s.Current = c == sysL && sysL != 'X';
                    List<int> dn = Native.DisksOfVolume(c);
                    string note = "";
                    if (dn.Count == 1) { s.Disk = dn[0]; s.PartNo = Native.PartitionNumberOfVolume(c); }
                    else note = "динамический том - не поддерживается";
                    if (s.Current) note = "текущая система";
                    string st; if (s.Disk < 0 || !styles.TryGetValue(s.Disk, out st)) st = "?";
                    ListViewItem it = new ListViewItem(new string[] { c + ":", s.Disk < 0 ? "?" : s.Disk.ToString(), st, s.Name, s.X64 ? "x64" : "x86", Util.FmtSize(s.Size), s.Label, note });
                    it.Tag = s;
                    lvSys.Items.Add(it);
                    count++;
                }
                catch { }
            }
            lblSysHint.Text = count == 0
                ? "Установленных Windows не найдено. Раздел может быть без буквы или зашифрован BitLocker."
                : "Раздел без буквы или зашифрованный BitLocker в списке не виден. В WinPE все разделы с файловой системой обычно получают буквы сами.";
            foreach (ListViewItem it in lvSys.Items)
                if (count == 1 || (prev != null && ((SysInfo)it.Tag).Letter == prev.Letter)) { it.Selected = true; return; }
            RepairSystemChanged();
        }

        SysInfo SelectedSys()
        {
            if (lvSys == null || lvSys.SelectedItems.Count == 0) return null;
            return (SysInfo)lvSys.SelectedItems[0].Tag;
        }

        void RepairSystemChanged()
        {
            FillBootParts();
            UpdateRepairOptions();
            lblRepDiag.Text = BuildRepairDiag();
            UpdateNav();
        }

        // ------------------------------------------------------------ разделы диска

        static string PartType(PartInfo p)
        {
            if (p.Gpt)
            {
                if (p.TypeGuid == Native.GuidEsp) return "EFI-раздел (ESP)";
                if (p.TypeGuid == Native.GuidMsr) return "MSR (резерв Microsoft)";
                if (p.TypeGuid == Native.GuidRecovery) return "Среда восстановления";
                if (p.TypeGuid == Native.GuidBasic) return "Основной";
                return "GPT " + p.TypeGuid.ToString().Substring(0, 8);
            }
            switch (p.MbrType)
            {
                case 0x07: return "NTFS / exFAT";
                case 0x27: return "Скрытый NTFS (восстановление)";
                case 0x0B: case 0x0C: return "FAT32";
                case 0x04: case 0x06: case 0x0E: return "FAT16";
                case 0x83: return "Linux";
                case 0x82: return "Linux swap";
                default: return "Тип 0x" + p.MbrType.ToString("X2");
            }
        }

        /// <summary>Раздел для загрузчика по умолчанию: ESP для GPT, активный раздел для MBR, иначе сам раздел Windows.</summary>
        static PartInfo DefaultBootPart(List<PartInfo> parts, SysInfo s)
        {
            foreach (PartInfo p in parts) if (p.IsEsp) return p;
            if (parts.Count > 0 && parts[0].Gpt) return null;      // на GPT без EFI-раздела молча подставлять нечего: нужно создать его
            foreach (PartInfo p in parts) if (!p.Gpt && p.Active) return p;
            foreach (PartInfo p in parts) if (p.Number == s.PartNo) return p;
            return null;
        }

        void FillBootParts()
        {
            lvBoot.Items.Clear();
            repDefault = null;
            SysInfo s = SelectedSys();
            repDisk = s == null ? -2 : s.Disk;
            if (s == null || s.Disk < 0) { lblBootHint.Text = ""; return; }
            List<PartInfo> parts = Native.Partitions(s.Disk);
            PartInfo def = DefaultBootPart(parts, s);
            repDefault = def;
            foreach (PartInfo p in parts)
            {
                string letters = p.Letters.Count > 0 ? string.Join(" ", p.Letters.ConvertAll<string>(delegate(char c) { return c + ":"; }).ToArray()) : "-";
                string fs = "";
                if (p.Letters.Count > 0)
                    try { DriveInfo di = new DriveInfo(p.Letters[0].ToString()); if (di.IsReady) fs = di.DriveFormat + (di.VolumeLabel.Length > 0 ? ", " + di.VolumeLabel : ""); } catch { }
                List<string> role = new List<string>();
                if (p.Number == s.PartNo) role.Add("Windows");
                if (!p.Gpt && p.Active) role.Add("активный");
                if (p == def) role.Add("← загрузчик");
                ListViewItem it = new ListViewItem(new string[] { p.Number.ToString(), PartType(p), Util.FmtSize(p.Length), letters, fs, string.Join(", ", role.ToArray()) });
                it.Tag = p;
                if (p.IsMsr) it.ForeColor = Theme.Muted;
                lvBoot.Items.Add(it);
                if (p == def) it.Selected = true;
            }
            bool gpt = parts.Count > 0 && parts[0].Gpt;
            lblBootHint.Text = parts.Count == 0 ? "Не удалось прочитать таблицу разделов диска (нужны права администратора)."
                : gpt ? "Диск GPT: загрузчик пишется в EFI-раздел (ESP)."
                : "Диск MBR: загрузчик пишется в активный раздел. Если активного нет, выбран раздел Windows и он станет активным.";
        }

        PartInfo RepairBootPart()
        {
            if (lvBoot == null) return null;
            if (lvBoot.SelectedItems.Count == 0) return repDefault;
            return (PartInfo)lvBoot.SelectedItems[0].Tag;
        }

        bool DiskHasEsp()
        {
            foreach (ListViewItem it in lvBoot.Items) if (((PartInfo)it.Tag).IsEsp) return true;
            return false;
        }

        /// <summary>Показывает только те параметры, что относятся к виду загрузки выбранного диска.</summary>
        void UpdateRepairOptions()
        {
            bool have = lvBoot != null && lvBoot.Items.Count > 0;
            bool gpt = have && ((PartInfo)lvBoot.Items[0].Tag).Gpt;
            chkRepFallback.Visible = have && gpt;
            chkRepMbr.Visible = have && !gpt;
            chkRepActive.Visible = have && !gpt;
            repGpt = have && gpt;
            repEspExists = repGpt && DiskHasEsp();
            repEspMissing = repGpt && !repEspExists;
            chkRepMakeEsp.Visible = repGpt;
            chkRepMakeEsp.Text = repEspMissing ? "На диске нет EFI-раздела: создать его (300 МБ)"
                : "Пересоздать EFI-раздел в другом месте (старый EFI-раздел будет удалён)";
            chkRepMakeEsp.Checked = repEspMissing;          // без EFI-раздела на GPT-диске загрузчик писать некуда; с ним - по желанию
            FillSpaceChoices();
            UpdateSpaceVisibility();
        }

        /// <summary>Создавать ли новый EFI-раздел. Состояние хранится в полях, а не в Visible: у контрола на невидимой странице Visible всегда false.</summary>
        bool MakeEspWanted() { return repGpt && chkRepMakeEsp.Checked; }

        void UpdateSpaceVisibility()
        {
            bool v = MakeEspWanted();
            capRepSpace.Visible = cmbRepSpace.Visible = lblRepSpaceHint.Visible = v;
            capRepTemp.Visible = cmbRepTemp.Visible = v && CurrentReorder() != null;
        }

        ReorderInfo CurrentReorder()
        {
            SpaceChoice sp = cmbRepSpace == null ? null : cmbRepSpace.SelectedItem as SpaceChoice;
            return sp == null ? null : sp.Reorder;
        }

        /// <summary>Список «где хранить копию системы» заполняется, когда выбран вариант переноса.</summary>
        void UpdateTempChoices()
        {
            if (cmbRepTemp == null) return;
            cmbRepTemp.Items.Clear();
            ReorderInfo ro = CurrentReorder();
            if (ro == null) return;
            foreach (TempChoice t in ro.Temps) cmbRepTemp.Items.Add(t);
            if (cmbRepTemp.Items.Count > 0) cmbRepTemp.SelectedIndex = 0;
        }

        /// <summary>EFI-раздел, который будет удалён при пересоздании: выбранный в таблице, иначе первый.</summary>
        PartInfo OldEsp()
        {
            PartInfo bp = RepairBootPart();
            if (bp != null && bp.IsEsp) return bp;
            foreach (ListViewItem it in lvBoot.Items) if (((PartInfo)it.Tag).IsEsp) return (PartInfo)it.Tag;
            return null;
        }

        /// <summary>
        /// Варианты «откуда взять 300 МБ»: свободные участки диска и разделы NTFS, которые можно уменьшить. Новый раздел встаёт
        /// в самое начало диска, если свободный участок начинается там; иначе - в конец участка (так он не мешает расширять соседний раздел).
        /// </summary>
        void FillSpaceChoices()
        {
            if (cmbRepSpace == null) return;
            cmbRepSpace.Items.Clear();
            lblRepSpaceHint.Text = "";
            SysInfo s = SelectedSys();
            PartInfo old = repEspExists && chkRepMakeEsp.Checked ? OldEsp() : null;
            repSpaceIgnore = old == null ? -1 : old.Number;
            if (s == null || s.Disk < 0 || !repGpt || !chkRepMakeEsp.Checked) return;
            const long MB = 1048576, Esp = 300 * MB;
            long us, ue;
            if (!Native.GptUsable(s.Disk, out us, out ue)) { lblRepSpaceHint.Text = "Не удалось прочитать границы GPT-диска."; return; }
            List<PartInfo> parts = new List<PartInfo>();
            foreach (ListViewItem it in lvBoot.Items) parts.Add((PartInfo)it.Tag);
            long usAligned = (us + MB - 1) / MB * MB, ueAligned = ue / MB * MB;

            List<SpaceChoice> list = new List<SpaceChoice>();
            int pick = -1;
            long bestGap = 0;
            foreach (FreeSpan g in Native.FreeSpans(parts, old, us, ue))
            {
                if (g.Length < Esp) continue;
                bool atStart = g.Start <= usAligned, toDiskEnd = g.End >= ueAligned;
                PartInfo prev = null, next = null;
                foreach (PartInfo p in parts)
                {
                    if (old != null && p.Number == old.Number) continue;
                    if (Math.Abs(p.Offset + p.Length - g.Start) < 2 * MB) prev = p;
                    if (Math.Abs(p.Offset - g.End) < 2 * MB) next = p;
                }
                string size = Util.FmtSize(g.Length);
                string after = prev == null ? "сразу после предыдущего раздела" : "сразу после " + PartGen(prev);
                if (atStart)
                {
                    SpaceChoice c = NewSpace(g, "в начале диска", "за счёт свободного места (" + size + ")");
                    c.AtStart = true;
                    list.Add(c);
                    pick = list.Count - 1;
                    continue;
                }
                // в конец участка: так раздел не мешает расширять предыдущий
                SpaceChoice e = NewSpace(g, toDiskEnd ? "в конце диска" : "в середине диска, перед " + (next == null ? "следующим разделом" : PartIns(next)),
                    "за счёт свободного места (" + size + (prev == null ? "" : " после " + PartGen(prev)) + ")");
                list.Add(e);
                if (pick < 0 || (!list[pick].AtStart && g.Length > bestGap)) { pick = list.Count - 1; bestGap = g.Length; }
                // в начало участка: сразу за предыдущим разделом
                SpaceChoice f = NewSpace(g, "в середине диска, " + after, "за счёт свободного места (" + size + ")");
                f.GapFront = true;
                list.Add(f);
            }
            int firstShrink = list.Count;
            foreach (PartInfo p in parts)
            {
                if (p.IsEsp || p.IsMsr || p.Letters.Count == 0) continue;
                try
                {
                    DriveInfo di = new DriveInfo(p.Letters[0].ToString());
                    if (!di.IsReady || di.DriveFormat != "NTFS" || di.TotalFreeSpace < Esp + 150 * MB) continue;
                    SpaceChoice c = new SpaceChoice();
                    c.Shrink = true; c.Letter = p.Letters[0]; c.GapFront = true;
                    c.Anchor = p.Offset + p.Length - MB;
                    c.Where = "в середине диска, сразу после " + c.Letter + ":";
                    c.Source = "за счёт раздела " + c.Letter + ": (уменьшится на 300 МБ, свободно " + Util.FmtSize(di.TotalFreeSpace) + " из " + Util.FmtSize(di.TotalSize) + ")";
                    c.Text = Capital(c.Where) + " - " + c.Source;
                    list.Add(c);
                    if (pick < 0 && c.Letter == s.Letter) pick = list.Count - 1;
                }
                catch { }
            }
            // перенос системы: EFI -> Windows -> остальное (через WIM-копию), если перед Windows нет других разделов
            string why;
            ReorderInfo ro = BuildReorder(s, parts, old, us, ue, out why);
            if (ro != null)
            {
                FillTemps(ro, s, parts, us, ue);
                SpaceChoice c = new SpaceChoice();
                c.Reorder = ro; c.AtStart = true;
                c.Where = "в начале диска, дальше " + s.Letter + ":, затем остальное";
                c.Source = "за счёт раздела " + s.Letter + ": (система переносится через копию WIM, раздел создаётся заново)";
                c.Text = Capital(c.Where) + " - " + c.Source;
                list.Add(c);
            }
            if (pick < 0 && list.Count > 0) pick = 0;
            foreach (SpaceChoice c in list) cmbRepSpace.Items.Add(c);
            if (pick >= 0) cmbRepSpace.SelectedIndex = pick;
            UpdateTempChoices();
            lblRepSpaceHint.Text = list.Count == 0
                ? "Нет места: нужно 300 МБ неразмеченного места или раздел NTFS со свободным местом. Освободите место и нажмите «Обновить» на первом шаге."
                : "В начало диска раздел встаёт сам, если там есть свободное место. Если его нет, остаётся перенос системы: она сохраняется в WIM-копию, "
                  + "разделы создаются заново (EFI, затем Windows того же размера, остальные не трогаются) и копия разворачивается обратно. "
                  + "Копия живёт до конца операции, при сбое систему можно восстановить из неё."
                  + (ro == null && why != null ? "\r\nПеренос системы недоступен: " + why + "." : "");
        }

        /// <summary>Можно ли перенести Windows в начало диска (EFI -> Windows -> остальное). null - нет, причина в why.</summary>
        ReorderInfo BuildReorder(SysInfo s, List<PartInfo> parts, PartInfo old, long us, long ue, out string why)
        {
            const long MB = 1048576, Esp = 300 * MB;
            why = null;
            if (s.Current) { why = "это работающая система, переносить её можно только из WinPE"; return null; }
            PartInfo win = null;
            foreach (PartInfo p in parts) if (p.Number == s.PartNo) win = p;
            if (win == null) { why = "раздел Windows не найден в таблице разделов"; return null; }
            List<PartInfo> sorted = new List<PartInfo>(parts);
            sorted.Sort(delegate(PartInfo a, PartInfo b) { return a.Offset.CompareTo(b.Offset); });
            if (sorted[0].IsEsp)
            {
                int j = 1;
                while (j < sorted.Count && sorted[j].IsMsr) j++;
                if (j < sorted.Count && sorted[j].Number == win.Number) { why = "EFI-раздел уже первый, а Windows идёт сразу за ним"; return null; }
            }
            long start = (us + MB - 1) / MB * MB;
            ReorderInfo ro = new ReorderInfo();
            ro.Win = win; ro.RegionStart = start; ro.RegionEnd = start + Esp + win.Length;
            if (ro.RegionEnd > ue / MB * MB) { why = "на диске не хватает места"; return null; }
            foreach (PartInfo p in parts)
            {
                bool del = p.Number == win.Number || (old != null && p.Number == old.Number);
                bool overlap = p.Offset < ro.RegionEnd && p.Offset + p.Length > start;
                if (!del && overlap)
                {
                    if (p.IsMsr) del = true;
                    else { why = "в начале диска стоит раздел №" + p.Number + " (" + PartType(p) + "), а программа его не двигает"; return null; }
                }
                if (del) ro.Delete.Add(p);
            }
            try
            {
                DriveInfo di = new DriveInfo(s.Letter.ToString());
                if (!di.IsReady) { why = "раздел " + s.Letter + ": не читается"; return null; }
                ro.UsedBytes = di.TotalSize - di.TotalFreeSpace;
                string lbl = (di.VolumeLabel ?? "").Replace("\"", "").Trim();
                ro.WinLabel = lbl.Length == 0 ? "Windows" : (lbl.Length > 32 ? lbl.Substring(0, 32) : lbl);
            }
            catch { why = "раздел " + s.Letter + ": не читается"; return null; }
            ro.WinLetter = s.Letter;
            return ro;
        }

        /// <summary>Куда положить WIM-копию: тома на другом диске, тома на этом же диске, затем временный раздел в свободном месте вне зоны переноса.</summary>
        void FillTemps(ReorderInfo ro, SysInfo s, List<PartInfo> parts, long us, long ue)
        {
            const long MB = 1048576, GB = 1073741824;
            HashSet<char> gone = new HashSet<char>();
            foreach (PartInfo p in ro.Delete) foreach (char c in p.Letters) gone.Add(c);
            List<TempChoice> other = new List<TempChoice>(), same = new List<TempChoice>();
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                char c = char.ToUpper(d.Name[0]);
                if (c == 'A' || c == 'B' || c == 'X' || gone.Contains(c)) continue;
                try
                {
                    if (!d.IsReady || (d.DriveFormat != "NTFS" && d.DriveFormat != "exFAT") || d.TotalFreeSpace < ro.UsedBytes + GB) continue;
                    bool sameDisk = Native.DisksOfVolume(c).Contains(s.Disk);
                    TempChoice t = new TempChoice();
                    t.Letter = c;
                    t.Text = c + ":" + (d.VolumeLabel.Length > 0 ? " " + d.VolumeLabel : "") + " (" + d.DriveFormat + ", свободно " + Util.FmtSize(d.TotalFreeSpace)
                        + (sameDisk ? ", этот же диск" : ", другой диск") + ")";
                    (sameDisk ? same : other).Add(t);
                }
                catch { }
            }
            ro.Temps.AddRange(other);
            ro.Temps.AddRange(same);

            long size = (ro.UsedBytes + 2 * GB + GB - 1) / GB * GB;
            if (size < 4 * GB) size = 4 * GB;
            long bestEnd = -1, bestLen = 0;
            foreach (FreeSpan g in Native.FreeSpans(parts, null, us, ue))
            {
                // часть участка вне зоны, куда встанут новый EFI и Windows
                long[][] pieces = new long[][] { new long[] { g.Start, Math.Min(g.End, ro.RegionStart) }, new long[] { Math.Max(g.Start, ro.RegionEnd), g.End } };
                foreach (long[] pc in pieces)
                    if (pc[1] - pc[0] >= size && pc[1] - pc[0] > bestLen) { bestLen = pc[1] - pc[0]; bestEnd = pc[1]; }
            }
            if (bestEnd > 0)
            {
                TempChoice t = new TempChoice();
                t.Size = size; t.Offset = (bestEnd - size) / MB * MB;
                t.Text = "Временный раздел в свободном месте этого диска (" + Util.FmtSize(size) + ") - создаётся и удаляется автоматически";
                ro.Temps.Add(t);
            }
        }

        static string PartGen(PartInfo p) { return p.Letters.Count > 0 ? "раздела " + p.Letters[0] + ":" : "раздела №" + p.Number; }       // после чего
        static string PartIns(PartInfo p) { return p.Letters.Count > 0 ? "разделом " + p.Letters[0] + ":" : "разделом №" + p.Number; }       // перед чем

        static string Capital(string s) { return s.Length == 0 ? s : char.ToUpper(s[0]) + s.Substring(1); }

        static SpaceChoice NewSpace(FreeSpan g, string where, string source)
        {
            SpaceChoice c = new SpaceChoice();
            c.Anchor = g.Start; c.GapStart = g.Start; c.GapEnd = g.End;
            c.Where = where; c.Source = source;
            c.Text = Capital(where) + " - " + source;
            return c;
        }

        // ------------------------------------------------------------ диагностика и проверки

        string BuildRepairDiag()
        {
            SysInfo s = SelectedSys();
            if (s == null) return "Выберите систему в списке.";
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(KV("Система", s.Letter + ":  " + s.Name + (s.X64 ? " x64" : " x86") + "  (" + s.Version + ")"));
            if (s.Disk < 0) { sb.Append("Динамические тома и тома на нескольких дисках не поддерживаются."); return sb.ToString(); }
            List<PartInfo> parts = new List<PartInfo>();
            foreach (ListViewItem it in lvBoot.Items) parts.Add((PartInfo)it.Tag);
            if (parts.Count == 0) { sb.Append("Не удалось прочитать таблицу разделов диска " + s.Disk + "."); return sb.ToString(); }
            bool gpt = parts[0].Gpt;
            sb.AppendLine(KV("Диск", s.Disk + ",  " + (gpt ? "GPT" : "MBR") + ",  разделов: " + parts.Count));
            sb.AppendLine(KV("Вид загрузки", gpt ? "UEFI (нужен EFI-раздел)" : "BIOS / Legacy (нужен активный раздел)"));

            List<string> problems = new List<string>();
            if (gpt)
            {
                int esp = 0; foreach (PartInfo p in parts) if (p.IsEsp) esp++;
                sb.AppendLine(KV("EFI-разделов", esp.ToString()));
                if (esp == 0) problems.Add("На диске нет EFI-раздела - Windows не загрузится. Его можно создать на следующем шаге.");
                string efi = s.Letter + ":\\Windows\\Boot\\EFI\\bootmgfw.efi";
                bool ok = File.Exists(efi);
                sb.AppendLine(KV("Файлы UEFI", ok ? "есть в Windows\\Boot\\EFI" : "НЕТ в Windows\\Boot\\EFI"));
                if (!ok) problems.Add("В этой Windows нет файлов UEFI-загрузчика (Windows 7 x86 или без SP1 грузится по UEFI не может).");
                if (Native.FirmwareMode() == "BIOS") problems.Add("ПК загружен в BIOS, а диск GPT - с него Windows не стартует, нужен режим UEFI.");
            }
            else
            {
                PartInfo act = null; foreach (PartInfo p in parts) if (p.Active) act = p;
                sb.AppendLine(KV("Активный раздел", act == null ? "НЕТ" : "№" + act.Number + (act.Letters.Count > 0 ? " (" + act.Letters[0] + ":)" : "")));
                string pc = s.Letter + ":\\Windows\\Boot\\PCAT\\bootmgr";
                bool ok = File.Exists(pc);
                sb.AppendLine(KV("Файлы BIOS", ok ? "есть в Windows\\Boot\\PCAT" : "НЕТ в Windows\\Boot\\PCAT"));
                if (act == null) problems.Add("Нет активного раздела - BIOS не найдёт загрузчик. Исправится при ремонте.");
                if (!ok) problems.Add("В этой Windows нет файлов BIOS-загрузчика (Windows\\Boot\\PCAT).");
                if (Native.FirmwareMode() == "UEFI") problems.Add("ПК загружен в UEFI, а диск MBR - для загрузки нужен режим Legacy/CSM.");
            }
            if (s.Current) problems.Add("Это система, из которой запущена программа. Лучше чинить из WinPE.");
            sb.AppendLine();
            if (problems.Count == 0) sb.Append("Явных проблем не видно. Ремонт пересоздаст загрузчик.");
            else foreach (string p in problems) sb.AppendLine("! " + p);
            return sb.ToString().TrimEnd();
        }

        /// <summary>Что мешает уйти с шага вперёд; null - можно.</summary>
        string RepairProblem(int step)
        {
            SysInfo s = SelectedSys();
            if (step == 0)
            {
                if (s == null) return "Выберите Windows, загрузчик которой нужно исправить.";
                if (s.Disk < 0) return "Динамические тома и тома на нескольких дисках не поддерживаются.";
                if (lvBoot.Items.Count == 0) return "Не удалось прочитать таблицу разделов диска " + s.Disk + ".";
            }
            if (step == 1)
            {
                if (MakeEspWanted())
                {
                    if (cmbRepSpace.SelectedItem == null) return "Для нового EFI-раздела нет места (нужно 300 МБ). Освободите место или выберите другой диск.";
                    ReorderInfo ro = CurrentReorder();
                    if (ro != null && cmbRepTemp.SelectedItem == null)
                        return "Нет места для копии системы (нужно около " + Util.FmtSize(ro.UsedBytes + 1073741824L) + " на томе NTFS/exFAT или столько же свободного места на диске). Подключите диск или флешку и нажмите «Обновить» на первом шаге.";
                    return null;
                }
                if (repEspMissing) return "На диске нет EFI-раздела, загрузчик некуда записать. Отметьте «Создать EFI-раздел» на шаге «Раздел загрузчика».";
                PartInfo bp = RepairBootPart();
                if (bp == null) return "Выберите раздел, куда записать загрузчик.";
                if (bp.Gpt && !bp.IsEsp) return "Выбран раздел №" + bp.Number + ", а на GPT-диске загрузчик пишется в EFI-раздел. На шаге «Раздел загрузчика» щёлкните строку «EFI-раздел (ESP)».";
            }
            return null;
        }

        string BuildRepairSummary()
        {
            SysInfo s = SelectedSys();
            PartInfo bp = RepairBootPart();
            if (s == null) return "";
            bool gpt = lvBoot.Items.Count > 0 && ((PartInfo)lvBoot.Items[0].Tag).Gpt;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(KV("Система", s.Letter + ":  " + s.Name + (s.X64 ? " x64" : " x86") + ",  диск " + s.Disk + " (" + (gpt ? "GPT" : "MBR") + ")"));
            bool mk = MakeEspWanted();
            SpaceChoice sp = cmbRepSpace.SelectedItem as SpaceChoice;
            PartInfo old = mk && repEspExists ? OldEsp() : null;
            sb.AppendLine(KV("Загрузчик в", mk ? "новый EFI-раздел 300 МБ" : bp == null ? "-" : "раздел №" + bp.Number + (bp.Letters.Count > 0 ? " (" + bp.Letters[0] + ":)" : " (временная буква)")));
            if (mk) sb.AppendLine(KV("Место", sp == null ? "-" : sp.Where));
            if (mk && sp != null) sb.AppendLine(KV("Откуда", sp.Source));
            if (mk && sp != null && sp.Reorder != null)
            {
                TempChoice tc = cmbRepTemp.SelectedItem as TempChoice;
                sb.AppendLine(KV("Копия системы", tc == null ? "-" : tc.Letter != ' ' ? "на томе " + tc.Letter + ": (файл WIM останется)" : "на временном разделе (удалится)"));
                sb.AppendLine(KV("Пересоздаётся", "раздел " + s.Letter + ": (" + Util.FmtSize(sp.Reorder.Win.Length) + ") и EFI"));
            }
            if (old != null) sb.AppendLine(KV("Старый EFI", "раздел №" + old.Number + " будет УДАЛЁН"));
            sb.AppendLine(KV("BCD", chkRepRebuild.Checked ? (chkRepKeepBak.Checked ? "создать заново, старый сохранить" : "создать заново, старый удалить без копии") : "добавить / обновить запись"));
            if (gpt) sb.AppendLine(KV("EFI\\Boot", chkRepFallback.Checked ? "записать запасной bootx64.efi" : "не трогать"));
            else
            {
                sb.AppendLine(KV("MBR", chkRepMbr.Checked ? "записать код загрузки" : "не трогать"));
                sb.AppendLine(KV("Активный раздел", chkRepActive.Checked ? "сделать раздел загрузчика активным" : "не менять"));
            }
            sb.AppendLine();
            sb.Append(mk && sp != null && sp.Reorder != null ? "Раздел " + s.Letter + ": удаляется и создаётся заново из копии системы (WIM). Остальные разделы не трогаются."
                : old != null ? "Старый EFI-раздел удаляется вместе с загрузчиками других систем на нём. Остальные данные не стираются."
                : "Данные на разделах не стираются.");
            return sb.ToString();
        }

        // ------------------------------------------------------------ запуск

        void StartRepair()
        {
            if (installing || busy) return;
            SysInfo s = SelectedSys();
            string pr = RepairProblem(0) ?? RepairProblem(1);
            if (pr != null) { MessageBox.Show(this, pr, "Ремонт загрузчика"); return; }
            PartInfo bp = RepairBootPart();
            RepairPlan r = new RepairPlan();
            r.Sys = s;
            r.Gpt = ((PartInfo)lvBoot.Items[0].Tag).Gpt;
            r.MakeEsp = MakeEspWanted();
            if (r.MakeEsp)
            {
                r.Space = (SpaceChoice)cmbRepSpace.SelectedItem;
                if (r.Space.Reorder != null) r.Temp = (TempChoice)cmbRepTemp.SelectedItem;
                r.RecreateEsp = repEspExists;
                if (r.RecreateEsp) r.OldEspPart = OldEsp().Number;
            }
            if (!r.MakeEsp) { r.BootPart = bp.Number; r.BootLetter = bp.Letters.Count > 0 ? bp.Letters[0] : ' '; }
            r.Rebuild = chkRepRebuild.Checked;
            r.KeepBak = r.Rebuild && chkRepKeepBak.Checked;
            r.Fallback = r.Gpt && chkRepFallback.Checked;
            r.WriteMbr = !r.Gpt && chkRepMbr.Checked;
            r.SetActive = !r.Gpt && chkRepActive.Checked;
            r.DryRun = Program.DryRun;

            string fw = Native.FirmwareMode();
            string warn = "";
            if (fw == "UEFI" && !r.Gpt) warn = "\n\nВнимание: ПК загружен в UEFI, а диск MBR - для загрузки нужен режим Legacy/CSM.";
            if (fw == "BIOS" && r.Gpt) warn = "\n\nВнимание: ПК загружен в BIOS, а диск GPT - с него Windows не загрузится.";
            string msg = BuildRepairSummary() + warn + (r.DryRun ? "\n\n[DRY-RUN - реально ничего не будет сделано]" : "") + "\n\nНачать?";
            if (MessageBox.Show(this, msg, "Подтверждение ремонта", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            if (r.Space != null && r.Space.Reorder != null
                && MessageBox.Show(this, "ВНИМАНИЕ. Раздел " + s.Letter + ": будет УДАЛЁН и создан заново из копии системы (WIM).\n\n"
                    + "- Копия снимается и проверяется ДО удаления; перед самым удалением программа спросит ещё раз.\n"
                    + "- Если питание отключится или программа зависнет после удаления, систему можно восстановить из копии вручную (команду покажут в журнале).\n"
                    + "- Точки восстановления и содержимое «System Volume Information» не переносятся; буквы других дисков после первой загрузки стоит проверить.\n\n"
                    + "Важные данные лучше сначала скопировать на другой диск. Продолжить?",
                    "Перенос системы", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            SetWorking(true);
            Thread t = new Thread(delegate() { RunRepair(r); });
            t.IsBackground = true; t.Start();
        }

        bool RunDiskpart(string text)
        {
            string diskpart = Util.FindTool("diskpart.exe") ?? "diskpart.exe";
            string script = Path.Combine(Path.GetTempPath(), "wininstaller_diskpart.txt");
            Log("--- diskpart script ---\n" + text + "-----------------------");
            File.WriteAllText(script, text, Encoding.ASCII);
            return RunProc(diskpart, "/s \"" + script + "\"", null, null, false) == 0;
        }

        /// <summary>Удаляет раздел, найдя его в таблице по смещению: номера после каждого удаления сдвигаются.</summary>
        bool DeletePart(RepairPlan r, PartInfo p)
        {
            int disk = r.Sys.Disk;
            if (r.DryRun) return RunDiskpart("select disk " + disk + "\r\nselect partition " + p.Number + "\r\ndelete partition override\r\nexit\r\n");
            foreach (PartInfo cur in Native.Partitions(disk))
                if (Math.Abs(cur.Offset - p.Offset) < 1048576)
                    return RunDiskpart("rescan\r\nselect disk " + disk + "\r\nselect partition " + cur.Number + "\r\ndelete partition override\r\nexit\r\n");
            Log("Раздел со смещением " + (p.Offset / 1048576) + " МБ не найден (уже удалён?).");
            return true;
        }

        /// <summary>
        /// Перенос системы в начало диска: WIM-копия Windows -> удаление Windows/EFI -> новый EFI (300 МБ) и Windows того же размера сразу за ним
        /// -> развёртывание копии. Остальные разделы не трогаются. true - EFI и Windows созданы, r.BootLetter задан; false - остановлено
        /// (ошибка уже показана или пользователь отказался до удаления).
        /// </summary>
        bool ReorderSystem(RepairPlan r, List<KeyValuePair<int, char>> temps, List<Action> afterOk)
        {
            const long MB = 1048576, EspSize = 300 * MB;
            SysInfo s = r.Sys;
            ReorderInfo ro = r.Space.Reorder;
            TempChoice tc = r.Temp;
            string W = s.Letter + ":";
            string dism = Util.FindTool("dism.exe");
            if (!r.DryRun && dism == null) { Fail("Не найден dism.exe"); return false; }
            dism = dism ?? "dism.exe";
            char[] fl = FreeLetters(2);
            if (fl.Length < 2) { Fail("Нет свободных букв дисков"); return false; }
            char espL = fl[0], tmpNewL = fl[1];

            // 1. где хранить копию
            char tmpL;
            long tmpOffset = -1;
            if (tc.Letter != ' ') tmpL = tc.Letter;
            else
            {
                SetStatus("Создание временного раздела для копии системы...");
                if (!RunDiskpart("rescan\r\nselect disk " + s.Disk + "\r\ncreate partition primary size=" + (tc.Size / MB) + " offset=" + (tc.Offset / 1024)
                    + "\r\nformat quick fs=ntfs label=\"BH_TMP\"\r\nassign letter=" + tmpNewL + "\r\nexit\r\n"))
                { Fail("Не удалось создать временный раздел для копии. Ничего не изменено."); return false; }
                tmpL = tmpNewL; tmpOffset = tc.Offset;
                if (!r.DryRun)
                {
                    Thread.Sleep(1500);
                    if (!Directory.Exists(tmpL + ":\\")) { Fail("Временный раздел " + tmpL + ": недоступен."); return false; }
                }
            }
            PartInfo tmpPart = null;
            if (tmpOffset >= 0) { tmpPart = new PartInfo(); tmpPart.Offset = tmpOffset; tmpPart.Number = 0; }
            Action dropTemp = delegate { if (tmpPart != null) DeletePart(r, tmpPart); };

            string wimDir = tmpL + ":\\BubaHelper";
            string wim = wimDir + "\\system_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".wim";
            if (!r.DryRun) Directory.CreateDirectory(wimDir);

            // 2. копия системы
            SetStatus("Копирование системы " + W + " в " + wim + " - долго, не выключайте ПК...");
            SetProgress(3);
            int rc = RunProc(dism, "/Capture-Image /ImageFile:\"" + wim + "\" /CaptureDir:" + W + "\\ /Name:\"Windows (перенос разделов)\" /Compress:fast /CheckIntegrity /ScratchDir:\"" + wimDir + "\"",
                delegate(double v) { SetProgress(3 + (int)(v * 0.40)); UI(delegate { lblStatus.Text = "Копирование системы в WIM: " + v.ToString("0") + "%"; }); }, null, false);
            if (rc != 0) { dropTemp(); Fail("dism /Capture-Image завершился с кодом " + rc + ". Ничего не изменено."); return false; }
            long wimSize = 0;
            if (!r.DryRun)
            {
                if (!File.Exists(wim)) { dropTemp(); Fail("dism отработал, но файл копии не найден: " + wim + ". Ничего не изменено."); return false; }
                wimSize = new FileInfo(wim).Length;
                if (wimSize < 100L * MB || WimReader.FromHeader(wim) == null)
                { dropTemp(); Fail("Файл копии подозрительный (" + Util.FmtSize(wimSize) + "), заголовок не читается. Ничего не изменено."); return false; }
                Log("Копия системы готова: " + wim + " (" + Util.FmtSize(wimSize) + ").");
            }

            // 3. точка невозврата
            bool go = r.DryRun || (bool)Invoke((Func<bool>)delegate
            {
                return MessageBox.Show(this, "Копия системы готова и проверена:\n" + wim + "  (" + Util.FmtSize(wimSize) + ")\n\n"
                    + "Сейчас будут УДАЛЕНЫ разделы:\n" + DeleteList(ro) + "\nи созданы заново: EFI 300 МБ и " + s.Letter + ": (" + Util.FmtSize(ro.Win.Length) + "), затем система развернётся из копии.\n\n"
                    + "После этого пути назад нет, кроме восстановления из копии. Продолжить?",
                    "Точка невозврата", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            });
            if (!go)
            {
                dropTemp();
                Log("Перенос отменён пользователем после создания копии. Разделы не менялись" + (tmpPart == null ? "; файл копии остался: " + wim : "") + ".");
                SetStatus("Перенос отменён, разделы не менялись.");
                return false;
            }

            string recover = "\nКопия системы сохранена: " + wim + (tmpPart != null ? " (на временном разделе, он не удалён)" : "")
                + ".\nВосстановление вручную: создайте раздел NTFS и выполните  dism /Apply-Image /ImageFile:\"" + wim + "\" /Index:1 /ApplyDir:<буква>:\\ ,  затем  bcdboot <буква>:\\Windows /s <EFI-раздел>: /f UEFI.";

            // 4. удаление
            foreach (PartInfo p in ro.Delete)
            {
                SetStatus("Удаление раздела №" + p.Number + " (" + PartType(p) + ")...");
                if (!DeletePart(r, p)) { Fail("Не удалось удалить раздел №" + p.Number + "." + recover); return false; }
            }
            SetProgress(46);

            // 5. новая разметка: EFI, затем Windows того же размера
            SetStatus("Создание EFI-раздела и раздела " + W + "...");
            string label = ro.WinLabel;
            if (!RunDiskpart("rescan\r\nselect disk " + s.Disk
                + "\r\ncreate partition efi size=300 offset=" + (ro.RegionStart / 1024) + "\r\nformat quick fs=fat32 label=\"EFI\"\r\nassign letter=" + espL
                + "\r\ncreate partition primary size=" + (ro.Win.Length / MB) + " offset=" + ((ro.RegionStart + EspSize) / 1024)
                + "\r\nformat quick fs=ntfs label=\"" + label + "\"\r\nassign letter=" + s.Letter + "\r\nexit\r\n"))
            { Fail("Не удалось создать новые разделы (diskpart вернул ошибку)." + recover); return false; }
            if (!r.DryRun)
            {
                Thread.Sleep(1500);
                if (!Directory.Exists(W + "\\")) { Fail("Новый раздел " + W + " недоступен." + recover); return false; }
            }
            SetProgress(50);

            // 6. возвращаем систему
            SetStatus("Развёртывание системы из копии на " + W + " - долго, не выключайте ПК...");
            rc = RunProc(dism, "/Apply-Image /ImageFile:\"" + wim + "\" /Index:1 /ApplyDir:" + W + "\\ /ScratchDir:\"" + wimDir + "\"",
                delegate(double v) { SetProgress(50 + (int)(v * 0.35)); UI(delegate { lblStatus.Text = "Развёртывание системы: " + v.ToString("0") + "%"; }); }, null, false);
            if (rc != 0) { Fail("dism /Apply-Image завершился с кодом " + rc + "." + recover); return false; }
            if (!r.DryRun && !File.Exists(W + "\\Windows\\System32\\config\\SYSTEM"))
            { Fail("Система развёрнута, но Windows\\System32\\config\\SYSTEM не найден." + recover); return false; }

            // 7. новый EFI-раздел: номер по смещению, буква уже назначена
            r.BootLetter = espL;
            if (!r.DryRun)
            {
                r.BootPart = -1;
                foreach (PartInfo p in Native.Partitions(s.Disk))
                    if (p.IsEsp && Math.Abs(p.Offset - ro.RegionStart) < MB) r.BootPart = p.Number;
                if (r.BootPart <= 0) { Fail("Новый EFI-раздел не найден в таблице разделов." + recover); return false; }
            }
            temps.Add(new KeyValuePair<int, char>(r.BootPart, espL));
            if (tmpPart != null) afterOk.Add(delegate { dropTemp(); Log("Временный раздел с копией удалён."); });
            else afterOk.Add(delegate { Log("Копия системы оставлена на томе " + tmpL + ": " + wim + " (её можно удалить)."); });
            Log("Перенос выполнен: EFI -> " + W + " -> остальное. Копия: " + wim);
            return true;
        }

        static string DeleteList(ReorderInfo ro)
        {
            StringBuilder sb = new StringBuilder();
            foreach (PartInfo p in ro.Delete)
                sb.AppendLine("  №" + p.Number + "  " + PartType(p) + "  " + Util.FmtSize(p.Length) + (p.Letters.Count > 0 ? "  (" + p.Letters[0] + ":)" : ""));
            return sb.ToString();
        }

        void RunRepair(RepairPlan r)
        {
            SysInfo s = r.Sys;
            string W = s.Letter + ":";
            List<KeyValuePair<int, char>> temps = new List<KeyValuePair<int, char>>();   // временные буквы, снять в конце
            List<Action> afterOk = new List<Action>();       // что сделать после успешной проверки загрузчика (удалить временный раздел с копией)
            int pfloor = 0;
            Action<int> Prog = delegate(int v) { SetProgress(Math.Max(v, pfloor)); };
            try
            {
                UI(delegate { lblStatus.ForeColor = Theme.Text; });
                string bcdboot = Util.FindTool("bcdboot.exe");
                if (!r.DryRun && bcdboot == null) { Fail("Не найден bcdboot.exe"); return; }
                bcdboot = bcdboot ?? "bcdboot.exe";
                Prog(3);

                // 0. загрузочные файлы самой Windows
                string srcFile = r.Gpt ? W + "\\Windows\\Boot\\EFI\\bootmgfw.efi" : W + "\\Windows\\Boot\\PCAT\\bootmgr";
                if (!r.DryRun && !File.Exists(srcFile))
                { Fail("В Windows нет файлов загрузчика: " + srcFile + (r.Gpt ? "\nWindows 7 x86 и Windows 7 без SP1 по UEFI не загружаются." : "")); return; }

                // 1. при необходимости - новый EFI-раздел (старый удаляется, место берётся из свободного участка или из раздела)
                if (r.MakeEsp && r.Space.Reorder != null)
                {
                    if (!ReorderSystem(r, temps, afterOk)) return;
                    pfloor = 85;       // перенос занял основную часть времени: дальше шкала идёт от 85 %
                }
                else if (r.MakeEsp)
                {
                    const long MB = 1048576, EspSize = 300 * MB;
                    SpaceChoice sp = r.Space;
                    char[] fl = FreeLetters(1);
                    if (fl.Length < 1) { Fail("Нет свободных букв дисков"); return; }
                    if (r.RecreateEsp)
                    {
                        SetStatus("Удаление старого EFI-раздела №" + r.OldEspPart + "...");
                        if (!RunDiskpart("select disk " + s.Disk + "\r\nselect partition " + r.OldEspPart + "\r\ndelete partition override\r\nexit\r\n"))
                        { Fail("Не удалось удалить старый EFI-раздел. Ничего не изменено."); return; }
                    }
                    string lost = r.RecreateEsp ? "\nСтарый EFI-раздел уже удалён: повторите ремонт, программа предложит создать новый." : "";
                    if (sp.Shrink)
                    {
                        SetStatus("Уменьшение раздела " + sp.Letter + ": на 300 МБ...");
                        if (!RunDiskpart("rescan\r\nselect volume " + sp.Letter + "\r\nshrink desired=300 minimum=300\r\nexit\r\n"))
                        { Fail("Не удалось уменьшить раздел " + sp.Letter + ": (мало свободного места или есть неперемещаемые файлы)." + lost); return; }
                    }

                    // где именно встанет раздел: после удаления/уменьшения участок слился с соседними, ищем его по опорной точке
                    bool front = sp.AtStart || sp.GapFront;       // в начало участка (сразу за предыдущим разделом) или в его конец
                    long offset = front ? sp.GapStart : (sp.GapEnd - EspSize) / MB * MB;
                    List<PartInfo> before = new List<PartInfo>();
                    if (!r.DryRun)
                    {
                        Thread.Sleep(1000);
                        before = Native.Partitions(s.Disk);
                        long us, ue;
                        if (!Native.GptUsable(s.Disk, out us, out ue)) { Fail("Не удалось прочитать границы GPT-диска." + lost); return; }
                        FreeSpan hit = null;
                        foreach (FreeSpan g in Native.FreeSpans(before, null, us, ue)) if (g.Start <= sp.Anchor && sp.Anchor < g.End) hit = g;
                        if (hit == null || hit.Length < EspSize) { Fail("Не найдено свободное место 300 МБ для EFI-раздела." + lost); return; }
                        offset = front || hit.Start <= (us + MB - 1) / MB * MB ? hit.Start : (hit.End - EspSize) / MB * MB;
                    }
                    SetStatus("Создание EFI-раздела (300 МБ)...");
                    string dp = "rescan\r\nselect disk " + s.Disk + "\r\ncreate partition efi size=300" + (offset > 0 ? " offset=" + (offset / 1024) : "")
                        + "\r\nformat quick fs=fat32 label=\"EFI\"\r\nassign letter=" + fl[0] + "\r\nexit\r\n";
                    if (!RunDiskpart(dp)) { Fail("Не удалось создать EFI-раздел (diskpart вернул ошибку)." + lost); return; }
                    r.BootLetter = fl[0];
                    if (!r.DryRun)
                    {
                        Thread.Sleep(1500);
                        r.BootPart = -1;
                        foreach (PartInfo p in Native.Partitions(s.Disk))
                            if (p.IsEsp && Math.Abs(p.Offset - offset) < MB) r.BootPart = p.Number;
                        if (r.BootPart <= 0) { Fail("EFI-раздел создан, но не найден в таблице разделов."); return; }
                    }
                    temps.Add(new KeyValuePair<int, char>(r.BootPart, fl[0]));
                }
                Prog(15);

                // 2. буква раздела загрузчика
                if (r.BootLetter == ' ')
                {
                    char[] fl = FreeLetters(1);
                    if (fl.Length < 1) { Fail("Нет свободных букв дисков"); return; }
                    SetStatus("Временная буква " + fl[0] + ": для раздела №" + r.BootPart + "...");
                    if (!RunDiskpart("rescan\r\nselect disk " + s.Disk + "\r\nselect partition " + r.BootPart + "\r\nassign letter=" + fl[0] + "\r\nexit\r\n"))
                    { Fail("Не удалось назначить букву разделу №" + r.BootPart); return; }
                    r.BootLetter = fl[0];
                    temps.Add(new KeyValuePair<int, char>(r.BootPart, fl[0]));
                    if (!r.DryRun) Thread.Sleep(1500);
                }
                string B = r.BootLetter + ":";
                if (!r.DryRun && !Directory.Exists(B + "\\")) { Fail("Раздел загрузчика " + B + " недоступен (нет файловой системы?)."); return; }
                Prog(25);

                // 3. BCD: сохранить старый и убрать, чтобы bcdboot создал новый
                string bcdPath = r.Gpt ? B + "\\EFI\\Microsoft\\Boot\\BCD" : B + "\\Boot\\BCD";
                if (r.Rebuild && !r.DryRun && File.Exists(bcdPath))
                {
                    try
                    {
                        File.SetAttributes(bcdPath, FileAttributes.Normal);
                        if (r.KeepBak)
                        {
                            string bak = bcdPath + ".bak_" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                            File.Copy(bcdPath, bak, true);
                            Log("Старый BCD сохранён: " + bak);
                        }
                        File.Delete(bcdPath);
                        if (!r.KeepBak) Log("Старый BCD удалён без копии.");
                    }
                    catch (Exception ex) { Log("Не удалось убрать старый BCD (" + ex.Message + ") - bcdboot обновит его на месте."); }
                }

                // 4. bcdboot
                SetStatus("Запись загрузчика (" + (r.Gpt ? "UEFI" : "BIOS") + ") в " + B + "...");
                Prog(40);
                string bargs = W + "\\Windows /s " + B + (r.Gpt ? " /f UEFI" : " /f BIOS");
                int rc = RunProc(bcdboot, bargs, null, null, false);
                if (rc != 0)
                {
                    string alt = W + "\\Windows\\System32\\bcdboot.exe";
                    if (!r.DryRun && File.Exists(alt))
                    {
                        // bcdboot из Windows 7 не знает ключа /f
                        string aargs = s.Build >= 9200 ? bargs : W + "\\Windows /s " + B;
                        Log("bcdboot из WinPE не справился, пробую bcdboot из " + s.Name);
                        try { rc = RunProc(alt, aargs, null, null, false); }
                        catch (Exception ex) { Log("bcdboot из Windows не запустился: " + ex.Message); }
                    }
                    if (rc != 0) { Fail("bcdboot завершился с кодом " + rc); return; }
                }
                Prog(65);

                // 5. BIOS: активный раздел, загрузочный сектор и MBR
                if (!r.Gpt)
                {
                    if (r.SetActive)
                    {
                        SetStatus("Раздел №" + r.BootPart + " делается активным...");
                        if (!RunDiskpart("select disk " + s.Disk + "\r\nselect partition " + r.BootPart + "\r\nactive\r\nexit\r\n"))
                            Log("ПРЕДУПРЕЖДЕНИЕ: не удалось сделать раздел активным.");
                    }
                    string bootsect = Util.BootsectPath();
                    if (bootsect == null) Log("ПРЕДУПРЕЖДЕНИЕ: bootsect.exe не найден (вшитая копия работает только в x64) - загрузочный сектор и MBR не записаны. Положите bootsect.exe в папку tools рядом с программой.");
                    else
                    {
                        SetStatus("Запись загрузочного сектора" + (r.WriteMbr ? " и MBR" : "") + "...");
                        if (RunProc(bootsect, "/nt60 " + B + (r.WriteMbr ? " /mbr" : "") + " /force", null, null, false) != 0)
                            Log("ПРЕДУПРЕЖДЕНИЕ: bootsect вернул ошибку.");
                    }
                }
                Prog(80);

                // 6. UEFI: запасной загрузчик по пути по умолчанию
                if (r.Gpt && r.Fallback && !r.DryRun)
                {
                    try
                    {
                        string from = B + "\\EFI\\Microsoft\\Boot\\bootmgfw.efi";
                        string dir = B + "\\EFI\\Boot";
                        Directory.CreateDirectory(dir);
                        File.Copy(from, Path.Combine(dir, s.X64 ? "bootx64.efi" : "bootia32.efi"), true);
                        Log("Запасной загрузчик записан: " + dir + "\\" + (s.X64 ? "bootx64.efi" : "bootia32.efi"));
                    }
                    catch (Exception ex) { Log("ПРЕДУПРЕЖДЕНИЕ: запасной загрузчик не записан: " + ex.Message); }
                }

                // 7. проверка
                if (!r.DryRun)
                {
                    string chk = r.Gpt ? B + "\\EFI\\Microsoft\\Boot\\bootmgfw.efi" : B + "\\bootmgr";
                    if (!File.Exists(chk)) { Fail("Загрузчик не найден после bcdboot: " + chk); return; }
                    if (!File.Exists(bcdPath)) { Fail("BCD не создан: " + bcdPath); return; }
                    string bcdedit = Util.FindTool("bcdedit.exe");
                    if (bcdedit != null) RunProc(bcdedit, "/store \"" + bcdPath + "\" /enum all", null, null, true);
                }
                foreach (Action a in afterOk) { try { a(); } catch (Exception ex) { Log("ПРЕДУПРЕЖДЕНИЕ: " + ex.Message); } }
                SetProgress(100);
                Beep(true);
                SetStatus("ГОТОВО. Загрузчик " + s.Name + " (" + W + ") записан в " + B + ".");
                UI(delegate
                {
                    MessageBox.Show(this, r.DryRun ? "DRY-RUN завершён: команды смотрите в логе."
                        : "Загрузчик исправлен.\n\nПерезагрузите ПК. Если Windows не стартует - пришлите журнал (install.log): в нём список записей BCD.", "Готово");
                });
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
            finally
            {
                // снять временные буквы (у ESP и скрытых разделов их быть не должно)
                foreach (KeyValuePair<int, char> t in temps)
                {
                    try { RunDiskpart("select disk " + s.Disk + "\r\nselect partition " + t.Key + "\r\nremove letter=" + t.Value + "\r\nexit\r\n"); }
                    catch (Exception ex) { Log("Не удалось снять букву " + t.Value + ": " + ex.Message); }
                }
                UI(delegate { FinishUi(); });
            }
        }
    }
}
