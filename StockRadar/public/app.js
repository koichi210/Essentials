// 株レーダーの画面。入力は localStorage に覚えておき、結果は /api/scan から受け取って並べる。
// スナップショット(/api/snapshots)は結果をサーバのファイルに保存し、あとで開いて今の値段と比べる。
// 平均割れ(/api/avgscan)はサーバで時間をかけて調べるので、進み具合を1秒ごとに聞きに行く。
// 見張り(/api/watches)は、決めた値段になったらサーバが LINE で知らせる
'use strict';

const STORE_KEY = 'stockRadar.form';
const LAST_KEY = 'stockRadar.last';
const SORT_KEY = 'stockRadar.sort';
const AVG_KEY = 'stockRadar.avg';
const AVG_VIEW_KEY = 'stockRadar.avgView';
const AVG_PAGE = 100;
const MODE_LABEL = { short: '短期', long: '長期' };
const MARKET_LABEL = { good: '地合い: 良い', mixed: '地合い: 普通', bad: '地合い: 弱い' };

const $ = (id) => document.getElementById(id);
const yen = (v) => `${Math.round(v).toLocaleString('ja-JP')}円`;
const price = (v) => v.toLocaleString('ja-JP', { maximumFractionDigits: 1 });
const escapeAttr = (s) => String(s).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');
const signed = (v, unit = '') => `${v > 0 ? '+' : v < 0 ? '−' : '±'}${Math.abs(v).toLocaleString('ja-JP', { maximumFractionDigits: 1 })}${unit}`;
// 上がった(赤)・下がった(青)の色の class
const tone = (v) => (v > 0 ? 'rise' : v < 0 ? 'fall' : '');

function load(key) {
  try {
    return JSON.parse(localStorage.getItem(key));
  } catch {
    return null;
  }
}

function save(key, value) {
  try {
    localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // 保存できなくても画面はそのまま使える
  }
}

function el(tag, attrs = {}, ...children) {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (k === 'class') {
      e.className = v;
    } else {
      e.setAttribute(k, v);
    }
  }
  for (const c of children.flat()) {
    if (c != null && c !== false) {
      e.append(c);
    }
  }
  return e;
}

function formatTime(iso) {
  const d = new Date(iso);
  return d.toLocaleString('ja-JP', { month: 'numeric', day: 'numeric', weekday: 'short', hour: '2-digit', minute: '2-digit' });
}

function formatDate(date) {
  const d = new Date(`${date}T00:00:00`);
  return d.toLocaleDateString('ja-JP', { month: 'numeric', day: 'numeric', weekday: 'short' });
}

// ---------------------------------------------------------------- 用語集

// 押すと用語集のその言葉を開く(id は glossary.js)
function term(label, id) {
  return id ? el('button', { type: 'button', class: 'term', 'data-term': id }, label) : label;
}

// 銘柄カードの数字の横に出す「(境目1倍・平均約1.7倍)」
const BASE = Object.fromEntries(GLOSSARY.flatMap((g) => g.items).filter((t) => t.base)
  .map((t) => [t.id, t.base.map(([kind, value]) => `${kind}${value}`).join('・')]));

function renderGlossary() {
  const index = el('nav', { class: 'g-index' },
    GLOSSARY.flatMap((g) => g.items).map((t) => el('a', { href: `#g-${t.id}`, 'data-term': t.id }, t.term.replace(/\(.*\)$/, ''))));
  const groups = GLOSSARY.map((g) => el('section', {},
    el('h3', { class: 'g-group' }, g.group),
    g.items.map((t) => el('article', { class: 'g-item', id: `g-${t.id}` },
      el('h4', {}, t.term),
      t.base ? el('p', { class: 'g-base' }, '📏 ', t.base.flatMap(([kind, value, note], i) => [i > 0 ? '／' : null, `${kind} `, el('b', {}, value), `(${note})`])) : null,
      el('p', {}, t.what),
      t.scale.length > 0
        ? el('dl', { class: 'g-scale' }, t.scale.map(([range, meaning, tone]) => el('div', { class: tone },
          el('dt', {}, range), el('dd', {}, tone === 'good' ? `👍 ${meaning}` : tone === 'bad' ? `⚠ ${meaning}` : meaning))))
        : null,
      t.tip ? el('p', { class: 'g-tip' }, `💡 ${t.tip}`) : null))));
  $('g-body').replaceChildren(index, ...groups);
}

function openGlossary(id) {
  const dialog = $('glossary');
  if (!dialog.open) {
    dialog.showModal();
  }
  const target = id && document.getElementById(`g-${id}`);
  if (target) {
    target.scrollIntoView({ block: 'start' });
    target.classList.remove('flash');
    void target.offsetWidth;
    target.classList.add('flash');
  } else if (!id) {
    $('g-body').scrollTop = 0;
  }
}

// ---------------------------------------------------------------- 描画

// 絞り込みの条件を1行にする(無ければ null)
function filterText(f = {}) {
  const crossText = (label, c) => {
    if (!c) {
      return null;
    }
    if (c.days == null) {
      return `${label}: もうすぐ起きそう`;
    }
    if (c.not) {
      return `${label}: ${c.days}日以内に起きた銘柄を除く${c.soon ? '(もうすぐも除く)' : ''}`;
    }
    return `${label}: ${c.days}日以内${c.soon ? '(もうすぐも含む)' : ''}`;
  };
  const conds = [
    f.per != null ? `PER ${f.per}倍以下` : null,
    f.pbr != null ? `PBR ${f.pbr}倍未満` : null,
    crossText('ゴールデンクロス', f.gc),
    crossText('デッドクロス', f.dc),
  ].filter(Boolean);
  return conds.length > 0 ? `${conds.join('・')}${f.gc || f.dc ? `（${f.crossName}）` : ''}` : null;
}

function renderStatus(r) {
  const s = $('status');
  s.className = 'status';
  const excluded = Object.entries(r.excluded).map(([k, v]) => `${k} ${v}`).join('・');
  const f = r.filter || {};
  const conds = filterText(f);
  s.textContent = [
    `${formatTime(r.at)} に調べました（${MODE_LABEL[r.mode]}・100株で${yen(r.budget.min)}〜${yen(r.budget.max)}）`,
    r.dataTime ? `株価は ${formatTime(r.dataTime)} 時点` : null,
    conds ? `絞り込み: ${conds}` : null,
    r.filtered != null && r.filtered !== r.universe ? `対象 ${r.universe} 銘柄 → PER・PBR で ${r.filtered} 銘柄` : `対象 ${r.universe} 銘柄`,
    f.longLimit ? `点の高い ${f.longLimit} 銘柄でクロスを確認` : `${r.analyzed} 銘柄を判定`,
    excluded ? `除外: ${excluded}` : null,
    r.failed ? `取得失敗 ${r.failed}` : null,
  ].filter(Boolean).join(' ／ ');
  s.hidden = false;

  const m = $('market');
  if (r.market) {
    m.className = `market ${r.market.level}`;
    m.replaceChildren(el('b', {}, term(MARKET_LABEL[r.market.level], 'market')), ` 日経平均 ${yen(r.market.price)}（25日線 ${yen(r.market.sma25)}・75日線 ${yen(r.market.sma75)}）。${r.market.text}`);
    m.hidden = false;
  } else {
    m.hidden = true;
  }
}

// 前日終値と、そこからの値動き(上がったら赤・下がったら青。チャートのローソク足と同じ)
function prevCloseText(it) {
  if (!(it.prevClose > 0)) {
    return [];
  }
  const diff = Math.round((it.price - it.prevClose) * 10) / 10;
  const pct = Math.round((it.price / it.prevClose - 1) * 1000) / 10;
  return [` ／ 前日終値 ${price(it.prevClose)}円`, el('span', { class: `move ${tone(diff)}` }, `（${signed(diff, '円')}・${signed(pct, '%')}）`)];
}

