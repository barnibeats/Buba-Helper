// Командная строка генератора (без окна):
//   WinInstaller.exe --gen settings.json out.xml [--launcher Start-Sysprep.cmd] [--preset id]
// Рядом с out.xml пишется out.xml.log: проблемы настроек («настройки: …») и предупреждения («! …»).
// Тот же формат, что у node tools/generate.js оригинала, - по нему devtools/gen-compare.js сверяет результат.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WinInstaller
{
    static class GenCli
    {
        public static int Run(string[] args)
        {
            string inFile = null, outFile = null, launcher = null, preset = null;
            List<string> pos = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--launcher" && i + 1 < args.Length) launcher = args[++i];
                else if (args[i] == "--preset" && i + 1 < args.Length) preset = args[++i];
                else pos.Add(args[i]);
            }
            if (pos.Count > 0) inFile = pos[0];
            if (pos.Count > 1) outFile = pos[1];
            if (inFile == null || outFile == null) return 2;
            StringBuilder log = new StringBuilder();
            try
            {
                List<string> problems = new List<string>();
                JObj state = GenState.Sanitize(Json.Parse(File.ReadAllText(inFile, Encoding.UTF8)), problems);
                foreach (string p in problems) log.Append("настройки: ").Append(p).Append('\n');
                if (preset != null) GenPresets.Apply(preset, state);
                GenResult res = Generator.Generate(state);
                File.WriteAllText(outFile, res.Xml, new UTF8Encoding(false));
                foreach (Warn w in res.Warnings) log.Append("! ").Append(w.Text).Append('\n');
                if (launcher != null) File.WriteAllText(launcher, GenLauncher.Launcher(state), new UTF8Encoding(false));   // как в оригинале: text/plain;charset=utf-8
                File.WriteAllText(outFile + ".log", log.ToString(), new UTF8Encoding(false));
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(outFile + ".log", "ОШИБКА: " + ex, new UTF8Encoding(false)); } catch { }
                return 1;
            }
        }
    }
}
