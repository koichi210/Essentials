# Launcher

よく使うexe・URLなどを登録しておき、ワンクリックで起動できるランチャーツール。

## 機能

- セクション単位でリンクをグルーピングし、折りたたみ表示できる
- セクション・項目ともに登録数は無制限（可変）
- 登録できるリンク先の例
  - Windowsデスクトップ上のexeのフルパス（例: `C:\Windows\System32\notepad.exe`）
  - `http` から始まるURL（例: `https://example.com`）
  - その他、Windowsが関連付けで開けるパス（フォルダ、ドキュメント等）
- 画面上から セクション追加／名前変更／削除、項目の追加／編集／削除 が可能
- 設定は自動保存される
- 保存先フォルダは画面から変更可能（Cheetos/FFEdit/FileArranger等の他ツールと同じ方式）

## 設定ファイル

- 既定の保存場所: `%LOCALAPPDATA%\Launcher\config.json`
  （Windowsのユーザーアカウントごとに自動的に別ファイルになる）
- 保存先フォルダの管理はCheetos/FFEdit/FileArranger等と共通の仕組み（`_Common/UserDataLocation.cs`）を使用。
  exeと同じフォルダに置かれる案内板ファイル`DataFolder.txt`（1行目に実データフォルダのパス）経由で
  実データフォルダの場所を解決する。`DataFolder.txt`が無い/壊れている場合は既定の
  `%LOCALAPPDATA%\Launcher\`を使い、`DataFolder.txt`は次回起動時に自動で作り直される
- 画面右上の「保存先フォルダを変更...」から任意のフォルダ（共有フォルダ等）に変更可能。
  既存のconfig.jsonは新しい保存先へ自動で移動する（選択先に既にconfig.jsonがあればそちらを使う）
- 初回起動時はサンプル設定（メモ帳・電卓・Google）が自動生成される
- 画面右上の「設定ファイルを開く」から直接JSONを編集することも可能

### JSONスキーマ例

```json
{
  "Sections": [
    {
      "Name": "よく使うツール",
      "IsExpanded": true,
      "Items": [
        { "Title": "メモ帳", "Path": "C:\\Windows\\System32\\notepad.exe" },
        { "Title": "Google", "Path": "https://www.google.com" }
      ]
    }
  ]
}
```

## ビルド・実行

.NET 10 SDK が必要。

```
dotnet build Launcher/Launcher.csproj
dotnet run --project Launcher/Launcher.csproj
```