function card(item, index, mode) {
  const head = el('div', { class: 'head' },
    el('span', { class: 'rank' }, `${index + 1}`),
    el('h2', { class: 'name' }, item.name),
    el('span', { class: 'code' }, item.code),
    item.setup ? el('span', { class: 'setup' }, item.setup) : null,
    el('span', { class: 'score' },
      term(`買いスコア ${item.score}`, 'score'),
      el('span', { class: 'meter' }, el('i', { style: `width:${item.score}%` }))),
  );

  const stats = [
    ['PER', item.stats.per, '倍', 'per'],
    ['PBR', item.stats.pbr, '倍', 'pbr'],
    ['ROE', item.stats.roe, '%', 'roe'],
    ['配当', item.stats.dividend, '%', 'dividend'],
    ['配当性向', item.stats.payout, '%', 'payout'],
    ['RSI', item.stats.rsi, '', 'rsi'],
  ].filter(([, v]) => v != null).map(([label, v, unit, id]) => term([`${label} ${v}${unit}`, BASE[id] ? el('small', { class: 'base' }, `(${BASE[id]})`) : null], id));
  const priceLine = el('p', { class: 'price' },
    '現在値 ', el('b', {}, `${price(item.price)}円`),
    item.priceDate ? `（${formatDate(item.priceDate)}）` : '',
    ...prevCloseText(item),
    ` ／ 100株で ${yen(item.cost)}`,
    ...(stats.length ? [' ／ ', ...stats.flatMap((s, i) => (i > 0 ? ['・', s] : [s]))] : []));

  const buyText = item.buy.low === item.buy.high ? `${price(item.buy.high)}円付近` : `${price(item.buy.low)}〜${price(item.buy.high)}円`;
  const plan = el('dl', { class: 'plan' },
    el('div', { class: 'buy' }, el('dt', {}, term('買い', 'limit')), el('dd', {}, buyText, el('small', {}, item.buy.low === item.buy.high ? 'この値段で指値' : 'この範囲で指値'))),
    el('div', { class: 'gain' }, el('dt', {}, term('利確', 'target')), el('dd', {}, `${price(item.target)}円`, el('small', {}, `100株で +${yen(item.gainYen)}`))),
    el('div', { class: 'loss' }, el('dt', {}, term('損切り', 'stop')), el('dd', {}, `${price(item.stop)}円`, el('small', {}, `100株で −${yen(item.lossYen)}`))),
  );

  const meta = mode === 'short'
    ? el('p', { class: 'meta' }, `⏳ ${formatDate(item.deadline)} までに利確・損切りのどちらにも届かなければ`, term('手じまい', 'deadline'), ' ／ ', term(`損益比 ${item.rr}`, 'rr'))
    : el('p', { class: 'meta' }, '🗓️ 半年〜1年が目安。決算のたびに見直し ／ ', term(`損益比 ${item.rr}`, 'rr'));

  // 短期で持っている間に権利確定が来るなら知らせる(権利落ち日は配当・優待の分だけ下がりやすい)
  const rights = (item.rights || []).map((r) => ({
    label: `保有中に${r.kind}の権利確定（${formatDate(r.date)}）`,
    text: '権利落ち日は、その分だけ株価が下がりやすい',
    term: 'rights',
  }));

  const watch = el('button', { type: 'button', class: 'link-button' }, '🔔 この銘柄を見張る');
  watch.addEventListener('click', () => openWatches({
    code: item.code,
    name: item.name,
    suggest: [
      { label: `買いの上限 ${price(item.buy.high)}円以下`, op: 'below', price: item.buy.high, note: '買い' },
      { label: `利確 ${price(item.target)}円以上`, op: 'above', price: item.target, note: '利確' },
      { label: `損切り ${price(item.stop)}円以下`, op: 'below', price: item.stop, note: '損切り' },
    ],
  }));
  const links = el('p', { class: 'links' },
    watch,
    el('a', { href: `https://kabutan.jp/stock/?code=${item.code}`, target: '_blank', rel: 'noopener' }, '株探で決算を見る'),
    el('a', { href: `https://finance.yahoo.co.jp/quote/${item.code}.T`, target: '_blank', rel: 'noopener' }, 'Yahoo!ファイナンス'),
    el('a', { href: `https://finance.yahoo.co.jp/quote/${item.code}.T/incentive`, target: '_blank', rel: 'noopener' }, '優待のページ'));

  // 横に広いときは、左に押しポイントなど・右にチャート。狭いときは縦に並べる
  const side = el('div', { class: 'card-side' },
    points('👍 押しポイント', 'good', item.reasons),
    points('⚠ 気をつけるところ', 'bad', [...item.warnings, ...rights]),
    perks(item));
  return el('li', { class: 'card' }, head, priceLine, plan, meta,
    el('div', { class: 'card-body' }, side, chartBox(item)),
    links);
}

function points(title, cls, list) {
  if (list.length === 0) {
    return null;
  }
  return el('div', { class: `points ${cls}` },
    el('p', { class: 'points-title' }, title),
    el('ul', {}, list.map((p) => (typeof p === 'string'
      // 前の版で保存した結果(文字列だけ)もそのまま出す
      ? el('li', {}, el('b', {}, p))
      : el('li', {}, el('b', {}, term(p.label, p.term)), p.text ? ` … ${p.text}` : '')))));
}

// ---------------------------------------------------------------- チャート

const INTERVAL_LABEL = { '1d': '日足', '1wk': '週足' };
const MA_LABEL = { '1d': (n) => `${n}日線`, '1wk': (n) => `${n}週線` };
const chartCache = new Map();

// 画面に近づいたカードから日足を取りに行く(20銘柄を一度に取らないように)
const chartObserver = new IntersectionObserver((entries) => {
  for (const e of entries) {
    if (e.isIntersecting) {
      chartObserver.unobserve(e.target);
      e.target.dispatchEvent(new Event('chart-visible'));
    }
  }
}, { rootMargin: '400px 0px' });

// 日足を出し、タブで週足に切り替える
function chartBox(item) {
  const box = el('div', { class: 'chart' });
  const tabs = el('div', { class: 'chart-tabs', role: 'tablist' },
    Object.entries(INTERVAL_LABEL).map(([iv, label]) => el('button', { type: 'button', role: 'tab', 'data-iv': iv, 'aria-selected': iv === '1d' ? 'true' : 'false' }, label)),
    el('span', { class: 'chart-help' }, term('ローソク足の見かた', 'candle')));
  const body = el('div', { class: 'chart-body' });
  box.append(tabs, body);
  box.addEventListener('chart-visible', () => showChart(body, item, tabs.querySelector('[aria-selected=true]').dataset.iv), { once: true });
  chartObserver.observe(box);
  tabs.addEventListener('click', (e) => {
    const b = e.target.closest('[data-iv]');
    if (!b) {
      return;
    }
    tabs.querySelectorAll('[data-iv]').forEach((t) => t.setAttribute('aria-selected', String(t === b)));
    showChart(body, item, b.dataset.iv);
  });
  return box;
}

async function showChart(body, item, interval) {
  const key = `${item.code}|${interval}`;
  body.replaceChildren(el('p', { class: 'chart-note' }, '読み込んでいます…'));
  try {
    if (!chartCache.has(key)) {
      const res = await fetch(`/api/chart?code=${encodeURIComponent(item.code)}&interval=${interval}`);
      const data = await res.json();
      if (!res.ok) {
        throw new Error(data.error || `エラー(${res.status})`);
      }
      chartCache.set(key, data);
    }
    drawChart(body, chartCache.get(key), item);
  } catch (e) {
    body.replaceChildren(el('p', { class: 'chart-note' }, `グラフを出せませんでした: ${e.message}`));
  }
}

// 目盛りの間隔(1・2・2.5・5 × 10のべき)
function niceStep(range, count) {
  const raw = range / count;
  const p = 10 ** Math.floor(Math.log10(raw));
  return [1, 2, 2.5, 5, 10].map((m) => m * p).find((s) => s >= raw);
}

