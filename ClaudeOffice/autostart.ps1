# 窓なしで裏起動する(スタートアップ用)。ショートカットのリンク先:
#   powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File autostart.ps1
$port = 8765
$listening = Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort $port -State Listen -ErrorAction SilentlyContinue
if ($listening) {
    exit 0
}
$dataDir = if ($env:CLAUDE_OFFICE_DATA) { $env:CLAUDE_OFFICE_DATA } else { Join-Path $env:LOCALAPPDATA 'ClaudeOffice' }
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
$log = Join-Path $dataDir 'server.log'
# 1MB を超えたら作り直す
if ((Test-Path $log) -and ((Get-Item $log).Length -gt 1MB)) {
    Remove-Item $log -Force
}
$server = Join-Path $PSScriptRoot 'server.js'
# cmd は外側の " を外すので二重に囲む
$command = "/c `"`"node`" `"$server`" >> `"$log`" 2>&1`""
Start-Process -FilePath 'cmd.exe' -ArgumentList $command -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
