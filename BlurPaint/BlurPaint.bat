@echo off
rem Open index.html in an Edge app window (no tabs, no address bar)
setlocal
set "APP=%~dp0index.html"
set "APP=%APP:\=/%"
start "" msedge --app="file:///%APP%"
