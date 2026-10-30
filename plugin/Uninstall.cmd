@echo off
setlocal
title LibrarySearch Uninstaller
echo Checking LibrarySearch installation...
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1"
set "librarysearch_exit=%errorlevel%"
if not "%librarysearch_exit%"=="0" echo Uninstall did not complete. Please read the message above.
pause
exit /b %librarysearch_exit%
