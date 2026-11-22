# 株レーダーの LINE 通知の設定(チャネルアクセストークンと、送り先のユーザーID)を登録する。
# 値は Windows の DPAPI(この PC・この Windows ユーザーだけが戻せる暗号)で暗号化して
# %LOCALAPPDATA%\StockRadar\line.dpapi に保存する(環境変数 STOCK_RADAR_DATA があればそこ)。値は画面に出さない。使い方:
#   powershell -ExecutionPolicy Bypass -File line-setup.ps1 <.env のパス>   … .env の LINE_CHANNEL_ACCESS_TOKEN と ADMIN_USER_ID を読む
#   powershell -ExecutionPolicy Bypass -File line-setup.ps1                 … 伏せ字で入力する
#   powershell -ExecutionPolicy Bypass -File line-setup.ps1 -Remove         … 登録を消す
param([string]$EnvFile, [switch]$Remove)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

$dataDir = if ($env:STOCK_RADAR_DATA) { $env:STOCK_RADAR_DATA } else { Join-Path $env:LOCALAPPDATA 'StockRadar' }
$file = Join-Path $dataDir 'line.dpapi'
# line.js と同じ値にする(暗号のときに混ぜる値。合わないと戻せない)
$entropy = [Text.Encoding]::UTF8.GetBytes('StockRadar.LINE')

if ($Remove) {
    if (Test-Path -LiteralPath $file) {
        Remove-Item -LiteralPath $file -Force
        Write-Host "LINE の設定を消しました($file)。"
    } else {
        Write-Host 'LINE の設定はありません。'
    }
    exit 0
}

# .env の「名前=値」を読む(export 付き・引用符付き・行末の # コメントにも対応)。値は返すだけで表示しない
function Read-EnvValue([string[]]$lines, [string]$name) {
    foreach ($l in $lines) {
        if ($l -match "^\s*(?:export\s+)?$([regex]::Escape($name))\s*=\s*(.*)$") {
            $v = $Matches[1].Trim()
            if ($v -match '^"([^"]*)"') {
                return $Matches[1]
            }
            if ($v -match "^'([^']*)'") {
                return $Matches[1]
            }
            return ($v -replace '\s+#.*$', '').Trim()
        }
    }
    return ''
}

function Read-Secret([string]$prompt) {
    $secure = Read-Host -Prompt $prompt -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    } finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

if ($EnvFile) {
    if (-not (Test-Path -LiteralPath $EnvFile -PathType Leaf)) {
        Write-Host ".env が見つかりません: $EnvFile"
        exit 1
    }
    $lines = Get-Content -LiteralPath $EnvFile -Encoding UTF8
    $token = Read-EnvValue $lines 'LINE_CHANNEL_ACCESS_TOKEN'
    $to = Read-EnvValue $lines 'ADMIN_USER_ID'
} else {
    Write-Host 'LINE Developers のチャネル(Messaging API)の値を入れてください。入力した文字は表示されません。'
    $token = Read-Secret 'チャネルアクセストークン(長期)'
    $to = Read-Secret '送り先のユーザーID(U で始まる33文字)'
}
$token = $token.Trim()
$to = $to.Trim()

if ($token.Length -lt 20 -or $token -match '\s') {
    Write-Host 'チャネルアクセストークンが見つからないか、形が違います(.env なら LINE_CHANNEL_ACCESS_TOKEN)。'
    exit 1
}
if ($to -cnotmatch '^[UCR][0-9a-f]{32}$') {
    Write-Host '送り先のユーザーIDが見つからないか、形が違います(.env なら ADMIN_USER_ID。U で始まる33文字)。'
    exit 1
}

$json = @{ token = $token; to = $to } | ConvertTo-Json -Compress
$bytes = [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($json), $entropy, 'CurrentUser')
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
[IO.File]::WriteAllText($file, [Convert]::ToBase64String($bytes))
$tokenLength = $token.Length
$token = $null
$to = $null
$json = $null
Write-Host "保存しました: $file(トークン $tokenLength 文字・送り先のユーザーID)"
Write-Host '株レーダーを起動し直す必要はありません。画面の「見張り」の「LINE にテスト送信」で届くか確かめられます。'
