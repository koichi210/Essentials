// MagicLauncher サーバ。127.0.0.1 だけで待ち受け、public/ の画面を配り、
// 設定ファイル(launcher.json)の読み書きと「登録済みの id を開く」を受け持つ。
// npm パッケージは使わない(http / fs / child_process などの標準モジュールのみ)。
'use strict';

const http = require('http');
const fs = require('fs');
const path = require('path');
const os = require('os');
const crypto = require('crypto');
const { spawn, execFile } = require('child_process');

const PORT = Number(process.env.PORT) || 8790;
const HOST = '127.0.0.1';
const LOCAL_APPDATA = process.env.LOCALAPPDATA || path.join(os.homedir(), 'AppData', 'Local');
const DATA_DIR = process.env.MAGIC_LAUNCHER_DATA || path.join(LOCAL_APPDATA, 'MagicLauncher');
const DRY_RUN = process.env.MAGIC_LAUNCHER_DRY_RUN === '1';
const PUBLIC_DIR = path.join(__dirname, 'public');
const DEFAULT_CONFIG = path.join(__dirname, 'launcher.json');
const SETTINGS_FILE = path.join(DATA_DIR, 'settings.json');
const OPENER_SRC = path.join(__dirname, 'opener.cs');
const OPENER_EXE = path.join(DATA_DIR, 'opener.exe');

const MAX_BODY = 1024 * 1024;
const KIND_TIMEOUT_MS = 1500; // 画面用の種類判定(ネットワークパスで固まらないよう打ち切り)
const KIND_CACHE_MS = 30 * 1000;
const OPEN_STAT_TIMEOUT_MS = 5000; // 開くときの存在確認
const OPENER_OK_TIMEOUT_MS = 15 * 1000;

const ALLOWED_HOSTS = new Set([`127.0.0.1:${PORT}`, `localhost:${PORT}`]);
const ALLOWED_ORIGINS = new Set([`http://127.0.0.1:${PORT}`, `http://localhost:${PORT}`]);

// 新しいタグに順に割り当てる色(青→水色→藍→青緑→紫→金→珊瑚→…の 12 色)
const PALETTE = [
  '#3f7fe0', '#5fd0ff', '#5b5ff0', '#1fc7a0', '#b06cf0', '#e0b43a',
  '#ff7a6a', '#ff5fd2', '#9be15d', '#ff9f43', '#7b9cff', '#e05a8a',
];
const IDLE_MODES = ['orbit', 'float', 'off'];
const HEX_RE = /^#[0-9a-f]{6}$/i;

// ---------------------------------------------------------------- 小物

function log(message) {
  console.log(`${new Date().toISOString()} ${message}`);
}

class HttpError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

function str(v) {
  return typeof v === 'string' ? v : v == null ? '' : String(v);
}

