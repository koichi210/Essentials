// node --test test/server.test.js で実行する(依存なし)。
// 本番ポートは使わず PORT=8791・MAGIC_LAUNCHER_DATA=一時フォルダ・MAGIC_LAUNCHER_DRY_RUN=1 で別に立てて確かめる。
'use strict';

const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const os = require('os');
const path = require('path');
const http = require('http');
const { spawn } = require('child_process');
const s = require('../server.js');

const PORT = Number(process.env.TEST_PORT) || 8791;
const BASE = `http://127.0.0.1:${PORT}`;
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'magic-launcher-test-'));
const dataDir = path.join(tmp, 'data');
const work = path.join(tmp, 'work');
const config = path.join(work, 'launcher.json');
let child;

function post(p, body, headers = {}) {
  return fetch(BASE + p, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Launcher': '1', ...headers },
    body: JSON.stringify(body),
  });
}
async function postJson(p, body) {
  const r = await post(p, body);
  return { status: r.status, body: await r.json() };
}
async function state() {
  return (await fetch(`${BASE}/api/state`)).json();
}
function raw(method, p, headers, body) {
  return new Promise((resolve, reject) => {
    const req = http.request({ host: '127.0.0.1', port: PORT, method, path: p, headers }, (res) => {
      let data = '';
      res.on('data', (c) => { data += c; });
      res.on('end', () => resolve({ status: res.statusCode, body: data }));
    });
    req.on('error', reject);
    if (body) req.write(body);
    req.end();
  });
}
function readConfig() {
  return JSON.parse(fs.readFileSync(config, 'utf8'));
}

