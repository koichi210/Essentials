// 株レーダーの判定ロジック。通信はしない(server.js が取ってきた値を受け取って計算するだけ)。
// 売買の単位は100株。値段は東証の呼値(標準)に丸める。
'use strict';

const LOT = 100;
const SHORT_HOLD_DAYS = 10;
const SHORT_MAX_LOSS = 0.07;
const LONG_MAX_LOSS = 0.10;
const LONG_TARGET = 0.20;
const MIN_TURNOVER = 3e8;
const EARNINGS_GUARD_DAYS = 7;
const DAY = 24 * 60 * 60;

// ---------------------------------------------------------------- 日付と呼値

function jstDate(sec) {
  return new Date((sec + 9 * 3600) * 1000).toISOString().slice(0, 10);
}

function jstHour(sec) {
  return new Date((sec + 9 * 3600) * 1000).getUTCHours();
}

// 土日だけ飛ばす(祝日は見ない)
function addBusinessDays(date, n) {
  const d = new Date(`${date}T00:00:00Z`);
  let left = n;
  while (left > 0) {
    d.setUTCDate(d.getUTCDate() + 1);
    const w = d.getUTCDay();
    if (w !== 0 && w !== 6) {
      left--;
    }
  }
  return d.toISOString().slice(0, 10);
}

// 標準の呼値。TOPIX500 の細かい呼値はこれを割り切るので、どの銘柄でも注文できる値段になる
function tickOf(price) {
  if (price <= 3000) {
    return 1;
  }
  if (price <= 5000) {
    return 5;
  }
  if (price <= 30000) {
    return 10;
  }
  if (price <= 50000) {
    return 50;
  }
  return 100;
}

function floorTick(price) {
  const t = tickOf(price);
  return Math.floor(price / t + 1e-9) * t;
}

function ceilTick(price) {
  const t = tickOf(price);
  return Math.ceil(price / t - 1e-9) * t;
}

// ---------------------------------------------------------------- 指標

function sma(values, n, end = values.length) {
  if (end < n) {
    return null;
  }
  let sum = 0;
  for (let i = end - n; i < end; i++) {
    sum += values[i];
  }
  return sum / n;
}

// Wilder の RSI
function rsi(closes, n = 14) {
  if (closes.length <= n) {
    return null;
  }
  let gain = 0;
  let loss = 0;
  for (let i = 1; i <= n; i++) {
    const d = closes[i] - closes[i - 1];
    gain += Math.max(d, 0);
    loss += Math.max(-d, 0);
  }
  gain /= n;
  loss /= n;
  for (let i = n + 1; i < closes.length; i++) {
    const d = closes[i] - closes[i - 1];
    gain = (gain * (n - 1) + Math.max(d, 0)) / n;
    loss = (loss * (n - 1) + Math.max(-d, 0)) / n;
  }
  if (loss === 0) {
    return 100;
  }
  return 100 - 100 / (1 + gain / loss);
}

// 直近 n 日の真の値幅の平均
function atr(bars, n = 14) {
  if (bars.length <= n) {
    return null;
  }
  let sum = 0;
  for (let i = bars.length - n; i < bars.length; i++) {
    const b = bars[i];
    const prev = bars[i - 1].close;
    sum += Math.max(b.high - b.low, Math.abs(b.high - prev), Math.abs(b.low - prev));
  }
  return sum / n;
}

function ema(values, n) {
  const k = 2 / (n + 1);
  const out = [values[0]];
  for (let i = 1; i < values.length; i++) {
    out.push(values[i] * k + out[i - 1] * (1 - k));
  }
  return out;
}

// MACD(12, 26, 9)。直近 days 本の間に MACD 線がシグナル線を下から上に抜けたら goldenCross
function macd(closes, days = 3) {
  if (closes.length < 35) {
    return null;
  }
  const fast = ema(closes, 12);
  const slow = ema(closes, 26);
  const line = closes.map((_, i) => fast[i] - slow[i]);
  const signal = ema(line, 9);
  const hist = line.map((v, i) => v - signal[i]);
  const n = hist.length;
  let goldenCross = false;
  for (let i = n - days; i < n; i++) {
    if (hist[i - 1] <= 0 && hist[i] > 0) {
      goldenCross = true;
    }
  }
  return { line: line[n - 1], signal: signal[n - 1], goldenCross, hist };
}

// ---------------------------------------------------------------- クロス

// 絞り込みで選べるクロスの線。fast が slow を下から上に抜けたらゴールデンクロス、上から下ならデッドクロス
const CROSS_TYPES = {
  ma5_25: { fast: 5, slow: 25, name: '5日線と25日線' },
  ma25_75: { fast: 25, slow: 75, name: '25日線と75日線' },
  macd: { name: 'MACD 線とシグナル線' },
};
const CROSS_LOOKBACK = 20;
const CROSS_SOON_DAYS = 3;

