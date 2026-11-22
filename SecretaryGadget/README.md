# SecretaryGadget（秘書さんガジェット）

従来版 Outlook の新着メール・会議の開始前・Teams のメンションを、画面右下からせり上がってくるキャラクター（秘書さん）の吹き出しで知らせる、Windows 用の常駐ガジェット。

- ふだんはタスクトレイにだけ常駐し、お知らせがあるときだけ表示、一定時間（既定 20 秒）で引っ込む
- 秘書さんは**画面キャプチャから除外**され、Teams の画面共有やスクショには映らない（「自動的に隠さない」中の 5 分だけ映る）
- プレゼン中・全画面・画面ロック中は出さず、解除後にまとめて出す（4 件以上は 1 件に集約）
- Outlook は勝手に起動しない。Teams の通知データベースは読まない（組み込みポップアップの文字を UI Automation で読む）

## 動作環境

- Windows 10 2004 以降 / Windows 11
- .NET Desktop Runtime 9 以降（`net9.0-windows10.0.19041.0`。10 だけのPCでも動くよう `RollForward=Major`）
- 通知元: 従来版 Outlook（`OUTLOOK.EXE`）、新しい Teams（`ms-teams.exe`）

## 使い方

| 操作 | 動き |
|---|---|
| トレイ左クリック | 表示中なら引っ込める／非表示なら「秘書さんを呼ぶ」 |
| トレイ右クリック | メニュー（休憩・大きさ・スキン・隠れるまでの時間・自動的に隠さない・設定・PC起動時に自動で起動・終了） |
| 「秘書さんを呼ぶ」の吹き出しの選択肢 | 1行ずつ押せる。押すと引っ込めてから、その選択肢を起動（並びは settings.json の `CallMenu`） |
| 吹き出しクリック | 引っ込めてから開く（メールを開く／Teams を前へ／会議に参加）。開く先が無いお知らせ（あいさつ等）は引っ込むだけ |
| × クリック | 吹き出しだけ消す（秘書さんは残る） |
| 絵クリック | 引っ込む |

## ビルド・配布

```
SecretaryGadget\Publish-SecretaryGadget.ps1   … 更新するとき実行（.NET SDK が必要）
```

