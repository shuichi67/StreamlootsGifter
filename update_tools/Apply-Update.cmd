@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0Apply-Update.ps1"
if errorlevel 1 (
  echo.
  echo The update could not be completed. See the message above.
  pause
)
endlocal