// 直近 CROSS_LOOKBACK 本の「fast − slow」の差
function crossDiffs(closes, type) {
  const t = CROSS_TYPES[type];
  if (!t.fast) {
    const m = macd(closes);
    return m ? m.hist.slice(-(CROSS_LOOKBACK + 1)) : [];
  }
  const diffs = [];
  for (let end = closes.length - CROSS_LOOKBACK; end <= closes.length; end++) {
    const f = sma(closes, t.fast, end);
    const s = sma(closes, t.slow, end);
    diffs.push(f == null || s == null ? null : f - s);
  }
  return diffs;
}

// いちばん新しい交差(daysAgo は今日の足が0)と、このままの勢いなら CROSS_SOON_DAYS 日以内に交差しそうか
function crossFromDiffs(diffs) {
  let last = null;
  for (let i = diffs.length - 1; i > 0; i--) {
    if (diffs[i] == null || diffs[i - 1] == null) {
      break;
    }
    if (diffs[i - 1] <= 0 && diffs[i] > 0) {
      last = { kind: 'gc', daysAgo: diffs.length - 1 - i };
      break;
    }
    if (diffs[i - 1] >= 0 && diffs[i] < 0) {
      last = { kind: 'dc', daysAgo: diffs.length - 1 - i };
      break;
    }
  }
  // 3日前からの差の縮み方で、あと何日で交差するかを見積もる
  let soon = null;
  const now = diffs[diffs.length - 1];
  const prev = diffs[diffs.length - 4];
  if (now != null && prev != null) {
    const slope = (now - prev) / 3;
    if (now < 0 && slope > 0 && -now / slope <= CROSS_SOON_DAYS) {
      soon = { kind: 'gc', inDays: Math.max(1, Math.ceil(-now / slope)) };
    } else if (now > 0 && slope < 0 && now / -slope <= CROSS_SOON_DAYS) {
      soon = { kind: 'dc', inDays: Math.max(1, Math.ceil(now / -slope)) };
    }
  }
  return { last, soon };
}

function crossInfo(closes, type) {
  return { type, ...crossFromDiffs(crossDiffs(closes, type)) };
}

// cond = { days: N日以内(null なら見ない), soon: もうすぐ交差しそうも含む }
function matchCross(cross, kind, cond) {
  if (!cond) {
    return true;
  }
  const recent = cond.days != null && cross.last && cross.last.kind === kind && cross.last.daysAgo < cond.days;
  const soon = cond.soon && cross.soon && cross.soon.kind === kind;
  // cond.not なら、条件に当たる銘柄を除く
  return Boolean(recent || soon) !== Boolean(cond.not);
}

// showDays 日以内の交差と「もうすぐ」を、押しポイント・気をつけるところの形にする
function crossPoint(cross, showDays = 10) {
  const t = CROSS_TYPES[cross.type];
  const label = (kind) => `${kind === 'gc' ? 'ゴールデンクロス' : 'デッドクロス'}(${t.name})`;
  const when = (d) => (d === 0 ? '今日' : `${d}営業日前に`);
  const out = [];
  if (cross.last && cross.last.daysAgo < showDays) {
    out.push(cross.last.kind === 'gc'
      ? { good: true, ...point(label('gc'), `${when(cross.last.daysAgo)}、短い線が長い線を下から上に抜けた。上がり始めのサイン`, 'cross') }
      : { good: false, ...point(label('dc'), `${when(cross.last.daysAgo)}、短い線が長い線を上から下に抜けた。下がり始めのサイン`, 'cross') });
  }
  if (cross.soon) {
    out.push(cross.soon.kind === 'gc'
      ? { good: true, ...point(`もうすぐ${label('gc')}`, `短い線が長い線に下から近づいている。このままなら、あと${cross.soon.inDays}日くらいで交差しそう`, 'cross') }
      : { good: false, ...point(`もうすぐ${label('dc')}`, `短い線が長い線に上から近づいている。このままなら、あと${cross.soon.inDays}日くらいで交差しそう`, 'cross') });
  }
  return out;
}

// ---------------------------------------------------------------- 絞り込み

function perOf(quote) {
  return quote.forwardPE > 0 ? quote.forwardPE : quote.trailingPE > 0 ? quote.trailingPE : null;
}

// PER は「以下」、PBR は「未満」。指定したときは値の無い銘柄(赤字など)を外す
function matchValuation(quote, filter) {
  if (filter.per != null) {
    const per = perOf(quote);
    if (per == null || per > filter.per) {
      return false;
    }
  }
  if (filter.pbr != null && !(quote.priceToBook > 0 && quote.priceToBook < filter.pbr)) {
    return false;
  }
  return true;
}

