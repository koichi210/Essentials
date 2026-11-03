# 窓なしで裏起動する(秘書さんの「株レーダー」メニューから呼ばれる)
$port = 8766
$listening = Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort $port -State Listen -ErrorAction SilentlyContinue
if ($listening) {
    exit 0
}
$dataDir = if ($env:STOCK_RADAR_DATA) { $env:STOCK_RADAR_DATA } else { Join-Path $env:LOCALAPPDATA 'StockRadar' }
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
