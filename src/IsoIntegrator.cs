// Интеграция файлов ответов в ISO (корень диска) и в WIM (Windows\Panther). Без интерфейса: используется окном и ключом --integrate.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

namespace WinInstaller
{
    /// <summary>Один файл ответов и места, куда его положить.</summary>
    class AnswerFile
    {
        public string Xml;
        public bool ToIsoRoot;   // корень ISO под именем Autounattend.xml
        public bool ToWim;       // внутрь install.wim: Windows\Panther\Unattend\Unattend.xml и Windows\Panther\unattend.xml
    }

    class IntegratePlan
    {
        public string Source;                 // .iso или .wim
        public string Out;                    // результат: .iso / .wim, а при FolderOnly - папка с содержимым ISO
        public List<AnswerFile> Files = new List<AnswerFile>();
        public List<int> Indexes;             // редакции WIM, null = все
        public string Label;                  // метка тома нового ISO, null = как у исходного
        public List<string> SysprepFiles = new List<string>();   // например Start-Sysprep.cmd: копируются в Windows\System32\Sysprep внутри WIM
        public string WorkDir;                // null = рядом с результатом
        public bool NoPrompt = true;          // efisys_noprompt.bin: без «Press any key to boot from CD»
        public bool FolderOnly;               // ISO не собирать, оставить папку Out
        public bool DryRun;

        public bool IsIso { get { return Path.GetExtension(Source ?? "").Equals(".iso", StringComparison.OrdinalIgnoreCase); } }
        public bool AnyWim { get { if (SysprepFiles.Count > 0) return true; foreach (AnswerFile f in Files) if (f.ToWim) return true; return false; } }
        public bool AnyRoot { get { foreach (AnswerFile f in Files) if (f.ToIsoRoot) return true; return false; } }
    }

    class IsoIntegrator
    {
        public const string RootName = "Autounattend.xml";
        public static readonly string[] PantherDests = { @"Windows\Panther\Unattend\Unattend.xml", @"Windows\Panther\unattend.xml" };
        public const string SysprepDir = @"Windows\System32\Sysprep";

        /// <summary>XML для Sysprep (образ), а не для чистой установки: в нём нет этапа windowsPE.</summary>
        public static bool IsSysprepXml(string xml)
        {
            try
            {
                XmlDocument d = new XmlDocument(); d.Load(xml);
                foreach (XmlNode n in d.SelectNodes("//*[local-name()='settings']"))
                    if (n.Attributes["pass"] != null && n.Attributes["pass"].Value == "windowsPE") return false;
                return true;
            }
            catch { return false; }
        }

        public Action<string> Log = delegate { };
        public Action<int, string> Progress = delegate { };   // общий процент и текст статуса

