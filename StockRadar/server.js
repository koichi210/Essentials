// 株レーダーのサーバ。Yahoo Finance の公開データ(スクリーナー・日足・月足)を取ってきて analyze.js で判定し、
// 画面(public/)へ JSON で渡す。npm パッケージは使わない。書くのはスナップショット・見張り・月足のキャッシュだけ。
// 動いている間は、取引時間中に見張りの銘柄の値段を数分おきに見て、条件を満たしたら LINE で知らせる(line.js)。
'use strict';

const http = require('http');
const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');
const a = require('./analyze.js');
const line = require('./line.js');

const PORT = Number(process.env.PORT) || 8766;
const HOST = '127.0.0.1';
const PUBLIC_DIR = path.join(__dirname, 'public');
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
// 見張り: DATA_DIR/watches.json。取引時間中に WATCH_MINUTES 分おきに今の値段を見る
const WATCH_FILE = path.join(DATA_DIR, 'watches.json');
const WATCH_ID = /^[0-9a-f]{12}$/;
const MAX_WATCHES = 100;
const WATCH_MINUTES = Math.min(60, Math.max(1, Math.round(Number(process.env.STOCK_RADAR_WATCH_MINUTES) || 5)));
// 動作確認用: 1 なら取引時間の外でも見張り、今日の値段でなくても条件を見る(ふだんは使わない)
const WATCH_ANYTIME = process.env.STOCK_RADAR_WATCH_ANYTIME === '1';
// 平均割れ: 月足は20年分を取り、DATA_DIR/cache/monthly/<コード>.json に1日覚えておく
const AVG_YEARS = [3, 5, 10, 15, 20];
const AVG_FETCH_YEARS = 20;
const MONTHLY_DIR = path.join(DATA_DIR, 'cache', 'monthly');
const MONTHLY_MS = 24 * 60 * 60 * 1000;
const AVG_CONCURRENCY = 4;
const MAX_ALL = 6000;
const MAX_RATE_LIMITED = 10;
// 待ち受けは 127.0.0.1 だけ。スマホから Tailscale serve 経由で開くときの名前(xxx.ts.net)は
// DATA_DIR/config.json の allowedHosts か環境変数 STOCK_RADAR_HOSTS(カンマ区切り)で足す。公開リポジトリに書かないため
const ALLOWED_HOSTS = new Set([`127.0.0.1:${PORT}`, `localhost:${PORT}`, ...extraHosts()]);
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.svg': 'image/svg+xml', '.ico': 'image/x-icon' };

function log(message) {
  console.log(`${new Date().toISOString()} ${message}`);
}

function extraHosts() {
  const hosts = String(process.env.STOCK_RADAR_HOSTS || '').split(',');
  try {
    const config = JSON.parse(fs.readFileSync(path.join(DATA_DIR, 'config.json'), 'utf8'));
    if (Array.isArray(config.allowedHosts)) {
      hosts.push(...config.allowedHosts);
    }
  } catch (e) {
    if (e.code !== 'ENOENT') {
      console.log(`config.json を読めませんでした: ${e.message}`);
    }
  }
  return hosts.map((h) => String(h).trim().toLowerCase().replace(/\.$/, '')).filter(Boolean);
}

