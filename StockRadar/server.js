// 株レーダーのサーバ。Yahoo Finance の公開データ(スクリーナー・日足)を取ってきて analyze.js で判定し、
// 画面(public/)へ JSON で渡す。npm パッケージは使わない。書くのはスナップショットのファイルだけ。
'use strict';

const http = require('http');
const fs = require('fs');
const os = require('os');
const path = require('path');
const a = require('./analyze.js');

const PORT = Number(process.env.PORT) || 8766;
const HOST = '127.0.0.1';
const PUBLIC_DIR = path.join(__dirname, 'public');
const ALLOWED_HOSTS = new Set([`127.0.0.1:${PORT}`, `localhost:${PORT}`]);
const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36';
const MIN_MARKET_CAP = 5e10;
const MAX_UNIVERSE = 1000;
const SHORT_CHARTS = 100;
const TOP = 20;
const CACHE_MS = 3 * 60 * 1000;
const SESSION_MS = 60 * 60 * 1000;
const EXTRAS_MS = 12 * 60 * 60 * 1000;
const HISTORY_MS = 10 * 60 * 1000;
const LONG_CHARTS = 150;
const DATA_DIR = process.env.STOCK_RADAR_DATA || path.join(process.env.LOCALAPPDATA || os.homedir(), 'StockRadar');
const SNAP_DIR = path.join(DATA_DIR, 'snapshots');
const SNAP_ID = /^\d{13}$/;
const MAX_BODY = 4 * 1024 * 1024;
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.svg': 'image/svg+xml', '.ico': 'image/x-icon' };

function log(message) {
  console.log(`${new Date().toISOString()} ${message}`);
}

// ---------------------------------------------------------------- Yahoo Finance

let session = null;

// スクリーナーには cookie と crumb が要る。1時間使い回し、401 が返ったら取り直す
async function yahooSession(force = false) {
  if (!force && session && Date.now() - session.at < SESSION_MS) {
    return session;
  }
  const first = await fetch('https://fc.yahoo.com/', { headers: { 'User-Agent': UA }, redirect: 'manual', signal: AbortSignal.timeout(15000) });
  const cookie = first.headers.getSetCookie().map((c) => c.split(';')[0]).join('; ');
  const res = await fetch('https://query2.finance.yahoo.com/v1/test/getcrumb', { headers: { 'User-Agent': UA, Cookie: cookie }, signal: AbortSignal.timeout(15000) });
  const crumb = (await res.text()).trim();
  if (!res.ok || !crumb || crumb.includes('<')) {
    throw new Error(`crumb を取れませんでした(${res.status})`);
  }
  session = { cookie, crumb, at: Date.now() };
  return session;
}

async function screenPage(minPrice, maxPrice, offset, retried = false) {
  const s = await yahooSession();
  const body = {
    size: 250, offset, sortField: 'intradaymarketcap', sortType: 'DESC', quoteType: 'EQUITY',
    query: { operator: 'AND', operands: [
      { operator: 'eq', operands: ['region', 'jp'] },
      { operator: 'eq', operands: ['exchange', 'JPX'] },
      { operator: 'btwn', operands: ['intradayprice', minPrice, maxPrice] },
      { operator: 'gt', operands: ['intradaymarketcap', MIN_MARKET_CAP] },
    ] },
  };
  const url = `https://query2.finance.yahoo.com/v1/finance/screener?crumb=${encodeURIComponent(s.crumb)}&lang=ja-JP&region=JP`;
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'User-Agent': UA, Cookie: s.cookie, 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
    signal: AbortSignal.timeout(20000),
  });
  if (res.status === 401 && !retried) {
    await yahooSession(true);
    return screenPage(minPrice, maxPrice, offset, true);
  }
  if (!res.ok) {
    throw new Error(`スクリーナーの取得に失敗しました(${res.status})`);
  }
  const r = (await res.json()).finance.result[0];
  return { total: r.total, quotes: r.quotes || [] };
}

