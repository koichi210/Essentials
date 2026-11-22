# 株レーダーをブラウザで開く(秘書さんの吹き出しの選択肢などから、ファイルのパスを指定して呼ぶ)。
# 起動していなければ autostart.ps1 で窓なしに起動し、待ち受けが始まるまで待ってから開く。
# 開けなかったときは理由を1行出力して終了コード 1 で終わる(呼んだ側がその文面で知らせられるように)。
param([int]$TimeoutSeconds = 30)

[Console]::OutputEncoding = [Text.Encoding]::UTF8
$port = 8766
$url = "http://127.0.0.1:$port/"

function Test-Listening {
    [bool](Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
}

if (-not (Test-Listening)) {
    if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
        Write-Output '株レーダーを起動できませんでした(Node.js が見つかりません)。'
        exit 1
    }
    & (Join-Path $PSScriptRoot 'autostart.ps1')
    $limit = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not (Test-Listening)) {
        if ((Get-Date) -gt $limit) {
            Write-Output "株レーダーが $TimeoutSeconds 秒たっても起動しませんでした(ログ: %LOCALAPPDATA%\StockRadar\server.log)。"
            exit 1
        }
        Start-Sleep -Milliseconds 500
    }
}
Start-Process $url