// 画面に出すときの、日足・週足の移動平均とクロスの位置(グラフ用)
function chartSeries(bars, periods, show) {
  const closes = bars.map((b) => b.close);
  const from = Math.max(0, bars.length - show);
  const ma = {};
  for (const n of periods) {
    ma[n] = closes.slice(from).map((_, i) => {
      const v = sma(closes, n, from + i + 1);
      return v == null ? null : round1(v);
    });
  }
  // 短い2本の線の交差に印を付ける
  const [fast, slow] = periods;
  const crosses = [];
  for (let i = 1; i < ma[fast].length; i++) {
    const [a0, b0, a1, b1] = [ma[fast][i - 1], ma[slow][i - 1], ma[fast][i], ma[slow][i]];
    if ([a0, b0, a1, b1].some((v) => v == null)) {
      continue;
    }
    if (a0 <= b0 && a1 > b1) {
      crosses.push({ index: i, kind: 'gc' });
    } else if (a0 >= b0 && a1 < b1) {
      crosses.push({ index: i, kind: 'dc' });
    }
  }
  return {
    // pc は前の足の終値(ローソク足の色を前日比で決めるため)
    bars: bars.slice(from).map((b, i) => ({ date: b.date, o: b.open, h: b.high, l: b.low, c: b.close, v: b.volume, pc: from + i > 0 ? bars[from + i - 1].close : null })),
    ma,
    crosses,
  };
}

// スナップショットの銘柄が、その後どう動いたか。since(そのときの株価の日付)より後の足で、
// 利確・損切りのどちらに先に届いたかを見る(同じ足で両方なら both。1日の中の順番は分からない)
function compareSince(item, bars, since) {
  if (bars.length === 0) {
    return null;
  }
  const after = bars.filter((b) => b.date > since);
  const last = bars[bars.length - 1];
  let hit = null;
  for (const b of after) {
    const target = b.high >= item.target;
    const stop = b.low <= item.stop;
    if (target || stop) {
      hit = { kind: target && stop ? 'both' : target ? 'target' : 'stop', date: b.date };
      break;
    }
  }
  return {
    price: last.close,
    date: last.date,
    change: round1((last.close / item.price - 1) * 100),
    high: after.length > 0 ? Math.max(...after.map((b) => b.high)) : null,
    low: after.length > 0 ? Math.min(...after.map((b) => b.low)) : null,
    days: after.length,
    hit,
  };
}

// スクリーナーの値から出す業績の指標。ROE = 1株利益 ÷ 1株純資産、配当性向 = 1株配当(予想) ÷ 1株利益
function fundamentals(quote) {
  const eps = Number.isFinite(quote.epsTrailingTwelveMonths) ? quote.epsTrailingTwelveMonths : null;
  const bps = quote.bookValue;
  const rating = Number.parseFloat(quote.averageAnalystRating);
  return {
    eps,
    roe: eps > 0 && bps > 0 ? (eps / bps) * 100 : null,
    payout: eps > 0 && quote.dividendRate > 0 ? (quote.dividendRate / eps) * 100 : null,
    rating: Number.isFinite(rating) ? rating : null,
  };
}

// 押しポイント・気をつけるところの1行。term は画面の用語集(public/glossary.js)の id
function point(label, text, term) {
  return { label, text, term };
}

// Yahoo の chart API の応答を日足の配列にする(値の欠けた日は捨てる)
function parseChart(json) {
  const r = json && json.chart && json.chart.result && json.chart.result[0];
  if (!r || !r.timestamp || !r.indicators || !r.indicators.quote) {
    return [];
  }
  const q = r.indicators.quote[0];
  const bars = [];
  for (let i = 0; i < r.timestamp.length; i++) {
    const close = q.close[i];
    if (close == null || q.high[i] == null || q.low[i] == null || q.open[i] == null) {
      continue;
    }
    bars.push({ t: r.timestamp[i], date: jstDate(r.timestamp[i]), open: q.open[i], high: q.high[i], low: q.low[i], close, volume: q.volume[i] || 0 });
  }
  return bars;
}

// ---------------------------------------------------------------- 決算

// 直近に終わった決算と、次の決算(どちらも7日以内のものだけ)
function earningsInfo(quote, nowSec) {
  const ts = [quote.earningsTimestamp, quote.earningsTimestampStart, quote.earningsTimestampEnd].filter(Number.isFinite);
  const past = ts.filter((t) => t <= nowSec && nowSec - t <= EARNINGS_GUARD_DAYS * DAY).sort((a, b) => b - a)[0];
  const next = ts.filter((t) => t > nowSec && t - nowSec <= EARNINGS_GUARD_DAYS * DAY).sort((a, b) => a - b)[0];
  return { past: past ?? null, next: next ?? null };
}

// 決算を受けた最初の日足の位置。15時以降の発表なら翌営業日、それより前なら当日。まだ無ければ -1
function reactionIndex(bars, earningsSec) {
  const date = jstDate(earningsSec);
  const afterClose = jstHour(earningsSec) >= 15;
  return bars.findIndex((b) => (afterClose ? b.date > date : b.date >= date));
}