// %VAR% を環境変数で展開する(無い変数はそのまま残す)
function expandEnv(s) {
  return str(s).replace(/%([^%\\/:*?"<>|\r\n]+)%/g, (m, name) => {
    const v = process.env[name];
    return v === undefined ? m : v;
  });
}

// 「英字 + :」で始まり、スキームが 2 文字以上なら URL(C: はドライブなのでパス)
function isUrl(s) {
  return /^[a-z][a-z0-9+.-]+:/i.test(str(s).trim());
}

// エクスプローラーの「パスのコピー」で付く前後の "..." を外す
function stripQuotes(s) {
  let t = str(s).trim();
  while (t.length >= 2 && t.startsWith('"') && t.endsWith('"')) {
    t = t.slice(1, -1).trim();
  }
  return t;
}

// id が無い項目に付ける「中身と位置から決まる id」(読むたびに同じ値になる)
function itemId(item, index) {
  const h = crypto.createHash('sha1').update(`${index}\n${JSON.stringify(item)}`).digest('hex');
  return `i${h.slice(0, 8)}`;
}

function newId(items) {
  const used = new Set(items.map((it) => it && it.id));
  for (;;) {
    const id = `i${crypto.randomBytes(4).toString('hex')}`;
    if (!used.has(id)) {
      return id;
    }
  }
}

function isPlainObject(v) {
  return v !== null && typeof v === 'object' && !Array.isArray(v);
}

function cleanTags(tags) {
  if (!Array.isArray(tags)) {
    return [];
  }
  const out = [];
  for (const t of tags) {
    const name = str(t).trim();
    if (name && !out.includes(name)) {
      out.push(name);
    }
  }
  return out;
}

function nextPaletteColor(usedColors) {
  const used = new Set([...usedColors].map((c) => str(c).toLowerCase()));
  const free = PALETTE.find((c) => !used.has(c));
  return free || PALETTE[used.size % PALETTE.length];
}

// ---------------------------------------------------------------- 設定ファイルの場所(PC ごと)

function readSettings() {
  try {
    const text = fs.readFileSync(SETTINGS_FILE, 'utf8').replace(/^\uFEFF/, '');
    const s = JSON.parse(text);
    return isPlainObject(s) ? s : {};
  } catch {
    return {};
  }
}

function writeSettings(s) {
  fs.mkdirSync(DATA_DIR, { recursive: true });
  fs.writeFileSync(SETTINGS_FILE, `${JSON.stringify(s, null, 2)}\n`);
}

function configLocation() {
  const raw = str(readSettings().configPath).trim();
  if (!raw) {
    return { raw: '', path: DEFAULT_CONFIG, isDefault: true };
  }
  return { raw, path: path.resolve(expandEnv(raw)), isDefault: false };
}

// ---------------------------------------------------------------- 設定ファイルの読み書き

function emptyData() {
  return { version: 1, title: 'Launcher', items: [] };
}

// 毎回ディスクから読み直す。壊れていれば error を付けて返し、書き込みは拒否する
function loadConfig() {
  const loc = configLocation();
  const base = { ...loc, exists: false, error: null, version: '0-0', data: emptyData() };
  let st;
  try {
    st = fs.statSync(loc.path);
  } catch (e) {
    if (e.code === 'ENOENT') {
      return base;
    }
    return { ...base, exists: false, error: `設定ファイルを確認できません(${e.code || e.message})`, data: null };
  }
  const version = `${Math.floor(st.mtimeMs)}-${st.size}`;
  if (st.isDirectory()) {
    return { ...base, exists: true, version, error: 'フォルダが指定されています(JSON ファイルを指定してください)', data: null };
  }
  let data;
  try {
    const text = fs.readFileSync(loc.path, 'utf8').replace(/^\uFEFF/, '');
    data = JSON.parse(text);
  } catch (e) {
    return { ...base, exists: true, version, error: `JSON として読めません(${e.message})`, data: null };
  }
  if (!isPlainObject(data)) {
    return { ...base, exists: true, version, error: '一番外が {} ではありません', data: null };
  }
  if (data.items !== undefined && !Array.isArray(data.items)) {
    return { ...base, exists: true, version, error: 'items が配列ではありません', data: null };
  }
  return { ...base, exists: true, version, data };
}

function validItem(it) {
  return isPlainObject(it) && typeof it.path === 'string' && it.path.trim() !== '';
}

// 各項目の id(書かれていなければ中身と位置から決める。重複していたら後ろは決めた値)
function itemIds(items) {
  const seen = new Set();
  return items.map((it, i) => {
    let id = isPlainObject(it) && typeof it.id === 'string' && it.id ? it.id : '';
    if (!id || seen.has(id)) {
      id = itemId(it, i);
    }
    seen.add(id);
    return id;
  });
}

// tags を「使われているタグ・色の無いタグにも色が付いた」形にする(data を書き換える)
function ensureTags(data) {
  if (!isPlainObject(data.tags)) {
    data.tags = {};
  }
  const items = Array.isArray(data.items) ? data.items : [];
  for (const it of items) {
    if (!validItem(it)) {
      continue;
    }
    for (const t of cleanTags(it.tags)) {
      if (!isPlainObject(data.tags[t])) {
        data.tags[t] = {};
      }
    }
  }
  for (const [name, t] of Object.entries(data.tags)) {
    if (!isPlainObject(t)) {
      data.tags[name] = {};
    }
  }
  for (const t of Object.values(data.tags)) {
    if (!HEX_RE.test(str(t.color))) {
      const used = Object.values(data.tags).map((x) => x.color).filter((c) => HEX_RE.test(str(c)));
      t.color = nextPaletteColor(used);
    }
  }
}

function writeConfig(file, data, existed) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  if (existed) {
    fs.copyFileSync(file, `${file}.bak`); // 直前の内容を 1 世代残す
  }
  fs.writeFileSync(file, `${JSON.stringify(data, null, 2)}\n`);
}

