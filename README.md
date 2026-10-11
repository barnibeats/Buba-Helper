# win-install-helper

[![build](https://github.com/barnibeats/win-install-helper/actions/workflows/build.yml/badge.svg)](https://github.com/barnibeats/win-install-helper/actions/workflows/build.yml)
[![license: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![platform: WinPE / Windows 10-11](https://img.shields.io/badge/platform-WinPE%20%7C%20Windows%2010%E2%80%9311-lightgrey)

Графическая утилита для **WinPE** (Sergei Strelec и др.): выбрать ISO/WIM/ESD с флешки, диск, разметку GPT/MBR и размер раздела — и получить установленную Windows с рабочим загрузчиком. Плюс захват sysprep-образа в WIM.
Один `exe`, без установки и зависимостей, кроме .NET Framework 4.

> **In short:** a single-file WinForms tool for WinPE that automates *pick image → pick disk → partition (GPT/MBR, size in MB) → apply (DISM) → boot files (bcdboot UEFI/BIOS)*, can reinstall only the Windows partition and keep the rest, supports unattend XML, driver injection, SHA-256 verification, reusable profiles and a fully automatic mode. It can also capture a sysprepped Windows partition into a `.wim`. UI is in Russian.

![Установка](docs/screenshots/install-wipe.png)

## Возможности

**Установка**
- Образы **ISO / WIM / ESD**: автопоиск на всех дисках, ISO монтируется сам (`virtdisk.dll`, без PowerShell), запасной путь — распаковка `install.wim` через `7z.exe`.
- Список редакций читается прямо из заголовка WIM (запасной вариант — `dism /Get-WimInfo`).
- Диски определяются через Win32 API (модель, размер, шина, стиль, тома) — **без разбора локализованного вывода** `diskpart`. USB-диски подсвечены, диск с образом защищён от выбора.
- Два режима:
  - **Стереть весь диск**: GPT (EFI 300 МБ + MSR 16 МБ + Windows) или MBR (один активный раздел); размер Windows вводится **в МБ** (`51200` = 50 ГБ) или «весь диск»; остаток — раздел *Data*.
  - **Только раздел Windows**: форматируется один раздел, остальное (Data и т. д.) не трогается; загрузчик — в существующий EFI-раздел (GPT) или активный раздел (MBR).
- Режим ПК (UEFI/BIOS) определяется сам; при несовпадении с разметкой — предупреждение.
- `dism /Apply-Image` → драйверы из папки (`/Add-Driver /Recurse`) → **unattend XML** (необязательно) → `bcdboot` → проверка загрузочных файлов.
- **Проверка SHA-256**: если рядом с образом лежит `.sha256`, сумма сверяется *до любых изменений диска*.
- Перед перезагрузкой — диалог «Вытащите флешку» с отсчётом 15 с, звуковой сигнал по итогу.

**Профили и авторежим**
- Настройки сохраняются в `.ini` (см. [`docs/profile.example.ini`](docs/profile.example.ini)); `profile.ini` рядом с exe подхватывается сам.
- `WinInstaller.exe --profile office.ini --auto` — сама находит образ по имени, выбирает единственный внутренний диск и запускает установку.

**Захват образа (sysprep)**
- Офлайн-проверка, что система обобщена (`GeneralizationState = 7`).
- `dism /Capture-Image /CheckIntegrity`, необязательный XML ответов (на время захвата кладётся в `Windows\Panther\Unattend`, потом раздел возвращается как был), проверка заголовка и запись `.sha256`.

| Режим «только раздел Windows» | Захват образа |
|---|---|
| ![keep](docs/screenshots/install-keep.png) | ![capture](docs/screenshots/capture.png) |

## Быстрый старт

1. Скачайте `WinInstaller.exe` из [Releases](../../releases) (рядом лежит `.sha256`) или соберите сами (ниже).
2. Положите на флешку вместе с образами. Необязательно: папка `Drivers\`, `tools\7z.exe`, `profile.ini`.
3. В WinPE запустите `WinInstaller.exe`, выберите образ → диск → разметку → **УСТАНОВИТЬ WINDOWS**.

Проверить сценарий, ничего не выполняя: `WinInstaller.exe --dry-run` (команды `diskpart`/`dism`/`bcdboot` только пишутся в лог).

## Сборка

Нужен только `csc.exe` из .NET Framework 4 (есть в любой Windows 10/11):

```bat
build.cmd
```

Результат — `bin\WinInstaller.exe`. Исходник — один файл [`src/WinInstaller.cs`](src/WinInstaller.cs) (C# 5).

## Командная строка

| Ключ | Действие |
|---|---|
| `--dry-run` | ничего не выполнять, только лог |
| `--profile <файл.ini>` | загрузить профиль |
| `--auto` | (с `--profile`) выбрать диск по правилу и перейти к установке с обычным подтверждением |
| `--yes` | пропустить подтверждение. **Опасно:** диск стирается без вопроса. Только вместе с `--auto` |

`disk=auto` в профиле — единственный внутренний (не USB) диск; в режиме `keep` — единственный диск с установленной Windows. Если однозначно выбрать нельзя, ничего не запускается.

## Как это работает

```
[SHA-256] → diskpart (clean/convert/create/format  или  format одного раздела)
          → dism /Apply-Image → [dism /Add-Driver] → [unattend.xml]
          → bcdboot (/f UEFI  или  /f BIOS) → проверка bootmgfw.efi / bootmgr
```

Подробнее о захвате и sysprep — в [`docs/sysprep-workflow.md`](docs/sysprep-workflow.md).

## Безопасность и ограничения

- Режим «стереть весь диск» **необратимо уничтожает** данные на выбранном диске. Перед запуском всегда показывается диалог с моделью, размером и томами диска.
- Протестируйте сценарий на виртуальной машине, прежде чем применять на реальных ПК.
- Нужен .NET Framework 4 в WinPE (проверьте наличие `X:\Windows\Microsoft.NET`).
- Не поддерживаются разбитые образы (`install.swm`).
- Утилита поставляется «как есть» (см. [LICENSE](LICENSE)).

## Лицензия

[MIT](LICENSE)
