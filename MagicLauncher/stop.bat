@echo off
rem MagicLauncher: stop only the process listening on 127.0.0.1:8790 (other node processes are left alone).
setlocal
set FOUND=0
for /f "tokens=5" %%p in ('netstat -ano ^| findstr /C:"127.0.0.1:8790" ^| findstr /C:"LISTENING"') do (
  echo Stopping MagicLauncher server, PID %%p
  taskkill /PID %%p /F > nul
  set FOUND=1
)
if "%FOUND%"=="0" echo MagicLauncher server is not running.
