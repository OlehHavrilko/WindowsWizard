# Каталог open source программ

Содержимое вкладки «Программы» в [TOOLBOX](TOOLBOX.md). Черновик от 2026-11-11.

Правила приёма: открытый исходный код, активная разработка, установка через winget. Идентификаторы winget, лицензии и актуальность проверяются при реализации — здесь только названия. Закрытые программы в каталог не входят, даже бесплатные.

| Категория | Программы |
|---|---|
| Браузеры | Firefox, LibreWolf, Brave, Zen Browser, Ungoogled Chromium |
| Файлы и система | 7-Zip, NanaZip, Files, WinDirStat, Bulk Crap Uninstaller, UniGetUI |
| Продуктивность | PowerToys, Flow Launcher, ShareX, Ditto, AutoHotkey, Notepad++ |
| Мониторинг и диагностика | LibreHardwareMonitor, CrystalDiskInfo, CrystalDiskMark, System Informer, PresentMon, CapFrameX |
| Игры и стриминг | Playnite, Heroic Games Launcher, Sunshine, Moonlight, OBS Studio, Special K |
| Разработка | Git, VSCodium, Windows Terminal, PowerShell 7, Neovim, Podman Desktop |
| Локальный AI | Ollama, llama.cpp, Jan, ComfyUI, KoboldCpp, whisper.cpp |
| Приватность и безопасность | KeePassXC, Bitwarden, VeraCrypt, Portmaster, simplewall, WireGuard |
| Медиа | mpv, VLC, HandBrake, Audacity, ImageGlass, yt-dlp |
| Сеть и обмен файлами | qBittorrent, LocalSend, Syncthing, WinSCP, Wireshark |
| Оформление | Windhawk, ExplorerPatcher, TranslucentTB, Rainmeter, EarTrumpet, Nilesoft Shell |
| Офис и заметки | LibreOffice, ONLYOFFICE, Joplin, Logseq, SumatraPDF, Thunderbird |

## Наборы

Установка пачкой одной кнопкой; состав — из профиля ([TOOLBOX](TOOLBOX.md) T-01).

| Набор | Состав |
|---|---|
| База | Firefox, 7-Zip, PowerToys, mpv, SumatraPDF |
| Игрок | Playnite, OBS Studio, LibreHardwareMonitor, PresentMon |
| Разработчик | Git, VSCodium, Windows Terminal, PowerShell 7, Podman Desktop |
| Локальный AI | Ollama, Jan, ComfyUI |

## Что проверить до включения

- лицензия каждой программы на дату релиза: проекты меняют лицензии;
- наличие и корректность пакета в winget, тихая установка;
- совместимость с инвариантами [SAFETY](SAFETY.md) §1: ExplorerPatcher и Windhawk вмешиваются в оболочку и ломаются после обновлений Windows — в каталоге с предупреждением;
- Portmaster и simplewall меняют сетевую фильтрацию — не ставить оба.
