# Claude Office

Claude Code の各セッションが「いま何をしているか」「過去に何をしたか」を、3D の箱庭オフィス（浮島の上の教室）で見渡すローカル専用ダッシュボードです。

- `%USERPROFILE%\.claude` 配下の会話記録を**読み取り専用**で解析し、在席中の会話1つにつき1人を机に座らせて、状態（作業中・考え中・入力待ち・おやすみ）をアニメーションで見せます
- 過去セッションの検索・日別タイムライン・使用率（5時間／週）の表示
- VSCode 版 Claude Code での再開、箱庭からの会話の続きの送信
- 詳細の窓の**会話ログ**（やりとりをターミナル風に読む）と、**箱庭で話す**（VSCode 拡張と同じやり方で claude を動かし、箱庭から話す・承認する）
- 返事が終わったときの Windows 通知

## 動かし方

Node.js 22.5 以上（`node:sqlite` を使うため）。npm パッケージは使いません。

```
start.bat        起動済みならブラウザで開くだけ、未起動なら起動
stop.bat         8765 で LISTEN 中のプロセスを止める
autostart.ps1    窓なしで裏起動（スタートアップ用）
open.ps1         窓なしで、未起動なら起動して待ち受けまで待ってからブラウザで開く（秘書さんの吹き出しの選択肢などから呼ぶ用）
```

スタートアップに置くショートカットのリンク先:

```
powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File autostart.ps1
```

待ち受けは `http://127.0.0.1:8765/` だけです（他の PC からはつながりません）。`/api/` は Host ヘッダが `127.0.0.1:<PORT>` か `localhost:<PORT>` のときだけ受けます（ほかのドメインを 127.0.0.1 に向け直した要求は 403）。

### 返事が終わったときの通知

`~/.claude/settings.json` の `hooks.Stop[].hooks[]` に次を追加します（画面の ⚙️ → 通知「設定画面を開く」にも表示されます）。

```json
{"type":"command","shell":"bash","timeout":10,"async":true,
 "command":"curl -s -m 3 -X POST -H 'Content-Type: application/json' --data-binary @- http://127.0.0.1:8765/api/hook/stop >/dev/null 2>&1 || true"}
```

## 会話ログと箱庭で話す

詳細の窓は「情報（はみ出したらここだけスクロール）／会話欄／送信欄」の3段です。窓は上下左右の辺と4つの角で大きさを変えられます。

- **💬 会話ログ**（読むだけ）: その会話の記録（`.jsonl`）を組み立て、時刻・`›` 依頼・`●` 返事・`⎿` ツールの形で出します。2秒ごとに続きだけ足します。巻き戻した会話は、いま続いている側の1本だけが出ます（1,500件を超える分は「古い N 件は省略」）。コマンドの本文・ツールの結果・考え中の中身は出さず、秘密はマスクします
- **💬 ここで話す**: どこでも開いていない会話なら、会話欄の下に入力欄が出て、箱庭から話せます。サーバが VSCode 拡張と同じやり方（`claude --input-format stream-json --output-format stream-json --permission-prompt-tool stdio`）で claude を動かし続け、JSON の行を送り合います。ネイティブ部品は要りません
  - 発言には `origin: {kind: "human"}` を付けるので、記録では VSCode やターミナルで打ったのと同じ**本人の発言**（`turnOrigin: human`）になります
  - 返事の途中経過は会話ログの下に流れ、書き終わると会話ログに入ります
  - ツールの承認はカードで出ます（✅ 許可／✅ この会話中は許可／🚫 許可しない）。承認するには何をするのかが見えないといけないので、カードにはコマンドもマスクして出します。claude からの質問（AskUserQuestion）は選択肢のボタンで、計画（ExitPlanMode）は「この計画で進める」で答えます
  - Enter 送信／Shift+Enter 改行／Esc・「⏹ 中断」でいまの返事を止める（窓は閉じない）。`/` コマンドは使えません
  - 「■ 終える」で claude を子プロセスごと止め、話した分も入った会話ログに戻ります。詳細の窓を閉じても claude は動いたままです。ページを再読み込みしても続きから使えます