// ---------------------------------------------------------------- 短期

function analyzeShort(quote, bars, nowSec) {
  const base = { code: codeOf(quote.symbol), name: nameOf(quote) };
  if (bars.length < 80) {
    return { ...base, excluded: 'データ不足' };
  }
  const closes = bars.map((b) => b.close);
  const vols = bars.map((b) => b.volume);
  const last = bars[bars.length - 1];
  const c = last.close;
  const s25 = sma(closes, 25);
  const s75 = sma(closes, 75);
  const s25prev = sma(closes, 25, closes.length - 5);
  const r = rsi(closes, 14);
  const a = atr(bars, 14);
  const v5 = sma(vols, 5);
  const v25 = sma(vols, 25);
  const high20 = Math.max(...bars.slice(-21, -1).map((b) => b.high));

  if (v25 * c < MIN_TURNOVER) {
    return { ...base, excluded: '売買代金が少ない' };
  }
  const earn = earningsInfo(quote, nowSec);
  if (earn.next) {
    return { ...base, excluded: '決算発表の直前' };
  }

  let score = 30;
  const reasons = [];
  const warnings = [];
  const up = c > s75 && s25 > s75;
  if (up) {
    score += 20;
    reasons.push(point('上昇トレンド', '25日線(約1か月の平均)が75日線(約3か月の平均)より上。買う人が多い流れ', 'trend'));
  } else if (c < s75 && s25 < s75) {
    score -= 20;
    warnings.push(point('下降トレンド', '株価も25日線も75日線より下。売られている流れなので、戻ってもまた下がりやすい', 'trend'));
  }
  if (s25 > s25prev) {
    score += 10;
    reasons.push(point('25日線が上向き', '1か月の平均の値段が5日前より上がっている。上がる勢いが続いている', 'ma'));
  }

  // 決算を受けた値動き
  let earnMove = null;
  if (earn.past) {
    const ri = reactionIndex(bars, earn.past);
    if (ri === -1) {
      score -= 10;
      warnings.push(point('決算の反応待ち', '決算は発表されたけれど、まだ株価が動いていない。次の取引で大きく上下するかも', 'earnings'));
    } else if (ri > 0) {
      const move = c / bars[ri - 1].close - 1;
      if (move >= 0.03 && c >= bars[ri].open * 0.98) {
        earnMove = move;
      } else if (move <= -0.05) {
        score -= 15;
        warnings.push(point('決算後に下落', `決算発表のあと${Math.round(-move * 100)}%下がった。内容が嫌われたサイン`, 'earnings'));
      }
    }
  }

  const dev25 = c / s25 - 1;
  const pct = (v) => `${round1(v * 100)}%`;
  let setup = null;
  if (earnMove != null) {
    setup = '決算後の初動';
    score += 30;
    reasons.push(point('決算後の初動', `決算発表のあと${pct(earnMove)}上がり、その高さを保っている。良い決算を見て買う人が続きやすい`, 'earnings'));
  } else if (c >= high20 && v5 >= v25 * 1.5 && r < 75) {
    setup = '高値ブレイク';
    score += 25;
    reasons.push(point('高値ブレイク', `最近20日でいちばん高い値を、いつもの${round1(v5 / v25)}倍の出来高で抜けた。上に行く力が強い`, 'breakout'));
  } else if (up && dev25 >= -0.03 && dev25 <= 0.02 && r >= 38 && r <= 55) {
    setup = '押し目';
    score += 30;
    reasons.push(point('押し目', `上り調子の株が、25日線の近く(${dev25 >= 0 ? '+' : ''}${pct(dev25)})まで一休みして下がってきた。安く拾えるタイミング`, 'pullback'));
  }

  const rr14 = Math.round(r);
  if (r >= 75) {
    score -= 25;
    warnings.push(point(`買われすぎ(RSI ${rr14})`, '短い間に上がりすぎ。75を超えると一度下がりやすい', 'rsi'));
  } else if (r >= 70) {
    score -= 10;
    warnings.push(point(`やや買われすぎ(RSI ${rr14})`, '70を超えると上がりすぎの目安。一度下がることがある', 'rsi'));
  } else if (r < 30) {
    score -= 15;
    warnings.push(point(`売られすぎ(RSI ${rr14})`, '急に下がっている途中。下げ止まりを確かめてからでも遅くない', 'rsi'));
  }
  if (dev25 > 0.08) {
    score -= 15;
    warnings.push(point('上がりすぎ', `25日線より${pct(dev25)}高い。平均に戻ろうとして下がりやすい`, 'ma'));
  }
  if (v5 > v25 * 1.3 && setup !== '高値ブレイク') {
    score += 5;
    reasons.push(point('出来高が増えている', `最近5日の出来高がいつもの${round1(v5 / v25)}倍。注目が集まっている`, 'volume'));
  }
  const m = macd(closes);
  if (m && m.goldenCross) {
    score += 8;
    reasons.push(point('MACDのゴールデンクロス', 'この3日の間に、MACD線がシグナル線を下から上に抜けた。上がる勢いが戻ってきたサイン', 'macd'));
  }
  // 業績: 大負けを避けるため赤字は減点、稼ぐ力があれば少し加点
  const f = fundamentals(quote);
  if (f.eps != null && f.eps <= 0) {
    score -= 10;
    warnings.push(point('赤字の会社', '直近1年の利益がマイナス。悪いニュースで大きく下がりやすい', 'eps'));
  } else if (f.roe >= 10) {
    score += 5;
    reasons.push(point(`ROE ${round1(f.roe)}%`, `株主のお金で1年に${round1(f.roe)}%の利益を出している(8%が合格ライン)。業績の裏付けがある`, 'roe'));
  }

  // 買いの幅: 押し目は25日線のすぐ下まで待つ、それ以外は今の値から ATR の半分まで
  const buyHigh = floorTick(c);
  const buyLow = Math.min(buyHigh, floorTick(setup === '押し目' ? Math.min(c, s25 * 0.99) : c - 0.5 * a));
  const entry = (buyLow + buyHigh) / 2;
  const stop = ceilTick(Math.max(entry - 1.5 * a, entry * (1 - SHORT_MAX_LOSS)));
  const risk = entry - stop;
  let target = entry + 2 * risk;
  // 上値の壁(60日高値・52週高値)が途中にあれば、その手前で売る
  const walls = [Math.max(...bars.slice(-60).map((b) => b.high)), quote.fiftyTwoWeekHigh]
    .filter((h) => Number.isFinite(h) && h > entry * 1.02 && h < target);
  const wall = walls.length > 0 ? Math.min(...walls) : null;
  if (wall != null) {
    target = wall * 0.99;
  }
  target = floorTick(target);
  const rr = (target - entry) / risk;
  if (rr < 1.2) {
    score -= 15;
    warnings.push(point('利益の幅が小さい', `少し上に最近の高値(${Math.round(wall ?? target).toLocaleString('ja-JP')}円)があり、そこで売られやすい`, 'high52'));
  }

  return {
    ...base,
    setup,
    score: clamp(Math.round(score), 0, 100),
    price: c,
    prevClose: bars.length > 1 ? bars[bars.length - 2].close : null,
    cost: Math.round(c * LOT),
    priceDate: last.date,
    buy: { low: buyLow, high: buyHigh },
    target,
    stop,
    deadline: addBusinessDays(last.date, SHORT_HOLD_DAYS),
    gainYen: Math.round((target - entry) * LOT),
    lossYen: Math.round((entry - stop) * LOT),
    rr: Math.round(rr * 10) / 10,
    dividendYen: quote.dividendYield > 0 ? Math.round((c * LOT * quote.dividendYield) / 100) : 0,
    reasons,
    warnings,
    stats: { rsi: rr14, dev25: round1(dev25 * 100), per: num(quote.forwardPE ?? quote.trailingPE), pbr: Number.isFinite(quote.priceToBook) ? round2(quote.priceToBook) : null, roe: num(f.roe), dividend: num(quote.dividendYield) },
  };
}

