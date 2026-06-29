@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
if errorlevel 1 (
  echo.
  echo WebOCR install failed.
  pause
  exit /b 1
)
echo.
echo WebOCR install complete.
echo Desktop shortcut: WebOCR
pause