async function screen(minPrice, maxPrice) {
  const all = [];
  for (let offset = 0; offset < MAX_UNIVERSE; offset += 250) {
    const page = await screenPage(minPrice, maxPrice, offset);
    all.push(...page.quotes);
    if (page.quotes.length < 250 || all.length >= page.total) {
      break;
    }
  }
  return all.filter((q) => q.quoteType === 'EQUITY' && q.regularMarketPrice > 0);
}

// 日足・週足は10分覚えておく(条件を変えて何度か調べても速いように)
const historyCache = new Map();

async function history(symbol, range, interval) {
  const key = `${symbol}|${range}|${interval}`;
  const hit = historyCache.get(key);
  if (hit && Date.now() - hit.at < HISTORY_MS) {
    return hit.bars;
  }
  const url = `https://query1.finance.yahoo.com/v8/finance/chart/${encodeURIComponent(symbol)}?range=${range}&interval=${interval}`;
  const res = await fetch(url, { headers: { 'User-Agent': UA }, signal: AbortSignal.timeout(15000) });
  if (!res.ok) {
    throw new Error(`${symbol} の値動きを取れませんでした(${res.status})`);
  }
  const bars = a.parseChart(await res.json());
  historyCache.set(key, { at: Date.now(), bars });
  return bars;
}

function chart(symbol) {
  return history(symbol, '6mo', '1d');
}

async function dividends(symbol, nowSec) {
  const url = `https://query1.finance.yahoo.com/v8/finance/chart/${encodeURIComponent(symbol)}?range=2y&interval=1mo&events=div`;
  const res = await fetch(url, { headers: { 'User-Agent': UA }, signal: AbortSignal.timeout(15000) });
  if (!res.ok) {
    throw new Error(`${symbol} の配当履歴を取れませんでした(${res.status})`);
  }
  return a.parseDividends(await res.json(), nowSec);
}

// 優待は Yahoo!ファイナンス(日本)の優待ページから取る(米国版の API には無いため)
async function incentive(code) {
  const res = await fetch(`https://finance.yahoo.co.jp/quote/${encodeURIComponent(code)}.T/incentive`, {
    headers: { 'User-Agent': UA, 'Accept-Language': 'ja' },
    signal: AbortSignal.timeout(15000),
  });
  if (!res.ok) {
    throw new Error(`${code} の優待ページを取れませんでした(${res.status})`);
  }
  return a.parseIncentive(await res.text());
}

// 配当・優待は頻繁には変わらないので半日覚えておく(画面に出す銘柄の分だけ取る)
const extrasCache = new Map();

async function extras(item, nowSec) {
  const hit = extrasCache.get(item.code);
  if (hit && Date.now() - hit.at < EXTRAS_MS) {
    return hit.value;
  }
  const [div, yutai] = await Promise.all([
    dividends(`${item.code}.T`, nowSec).catch(() => null),
    incentive(item.code).catch(() => null),
  ]);
  const value = { div, yutai };
  if (div && yutai) {
    extrasCache.set(item.code, { at: Date.now(), value });
  }
  return value;
}

async function attachExtras(items, mode, nowSec) {
  await mapLimit(items, 4, async (item) => {
    const { div, yutai } = await extras(item, nowSec);
    item.div = div;
    item.yutai = yutai;
    if (mode === 'short' && item.deadline) {
      item.rights = a.rightsDuring(item.priceDate, item.deadline, div ? div.months : [], yutai && !yutai.none ? yutai.lastDates : []);
    }
  });
}

// 同時に limit 本まで
async function mapLimit(items, limit, fn) {
  const out = new Array(items.length);
  let next = 0;
  async function worker() {
    while (next < items.length) {
      const i = next++;
      try {
        out[i] = await fn(items[i]);
      } catch (e) {
        out[i] = { error: e.message };
      }
    }
  }
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, worker));
  return out;
}