- **右パネル「🏢 在席」の上の「💬 箱庭で話す」**: 話している会話の一覧（⏳ 返事中・🔐 承認待ち）と、今までに使ったフォルダを選んで「＋ 新しい会話」。詳細の窓を開いていない会話は別の窓で開き（`＿` で隠す・一覧から戻す）、その会話の詳細の窓を開くと会話欄へ移ります
- **サーバを止めると、箱庭で話している claude も終わります**（記録はそこまで残ります）
- 左上の HUD・右パネル・下のタイムラインは端へたためます（◀ ▶ ▾。次に開いたときも覚えています）

### 同じ会話を2か所で開かない

同じ会話を2本同時に `--resume` すると記録が枝分かれするため、VSCode（ほかのターミナル）で開いている会話・箱庭から続きを送っている途中の会話は、箱庭で話せません。逆に箱庭で話している間は、送信欄と「VSCode で開く」が使えません。

VSCode でタブを開いただけ（まだ発言していない）の会話は claude が起動しておらず見分けられないので、**VSCode で開いているタブは閉じてから使ってください**。

### 同梱物

npm install はせず、取ってきたファイルを置くだけです。

| 部品 | 置き場所 | 取り方 |
|---|---|---|
| xterm.js 5.5.0 | `public/vendor/xterm/` | `npm pack @xterm/xterm@5.5.0` の `lib/xterm.js`・`css/xterm.css`・`LICENSE` |
| @xterm/addon-fit 0.10.0 | `public/vendor/xterm/` | `npm pack @xterm/addon-fit@0.10.0` の `lib/addon-fit.js` |

## 環境変数

| 変数 | 既定 | 意味 |
|---|---|---|
| `PORT` | 8765 | 待ち受けポート |
| `CLAUDE_OFFICE_TALK_ARGS` | なし | 箱庭で話すときに claude に足す引数（空白区切り。試す用。例 `--model haiku`） |
| `HISTORY_DAYS` | 0 | 読み込む履歴の日数（0 = 全期間） |
| `CLAUDE_CONFIG_DIR` | `%USERPROFILE%\.claude` | Claude Code の設定フォルダ |
| `CLAUDE_OFFICE_DATA` | `%LOCALAPPDATA%\ClaudeOffice` | 要約の保存先（`archive.json`）とログ |

## 書き込むファイル

`%USERPROFILE%\.claude` と VSCode の状態ファイルは読むだけです。書き込むのは次の2つだけです。

- `%LOCALAPPDATA%\ClaudeOffice\archive.json` … 要約の保存（会話記録は既定30日で消えるため）
- このフォルダの `notify.json` … 通知の設定（保存したときだけ作られる。git 管理外）

## 画面

- 左上: 時計・時間帯・在席数・アカウントと使用率・設定（スキン／動作モード）
- 右: 在席・グループ（VSCode のグループ）・履歴
- 下: 日別タイムライン（◀▶ で日付移動、時間プレビューで空の色を任意の時刻に）
- 箱庭: 人をクリックで詳細の窓、ダブルクリックで VSCode で開く。本棚の本は過去のセッション
- スキン: 🏫 教室（白い壁と芝生の浮島）／🍺 冒険者の酒場（ステンドグラスと丸テーブル、ストーンサークル）／💀 魔界（溶岩のひびが走る黒い島と魔王の館）
- 本棚・コーヒーなど主な家具には名札が付く。席のメンバーは仕事中に席を立って歩き、立ち寄り先（水槽・カウンター・大釜）へ寄る
- 動作モード: ☀️ 標準（酒場のきらきら・魔界の火の粉、ブルーム、影あり）／🍃 軽量（演出・影なし）／🪶 超軽量（壁・時計・人の玉だけ）

URL 引数: `?skin=default|isekai|makai`、`?t=18.5`（時間プレビュー）、`?session=<id>`（詳細を開いて開始）、`?first`、`?tab=groups|history`

## テスト

```
node --test test/server.test.js
```

## ライセンス

同梱の Three.js r170（`public/vendor/`）は MIT License（`public/vendor/LICENSE`）。
xterm.js・@xterm/addon-fit（`public/vendor/xterm/`）も MIT License（`public/vendor/xterm/LICENSE`）。
