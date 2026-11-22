# MagicLauncher のサーバを窓なしで裏起動する(start.bat とスタートアップから呼ぶ)。
# スタートアップ(shell:startup)に置くショートカットのリンク先:
#   powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "<このフォルダ>\autostart.ps1"
# 窓なしで呼ばれるので Read-Host は使わない(確認はダイアログで出す)。
# ポート番号は server.js / start.bat / stop.bat / autostart.ps1 / open.ps1 の 5 か所(変えるときは全部)。
# このファイルは UTF-8 BOM 付きで保存する(無いと PowerShell 5.1 で日本語が化けて構文エラーになる)。
$ErrorActionPreference = 'Stop'
$port = 8790

function Test-Listening {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $async = $client.BeginConnect('127.0.0.1', $port, $null, $null)
        if (-not $async.AsyncWaitHandle.WaitOne(300)) {
            return $false
        }
        $client.EndConnect($async)
        return $true
    } catch {
        return $false
    } finally {
        $client.Close()
    }
}

function Show-Message([string]$text, [string]$buttons = 'OK', [string]$icon = 'Information') {
    Add-Type -AssemblyName System.Windows.Forms
    return [System.Windows.Forms.MessageBox]::Show($text, 'MagicLauncher', $buttons, $icon)
}

function Find-Node {
    $cmd = Get-Command node -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }
    return $null
}

# 待ち受け中なら何もしない
if (Test-Listening) {
    exit 0
}

# Node.js が無ければ、確認してから winget で入れる(winget も無ければ nodejs.org を開く)
$node = Find-Node
if (-not $node) {
    $answer = Show-Message "Node.js が見つかりません。`nいまインストールしますか？(winget install OpenJS.NodeJS.LTS)" 'YesNo' 'Question'
    if ($answer -ne 'Yes') {
        exit 1
    }
    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        Start-Process 'https://nodejs.org/'
        Show-Message "winget が無いため、nodejs.org を開きました。`nNode.js (LTS) を入れてから、もう一度起動してください。" | Out-Null
        exit 1
    }
    $p = Start-Process -FilePath $winget.Source -ArgumentList @('install', '--id', 'OpenJS.NodeJS.LTS', '-e', '--accept-source-agreements', '--accept-package-agreements') -Wait -PassThru
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
    $node = Find-Node
    if (-not $node) {
        Show-Message "Node.js のインストールを確認できませんでした(終了コード $($p.ExitCode))。`nインストール後に、もう一度起動してください。" 'OK' 'Warning' | Out-Null
        exit 1
    }
}

$dataDir = if ($env:MAGIC_LAUNCHER_DATA) { $env:MAGIC_LAUNCHER_DATA } else { Join-Path $env:LOCALAPPDATA 'MagicLauncher' }
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
$log = Join-Path $dataDir 'server.log'
# ログは 1MB を超えたら作り直す
if ((Test-Path $log) -and ((Get-Item $log).Length -gt 1MB)) {
    Remove-Item $log -Force
}

$server = Join-Path $PSScriptRoot 'server.js'
# cmd は外側の " を外すので二重に囲む
$command = "/c `"`"$node`" `"$server`" >> `"$log`" 2>&1`""
Start-Process -FilePath 'cmd.exe' -ArgumentList $command -WorkingDirectory $PSScriptRoot -WindowStyle Hidden

# 接続できるまで最大 10 秒待つ
$deadline = (Get-Date).AddSeconds(10)
while ((Get-Date) -lt $deadline) {
    if (Test-Listening) {
        exit 0
    }
    Start-Sleep -Milliseconds 250
}
exit 1
