# Claude Office

Claude Code の各セッションが「いま何をしているか」「過去に何をしたか」を、3D の箱庭オフィス（浮島の上の教室）で見渡すローカル専用ダッシュボードです。

- `%USERPROFILE%\.claude` 配下の会話記録を**読み取り専用**で解析し、在席中の会話1つにつき1人を机に座らせて、状態（作業中・考え中・入力待ち・おやすみ）をアニメーションで見せます
- 過去セッションの検索・日別タイムライン・使用率（5時間／週）の表示
- VSCode 版 Claude Code での再開、箱庭からの会話の続きの送信
- 返事が終わったときの Windows 通知

## 動かし方

Node.js 22.5 以上（`node:sqlite` を使うため）。npm パッケージは使いません。

```
start.bat        起動済みならブラウザで開くだけ、未起動なら起動
stop.bat         8765 で LISTEN 中のプロセスを止める
autostart.ps1    窓なしで裏起動（スタートアップ用）
```

スタートアップに置くショートカットのリンク先:

```
powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File autostart.ps1
```

待ち受けは `http://127.0.0.1:8765/` だけです（他の PC からはつながりません）。

### 返事が終わったときの通知

`~/.claude/settings.json` の `hooks.Stop[].hooks[]` に次を追加します（画面の ⚙️ → 通知「設定画面を開く」にも表示されます）。

```json
{"type":"command","shell":"bash","timeout":10,"async":true,
 "command":"curl -s -m 3 -X POST -H 'Content-Type: application/json' --data-binary @- http://127.0.0.1:8765/api/hook/stop >/dev/null 2>&1 || true"}
```

## 環境変数

| 変数 | 既定 | 意味 |
|---|---|---|
| `PORT` | 8765 | 待ち受けポート |
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
- スキン: 🏫 教室（白い壁と芝生の浮島）／🍺 ルイーダの酒場（ステンドグラスと丸テーブル、ストーンサークル）／💀 魔界（溶岩のひびが走る黒い島と魔王の館）
- 本棚・コーヒーなど主な家具には名札が付く。席のメンバーは仕事中に席を立って歩き、立ち寄り先（水槽・カウンター・大釜）へ寄る
- 動作モード: ☀️ 標準（酒場のきらきら・魔界の火の粉、ブルーム、影あり）／🍃 軽量（演出・影なし）／🪶 超軽量（壁・時計・人の玉だけ）

URL 引数: `?skin=default|isekai|makai`、`?t=18.5`（時間プレビュー）、`?session=<id>`（詳細を開いて開始）、`?first`、`?tab=groups|history`

## テスト

```
node --test test/server.test.js
```

## ライセンス

同梱の Three.js r170（`public/vendor/`）は MIT License（`public/vendor/LICENSE`）。
