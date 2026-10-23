---
name: Bug report / Ошибка
about: Something went wrong during install, boot repair or capture
labels: bug
---

**What happened / Что произошло**

**Mode / Режим**
- [ ] Install ISO/WIM, wipe whole disk
- [ ] Install ISO/WIM, only Windows partition
- [ ] Boot repair (Windows 7 / 8 / 10 / 11), step: system / boot partition / run
- [ ] Capture (sysprep)
- [ ] Answer file (XML)

**Environment / Окружение**
- Buba-Helper version (first line of `install.log`, e.g. 1.2.0):
- WinPE (e.g. Sergei Strelec, version):
- Firmware: UEFI / BIOS
- Image (install): Windows version, ISO / WIM / ESD
- Windows being repaired (boot repair): version, x86 / x64, GPT / MBR
- Disk bus: SATA / NVMe / USB

**Log / Лог** (`install.log` next to the exe — remove anything private; for boot repair it contains the full `diskpart` / `bcdboot` output and the BCD listing)

```
paste here
```