// Origin(https://xxx.ts.net など)から host[:port] を取り出す。読めなければ ''
function originHost(origin) {
  try {
    return new URL(origin).host;
  } catch {
    return '';
  }
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

// 東証の銘柄を時価総額の大きい順に250件ずつ。operands は地域・取引所に足す条件(値段の幅・時価総額など)
async function screenPage(operands, offset, retried = false) {
  const s = await yahooSession();
  const body = {
    size: 250, offset, sortField: 'intradaymarketcap', sortType: 'DESC', quoteType: 'EQUITY',
    query: { operator: 'AND', operands: [
      { operator: 'eq', operands: ['region', 'jp'] },
      { operator: 'eq', operands: ['exchange', 'JPX'] },
      ...operands,
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
    return screenPage(operands, offset, true);
  }
  if (!res.ok) {
    throw new Error(`スクリーナーの取得に失敗しました(${res.status})`);
  }
  const r = (await res.json()).finance.result[0];
  return { total: r.total, quotes: r.quotes || [] };
}

// 最大 maxCount 件まで、ページをめくって全部取る。onPage(取った件数, 全体の件数) で進み具合を知らせる
async function screenAll(operands, maxCount, onPage = () => {}) {
  const all = [];
  for (let offset = 0; offset < maxCount; offset += 250) {
    const page = await screenPage(operands, offset);
    all.push(...page.quotes);
    onPage(all.length, page.total);
    if (page.quotes.length < 250 || all.length >= page.total) {
      break;
    }
  }
  return all.filter((q) => q.quoteType === 'EQUITY' && q.regularMarketPrice > 0);
}

function screen(minPrice, maxPrice) {
  return screenAll([
    { operator: 'btwn', operands: ['intradayprice', minPrice, maxPrice] },
    { operator: 'gt', operands: ['intradaymarketcap', MIN_MARKET_CAP] },
  ], MAX_UNIVERSE);
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

// 見張り用の今の値段(約20分遅れ)。日足のキャッシュは使わず毎回取る(1銘柄 1KB ほど)
async function quoteNow(code) {
  const url = `https://query1.finance.yahoo.com/v8/finance/chart/${encodeURIComponent(code)}.T?range=1d&interval=1d`;
  const res = await fetch(url, { headers: { 'User-Agent': UA }, signal: AbortSignal.timeout(15000) });
  if (res.status === 404) {
    throw new Error(`${code} という銘柄が見つかりません`);
  }
  if (!res.ok) {
    throw new Error(`${code} の値段を取れませんでした(${res.status})`);
  }
  const q = a.quoteFromChart(await res.json());
  if (!q) {
    throw new Error(`${code} の値段がありません`);
  }
  return q;
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// 月足(20年分)。DATA_DIR/cache/monthly/<コード>.json に1日覚えておく。
// 429(送りすぎ)や 5xx は少し待って2回まで取り直し、429 が続いたら job.stopped を立てて残りを取りに行かない
async function monthlyHistory(code, job) {
  const file = path.join(MONTHLY_DIR, `${code}.json`);
  try {
    const c = JSON.parse(await fs.promises.readFile(file, 'utf8'));
    if (Date.now() - c.at < MONTHLY_MS && Array.isArray(c.months)) {
      return { months: c.months, cached: true };
    }
  } catch {
    // 無いか壊れていれば取り直す
  }
  const nowSec = Math.floor(Date.now() / 1000);
  const period1 = nowSec - Math.round((AVG_FETCH_YEARS * 12 + 2) * 30.44 * 86400);
  const url = `https://query1.finance.yahoo.com/v8/finance/chart/${encodeURIComponent(code)}.T?period1=${period1}&period2=${nowSec}&interval=1mo`;
  let res;
  for (let i = 0; ; i++) {
    res = await fetch(url, { headers: { 'User-Agent': UA }, signal: AbortSignal.timeout(20000) });
    if (res.status === 429) {
      job.rateLimited++;
    }
    if ((res.status !== 429 && res.status < 500) || i >= 2 || job.cancelled) {
      break;
    }
    await sleep(3000 * (i + 1));
  }
  if (res.status === 429 && job.rateLimited >= MAX_RATE_LIMITED) {
    job.stopped = 'Yahoo に何度も断られた(送りすぎ)ので、途中で止めました。しばらくたってからもう一度試してください。';
  }
  if (!res.ok) {
    throw new Error(`${code} の月足を取れませんでした(${res.status})`);
  }
  const months = a.monthlyCloses(await res.json());
  await fs.promises.mkdir(MONTHLY_DIR, { recursive: true });
  await fs.promises.writeFile(file, JSON.stringify({ at: Date.now(), months }));
  return { months, cached: false };
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

// JSON の本文を読む。読めなければ応答を返して undefined
async function readJson(req, res) {
  if (!String(req.headers['content-type'] || '').startsWith('application/json')) {
    sendJson(res, 415, { error: 'JSON で送ってください。' });
    return undefined;
  }
  try {
    return JSON.parse(await readBody(req)) ?? {};
  } catch {
    sendJson(res, 400, { error: '送られてきた内容を読めませんでした。' });
    return undefined;
  }
}

// ---------------------------------------------------------------- 見張り(指定した値段になったら LINE で知らせる)

// 画面から送られた見張りの条件。code: 4けた、op: above(以上) / below(以下)、price: 円。name・note は表示用
function parseWatch(body) {
  const code = String((body && body.code) ?? '').trim().toUpperCase();
  if (!/^[0-9A-Z]{4}$/.test(code)) {
    return { error: '銘柄コードは4けたで入れてください。' };
  }
  if (!Object.hasOwn(a.WATCH_OPS, body.op)) {
    return { error: '「以上」か「以下」を選んでください。' };
  }
  const price = Number(body.price);
  if (!Number.isFinite(price) || price <= 0 || price > 1e7) {
    return { error: '値段は0円より大きく、1000万円までで入れてください。' };
  }
  const clean = (v, n) => String(v ?? '').replace(/[\u0000-\u001f\u007f]/g, ' ').trim().slice(0, n);
  return { code, op: body.op, price, name: clean(body.name, 60), note: clean(body.note, 40) };
}

let watchList = null;
let watchWrite = Promise.resolve();
const monitor = { lastRunAt: null, running: false };

async function loadWatches() {
  if (watchList) {
    return watchList;
  }
  try {
    const v = JSON.parse(await fs.promises.readFile(WATCH_FILE, 'utf8'));
    watchList = Array.isArray(v) ? v.filter((w) => w && WATCH_ID.test(w.id)) : [];
  } catch (e) {
    if (e.code !== 'ENOENT') {
      // 壊れたファイルは消さずに残しておく
      log(`watches.json を読めませんでした(watches.broken.json に残します): ${e.message}`);
      await fs.promises.copyFile(WATCH_FILE, path.join(DATA_DIR, 'watches.broken.json')).catch(() => {});
    }
    watchList = [];
  }
  return watchList;
}

// 書きかけで落ちても壊れないように、別名で書いてから置き換える。書く順番は呼んだ順
function saveWatches() {
  const text = JSON.stringify(watchList, null, 1);
  watchWrite = watchWrite.catch(() => {}).then(async () => {
    await fs.promises.mkdir(DATA_DIR, { recursive: true });
    const tmp = `${WATCH_FILE}.tmp`;
    await fs.promises.writeFile(tmp, text);
    await fs.promises.rename(tmp, WATCH_FILE);
  });
  return watchWrite;
}

// 見張っている銘柄の今の値段を見て、条件を満たしたものを LINE で1通にまとめて知らせる。
// 自動(manual=false)のときは取引時間の外なら何もしない。手動(画面の「今の値段を確かめる」)は値段だけいつでも更新する。
// 知らせるのは、取引時間中で今日ついた値段が条件を満たしたときだけ。送れたら「知らせた」にして止める(送れなければ次の回にまた試す)
async function checkWatches(manual = false) {
  const nowSec = Math.floor(Date.now() / 1000);
  if (monitor.running || (!manual && !WATCH_ANYTIME && !a.isMarketHours(nowSec))) {
    return { checked: 0, hits: 0 };
  }
  const active = (await loadWatches()).filter((w) => w.state === 'watching');
  if (active.length === 0) {
    return { checked: 0, hits: 0 };
  }
  monitor.running = true;
  try {
    const quotes = new Map();
    await mapLimit([...new Set(active.map((w) => w.code))], 3, async (code) => {
      quotes.set(code, await quoteNow(code).catch((e) => ({ error: e.message })));
    });
    const at = new Date().toISOString();
    const canNotify = WATCH_ANYTIME || a.isMarketHours(nowSec);
    const hits = [];
    for (const w of active) {
      const q = quotes.get(w.code);
      w.checkedAt = at;
      if (!q || q.error) {
        w.priceError = q ? q.error : '値段を取れませんでした';
        continue;
      }
      delete w.priceError;
      w.lastPrice = q.price;
      w.lastTime = q.time;
      if (canNotify && (WATCH_ANYTIME || a.isTodayPrice(q.time, nowSec)) && a.watchHit(w, q.price)) {
        hits.push({ watch: w, price: q.price, time: q.time });
      }
    }
    if (hits.length > 0) {
      try {
        await line.pushText(DATA_DIR, a.watchMessage(hits), log);
        for (const h of hits) {
          Object.assign(h.watch, { state: 'notified', notifiedAt: at, hitPrice: h.price, hitTime: h.time });
          delete h.watch.error;
        }
        log(`見張り: ${hits.map((h) => h.watch.code).join(',')} を知らせました`);
      } catch (e) {
        for (const h of hits) {
          h.watch.error = e.message;
        }
        log(`見張り: ${e.message}`);
      }
    }
    monitor.lastRunAt = at;
    await saveWatches();
    return { checked: active.length, hits: hits.length };
  } finally {
    monitor.running = false;
  }
}

async function watchStatus() {
  return {
    watches: await loadWatches(),
    line: { configured: line.isConfigured(DATA_DIR), dryRun: line.isDryRun() },
    monitor: { minutes: WATCH_MINUTES, marketHours: a.isMarketHours(Math.floor(Date.now() / 1000)), anytime: WATCH_ANYTIME, lastRunAt: monitor.lastRunAt },
  };
}

async function handleWatches(req, res, pathname) {
  const m = pathname.match(/^\/api\/watches(?:\/([0-9a-f]{12})(\/resume)?|\/(check))?$/);
  if (!m) {
    res.writeHead(404).end();
    return;
  }
  const [, id, resume, check] = m;
  try {
    if (!id && !check && req.method === 'GET') {
      sendJson(res, 200, await watchStatus());
    } else if (!id && !check && req.method === 'POST') {
      const body = await readJson(req, res);
      if (body === undefined) {
        return;
      }
      const w = parseWatch(body);
      if (w.error) {
        sendJson(res, 400, { error: w.error });
        return;
      }
      const list = await loadWatches();
      if (list.length >= MAX_WATCHES) {
        sendJson(res, 400, { error: `見張りは${MAX_WATCHES}件までです。いらないものを削除してください。` });
        return;
      }
      // 銘柄があるかを確かめ、今の値段を一緒に覚えておく
      let q;
      try {
        q = await quoteNow(w.code);
      } catch (e) {
        sendJson(res, 400, { error: e.message });
        return;
      }
      const watch = {
        id: crypto.randomBytes(6).toString('hex'),
        code: w.code,
        name: w.name || q.name,
        op: w.op,
        price: w.price,
        note: w.note,
        state: 'watching',
        createdAt: new Date().toISOString(),
        lastPrice: q.price,
        lastTime: q.time,
        checkedAt: new Date().toISOString(),
      };
      list.push(watch);
      await saveWatches();
      log(`見張り 追加 ${watch.code} ${watch.op}`);
      sendJson(res, 200, { watch, already: a.watchHit(watch, q.price) });
    } else if (check && req.method === 'POST') {
      const r = await checkWatches(true);
      sendJson(res, 200, { ...r, ...(await watchStatus()) });
    } else if (id && req.method === 'DELETE' && !resume) {
      const list = await loadWatches();
      const i = list.findIndex((w) => w.id === id);
      if (i < 0) {
        sendJson(res, 404, { error: '見張りが見つかりません。' });
        return;
      }
      list.splice(i, 1);
      await saveWatches();
      sendJson(res, 200, { id });
    } else if (id && resume && req.method === 'POST') {
      const w = (await loadWatches()).find((x) => x.id === id);
      if (!w) {
        sendJson(res, 404, { error: '見張りが見つかりません。' });
        return;
      }
      w.state = 'watching';
      for (const k of ['notifiedAt', 'hitPrice', 'hitTime', 'error']) {
        delete w[k];
      }
      await saveWatches();
      sendJson(res, 200, { watch: w });
    } else {
      res.writeHead(405).end();
    }
  } catch (e) {
    log(`見張り 失敗: ${e.message}`);
    sendJson(res, 500, { error: `見張りを扱えませんでした: ${e.message}` });
  }
}

async function handleLineTest(req, res) {
  if (req.method !== 'POST') {
    res.writeHead(405).end();
    return;
  }
  try {
    const r = await line.pushText(DATA_DIR, '📡 株レーダーからのテストです。値段の見張りの知らせは、このトークに届きます。', log);
    sendJson(res, 200, r);
  } catch (e) {
    sendJson(res, 502, { error: e.message });
  }
}

function startMonitor() {
  const tick = () => checkWatches().catch((e) => log(`見張り 失敗: ${e.message}`));
  setTimeout(tick, 10 * 1000);
  setInterval(tick, WATCH_MINUTES * 60 * 1000);
}

// ---------------------------------------------------------------- 平均割れ(過去N年の平均より安い銘柄)

// 空なら null、lo〜hi の数ならその数、それ以外は NaN(JSON の本文用)
function bodyNumber(v, lo, hi) {
  if (v == null || v === '') {
    return null;
  }
  const n = Number(v);
  return Number.isFinite(n) && n >= lo && n <= hi ? n : NaN;
}

// years: 3・5・10・15・20 年。per・pbr: 既存の絞り込みと同じ。mcap: 時価総額(円)の下限。
// budget: { min, max }(100株の金額、円)を付けたときだけ値段で絞る。limit: 月足を取る銘柄数の上限(動作確認用)
function parseAvgScan(body) {
  const b = body || {};
  const years = b.years == null ? 10 : Number(b.years);
  if (!AVG_YEARS.includes(years)) {
    return { error: `年数は ${AVG_YEARS.join('・')} 年から選んでください。` };
  }
  const per = bodyNumber(b.per, 1, 1000);
  const pbr = bodyNumber(b.pbr, 0.1, 100);
  const mcap = bodyNumber(b.mcap, 0, 1e15);
  const limit = bodyNumber(b.limit, 1, MAX_ALL);
  if ([per, pbr, mcap, limit].some(Number.isNaN) || (limit != null && !Number.isInteger(limit))) {
    return { error: '絞り込みの条件が正しくありません。' };
  }
  let budget = null;
  if (b.budget) {
    const max = Number(b.budget.max);
    const min = Number(b.budget.min || 0);
    if (!Number.isFinite(max) || max < 10000 || max > 1e8 || !Number.isFinite(min) || min < 0 || min >= max) {
      return { error: '予算は1万円〜1億円で、下限は上限より小さくしてください。' };
    }
    budget = { min, max };
  }
  return { years, per, pbr, mcap: mcap || null, budget, limit };
}

let avgJob = null;

function publicJob(job) {
  if (!job) {
    return { state: 'idle' };
  }
  const { req, state, phase, done, total, cached, startedAt, finishedAt, error, result } = job;
  return { state, phase, done, total, cached, startedAt, finishedAt, error, req, result };
}

async function runAvgScan(job) {
  const { req } = job;
  const nowSec = Math.floor(Date.now() / 1000);
  const operands = [];
  if (req.budget) {
    operands.push({ operator: 'btwn', operands: ['intradayprice', Math.max(1, req.budget.min / a.LOT), req.budget.max / a.LOT] });
  }
  if (req.mcap) {
    operands.push({ operator: 'gt', operands: ['intradaymarketcap', req.mcap] });
  }
  job.phase = 'screen';
  const all = await screenAll(operands, MAX_ALL, (got, total) => {
    job.done = got;
    job.total = total;
  });
  // REIT などを外し、PER・PBR(スクリーナーの値)で絞ってから月足を取る
  const stocks = all.filter(a.isCommonStock);
  const matched = stocks.filter((q) => a.matchValuation(q, req));
  const { from, to } = a.avgWindow(req.years, nowSec);
  // Yahoo のデータが平均の範囲より後から始まる銘柄(上場して N 年たっていない)は、月足を取らずにデータ不足へ
  const young = matched.filter((q) => a.listedAfter(q, from));
  let targets = matched.filter((q) => !a.listedAfter(q, from));
  const limited = req.limit != null && targets.length > req.limit;
  if (limited) {
    targets = targets.slice(0, req.limit);
  }
  job.phase = 'history';
  job.done = 0;
  job.total = targets.length;
  const rows = await mapLimit(targets, AVG_CONCURRENCY, async (q) => {
    if (job.cancelled || job.stopped) {
      return { skipped: true };
    }
    try {
      const { months, cached } = await monthlyHistory(a.codeOf(q.symbol), job);
      if (cached) {
        job.cached++;
      }
      return a.belowAverage(q, months, req.years, nowSec);
    } finally {
      job.done++;
    }
  });
  if (job.cancelled) {
    return null;
  }
  const ok = rows.filter((r) => r && !r.error && !r.skipped);
  const judged = ok.filter((r) => !r.excluded);
  const insufficient = [
    ...young.map((q) => ({ code: a.codeOf(q.symbol), name: a.belowAverage(q, [], req.years, nowSec).name, first: a.firstMonthOf(q), months: null })),
    ...ok.filter((r) => r.excluded).map(({ code, name, first, months }) => ({ code, name, first, months })),
  ];
  const dataTimes = matched.map((q) => q.regularMarketTime).filter(Number.isFinite);
  return {
    at: new Date().toISOString(),
    dataTime: dataTimes.length > 0 ? new Date(Math.max(...dataTimes) * 1000).toISOString() : null,
    years: req.years,
    window: { from, to },
    filter: { per: req.per, pbr: req.pbr, mcap: req.mcap, budget: req.budget },
    universe: all.length,
    stocks: stocks.length,
    matched: matched.length,
    fetched: targets.length,
    cached: job.cached,
    limited: limited ? req.limit : null,
    failed: rows.filter((r) => r && r.error).length,
    skipped: rows.filter((r) => r && r.skipped).length,
    stopped: job.stopped || null,
    above: judged.filter((r) => r.gap <= 0).length,
    insufficient,
    items: judged.filter((r) => r.gap > 0).sort((x, y) => y.gap - x.gap || x.code.localeCompare(y.code)),
  };
}

function startAvgScan(req) {
  const job = { req, state: 'running', phase: 'screen', done: 0, total: 0, cached: 0, rateLimited: 0, startedAt: new Date().toISOString() };
  avgJob = job;
  const started = Date.now();
  runAvgScan(job).then((result) => {
    job.finishedAt = new Date().toISOString();
    if (job.cancelled) {
      job.state = 'cancelled';
      log('平均割れ: やめました');
      return;
    }
    job.state = 'done';
    job.result = result;
    log(`平均割れ ${req.years}年: 対象 ${result.matched} → 月足 ${result.fetched}(キャッシュ ${result.cached}・失敗 ${result.failed}) → 平均より下 ${result.items.length} ${Date.now() - started}ms`);
  }).catch((e) => {
    job.finishedAt = new Date().toISOString();
    job.state = 'error';
    job.error = `データを取れませんでした: ${e.message}`;
    log(`平均割れ 失敗: ${e.message}`);
  });
  return job;
}

async function handleAvgScan(req, res, pathname) {
  if (pathname === '/api/avgscan' && req.method === 'GET') {
    sendJson(res, 200, publicJob(avgJob));
  } else if (pathname === '/api/avgscan' && req.method === 'POST') {
    if (avgJob && avgJob.state === 'running') {
      sendJson(res, 409, { error: '今調べている途中です。終わるか「やめる」を押してからにしてください。', ...publicJob(avgJob) });
      return;
    }
    const body = await readJson(req, res);
    if (body === undefined) {
      return;
    }
    const params = parseAvgScan(body);
    if (params.error) {
      sendJson(res, 400, { error: params.error });
      return;
    }
    sendJson(res, 200, publicJob(startAvgScan(params)));
  } else if (pathname === '/api/avgscan/cancel' && req.method === 'POST') {
    if (avgJob && avgJob.state === 'running') {
      avgJob.cancelled = true;
    }
    sendJson(res, 200, publicJob(avgJob));
  } else {
    res.writeHead(pathname === '/api/avgscan' || pathname === '/api/avgscan/cancel' ? 405 : 404).end();
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

// GET 以外は Origin を確かめる API
const WRITE_APIS = [
  ['/api/snapshots', handleSnapshots],
  ['/api/watches', handleWatches],
  ['/api/line/test', handleLineTest],
  ['/api/avgscan', handleAvgScan],
];

async function handle(req, res) {
  // DNS リバインディング対策: 自分宛ての Host 以外は断る
  if (!ALLOWED_HOSTS.has(String(req.headers.host || '').toLowerCase())) {
    res.writeHead(403).end();
    return;
  }
  const url = new URL(req.url, `http://${req.headers.host}`);
  const writeApi = WRITE_APIS.find(([prefix]) => url.pathname === prefix || url.pathname.startsWith(`${prefix}/`));
  if (writeApi) {
    // 書き換える操作(と LINE 送信・重い調査の開始)は、この画面から来たものだけ受け付ける(よそのページからの送信を断る)
    if (req.method !== 'GET' && !ALLOWED_HOSTS.has(originHost(req.headers.origin))) {
      res.writeHead(403).end();
      return;
    }
    await writeApi[1](req, res, url.pathname);
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

module.exports = { parseScan, shortCandidates, countExcluded, scan, isScanResult, parseWatch, parseAvgScan };

if (require.main === module) {
  http.createServer((req, res) => {
    handle(req, res).catch((e) => {
      log(`error: ${e.message}`);
      if (!res.headersSent) {
        res.writeHead(500).end();
      }
    });
  }).listen(PORT, HOST, () => {
    log(`株レーダー http://${HOST}:${PORT}/ (受け付ける名前: ${[...ALLOWED_HOSTS].join(', ')})`);
    const lineMode = line.isDryRun() ? '送らないモード' : line.isConfigured(DATA_DIR) ? '設定あり' : '未設定';
    log(`見張り: 取引時間中に${WATCH_MINUTES}分おき${WATCH_ANYTIME ? '(時間外も見る確認用モード)' : ''} / LINE: ${lineMode}`);
    startMonitor();
  });
}