function drawChart(body, data, item) {
  const bars = data.bars;
  if (bars.length < 2) {
    body.replaceChildren(el('p', { class: 'chart-note' }, 'グラフにできるデータがありません。'));
    return;
  }
  const info = el('p', { class: 'chart-info' });
  const frame = el('div', { class: 'chart-frame' });
  const periods = Object.keys(data.ma).map(Number);
  const maName = MA_LABEL[data.interval];
  const prev = data.interval === '1wk' ? '前の週' : '前の日';
  const legend = el('ul', { class: 'chart-legend' },
    el('li', {}, el('i', { class: 'sw rise' }), `${prev}より上がった`),
    el('li', {}, el('i', { class: 'sw fall' }), `${prev}より下がった`),
    el('li', {}, el('i', { class: 'sw up' }), '中抜き＝始値より上で終わった'),
    el('li', {}, el('i', { class: 'sw down' }), '塗りつぶし＝始値より下で終わった'),
    periods.map((n, i) => el('li', {}, el('i', { class: `sw line ma${i}` }), maName(n))),
    el('li', {}, el('b', { class: 'mk' }, '▲'), 'ゴールデンクロス', el('b', { class: 'mk' }, '▼'), `デッドクロス(${maName(periods[0])}と${maName(periods[1])})`),
    el('li', {}, el('i', { class: 'sw band' }), '買いの範囲'),
    el('li', {}, el('i', { class: 'sw dash gain' }), '利確'),
    el('li', {}, el('i', { class: 'sw dash loss' }), '損切り'));
  body.replaceChildren(info, frame, legend);

  const render = () => {
    const W = Math.max(300, frame.clientWidth);
    const H = W < 500 ? 260 : 320;
    const L = 6;
    const R = 84;
    const T = 10;
    const priceH = Math.round((H - 30) * 0.74);
    const volTop = T + priceH + 10;
    const volH = H - 30 - priceH - 10;
    const plotW = W - L - R;
    const step = plotW / bars.length;
    const bodyW = Math.max(1, Math.min(10, step * 0.62));
    const x = (i) => L + step * (i + 0.5);

    const plan = [item.buy.low, item.buy.high, item.target, item.stop];
    const maValues = periods.flatMap((n) => data.ma[n]).filter((v) => v != null);
    let lo = Math.min(...bars.map((b) => b.l), ...plan, ...maValues);
    let hi = Math.max(...bars.map((b) => b.h), ...plan, ...maValues);
    const pad = (hi - lo) * 0.06;
    lo -= pad;
    hi += pad;
    const y = (v) => T + ((hi - v) / (hi - lo)) * priceH;
    const vMax = Math.max(...bars.map((b) => b.v)) || 1;
    const vy = (v) => volTop + volH - (v / vMax) * volH;

    const parts = [];
    // 右の目盛りには計画の値段を優先して出し、重ならないように上下にずらす
    const planLabels = [
      { v: item.target, text: '利確', cls: 'gain' },
      { v: item.buy.high, text: '買い', cls: 'buy' },
      { v: item.stop, text: '損切り', cls: 'loss' },
    ].map((p) => ({ ...p, y: y(p.v) })).sort((p, q) => p.y - q.y);
    for (let i = 1; i < planLabels.length; i++) {
      planLabels[i].y = Math.max(planLabels[i].y, planLabels[i - 1].y + 14);
    }
    const tick = niceStep(hi - lo, 4);
    for (let v = Math.ceil(lo / tick) * tick; v <= hi; v += tick) {
      parts.push(`<line class="grid" x1="${L}" x2="${L + plotW}" y1="${y(v)}" y2="${y(v)}"/>`);
      if (planLabels.every((p) => Math.abs(p.y - y(v)) >= 14)) {
        parts.push(`<text class="axis" x="${W - R + 6}" y="${y(v) + 4}">${Math.round(v).toLocaleString('ja-JP')}</text>`);
      }
    }
    // 買いの範囲・利確・損切り
    const bandTop = y(item.buy.high);
    const bandH = Math.max(2, y(item.buy.low) - bandTop);
    parts.push(`<rect class="band" x="${L}" y="${bandTop}" width="${plotW}" height="${bandH}"/>`);
    parts.push(`<line class="plan gain" x1="${L}" x2="${L + plotW}" y1="${y(item.target)}" y2="${y(item.target)}"/>`);
    parts.push(`<line class="plan loss" x1="${L}" x2="${L + plotW}" y1="${y(item.stop)}" y2="${y(item.stop)}"/>`);
    // 出来高
    for (let i = 0; i < bars.length; i++) {
      parts.push(`<rect class="vol" x="${x(i) - bodyW / 2}" y="${vy(bars[i].v)}" width="${bodyW}" height="${volTop + volH - vy(bars[i].v)}"/>`);
    }
    parts.push(`<line class="base" x1="${L}" x2="${L + plotW}" y1="${volTop + volH}" y2="${volTop + volH}"/>`);
    // ローソク足。色は前の足の終値より上がったか(赤)・下がったか(青)、中抜き・塗りつぶしは始値と終値のどちらが高いか
    for (let i = 0; i < bars.length; i++) {
      const b = bars[i];
      const up = b.c >= b.o;
      const move = b.pc == null || b.c === b.pc ? 'flat' : b.c > b.pc ? 'rise' : 'fall';
      const top = y(Math.max(b.o, b.c));
      const h = Math.max(1, y(Math.min(b.o, b.c)) - top);
      parts.push(`<line class="wick ${move}" x1="${x(i)}" x2="${x(i)}" y1="${y(b.h)}" y2="${y(b.l)}"/>`);
      parts.push(`<rect class="${up ? 'up' : 'down'} ${move}" x="${x(i) - bodyW / 2}" y="${top}" width="${bodyW}" height="${h}"/>`);
    }
    // 移動平均線(値の無い前半は描かない)
    periods.forEach((n, k) => {
      let d = '';
      let pen = false;
      data.ma[n].forEach((v, i) => {
        if (v == null) {
          pen = false;
          return;
        }
        d += `${pen ? 'L' : 'M'}${x(i).toFixed(1)},${y(v).toFixed(1)}`;
        pen = true;
      });
      if (d) {
        parts.push(`<path class="ma ma${k}" d="${d}"/>`);
      }
    });
    // クロスの印(ゴールデンは足の下に▲、デッドは上に▼)
    for (const c of data.crosses) {
      const b = bars[c.index];
      const cx = x(c.index);
      parts.push(c.kind === 'gc'
        ? `<path class="mk-gc" d="M${cx},${Math.min(y(b.l) + 4, T + priceH - 9)} l5,9 h-10 z"/>`
        : `<path class="mk-dc" d="M${cx},${y(b.h) - 4} l5,-9 h-10 z"/>`);
    }
    // 計画の値段(右の目盛りの位置。ずらしたときは線から引き出し線を引く)
    for (const p of planLabels) {
      parts.push(`<path class="plan-lead ${p.cls}" d="M${L + plotW},${y(p.v)} L${W - R + 4},${p.y}"/>`);
      parts.push(`<text class="plan-label" x="${W - R + 6}" y="${p.y + 4}">${p.text} ${Math.round(p.v).toLocaleString('ja-JP')}</text>`);
    }
    // 月の目盛り
    let prevMonth = null;
    for (let i = 0; i < bars.length; i++) {
      const [yy, mm] = bars[i].date.split('-').map(Number);
      if (mm !== prevMonth && prevMonth !== null && (data.interval === '1d' || mm % 3 === 1)) {
        const text = data.interval === '1d' ? `${mm}月` : `${String(yy).slice(2)}/${mm}`;
        parts.push(`<line class="grid" x1="${x(i)}" x2="${x(i)}" y1="${T}" y2="${volTop + volH}"/>`);
        parts.push(`<text class="axis" x="${x(i)}" y="${H - 8}" text-anchor="middle">${text}</text>`);
      }
      prevMonth = mm;
    }
    parts.push(`<line class="cursor" x1="0" x2="0" y1="${T}" y2="${volTop + volH}" visibility="hidden"/>`);
    parts.push(`<rect class="hit" x="${L}" y="${T}" width="${plotW}" height="${volTop + volH - T}"/>`);

    const last = bars[bars.length - 1];
    frame.innerHTML = `<svg width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" role="img" aria-label="${escapeAttr(item.name)}の${INTERVAL_LABEL[data.interval]}。${bars[0].date}から${last.date}まで。最新の終値 ${Math.round(last.c)}円">${parts.join('')}</svg>`;

    // なぞると、その足の値段を上に出す
    const svg = frame.querySelector('svg');
    const cursor = svg.querySelector('.cursor');
    const showInfo = (i) => {
      const b = bars[i];
      const mas = periods.map((n) => (data.ma[n][i] != null ? `${maName(n)} ${price(data.ma[n][i])}` : null)).filter(Boolean);
      const diff = b.pc == null ? null : b.c - b.pc;
      info.replaceChildren(
        el('b', {}, `${formatDate(b.date)}${data.interval === '1wk' ? 'の週' : ''}`),
        ` 始 ${price(b.o)}・高 ${price(b.h)}・安 ${price(b.l)}・終 `, el('b', {}, price(b.c)),
        ...(diff == null ? [] : [` ／ ${data.interval === '1wk' ? '前週比' : '前日比'} `, el('b', { class: tone(diff) }, `${signed(diff)}（${signed((diff / b.pc) * 100, '%')}）`)]),
        ` ／ 出来高 ${Math.round(b.v).toLocaleString('ja-JP')}株`,
        mas.length ? ` ／ ${mas.join('・')}` : '');
    };
    showInfo(bars.length - 1);
    svg.querySelector('.hit').addEventListener('pointermove', (e) => {
      const r = svg.getBoundingClientRect();
      const i = Math.min(bars.length - 1, Math.max(0, Math.floor((e.clientX - r.left - L) / step)));
      cursor.setAttribute('x1', x(i));
      cursor.setAttribute('x2', x(i));
      cursor.setAttribute('visibility', 'visible');
      showInfo(i);
    });
    svg.querySelector('.hit').addEventListener('pointerleave', () => {
      cursor.setAttribute('visibility', 'hidden');
      showInfo(bars.length - 1);
    });
  };
  render();
  // 幅が変わったら描き直す
  let lastW = frame.clientWidth;
  const ro = new ResizeObserver(() => {
    if (!frame.isConnected) {
      ro.disconnect();
    } else if (frame.clientWidth !== lastW) {
      lastW = frame.clientWidth;
      render();
    }
  });
  ro.observe(frame);
}