動いている秘書さんを `--exit` で終了 → `dotnet build -c Release` → ビルド出力を `Release\` へミラー → `Release\SecretaryGadget.exe` を起動。
実行場所は `Release\` の1か所だけ（このPC専用。ほかのPCへ配る仕組みは持たない）。秘書さんの動作中は exe がロックされるので、`Release\` を書き換える前に必ず終了させる。
スキンの絵を差し替えたときも、ソースの `Skins\` を直してから Publish する（`Release\Skins\` は Publish で上書きされる）。

ビルド出力（bin/obj）は `Directory.Build.props` で `%LOCALAPPDATA%\SecGad\build` へ逃がしている（ソースを共有フォルダに置いても同期させないため）。

⚠️ 終了は必ずメニューの「終了」か `--exit` で。本家 Teams の小窓を透明化している最中に強制終了すると、小窓が表示されなくなる（次回起動時に修復はする）。

## 設定・データの場所

| ファイル | 場所 |
|---|---|
| settings.json | `%LOCALAPPDATA%\SecGad\settings_location.json` の `SettingsDir`（環境変数展開可）、無ければ `%LOCALAPPDATA%\SecGad\` |
| log.txt | `%LOCALAPPDATA%\SecGad\`（件名・差出人・本文・人名は書かない） |
| 追加スキン | `%LOCALAPPDATA%\SecGad\skins\<スキンID>\` |

settings.json のフォルダは、メニューの「設定」→「設定ファイルのフォルダを変更」で選び直せる（`settings_location.json` が書き換わり、次回の起動からもそのフォルダを使う）。選んだフォルダに settings.json が無ければ今の設定をそこへ保存し、あればそちらを読み込んで使う（共有フォルダで複数のPCの設定を揃える用）。元の場所の settings.json は消さない。

settings.json の主な項目: `UserName`（あいさつの呼び名）、`OutlookWatchFolders`（見張りフォルダ。`"受信トレイ/サブフォルダ"` のように書く）、`TeamsMentionKeywords`（自分の姓の漢字・ひらがな・ローマ字など）、`MarkReadSearchFolder`（呼んだときに既読化する検索フォルダー。空でオフ）。

### 吹き出しの選択肢（`CallMenu`）

「秘書さんを呼ぶ」の吹き出しの下に並べる選択肢のリスト。行を足せば選択肢も増える。秘書さんを呼ぶたびに読み直すので、書き足したら再起動しなくても次に呼んだときから出る。

```jsonc
"CallMenu": [
  { "Label": "本日の予定", "Path": "@schedule" },
  { "Label": "箱庭", "Path": "D:\\GitHub\\stable\\Essentials\\ClaudeOffice\\open.ps1" },
  { "Label": "株レーダー", "Path": "D:\\GitHub\\stable\\Essentials\\StockRadar\\open.ps1" },
  { "Label": "タイマー", "Path": "@timer" },
  { "Label": "日報を書く", "Path": "C:\\Tools\\nippo.bat" },
  { "Label": "メモ帳", "Path": "%WINDIR%\\notepad.exe", "Arguments": "memo.txt" }
]
```

| 項目 | 内容 |
|---|---|
| `Label` | 吹き出しに出す表示名 |
| `Path` | 起動するファイル（exe・bat・ps1・ショートカットなど。環境変数可。JSON なので `\` は `\\` と書く）。外のツールの場所は秘書さんに持たせないので、必ずここに書く。例外は、外のファイルを持たない秘書さん自身の機能だけ。これは `@` で始まる名前で呼ぶ: `@schedule`（本日の予定）・`@timer`（タイマー。セット中のタイマーがあれば、吹き出しでセットか取り消しを選ぶ） |
| `Arguments` | 起動するファイルに渡す引数（省略可） |

ファイルは関連付けで開く（bat はそのファイルのフォルダを作業フォルダにして実行）。ps1 だけは関連付けだとメモ帳で開いてしまうので、PowerShell で窓なしに実行して終わるまで待つ。終了コードが 0 以外なら、スクリプトが最後に出力した1行を吹き出しで知らせる（文字化けしないよう、スクリプト側で `[Console]::OutputEncoding = [Text.Encoding]::UTF8` にしておく）。

箱庭（ClaudeOffice）・株レーダー（StockRadar）には、この用の `open.ps1` がある（未起動なら窓なしで起動し、待ち受けを待ってからブラウザで開く）。

`CallMenu` の項目自体が無ければ `@schedule`・`@timer` の2つ、`[]` にすると選択肢を出さない。

## スキン

```
<exe のフォルダ>\Skins\<スキンID>\       … 同梱
%LOCALAPPDATA%\SecGad\skins\<スキンID>\  … 追加分（同じIDは同梱が優先）
    secretary.png  必須：背景透過の縦長の絵（目安 1024×1536。キャラの下端が画像の下端に接し、左上に吹き出し用の余白、顔が上から約23%）
    icon.ico       任意：トレイアイコン（16/32/48 を含むマルチサイズ）
    skin.json      任意：{ "Name": "メニュー表示名", "BubbleOverlap": 0.34 }
```

いまの同梱スキンは `enji`（園児）・`blue`（青髪）・`red`（赤髪）・`brown`（茶髪）・`green`（黄緑髪）・`rose`（ローズ髪）・`maid`（メイド）・`butler`（執事）・`megane`（メガネ）・`redboy`（赤髪の秘書くん）の10個。スキンが1つも無いときの保険として、exe に埋め込んだ既定絵 `Assets\secretary.png` も同じ画像にしてある。スキンを足したらメニューの「スキン一覧を読み直す」で反映される。

## 起動引数（確認用）

| 引数 | 意味 |
|---|---|
| `--exit` | 動いている秘書さんを後片付け付きで終了 |
| `--wait-instance` | 前の秘書さんの終了を最大30秒待ってから起動 |
| `--skin <ID>` / `--scale <倍率>` | 設定を変えずに起動 |
| `--render-preview <png>` | 吹き出し付きの見た目を背景 #3A6EA5 で PNG にして終了（`--preview-call` / `--preview-schedule` で見本データ） |
| `--diag-*` | `call` / `summary` / `restart-mb N` / `no-outlook` / `no-popup` / `no-toast` / `no-mention-count` / `capture-on` / `snooze-1min` / `meeting-expiry` |

## テスト

```
dotnet test SecretaryGadget.sln -c Release
```