test.before(async () => {
  fs.mkdirSync(path.join(work, 'Folder A&B %X%'), { recursive: true });
  fs.writeFileSync(path.join(work, 'memo.txt'), 'hello');
  fs.mkdirSync(dataDir, { recursive: true });
  // 設定ファイルの場所は %VAR% 入りで覚える
  fs.writeFileSync(path.join(dataDir, 'settings.json'), JSON.stringify({ configPath: '%ML_TEST_WORK%\\launcher.json' }));
  child = spawn(process.execPath, [path.join(__dirname, '..', 'server.js')], {
    env: { ...process.env, PORT: String(PORT), MAGIC_LAUNCHER_DATA: dataDir, MAGIC_LAUNCHER_DRY_RUN: '1', ML_TEST_WORK: work },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  for (let i = 0; i < 50; i++) {
    try {
      await fetch(`${BASE}/api/version`);
      return;
    } catch {
      await new Promise((r) => setTimeout(r, 100));
    }
  }
  throw new Error('server did not start');
});

test.after(() => {
  if (child) child.kill();
  fs.rmSync(tmp, { recursive: true, force: true });
});

test('小物: URL 判定・引用符外し・%VAR% 展開・id', () => {
  assert.ok(s.isUrl('https://example.com'));
  assert.ok(s.isUrl('vscode://file/c:/x'));
  assert.ok(s.isUrl('onenote:foo'));
  assert.ok(!s.isUrl('C:\\Users'));
  assert.ok(!s.isUrl('\\\\server\\share'));
  assert.strictEqual(s.stripQuotes('  "C:\\a b\\c"  '), 'C:\\a b\\c');
  process.env.ML_UNIT_X = 'XX';
  assert.strictEqual(s.expandEnv('%ML_UNIT_X%\\a %NO_SUCH_VAR_123%'), 'XX\\a %NO_SUCH_VAR_123%');
  assert.strictEqual(s.itemId({ path: 'a' }, 0), s.itemId({ path: 'a' }, 0));
  assert.notStrictEqual(s.itemId({ path: 'a' }, 0), s.itemId({ path: 'a' }, 1));
  assert.deepStrictEqual(s.cleanTags([' a ', 'a', '', 'b', null]), ['a', 'b']);
});

test('ファイルが無いときは空の状態で、場所は %VAR% を展開して返す', async () => {
  const st = await state();
  assert.strictEqual(st.exists, false);
  assert.strictEqual(st.error, null);
  assert.strictEqual(st.configPath, config);
  assert.strictEqual(st.configPathRaw, '%ML_TEST_WORK%\\launcher.json');
  assert.strictEqual(st.isDefaultPath, false);
  assert.strictEqual(st.idle, 'orbit');
  assert.strictEqual(st.dryRun, true);
  assert.deepStrictEqual(st.items, []);
});

test('追加・更新・削除が launcher.json に反映され .bak が残る', async () => {
  const a = await postJson('/api/item', { item: { name: '', path: `"%ML_TEST_WORK%\\Folder A&B %X%"`, tags: ['リポジトリ', 'レジプラ'], emoji: '', note: 'n' } });
  assert.strictEqual(a.status, 200);
  assert.match(a.body.id, /^i[0-9a-f]{8}$/);
  let cfg = readConfig();
  assert.strictEqual(cfg.items.length, 1);
  assert.strictEqual(cfg.items[0].path, '%ML_TEST_WORK%\\Folder A&B %X%'); // 前後の "..." は外す・%VAR% はそのまま保存
  assert.ok(cfg.tags['リポジトリ'].color.match(/^#[0-9a-f]{6}$/));
  assert.notStrictEqual(cfg.tags['リポジトリ'].color, cfg.tags['レジプラ'].color);
  assert.ok(!fs.existsSync(`${config}.bak`)); // 新規作成では .bak は無い

  const b = await postJson('/api/item', { item: { name: 'Memo', path: '%ML_TEST_WORK%\\memo.txt', tags: ['ツール'] } });
  assert.ok(fs.existsSync(`${config}.bak`));
  assert.strictEqual(JSON.parse(fs.readFileSync(`${config}.bak`, 'utf8')).items.length, 1); // 直前の内容
  await postJson('/api/item', { item: { name: 'Web', path: 'https://example.com/x', tags: ['ツール'] } });
  await postJson('/api/item', { item: { name: 'VS', path: 'vscode://file/c:/x', tags: [] } });
  await postJson('/api/item', { item: { name: 'Gone', path: 'C:\\no\\such\\path\\zzz', tags: ['ツール'] } });

  const st = await state();
  const kinds = Object.fromEntries(st.items.map((it) => [it.name || 'folder', it.kind]));
  assert.deepStrictEqual(kinds, { folder: 'folder', Memo: 'file', Web: 'web', VS: 'link', Gone: 'missing' });

  const u = await postJson('/api/item', { item: { id: b.body.id, name: 'Memo2', path: '%ML_TEST_WORK%\\memo.txt', tags: ['ツール', 'リポジトリ'], emoji: '📝', note: '' } });
  assert.strictEqual(u.status, 200);
  cfg = readConfig();
  assert.strictEqual(cfg.items.find((x) => x.id === b.body.id).name, 'Memo2');

  const nf = await postJson('/api/item', { item: { id: 'izzzzzzzz', name: 'x', path: 'C:\\x' } });
  assert.strictEqual(nf.status, 404);
  assert.match(nf.body.error, /ほかの PC で削除されたかも/);
  const empty = await postJson('/api/item', { item: { name: 'x', path: '  ' } });
  assert.strictEqual(empty.status, 400);

  const vs = (await state()).items.find((x) => x.name === 'VS');
  assert.strictEqual((await postJson('/api/item/delete', { id: vs.id })).status, 200);
  assert.ok(!readConfig().items.some((x) => x.name === 'VS'));
  assert.strictEqual((await postJson('/api/item/delete', { id: vs.id })).status, 404);
});

test('開く: 登録済み id だけ・%VAR% 展開・無いパスは 404・DRY RUN は開かずにパスを返す', async () => {
  const st = await state();
  const folder = st.items.find((x) => x.kind === 'folder');
  const o = await postJson('/api/open', { id: folder.id });
  assert.strictEqual(o.status, 200);
  assert.deepStrictEqual(o.body, { opened: path.join(work, 'Folder A&B %X%'), dryRun: true });
  const web = st.items.find((x) => x.kind === 'web');
  assert.strictEqual((await postJson('/api/open', { id: web.id })).body.opened, 'https://example.com/x');
  const gone = st.items.find((x) => x.kind === 'missing');
  const g = await postJson('/api/open', { id: gone.id });
  assert.strictEqual(g.status, 404);
  // %USERPROFILE% 入りのパスも開ける
  const up = await postJson('/api/item', { item: { name: 'Home', path: '%USERPROFILE%', tags: [] } });
  assert.strictEqual((await state()).items.find((x) => x.id === up.body.id).kind, 'folder');
  assert.strictEqual((await postJson('/api/open', { id: up.body.id })).body.opened, path.normalize(os.homedir()));
  await postJson('/api/item/delete', { id: up.body.id });
  // 任意のパスを開く口は無い(id 以外は受け付けない)
  assert.strictEqual((await postJson('/api/open', { path: 'C:\\Windows' })).status, 404);
  assert.strictEqual((await postJson('/api/open', { id: 'C:\\Windows' })).status, 404);
});

test('id が無い項目には、中身と位置から決まる id が読むたびに同じ値で付く', async () => {
  const cfg = readConfig();
  cfg.items.push({ path: '%ML_TEST_WORK%\\memo.txt', name: 'HandWritten', tags: ['手書き'] });
  fs.writeFileSync(config, JSON.stringify(cfg, null, 2));
  const a = (await state()).items.find((x) => x.name === 'HandWritten');
  const b = (await state()).items.find((x) => x.name === 'HandWritten');
  assert.match(a.id, /^i[0-9a-f]{8}$/);
  assert.strictEqual(a.id, b.id);
  assert.ok((await state()).tags['手書き'].color); // 手で足したタグにも色が付いて見える
  assert.strictEqual(readConfig().tags['手書き'], undefined); // ただしファイルには書かない
  // その id で開ける・更新できる(更新時に id を書き込む)
  assert.strictEqual((await postJson('/api/open', { id: a.id })).status, 200);
  assert.strictEqual((await postJson('/api/item', { item: { id: a.id, name: 'HandWritten', path: '%ML_TEST_WORK%\\memo.txt', tags: ['手書き'] } })).status, 200);
  assert.strictEqual(readConfig().items.find((x) => x.name === 'HandWritten').id, a.id);
});

test('並べ替え: items/swap は同じ先頭タグどうしのみ、tags/swap は別の魔法陣なら circle も交換', async () => {
  let st = await state();
  const tool = st.items.filter((x) => x.tags[0] === 'ツール');
  assert.ok(tool.length >= 2);
  const before = readConfig().items.map((x) => x.id);
  assert.strictEqual((await postJson('/api/items/swap', { a: tool[0].id, b: tool[1].id })).status, 200);
  const after = readConfig().items.map((x) => x.id);
  assert.strictEqual(after.indexOf(tool[0].id), before.indexOf(tool[1].id));
  const other = st.items.find((x) => x.tags[0] === 'リポジトリ');
  assert.strictEqual((await postJson('/api/items/swap', { a: tool[0].id, b: other.id })).status, 400);

  // タグを別の魔法陣へ
  assert.strictEqual((await postJson('/api/tag', { name: 'ツール', circle: '製品' })).status, 200);
  let cfg = readConfig();
  assert.strictEqual(cfg.tags['ツール'].circle, '製品');
  assert.deepStrictEqual(cfg.circles, ['Launcher', '製品']); // 並びが空なら、いまの最初の魔法陣を先頭に固定してから足す
  const keys = Object.keys(cfg.tags);
  assert.strictEqual((await postJson('/api/tags/swap', { a: 'リポジトリ', b: 'ツール' })).status, 200);
  cfg = readConfig();
  const keys2 = Object.keys(cfg.tags);
  assert.strictEqual(keys2.indexOf('ツール'), keys.indexOf('リポジトリ'));
  assert.strictEqual(keys2.indexOf('リポジトリ'), keys.indexOf('ツール'));
  assert.strictEqual(cfg.tags['リポジトリ'].circle, '製品');
  assert.strictEqual(cfg.tags['ツール'].circle, undefined);

  assert.strictEqual((await postJson('/api/tag', { name: 'ツール', color: 'red' })).status, 400);
  assert.strictEqual((await postJson('/api/tag', { name: 'ツール', color: '#AABBCC' })).status, 200);
  assert.strictEqual(readConfig().tags['ツール'].color, '#aabbcc');

  assert.strictEqual((await postJson('/api/circles', { order: ['製品', 'Launcher'] })).status, 200);
  cfg = readConfig();
  assert.deepStrictEqual(cfg.circles, ['製品', 'Launcher']);
  // 先頭が入れ替わっても、置き場所が空欄だったタグは元の魔法陣に残る(新しい先頭へ一斉に移らない)
  assert.strictEqual(cfg.tags['ツール'].circle, 'Launcher');
  assert.strictEqual(cfg.tags['リポジトリ'].circle, '製品');
  assert.strictEqual((await postJson('/api/circle', { name: '製品', color: '#19b6d2' })).status, 200);
  st = await state();
  assert.deepStrictEqual(st.circleColors, { 製品: '#19b6d2' });
  assert.strictEqual((await postJson('/api/circle', { name: '製品', color: '' })).status, 200);
  assert.deepStrictEqual((await state()).circleColors, {});
});

test('名前の変更: タグは並び・色・項目のタグごと、魔法陣は並び・色・置いたタグごと変わる', async () => {
  let cfg = readConfig();
  const keys = Object.keys(cfg.tags);
  const color = cfg.tags['ツール'].color;
  const tagged = cfg.items.filter((x) => (x.tags || []).includes('ツール')).map((x) => x.id);
  assert.ok(tagged.length >= 1);
  assert.strictEqual((await postJson('/api/tag/rename', { from: 'ツール', to: '道具' })).status, 200);
  cfg = readConfig();
  assert.strictEqual(cfg.tags['ツール'], undefined);
  assert.strictEqual(Object.keys(cfg.tags).indexOf('道具'), keys.indexOf('ツール')); // 並びはそのまま
  assert.strictEqual(cfg.tags['道具'].color, color);
  for (const id of tagged) {
    const it = cfg.items.find((x) => x.id === id);
    assert.ok(it.tags.includes('道具') && !it.tags.includes('ツール'));
  }
  assert.strictEqual((await postJson('/api/tag/rename', { from: '道具', to: 'リポジトリ' })).status, 409); // 既にある名前
  assert.strictEqual((await postJson('/api/tag/rename', { from: '無いタグ', to: 'x' })).status, 404);
  assert.strictEqual((await postJson('/api/tag/rename', { from: '道具', to: ' ' })).status, 400);
  assert.strictEqual((await postJson('/api/tag/rename', { from: '道具', to: 'ツール' })).status, 200);

  // 魔法陣: いまは circles = ['製品', 'Launcher']、リポジトリ → 製品、ツール → Launcher
  assert.strictEqual((await postJson('/api/circle', { name: '製品', color: '#19b6d2' })).status, 200);
  assert.strictEqual((await postJson('/api/circle/rename', { from: '製品', to: '作品' })).status, 200);
  cfg = readConfig();
  assert.deepStrictEqual(cfg.circles, ['作品', 'Launcher']);
  assert.strictEqual(cfg.tags['リポジトリ'].circle, '作品');
  assert.deepStrictEqual(cfg.circleColors, { 作品: '#19b6d2' });
  assert.strictEqual((await postJson('/api/circle/rename', { from: '作品', to: 'Launcher' })).status, 409);
  assert.strictEqual((await postJson('/api/circle/rename', { from: '無い魔法陣', to: 'x' })).status, 404);
  assert.strictEqual((await postJson('/api/circle/rename', { from: '作品', to: '製品' })).status, 200);
  assert.strictEqual((await postJson('/api/circle', { name: '製品', color: '' })).status, 200);
});

test('名前の変更: circles が空でも、title の名前の最初の魔法陣を変えられる', async () => {
  const saved = fs.readFileSync(config, 'utf8');
  fs.writeFileSync(config, JSON.stringify({ version: 1, title: 'Launcher', items: [{ id: 'i1', path: 'C:\\x', tags: ['T'] }], tags: { T: {} } }));
  try {
    assert.strictEqual((await postJson('/api/circle/rename', { from: 'Launcher', to: 'Essentials 陣' })).status, 200);
    const cfg = readConfig();
    assert.deepStrictEqual(cfg.circles, ['Essentials 陣']);
    assert.strictEqual(cfg.title, 'Launcher'); // 画面左上の題名は変えない
    const st = await state();
    assert.deepStrictEqual(st.circles, ['Essentials 陣']);
  } finally {
    fs.writeFileSync(config, saved);
  }
});

test('layout: columns 0〜12・idle の既定 orbit はファイルに書かない', async () => {
  assert.strictEqual((await postJson('/api/layout', { columns: 3, idle: 'float' })).status, 200);
  let cfg = readConfig();
  assert.strictEqual(cfg.columns, 3);
  assert.strictEqual(cfg.idle, 'float');
  assert.strictEqual((await postJson('/api/layout', { idle: 'orbit' })).status, 200);
  cfg = readConfig();
  assert.ok(!('idle' in cfg));
  assert.strictEqual((await postJson('/api/layout', { columns: 13 })).status, 400);
  assert.strictEqual((await postJson('/api/layout', { columns: 1.5 })).status, 400);
  assert.strictEqual((await postJson('/api/layout', { idle: 'spin' })).status, 400);
});

test('版数 = mtime-size。別プロセスの書き換えで変わる', async () => {
  const v1 = await (await fetch(`${BASE}/api/version`)).json();
  assert.match(v1.version, /^\d+-\d+$/);
  assert.strictEqual(v1.configPath, config);
  const cfg = readConfig();
  cfg.title = 'Changed by other PC';
  fs.writeFileSync(config, JSON.stringify(cfg, null, 2));
  const v2 = await (await fetch(`${BASE}/api/version`)).json();
  assert.notStrictEqual(v1.version, v2.version);
  assert.strictEqual((await state()).title, 'Changed by other PC');
});

test('壊れた JSON は警告を出し、書き込みを 409 で拒否して上書きしない', async () => {
  const good = fs.readFileSync(config, 'utf8');
  for (const broken of ['{ "items": [ ', '[1,2]', '{"items": {}}']) {
    fs.writeFileSync(config, broken);
    const st = await state();
    assert.ok(st.error, broken);
    assert.deepStrictEqual(st.items, []);
    const r = await postJson('/api/item', { item: { name: 'x', path: 'C:\\x' } });
    assert.strictEqual(r.status, 409);
    assert.strictEqual(fs.readFileSync(config, 'utf8'), broken); // 空の内容で上書きしない
    assert.strictEqual((await postJson('/api/layout', { columns: 1 })).status, 409);
  }
  fs.writeFileSync(config, good);
  assert.strictEqual((await state()).error, null);
});

test('設定ファイルの場所: フォルダは拒否・無いフォルダも拒否・空で既定へ', async () => {
  let r = await postJson('/api/config', { path: work });
  assert.strictEqual(r.status, 400);
  r = await postJson('/api/config', { path: path.join(work, 'nope', 'x.json') });
  assert.strictEqual(r.status, 400);
  r = await postJson('/api/config', { path: '"%ML_TEST_WORK%\\other.json"' });
  assert.strictEqual(r.status, 200);
  let st = await state();
  assert.strictEqual(st.configPath, path.join(work, 'other.json'));
  assert.strictEqual(st.exists, false);
  r = await postJson('/api/config', { path: '' });
  assert.strictEqual(r.status, 200);
  st = await state();
  assert.strictEqual(st.isDefaultPath, true);
  assert.strictEqual(st.configPath, st.defaultPath);
  // 元へ戻す
  await postJson('/api/config', { path: '%ML_TEST_WORK%\\launcher.json' });
  assert.strictEqual((await state()).configPath, config);
  // フォルダを指していたら画面に警告
  fs.writeFileSync(path.join(dataDir, 'settings.json'), JSON.stringify({ configPath: '%ML_TEST_WORK%' }));
  st = await state();
  assert.match(st.error, /フォルダ/);
  fs.writeFileSync(path.join(dataDir, 'settings.json'), JSON.stringify({ configPath: '%ML_TEST_WORK%\\launcher.json' }));
});

test('安全: Host 違い・よその Origin・X-Launcher 無しを拒否する', async () => {
  assert.strictEqual((await raw('GET', '/api/state', { Host: 'evil.example:8791' })).status, 403);
  assert.strictEqual((await raw('GET', '/api/state', { Host: `localhost:${PORT}` })).status, 200);
  assert.strictEqual((await raw('GET', '/', { Host: `127.0.0.1:${PORT}` })).status, 200);
  const st = await state();
  const id = st.items[0].id;
  let r = await post('/api/open', { id }, { 'X-Launcher': '' });
  assert.strictEqual(r.status, 403);
  r = await post('/api/open', { id }, { Origin: 'http://evil.example' });
  assert.strictEqual(r.status, 403);
  r = await post('/api/open', { id }, { Origin: `http://localhost:${PORT}` });
  assert.strictEqual(r.status, 200);
  assert.strictEqual((await raw('OPTIONS', '/api/open', { Host: `127.0.0.1:${PORT}`, Origin: 'http://evil.example' })).status, 403);
  // フォームから送れる形(text/plain)でも X-Launcher が無ければ拒否
  assert.strictEqual((await raw('POST', '/api/item/delete', { Host: `127.0.0.1:${PORT}`, 'Content-Type': 'text/plain' }, JSON.stringify({ id }))).status, 403);
});

test('安全: 静的ファイルは public/ の外へ出られない・本文は 1MB まで', async () => {
  for (const p of ['/../server.js', '/%2e%2e/server.js', '/..%5cserver.js', '/vendor/..%2f..%2fserver.js', '/%2e%2e%2f%2e%2e%2fWindows/win.ini']) {
    const r = await raw('GET', p, { Host: `127.0.0.1:${PORT}` });
    assert.ok(r.status === 404 || r.status === 403 || r.status === 400, `${p} -> ${r.status}`);
    assert.ok(!r.body.includes('MagicLauncher サーバ'), p);
  }
  assert.strictEqual((await raw('GET', '/vendor/three.min.js', { Host: `127.0.0.1:${PORT}` })).status, 200);
  const big = JSON.stringify({ item: { name: 'x'.repeat(1024 * 1024 + 10), path: 'C:\\x' } });
  const r = await raw('POST', '/api/item', { Host: `127.0.0.1:${PORT}`, 'X-Launcher': '1', 'Content-Type': 'application/json' }, big);
  assert.strictEqual(r.status, 413);
});

test('二重起動: ポート使用中なら「すでに動いているかも」と出して終わる', async () => {
  const c2 = spawn(process.execPath, [path.join(__dirname, '..', 'server.js')], {
    env: { ...process.env, PORT: String(PORT), MAGIC_LAUNCHER_DATA: dataDir },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  let err = '';
  c2.stderr.on('data', (b) => { err += b; });
  const code = await new Promise((r) => c2.on('exit', r));
  assert.strictEqual(code, 1);
  assert.match(err, /すでに MagicLauncher が動いているかも/);
});
