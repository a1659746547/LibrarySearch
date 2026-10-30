@echo off
setlocal
title LibrarySearch Installer
echo Starting LibrarySearch installer...
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
set "librarysearch_exit=%errorlevel%"
if not "%librarysearch_exit%"=="0" echo Installation did not complete. Please read the message above.
pause
exit /b %librarysearch_exit%
