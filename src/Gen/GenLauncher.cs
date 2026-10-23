// Start-Sysprep.cmd и README для комплекта файлов (порт core/launcher.js).
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WinInstaller
{
    static class GenLauncher
    {
        /// <summary>Стартовый .cmd для Sysprep: проверки, очистка, подтверждение и запуск. Только ASCII.</summary>
        public static string Launcher(JObj st)
        {
            JObj SP = st.O("sysprep");
            List<string> svc = new List<string>();
            foreach (string p in Regex.Split(SP.S("services"), "[\r\n;,]+"))
            {
                string x = p.Trim();
                if (Regex.IsMatch(x, "^[A-Za-z0-9_.$\\-]+\\z")) svc.Add(x);
            }
            string after = SP.S("after");
            string act = after == "reboot" ? "/reboot" : after == "quit" ? "/quit" : "/shutdown";
            List<string> L = new List<string>();
            L.AddRange(new string[] {
                "@echo off", "setlocal EnableExtensions",
                "set \"LOG=%WINDIR%\\Temp\\Sysprep_Precheck.log\"",
                "set \"SYSPREP_DIR=%WINDIR%\\System32\\Sysprep\"",
                "set \"SYSPREP_EXE=%SYSPREP_DIR%\\Sysprep.exe\"",
                "set \"XML=%SYSPREP_DIR%\\unattend.xml\"",
                "echo ===== Sysprep precheck started %DATE% %TIME% ===== > \"%LOG%\"",
                "",
                "net session >nul 2>&1",
                "if not \"%ERRORLEVEL%\"==\"0\" ( echo ERROR: run this file as Administrator. & echo Not elevated. >> \"%LOG%\" & pause & exit /b 1 )",
                "if not exist \"%SYSPREP_EXE%\" ( echo ERROR: Sysprep.exe not found. & echo Sysprep.exe not found. >> \"%LOG%\" & pause & exit /b 2 )",
                "if not exist \"%XML%\" ( echo ERROR: answer file not found: \"%XML%\" & echo Answer file not found. >> \"%LOG%\" & pause & exit /b 3 )",
                "findstr /C:\"<CopyProfile>" + (SP.B("copyProfile") ? "true" : "false") + "</CopyProfile>\" \"%XML%\" >nul 2>&1",
                "if not \"%ERRORLEVEL%\"==\"0\" ( echo ERROR: CopyProfile in the answer file does not match the expected value. & echo CopyProfile check failed. >> \"%LOG%\" & pause & exit /b 4 )" });
            if (SP.S("preScript").Trim().Length > 0)
            {
                L.Add(""); L.Add("rem --- user pre-sysprep script ---");
                L.Add("call " + Regex.Replace(SP.S("preScript").Trim(), "[\r\n]+", " ") + " >> \"%LOG%\" 2>&1");
            }
            if (svc.Count > 0)
            {
                L.Add(""); L.Add("rem --- stop services and set them to manual start ---");
                foreach (string s in svc)
                {
                    L.Add("sc query \"" + s + "\" >nul 2>&1");
                    L.Add("if \"%ERRORLEVEL%\"==\"0\" ( net stop \"" + s + "\" >> \"%LOG%\" 2>&1 & sc config \"" + s + "\" start= demand >> \"%LOG%\" 2>&1 )");
                }
            }
            L.AddRange(new string[] {
                "", "echo.",
                "echo After a successful Sysprep the PC will " + (act == "/shutdown" ? "SHUT DOWN" : act == "/reboot" ? "REBOOT" : "stay on") + ".",
                "echo Do not boot this Windows again before capturing the image.",
                "choice /C YN /N /M \"Start Sysprep now? [Y/N]: \"",
                "if errorlevel 2 ( echo Cancelled. & echo Cancelled by user. >> \"%LOG%\" & exit /b 0 )" });
            if (SP.B("cleanup"))
                L.AddRange(new string[] {
                    "", "echo Cleanup...", "DISM /Online /Cleanup-Image /StartComponentCleanup >> \"%LOG%\" 2>&1",
                    "del /f /q /s \"%TEMP%\\*\" >> \"%LOG%\" 2>&1",
                    "for /d %%D in (\"%TEMP%\\*\") do rd /s /q \"%%D\" >> \"%LOG%\" 2>&1",
                    "for %%F in (\"%WINDIR%\\Temp\\*\") do if /i not \"%%~fF\"==\"%LOG%\" del /f /q \"%%F\" >nul 2>&1",
                    "for /d %%D in (\"%WINDIR%\\Temp\\*\") do rd /s /q \"%%D\" >nul 2>&1" });
            if (SP.B("clearEvt"))
                L.AddRange(new string[] {
                    "", "for /f \"delims=\" %%L in ('wevtutil el') do wevtutil cl \"%%L\" >nul 2>&1",
                    "del /f /q \"%WINDIR%\\Panther\\*.log\" >nul 2>&1",
                    "del /f /q \"%SYSPREP_DIR%\\Panther\\*.log\" >nul 2>&1" });
            L.AddRange(new string[] {
                "", "echo Starting Sysprep... >> \"%LOG%\"", "cd /d \"%SYSPREP_DIR%\"",
                "\"%SYSPREP_EXE%\" /generalize /oobe " + act + " /unattend:\"%XML%\" >> \"%LOG%\" 2>&1",
                "set \"RC=%ERRORLEVEL%\"",
                "if not \"%RC%\"==\"0\" (",
                "    echo ERROR: Sysprep returned code %RC%. See:",
                "    echo   %LOG%",
                "    echo   %SYSPREP_DIR%\\Panther\\setuperr.log",
                "    echo   %SYSPREP_DIR%\\Panther\\setupact.log",
                "    pause",
                "    exit /b %RC%",
                ")",
                "exit /b 0", "" });
            return string.Join("\r\n", L.ToArray());
        }

        /// <summary>README.txt для комплекта файлов.</summary>
        public static string Readme(JObj st, GenResult res)
        {
            bool sys = st.S("mode") == "sysprep";
            List<string> L = new List<string>();
            L.Add("Файлы ответов Windows " + res.Os + " — " + (sys ? "образ Sysprep (unattend.xml)" : "чистая установка (autounattend.xml)"));
            L.Add("Создано программой Buba-Helper, всё формировалось локально."); L.Add("");
            L.Add("СОСТАВ КОМПЛЕКТА");
            L.Add("  " + (sys ? "unattend.xml" : "autounattend.xml") + "  — файл ответов");
            if (sys) L.Add("  Start-Sysprep.cmd  — стартовый скрипт Sysprep");
            L.Add("  settings.json  — ваши настройки (можно загрузить обратно). ВНИМАНИЕ: содержит пароли открытым текстом.");
            L.Add("  scripts\\  — только для просмотра: копии PowerShell/CMD-скриптов, которые уже вшиты в XML. Копировать их никуда не нужно."); L.Add("");
            if (sys)
            {
                L.Add("КАК ИСПОЛЬЗОВАТЬ");
                L.Add("  1. На эталонной машине положите unattend.xml в C:\\Windows\\System32\\Sysprep\\ и настройте систему.");
                L.Add("  2. Запустите Start-Sysprep.cmd от имени администратора и подтвердите запуск.");
                L.Add("  3. После выключения снимите образ (DISM / Clonezilla) и разверните на остальные компьютеры.");
                L.Add("  4. Не запускайте Sysprep повторно на одной и той же установке — используйте снимок виртуальной машины.");
            }
            else
            {
                L.Add("КАК ИСПОЛЬЗОВАТЬ");
                L.Add("  1. Запишите образ Windows на флешку (Media Creation Tool или Rufus).");
                L.Add("  2. Положите autounattend.xml в КОРЕНЬ флешки. Имя файла менять нельзя.");
                L.Add("  3. Загрузитесь с флешки: установщик сам найдёт файл и пройдёт все этапы без вопросов.");
                L.Add("  4. Сначала проверьте на виртуальной машине.");
            }
            L.Add(""); L.Add("ЛОГИ после установки: C:\\Windows\\Setup\\Scripts\\*.log"); L.Add("");
            if (res.Warnings.Count > 0)
            {
                L.Add("ПРЕДУПРЕЖДЕНИЯ ГЕНЕРАТОРА");
                foreach (Warn w in res.Warnings) L.Add("  - " + w.Text);
                L.Add("");
            }
            return string.Join("\r\n", L.ToArray());
        }
    }
}
