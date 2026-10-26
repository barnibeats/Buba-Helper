// Главная форма: поля, конструктор и логика мастера шагов. Построение интерфейса - в MainForm.Ui.cs.
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
        // оболочка
        UiHeader header;
        DPanel body, footer, pnlLog;
        NavList navSteps;
        UiButton btnLog;
        bool logOpen;
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
        Control nudSizeBox;                       // рамка вокруг nudSize (скрывается вместе с ним)
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
        UiCard grpDisk;
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

        const int EfiMB = 300, MsrMB = 16, RecMB = 1024;

        // разметка: размер раздела Windows вводится в МБ или ГБ; раздел восстановления; диски загрузочного носителя
        ComboBox cmbSizeUnit;
        CheckBox chkRecovery;
        bool sizeUnitGb = true;
        List<int> bootDisks = new List<int>();   // диски, с которых загружена WinPE или запущена программа - на них ставить нельзя

        public MainForm() : this(null, false, false) { }

        public MainForm(string profilePath, bool auto, bool yes)
        {
            BuildUi();
            logFile = Path.Combine(Util.AppDir(), "install.log");
            firmware = Native.FirmwareMode();
            lblFw.Text = "ПК загружен в режиме: " + firmware;
            header.Info = "ПК: " + firmware + (Program.DryRun ? "   ·   DRY-RUN" : "");
            if (firmware == "BIOS") rbMbr.Checked = true; else rbGpt.Checked = true;
            Log("Buba-Helper " + Util.Version() + " запущен" + (Program.DryRun ? " (DRY-RUN: команды не выполняются)" : "") + ". Режим прошивки: " + firmware);
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
                navSteps.SelectSilently(CurrentTabs().SelectedIndex);   // до создания окна TabControl отдаёт индекс -1
                RefreshDisks(); ScanSources(); UpdatePlanText();
                if (Program.StartMode == "xml") { rbModeXml.Checked = true; if (genEditor != null) genEditor.Go(Program.StartPage); }
                else if (Program.StartMode == "capture") rbModeCapture.Checked = true;
                else if (Program.StartMode == "integrate") rbModeIntegrate.Checked = true;
                else if (Program.StartMode == "repair")
                {
                    rbModeRepair.Checked = true;
                    if (Program.StartPage > 0 && Program.StartPage < tabRepair.TabCount) tabRepair.SelectedIndex = Program.StartPage;
                }            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Dark.Title(Handle);
        }

        TabControl CurrentTabs() { return rbModeCapture.Checked ? tabCapture : rbModeRepair.Checked ? tabRepair : rbModeIntegrate.Checked ? tabIntegrate : tabInstall; }

        /// <summary>Что мешает уйти с шага step вперёд; null - шаг заполнен.</summary>
        string StepProblem(int step)
        {
            if (rbModeRepair.Checked) return RepairProblem(step);
            if (rbModeIntegrate.Checked) return IntegrateProblem(step);
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
                    if (!rbKeep.Checked && d.Blocked) return "Этот диск - источник образа или загрузочный носитель WinPE. Выберите другой диск.";
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
            if (i == last && rbModeRepair.Checked) lblRepSummary.Text = BuildRepairSummary();
            else if (i == last && rbModeIntegrate.Checked) lblIntSummary.Text = BuildIntegrateSummary();
            else if (i == last && !rbModeCapture.Checked) lblSummary.Text = BuildSummary();
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

        static string KV(string k, string v) { return (k + ":").PadRight(18) + v; }

        /// <summary>Итоговая сводка на последнем шаге установки.</summary>
        string BuildSummary()
        {
            StringBuilder sb = new StringBuilder();
            ImageInfo im = cmbEdition.SelectedItem as ImageInfo;
            sb.AppendLine(KV("Образ", (sourcePath ?? "-")));
            sb.AppendLine(KV("Редакция", im == null ? "-" : im.ToString()));
            DiskInfo d = SelectedDisk();
            sb.AppendLine(KV("Диск", d == null ? "-" : d.Number + ":  " + d.Model + "  [" + Util.FmtSize(d.Size) + ", " + d.Bus + ", " + d.Style + "]"));
            sb.AppendLine(KV("Режим", rbKeep.Checked ? "только раздел Windows" : "стереть весь диск"));
            sb.AppendLine();
            sb.AppendLine(lblPlan.Text);
            sb.AppendLine();
            string w11 = Win11Notes();
            if (w11 != null) { sb.AppendLine(w11); sb.AppendLine(); }
            sb.AppendLine(KV("Драйверы", chkDrivers.Checked && txtDrivers.Text.Trim().Length > 0 ? txtDrivers.Text.Trim() : "нет"));
            sb.AppendLine(KV("XML ответов", txtInstXml.Text.Trim().Length > 0 ? txtInstXml.Text.Trim() : "без XML"));
            sb.AppendLine(KV("После установки", (chkReboot.Checked ? "перезагрузить ПК" : "не перезагружать") + ";  проверка SHA-256: " + (chkVerify.Checked ? "да" : "нет")));
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

        /// <summary>Показывает или прячет панель журнала над нижней полосой.</summary>
        void ShowLog(bool on)
        {
            logOpen = on;
            pnlLog.Visible = on && !rbModeXml.Checked;
            btnLog.Text = on ? "Скрыть журнал" : "Журнал";
        }
    }
}