        public static string FindOscdimg()
        {
            string p = Util.FindTool("oscdimg.exe");
            if (p != null) return p;
            string pf = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? Environment.GetEnvironmentVariable("ProgramFiles");
            if (pf == null) return null;
            foreach (string arch in new string[] { "amd64", "x86", "arm64" })
            {
                try
                {
                    string f = Path.Combine(pf, @"Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\" + arch + @"\Oscdimg\oscdimg.exe");
                    if (File.Exists(f)) return f;
                }
                catch { }
            }
            return null;
        }

        /// <summary>Проверка плана до начала работы. null - всё в порядке.</summary>
        public static string Validate(IntegratePlan p)
        {
            if (string.IsNullOrEmpty(p.Source) || !File.Exists(p.Source)) return "Образ не найден: " + p.Source;
            string ext = Path.GetExtension(p.Source).ToLowerInvariant();
            if (ext != ".iso" && ext != ".wim") return "Источник должен быть .iso или .wim (ESD изменять нельзя - сначала преобразуйте в WIM).";
            if (string.IsNullOrEmpty(p.Out)) return "Укажите, куда сохранить результат.";
            if (p.Files.Count == 0) return "Не выбран ни один файл ответов.";
            foreach (string sf in p.SysprepFiles) if (!File.Exists(sf)) return "Файл не найден: " + sf;
            int roots = 0;
            foreach (AnswerFile f in p.Files)
            {
                if (!f.ToIsoRoot && !f.ToWim) return "Для файла " + f.Xml + " не выбрано, куда его поместить.";
                if (string.IsNullOrEmpty(f.Xml) || !File.Exists(f.Xml)) return "Файл ответов не найден: " + f.Xml;
                try { XmlDocument d = new XmlDocument(); d.Load(f.Xml); }
                catch (Exception ex) { return "Файл ответов повреждён (" + Path.GetFileName(f.Xml) + "): " + ex.Message; }
                if (f.ToIsoRoot) roots++;
            }
            if (roots > 1) return "В корень ISO можно положить только один файл (Autounattend.xml).";
            if (!p.IsIso)
            {
                if (p.AnyRoot) return "Источник - WIM: в корень ISO класть некуда. Выберите «Внутрь WIM».";
                if (!Path.GetExtension(p.Out).Equals(".wim", StringComparison.OrdinalIgnoreCase)) return "Результат должен быть файлом .wim.";
            }
            else
            {
                if (p.Indexes != null && p.Indexes.Count == 0 && p.AnyWim) return "Выберите хотя бы одну редакцию.";
                if (!p.FolderOnly && !Path.GetExtension(p.Out).Equals(".iso", StringComparison.OrdinalIgnoreCase)) return "Результат должен быть файлом .iso.";
                if (string.Equals(Path.GetFullPath(p.Out), Path.GetFullPath(p.Source), StringComparison.OrdinalIgnoreCase)) return "Нельзя сохранять поверх исходного ISO - укажите другое имя.";
                if (p.FolderOnly && Directory.Exists(p.Out) && Directory.GetFileSystemEntries(p.Out).Length > 0) return "Папка результата не пустая: " + p.Out;
            }
            if (p.AnyWim && !p.DryRun && Util.FindTool("dism.exe") == null) return "Не найден dism.exe.";
            if (p.IsIso && !p.FolderOnly && !p.DryRun && FindOscdimg() == null)
                return "Не найден oscdimg.exe, нужный для сборки ISO. Положите его в папку tools рядом с программой (он входит в Windows ADK, компонент Deployment Tools) "
                     + "или включите «Не собирать ISO».";
            return null;
        }

        // ------------------------------------------------------------ запуск

        public bool Run(IntegratePlan p, out string error)
        {
            error = Validate(p);
            if (error != null) return false;
            if (p.DryRun) { DescribeDry(p); return true; }

            string work = null, mounted = null, dism = null;
            IsoMount iso = null;
            try
            {
                dism = Util.FindTool("dism.exe");
                string fullOut = Path.GetFullPath(p.Out);
                // рабочая папка всегда новая вложенная: в конце она удаляется целиком, поэтому чужие файлы не пострадают
                string workRoot = p.WorkDir != null && p.WorkDir.Trim().Length > 0 ? Path.GetFullPath(p.WorkDir.Trim())
                    : Path.GetDirectoryName(fullOut.TrimEnd('\\')) ?? fullOut;
                work = Path.Combine(workRoot, "_buba_work_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                string scratch = Path.Combine(work, "scratch"), mnt = Path.Combine(work, "mnt");
                Directory.CreateDirectory(scratch); Directory.CreateDirectory(mnt);

                string wimPath, label = p.Label, tmpIso = null, isoDir = null;
                int wimW = 0;   // доля прогресса на изменение WIM
                if (p.IsIso)
                {
                    isoDir = p.FolderOnly ? fullOut : Path.Combine(work, "iso");
                    string e = CheckSpace(p, work, fullOut);
                    if (e != null) { error = e; return false; }
                    Directory.CreateDirectory(isoDir);
                    bool build = !p.FolderOnly;
                    int copyW = p.AnyWim ? 30 : 45;
                    iso = new IsoMount();
                    string mErr;
                    Progress(0, "Подключение ISO...");
                    if (iso.Mount(p.Source, out mErr))
                    {
                        Log("ISO подключён как " + iso.Letter);
                        if (label == null) { try { label = new DriveInfo(iso.Letter).VolumeLabel; } catch { } }
                        CopyTree(iso.Letter + "\\", isoDir, 0, copyW);
                        iso.Dismount();
                    }
                    else
                    {
                        Log("Монтирование ISO не удалось (" + mErr + "), пробую 7-Zip");
                        string sz = Util.FindTool("7z.exe");
                        if (sz == null) { error = "Не удалось подключить ISO (" + mErr + ") и не найден 7z.exe (положите в папку tools рядом с программой)."; return false; }
                        int rc = Exec(sz, "x -y -bsp1 -o\"" + isoDir + "\" \"" + p.Source + "\"", delegate(double v) { Progress((int)(v * copyW / 100), "Распаковка ISO: " + (int)v + "%"); });
                        if (rc != 0) { error = "7-Zip завершился с кодом " + rc; return false; }
                    }
                    ClearReadOnly(isoDir);
                    wimPath = Path.Combine(isoDir, @"sources\install.wim");
                    if (!File.Exists(wimPath))
                    {
                        error = File.Exists(Path.Combine(isoDir, @"sources\install.esd")) && p.AnyWim
                            ? "В ISO install.esd, а не install.wim: ESD изменять нельзя. Положите файл ответов только в корень ISO или преобразуйте ESD в WIM."
                            : (p.AnyWim ? "В ISO нет sources\\install.wim." : null);
                        if (error != null) return false;
                    }
                    wimW = p.AnyWim ? 25 : 0;
                    if (p.AnyWim) PatchWim(p, wimPath, dism, mnt, scratch, copyW, wimW, ref mounted);
                    if (p.AnyRoot)
                    {
                        foreach (AnswerFile f in p.Files)
                            if (f.ToIsoRoot)
                            {
                                string dst = Path.Combine(isoDir, RootName);
                                if (File.Exists(dst)) Log("В ISO уже был " + RootName + " - заменяю.");
                                File.Copy(f.Xml, dst, true);
                                Log("Файл ответов -> корень ISO: " + dst);
                            }
                    }
                    if (build)
                    {
                        string osc = FindOscdimg();
                        tmpIso = fullOut + ".tmp";
                        if (File.Exists(tmpIso)) File.Delete(tmpIso);
                        Directory.CreateDirectory(Path.GetDirectoryName(fullOut));
                        string bd = BootData(isoDir, p.NoPrompt);
                        string lbl = (label ?? "WINDOWS").Replace("\"", "");
                        if (lbl.Length > 32) lbl = lbl.Substring(0, 32);
                        int bs = copyW + wimW;
                        string args = "-m -o -u2 -udfver102 -l\"" + lbl + "\" " + bd + " \"" + isoDir + "\" \"" + tmpIso + "\"";
                        int rc = Exec(osc, args, delegate(double v) { Progress(bs + (int)(v * (99 - bs) / 100), "Сборка ISO: " + (int)v + "%"); });
                        if (rc != 0) { error = "oscdimg завершился с кодом " + rc; return false; }
                        if (!File.Exists(tmpIso) || new FileInfo(tmpIso).Length < 100L * 1048576) { error = "oscdimg отработал, но ISO не создан или слишком мал."; return false; }
                        if (File.Exists(fullOut)) File.Delete(fullOut);
                        File.Move(tmpIso, fullOut);
                        tmpIso = null;
                        Verify(fullOut, p);
                        Log("Готово: " + fullOut + " (" + Util.FmtSize(new FileInfo(fullOut).Length) + ")");
                    }
                    else Log("Готово. Содержимое ISO с файлами ответов: " + fullOut);
                }
                else
                {
                    wimPath = fullOut;
                    if (!string.Equals(Path.GetFullPath(p.Source), fullOut, StringComparison.OrdinalIgnoreCase))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(fullOut));
                        CopyFile(p.Source, fullOut, 0, 30, "Копирование WIM");
                        try { File.SetAttributes(fullOut, FileAttributes.Normal); } catch { }
                    }
                    PatchWim(p, wimPath, dism, mnt, scratch, 30, 69, ref mounted);
                    Log("Готово: " + fullOut);
                }
                Progress(100, "Готово");
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
            finally
            {
                if (iso != null) iso.Dismount();
                if (mounted != null && dism != null)
                {
                    Log("Откат: образ остался смонтированным, размонтирую без сохранения...");
                    try { Exec(dism, "/Unmount-Image /MountDir:\"" + mounted + "\" /Discard", null); } catch { }
                    try { Exec(dism, "/Cleanup-Mountpoints", null); } catch { }
                }
                try
                {
                    if (work != null && Directory.Exists(work)) Directory.Delete(work, true);
                }
                catch (Exception ex) { Log("Рабочая папка не удалена (" + work + "): " + ex.Message); }
                if (error != null)
                {
                    try { string t = Path.GetFullPath(p.Out) + ".tmp"; if (File.Exists(t)) File.Delete(t); } catch { }
                }
            }
        }

        // ------------------------------------------------------------ WIM

        void PatchWim(IntegratePlan p, string wim, string dism, string mnt, string scratch, int baseP, int span, ref string mounted)
        {
            List<ImageInfo> all = WimReader.FromHeader(wim);
            if (all == null) { List<string> raw = new List<string>(); all = WimReader.FromDism(wim, raw); }
            if (all.Count == 0) throw new Exception("В WIM не найдено ни одной редакции.");
            List<ImageInfo> todo = new List<ImageInfo>();
            foreach (ImageInfo im in all)
                if (p.Indexes == null || p.Indexes.Contains(im.Index)) todo.Add(im);
            if (todo.Count == 0) throw new Exception("Выбранных редакций нет в WIM.");
            for (int i = 0; i < todo.Count; i++)
            {
                ImageInfo im = todo[i];
                int b0 = baseP + span * i / todo.Count, w = Math.Max(1, span / todo.Count);
                string tag = "Редакция " + im.Index + " (" + (i + 1) + " из " + todo.Count + ")";
                Progress(b0, tag + ": подключение...");
                int rc = Exec(dism, "/Mount-Image /ImageFile:\"" + wim + "\" /Index:" + im.Index + " /MountDir:\"" + mnt + "\" /ScratchDir:\"" + scratch + "\"",
                    delegate(double v) { Progress(b0 + (int)(v * w * 0.4 / 100), tag + ": подключение " + (int)v + "%"); });
                if (rc != 0) throw new Exception("dism /Mount-Image (редакция " + im.Index + ") завершился с кодом " + rc);
                mounted = mnt;
                if (!Directory.Exists(Path.Combine(mnt, "Windows"))) throw new Exception("После подключения в " + mnt + " нет папки Windows.");
                foreach (AnswerFile f in p.Files)
                {
                    if (!f.ToWim) continue;
                    foreach (string d in PantherDests)
                    {
                        string dst = Path.Combine(mnt, d);
                        Directory.CreateDirectory(Path.GetDirectoryName(dst));
                        File.Copy(f.Xml, dst, true);
                    }
                    Log("Файл ответов -> WIM (индекс " + im.Index + "): Windows\\Panther\\Unattend\\Unattend.xml и Windows\\Panther\\unattend.xml");
                    if (IsSysprepXml(f.Xml))
                    {
                        string sd = Path.Combine(mnt, SysprepDir);
                        Directory.CreateDirectory(sd);
                        File.Copy(f.Xml, Path.Combine(sd, "unattend.xml"), true);   // его берёт Start-Sysprep.cmd
                        Log("  и " + SysprepDir + "\\unattend.xml (файл для Sysprep)");
                    }
                }
                foreach (string sf in p.SysprepFiles)
                {
                    string sd = Path.Combine(mnt, SysprepDir);
                    Directory.CreateDirectory(sd);
                    File.Copy(sf, Path.Combine(sd, Path.GetFileName(sf)), true);
                    Log("Файл -> WIM (индекс " + im.Index + "): " + SysprepDir + "\\" + Path.GetFileName(sf));
                }
                rc = Exec(dism, "/Unmount-Image /MountDir:\"" + mnt + "\" /Commit",
                    delegate(double v) { Progress(b0 + (int)(w * 0.4) + (int)(v * w * 0.6 / 100), tag + ": сохранение " + (int)v + "%"); });
                if (rc != 0) throw new Exception("dism /Unmount-Image /Commit (редакция " + im.Index + ") завершился с кодом " + rc);
                mounted = null;
            }
        }

        // ------------------------------------------------------------ ISO

        static string BootData(string isoDir, bool noPrompt)
        {
            string bios = Path.Combine(isoDir, @"boot\etfsboot.com");
            string efiDir = Path.Combine(isoDir, @"efi\microsoft\boot");
            string efi = Path.Combine(efiDir, noPrompt ? "efisys_noprompt.bin" : "efisys.bin");
            if (!File.Exists(efi)) efi = Path.Combine(efiDir, "efisys.bin");
            bool hasBios = File.Exists(bios), hasEfi = File.Exists(efi);
            if (hasBios && hasEfi) return "-bootdata:2#p0,e,b\"" + bios + "\"#pEF,e,b\"" + efi + "\"";
            if (hasBios) return "-b\"" + bios + "\"";
            if (hasEfi) return "-bootdata:1#pEF,e,b\"" + efi + "\"";
            throw new Exception("В образе нет загрузочных файлов (boot\\etfsboot.com, efi\\microsoft\\boot\\efisys.bin) - это не загрузочный ISO Windows.");
        }

        void Verify(string iso, IntegratePlan p)
        {
            if (!p.AnyRoot) return;
            IsoMount m = new IsoMount();
            try
            {
                string e;
                if (!m.Mount(iso, out e)) { Log("Проверка итогового ISO пропущена: " + e); return; }
                if (!File.Exists(m.Letter + "\\" + RootName)) throw new Exception("В готовом ISO нет " + RootName + ".");
                Log("Проверка: " + RootName + " на месте.");
            }
            finally { m.Dismount(); }
        }

        string CheckSpace(IntegratePlan p, string work, string fullOut)
        {
            try
            {
                long iso = new FileInfo(p.Source).Length;
                long needWork = iso + iso / 20 + 512L * 1048576, needOut = p.FolderOnly ? 0 : iso + 200L * 1048576;
                string wr = Path.GetPathRoot(work), orr = Path.GetPathRoot(fullOut);
                if (wr == null || wr.StartsWith("\\\\")) return null;
                if (p.FolderOnly) needWork = 512L * 1048576;
                bool same = string.Equals(wr, orr, StringComparison.OrdinalIgnoreCase);
                long freeW = new DriveInfo(wr).AvailableFreeSpace;
                if (same ? freeW < needWork + needOut : freeW < needWork)
                    return "Мало места на " + wr + ": свободно " + Util.FmtSize(freeW) + ", нужно около " + Util.FmtSize(same ? needWork + needOut : needWork) + ". Укажите другую рабочую папку.";
                if (!same && needOut > 0 && orr != null && !orr.StartsWith("\\\\"))
                {
                    long freeO = new DriveInfo(orr).AvailableFreeSpace;
                    if (freeO < needOut) return "Мало места на " + orr + ": свободно " + Util.FmtSize(freeO) + ", нужно около " + Util.FmtSize(needOut) + ".";
                }
            }
            catch { }
            return null;
        }

        void DescribeDry(IntegratePlan p)
        {
            Log("[dry-run] источник: " + p.Source + (p.IsIso ? " (ISO)" : " (WIM)"));
            foreach (AnswerFile f in p.Files)
                Log("[dry-run] " + f.Xml + " -> " + (f.ToIsoRoot ? "корень ISO (" + RootName + ")" : "") + (f.ToIsoRoot && f.ToWim ? " и " : "") + (f.ToWim ? "внутрь WIM (Windows\\Panther)" : ""));
            Log("[dry-run] результат: " + p.Out + (p.FolderOnly ? " (папка)" : ""));
            Progress(100, "DRY-RUN завершён");
        }

        // ------------------------------------------------------------ файлы и процессы

        static void ClearReadOnly(string dir)
        {
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { FileAttributes a = File.GetAttributes(f); if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(f, a & ~FileAttributes.ReadOnly); } catch { }
            }
        }

