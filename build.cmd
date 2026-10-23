@echo off
rem Builds bin\Buba-Helper.exe. Needs only .NET Framework 4 (present on every Windows 10/11).
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (echo csc.exe not found - .NET Framework 4 is required & exit /b 1)
if not exist bin mkdir bin
set "RES=/resource:src\Gen\gendata.json,WinInstaller.gendata.json"
rem assets\bootsect.exe (a Microsoft file) is not in the repository: if present it is embedded into the exe, otherwise the program looks for bootsect.exe in tools\ and WinPE
if exist assets\bootsect.exe (set "RES=%RES% /resource:assets\bootsect.exe,WinInstaller.bootsect.exe" & echo bootsect.exe: embedded) else (echo bootsect.exe: not embedded - assets\bootsect.exe not found)
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /win32icon:assets\app.ico %RES% /out:bin\Buba-Helper.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll src\*.cs src\Gen\*.cs
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo OK: bin\Buba-Helper.exe