// 書き込みは必ずディスクの内容を読み直し、差分だけ当てて保存する(画面の古い一覧で上書きしない)
function mutate(fn) {
  const cfg = loadConfig();
  if (cfg.error) {
    throw new HttpError(409, `設定ファイルを読めないため保存しません: ${cfg.error}`);
  }
  const data = cfg.data;
  if (!Array.isArray(data.items)) {
    data.items = [];
  }
  // id の無い項目には、読んだときと同じ id を書き込んでおく(以降は位置が変わっても同じ id)
  const ids = itemIds(data.items);
  data.items.forEach((it, i) => {
    if (isPlainObject(it) && it.id !== ids[i]) {
      const { id: _old, ...rest } = it; // eslint-disable-line no-unused-vars
      data.items[i] = { id: ids[i], ...rest };
    }
  });
  ensureTags(data);
  const result = fn(data);
  ensureTags(data);
  writeConfig(cfg.path, data, cfg.exists);
  return result;
}

function firstCircleName(data) {
  const circles = Array.isArray(data.circles) ? data.circles.map((c) => str(c).trim()).filter(Boolean) : [];
  return circles[0] || str(data.title).trim() || 'Launcher';
}

// ---------------------------------------------------------------- 種類の判定

const kindCache = new Map(); // パス → { kind, at }
const kindPending = new Map(); // パス → Promise<kind>

function statOnce(p) {
  let pr = kindPending.get(p);
  if (!pr) {
    pr = fs.promises.stat(p).then(
      (s) => (s.isDirectory() ? 'folder' : 'file'),
      (e) => (['ENOENT', 'ENOTDIR', 'EINVAL', 'ENAMETOOLONG'].includes(e.code) ? 'missing' : 'unknown'),
    ).then((kind) => {
      kindCache.set(p, { kind, at: Date.now() });
      kindPending.delete(p);
      return kind;
    });
    kindPending.set(p, pr);
  }
  return pr;
}

// パスを stat して folder / file / missing、timeout を過ぎたら unknown
function statKind(p, timeoutMs, useCache) {
  if (useCache) {
    const c = kindCache.get(p);
    if (c && Date.now() - c.at < KIND_CACHE_MS) {
      return Promise.resolve(c.kind);
    }
  }
  let timer;
  const timeout = new Promise((resolve) => {
    timer = setTimeout(() => resolve('unknown'), timeoutMs);
  });
  return Promise.race([statOnce(p), timeout]).finally(() => clearTimeout(timer));
}

function kindOf(rawPath) {
  const p = stripQuotes(rawPath);
  if (isUrl(p)) {
    return Promise.resolve(/^https?:/i.test(p) ? 'web' : 'link');
  }
  const full = expandEnv(p);
  if (!path.isAbsolute(full)) {
    return Promise.resolve('missing');
  }
  return statKind(full, KIND_TIMEOUT_MS, true);
}

// ---------------------------------------------------------------- 画面に渡す形

function viewTags(data) {
  const copy = { tags: isPlainObject(data.tags) ? JSON.parse(JSON.stringify(data.tags)) : {}, items: data.items };
  ensureTags(copy); // 手で書き足したタグにも(ファイルには書かずに)色を付けて見せる
  const out = {};
  for (const [name, t] of Object.entries(copy.tags)) {
    out[name] = { color: t.color.toLowerCase(), circle: str(t.circle).trim() };
  }
  return out;
}

async function buildState() {
  const cfg = loadConfig();
  const data = cfg.data || emptyData();
  const rawItems = Array.isArray(data.items) ? data.items : [];
  const ids = itemIds(rawItems);
  const list = [];
  rawItems.forEach((it, i) => {
    if (validItem(it)) {
      list.push({ it, id: ids[i] });
    }
  });
  const kinds = await Promise.all(list.map(({ it }) => kindOf(it.path)));
  const items = list.map(({ it, id }, i) => ({
    id,
    name: str(it.name),
    path: stripQuotes(it.path),
    tags: cleanTags(it.tags),
    emoji: str(it.emoji),
    note: str(it.note),
    kind: kinds[i],
  }));
  const circleColors = {};
  if (isPlainObject(data.circleColors)) {
    for (const [k, v] of Object.entries(data.circleColors)) {
      if (HEX_RE.test(str(v))) {
        circleColors[k] = v.toLowerCase();
      }
    }
  }
  const columns = Number.isInteger(data.columns) && data.columns >= 0 && data.columns <= 12 ? data.columns : 0;
  return {
    configPath: cfg.path,
    configPathRaw: cfg.raw,
    isDefaultPath: cfg.isDefault,
    defaultPath: DEFAULT_CONFIG,
    exists: cfg.exists,
    error: cfg.error,
    version: cfg.version,
    title: str(data.title).trim() || 'Launcher',
    tags: viewTags({ ...data, items: rawItems }),
    circleColors,
    idle: IDLE_MODES.includes(data.idle) ? data.idle : 'orbit',
    columns,
    circles: Array.isArray(data.circles) ? cleanTags(data.circles) : [],
    items,
    dryRun: DRY_RUN,
  };
}