        void CopyTree(string src, string dst, int baseP, int span)
        {
            string[] files = Directory.GetFiles(src, "*", SearchOption.AllDirectories);
            long total = 0, done = 0;
            foreach (string f in files) total += new FileInfo(f).Length;
            total = Math.Max(1, total);
            int last = -1;
            byte[] buf = new byte[4 << 20];
            foreach (string f in files)
            {
                string rel = f.Substring(src.Length).TrimStart('\\');
                string to = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                using (FileStream a = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
                using (FileStream b = new FileStream(to, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                {
                    int n;
                    while ((n = a.Read(buf, 0, buf.Length)) > 0)
                    {
                        b.Write(buf, 0, n);
                        done += n;
                        int pc = (int)(done * 100 / total);
                        if (pc != last) { last = pc; Progress(baseP + pc * span / 100, "Копирование файлов ISO: " + pc + "%"); }
                    }
                }
            }
            Log("Скопировано файлов: " + files.Length + " (" + Util.FmtSize(total) + ")");
        }

        void CopyFile(string src, string dst, int baseP, int span, string what)
        {
            long total = Math.Max(1, new FileInfo(src).Length), done = 0;
            int last = -1;
            byte[] buf = new byte[4 << 20];
            using (FileStream a = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            using (FileStream b = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                int n;
                while ((n = a.Read(buf, 0, buf.Length)) > 0)
                {
                    b.Write(buf, 0, n);
                    done += n;
                    int pc = (int)(done * 100 / total);
                    if (pc != last) { last = pc; Progress(baseP + pc * span / 100, what + ": " + pc + "%"); }
                }
            }
        }

        /// <summary>Запускает процесс; проценты из вывода (dism, oscdimg, 7z) уходят в onPercent, остальное - в журнал.</summary>
        int Exec(string exe, string args, Action<double> onPercent)
        {
            Log("> " + Path.GetFileName(exe) + " " + args);
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            Encoding enc = OemEnc.Get();
            psi.StandardOutputEncoding = enc; psi.StandardErrorEncoding = enc;
            using (Process pr = Process.Start(psi))
            {
                Action<string> handle = delegate(string raw)
                {
                    string line = raw.Trim();
                    if (line.Length == 0) return;
                    MatchCollection ms = Regex.Matches(line, @"(\d{1,3}(?:[.,]\d+)?)\s*%");
                    if (ms.Count > 0 && (line.StartsWith("[") || line.Contains("% complete") || Regex.IsMatch(line, @"^\d{1,3}%")))
                    {
                        double v;
                        if (onPercent != null && double.TryParse(ms[ms.Count - 1].Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) onPercent(v);
                        return;
                    }
                    Log("  " + line);
                };
                Thread te = new Thread(delegate() { try { string s; while ((s = pr.StandardError.ReadLine()) != null) handle(s); } catch { } });   // oscdimg пишет прогресс в stderr
                te.IsBackground = true; te.Start();
                StringBuilder sb = new StringBuilder();
                Action flush = delegate() { string line = sb.ToString(); sb.Length = 0; handle(line); };
                int ch;
                while ((ch = pr.StandardOutput.Read()) >= 0)
                {
                    if (ch == '\r' || ch == '\n' || ch == '\b') flush(); else sb.Append((char)ch);
                }
                flush();
                pr.WaitForExit();
                te.Join(2000);
                return pr.ExitCode;
            }
        }
    }

    /// <summary>Ключ командной строки: Buba-Helper.exe --integrate &lt;источник&gt; &lt;результат&gt; [--root xml] [--wim xml] [--both xml] [--index 3,4] [--sysprep-cmd Start-Sysprep.cmd] [--label L] [--work dir] [--folder] [--prompt] [--dry-run]</summary>
    static class IntegrateCli
    {
        public static int Run(string[] a)
        {
            if (a.Length < 3) return 2;
            IntegratePlan p = new IntegratePlan();
            p.Source = a[1]; p.Out = a[2];
            for (int i = 3; i < a.Length; i++)
            {
                string k = a[i].ToLowerInvariant();
                bool more = i + 1 < a.Length;
                if (k == "--root" && more) p.Files.Add(new AnswerFile { Xml = a[++i], ToIsoRoot = true });
                else if (k == "--wim" && more) p.Files.Add(new AnswerFile { Xml = a[++i], ToWim = true });
                else if (k == "--both" && more) p.Files.Add(new AnswerFile { Xml = a[++i], ToIsoRoot = true, ToWim = true });
                else if (k == "--index" && more)
                {
                    p.Indexes = new List<int>();
                    foreach (string s in a[++i].Split(',')) { int n; if (int.TryParse(s, out n)) p.Indexes.Add(n); }
                }
                else if (k == "--sysprep-cmd" && more) p.SysprepFiles.Add(a[++i]);
                else if (k == "--label" && more) p.Label = a[++i];
                else if (k == "--work" && more) p.WorkDir = a[++i];
                else if (k == "--folder") p.FolderOnly = true;
                else if (k == "--prompt") p.NoPrompt = false;
                else if (k == "--dry-run") p.DryRun = true;
            }
            string logFile = p.Out.TrimEnd('\\') + ".log";
            IsoIntegrator ig = new IsoIntegrator();
            ig.Log = delegate(string s) { try { File.AppendAllText(logFile, DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine, Encoding.UTF8); } catch { } };
            string err;
            bool ok = ig.Run(p, out err);
            if (!ok) ig.Log("ОШИБКА: " + err);
            return ok ? 0 : 1;
        }
    }
}