const monthsText = (months) => months.map((m) => `${m}月`).join('・');

function dividendText(item) {
  const div = item.div;
  const parts = [item.dividendYen > 0 ? `100株で年 ${yen(item.dividendYen)}（予想）` : '予想なし'];
  if (div && div.months.length > 0) {
    parts.push(`権利確定 ${monthsText(div.months)}末（次は ${div.next}月末）`);
  } else if (div == null) {
    parts.push('権利確定月は取得できませんでした');
  }
  return parts.join(' ／ ');
}

function yutaiBody(y) {
  if (y == null) {
    return el('dd', {}, '取得できませんでした（「優待のページ」で確認してください）');
  }
  if (y.none) {
    return el('dd', {}, 'なし');
  }
  const parts = [y.kinds || '内容あり'];
  if (y.lastDates.length > 0) {
    parts.push(`権利確定 ${monthsText(y.months)}（権利付き最終日 ${y.lastDates.map(formatDate).join('・')}）`);
  }
  if (y.minShares > 100) {
    parts.push(`⚠ ${y.minShares.toLocaleString('ja-JP')}株以上から（100株では対象外）`);
  } else if (y.minShares === 100) {
    parts.push('100株から');
  }
  const cont = y.continuity.lines.length > 0
    ? el('div', { class: 'cont' },
      el('b', {}, '⏳ ', term('継続保有の条件', 'continuity'), `あり${y.continuity.span ? `（${y.continuity.span}）` : ''}`),
      el('ul', {}, y.continuity.lines.map((t) => el('li', {}, t))))
    : null;
  const detail = el('details', {},
    el('summary', {}, '優待の内容を見る'),
    el('div', { class: 'yutai' }, y.lines.map((l) => el('p', { class: l.kind }, l.kind === 'row' ? [el('b', {}, l.shares), ` ${l.text}`] : l.text))));
  return el('dd', {}, parts.join(' ／ '), cont, detail);
}

function perks(item) {
  return el('dl', { class: 'perks' },
    el('div', {}, el('dt', {}, '💴 ', term('配当', 'rights')), el('dd', {}, dividendText(item))),
    el('div', {}, el('dt', {}, '🎁 ', term('優待', 'yutai')), yutaiBody(item.yutai)));
}

// 並べ替え。値の無いもの(赤字で PER が無いなど)は向きに関係なく最後。同じ値なら買いスコアの順のまま
function byValue(get, dir) {
  return (x, y) => {
    const a = get(x);
    const b = get(y);
    if (a == null || b == null) {
      return (a == null) - (b == null);
    }
    return (a - b) * dir;
  };
}

const positive = (v) => (v > 0 ? v : null);
const SORTS = {
  score: () => 0,
  costAsc: byValue((i) => i.cost, 1),
  costDesc: byValue((i) => i.cost, -1),
  pbrAsc: byValue((i) => positive(i.stats.pbr), 1),
  pbrDesc: byValue((i) => positive(i.stats.pbr), -1),
  perAsc: byValue((i) => positive(i.stats.per), 1),
  perDesc: byValue((i) => positive(i.stats.per), -1),
};

let current = null;
// 開いているスナップショット(最新の結果を出しているときは null)
let viewing = null;

// 銘柄カードの一覧(短期・長期)と、平均割れの表のどちらを出すか
function showPane(avg) {
  $('avgview').hidden = !avg;
  $('list').hidden = avg;
  if (avg) {
    for (const id of ['status', 'market', 'snapview', 'sortbar']) {
      $(id).hidden = true;
    }
  }
}

function render(r, snap = null) {
  current = r;
  viewing = snap;
  showPane(false);
  renderStatus(r);
  renderSnapView();
  renderList();
}

function renderList() {
  const list = $('list');
  $('sortbar').hidden = current.items.length === 0;
  if (current.items.length === 0) {
    list.replaceChildren(el('li', { class: 'empty' }, '条件に合う銘柄がありませんでした。予算の幅を広げるか、絞り込みの条件をゆるめてみてください。'));
    return;
  }
  // サーバは買いスコアの順で返すので、それを元に並べ替える
  const items = [...current.items].sort(SORTS[$('sort').value] || SORTS.score);
  list.replaceChildren(...items.map((item, i) => card(item, i, current.mode)));
}

// ---------------------------------------------------------------- スナップショット

async function saveSnapshot() {
  const button = $('snap-save');
  const msg = $('snap-msg');
  button.disabled = true;
  try {
    const res = await fetch('/api/snapshots', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ result: current }) });
    const body = await res.json();
    if (!res.ok) {
      throw new Error(body.error || `エラー(${res.status})`);
    }
    msg.textContent = `保存しました（${formatTime(body.savedAt)}）。右上の「📸 スナップショット」から開けます`;
  } catch (e) {
    msg.textContent = `保存できませんでした: ${e.message}`;
    button.disabled = false;
  }
}

async function openSnapshots() {
  const list = $('s-list');
  list.replaceChildren(el('li', { class: 'hint' }, '読み込んでいます…'));
  if (!$('snaps').open) {
    $('snaps').showModal();
  }
  try {
    const res = await fetch('/api/snapshots');
    const snaps = await res.json();
    if (!res.ok) {
      throw new Error(snaps.error || `エラー(${res.status})`);
    }
    list.replaceChildren(...(snaps.length > 0
      ? snaps.map(snapRow)
      : [el('li', { class: 'hint' }, 'まだ保存していません。調べた結果の上にある「📸 この結果を保存」で保存できます。')]));
  } catch (e) {
    list.replaceChildren(el('li', { class: 'hint' }, `読み込めませんでした: ${e.message}`));
  }
}

function snapRow(s) {
  const open = el('button', { type: 'button', class: 'snap-button' }, '開いて今と比べる');
  const del = el('button', { type: 'button', class: 'snap-button quiet' }, '削除');
  const conds = filterText(s.filter);
  const row = el('li', {},
    el('div', { class: 's-main' },
      el('b', {}, `${formatTime(s.savedAt)} に保存`),
      ` ${MODE_LABEL[s.mode]}・100株で${yen(s.budget.min)}〜${yen(s.budget.max)}・${s.count}銘柄`,
      conds ? el('small', {}, `絞り込み: ${conds}`) : null,
      el('small', {}, `${s.names.join('・')}${s.count > s.names.length ? ' など' : ''}`)),
    el('div', { class: 's-actions' }, open, del));
  open.addEventListener('click', () => openSnapshot(s.id));
  del.addEventListener('click', async () => {
    if (!confirm(`${formatTime(s.savedAt)} に保存したスナップショットを削除しますか？`)) {
      return;
    }
    const res = await fetch(`/api/snapshots/${s.id}`, { method: 'DELETE' });
    if (res.ok) {
      row.remove();
      if (viewing && viewing.id === s.id) {
        backToLatest();
      }
    }
  });
  return row;
}

