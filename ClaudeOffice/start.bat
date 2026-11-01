@echo off
chcp 65001 > nul
rem 起動済みならブラウザで開くだけ、未起動なら開いてから node server.js
netstat -ano | findstr /C:"127.0.0.1:8765" | findstr /C:"LISTENING" > nul
if %ERRORLEVEL%==0 (
  start "" http://127.0.0.1:8765/
  exit /b 0
)
start "" http://127.0.0.1:8765/
cd /d "%~dp0"
node server.js
