@echo off
rem Запуск движка из каталога Executables плейбука: exe и catalog лежат рядом (кладёт tools/package.py).
rem Использование: bareos-run.cmd <plan|apply|verify|revert> [опции]
"%~dp0bareos.exe" %* --catalog "%~dp0catalog"
exit /b %ERRORLEVEL%
