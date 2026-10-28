# Собирает assets\net-drivers.zip: сетевые драйверы Intel и Realtek из каталога Microsoft Update (вшиваются в exe при сборке).
# Нужен собранный bin\Buba-Helper.exe (ключ --drv-fetch) и интернет. Архив в репозиторий не входит (.gitignore).
# Запуск:  powershell -ExecutionPolicy Bypass -File devtools\make-net-drivers.ps1
param([string]$Exe = (Join-Path $PSScriptRoot '..\bin\Buba-Helper.exe'))
$ErrorActionPreference = 'Stop'
$sets = [ordered]@{
    'intel-i219-e1d' = 'PCI\VEN_8086&DEV_15FA'   # Intel I217/I218/I219
    'intel-i210-e1r' = 'PCI\VEN_8086&DEV_1533'   # Intel I210/I211
    'realtek-8168'   = 'PCI\VEN_10EC&DEV_8168'   # Realtek PCIe GbE
    'realtek-8125'   = 'PCI\VEN_10EC&DEV_8125'   # Realtek 2.5GbE
}
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('netdrv-' + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path $tmp 'stage'
New-Item -ItemType Directory $stage | Out-Null
try {
    foreach ($name in $sets.Keys) {
        $report = Join-Path $tmp "$name.txt"
        Start-Process -FilePath $Exe -ArgumentList '--drv-fetch', "`"$report`"", "`"$(Join-Path $tmp $name)`"", "`"$($sets[$name])`"" -Wait   # exe оконный: смотрим на отчёт
        $line = if (Test-Path $report) { Get-Content $report -Encoding UTF8 | Where-Object { $_ -match '=> ' -and $_ -notmatch 'NOT FOUND' } | Select-Object -First 1 }
        if (-not $line) { Write-Host "Не найден драйвер для $($sets[$name])"; continue }
        $inf = ($line -split '=> ', 2)[1].Trim()
        Copy-Item (Split-Path $inf -Parent) (Join-Path $stage $name) -Recurse
        Write-Host "$name <- $inf"
    }
    if (-not (Get-ChildItem $stage)) { throw 'Ни одного драйвера не скачано' }
    $zip = Join-Path $PSScriptRoot '..\assets\net-drivers.zip'
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    Write-Host "Готово: $zip"
}
finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