// ---------------------------------------------------------------- 開く係(opener.exe)

let openerReady = null; // Promise<{ ok, reason }>

function cscPath() {
  const windir = process.env.WINDIR || process.env.SystemRoot || 'C:\\Windows';
  return path.join(windir, 'Microsoft.NET', 'Framework64', 'v4.0.30319', 'csc.exe');
}

// opener.cs の方が新しいときだけビルドし直す
function buildOpener() {
  return new Promise((resolve) => {
    if (process.platform !== 'win32') {
      resolve({ ok: false, reason: 'Windows ではないため' });
      return;
    }
    let src;
    try {
      src = fs.statSync(OPENER_SRC);
    } catch {
      resolve({ ok: false, reason: 'opener.cs がありません' });
      return;
    }
    try {
      const exe = fs.statSync(OPENER_EXE);
      if (exe.mtimeMs >= src.mtimeMs) {
        resolve({ ok: true, reason: '' });
        return;
      }
    } catch {
      // 未ビルド
    }
    const csc = cscPath();
    if (!fs.existsSync(csc)) {
      resolve({ ok: false, reason: `csc.exe が見つかりません(${csc})` });
      return;
    }
    fs.mkdirSync(DATA_DIR, { recursive: true });
    const args = ['/nologo', '/target:winexe', '/optimize', '/codepage:65001',
      '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', `/out:${OPENER_EXE}`, OPENER_SRC];
    execFile(csc, args, { windowsHide: true, timeout: 60 * 1000 }, (err, stdout) => {
      if (err) {
        resolve({ ok: false, reason: `opener.exe のビルドに失敗: ${str(stdout).trim() || err.message}` });
        return;
      }
      log(`opener.exe をビルドしました: ${OPENER_EXE}`);
      resolve({ ok: true, reason: '' });
    });
  }).then((r) => {
    if (!r.ok) {
      log(`開く係を使わず rundll32 で開きます(窓は後ろに出ます)。理由: ${r.reason}`);
    }
    return r;
  });
}

function openWithOpener(target) {
  return new Promise((resolve, reject) => {
    const child = spawn(OPENER_EXE, [target], { stdio: ['ignore', 'pipe', 'pipe'], windowsHide: false });
    let out = Buffer.alloc(0);
    let errText = '';
    let done = false;
    const finish = (fn, v) => {
      if (!done) {
        done = true;
        clearTimeout(timer);
        fn(v);
      }
    };
    const timer = setTimeout(() => finish(reject, new HttpError(504, '開く係から応答がありません')), OPENER_OK_TIMEOUT_MS);
    child.stdout.on('data', (b) => {
      out = Buffer.concat([out, b]);
      if (/^ok\b/m.test(out.toString('utf8'))) {
        finish(resolve); // ok が届いた時点で応答を返す(係は窓が手前に来るまで少し居残る)
      }
    });
    child.stderr.on('data', (b) => {
      errText += b.toString('utf8');
    });
    child.on('error', (e) => finish(reject, new HttpError(500, `開く係を起動できません: ${e.message}`)));
    child.on('exit', (code) => {
      if (code !== 0) {
        finish(reject, new HttpError(500, errText.trim() || `開けませんでした(終了コード ${code})`));
      } else {
        finish(resolve);
      }
    });
  });
}

function openWithRundll(target) {
  return new Promise((resolve, reject) => {
    const child = spawn('rundll32.exe', ['url.dll,FileProtocolHandler', target], { stdio: 'ignore', detached: true });
    child.on('error', (e) => reject(new HttpError(500, `開けませんでした: ${e.message}`)));
    child.on('spawn', () => {
      child.unref();
      resolve();
    });
  });
}

