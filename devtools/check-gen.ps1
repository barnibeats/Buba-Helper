# Проверка генератора файлов ответов: создаёт XML из файла настроек и проверяет его без установки Windows.
#   - XML well-formed;
#   - команда извлечения скриптов (первая в specialize) не длиннее 259 знаков;
#   - все вшитые скрипты (ScriptFile) разбираются парсером PowerShell без синтаксических ошибок.
# Запуск:  powershell -ExecutionPolicy Bypass -File devtools\check-gen.ps1 [bin\Buba-Helper.exe] [docs\example-image-settings.json]
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\bin\Buba-Helper.exe'),
    [string]$Settings = (Join-Path $PSScriptRoot '..\docs\example-image-settings.json')
)
$ErrorActionPreference = 'Stop'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('gencheck-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $tmp | Out-Null
$bad = 0
try {
    $xmlFile = Join-Path $tmp 'out.xml'
    Start-Process -FilePath $Exe -ArgumentList '--gen', "`"$Settings`"", "`"$xmlFile`"" -Wait   # exe оконный: код выхода не возвращается, смотрим на результат
    if (-not (Test-Path $xmlFile)) { throw "XML не создан (см. $xmlFile.log)" }
    $xml = New-Object Xml.XmlDocument
    $xml.Load($xmlFile)
    Write-Host 'XML разобран'
    $log = "$xmlFile.log"
    if ((Test-Path $log) -and (Get-Item $log).Length -gt 0) { Write-Host '--- предупреждения генератора:'; Get-Content $log -Encoding UTF8 }
    $ns = New-Object Xml.XmlNamespaceManager($xml.NameTable); $ns.AddNamespace('u', 'urn:schemas-microsoft-com:unattend')
    $first = $xml.SelectSingleNode("//u:settings[@pass='specialize']//u:RunSynchronousCommand[u:Order='1']/u:Path", $ns)
    if ($first -and $first.InnerText.Length -gt 259) { Write-Host "ОШИБКА: команда извлечения длиннее 259 знаков ($($first.InnerText.Length))"; $bad++ }
    elseif ($first) { Write-Host "Команда извлечения: $($first.InnerText.Length) знаков (лимит 259)" }
    foreach ($n in $xml.SelectNodes('//*[@path]')) {
        $f = Join-Path $tmp (Split-Path $n.path -Leaf)
        ($n.InnerText.Trim() -split '\n') | Set-Content $f -Encoding UTF8
        $err = $null
        [void][Management.Automation.Language.Parser]::ParseFile($f, [ref]$null, [ref]$err)
        $mark = if ($err) { $bad++; 'ОШИБКА' } else { 'ok' }
        Write-Host ('{0,-26} {1}' -f (Split-Path $f -Leaf), $mark)
        foreach ($e in $err) { Write-Host ('    строка {0}: {1}' -f $e.Extent.StartLineNumber, $e.Message) }
    }
}
finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
if ($bad) { Write-Host "Проблем: $bad"; exit 1 }
Write-Host 'Всё в порядке'
