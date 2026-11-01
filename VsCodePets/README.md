# vscode-pets (Flying)

VS Code のパネルにペットを住まわせる拡張機能「vscode-pets」の改造版です。ペットがパネルの中を**飛び回る**ようにし、掴んで動かしたり、卵を産んだり、時間帯に合わせておしゃべりしたりします。

- 拡張 ID: `essentials-local.vscode-pets`
- 表示名: vscode-pets (Flying)
- version: 1.36.2（上流タグ 1.36.0 がベース）

## 画像について

ペットの GIF・アイコン・背景・ボタンの画像は、**すべてこのリポジトリで新しく描いたオリジナル**です（上流の画像は使っていません）。
`tools/generate_art.py` がドット絵を手続き的に描いて `media/` に書き出します。

```
py -3 tools/generate_art.py media
```

選べるペットは、オリジナルの絵を描いた 15 種類です（dog・fox・chicken・cockatiel・crab・horse・monkey・panda・raccoon・rat・rubber-duck・skeleton・snail・snake・turtle）。

## 上流からの改造

| 改造 | 内容 |
|---|---|
| 飛行 | 地上の動きが終わるたびに 80% で飛び立ち、パネル内のランダムな点へ飛ぶ → 4秒ホバリング → 70% でもう一度飛ぶ／30% で真下へ降りる |
| マウスで掴む | ペットを左ボタンで掴んで動かせる。空中で離すと降りてくる |
| 卵と 💩 | ときどきパネルの上端まで飛んで 🥚 か 💩 を落とす。🥚 は20秒で孵化（クリックで即孵化）、💩 はクリックで消える |
| おしゃべり | 約10秒に1回、時間帯に合ったひとことを吹き出しで話す |
| ペット同士の回避 | 近づくと進路を変え、重なったら押し離す |
| パネル内クランプ | パネルを小さくしても、ペット・吹き出し・卵がはみ出さない |
| 雪の軽量化 | winter テーマの雪を 30fps・粒数 1/3 にした |
| ワークスペース単位の保存 | ペット一覧と「Change Theme」で選んだテーマをワークスペースごとに覚える |

コマンド・設定は上流と同じです（新しい設定・コマンドはありません）。上流の vscode-pets と同時に入れるとコマンド ID が衝突するため、上流版はアンインストール（または無効化）してください。

## ビルド

Node.js 20 以上。

```
npm ci
npm run compile
npm run lint
npx @vscode/vsce package -o release/vscode-pets-flying.vsix
code --install-extension release/vscode-pets-flying.vsix --force
```

## ライセンス

MIT。コードは [tonybaloney/vscode-pets](https://github.com/tonybaloney/vscode-pets)（MIT License）をもとにしています（`LICENSE` を参照）。