async function openTarget(target) {
  if (!openerReady) {
    openerReady = buildOpener();
  }
  const r = await openerReady;
  if (r.ok) {
    return openWithOpener(target);
  }
  return openWithRundll(target);
}

// ---------------------------------------------------------------- API

const api = {
  'GET /api/state': () => buildState(),

  'GET /api/version': () => {
    const cfg = loadConfig();
    return { version: cfg.version, configPath: cfg.path };
  },

  'POST /api/item': (body) => {
    const it = body.item;
    if (!isPlainObject(it)) {
      throw new HttpError(400, 'item がありません');
    }
    const p = stripQuotes(it.path);
    if (!p) {
      throw new HttpError(400, 'パスが空です');
    }
    const fields = {
      name: str(it.name).trim(),
      path: p,
      tags: cleanTags(it.tags),
      emoji: str(it.emoji).trim(),
      note: str(it.note),
    };
    const id = str(it.id).trim();
    return mutate((data) => {
      if (id) {
        const ids = itemIds(data.items);
        const idx = ids.indexOf(id);
        if (idx < 0) {
          throw new HttpError(404, 'この項目が見つかりません(ほかの PC で削除されたかも)');
        }
        Object.assign(data.items[idx], fields);
        return { id };
      }
      const nid = newId(data.items);
      data.items.push({ id: nid, ...fields });
      return { id: nid };
    });
  },

  'POST /api/item/delete': (body) => {
    const id = str(body.id).trim();
    return mutate((data) => {
      const idx = itemIds(data.items).indexOf(id);
      if (!id || idx < 0) {
        throw new HttpError(404, 'この項目が見つかりません(ほかの PC で削除されたかも)');
      }
      data.items.splice(idx, 1);
      return { ok: true };
    });
  },

  'POST /api/tag': (body) => {
    const name = str(body.name).trim();
    if (!name) {
      throw new HttpError(400, 'タグの名前がありません');
    }
    if (body.color !== undefined && !HEX_RE.test(str(body.color))) {
      throw new HttpError(400, '色は #rrggbb で指定してください');
    }
    return mutate((data) => {
      if (!isPlainObject(data.tags[name])) {
        data.tags[name] = {};
      }
      const t = data.tags[name];
      if (body.color !== undefined) {
        t.color = str(body.color).toLowerCase();
      }
      if (body.circle !== undefined) {
        const c = str(body.circle).trim();
        if (!c) {
          delete t.circle;
        } else {
          // 新しい魔法陣の名前なら並びの末尾へ(並びが空なら、いまの最初の魔法陣を先頭に固定してから)
          let circles = Array.isArray(data.circles) ? cleanTags(data.circles) : [];
          if (!circles.length) {
            circles = [firstCircleName(data)];
          }
          if (!circles.includes(c)) {
            circles.push(c);
          }
          data.circles = circles;
          t.circle = c;
        }
      }
      return { ok: true };
    });
  },

  'POST /api/tags/swap': (body) => {
    const a = str(body.a).trim();
    const b = str(body.b).trim();
    if (!a || !b || a === b) {
      throw new HttpError(400, '入れ替える 2 つのタグを指定してください');
    }
    return mutate((data) => {
      const keys = Object.keys(data.tags);
      const ia = keys.indexOf(a);
      const ib = keys.indexOf(b);
      if (ia < 0 || ib < 0) {
        throw new HttpError(404, 'タグが見つかりません');
      }
      const first = firstCircleName(data);
      const ta = data.tags[a];
      const tb = data.tags[b];
      const ca = str(ta.circle).trim() || first;
      const cb = str(tb.circle).trim() || first;
      if (ca !== cb) {
        // 別の魔法陣どうしなら置き場所も入れ替える
        const tmp = ta.circle;
        if (tb.circle === undefined) delete ta.circle; else ta.circle = tb.circle;
        if (tmp === undefined) delete tb.circle; else tb.circle = tmp;
      }
      [keys[ia], keys[ib]] = [keys[ib], keys[ia]];
      const next = {};
      for (const k of keys) {
        next[k] = data.tags[k];
      }
      data.tags = next;
      return { ok: true };
    });
  },

  // タグの名前を変える。並び・色・置く魔法陣はそのままで、付いている項目のタグも書き換える
  'POST /api/tag/rename': (body) => {
    const from = str(body.from).trim();
    const to = str(body.to).trim();
    if (!from || !to) {
      throw new HttpError(400, '変更前と変更後のタグの名前を指定してください');
    }
    return mutate((data) => {
      const used = data.items.some((it) => validItem(it) && cleanTags(it.tags).includes(from));
      if (!isPlainObject(data.tags[from]) && !used) {
        throw new HttpError(404, 'タグが見つかりません(ほかの PC で変更されたかも)');
      }
      if (from === to) {
        return { ok: true };
      }
      if (isPlainObject(data.tags[to])) {
        throw new HttpError(409, `「${to}」というタグはもうあります`);
      }
      const next = {};
      for (const [k, v] of Object.entries(data.tags)) {
        next[k === from ? to : k] = v;
      }
      data.tags = next;
      for (const it of data.items) {
        if (isPlainObject(it) && Array.isArray(it.tags)) {
          it.tags = cleanTags(it.tags.map((t) => (str(t).trim() === from ? to : t)));
        }
      }
      return { ok: true };
    });
  },

  'POST /api/items/swap': (body) => {
    const a = str(body.a).trim();
    const b = str(body.b).trim();
    if (!a || !b || a === b) {
      throw new HttpError(400, '入れ替える 2 つの項目を指定してください');
    }
    return mutate((data) => {
      const ids = itemIds(data.items);
      const ia = ids.indexOf(a);
      const ib = ids.indexOf(b);
      if (ia < 0 || ib < 0) {
        throw new HttpError(404, '項目が見つかりません(ほかの PC で削除されたかも)');
      }
      const fa = cleanTags(data.items[ia].tags)[0] || '';
      const fb = cleanTags(data.items[ib].tags)[0] || '';
      if (fa !== fb) {
        throw new HttpError(400, '先頭のタグが同じ項目どうしだけ入れ替えられます');
      }
      [data.items[ia], data.items[ib]] = [data.items[ib], data.items[ia]];
      return { ok: true };
    });
  },

  'POST /api/circles': (body) => {
    if (!Array.isArray(body.order)) {
      throw new HttpError(400, 'order は名前の配列で指定してください');
    }
    const order = cleanTags(body.order);
    return mutate((data) => {
      // 置き場所が空欄のタグは「最初の魔法陣」に入る。先頭が入れ替わるときは、いまの先頭の名前に固定してから並べ替える
      // (しないと空欄のタグが新しい先頭へ一斉に移ってしまう)
      const oldFirst = firstCircleName(data);
      if (order.length && order[0] !== oldFirst) {
        for (const t of Object.values(data.tags)) {
          if (!str(t.circle).trim()) {
            t.circle = oldFirst;
          }
        }
        if (!order.includes(oldFirst)) {
          order.push(oldFirst);
        }
      }
      data.circles = order;
      return { ok: true };
    });
  },

  'POST /api/circle': (body) => {
    const name = str(body.name).trim();
    const color = str(body.color).trim();
    if (!name) {
      throw new HttpError(400, '魔法陣の名前がありません');
    }
    if (color && !HEX_RE.test(color)) {
      throw new HttpError(400, '色は #rrggbb で指定してください');
    }
    return mutate((data) => {
      if (!isPlainObject(data.circleColors)) {
        data.circleColors = {};
      }
      if (color) {
        data.circleColors[name] = color.toLowerCase();
      } else {
        delete data.circleColors[name]; // 空なら自動の色に戻す
      }
      return { ok: true };
    });
  },

  // 魔法陣の名前を変える。並び・色・そこに置いたタグはそのまま付いていく
  'POST /api/circle/rename': (body) => {
    const from = str(body.from).trim();
    const to = str(body.to).trim();
    if (!from || !to) {
      throw new HttpError(400, '変更前と変更後の魔法陣の名前を指定してください');
    }
    return mutate((data) => {
      const first = firstCircleName(data);
      let circles = Array.isArray(data.circles) ? cleanTags(data.circles) : [];
      const tagCircles = Object.values(data.tags).map((t) => str(t.circle).trim());
      if (from !== first && !circles.includes(from) && !tagCircles.includes(from)) {
        throw new HttpError(404, '魔法陣が見つかりません(ほかの PC で変更されたかも)');
      }
      if (from === to) {
        return { ok: true };
      }
      if (to === first || circles.includes(to) || tagCircles.includes(to)) {
        throw new HttpError(409, `「${to}」という魔法陣はもうあります`);
      }
      if (!circles.length) {
        circles = [first]; // 並びが空なら、いまの最初の魔法陣(title の名前)を先頭に固定してから変える
      }
      data.circles = circles.map((c) => (c === from ? to : c));
      for (const t of Object.values(data.tags)) {
        if (str(t.circle).trim() === from) {
          t.circle = to;
        }
      }
      if (isPlainObject(data.circleColors) && data.circleColors[from] !== undefined) {
        const next = {};
        for (const [k, v] of Object.entries(data.circleColors)) {
          next[k === from ? to : k] = v;
        }
        data.circleColors = next;
      }
      return { ok: true };
    });
  },

  'POST /api/layout': (body) => {
    if (body.columns !== undefined && !(Number.isInteger(body.columns) && body.columns >= 0 && body.columns <= 12)) {
      throw new HttpError(400, 'columns は 0〜12 の整数です');
    }
    if (body.idle !== undefined && !IDLE_MODES.includes(body.idle)) {
      throw new HttpError(400, 'idle は orbit / float / off のどれかです');
    }
    return mutate((data) => {
      if (body.columns !== undefined) {
        data.columns = body.columns;
      }
      if (body.idle !== undefined) {
        if (body.idle === 'orbit') {
          delete data.idle; // 既定の orbit はファイルに書かない
        } else {
          data.idle = body.idle;
        }
      }
      return { ok: true };
    });
  },

  'POST /api/config': (body) => {
    const raw = stripQuotes(body.path);
    const settings = readSettings();
    if (!raw) {
      delete settings.configPath;
      writeSettings(settings);
      log('設定ファイルの場所を既定に戻しました');
      return { ok: true };
    }
    const full = expandEnv(raw);
    if (!path.isAbsolute(full)) {
      throw new HttpError(400, 'フルパスで指定してください(%USERPROFILE% などは使えます)');
    }
    let st = null;
    try {
      st = fs.statSync(full);
    } catch (e) {
      if (e.code !== 'ENOENT') {
        throw new HttpError(400, `その場所を確認できません(${e.code || e.message})`);
      }
    }
    if (st && st.isDirectory()) {
      throw new HttpError(400, 'フォルダは指定できません(launcher.json などのファイル名まで入れてください)');
    }
    if (!st && !fs.existsSync(path.dirname(full))) {
      throw new HttpError(400, 'そのフォルダがありません');
    }
    settings.configPath = raw;
    writeSettings(settings);
    log('設定ファイルの場所を変更しました');
    return { ok: true };
  },

  'POST /api/open': async (body) => {
    const id = str(body.id).trim();
    const cfg = loadConfig();
    if (cfg.error) {
      throw new HttpError(409, `設定ファイルを読めません: ${cfg.error}`);
    }
    const items = Array.isArray(cfg.data.items) ? cfg.data.items : [];
    const idx = itemIds(items).indexOf(id);
    if (!id || idx < 0 || !validItem(items[idx])) {
      throw new HttpError(404, '登録されていない項目です');
    }
    let target = expandEnv(stripQuotes(items[idx].path));
    if (!isUrl(target)) {
      if (!path.isAbsolute(target)) {
        throw new HttpError(404, `見つかりません: ${target}`);
      }
      target = path.normalize(target);
      const kind = await statKind(target, OPEN_STAT_TIMEOUT_MS, false);
      if (kind === 'missing') {
        throw new HttpError(404, `見つかりません: ${target}`);
      }
      if (kind === 'unknown') {
        throw new HttpError(504, `応答がありません: ${target}`);
      }
    }
    if (DRY_RUN) {
      log(`[dry-run] open ${id}`);
      return { opened: target, dryRun: true };
    }
    await openTarget(target);
    log(`open ${id}`);
    return { opened: target, dryRun: false };
  },
};