// ---------------------------------------------------------------- 調査

// 空なら null、lo〜hi の数ならその数、それ以外は NaN
function optionalNumber(params, name, lo, hi) {
  const raw = params.get(name);
  if (raw == null || raw === '') {
    return null;
  }
  const v = Number(raw);
  return Number.isFinite(v) && v >= lo && v <= hi ? v : NaN;
}

// クロスの条件。gc=5 なら5日以内、gc=soon なら「もうすぐ」だけ、gcSoon=1 で「もうすぐ」も含める。
// dc=not10 のように not を付けると、その条件に当たる銘柄を除く
function parseCross(params, name) {
  const raw = params.get(name);
  if (raw == null || raw === '') {
    return null;
  }
  if (raw === 'soon') {
    return { days: null, soon: true };
  }
  const not = raw.startsWith('not');
  const days = Number(not ? raw.slice(3) : raw);
  if (!Number.isInteger(days) || days < 1 || days > 20) {
    return NaN;
  }
  const cond = { days, soon: params.get(`${name}Soon`) === '1' };
  return not ? { ...cond, not } : cond;
}

function parseScan(params) {
  const mode = params.get('mode') === 'long' ? 'long' : 'short';
  const max = Number(params.get('max'));
  const min = Number(params.get('min') || 0);
  if (!Number.isFinite(max) || max < 10000 || max > 1e8) {
    return { error: '予算の上限は1万円〜1億円で指定してください。' };
  }
  if (!Number.isFinite(min) || min < 0 || min >= max) {
    return { error: '予算の下限は上限より小さくしてください。' };
  }
  const per = optionalNumber(params, 'per', 1, 1000);
  const pbr = optionalNumber(params, 'pbr', 0.1, 100);
  if (Number.isNaN(per) || Number.isNaN(pbr)) {
    return { error: 'PER・PBR の条件が正しくありません。' };
  }
  const cross = params.get('cross') || 'ma5_25';
  const gc = parseCross(params, 'gc');
  const dc = parseCross(params, 'dc');
  if (!Object.hasOwn(a.CROSS_TYPES, cross) || Number.isNaN(gc) || Number.isNaN(dc)) {
    return { error: 'クロスの条件が正しくありません。' };
  }
  return { mode, max, min, per, pbr, cross, gc, dc };
}

// 短期は日足を取る前に、売買代金と50日線で候補を絞る(全部取ると時間がかかる)。
// デッドクロスで探すときは下げている銘柄が対象なので、50日線では絞らない
function shortCandidates(quotes, nowSec, keepFalling = false) {
  return quotes
    .filter((q) => (q.averageDailyVolume3Month || 0) * q.regularMarketPrice >= a.MIN_TURNOVER)
    .filter((q) => keepFalling || q.regularMarketPrice >= (q.fiftyDayAverage || 0) * 0.95 || a.earningsInfo(q, nowSec).past)
    .sort((x, y) => y.averageDailyVolume3Month * y.regularMarketPrice - x.averageDailyVolume3Month * x.regularMarketPrice)
    .slice(0, SHORT_CHARTS);
}

function countExcluded(results) {
  const counts = {};
  for (const r of results) {
    if (r && r.excluded) {
      counts[r.excluded] = (counts[r.excluded] || 0) + 1;
    }
  }
  return counts;
}

const byScore = (x, y) => y.score - x.score || y.rr - x.rr;
const matchesCross = (r, req) => r.cross && a.matchCross(r.cross, 'gc', req.gc) && a.matchCross(r.cross, 'dc', req.dc);

// 日足を取ってクロスを調べる(取れなかった銘柄は cross なし)
async function attachCross(items, type) {
  await mapLimit(items, 6, async (r) => {
    const bars = await chart(`${r.code}.T`);
    r.cross = a.crossInfo(bars.map((b) => b.close), type);
  });
}

