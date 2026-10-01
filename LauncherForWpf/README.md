# LauncherForWpf

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

## 設定ファイル

- 保存場所: `%APPDATA%\LauncherForWpf\config.json`
  （Windowsのユーザーアカウントごとに自動的に別ファイルになる）
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
dotnet build LauncherForWpf/LauncherForWpf.csproj
dotnet run --project LauncherForWpf/LauncherForWpf.csproj
```
