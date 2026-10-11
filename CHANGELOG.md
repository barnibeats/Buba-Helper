# Changelog

Формат — [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/), версии — [SemVer](https://semver.org/lang/ru/).

## [1.0.0] — 2026-10-11

### Добавлено
- Установка Windows из ISO / WIM / ESD: автопоиск образов, монтирование ISO через `virtdisk.dll`, запасная распаковка через 7-Zip.
- Выбор диска (Win32 API, без разбора вывода `diskpart`), защита диска-источника.
- Разметка GPT/MBR, размер раздела Windows вручную в МБ, раздел *Data* из остатка.
- Режим «только раздел Windows» — остальные разделы диска не трогаются.
- `dism /Apply-Image`, добавление драйверов, unattend XML, `bcdboot` (UEFI/BIOS), проверка загрузочных файлов.
- Проверка SHA-256 до изменения диска.
- Профили `.ini`, ключи `--profile`, `--auto`, `--yes`, `--dry-run`.
- Захват sysprep-образа в WIM: офлайн-проверка `GeneralizationState`, `/CheckIntegrity`, `.sha256`.
- Диалог с отсчётом перед перезагрузкой, звуковой сигнал.