// クロスを押しポイント・気をつけるところに足す
function addCrossPoints(item, showDays) {
  if (!item.cross) {
    return;
  }
  for (const { good, ...p } of a.crossPoint(item.cross, showDays)) {
    const list = good ? item.reasons : item.warnings;
    if (!list.some((x) => x.label === p.label)) {
      list.push(p);
    }
  }
}

async function scan(req) {
  const { mode, max, min } = req;
  const nowSec = Math.floor(Date.now() / 1000);
  const [all, marketBars] = await Promise.all([
    screen(Math.max(1, min / a.LOT), max / a.LOT),
    chart('^N225').catch(() => []),
  ]);
  // PER・PBR はスクリーナーの値だけで絞れるので、日足を取る前に絞る
  const quotes = all.filter((q) => a.matchValuation(q, req));
  const crossFilter = Boolean(req.gc || req.dc);
  let results;
  let analyzed;
  if (mode === 'long') {
    results = quotes.map((q) => a.analyzeLong(q));
    analyzed = quotes.length;
  } else {
    const picks = shortCandidates(quotes, nowSec, Boolean(req.dc && !req.dc.not));
    results = await mapLimit(picks, 6, async (q) => {
      const bars = await chart(q.symbol);
      const r = a.analyzeShort(q, bars, nowSec);
      r.cross = a.crossInfo(bars.map((b) => b.close), req.cross);
      return r;
    });
    analyzed = picks.length;
  }
  let ranked = results.filter((r) => r && !r.excluded && !r.error).sort(byScore);
  if (mode === 'long' && crossFilter) {
    // 長期は日足を使わないので、点の高い順に LONG_CHARTS 銘柄だけ日足を取ってクロスを見る
    ranked = ranked.slice(0, LONG_CHARTS);
    await attachCross(ranked, req.cross);
  }
  if (crossFilter) {
    ranked = ranked.filter((r) => matchesCross(r, req));
  }
  const items = ranked.slice(0, TOP);
  if (mode === 'long' && !crossFilter) {
    // 表示用に、出す銘柄だけクロスを調べる
    await attachCross(items, req.cross);
  }
  // 絞り込みで10日より長く選んだときは、その日数までの交差を説明に出す
  const daysOf = (c) => (c && !c.not && c.days) || 0;
  const showDays = Math.max(10, daysOf(req.gc), daysOf(req.dc));
  items.forEach((item) => addCrossPoints(item, showDays));
  await attachExtras(items, mode, nowSec);
  const dataTimes = quotes.map((q) => q.regularMarketTime).filter(Number.isFinite);
  return {
    mode,
    budget: { min, max },
    at: new Date().toISOString(),
    dataTime: dataTimes.length > 0 ? new Date(Math.max(...dataTimes) * 1000).toISOString() : null,
    market: a.analyzeMarket(marketBars),
    universe: all.length,
    filtered: quotes.length,
    analyzed,
    failed: results.filter((r) => r && r.error).length,
    excluded: countExcluded(results),
    filter: {
      per: req.per,
      pbr: req.pbr,
      crossName: a.CROSS_TYPES[req.cross].name,
      gc: req.gc,
      dc: req.dc,
      longLimit: mode === 'long' && crossFilter ? LONG_CHARTS : null,
    },
    items,
  };
}

const cache = new Map();

async function cachedScan(req) {
  const key = JSON.stringify(req);
  const hit = cache.get(key);
  if (hit && Date.now() - hit.at < CACHE_MS) {
    return hit.value;
  }
  const value = await scan(req);
  cache.set(key, { at: Date.now(), value });
  return value;
}

// ---------------------------------------------------------------- スナップショット

// 調べた結果を JSON のまま保存しておき、あとで「その後どう動いたか」と比べる。ファイル名は保存した時刻(ミリ秒)
const snapFile = (id) => path.join(SNAP_DIR, `${id}.json`);

