# Essentials

ふだんよく使っている Windows 用のツールを集めたリポジトリです（C# の WPF と、C++ の MFC）。

| ツール | 説明 | 動作環境 |
|---|---|---|
| [ClaudeOffice](ClaudeOffice) | Claude Code の会話の様子を 3D の箱庭オフィスで見渡すローカル用ダッシュボード | Node.js 22.5 以上 |
| [BlurPaint](BlurPaint) | スクリーンショットに矢印・枠・文字・番号・ぼかし／モザイクの注釈を入れる、ブラウザだけで動くペイントエディタ | Edge / Chrome（インストール不要） |
| [Cheetos](Cheetos) | 画面（全画面・いまのモニター・いまのウィンドウ）を、決めた間隔で連続キャプチャして保存する | .NET Framework |
| [DigitalClock](DigitalClock) | 時刻だけを大きく表示するデジタル時計 | .NET Framework |
| [DuplicateFinder](DuplicateFinder) | 複数のフォルダから中身が同じファイルを探し出して、選んで削除する | .NET Framework 4.8 |
| [EventRecorder](EventRecorder) | キーボードとマウスの操作を記録して再生する（プレイリスト・ループ再生） | .NET Framework 4.8 / x64 |
| [FFEdit](FFEdit) | フォルダ内のファイル・フォルダの名前を、通し番号などでまとめて変換する | .NET Framework |
| [FileArranger](FileArranger) | フォルダの移動や名前変更、ファイルの移動・振り分けをまとめて行う | .NET Framework |
| [VsCodePets](VsCodePets) | VS Code のパネルでペットが飛び回る拡張機能（vscode-pets の改造版・画像はすべてオリジナル） | VS Code 1.73 以上 |
| [Launcher](Launcher) | よく使う exe や URL を登録して、ワンクリックで起動する | .NET 10（ランタイムが必要） |
| [Mailer](Mailer) | 宛先・件名・本文を決めておいて、ブラウザのメール作成画面を開く | .NET Framework |
| [Othello](Othello) | オセロ（人対人・人対COM・COM対COM） | .NET Framework |
| [SecretaryGadget](SecretaryGadget) | Outlook の新着メール・会議の開始前・Teams のメンションを、画面右下の秘書さんが吹き出しで知らせる常駐ガジェット | .NET 9 以降（ランタイムが必要） |
| [OthelloMfc](OthelloMfc) | オセロの C++ 版 | C++ / MFC |
| [LoginHistory](LoginHistory) | PC が動いていた時刻を、一定の間隔でログファイルに書き残す稼働ログ | C++ / MFC |

各ツールの `bin/Release`（C++ の2本と SecretaryGadget は `Release`）に、ビルド済みの exe を置いています。

## ビルド

共通クラスは [CS_Common](https://github.com/koichi210/CS_Common) を `_Common` サブモジュールとして使っています。

```
git clone --recurse-submodules https://github.com/koichi210/Essentials.git
```

C++ の2本（OthelloMfc / LoginHistory）は Visual Studio 2019（v142、MFC 入り）で、x64 でビルドします。

```
MSBuild.exe OthelloMfc\OthelloMfc.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64
```

## ライセンス

MIT
