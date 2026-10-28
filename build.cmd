@echo off
rem Builds bin\Buba-Helper.exe: a single self-contained exe (.NET 8 inside, nothing needs to be installed on the target PC / WinPE).
rem Needs the .NET SDK 8 on the build machine (https://dot.net).
rem Optional: assets\bootsect.exe (Microsoft file, not in the repository) and assets\net-drivers.zip (see devtools\make-net-drivers.ps1) are embedded when present.
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul || (echo dotnet SDK 8 not found - install it from https://dot.net & exit /b 1)
dotnet publish Buba-Helper.csproj -c Release -o bin
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
if exist bin\Buba-Helper.pdb del bin\Buba-Helper.pdb
echo OK: bin\Buba-Helper.exe