// ---------------------------------------------------------------- HTTP

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
  '.txt': 'text/plain; charset=utf-8',
};

function sendJson(res, status, obj, head) {
  const body = JSON.stringify(obj);
  res.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff',
  });
  res.end(head ? undefined : body);
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    let over = false;
    req.on('data', (c) => {
      if (over) {
        return;
      }
      size += c.length;
      if (size > MAX_BODY) {
        over = true;
        reject(new HttpError(413, '本文が大きすぎます(1MB まで)'));
        return;
      }
      chunks.push(c);
    });
    req.on('end', () => {
      if (over) {
        return;
      }
      const text = Buffer.concat(chunks).toString('utf8');
      if (!text.trim()) {
        resolve({});
        return;
      }
      try {
        const v = JSON.parse(text);
        resolve(isPlainObject(v) ? v : {});
      } catch {
        reject(new HttpError(400, 'JSON として読めません'));
      }
    });
    req.on('error', reject);
  });
}

function serveStatic(req, res, pathname) {
  let rel;
  try {
    rel = decodeURIComponent(pathname);
  } catch {
    return sendJson(res, 400, { error: 'パスが不正です' });
  }
  if (rel.includes('\0')) {
    return sendJson(res, 400, { error: 'パスが不正です' });
  }
  if (rel === '/' || rel === '') {
    rel = '/index.html';
  }
  // public/ の外へ出られないよう正規化して確かめる
  const file = path.resolve(PUBLIC_DIR, `.${path.posix.normalize(rel.replace(/\\/g, '/'))}`);
  if (file !== PUBLIC_DIR && !file.startsWith(PUBLIC_DIR + path.sep)) {
    return sendJson(res, 403, { error: '見られない場所です' });
  }
  fs.stat(file, (err, st) => {
    if (err || !st.isFile()) {
      sendJson(res, 404, { error: 'ありません' });
      return;
    }
    res.writeHead(200, {
      'Content-Type': MIME[path.extname(file).toLowerCase()] || 'application/octet-stream',
      'Content-Length': st.size,
      'Cache-Control': 'no-cache',
      'X-Content-Type-Options': 'nosniff',
      'X-Frame-Options': 'DENY',
      'Referrer-Policy': 'no-referrer',
    });
    if (req.method === 'HEAD') {
      res.end();
      return;
    }
    fs.createReadStream(file).pipe(res);
  });
  return undefined;
}