function snapSummary(snap) {
  const r = snap.result;
  return { id: snap.id, savedAt: snap.savedAt, mode: r.mode, at: r.at, budget: r.budget, filter: r.filter, count: r.items.length, names: r.items.slice(0, 3).map((i) => i.name) };
}

// 画面から送られてきたものが、調べた結果の形をしているか
function isScanResult(r) {
  return Boolean(r) && (r.mode === 'short' || r.mode === 'long') && typeof r.at === 'string'
    && Array.isArray(r.items) && r.items.length <= 100
    && r.items.every((i) => i && /^[0-9A-Z]{4}$/.test(i.code) && [i.price, i.target, i.stop].every(Number.isFinite));
}

async function listSnapshots() {
  let names;
  try {
    names = await fs.promises.readdir(SNAP_DIR);
  } catch {
    return [];
  }
  const out = [];
  for (const name of names.filter((n) => SNAP_ID.test(path.basename(n, '.json')) && n.endsWith('.json'))) {
    try {
      out.push(snapSummary(JSON.parse(await fs.promises.readFile(path.join(SNAP_DIR, name), 'utf8'))));
    } catch {
      // 壊れたファイルは飛ばす
    }
  }
  return out.sort((x, y) => y.id.localeCompare(x.id));
}

async function readSnapshot(id) {
  return JSON.parse(await fs.promises.readFile(snapFile(id), 'utf8'));
}

async function saveSnapshot(result) {
  await fs.promises.mkdir(SNAP_DIR, { recursive: true });
  const snap = { id: String(Date.now()), savedAt: new Date().toISOString(), result };
  await fs.promises.writeFile(snapFile(snap.id), JSON.stringify(snap), { flag: 'wx' });
  return snapSummary(snap);
}

// 保存した銘柄ごとに、そのときの株価の日付より後の日足で今の値段と利確・損切りの到達を見る。
// rows は保存した items と同じ順(取れなかった銘柄は { error })
async function compareSnapshot(snap) {
  const fallback = snap.result.at.slice(0, 10);
  const rows = await mapLimit(snap.result.items, 6, async (item) => {
    const since = item.priceDate || fallback;
    const ageDays = (Date.now() - Date.parse(since)) / 86400000;
    const bars = await history(`${item.code}.T`, ageDays > 300 ? '5y' : '1y', '1d');
    return a.compareSince(item, bars, since);
  });
  return { id: snap.id, at: new Date().toISOString(), rows };
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    req.on('data', (c) => {
      size += c.length;
      if (size > MAX_BODY) {
        reject(new Error('大きすぎます'));
        req.destroy();
      } else {
        chunks.push(c);
      }
    });
    req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    req.on('error', reject);
  });
}

async function handleSnapshots(req, res, pathname) {
  const m = pathname.match(/^\/api\/snapshots(?:\/(\d{13}))?(\/compare)?$/);
  if (!m || (m[2] && !m[1])) {
    res.writeHead(404).end();
    return;
  }
  const [, id, compare] = m;
  try {
    if (!id && req.method === 'GET') {
      sendJson(res, 200, await listSnapshots());
    } else if (!id && req.method === 'POST') {
      if (!String(req.headers['content-type'] || '').startsWith('application/json')) {
        sendJson(res, 415, { error: 'JSON で送ってください。' });
        return;
      }
      const body = JSON.parse(await readBody(req));
      if (!isScanResult(body.result)) {
        sendJson(res, 400, { error: '保存できる結果ではありません。' });
        return;
      }
      const summary = await saveSnapshot(body.result);
      log(`snapshot 保存 ${summary.id}`);
      sendJson(res, 200, summary);
    } else if (id && req.method === 'GET') {
      const snap = await readSnapshot(id);
      sendJson(res, 200, compare ? await compareSnapshot(snap) : snap);
    } else if (id && !compare && req.method === 'DELETE') {
      await fs.promises.unlink(snapFile(id));
      log(`snapshot 削除 ${id}`);
      sendJson(res, 200, { id });
    } else {
      res.writeHead(405).end();
    }
  } catch (e) {
    if (e.code === 'ENOENT') {
      sendJson(res, 404, { error: 'スナップショットが見つかりません。' });
    } else {
      log(`snapshot 失敗: ${e.message}`);
      sendJson(res, 500, { error: `スナップショットを扱えませんでした: ${e.message}` });
    }
  }
}

