# 秘書さんガジェットをビルドして Release\ を作り直し、Release\ の exe をそのまま起動する(.NET SDK が必要)。
# 実行中の exe はロックされて置き換えられないので、先に動いている秘書さんを終了させる。
$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$exeName = 'SecretaryGadget'
$buildOut = Join-Path $env:LOCALAPPDATA 'SecGad\build\bin\Release\net9.0-windows10.0.19041.0'
$release = Join-Path $src 'Release'

# 動いている秘書さんに --exit を渡して後片付け付きで終了させる(強制終了すると本家 Teams の小窓が表示されなくなるため使わない)
function Stop-Secretary {
    $procs = @(Get-Process -Name $exeName -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) {
        return $true
    }
    foreach ($p in $procs) {
        if ($p.Path) {
            Start-Process -FilePath $p.Path -ArgumentList '--exit' -WindowStyle Hidden
            break
        }
    }
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 500
        if (@(Get-Process -Name $exeName -ErrorAction SilentlyContinue).Count -eq 0) {
            return $true
        }
    }
    return $false
}

# 1. 動いている秘書さんを終了(終わらなければメニューから終了するよう案内して中止)
if (-not (Stop-Secretary)) {
    Write-Host '秘書さんが終了しませんでした。タスクトレイのメニューから「終了」してから、もう一度実行してください。'
    exit 1
}

# 2. ビルド。同梱スキンを消したときに古いスキンがビルド出力に残らないよう、先に出力の Skins を消す
$builtSkins = Join-Path $buildOut 'Skins'
if (Test-Path $builtSkins) {
    Remove-Item -Recurse -Force $builtSkins
}
# (実行中に --no-incremental でビルドしないこと。使用中の exe/dll は消せず runtimeconfig.json だけ消える)
dotnet build (Join-Path $src 'SecretaryGadget.csproj') -c Release
if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $buildOut "$exeName.exe"))) {
    Write-Host 'ビルドに失敗したか、exe が出来ていません。中止します。'
    exit 1
}

# 3. ビルド出力 → Release\ へミラー(.pdb は除く)
robocopy $buildOut $release /MIR /XF *.pdb /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) {
    Write-Host "Release へのコピーに失敗しました(robocopy 終了コード $LASTEXITCODE)。"
    exit 1
}

# 4. Release\ の exe を起動(自動起動の登録先は、起動した秘書さんが今の exe に直す)
Start-Process -FilePath (Join-Path $release "$exeName.exe")
Write-Host "Release を更新して秘書さんを起動しました。"
exit 0