async function openSnapshot(id) {
  $('snaps').close();
  try {
    const res = await fetch(`/api/snapshots/${id}`);
    const snap = await res.json();
    if (!res.ok) {
      throw new Error(snap.error || `エラー(${res.status})`);
    }
    render(snap.result, snap);
    $('snapview').scrollIntoView({ block: 'start' });
    const cres = await fetch(`/api/snapshots/${id}/compare`);
    const cmp = await cres.json();
    // 比べている間に別の結果に切り替えていたら出さない
    if (viewing !== snap) {
      return;
    }
    if (!cres.ok) {
      $('snap-compare').textContent = `その後の値動きを取れませんでした: ${cmp.error || cres.status}`;
      return;
    }
    renderCompare(snap, cmp);
  } catch (e) {
    showError(`スナップショットを開けませんでした: ${e.message}`);
  }
}

function backToLatest() {
  const last = load(LAST_KEY);
  if (last && Array.isArray(last.items)) {
    render(last);
    return;
  }
  current = null;
  viewing = null;
  for (const id of ['status', 'market', 'snapview', 'sortbar']) {
    $(id).hidden = true;
  }
  $('list').replaceChildren();
}

function renderSnapView() {
  const v = $('snapview');
  $('snap-save').hidden = Boolean(viewing);
  $('snap-save').disabled = false;
  $('snap-msg').textContent = '';
  if (!viewing) {
    v.hidden = true;
    v.replaceChildren();
    return;
  }
  const back = el('button', { type: 'button', class: 'snap-button' }, '最新の結果に戻る');
  back.addEventListener('click', backToLatest);
  v.replaceChildren(
    el('div', { class: 'snap-head' },
      el('p', {}, el('b', {}, `📸 ${formatTime(viewing.savedAt)} に保存したスナップショット`), 'を表示しています。銘柄カードは保存したときの内容で、チャートだけ今のデータです。'),
      back),
    el('div', { id: 'snap-compare', class: 'hint' }, 'その後の値動きを調べています…'));
  v.hidden = false;
}

function hitText(hit, item) {
  if (!hit) {
    return `まだ（利確 ${price(item.target)}・損切り ${price(item.stop)}）`;
  }
  const when = `（${formatDate(hit.date)}）`;
  return { target: `🎯 利確に届いた${when}`, stop: `🛑 損切りに届いた${when}`, both: `⚡ 同じ日に両方${when}` }[hit.kind];
}

// 保存したときと今を比べる表。最新の検索結果(同じ期間で、保存より後に調べたもの)があれば、今の順位と新顔も出す
function renderCompare(snap, cmp) {
  const items = snap.result.items;
  const live = load(LAST_KEY);
  const useLive = Boolean(live && Array.isArray(live.items) && live.mode === snap.result.mode && live.at > snap.result.at);
  const liveRank = new Map(useLive ? live.items.map((i, k) => [i.code, k + 1]) : []);
  const rows = items.map((item, k) => ({ item, rank: k + 1, c: cmp.rows[k] && !cmp.rows[k].error ? cmp.rows[k] : null }));
  const ok = rows.filter((r) => r.c);
  const count = (kind) => ok.filter((r) => (r.c.hit ? r.c.hit.kind : null) === kind).length;
  const avg = ok.length > 0 ? Math.round((ok.reduce((sum, r) => sum + r.c.change, 0) / ok.length) * 10) / 10 : null;
  const summary = el('p', { class: 'snap-summary' },
    `${ok.length}銘柄のその後: `,
    el('b', {}, `🎯 利確に届いた ${count('target')}`), '・', el('b', {}, `🛑 損切りに届いた ${count('stop')}`),
    count('both') > 0 ? `・同じ日に両方 ${count('both')}` : '',
    `・まだどちらも ${count(null)}`,
    avg == null ? '' : [' ／ 値段の変化の平均 ', el('b', { class: tone(avg) }, signed(avg, '%'))]);
  const head = ['順位', '銘柄', '保存したとき', '今', '変化', 'その後の高値 / 安値', '利確・損切り', useLive ? '最新の検索' : null].filter(Boolean);
  const table = el('div', { class: 'snap-table-wrap' }, el('table', { class: 'snap-table' },
    el('thead', {}, el('tr', {}, head.map((h) => el('th', {}, h)))),
    el('tbody', {}, rows.map(({ item, rank, c }) => el('tr', {},
      el('td', { class: 'num' }, `${rank}`),
      el('td', {}, el('b', {}, item.name), el('small', {}, ` ${item.code}`)),
      el('td', { class: 'num' }, `${price(item.price)}円`, item.priceDate ? el('small', {}, `（${formatDate(item.priceDate)}）`) : null),
      c
        ? [
          el('td', { class: 'num' }, `${price(c.price)}円`, el('small', {}, `（${formatDate(c.date)}）`)),
          el('td', { class: `num ${tone(c.change)}` }, signed(c.change, '%')),
          el('td', { class: 'num' }, c.high == null ? '—' : `${price(c.high)} / ${price(c.low)}`),
          el('td', {}, hitText(c.hit, item)),
        ]
        : el('td', { colspan: '4', class: 'hint' }, '値動きを取れませんでした'),
      useLive ? el('td', {}, liveRank.has(item.code) ? `${liveRank.get(item.code)}位` : '圏外') : null)))));
  const fresh = useLive ? live.items.filter((i) => !items.some((s) => s.code === i.code)) : [];
  const notes = [
    el('p', { class: 'hint' }, '「その後」は保存したときの株価の日より後の日足で見ています。1日の中で利確と損切りのどちらが先に来たかは分からないので、同じ日に両方届いたときは「同じ日に両方」と出します。'),
    useLive ? el('p', { class: 'hint' }, `「最新の検索」は ${formatTime(live.at)} に調べた結果（そのときの条件）での順位です。`) : null,
    fresh.length > 0 ? el('p', {}, el('b', {}, '🆕 最新の検索で新しく入った銘柄: '), fresh.map((i) => `${i.name}(${i.code})`).join('・')) : null,
  ];
  $('snap-compare').className = 'snap-compare';
  $('snap-compare').replaceChildren(summary, table, ...notes.filter(Boolean));
}

function showError(message) {
  showPane(false);
  const s = $('status');
  s.className = 'status error';
  s.textContent = message;
  s.hidden = false;
}

// ---------------------------------------------------------------- 平均割れ

const ym = (month) => `${month.slice(0, 4)}年${Number(month.slice(5, 7))}月`;
const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const oku = (v) => `${Math.round(v / 1e8).toLocaleString('ja-JP')}億円`;

const AVG_SORTS = {
  gap: byValue((i) => i.gap, -1),
  perAsc: byValue((i) => positive(i.stats.per), 1),
  pbrAsc: byValue((i) => positive(i.stats.pbr), 1),
  costAsc: byValue((i) => i.cost, 1),
  mcapDesc: byValue((i) => i.marketCap, -1),
};
const AVG_SORT_LABEL = { gap: '平均より安い順', perAsc: 'PER が低い順', pbrAsc: 'PBR が低い順', costAsc: '100株の金額が安い順', mcapDesc: '時価総額が大きい順' };
const AVG_GAPS = [0, 10, 20, 30, 50];

let avgResult = null;
let avgShown = AVG_PAGE;
let avgPolling = false;

function setRunBusy(busy) {
  const button = $('run');
  button.disabled = busy;
  button.textContent = busy ? '調べています…' : runLabel();
}

function avgFilterText(f = {}) {
  const conds = [
    f.per != null ? `PER ${f.per}倍以下` : null,
    f.pbr != null ? `PBR ${f.pbr}倍未満` : null,
    f.mcap ? `時価総額 ${oku(f.mcap)}以上` : null,
    f.budget ? `100株で${yen(f.budget.min)}〜${yen(f.budget.max)}` : null,
  ].filter(Boolean);
  return conds.length > 0 ? conds.join('・') : null;
}

function showAvgMessage(message, isError = true) {
  showPane(true);
  $('avgview').replaceChildren(el('p', { class: isError ? 'status error' : 'status' }, message));
}

function renderAvgProgress(job) {
  showPane(true);
  const cancel = el('button', { type: 'button', class: 'snap-button quiet' }, 'やめる');
  cancel.addEventListener('click', async () => {
    cancel.disabled = true;
    await fetch('/api/avgscan/cancel', { method: 'POST' }).catch(() => {});
  });
  const text = job.phase === 'screen'
    ? `銘柄の一覧を取っています… ${job.done.toLocaleString('ja-JP')} / ${(job.total || 0).toLocaleString('ja-JP')}`
    : `月足(毎月の終値)を取っています… ${job.done.toLocaleString('ja-JP')} / ${job.total.toLocaleString('ja-JP')} 銘柄（今日すでに取って保存してあった分 ${job.cached.toLocaleString('ja-JP')}）`;
  const years = job.req ? `過去${job.req.years}年の平均と比べます。` : '';
  $('avgview').replaceChildren(el('div', { class: 'avg-progress' },
    el('p', {}, el('b', {}, '📉 平均割れを調べています'), ` ${years}全部の銘柄だと数分かかります（同じ日の2回目からは保存した月足を使うので早くなります）。`),
    el('p', {}, text),
    el('progress', { max: String(job.total || 1), value: String(job.done || 0) }),
    cancel));
}

