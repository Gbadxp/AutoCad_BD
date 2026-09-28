@echo off
rem Clique duas vezes para verificar se este computador tem tudo o que o Fiber Plugin precisa.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0verificar.ps1" %*
echo.
pause