// ---------------------------------------------------------------- HTTP

function sendJson(res, status, body) {
  res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' });
  res.end(JSON.stringify(body));
}

function serveStatic(res, pathname) {
  const rel = pathname === '/' ? 'index.html' : decodeURIComponent(pathname.slice(1));
  const file = path.resolve(PUBLIC_DIR, rel);
  if (!file.startsWith(PUBLIC_DIR + path.sep)) {
    res.writeHead(403).end();
    return;
  }
  fs.readFile(file, (err, data) => {
    if (err) {
      res.writeHead(404).end();
      return;
    }
    res.writeHead(200, { 'Content-Type': TYPES[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-cache' });
    res.end(data);
  });
}

async function handle(req, res) {
  // DNS リバインディング対策: 自分宛ての Host 以外は断る
  if (!ALLOWED_HOSTS.has(req.headers.host)) {
    res.writeHead(403).end();
    return;
  }
  const url = new URL(req.url, `http://${req.headers.host}`);
  if (url.pathname.startsWith('/api/snapshots')) {
    // 書き換える操作は、この画面から来たものだけ受け付ける(よそのページからの送信を断る)
    const origin = String(req.headers.origin || '').replace(/^http:\/\//, '');
    if (req.method !== 'GET' && !ALLOWED_HOSTS.has(origin)) {
      res.writeHead(403).end();
      return;
    }
    await handleSnapshots(req, res, url.pathname);
    return;
  }
  if (req.method !== 'GET') {
    res.writeHead(405).end();
    return;
  }
  if (url.pathname === '/api/scan') {
    const params = parseScan(url.searchParams);
    if (params.error) {
      sendJson(res, 400, { error: params.error });
      return;
    }
    const started = Date.now();
    try {
      const result = await cachedScan(params);
      log(`scan ${params.mode} ${params.min}-${params.max}: ${result.items.length}件 ${Date.now() - started}ms`);
      sendJson(res, 200, result);
    } catch (e) {
      log(`scan 失敗: ${e.message}`);
      sendJson(res, 502, { error: `データを取れませんでした: ${e.message}` });
    }
    return;
  }
  if (url.pathname === '/api/chart') {
    const code = url.searchParams.get('code') || '';
    const weekly = url.searchParams.get('interval') === '1wk';
    if (!/^[0-9A-Z]{4}$/.test(code)) {
      sendJson(res, 400, { error: '銘柄コードが正しくありません。' });
      return;
    }
    try {
      // 日足は100本(5・25・75日線)、週足は78本(13・26週線)を出す。線を引く分だけ前から取る
      const series = weekly
        ? a.chartSeries(await history(`${code}.T`, '2y', '1wk'), [13, 26], 78)
        : a.chartSeries(await history(`${code}.T`, '1y', '1d'), [5, 25, 75], 100);
      sendJson(res, 200, { code, interval: weekly ? '1wk' : '1d', ...series });
    } catch (e) {
      sendJson(res, 502, { error: `グラフのデータを取れませんでした: ${e.message}` });
    }
    return;
  }
  serveStatic(res, url.pathname);
}

module.exports = { parseScan, shortCandidates, countExcluded, scan, isScanResult };

if (require.main === module) {
  http.createServer((req, res) => {
    handle(req, res).catch((e) => {
      log(`error: ${e.message}`);
      if (!res.headersSent) {
        res.writeHead(500).end();
      }
    });
  }).listen(PORT, HOST, () => log(`株レーダー http://${HOST}:${PORT}/`));
}
