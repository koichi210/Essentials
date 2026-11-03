@echo off
chcp 65001 > nul
rem 8766 で LISTEN 中の PID だけを止める(プロセス名 node では止めない。他の node を巻き込まないため)
for /f "tokens=5" %%p in ('netstat -ano ^| findstr /C:"127.0.0.1:8766" ^| findstr /C:"LISTENING"') do (
  taskkill /PID %%p /F
)
