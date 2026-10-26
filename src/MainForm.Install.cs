// Главная форма: запуск и выполнение установки, звук и отсчёт перед перезагрузкой.
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
    partial class MainForm
    {
        // ------------------------------------------------------------ запуск установки

        void StartInstall(bool skipConfirm)
        {
            if (installing || busy) return;
            if (imageFile == null || cmbEdition.SelectedItem == null) { MessageBox.Show(this, "Выберите образ и редакцию.", "Нет образа"); return; }
            DiskInfo d = SelectedDisk();
            if (d == null) { MessageBox.Show(this, "Выберите диск для установки.", "Нет диска"); return; }
            bool keep = rbKeep.Checked;
            if (!keep && d.Blocked) { MessageBox.Show(this, "Этот диск - источник образа или загрузочный носитель WinPE. Выберите другой диск.", "Диск нельзя стирать"); return; }

            Plan p = new Plan();
            p.ImageFile = imageFile;
            p.SourcePath = sourcePath;
            p.ImageIndex = ((ImageInfo)cmbEdition.SelectedItem).Index;
            p.Disk = d;
            p.Keep = keep;
            p.Reboot = chkReboot.Checked;
            p.VerifyHash = chkVerify.Checked;
            p.DryRun = Program.DryRun;
            if (chkDrivers.Checked)
            {
                string dd = txtDrivers.Text.Trim().TrimEnd('\\');
                if (!Directory.Exists(dd)) { MessageBox.Show(this, "Папка с драйверами не найдена: " + dd, "Драйверы"); return; }
                p.DriversDir = dd;
            }
            string xml = ResolvePath(txtInstXml.Text);
            if (xml.Length > 0)
            {
                if (!File.Exists(xml)) { MessageBox.Show(this, "XML-файл не найден: " + xml, "XML"); return; }
                try { XmlDocument xd = new XmlDocument(); xd.Load(xml); }
                catch (Exception ex) { MessageBox.Show(this, "XML-файл повреждён: " + ex.Message, "XML"); return; }
                p.Xml = xml;
            }

            string vols = d.Volumes.Count > 0 ? string.Join("\n    ", d.Volumes.ToArray()) : "(нет томов с буквами)";
            string msg;
            if (keep)
            {
                if (d.Style != "GPT" && d.Style != "MBR") { MessageBox.Show(this, "Не удалось определить таблицу разделов диска (нужны права администратора).", "Диск"); return; }
                PartItem pi = cmbPart.SelectedItem as PartItem;
                if (pi == null) { MessageBox.Show(this, "Выберите раздел, в который ставить Windows.", "Раздел"); return; }
                if (sourceLetters.Contains(pi.Letter)) { MessageBox.Show(this, "В этом разделе лежит образ - выберите другой.", "Раздел"); return; }
                p.Gpt = d.Style == "GPT";
                p.WinLetter = pi.Letter;
                long psz = 0;
                try { psz = new DriveInfo(pi.Letter.ToString()).TotalSize; } catch { }
                if (psz < 15L * 1073741824) { MessageBox.Show(this, "Раздел слишком мал для Windows (нужно хотя бы 15 ГБ).", "Раздел"); return; }
                p.WinPart = Native.PartitionNumberOfVolume(pi.Letter);
                if (p.WinPart <= 0 && !p.DryRun) { MessageBox.Show(this, "Не удалось определить номер раздела " + pi.Letter + ":.", "Раздел"); return; }
                if (p.Gpt)
                {
                    p.EspPart = Native.EspPartitionNumber(d.Number);
                    if (p.EspPart <= 0 && !p.DryRun) { MessageBox.Show(this, "На диске нет EFI-раздела. Используйте режим «Стереть весь диск».", "Загрузчик"); return; }
                }
                msg = "Диск " + d.Number + ":  " + d.Model + "  [" + Util.FmtSize(d.Size) + ", " + d.Bus + ", " + d.Style + "]\n"
                    + "Тома на нём:\n    " + vols + "\n\n"
                    + "БУДЕТ ОТФОРМАТИРОВАН ТОЛЬКО РАЗДЕЛ  " + pi.Text.Trim() + "\n"
                    + "Остальные разделы диска не затрагиваются.\n\n"
                    + "Редакция: " + cmbEdition.SelectedItem + "\n"
                    + "Загрузчик: " + (p.Gpt ? "UEFI, существующий EFI-раздел №" + p.EspPart : "BIOS, раздел " + pi.Letter + ": станет активным") + "\n\n";
            }
            else
            {
                p.Gpt = rbGpt.Checked;
                int mb = SelectedSizeMB();
                p.WinSizeMB = mb;
                p.DataPart = chkData.Checked && mb != 0;
                p.Recovery = chkRecovery.Checked;
                long diskMB = d.Size / 1048576;
                long avail = diskMB - OverheadMB(p.Gpt);
                if (avail < 20 * 1024) { MessageBox.Show(this, "Диск слишком мал для Windows (нужно хотя бы 20 ГБ).", "Размер"); return; }
                if (p.WinSizeMB != 0 && p.WinSizeMB + (p.DataPart ? 1024 : 0) > avail)
                { MessageBox.Show(this, "Выбранный размер не помещается на диск (доступно " + Util.FmtSize(avail * 1048576) + ").", "Размер"); return; }
                if (!p.Gpt && (p.WinSizeMB == 0 ? avail : p.WinSizeMB) > 2L * 1024 * 1024)
                { MessageBox.Show(this, "MBR не поддерживает раздел Windows больше 2 ТБ. Укажите размер меньше 2 ТБ.", "MBR"); return; }
                if (p.WinSizeMB != 0 && p.WinSizeMB < 20 * 1024) { MessageBox.Show(this, "Для Windows нужно хотя бы 20 ГБ.", "Размер"); return; }
                if (p.Recovery && p.WinSizeMB == 0) p.WinSizeMB = avail;     // «весь диск» с разделом восстановления: Windows занимает всё, кроме него
                msg = "Диск " + d.Number + ":  " + d.Model + "  [" + Util.FmtSize(d.Size) + ", " + d.Bus + ", " + d.Style + "]\n"
                    + "Тома на нём:\n    " + vols + "\n\n"
                    + "ВСЕ ДАННЫЕ НА ЭТОМ ДИСКЕ БУДУТ УНИЧТОЖЕНЫ.\n\n"
                    + "Редакция: " + cmbEdition.SelectedItem + "\n"
                    + "Разметка: " + (p.Gpt ? "GPT/UEFI" : "MBR/BIOS") + ",  Windows: " + (mb == 0 ? "весь диск" : mb + " МБ") + (p.Recovery ? " + Recovery " + RecMB + " МБ" : "") + (p.DataPart ? " + Data" : "") + "\n\n";
            }
            msg += "XML: " + (p.Xml ?? "без XML") + "\n\n"
                + (p.DryRun ? "[DRY-RUN - реально ничего не будет сделано]\n\n" : "") + "Продолжить?";
            if (skipConfirm) Log("Подтверждение пропущено (--yes).");
            else if (MessageBox.Show(this, msg, "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            SetWorking(true);
            Thread t = new Thread(delegate() { RunInstall(p); });
            t.IsBackground = true; t.Start();
        }

        // ------------------------------------------------------------ процессы

        /// <summary>Запускает процесс, читает вывод посимвольно (dism обновляет прогресс через \r).</summary>
        int RunProc(string exe, string args, Action<double> onPercent, List<string> capture, bool force)
        {
            Log("> " + Path.GetFileName(exe) + " " + args);
            if (Program.DryRun && !force) { Log("  [dry-run] пропущено"); return 0; }
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            Encoding enc = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            psi.StandardOutputEncoding = enc; psi.StandardErrorEncoding = enc;
            using (Process p = Process.Start(psi))
            {
                ThreadStart reader = null;
                Thread te = new Thread(delegate() { try { string s; while ((s = p.StandardError.ReadLine()) != null) if (s.Trim().Length > 0) Log("  " + s); } catch { } });
                te.IsBackground = true; te.Start();
                StringBuilder sb = new StringBuilder();
                Action flush = delegate()
                {
                    string line = sb.ToString().Trim(); sb.Length = 0;
                    if (line.Length == 0) return;
                    Match m = Regex.Match(line, @"(\d{1,3}(?:[.,]\d+)?)\s*%");
                    if (m.Success && line.StartsWith("["))
                    {
                        double v;
                        if (onPercent != null && double.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) onPercent(v);
                        return;
                    }
                    if (capture != null) capture.Add(line);
                    Log("  " + line);
                };
                int ch;
                while ((ch = p.StandardOutput.Read()) >= 0)
                {
                    if (ch == '\r' || ch == '\n') flush(); else sb.Append((char)ch);
                }
                flush();
                p.WaitForExit();
                te.Join(2000);
                GC.KeepAlive(reader);
                return p.ExitCode;
            }
        }

        // ------------------------------------------------------------ сам процесс установки

        static char[] FreeLetters(int count)
        {
            HashSet<char> used = new HashSet<char>();
            foreach (DriveInfo d in DriveInfo.GetDrives()) used.Add(char.ToUpper(d.Name[0]));
            List<char> res = new List<char>();
            foreach (char c in "WSVUTRQPONMLKJIHGFE")
                if (!used.Contains(c)) { res.Add(c); if (res.Count == count) break; }
            return res.ToArray();
        }

        void Fail(string msg)
        {
            Beep(false);
            Log("ОШИБКА: " + msg);
            UI(delegate
            {
                lblStatus.Text = "ОШИБКА: " + msg;
                lblStatus.ForeColor = Theme.Danger;
                MessageBox.Show(this, msg + "\n\nПодробности - в логе и в файле install.log.", "Ошибка установки", MessageBoxButtons.OK, MessageBoxIcon.Error);
                FinishUi();
            });
        }

        void SetWorking(bool w)
        {
            installing = w;
            btnInstall.Enabled = !w;
            tabInstall.Enabled = !w; tabCapture.Enabled = !w; tabRepair.Enabled = !w; tabIntegrate.Enabled = !w;
            rbModeInstall.Enabled = !w; rbModeCapture.Enabled = !w; rbModeRepair.Enabled = !w; rbModeIntegrate.Enabled = !w; rbModeXml.Enabled = !w;
            navSteps.Enabled = !w;
            if (w) ShowLog(true);   // во время работы журнал виден сразу
            UpdateNav();
        }

        void FinishUi()
        {
            SetWorking(false);
            RefreshDisks();
            if (rbModeCapture.Checked) RefreshVolumes();
            if (rbModeRepair.Checked) RefreshSystems();
        }

        /// <summary>
        /// Копирует Winre.wim из развёрнутой Windows в раздел восстановления (R:\Recovery\WindowsRE) и регистрирует его через reagentc,
        /// затем снимает с раздела временную букву. Любая неудача - только предупреждение: Windows от этого не перестаёт загружаться.
        /// </summary>
        void SetupRecovery(Plan p, string W, char recL)
        {
            string R = recL + ":";
            try
            {
                string winre = W + "\\Windows\\System32\\Recovery\\Winre.wim";
                if (!p.DryRun && !File.Exists(winre))
                    Log("ПРЕДУПРЕЖДЕНИЕ: в образе нет Winre.wim (Windows 7 и образы без среды восстановления) - раздел восстановления остался пустым.");
                else
                {
                    SetStatus("Среда восстановления: копирование Winre.wim в " + R + "...");
                    string dir = R + "\\Recovery\\WindowsRE";
                    if (!p.DryRun)
                    {
                        Directory.CreateDirectory(dir);
                        File.Copy(winre, Path.Combine(dir, "Winre.wim"), true);
                    }
                    string reagentc = Util.FindTool("reagentc.exe") ?? (File.Exists(W + "\\Windows\\System32\\reagentc.exe") ? W + "\\Windows\\System32\\reagentc.exe" : "reagentc.exe");
                    if (RunProc(reagentc, "/setreimage /path " + R + "\\Recovery\\WindowsRE /target " + W + "\\Windows", null, null, false) != 0)
                        Log("ПРЕДУПРЕЖДЕНИЕ: reagentc не зарегистрировал среду восстановления - после первой загрузки выполните reagentc /enable.");
                    else RunProc(reagentc, "/info /target " + W + "\\Windows", null, null, false);
                }
            }
            catch (Exception ex) { Log("ПРЕДУПРЕЖДЕНИЕ: среда восстановления не настроена: " + ex.Message); }
            // раздел восстановления не должен иметь буквы
            string diskpart = Util.FindTool("diskpart.exe") ?? "diskpart.exe";
            string script = Path.Combine(Path.GetTempPath(), "wininstaller_diskpart.txt");
            try
            {
                File.WriteAllText(script, "select volume " + recL + "\r\nremove letter=" + recL + "\r\nexit\r\n", Encoding.ASCII);
                RunProc(diskpart, "/s \"" + script + "\"", null, null, false);
            }
            catch (Exception ex) { Log("Не удалось снять букву " + R + ": " + ex.Message); }
        }

        void RunInstall(Plan p)
        {
            try
            {
                UI(delegate { lblStatus.ForeColor = Theme.Text; });
                string dism = Util.FindTool("dism.exe");
                string bcdboot = Util.FindTool("bcdboot.exe");
                if (!p.DryRun && (dism == null || bcdboot == null)) { Fail("Не найден dism.exe или bcdboot.exe"); return; }
                dism = dism ?? "dism.exe"; bcdboot = bcdboot ?? "bcdboot.exe";

                // 0. контрольная сумма (до любых изменений диска)
                if (p.VerifyHash && p.SourcePath != null)
                {
                    string sc = p.SourcePath + ".sha256";
                    if (!File.Exists(sc)) Log("Файл " + Path.GetFileName(sc) + " не найден - проверка SHA-256 пропущена.");
                    else if (p.DryRun) Log("[dry-run] проверка SHA-256 по " + sc + " пропущена.");
                    else
                    {
                        Match em = Regex.Match(File.ReadAllText(sc), "[0-9a-fA-F]{64}");
                        if (!em.Success) Log("Не удалось прочитать хэш из " + sc + " - проверка пропущена.");
                        else
                        {
                            SetStatus("Проверка контрольной суммы образа (SHA-256)...");
                            string got = Util.Sha256File(p.SourcePath, delegate(int pc)
                            {
                                SetProgress(pc / 4);
                                UI(delegate { lblStatus.Text = "Проверка SHA-256: " + pc + "%"; });
                            });
                            if (!string.Equals(got, em.Value, StringComparison.OrdinalIgnoreCase))
                            { Fail("Контрольная сумма образа не совпала - файл повреждён.\nОжидалось " + em.Value + "\nПолучено   " + got + "\nДиск не тронут."); return; }
                            Log("SHA-256 совпала.");
                        }
                    }
                }

                char winL, efiL = ' ', dataL = ' ', recL = ' ';
                if (p.Keep)
                {
                    winL = p.WinLetter;
                    if (p.Gpt)
                    {
                        char[] l1 = FreeLetters(1);
                        if (l1.Length < 1) { Fail("Нет свободных букв дисков"); return; }
                        efiL = l1[0];
                    }
                }
                else
                {
                    int need = 2 + (p.DataPart ? 1 : 0) + (p.Recovery ? 1 : 0);
                    char[] lt = FreeLetters(need);
                    if (lt.Length < need) { Fail("Нет свободных букв дисков"); return; }
                    winL = lt[0]; efiL = lt[1];
                    int li = 2;
                    if (p.DataPart) dataL = lt[li++];
                    if (p.Recovery) recL = lt[li++];
                }
                string W = winL + ":";

                // 1. разметка / форматирование
                SetStatus(p.Keep ? "Форматирование раздела " + W + " (диск " + p.Disk.Number + ")..."
                                 : "Разметка диска " + p.Disk.Number + " (" + (p.Gpt ? "GPT" : "MBR") + ")...");
                SetProgress(2);
                StringBuilder dp = new StringBuilder();
                dp.AppendLine("rescan");
                if (p.Keep)
                {
                    dp.AppendLine("select disk " + p.Disk.Number);
                    if (p.Gpt)
                    {
                        dp.AppendLine("select partition " + p.EspPart);
                        dp.AppendLine("assign letter=" + efiL);
                    }
                    dp.AppendLine("select partition " + p.WinPart);
                    dp.AppendLine("format quick fs=ntfs label=\"Windows\" override");
                    if (!p.Gpt) dp.AppendLine("active");
                }
                else
                {
                    dp.AppendLine("select disk " + p.Disk.Number);
                    dp.AppendLine("attributes disk clear readonly noerr");
                    dp.AppendLine("clean");
                    if (p.Gpt)
                    {
                        dp.AppendLine("convert gpt");
                        dp.AppendLine("create partition efi size=" + EfiMB);
                        dp.AppendLine("format quick fs=fat32 label=\"EFI\"");
                        dp.AppendLine("assign letter=" + efiL);
                        dp.AppendLine("create partition msr size=" + MsrMB);
                    }
                    else dp.AppendLine("convert mbr");
                    dp.AppendLine(p.WinSizeMB == 0 ? "create partition primary" : "create partition primary size=" + p.WinSizeMB);
                    dp.AppendLine("format quick fs=ntfs label=\"Windows\"");
                    dp.AppendLine("assign letter=" + winL);
                    if (!p.Gpt) dp.AppendLine("active");
                    if (p.Recovery)
                    {
                        // раздел восстановления сразу после Windows; тип и (для GPT) атрибуты по рекомендации Microsoft
                        dp.AppendLine("create partition primary size=" + RecMB);
                        dp.AppendLine("format quick fs=ntfs label=\"Recovery\"");
                        dp.AppendLine("assign letter=" + recL);
                        if (p.Gpt)
                        {
                            dp.AppendLine("set id=\"de94bba4-06d1-4d40-a16a-bfd50179d6ac\"");
                            dp.AppendLine("gpt attributes=0x8000000000000001");
                        }
                        else dp.AppendLine("set id=27");
                    }
                    if (p.DataPart)
                    {
                        dp.AppendLine("create partition primary");
                        dp.AppendLine("format quick fs=ntfs label=\"Data\"");
                        dp.AppendLine("assign letter=" + dataL);
                    }
                }
                dp.AppendLine("exit");
                Log("--- diskpart script ---\n" + dp.ToString() + "-----------------------");

                string diskpart = Util.FindTool("diskpart.exe") ?? "diskpart.exe";
                string script = Path.Combine(Path.GetTempPath(), "wininstaller_diskpart.txt");
                File.WriteAllText(script, dp.ToString(), Encoding.ASCII);
                int rc = RunProc(diskpart, "/s \"" + script + "\"", null, null, false);
                if (rc != 0) { Fail("diskpart завершился с кодом " + rc); return; }
                if (!p.DryRun)
                {
                    Thread.Sleep(1500);
                    if (!Directory.Exists(W + "\\")) { Fail("После разметки не появился раздел " + W); return; }
                    if (p.Gpt && !Directory.Exists(efiL + ":\\")) { Fail("После разметки не появился EFI-раздел " + efiL + ":"); return; }
                }

                // 2. применение образа
                string scratch = W + "\\_dism_tmp";
                if (!p.DryRun) Directory.CreateDirectory(scratch);
                SetStatus("Развёртывание образа (индекс " + p.ImageIndex + ") на " + W + " - это самый долгий этап...");
                SetProgress(5);
                rc = RunProc(dism, "/Apply-Image /ImageFile:\"" + p.ImageFile + "\" /Index:" + p.ImageIndex + " /ApplyDir:" + W + "\\ /ScratchDir:\"" + scratch + "\"",
                    delegate(double v)
                    {
                        SetProgress(5 + (int)(v * 0.85));
                        UI(delegate { lblStatus.Text = "Развёртывание образа: " + v.ToString("0.0") + "%"; });
                    }, null, false);
                if (rc != 0) { Fail("dism /Apply-Image завершился с кодом " + rc); return; }
                if (!p.DryRun && !File.Exists(W + "\\Windows\\System32\\config\\SYSTEM")) { Fail("Образ применён, но Windows\\System32\\config\\SYSTEM не найден"); return; }

                // 3. драйверы
                if (p.DriversDir != null)
                {
                    SetStatus("Добавление драйверов из " + p.DriversDir + "...");
                    SetProgress(91);
                    rc = RunProc(dism, "/Image:" + W + "\\ /Add-Driver /Driver:\"" + p.DriversDir + "\" /Recurse /ScratchDir:\"" + scratch + "\"", null, null, false);
                    if (rc != 0) Log("ПРЕДУПРЕЖДЕНИЕ: добавление драйверов вернуло код " + rc + " (установка продолжается)");
                }

                // 3b. XML ответов
                if (p.Xml != null)
                {
                    // в оба места: Panther\Unattend\Unattend.xml находит Windows, а Panther\unattend.xml читают
                    // скрипты из файла ответов (генератор autounattend извлекает из него свои .ps1)
                    string[] xdests = new string[] { W + "\\Windows\\Panther\\Unattend\\Unattend.xml", W + "\\Windows\\Panther\\unattend.xml" };
                    foreach (string xdest in xdests)
                    {
                        SetStatus("Копирование XML в " + xdest);
                        if (p.DryRun) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(xdest));
                        File.Copy(p.Xml, xdest, true);
                    }
                }

                // 3c. среда восстановления: Winre.wim из образа - в раздел восстановления и регистрация через reagentc
                if (p.Recovery) SetupRecovery(p, W, recL);

                // 4. загрузчик
                if (p.Keep && p.Gpt && !p.DryRun)
                {
                    string oldBcd = efiL + ":\\EFI\\Microsoft\\Boot\\BCD";
                    try
                    {
                        if (File.Exists(oldBcd)) { File.SetAttributes(oldBcd, FileAttributes.Normal); File.Delete(oldBcd); Log("Старый BCD удалён, будет создан заново."); }
                    }
                    catch (Exception ex) { Log("Не удалось удалить старый BCD: " + ex.Message); }
                }
                SetStatus("Установка загрузчика (" + (p.Gpt ? "UEFI" : "BIOS") + ")...");
                SetProgress(95);
                string bargs = p.Gpt
                    ? W + "\\Windows /s " + efiL + ": /f UEFI"
                    : W + "\\Windows /s " + W + " /f BIOS";
                rc = RunProc(bcdboot, bargs, null, null, false);
                if (rc != 0)
                {
                    string alt = W + "\\Windows\\System32\\bcdboot.exe";
                    if (!p.DryRun && File.Exists(alt))
                    {
                        Log("bcdboot из WinPE не справился, пробую bcdboot из установленной Windows");
                        rc = RunProc(alt, bargs, null, null, false);
                    }
                    if (rc != 0) { Fail("bcdboot завершился с кодом " + rc); return; }
                }
                if (!p.Gpt)
                {
                    string bootsect = Util.BootsectPath();
                    if (bootsect != null) RunProc(bootsect, "/nt60 " + W + " /mbr /force", null, null, false);
                }
                if (!p.DryRun)
                {
                    string chk = p.Gpt ? efiL + ":\\EFI\\Microsoft\\Boot\\bootmgfw.efi" : W + "\\bootmgr";
                    if (!File.Exists(chk)) { Fail("Загрузчик не найден после bcdboot: " + chk); return; }
                }

                // 5. уборка
                try { if (!p.DryRun) Directory.Delete(scratch, true); } catch { }
                if (p.Keep && p.Gpt)
                {
                    // убрать временную букву с существующего EFI-раздела
                    File.WriteAllText(script, "select disk " + p.Disk.Number + "\r\nselect partition " + p.EspPart + "\r\nremove letter=" + efiL + "\r\nexit\r\n", Encoding.ASCII);
                    RunProc(diskpart, "/s \"" + script + "\"", null, null, false);
                }
                SetProgress(100);
                Beep(true);
                SetStatus("ГОТОВО. Windows установлена на диск " + p.Disk.Number + " (" + W + ").");
                if (p.Reboot && !p.DryRun)
                {
                    bool go = (bool)Invoke((Func<bool>)delegate { return CountdownDialog(15); });
                    if (go)
                    {
                        UI(delegate { installing = false; });
                        iso.Dismount();
                        string wpeutil = Util.FindTool("wpeutil.exe");
                        if (wpeutil != null) RunProc(wpeutil, "reboot", null, null, true);
                        else RunProc("shutdown.exe", "/r /t 0", null, null, true);
                    }
                    else
                    {
                        Log("Перезагрузка отменена пользователем.");
                        UI(delegate { FinishUi(); });
                    }
                }
                else
                {
                    UI(delegate
                    {
                        MessageBox.Show(this, p.DryRun ? "DRY-RUN завершён: команды смотрите в логе." : "Установка завершена.", "Готово");
                        FinishUi();
                    });
                }
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
        }

        // ------------------------------------------------------------ звук и отсчёт перед перезагрузкой

        static void Beep(bool ok)
        {
            if (Program.DryRun) return;
            try { if (ok) System.Media.SystemSounds.Asterisk.Play(); else System.Media.SystemSounds.Hand.Play(); } catch { }
            try { Console.Beep(ok ? 1000 : 400, 250); } catch { }
        }

        bool CountdownDialog(int seconds)
        {
            using (Form f = new Form())
            {
                f.Text = "Готово"; f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent; f.ClientSize = new Size(440, 160);
                f.MinimizeBox = false; f.MaximizeBox = false; f.ShowInTaskbar = false; f.Font = Font;
                f.BackColor = Theme.Bg; f.ForeColor = Theme.Text;
                f.HandleCreated += delegate { Dark.Title(f.Handle); };
                Label l = Add(f, new Label(), 16, 14, 408, 90);
                l.Font = Theme.Big;
                Button b1 = Add(f, new UiButton { Text = "Перезагрузить сейчас", DialogResult = DialogResult.Yes, Primary = true }, 16, 112, 200, 34);
                Button b2 = Add(f, new UiButton { Text = "Отмена", DialogResult = DialogResult.No }, 224, 112, 200, 34);
                f.AcceptButton = b1; f.CancelButton = b2;
                int left = seconds;
                Action upd = delegate { l.Text = "Установка завершена.\nВЫТАЩИТЕ УСТАНОВОЧНУЮ ФЛЕШКУ!\n\nПерезагрузка через " + left + " с."; };
                upd();
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = 1000;
                t.Tick += delegate { left--; if (left <= 0) { t.Stop(); f.DialogResult = DialogResult.Yes; } else upd(); };
                f.Shown += delegate { t.Start(); };
                bool r = f.ShowDialog(this) == DialogResult.Yes;
                t.Dispose();
                return r;
            }
        }
    }
}