// サーバの調査が終わるまで1秒ごとに進み具合を聞く
async function pollAvg() {
  if (avgPolling) {
    return;
  }
  avgPolling = true;
  setRunBusy(true);
  try {
    for (;;) {
      const res = await fetch('/api/avgscan');
      const job = await res.json();
      if (job.state === 'running') {
        renderAvgProgress(job);
        await wait(1000);
        continue;
      }
      if (job.state === 'done' && job.result) {
        save(AVG_KEY, job.result);
        avgShown = AVG_PAGE;
        renderAvg(job.result);
      } else if (job.state === 'error') {
        showAvgMessage(job.error);
      } else if (job.state === 'cancelled') {
        showAvgMessage('途中でやめました。', false);
      }
      break;
    }
  } catch {
    showAvgMessage('サーバにつながりませんでした。株レーダーが起動しているか確認してください。');
  } finally {
    avgPolling = false;
    setRunBusy(false);
  }
}

async function startAvg(f) {
  const body = {
    years: Number(f.years),
    per: f.per || null,
    pbr: f.pbr || null,
    mcap: f.mcap ? Number(f.mcap) : null,
    budget: f.avgBudget ? { min: f.min * 10000, max: f.max * 10000 } : null,
  };
  try {
    const res = await fetch('/api/avgscan', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    const j = await res.json();
    // 409 は別の調査の途中。その進み具合を出す
    if (!res.ok && res.status !== 409) {
      showAvgMessage(j.error || `エラー(${res.status})`);
      return;
    }
  } catch {
    showAvgMessage('サーバにつながりませんでした。株レーダーが起動しているか確認してください。');
    return;
  }
  await pollAvg();
}

function avgStatus(r) {
  const parts = [
    `${formatTime(r.at)} に調べました（過去${r.years}年 = ${ym(r.window.from)}〜${ym(r.window.to)} の月末終値の平均と、今の株価を比べています）`,
    r.dataTime ? `株価は ${formatTime(r.dataTime)} 時点` : null,
    avgFilterText(r.filter) ? `絞り込み: ${avgFilterText(r.filter)}` : null,
    `東証の普通株 ${r.stocks.toLocaleString('ja-JP')} 銘柄（REIT・優先株などを除く）→ 絞り込みで ${r.matched.toLocaleString('ja-JP')} → 月足を見た ${r.fetched.toLocaleString('ja-JP')}（保存済みを使った ${r.cached.toLocaleString('ja-JP')}）`,
    `平均より下 ${r.items.length.toLocaleString('ja-JP')}・平均以上 ${r.above.toLocaleString('ja-JP')}・データ不足 ${r.insufficient.length.toLocaleString('ja-JP')}`,
    r.failed ? `取得失敗 ${r.failed}` : null,
  ];
  return parts.filter(Boolean).join(' ／ ');
}

function avgRow(item, rank, years) {
  const watch = el('button', { type: 'button', class: 'link-button' }, '🔔 見張る');
  const avg = Math.round(item.avg);
  const low = Math.floor(item.low);
  watch.addEventListener('click', () => openWatches({
    code: item.code,
    name: item.name,
    suggest: [
      { label: `過去${years}年の平均 ${price(avg)}円以上`, op: 'above', price: avg, note: `${years}年平均` },
      { label: `過去${years}年の安値 ${price(low)}円以下`, op: 'below', price: low, note: `${years}年安値` },
    ],
  }));
  const stat = (v, unit) => (v == null ? '—' : `${v}${unit}`);
  return el('tr', {},
    el('td', { class: 'num' }, `${rank}`),
    el('td', {}, el('a', { href: `https://finance.yahoo.co.jp/quote/${item.code}.T`, target: '_blank', rel: 'noopener' }, item.name), el('small', {}, ` ${item.code}`)),
    el('td', { class: 'num' }, `${price(item.price)}円`, item.priceDate ? el('small', {}, `（${formatDate(item.priceDate)}）`) : null),
    el('td', { class: 'num' }, `${price(item.avg)}円`),
    el('td', { class: 'num' }, el('b', {}, `${item.gap.toLocaleString('ja-JP', { maximumFractionDigits: 1 })}%安い`)),
    el('td', { class: 'num' }, `${price(item.low)} 〜 ${price(item.high)}`),
    el('td', { class: 'num' }, yen(item.cost)),
    el('td', { class: 'num' }, stat(item.stats.per, '倍')),
    el('td', { class: 'num' }, stat(item.stats.pbr, '倍')),
    el('td', { class: 'num' }, stat(item.stats.dividend, '%')),
    el('td', {}, watch));
}

function renderAvg(r) {
  avgResult = r;
  showPane(true);
  const view = load(AVG_VIEW_KEY) || {};
  const sort = Object.hasOwn(AVG_SORTS, view.sort) ? view.sort : 'gap';
  const minGap = AVG_GAPS.includes(view.minGap) ? view.minGap : 0;
  const rows = r.items.filter((i) => i.gap >= minGap).sort(AVG_SORTS[sort]);

  const gapSelect = el('select', {}, AVG_GAPS.map((g) => el('option', { value: String(g) }, g === 0 ? '平均より下ならすべて' : `平均より${g}%以上安い`)));
  gapSelect.value = String(minGap);
  const sortSelect = el('select', {}, Object.entries(AVG_SORT_LABEL).map(([k, label]) => el('option', { value: k }, label)));
  sortSelect.value = sort;
  const onChange = () => {
    save(AVG_VIEW_KEY, { sort: sortSelect.value, minGap: Number(gapSelect.value) });
    avgShown = AVG_PAGE;
    renderAvg(avgResult);
  };
  gapSelect.addEventListener('change', onChange);
  sortSelect.addEventListener('change', onChange);

  const notes = [
    r.stopped ? el('p', { class: 'status error' }, `⚠ ${r.stopped}（見られなかった ${r.skipped} 銘柄は表に入っていません）`) : null,
    r.limited ? el('p', { class: 'hint' }, `動作確認用に、月足を取る銘柄を時価総額の大きい ${r.limited} 銘柄までにしています。`) : null,
  ];
  const head = ['順位', '銘柄', '今の株価', `過去${r.years}年の平均`, '平均より', `${r.years}年の安値〜高値(月末終値)`, '100株で', 'PER', 'PBR', '配当', ''];
  const table = rows.length > 0
    ? el('div', { class: 'snap-table-wrap' }, el('table', { class: 'snap-table avg-table' },
      el('thead', {}, el('tr', {}, head.map((h, k) => el('th', {}, k === 4 ? term(h, 'longavg') : h)))),
      el('tbody', {}, rows.slice(0, avgShown).map((item, k) => avgRow(item, k + 1, r.years)))))
    : el('p', { class: 'empty' }, '条件に合う銘柄がありませんでした。');
  let more = null;
  if (rows.length > avgShown) {
    more = el('button', { type: 'button', class: 'snap-button' }, `もっと見る（あと ${(rows.length - avgShown).toLocaleString('ja-JP')} 銘柄）`);
    more.addEventListener('click', () => {
      avgShown += AVG_PAGE;
      renderAvg(avgResult);
    });
  }
  const short = r.insufficient.length > 0
    ? el('details', { class: 'avg-short' },
      el('summary', {}, `データ不足で比べていない ${r.insufficient.length.toLocaleString('ja-JP')} 銘柄（上場して${r.years}年たっていない・月足が足りない）`),
      el('p', {}, r.insufficient.map((i) => `${i.name}(${i.code}${i.first ? `・${ym(i.first)}から` : ''})`).join('、')))
    : null;
  $('avgview').replaceChildren(...[
    el('p', { class: 'status' }, avgStatus(r)),
    ...notes,
    el('div', { class: 'sortbar' },
      el('label', {}, '表示'), gapSelect,
      el('label', {}, '並び順'), sortSelect,
      el('span', { class: 'hint' }, `${rows.length.toLocaleString('ja-JP')} 銘柄`)),
    table,
    more,
    short,
    el('p', { class: 'hint' }, `「平均」は、今月を除く直近${r.years}年(${r.years * 12}か月)の毎月の最後の取引日の終値を足して月数で割った値です(株式分割は調整済み・配当は調整なし)。「◯%安い」は (1 − 今の株価 ÷ 平均) × 100。安いのには業績が悪くなったなどの理由があることも多いので(割安の罠)、決算と PER・PBR・ROE も確かめてください。株式併合や、増資で株数が大きく増えた銘柄は、Yahoo のデータのままだと昔の値段が今と比べものにならないことがあります(安値〜高値の幅が極端なものは注意)。`),
  ].filter(Boolean));
}

// ---------------------------------------------------------------- 見張り

// カードや表から開いたときの銘柄(コードを打ち替えたら消す)
let watchDraft = null;

const unixTime = (sec) => formatTime(new Date(sec * 1000).toISOString());
const condText = (w) => `${price(w.price)}円${w.op === 'above' ? '以上' : '以下'}になったら`;

// 今の画面の結果から銘柄の名前を探す(コードだけ打ったとき用。無ければサーバが英語名を入れる)
function knownName(code) {
  const lists = [current && current.items, (load(LAST_KEY) || {}).items, avgResult && avgResult.items];
  for (const list of lists) {
    const hit = Array.isArray(list) && list.find((i) => i.code === code);
    if (hit) {
      return hit.name;
    }
  }
  return '';
}

function renderSuggest(draft) {
  const box = $('w-suggest');
  if (!draft || !draft.suggest) {
    box.replaceChildren();
    return;
  }
  box.replaceChildren(el('span', { class: 'hint' }, 'よく使う値段: '), ...draft.suggest.map((s) => {
    const b = el('button', { type: 'button', class: 'chip' }, s.label);
    b.addEventListener('click', () => {
      $('w-price').value = s.price;
      $('w-op').value = s.op;
      $('w-note').value = s.note || '';
    });
    return b;
  }));
}

function watchRow(w) {
  const del = el('button', { type: 'button', class: 'snap-button quiet' }, '削除');
  const actions = [del];
  let state;
  if (w.state === 'notified') {
    const resume = el('button', { type: 'button', class: 'snap-button' }, '再開');
    resume.addEventListener('click', async () => {
      const res = await fetch(`/api/watches/${w.id}/resume`, { method: 'POST' });
      if (res.ok) {
        refreshWatches();
      }
    });
    actions.unshift(resume);
    state = el('small', { class: 'w-done' }, `🔔 ${formatTime(w.notifiedAt)} に知らせました（${price(w.hitPrice)}円${w.hitTime ? `・${unixTime(w.hitTime)} 時点` : ''}）。見張りは止めています`);
  } else {
    state = el('small', {}, '👀 見張り中',
      w.lastPrice ? `（今 ${price(w.lastPrice)}円${w.lastTime ? `・${unixTime(w.lastTime)} 時点` : ''}）` : '',
      w.priceError ? el('span', { class: 'w-warn' }, ` ⚠ ${w.priceError}`) : null);
  }
  const row = el('li', {},
    el('div', { class: 's-main' },
      el('b', {}, `${w.name || w.code}`), el('span', { class: 'code' }, ` ${w.code}`), ` ／ ${condText(w)}`,
      w.note ? el('small', {}, `メモ: ${w.note}`) : null,
      state,
      w.error ? el('small', { class: 'w-warn' }, `⚠ 条件を満たしましたが、知らせられませんでした: ${w.error}（次の確認でまた送ります）`) : null),
    el('div', { class: 's-actions' }, actions));
  del.addEventListener('click', async () => {
    if (!confirm(`${w.name || w.code} の見張り（${condText(w)}）を削除しますか？`)) {
      return;
    }
    const res = await fetch(`/api/watches/${w.id}`, { method: 'DELETE' });
    if (res.ok) {
      row.remove();
    }
  });
  return row;
}

function renderWatches(data) {
  const l = data.line;
  const m = data.monitor;
  const lineText = l.dryRun
    ? 'LINE: 送らないモード（STOCK_RADAR_LINE_DRY_RUN=1。サーバのログに出すだけ）'
    : l.configured ? 'LINE: 設定済み' : 'LINE: まだ設定していません。README の「LINE で知らせる」にある line-setup.ps1 で登録すると届くようになります';
  const when = m.anytime
    ? '確認用モード（STOCK_RADAR_WATCH_ANYTIME=1）: 取引時間の外でも見ています'
    : m.marketHours ? `今は取引時間中です。${m.minutes}分おきに確かめています` : `今は取引時間の外です。平日 9:00〜15:50 に${m.minutes}分おきに確かめます`;
  $('w-status').className = `w-status${l.configured || l.dryRun ? '' : ' warn'}`;
  $('w-status').replaceChildren(lineText, el('br'), when, m.lastRunAt ? `（最後に確かめた: ${formatTime(m.lastRunAt)}）` : '');
  const list = [...data.watches].sort((x, y) => (x.state === y.state ? y.createdAt.localeCompare(x.createdAt) : x.state === 'watching' ? -1 : 1));
  $('w-list').replaceChildren(...(list.length > 0
    ? list.map(watchRow)
    : [el('li', { class: 'hint' }, 'まだ見張っていません。上で銘柄コードと値段を入れるか、銘柄カードの「🔔 この銘柄を見張る」から登録できます。')]));
}

async function refreshWatches() {
  try {
    const res = await fetch('/api/watches');
    const data = await res.json();
    if (!res.ok) {
      throw new Error(data.error || `エラー(${res.status})`);
    }
    renderWatches(data);
  } catch (e) {
    $('w-list').replaceChildren(el('li', { class: 'hint' }, `読み込めませんでした: ${e.message}`));
  }
}

async function openWatches(draft = null) {
  if (draft) {
    watchDraft = draft;
    $('w-code').value = draft.code;
    $('w-price').value = '';
    $('w-note').value = '';
    $('w-name').textContent = draft.name || '';
  }
  renderSuggest(watchDraft);
  $('w-msg').textContent = '';
  if (!$('watches').open) {
    $('watches').showModal();
  }
  if (draft) {
    $('w-price').focus();
  }
  await refreshWatches();
}

async function addWatch(e) {
  e.preventDefault();
  const code = $('w-code').value.trim().toUpperCase();
  const name = watchDraft && watchDraft.code === code ? watchDraft.name : knownName(code);
  const body = { code, op: $('w-op').value, price: Number($('w-price').value), name, note: $('w-note').value };
  const msg = $('w-msg');
  try {
    const res = await fetch('/api/watches', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    const j = await res.json();
    if (!res.ok) {
      throw new Error(j.error || `エラー(${res.status})`);
    }
    msg.textContent = `${j.watch.name || j.watch.code} を見張り始めました（今 ${price(j.watch.lastPrice)}円）。${j.already ? '⚠ 今の値段がもう条件を満たしているので、取引時間中なら次の確認で知らせます。' : ''}`;
    $('w-price').value = '';
    $('w-note').value = '';
    await refreshWatches();
  } catch (err) {
    msg.textContent = `登録できませんでした: ${err.message}`;
  }
}

function initWatches() {
  $('open-watches').addEventListener('click', () => openWatches());
  $('w-close').addEventListener('click', () => $('watches').close());
  $('w-form').addEventListener('submit', addWatch);
  $('w-code').addEventListener('input', () => {
    const code = $('w-code').value.trim().toUpperCase();
    if (watchDraft && watchDraft.code !== code) {
      watchDraft = null;
      renderSuggest(null);
    }
    $('w-name').textContent = watchDraft ? watchDraft.name : knownName(code);
  });
  $('w-check').addEventListener('click', async () => {
    const b = $('w-check');
    b.disabled = true;
    try {
      const res = await fetch('/api/watches/check', { method: 'POST' });
      const data = await res.json();
      if (res.ok) {
        renderWatches(data);
        $('w-msg').textContent = data.hits > 0 ? `${data.hits}件が条件を満たしたので知らせました。` : '今の値段にしました。';
      } else {
        $('w-msg').textContent = data.error || `エラー(${res.status})`;
      }
    } finally {
      b.disabled = false;
    }
  });
  $('w-test').addEventListener('click', async () => {
    if (!confirm('LINE にテストのメッセージを1通送ります（LINE の無料プランは月200通まで）。送りますか？')) {
      return;
    }
    const res = await fetch('/api/line/test', { method: 'POST' });
    const j = await res.json().catch(() => ({}));
    $('w-msg').textContent = res.ok
      ? (j.dryRun ? '送らないモードなので、送るはずだった文をサーバのログに出しました。' : 'テストのメッセージを送りました。LINE に届いたか見てください。')
      : `送れませんでした: ${j.error || res.status}`;
  });
}

// ---------------------------------------------------------------- 操作

const FILTER_SELECTS = ['per', 'pbr', 'cross', 'gc', 'dc', 'years', 'mcap'];
const FILTER_CHECKS = ['gcSoon', 'dcSoon', 'avgBudget'];

const currentMode = () => document.querySelector('input[name=mode]:checked').value;
const runLabel = () => (currentMode() === 'avg' ? '平均より安い銘柄を探す' : '買い候補を探す');

// 平均割れのときは、その条件を出してクロスの絞り込みを隠す。「予算で絞る」を外したら予算の欄は使わない
function syncMode() {
  const avg = currentMode() === 'avg';
  document.querySelectorAll('.avg-only').forEach((e) => {
    e.hidden = !avg;
  });
  document.querySelectorAll('.cross-only').forEach((e) => {
    e.hidden = avg;
  });
  const noBudget = avg && !$('avgBudget').checked;
  $('max').disabled = noBudget;
  $('min').disabled = noBudget;
  if (!$('run').disabled) {
    $('run').textContent = runLabel();
  }
}

function readForm() {
  const f = {
    mode: document.querySelector('input[name=mode]:checked').value,
    max: Number($('max').value),
    min: Number($('min').value || 0),
  };
  for (const id of FILTER_SELECTS) {
    f[id] = $(id).value;
  }
  for (const id of FILTER_CHECKS) {
    f[id] = $(id).checked;
  }
  return f;
}

// 「N日以内」を選んだときだけ「もうすぐ起きそうも含む」を選べる。「除く」のときは「もうすぐ起きそうも除く」
function syncCrossChecks() {
  for (const kind of ['gc', 'dc']) {
    const v = $(kind).value;
    $(`${kind}Soon`).disabled = v === '' || v === 'soon';
    const text = $(`${kind}SoonText`);
    if (text) {
      text.textContent = v.startsWith('not') ? 'もうすぐ起きそうも除く' : 'もうすぐ起きそうも含む';
    }
  }
}

function scanQuery(f) {
  const q = new URLSearchParams({ mode: f.mode, max: f.max * 10000, min: f.min * 10000, cross: f.cross });
  for (const id of ['per', 'pbr', 'gc', 'dc']) {
    if (f[id]) {
      q.set(id, f[id]);
    }
  }
  for (const kind of ['gc', 'dc']) {
    if (f[kind] && f[kind] !== 'soon' && f[`${kind}Soon`]) {
      q.set(`${kind}Soon`, '1');
    }
  }
  return q;
}

async function run(e) {
  e.preventDefault();
  const f = readForm();
  if (f.mode !== 'avg' || f.avgBudget) {
    if (!(f.max > 0)) {
      showError('予算の上限を入れてください。');
      return;
    }
    if (f.min >= f.max) {
      showError('予算の下限は上限より小さくしてください。');
      return;
    }
  }
  save(STORE_KEY, f);
  if (f.mode === 'avg') {
    await startAvg(f);
    return;
  }
  const button = $('run');
  button.disabled = true;
  button.textContent = '調べています…';
  try {
    const res = await fetch(`/api/scan?${scanQuery(f)}`);
    const body = await res.json();
    if (!res.ok) {
      showError(body.error || `エラー(${res.status})`);
      return;
    }
    save(LAST_KEY, body);
    render(body);
  } catch {
    showError('サーバにつながりませんでした。株レーダーが起動しているか確認してください。');
  } finally {
    button.disabled = false;
    button.textContent = runLabel();
  }
}

function init() {
  const f = load(STORE_KEY);
  if (f) {
    const radio = document.querySelector(`input[name=mode][value=${['long', 'avg'].includes(f.mode) ? f.mode : 'short'}]`);
    radio.checked = true;
    if (f.max > 0) {
      $('max').value = f.max;
    }
    if (f.min >= 0) {
      $('min').value = f.min;
    }
    for (const id of FILTER_SELECTS) {
      // 選択肢に無い値(古い保存など)は無視する
      if (typeof f[id] === 'string' && [...$(id).options].some((o) => o.value === f[id])) {
        $(id).value = f[id];
      }
    }
    for (const id of FILTER_CHECKS) {
      if (typeof f[id] === 'boolean') {
        $(id).checked = f[id];
      }
    }
  }
  syncCrossChecks();
  syncMode();
  $('gc').addEventListener('change', syncCrossChecks);
  $('dc').addEventListener('change', syncCrossChecks);
  document.querySelectorAll('input[name=mode]').forEach((r) => r.addEventListener('change', syncMode));
  $('avgBudget').addEventListener('change', syncMode);
  // 最初の状態(HTML に書いた selected・checked)に戻す
  $('clear').addEventListener('click', () => {
    for (const id of FILTER_SELECTS) {
      const options = [...$(id).options];
      $(id).value = (options.find((o) => o.defaultSelected) || options[0]).value;
    }
    for (const id of FILTER_CHECKS) {
      $(id).checked = $(id).defaultChecked;
    }
    syncCrossChecks();
    syncMode();
  });
  // 前回の結果を出しておく(押した時刻が出るので古さは分かる)
  const sort = load(SORT_KEY);
  if (Object.hasOwn(SORTS, sort)) {
    $('sort').value = sort;
  }
  $('sort').addEventListener('change', () => {
    save(SORT_KEY, $('sort').value);
    if (current) {
      renderList();
    }
  });
  // 前回の結果: 平均割れを選んでいればその表、それ以外は銘柄カード
  const last = load(LAST_KEY);
  const lastAvg = load(AVG_KEY);
  if (currentMode() === 'avg' && lastAvg && Array.isArray(lastAvg.items)) {
    renderAvg(lastAvg);
  } else if (last && Array.isArray(last.items)) {
    render(last);
  }
  if (lastAvg && Array.isArray(lastAvg.items)) {
    avgResult = lastAvg;
  }
  // 平均割れを調べている途中なら(画面を開き直したときなど)、進み具合を出す
  fetch('/api/avgscan').then((res) => res.json()).then((job) => {
    if (job.state === 'running') {
      pollAvg();
    }
  }).catch(() => {});
  $('form').addEventListener('submit', run);
  initWatches();

  renderGlossary();
  document.addEventListener('click', (e) => {
    const t = e.target.closest('[data-term]');
    if (t) {
      e.preventDefault();
      openGlossary(t.dataset.term);
    }
  });
  $('g-close').addEventListener('click', () => $('glossary').close());
  $('s-close').addEventListener('click', () => $('snaps').close());
  // 枠の外(背景)を押しても閉じる
  for (const id of ['glossary', 'snaps', 'watches']) {
    $(id).addEventListener('click', (e) => {
      if (e.target === e.currentTarget) {
        e.currentTarget.close();
      }
    });
  }
  $('snap-save').addEventListener('click', saveSnapshot);
  $('open-snaps').addEventListener('click', openSnapshots);
  initStop();
}

// 停止ボタン。スマホ(Tailscale)からは起動し直せないので、PC の画面のときだけ出す
function initStop() {
  if (!['127.0.0.1', 'localhost'].includes(location.hostname)) {
    return;
  }
  const b = $('stop-server');
  b.hidden = false;
  b.addEventListener('click', async () => {
    if (!confirm('株レーダーを止めますか？\n止めると、値段の見張り（LINE 通知）も止まります。')) {
      return;
    }
    b.disabled = true;
    try {
      const res = await fetch('/api/shutdown', { method: 'POST' });
      const j = await res.json().catch(() => ({}));
      if (!res.ok) {
        alert(`止められませんでした: ${j.error || res.status}`);
        b.disabled = false;
        return;
      }
    } catch (e) {
      // 止まる途中で通信が切れることがあるので、そのまま止まった扱いにする
    }
    document.body.innerHTML = '<div class="panel stopped"><h1>⏻ 株レーダーを止めました</h1>'
      + '<p>このタブは閉じて大丈夫です。<br>また使うときは <code>start.bat</code> か、秘書さんの「株レーダー」メニューで起動してください。</p></div>';
  });
}

init();
