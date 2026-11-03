// 株レーダーの画面。入力は localStorage に覚えておき、結果は /api/scan から受け取って並べる
'use strict';

const STORE_KEY = 'stockRadar.form';
const LAST_KEY = 'stockRadar.last';
const SORT_KEY = 'stockRadar.sort';
const MODE_LABEL = { short: '短期', long: '長期' };
const MARKET_LABEL = { good: '地合い: 良い', mixed: '地合い: 普通', bad: '地合い: 弱い' };

const $ = (id) => document.getElementById(id);
const yen = (v) => `${Math.round(v).toLocaleString('ja-JP')}円`;
const price = (v) => v.toLocaleString('ja-JP', { maximumFractionDigits: 1 });
const escapeAttr = (s) => String(s).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');

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

function renderStatus(r) {
  const s = $('status');
  s.className = 'status';
  const excluded = Object.entries(r.excluded).map(([k, v]) => `${k} ${v}`).join('・');
  const f = r.filter || {};
  const crossText = (label, c) => (c ? `${label}: ${c.days == null ? 'もうすぐ起きそう' : `${c.days}日以内${c.soon ? '(もうすぐも含む)' : ''}`}` : null);
  const conds = [
    f.per != null ? `PER ${f.per}倍以下` : null,
    f.pbr != null ? `PBR ${f.pbr}倍未満` : null,
    crossText('ゴールデンクロス', f.gc),
    crossText('デッドクロス', f.dc),
  ].filter(Boolean);
  s.textContent = [
    `${formatTime(r.at)} に調べました（${MODE_LABEL[r.mode]}・100株で${yen(r.budget.min)}〜${yen(r.budget.max)}）`,
    r.dataTime ? `株価は ${formatTime(r.dataTime)} 時点` : null,
    conds.length > 0 ? `絞り込み: ${conds.join('・')}${f.gc || f.dc ? `（${f.crossName}）` : ''}` : null,
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

  const links = el('p', { class: 'links' },
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
  const legend = el('ul', { class: 'chart-legend' },
    el('li', {}, el('i', { class: 'sw up' }), '上がった足'),
    el('li', {}, el('i', { class: 'sw down' }), '下がった足'),
    periods.map((n, i) => el('li', {}, el('i', { class: `sw line ma${i}` }), maName(n))),
    el('li', {}, el('b', { class: 'mk gc' }, '▲'), el('b', { class: 'mk dc' }, '▼'), `${maName(periods[0])}と${maName(periods[1])}のクロス`),
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
    // ローソク足
    for (let i = 0; i < bars.length; i++) {
      const b = bars[i];
      const up = b.c >= b.o;
      const top = y(Math.max(b.o, b.c));
      const h = Math.max(1, y(Math.min(b.o, b.c)) - top);
      parts.push(`<line class="wick" x1="${x(i)}" x2="${x(i)}" y1="${y(b.h)}" y2="${y(b.l)}"/>`);
      parts.push(`<rect class="${up ? 'up' : 'down'}" x="${x(i) - bodyW / 2}" y="${top}" width="${bodyW}" height="${h}"/>`);
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
      info.replaceChildren(
        el('b', {}, `${formatDate(b.date)}${data.interval === '1wk' ? 'の週' : ''}`),
        ` 始 ${price(b.o)}・高 ${price(b.h)}・安 ${price(b.l)}・終 `, el('b', {}, price(b.c)),
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

function render(r) {
  current = r;
  renderStatus(r);
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

function showError(message) {
  const s = $('status');
  s.className = 'status error';
  s.textContent = message;
  s.hidden = false;
}

// ---------------------------------------------------------------- 操作

const FILTER_SELECTS = ['per', 'pbr', 'cross', 'gc', 'dc'];
const FILTER_CHECKS = ['gcSoon', 'dcSoon'];

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

// 「N日以内」を選んだときだけ「もうすぐ起きそうも含む」を選べる
function syncCrossChecks() {
  for (const kind of ['gc', 'dc']) {
    const v = $(kind).value;
    $(`${kind}Soon`).disabled = v === '' || v === 'soon';
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
  if (!(f.max > 0)) {
    showError('予算の上限を入れてください。');
    return;
  }
  if (f.min >= f.max) {
    showError('予算の下限は上限より小さくしてください。');
    return;
  }
  save(STORE_KEY, f);
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
    button.textContent = '買い候補を探す';
  }
}

function init() {
  const f = load(STORE_KEY);
  if (f) {
    const radio = document.querySelector(`input[name=mode][value=${f.mode === 'long' ? 'long' : 'short'}]`);
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
  $('gc').addEventListener('change', syncCrossChecks);
  $('dc').addEventListener('change', syncCrossChecks);
  $('clear').addEventListener('click', () => {
    for (const id of FILTER_SELECTS) {
      $(id).selectedIndex = 0;
    }
    for (const id of FILTER_CHECKS) {
      $(id).checked = true;
    }
    syncCrossChecks();
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
  const last = load(LAST_KEY);
  if (last && Array.isArray(last.items)) {
    render(last);
  }
  $('form').addEventListener('submit', run);

  renderGlossary();
  document.addEventListener('click', (e) => {
    const t = e.target.closest('[data-term]');
    if (t) {
      e.preventDefault();
      openGlossary(t.dataset.term);
    }
  });
  $('g-close').addEventListener('click', () => $('glossary').close());
  // 枠の外(背景)を押しても閉じる
  $('glossary').addEventListener('click', (e) => {
    if (e.target === e.currentTarget) {
      e.currentTarget.close();
    }
  });
}

init();