async function handle(req, res) {
  const host = str(req.headers.host).toLowerCase();
  if (!ALLOWED_HOSTS.has(host)) {
    return sendJson(res, 403, { error: 'Host が違います' }); // DNS リバインディング対策
  }
  const method = req.method;
  if (method !== 'GET' && method !== 'HEAD') {
    const origin = req.headers.origin;
    if (origin !== undefined && !ALLOWED_ORIGINS.has(origin)) {
      return sendJson(res, 403, { error: 'よそのページからは呼べません' });
    }
    if (req.headers['x-launcher'] !== '1') {
      return sendJson(res, 403, { error: 'X-Launcher ヘッダがありません' });
    }
  }
  const url = new URL(req.url, `http://${host}`);
  if (url.pathname.startsWith('/api/')) {
    const fn = api[`${method === 'HEAD' ? 'GET' : method} ${url.pathname}`];
    if (!fn) {
      return sendJson(res, 404, { error: 'その API はありません' });
    }
    const body = method === 'POST' ? await readBody(req) : {};
    const result = await fn(body);
    return sendJson(res, 200, result, method === 'HEAD');
  }
  if (method !== 'GET' && method !== 'HEAD') {
    return sendJson(res, 405, { error: '使えないメソッドです' });
  }
  return serveStatic(req, res, url.pathname);
}

function createServer() {
  return http.createServer((req, res) => {
    handle(req, res).catch((e) => {
      const status = e instanceof HttpError ? e.status : 500;
      if (status === 500) {
        log(`error ${req.method} ${req.url}: ${e.stack || e.message}`);
      }
      if (!res.headersSent) {
        sendJson(res, status, { error: e.message });
      }
      if (status === 413) {
        req.destroy();
      }
    });
  });
}

function start() {
  const server = createServer();
  server.on('error', (e) => {
    if (e.code === 'EADDRINUSE') {
      console.error(`${HOST}:${PORT} は使用中です。すでに MagicLauncher が動いているかも。`);
    } else {
      console.error(`起動できません: ${e.message}`);
    }
    process.exit(1);
  });
  server.listen(PORT, HOST, () => {
    log(`MagicLauncher http://${HOST}:${PORT}/  config=${configLocation().path}${DRY_RUN ? '  (dry-run)' : ''}`);
    openerReady = buildOpener();
  });
}

if (require.main === module) {
  start();
}

module.exports = { expandEnv, isUrl, stripQuotes, itemId, itemIds, cleanTags, nextPaletteColor, PALETTE, createServer };