// ---------------------------------------------------------------- 長期

function analyzeLong(quote) {
  const base = { code: codeOf(quote.symbol), name: nameOf(quote) };
  const c = quote.regularMarketPrice;
  const per = perOf(quote);
  if (!(c > 0) || per == null) {
    return { ...base, excluded: '赤字または利益のデータなし' };
  }
  const pbr = quote.priceToBook > 0 ? quote.priceToBook : null;
  const dividend = quote.dividendYield > 0 ? quote.dividendYield : 0;
  const s50 = quote.fiftyDayAverage;
  const s200 = quote.twoHundredDayAverage;

  let score = 0;
  const reasons = [];
  const warnings = [];
  const perText = `PER ${round1(per)}倍`;
  const years = `今の利益の約${Math.round(per)}年分の値段`;
  if (per <= 10) {
    score += 25;
    reasons.push(point(perText, `${years}。東証プライムの平均(約20倍)の半分以下`, 'per'));
  } else if (per <= 15) {
    score += 18;
    reasons.push(point(perText, `${years}。東証プライムの平均(約20倍)より低い`, 'per'));
  } else if (per <= 20) {
    score += 8;
    reasons.push(point(perText, `${years}。東証プライムの平均(約20倍)並み`, 'per'));
  } else if (per > 40) {
    score -= 10;
    warnings.push(point(perText, `${years}。期待が大きい分、がっかりすると大きく下がりやすい`, 'per'));
  }
  const f = fundamentals(quote);
  if (pbr != null) {
    const pbrText = `PBR ${round2(pbr)}倍`;
    const off = Math.round((1 - pbr) * 10);
    const cheap = off >= 1 ? `会社の持ち物(純資産)より約${off}割安い値段` : '会社の持ち物(純資産)とほぼ同じ値段';
    if (pbr < 0.8) {
      score += 20;
      reasons.push(point(pbrText, `${cheap}。0.8倍未満はかなり割安`, 'pbr'));
    } else if (pbr < 1) {
      score += 15;
      reasons.push(point(pbrText, `${cheap}。1倍割れは割安のサイン`, 'pbr'));
    } else if (pbr < 1.5) {
      score += 8;
      reasons.push(point(pbrText, `会社の持ち物(純資産)の${round2(pbr)}倍の値段。高すぎない`, 'pbr'));
    }
  }
  if (f.roe != null) {
    const roeText = `ROE ${round1(f.roe)}%`;
    const earn = `株主のお金で1年に${round1(f.roe)}%の利益を出している`;
    if (f.roe >= 15) {
      score += 12;
      reasons.push(point(roeText, `${earn}。15%以上はかなり優秀`, 'roe'));
    } else if (f.roe >= 10) {
      score += 8;
      reasons.push(point(roeText, `${earn}。10%以上は優秀`, 'roe'));
    } else if (f.roe >= 8) {
      score += 4;
      reasons.push(point(roeText, `${earn}。8%が合格ライン`, 'roe'));
    } else if (f.roe < 5) {
      score -= 5;
      warnings.push(point(roeText, pbr != null && pbr < 1
        ? `稼ぐ力が弱め(8%が合格ライン)。PBRが低いのはそのせいかも(割安の罠)`
        : `稼ぐ力が弱め(8%が合格ライン)`, 'roe'));
    }
  }
  const yen = Math.round((c * LOT * dividend) / 100).toLocaleString('ja-JP');
  if (dividend >= 4) {
    score += 20;
    reasons.push(point(`配当利回り ${round1(dividend)}%`, `100株を持っているだけで1年に約${yen}円。4%以上は高配当`, 'dividend'));
  } else if (dividend >= 3) {
    score += 15;
    reasons.push(point(`配当利回り ${round1(dividend)}%`, `100株を持っているだけで1年に約${yen}円。3%以上は高め`, 'dividend'));
  } else if (dividend >= 2) {
    score += 8;
    reasons.push(point(`配当利回り ${round1(dividend)}%`, `100株を持っているだけで1年に約${yen}円`, 'dividend'));
  }
  if (f.payout != null && dividend >= 2) {
    const payoutText = `配当性向 ${Math.round(f.payout)}%`;
    if (f.payout <= 50) {
      score += 5;
      reasons.push(point(payoutText, `利益の${Math.round(f.payout)}%を配当に回している。余裕があるので減らされにくい`, 'payout'));
    } else if (f.payout > 100) {
      score -= 10;
      warnings.push(point(payoutText, '利益より多く配当している。配当が減らされるかも', 'payout'));
    }
  }
  if (quote.epsForward > 0 && f.eps > 0 && quote.epsForward >= f.eps * 1.1) {
    score += 5;
    reasons.push(point('増益予想', `1株あたりの利益が、今より${Math.round((quote.epsForward / f.eps - 1) * 100)}%増える予想`, 'eps'));
  }
  if (f.rating != null) {
    const ratingText = `アナリスト評価 ${f.rating}`;
    if (f.rating <= 2) {
      score += 8;
      reasons.push(point(ratingText, 'プロの評価の平均が「買い」寄り(1が最高、5が最低)', 'analyst'));
    } else if (f.rating <= 2.5) {
      score += 4;
      reasons.push(point(ratingText, 'プロの評価の平均がやや「買い」寄り(1が最高、5が最低)', 'analyst'));
    } else if (f.rating >= 3.5) {
      score -= 5;
      warnings.push(point(ratingText, 'プロの評価の平均が「売り」寄り(1が最高、5が最低)', 'analyst'));
    }
  }
  if (s200 > 0) {
    if (c >= s200) {
      score += 15;
      reasons.push(point('200日線より上', '約10か月の平均の値段より上。長い目で見て上り調子', 'ma'));
    } else if (c < s200 * 0.85) {
      score -= 15;
      warnings.push(point('200日線を大きく下回る', `約10か月の平均より${Math.round((1 - c / s200) * 100)}%下。安いのには理由があるかも(割安の罠)`, 'trap'));
    }
  }
  if (quote.fiftyTwoWeekHigh > 0 && c > quote.fiftyTwoWeekHigh * 0.97) {
    score -= 5;
    warnings.push(point('1年の高値の近く', `この1年でいちばん高い値(${Math.round(quote.fiftyTwoWeekHigh).toLocaleString('ja-JP')}円)のすぐ近く。高値づかみに注意`, 'high52'));
  }

  // 買いの幅: 50日線より上なら50日線まで(ただし5%下まで)、下なら3%下まで
  const buyHigh = floorTick(c);
  const lowRaw = s50 > 0 && s50 < c ? Math.max(s50, c * 0.95) : c * 0.97;
  const buyLow = Math.min(buyHigh, floorTick(lowRaw));
  const entry = (buyLow + buyHigh) / 2;
  const stop = ceilTick(entry * (1 - LONG_MAX_LOSS));
  let target = entry * (1 + LONG_TARGET);
  if (quote.fiftyTwoWeekHigh > entry * 1.08 && quote.fiftyTwoWeekHigh < target) {
    target = quote.fiftyTwoWeekHigh * 0.99;
  }
  target = floorTick(target);

  return {
    ...base,
    setup: dividend >= 3 ? '高配当' : pbr != null && pbr < 1 ? '割安' : null,
    score: clamp(Math.round(score), 0, 100),
    price: c,
    prevClose: quote.regularMarketPreviousClose > 0 ? quote.regularMarketPreviousClose : null,
    cost: Math.round(c * LOT),
    priceDate: Number.isFinite(quote.regularMarketTime) ? jstDate(quote.regularMarketTime) : null,
    buy: { low: buyLow, high: buyHigh },
    target,
    stop,
    deadline: null,
    gainYen: Math.round((target - entry) * LOT),
    lossYen: Math.round((entry - stop) * LOT),
    rr: Math.round(((target - entry) / (entry - stop)) * 10) / 10,
    dividendYen: Math.round((c * LOT * dividend) / 100),
    reasons,
    warnings,
    stats: { per: round1(per), pbr: pbr == null ? null : round2(pbr), roe: num(f.roe), dividend: num(dividend), payout: f.payout == null ? null : Math.round(f.payout) },
  };
}

