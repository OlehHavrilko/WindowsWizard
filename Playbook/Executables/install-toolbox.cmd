@echo off
rem Ставит Bare OS Toolbox: копирует bareos.exe, Toolbox и каталог в %ProgramFiles%\Bare OS, создаёт ярлыки (Пуск и рабочий стол).
rem Вызывается задачей плейбука 01-toolbox.yml; повторный запуск безопасен.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-toolbox.ps1" -Source "%~dp0."
exit /b %ERRORLEVEL%
