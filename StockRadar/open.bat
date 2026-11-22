@echo off
rem Open StockRadar in the browser without a console window (starts it hidden if not running).
rem For launchers that run a file by double-click (e.g. MagicLauncher). See open.ps1.
start "" powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0open.ps1"
