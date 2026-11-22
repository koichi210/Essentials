# MagicLauncher をブラウザで開く(秘書さんの吹き出しの選択肢などから、ファイルのパスを指定して呼ぶ)。
# 起動していなければ autostart.ps1 で窓なしに起動し(待ち受けまで待つのは autostart.ps1 側)、既定のブラウザで開く。
# 開けなかったときは理由を1行出力して終了コード 1 で終わる(呼んだ側がその文面で知らせられるように)。
# ポート番号は server.js / start.bat / stop.bat / autostart.ps1 / open.ps1 の 5 か所(変えるときは全部)。
# このファイルは UTF-8 BOM 付きで保存する(無いと PowerShell 5.1 で日本語が化けて構文エラーになる)。
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$port = 8790

& (Join-Path $PSScriptRoot 'autostart.ps1')
if ($LASTEXITCODE -ne 0) {
    Write-Output 'ランチャーが起動しませんでした(ログ: %LOCALAPPDATA%\MagicLauncher\server.log)。'
    exit 1
}
Start-Process "http://127.0.0.1:$port/"
