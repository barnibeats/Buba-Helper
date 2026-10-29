@echo off
rem Offline network diagnosis + fix of an INSTALLED Windows, run from WinPE as administrator.
rem   net-fix-offline.cmd [C:] [/fix]
rem Without /fix it only prints a report. With /fix it also:
rem   - turns off Fast Startup (HiberbootEnabled=0) and deletes hiberfil.sys;
rem   - removes device-installation restrictions (group policy left in a cloned image);
rem   - deletes the stale PnP entries of the NIC (Intel I219 / I210 / Realtek 8168 / 8125), so Windows detects the card
rem     as a NEW device on the next boot and installs the driver already staged in its driver store;
rem   - makes sure the NIC driver services start (Start=3).
rem The report is also saved to %TEMP%\net-diag.txt.
setlocal enabledelayedexpansion
set "W=C:"
set "FIX=0"
for %%A in (%*) do (
  if /i "%%~A"=="/fix" (set "FIX=1") else (set "W=%%~A")
)
if "%W:~-1%"=="\" set "W=%W:~0,-1%"
if not exist "%W%\Windows\System32\config\SYSTEM" (echo No Windows found on %W% & exit /b 1)
set "OUT=%TEMP%\net-diag.txt"
call :run > "%OUT%" 2>&1
type "%OUT%"
echo.
echo Report saved: %OUT%
exit /b 0

:run
echo ===== Windows on %W%   fix=%FIX% =====
reg unload HKLM\OFFSYS >nul 2>&1
reg unload HKLM\OFFSOFT >nul 2>&1
reg load HKLM\OFFSYS "%W%\Windows\System32\config\SYSTEM" >nul || (echo cannot load SYSTEM hive & exit /b 1)
reg load HKLM\OFFSOFT "%W%\Windows\System32\config\SOFTWARE" >nul || echo cannot load SOFTWARE hive
set "CSN=1"
for /f "tokens=3" %%C in ('reg query HKLM\OFFSYS\Select /v Current ^| find "Current"') do set /a CSN=%%C
set "CSET=00%CSN%"
set "CSET=ControlSet%CSET:~-3%"
echo Active control set: %CSET%
echo.
echo ===== Fast Startup / hibernation =====
if exist "%W%\hiberfil.sys" (echo hiberfil.sys: EXISTS) else (echo hiberfil.sys: not present)
reg query "HKLM\OFFSYS\%CSET%\Control\Session Manager\Power" /v HiberbootEnabled 2>nul
if "%FIX%"=="1" (
  reg add "HKLM\OFFSYS\%CSET%\Control\Session Manager\Power" /v HiberbootEnabled /t REG_DWORD /d 0 /f >nul && echo [fix] HiberbootEnabled=0
  if exist "%W%\hiberfil.sys" (attrib -s -h -r "%W%\hiberfil.sys" & del /f /q "%W%\hiberfil.sys" & echo [fix] hiberfil.sys deleted)
)
echo.
echo ===== Device installation restrictions (policy) =====
reg query "HKLM\OFFSOFT\Policies\Microsoft\Windows\DeviceInstallation" /s 2>nul
reg query "HKLM\OFFSOFT\Policies\Microsoft\Windows\DeviceInstallation" >nul 2>&1 && (
  if "%FIX%"=="1" reg delete "HKLM\OFFSOFT\Policies\Microsoft\Windows\DeviceInstallation" /f >nul && echo [fix] restrictions removed
) || echo (none)
echo.
echo ===== NIC entries known to this Windows (Enum\PCI) =====
call :nic 8086 15FA
call :nic 8086 1533
call :nic 10EC 8168
call :nic 10EC 8125
echo.
echo ===== Driver services (Start: 0x3 = manual/demand, 0x4 = DISABLED) =====
for %%S in (e1dexpress e1rexpress rt640x64 rt25cx21x64 Ndis Tcpip NetSetupSvc nsi) do (
  echo [%%S]
  reg query "HKLM\OFFSYS\%CSET%\Services\%%S" /v Start 2>nul
  if "%FIX%"=="1" if /i not "%%S"=="Ndis" if /i not "%%S"=="Tcpip" if /i not "%%S"=="NetSetupSvc" if /i not "%%S"=="nsi" (
    reg query "HKLM\OFFSYS\%CSET%\Services\%%S" >nul 2>&1 && reg add "HKLM\OFFSYS\%CSET%\Services\%%S" /v Start /t REG_DWORD /d 3 /f >nul && echo     [fix] Start=3
  )
)
echo.
echo ===== Net class filters (UpperFilters / LowerFilters) =====
reg query "HKLM\OFFSYS\%CSET%\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}" /v UpperFilters 2>nul
reg query "HKLM\OFFSYS\%CSET%\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}" /v LowerFilters 2>nul
echo.
echo ===== Adapters ever registered (NetworkCards) =====
reg query "HKLM\OFFSOFT\Microsoft\Windows NT\CurrentVersion\NetworkCards" /s 2>nul | findstr /i "Description ServiceName"
echo.
echo ===== Drivers staged in the driver store =====
dir /b "%W%\Windows\System32\DriverStore\FileRepository" | findstr /i "^e1d ^e1r ^rt640 ^rt25 ^netrtl"
echo.
echo ===== setupapi.dev.log: last lines about the NIC =====
if exist "%W%\Windows\INF\setupapi.dev.log" (
  findstr /i /c:"15FA" /c:"e1d68x64" /c:"e1d.inf" /c:"DEV_8168" /c:"DEV_8125" "%W%\Windows\INF\setupapi.dev.log" > "%TEMP%\sa.txt"
  for /f %%N in ('find /c /v "" ^< "%TEMP%\sa.txt"') do set "N=%%N"
  set /a SK=N-70
  if !SK! lss 0 set SK=0
  more +!SK! "%TEMP%\sa.txt"
) else echo setupapi.dev.log not found
echo.
reg unload HKLM\OFFSYS >nul 2>&1 || (ping -n 3 127.0.0.1 >nul & reg unload HKLM\OFFSYS >nul 2>&1)
reg unload HKLM\OFFSOFT >nul 2>&1 || (ping -n 3 127.0.0.1 >nul & reg unload HKLM\OFFSOFT >nul 2>&1)
echo ===== done =====
if "%FIX%"=="1" (echo Fixes applied. Reboot into Windows and wait 1-2 minutes.) else (echo Report only. Run again with /fix to apply the fixes.)
exit /b 0

:nic
set "K=VEN_%~1&DEV_%~2"
set "FOUND=0"
for /f "delims=" %%P in ('reg query "HKLM\OFFSYS\%CSET%\Enum\PCI" /f "VEN_%~1&DEV_%~2" /k ^| find "HKEY_"') do (
  set "FOUND=1"
  echo --- known to Windows: %%P
  reg query "%%P" /s | findstr /i "HKEY_LOCAL_MACHINE Service ClassGUID ConfigFlags DeviceDesc"
  if "%FIX%"=="1" (reg delete "%%P" /f >nul 2>&1 && (echo     [fix] stale entry removed) || (echo     [fix] COULD NOT remove it - run as SYSTEM or from a WinPE with admin rights))
)
if "!FOUND!"=="0" echo --- !K!: no entry (Windows has not enumerated this device yet)
exit /b 0
