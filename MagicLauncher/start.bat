@echo off
rem MagicLauncher: start the server if it is not running, then open the browser.
rem Port 8790 also appears in server.js / stop.bat / autostart.ps1 / open.ps1 (change all five together).
setlocal
netstat -ano | findstr /C:"127.0.0.1:8790" | findstr /C:"LISTENING" > nul
if errorlevel 1 (
  echo Starting MagicLauncher server...
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0autostart.ps1"
  if errorlevel 1 (
    echo Failed to start the server. See %LOCALAPPDATA%\MagicLauncher\server.log
    pause
    exit /b 1
  )
)
start "" "http://127.0.0.1:8790/"