// ---------------------------------------------------------------- 地合い

function analyzeMarket(bars) {
  if (bars.length < 80) {
    return null;
  }
  const closes = bars.map((b) => b.close);
  const c = closes[closes.length - 1];
  const s25 = sma(closes, 25);
  const s75 = sma(closes, 75);
  const r = rsi(closes, 14);
  let level;
  let text;
  if (c > s25 && s25 > s75) {
    level = 'good';
    text = '日経平均は上昇基調です。';
  } else if (c < s25 && c < s75) {
    level = 'bad';
    text = '日経平均が25日線・75日線を下回っています。買いは控えめに。';
  } else {
    level = 'mixed';
    text = '日経平均は方向感が出ていません。買うなら予算の半分からに。';
  }
  if (r >= 75) {
    text += ` RSI ${Math.round(r)} で過熱ぎみです。`;
  }
  return { level, text, price: c, date: bars[bars.length - 1].date, sma25: Math.round(s25), sma75: Math.round(s75), rsi: Math.round(r) };
}

// ---------------------------------------------------------------- 配当・優待

// chart API(events=div)の配当履歴から、権利確定月・直近1年の1株配当・次の確定月を出す。
// 配当の日付は権利落ち日で、権利確定日(月末)と同じ月になる
function parseDividends(json, nowSec) {
  const r = json && json.chart && json.chart.result && json.chart.result[0];
  const divs = Object.values((r && r.events && r.events.dividends) || {}).filter((d) => Number.isFinite(d.date) && d.amount > 0);
  if (divs.length === 0) {
    return { months: [], last12: 0, next: null };
  }
  const months = [...new Set(divs.map((d) => monthOf(d.date)))].sort((x, y) => x - y);
  const last12 = divs.filter((d) => nowSec - d.date <= 365 * DAY).reduce((sum, d) => sum + d.amount, 0);
  const now = monthOf(nowSec);
  return { months, last12: round2(last12), next: months.find((m) => m >= now) ?? months[0] };
}

