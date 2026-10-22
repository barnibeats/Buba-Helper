// Твики Buba-Helper поверх каталога оригинального генератора: настройки «для образа» (оболочка, политики, отдельные
// правила для встроенного Администратора и остальных пользователей). Живут в коде, а не в gendata.json: этот файл
// перезаписывается выгрузкой из оригинала (devtools/export-gen-data.js).
using System;
using System.Collections.Generic;

namespace WinInstaller
{
    static class GenExtras
    {
        const string ExplorerPol = "HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer";
        const string SystemPol = "HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System";
        const string DefExplorerPol = "HKU\\DefUser\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer";
        const string DefSystemPol = "HKU\\DefUser\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System";
        const string UserExplorerPol = "HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer";
        const string UserSystemPol = "HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System";
        const string UserSearch = "HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Search";
        const string UserDeskIcons = "HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\HideDesktopIcons\\NewStartPanel";

        // Windows при первом входе включает поле поиска заново (onboarding), даже если в шаблоне профиля стоит 0.
        // Поэтому: пометка «onboarding выполнен» + проверка при каждом входе после запуска Проводника (один перезапуск Проводника, если поле вернулось).
        const string HideSearchPs1 = @"# запускается при входе: если Windows вернула поле поиска - скрыть и один раз перезапустить Проводник этого пользователя
$k = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search'
$m = (Get-ItemProperty $k -ErrorAction SilentlyContinue).SearchboxTaskbarMode
if ($m -eq 0) { exit 0 }
Start-Sleep -Seconds 10
Set-ItemProperty -Path $k -Name 'SearchboxTaskbarMode' -Value 0 -Type DWord
$sid = (Get-Process -Id $PID).SessionId
Get-Process explorer -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $sid } | Stop-Process -Force";

