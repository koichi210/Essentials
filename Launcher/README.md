# Launcher

よく使うexe・URLなどを登録しておき、ワンクリックで起動できるランチャーツール。
登録データは **[MagicLauncher](../MagicLauncher)（魔法陣ランチャー）と共通の設定ファイル `launcher.json`** をそのまま読み書きする。
どちらで登録・編集しても、もう片方にそのまま出る。

## 機能

- 魔法陣（MagicLauncher の `circles`）ごとに、セクション（= タグ。MagicLauncher の Node）を並べ、セクションは折りたたみ表示できる
- 項目は **先頭のタグ** のセクションに並ぶ（MagicLauncher で円がぶら下がる場所と同じ）。タグの無い項目は最初の魔法陣の「タグなし」へ
- 登録できるリンク先の例
  - Windowsデスクトップ上のexeのフルパス（例: `C:\Windows\System32\notepad.exe`）
  - `http` から始まるURL（例: `https://example.com`）や `steam://` などスキーム付きのURL
  - その他、Windowsが関連付けで開けるパス（フォルダ、ドキュメント等）。`%USERPROFILE%` などの環境変数も使える
- 画面上から 魔法陣へのセクション追加／名前変更／削除、項目の追加／編集／削除 が可能
- 変更はその場で設定ファイルへ保存。MagicLauncher や手で編集した内容は 10 秒ごと（とウィンドウに戻ったとき）に自動で取り込む

## 設定ファイル（MagicLauncher と共通）

- 場所は MagicLauncher と同じく **`%LOCALAPPDATA%\MagicLauncher\settings.json` の `configPath`** で決まる（`%VAR%` 可）
  - `configPath` が無いときは、このアプリの既定 `%LOCALAPPDATA%\Launcher\launcher.json` を使う（MagicLauncher の既定は本体の隣なので、この間は共有されない。画面に「まだ共有していません」と出る）
  - 画面右上の「設定ファイルの場所を変更...」で選んだファイルは `configPath` に書く → **MagicLauncher も同じファイルを使うようになる**
- 形式は MagicLauncher の README の「設定ファイル（launcher.json）」のとおり。このアプリが使うのは次の値だけで、ほかの値（`emoji`・`note`・`circleColors`・`idle`・`columns` など）は**そのまま残す**

| 値 | このアプリでの意味 |
| --- | --- |
| `circles` / `title` | 魔法陣の並び。先頭（無ければ `title`、それも無ければ `Launcher`）が「最初の魔法陣」 |
| `tags` | キーの順がセクションの並び。`circle` が置く魔法陣（空欄なら最初の魔法陣）、`color` はセクション名の前の丸 |
| `items[]` | `id`・`name`（空ならURLはホスト名、パスは末尾の名前を表示）・`path`・`tags`（先頭のタグのセクションに並ぶ）・`emoji`（ボタンの頭に出す）・`note`（マウスを載せると出る） |

### 読み書きのルール（MagicLauncher の server.js と同じ）

- 書き込みのたびに**ディスクの内容を読み直してから差分だけ当てて**保存する（画面が持つ古い一覧で上書きしない）。保存直前に `.bak` へ 1 世代コピー
- 書き出しは JavaScript の `JSON.stringify(data, null, 2)` と同じ形（日本語・絵文字はそのまま、キーの順も同じ）。何も変えずに保存すると1バイトも変わらない
- id の無い項目には MagicLauncher と同じ「中身と位置から決まる id」を付ける。新しいタグには同じ 12 色のパレットから未使用の色を順に割り当てる
- 読めない・一番外が `{}` でない・`items` が配列でない・フォルダを指定 → 画面に赤い帯で警告し、**直るまで保存しない**
- MagicLauncher と違う点: 項目の無いタグもセクションとして出す（作ったばかりのセクションに項目を足せるように。MagicLauncher 側では項目が付くまで出ない）。セクションの削除は MagicLauncher に無い操作で、そのタグだけが付いた項目は削除、ほかのタグも付いた項目はタグを外すだけ（残りのタグのセクションへ移る）

### この PC だけの値

- セクションの開閉状態は共有の設定ファイルに書かず、`%LOCALAPPDATA%\Launcher\view.json` に覚える
- 以前の版の `%LOCALAPPDATA%\Launcher\config.json`・exe の隣の `DataFolder.txt` はもう使わない（残っていても読まない）

## ビルド・実行・テスト

.NET 10 SDK が必要。

```powershell
dotnet build Launcher/Launcher.csproj
dotnet run --project Launcher/Launcher.csproj
dotnet test Tests/Launcher.Tests/Launcher.Tests.csproj
```

テストの `TestData/messy.json` は散らかった launcher.json（id なし・id 重複・色なし・数字だけのタグ名など）、
`messy.expected.json` はそれを MagicLauncher の server.js が何も変えずに保存したときの実際の出力（正解）。
MagicLauncher の書き方を変えたら、server.js で作り直してテストを見直す。

確認するときは、環境変数 `MAGIC_LAUNCHER_DATA` に偽フォルダ（`settings.json` の `configPath` でコピーした launcher.json を指す）を入れて起動すれば、本番の設定ファイルを汚さない（MagicLauncher の確認方法と同じ）。