// Yahoo!ファイナンス(日本)の優待ページ(/quote/XXXX.T/incentive)の HTML から優待を取り出す。
// 優待なしは { none: true }、ページの形が想定と違えば null
function parseIncentive(html) {
  if (html.includes('株主優待情報はありません')) {
    return { none: true };
  }
  const summary = {};
  for (const m of html.matchAll(/<th[^>]*>(権利付き最終日|単元株数|優待の種類)<\/th>\s*<td[^>]*>(.*?)<\/td>/gs)) {
    summary[m[1]] = stripTags(m[2]);
  }
  const start = html.indexOf('株主優待の内容</h2>');
  if (!summary['権利付き最終日'] || start < 0) {
    return null;
  }
  const end = html.indexOf('</section>', start);
  const body = html.slice(start, end < 0 ? undefined : end);
  // 見出し(優待の名前)・注記・株数ごとの行を、ページに出ている順に並べる(レイアウト用の空の段落は捨てる)
  const lines = [];
  for (const m of body.matchAll(/<h3[^>]*>(.*?)<\/h3>|<p[^>]*>(.*?)<\/p>|<tr[^>]*>\s*<th[^>]*>(.*?)<\/th>\s*<td[^>]*>(.*?)<\/td>/gs)) {
    const line = m[1] != null ? { kind: 'title', text: stripTags(m[1]) }
      : m[2] != null ? { kind: 'note', text: stripTags(m[2]) }
        : { kind: 'row', shares: stripTags(m[3]), text: stripTags(m[4]) };
    if (line.text || line.shares) {
      lines.push(line);
    }
  }
  const lastDates = [...summary['権利付き最終日'].matchAll(/(\d{4})年(\d{1,2})月(\d{1,2})日/g)]
    .map((m) => `${m[1]}-${m[2].padStart(2, '0')}-${m[3].padStart(2, '0')}`);
  const shares = lines
    .map((l) => /([\d,]+)株以上/.exec(l.shares || ''))
    .filter(Boolean)
    .map((m) => Number(m[1].replace(/,/g, '')));
  const continuity = [...new Set(lines.flatMap((l) => [l.shares, l.text]).filter((t) => t && t.includes('継続')))];
  return {
    none: false,
    kinds: summary['優待の種類'] || '',
    lastDates,
    months: [...new Set(lastDates.map((d) => Number(d.slice(5, 7))))].sort((x, y) => x - y),
    minShares: shares.length > 0 ? Math.min(...shares) : null,
    continuity: { span: continuitySpan(continuity), lines: continuity },
    lines,
  };
}

