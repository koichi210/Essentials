# スマホから株レーダーを開けるようにする(Tailscale serve 経由。自分の Tailscale につないだ端末だけが開ける)
# 先に Tailscale を入れてログインしておく。使い方:
#   powershell -ExecutionPolicy Bypass -File remote-setup.ps1          … 設定する
#   powershell -ExecutionPolicy Bypass -File remote-setup.ps1 -Remove  … やめる
# やること: ①この PC の Tailscale の名前(xxx.ts.net)を %LOCALAPPDATA%\StockRadar\config.json に書く
#           ②ログオン時にサーバーを起動するタスク「StockRadar」を登録 ③サーバーを再起動 ④tailscale serve で HTTPS 中継
# Funnel(インターネットへの公開)は使わない
param([switch]$Remove)
$ErrorActionPreference = 'Stop'
$port = 8766
$taskName = 'StockRadar'

$tailscale = Join-Path $env:ProgramFiles 'Tailscale\tailscale.exe'
if (-not (Test-Path $tailscale)) {
    $cmd = Get-Command tailscale -ErrorAction SilentlyContinue
    if (-not $cmd) {
        Write-Host 'Tailscale が見つかりません。https://tailscale.com/download から入れてログインしてください。'
        exit 1
    }
    $tailscale = $cmd.Source
}

function Stop-Server {
    Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
        ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }
}

if ($Remove) {
    & $tailscale serve --https=443 off
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host 'スマホからの中継とログオン時の起動をやめました(config.json は残しています)。'
    exit 0
}

$status = & $tailscale status --json | Out-String | ConvertFrom-Json
if ($status.BackendState -ne 'Running') {
    Write-Host "Tailscale がつながっていません(状態: $($status.BackendState))。ログインしてから実行してください。"
    exit 1
}
$name = ([string]$status.Self.DNSName).TrimEnd('.').ToLower()
if (-not $name) {
    Write-Host 'この PC の Tailscale の名前が取れません。管理画面で MagicDNS を有効にしてください。'
    exit 1
}

# ① config.json に名前を足す(ほかの設定は残す)
$dataDir = if ($env:STOCK_RADAR_DATA) { $env:STOCK_RADAR_DATA } else { Join-Path $env:LOCALAPPDATA 'StockRadar' }
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
$configPath = Join-Path $dataDir 'config.json'
$config = if (Test-Path $configPath) { Get-Content $configPath -Raw -Encoding UTF8 | ConvertFrom-Json } else { [pscustomobject]@{} }
$hosts = @($config.allowedHosts | Where-Object { $_ }) + $name | Select-Object -Unique
$config | Add-Member -NotePropertyName allowedHosts -NotePropertyValue @($hosts) -Force
[IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))

# ② ログオン時に裏で起動する
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$(Join-Path $PSScriptRoot 'autostart.ps1')`"" -WorkingDirectory $PSScriptRoot
$trigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings -Description '株レーダーのサーバーをログオン時に起動する' -Force | Out-Null

# ③ 新しい config.json を読ませるため再起動
Stop-Server
Start-Sleep -Milliseconds 500
& (Join-Path $PSScriptRoot 'autostart.ps1')

# ④ Tailscale の中だけに HTTPS で中継する(localhost だと ::1 に行くことがあるので 127.0.0.1 を書く)
& $tailscale serve --bg "http://127.0.0.1:$port"

Write-Host ''
Write-Host "できました。スマホで Tailscale をオンにして https://$name/ を開いてください。"
Write-Host 'この PC がスリープ中や電源オフのときは開けません。'