        // Папки профиля в «Этом компьютере»: ключи FolderDescriptions принадлежат TrustedInstaller, поэтому сначала берём владение.
        // «Объемные объекты» живут в MyComputer\NameSpace - там просто удаляется ключ.
        public const string ThisPcPs1 = @"$ErrorActionPreference = 'Continue'
Start-Transcript -Path ""$env:SystemRoot\Setup\Scripts\ThisPC.log"" -Force | Out-Null
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class Priv {
    [DllImport(""advapi32.dll"", SetLastError = true)] static extern bool OpenProcessToken(IntPtr h, int access, out IntPtr tok);
    [DllImport(""advapi32.dll"", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool LookupPrivilegeValue(string host, string name, out long luid);
    [DllImport(""advapi32.dll"", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr tok, bool disableAll, ref TokPriv newState, int len, IntPtr prev, IntPtr relen);
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct TokPriv { public int Count; public long Luid; public int Attr; }
    public static bool Enable(string name) {
        IntPtr tok;
        if (!OpenProcessToken(System.Diagnostics.Process.GetCurrentProcess().Handle, 0x28, out tok)) return false;
        TokPriv tp = new TokPriv(); tp.Count = 1; tp.Attr = 2;
        if (!LookupPrivilegeValue(null, name, out tp.Luid)) return false;
        return AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
    }
}
'@
[Priv]::Enable('SeTakeOwnershipPrivilege') | Out-Null
[Priv]::Enable('SeRestorePrivilege') | Out-Null
$admins = New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')
$ids = [ordered]@{
    'Видео'        = '{35286a68-3c57-41a1-bbb1-0eae73d76c95}'
    'Документы'    = '{f42ee2d3-909f-4907-8871-4c22fc0bf756}'
    'Загрузки'     = '{7d83ee9b-2244-4e70-b1f5-5393042af1e4}'
    'Изображения'  = '{0ddd015d-b06c-45d5-8c4c-f59713854639}'
    'Музыка'       = '{a0c69a99-21c8-4671-8703-7934162fcf1d}'
    'Рабочий стол' = '{B4BFCC3A-DB2C-424C-B029-7FE99A87C641}'
}
foreach ($view in 'SOFTWARE', 'SOFTWARE\Wow6432Node') {
    foreach ($name in $ids.Keys) {
        $sub = ""$view\Microsoft\Windows\CurrentVersion\Explorer\FolderDescriptions\$($ids[$name])\PropertyBag""
        try {
            $k = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey($sub, 'ReadWriteSubTree', 'TakeOwnership')
            if (-not $k) { Write-Output ""нет ключа: $sub""; continue }
            $acl = $k.GetAccessControl('None'); $acl.SetOwner($admins); $k.SetAccessControl($acl); $k.Close()
            $k = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey($sub, 'ReadWriteSubTree', 'ChangePermissions')
            $acl = $k.GetAccessControl()
            $acl.AddAccessRule((New-Object Security.AccessControl.RegistryAccessRule($admins, 'FullControl', 'ContainerInherit', 'None', 'Allow')))
            $k.SetAccessControl($acl); $k.Close()
            Set-ItemProperty -Path ""HKLM:\$sub"" -Name 'ThisPCPolicy' -Value 'Hide'
            Write-Output ""скрыто: $name ($view)""
        } catch { Write-Output ""ОШИБКА $name ($view): $($_.Exception.Message)"" }
    }
}
foreach ($view in 'SOFTWARE', 'SOFTWARE\Wow6432Node') {
    $p = ""HKLM:\$view\Microsoft\Windows\CurrentVersion\Explorer\MyComputer\NameSpace\{0DB7E03F-FC29-4DC6-9020-FF41B59E513A}""
    if (Test-Path $p) {
        try { Remove-Item $p -Force -Recurse -ErrorAction Stop; Write-Output ""удалён: Объемные объекты ($view)"" }
        catch { Write-Output ""ОШИБКА Объемные объекты ($view): $($_.Exception.Message)"" }
    }
}
Stop-Transcript | Out-Null";

        static Tw Mk(string id, string label, string hint, string os, params string[] lines)
        {
            Tw t = new Tw(); t.Id = id; t.Label = label; t.Hint = hint ?? ""; t.Warn = ""; t.Os = os ?? ""; t.Lines = lines;
            return t;
        }

        public static void Patch()
        {
            // поле поиска: пометка onboarding + проверка при каждом входе
            Tw s;
            if (Cat.TwIndex.TryGetValue("nosearchbox", out s))
            {
                s.Hint = "Windows при первом входе может вернуть поле поиска; поэтому дополнительно ставится пометка «onboarding выполнен» и при входе запускается проверка (без окна), которая один раз перезапускает Проводник, если поле вернулось.";
                s.UserLines = new string[] {
                    "SetReg '" + UserSearch + "' 'SearchboxTaskbarMode' 0 DWord",
                    "SetReg '" + UserSearch + "' 'OnboardSearchboxOnTaskbar' 1 DWord",
                    "SetReg '" + UserSearch + "' 'OnboardSBEmode' 1 DWord" };
                s.Files["HideSearch.ps1"] = HideSearchPs1;
                s.RunAtLogon = new string[] { "HideSearch.ps1" };
            }

            TwGroup g = new TwGroup();
            g.Name = "Образ и рабочее место (Buba-Helper)";

            Tw t = Mk("nomeetnow", "Убрать значок «Провести собрание» (Meet Now) из области уведомлений",
                "Политика HideSCAMeetNow: для всей системы, для новых пользователей и для уже существующих профилей.", "10",
                "RegSet '" + ExplorerPol + "' 'HideSCAMeetNow' REG_DWORD '1'",
                "RegSet '" + DefExplorerPol + "' 'HideSCAMeetNow' REG_DWORD '1'");
            t.UserLines = new string[] { "SetReg '" + UserExplorerPol + "' 'HideSCAMeetNow' 1 DWord" };
            g.Items.Add(t);

            t = Mk("lockthemes", "Запретить смену темы, цветов и фона для всех пользователей",
                "Скрывает страницы «Параметров» (Темы, Цвета, Фон) и включает классические запреты NoThemesTab и NoDispAppearancePage. Обои задаются отдельно (страница «Рабочий стол и ярлыки»).", "",
                "RegSet '" + ExplorerPol + "' 'NoThemesTab' REG_DWORD '1'",
                "RegSet '" + SystemPol + "' 'NoDispAppearancePage' REG_DWORD '1'",
                "RegSet '" + DefExplorerPol + "' 'NoThemesTab' REG_DWORD '1'",
                "RegSet '" + DefSystemPol + "' 'NoDispAppearancePage' REG_DWORD '1'");
            t.UserLines = new string[] {
                "SetReg '" + UserExplorerPol + "' 'NoThemesTab' 1 DWord",
                "SetReg '" + UserSystemPol + "' 'NoDispAppearancePage' 1 DWord" };
            t.HidePages = new string[] { "themes", "colors", "personalization-background" };
            g.Items.Add(t);

            t = Mk("nowuforusers", "Центр обновления Windows: скрыть и запретить всем, кроме встроенного Администратора",
                "Чтобы обновление не запустили случайно. Для всех остальных пользователей страница «Центр обновления Windows» скрывается в «Параметрах» и включается политика DisableWindowsUpdateAccess. Если у пользователя права администратора, запрет защищает от случайных нажатий, а не от осознанных действий.", "");
            t.OtherLines = new string[] { "SetReg 'HKCU:\\Software\\Policies\\Microsoft\\Windows\\WindowsUpdate' 'DisableWindowsUpdateAccess' 1 DWord" };
            t.HidePagesOther = new string[] { "windowsupdate" };
            g.Items.Add(t);

            t = Mk("admindeskicons", "Значки «Этот компьютер» и «Панель управления» на рабочем столе только у встроенного Администратора",
                "Включается при первом входе встроенного Администратора (профиль Default остаётся без этих значков). Для всех сразу есть твик «Значки на рабочем столе» выше.", "", new string[0]);
            t.AdminLines = new string[] {
                "SetReg '" + UserDeskIcons + "' '{20D04FE0-3AEA-1069-A2D8-08002B30309D}' 0 DWord",
                "SetReg '" + UserDeskIcons + "' '{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}' 0 DWord" };
            g.Items.Add(t);

            t = Mk("hidepcfolders", "Скрыть папки профиля (Видео, Документы, Загрузки, Изображения, Музыка, Рабочий стол, Объемные объекты) в «Этом компьютере»",
                "Раздел «Папки» из «Этого компьютера» пропадает; сами папки остаются на диске. Ключи реестра принадлежат TrustedInstaller, скрипт берёт владение (specialize, SYSTEM).", "", new string[0]);
            t.Special = "pcfolders";
            g.Items.Add(t);

            foreach (Tw it in g.Items) Cat.TwIndex[it.Id] = it;
            Cat.Tweaks.Add(g);
        }
    }
}