// 継続保有の条件のうち、いちばん短い期間(「1年以上」「半年以上」「6か月以上」など)
function continuitySpan(lines) {
  const spans = [];
  for (const t of lines) {
    for (const m of t.matchAll(/(\d+)\s*年(?:以上|間以上)/g)) {
      spans.push({ months: Number(m[1]) * 12, text: `${m[1]}年以上` });
    }
    for (const m of t.matchAll(/(\d+)\s*[かヶカケ]月(?:以上|間以上)/g)) {
      spans.push({ months: Number(m[1]), text: `${m[1]}か月以上` });
    }
    if (/半年(?:以上|間以上)/.test(t)) {
      spans.push({ months: 6, text: '半年以上' });
    }
  }
  spans.sort((x, y) => x.months - y.months);
  return spans.length > 0 ? spans[0].text : null;
}

// 短期で持っている間(from〜to)に来る権利確定。優待は権利付き最終日、配当は確定月の月末で見る
function rightsDuring(from, to, divMonths, yutaiDates) {
  const hits = [];
  for (const d of yutaiDates) {
    if (d >= from && d <= to) {
      hits.push({ kind: '優待', date: d });
    }
  }
  const d = new Date(`${from.slice(0, 7)}-01T00:00:00Z`);
  while (true) {
    const monthEnd = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + 1, 0)).toISOString().slice(0, 10);
    if (monthEnd > to) {
      break;
    }
    if (monthEnd >= from && divMonths.includes(d.getUTCMonth() + 1)) {
      hits.push({ kind: '配当', date: monthEnd });
    }
    d.setUTCMonth(d.getUTCMonth() + 1);
  }
  return hits;
}

function monthOf(sec) {
  return Number(jstDate(sec).slice(5, 7));
}

function stripTags(s) {
  return decodeEntities(s.replace(/<br\s*\/?>/gi, ' ').replace(/<[^>]+>/g, '')).replace(/\s+/g, ' ').trim();
}

function decodeEntities(s) {
  return s
    .replace(/&#(\d+);/g, (_, n) => String.fromCodePoint(Number(n)))
    .replace(/&#x([0-9a-f]+);/gi, (_, n) => String.fromCodePoint(parseInt(n, 16)))
    .replace(/&nbsp;/g, ' ')
    .replace(/&quot;/g, '"')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&amp;/g, '&');
}

// ---------------------------------------------------------------- 小物

function codeOf(symbol) {
  return String(symbol || '').replace(/\.T$/, '');
}

function nameOf(quote) {
  // 全角の英数字・空白(「ＴＯＹＯ　ＴＩＲＥ」など)は半角にそろえる
  return (quote.longName || quote.shortName || codeOf(quote.symbol)).normalize('NFKC');
}

function clamp(v, lo, hi) {
  return Math.min(hi, Math.max(lo, v));
}

function round1(v) {
  return Math.round(v * 10) / 10;
}

function round2(v) {
  return Math.round(v * 100) / 100;
}

function num(v) {
  return Number.isFinite(v) ? round1(v) : null;
}

module.exports = {
  LOT, MIN_TURNOVER,
  jstDate, addBusinessDays, tickOf, floorTick, ceilTick,
  sma, rsi, atr, macd, fundamentals, parseChart, earningsInfo, reactionIndex,
  CROSS_TYPES, crossInfo, crossFromDiffs, matchCross, crossPoint, matchValuation, chartSeries, compareSince,
  analyzeShort, analyzeLong, analyzeMarket,
  parseDividends, parseIncentive, rightsDuring,
};
