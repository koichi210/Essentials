// node --test test/analyze.test.js で実行する(依存なし・通信なし)
'use strict';

const test = require('node:test');
const assert = require('node:assert');
const a = require('../analyze.js');
const s = require('../server.js');

const DAY = 24 * 60 * 60;
// 2026-05-01(金) 15:30 JST から平日だけ並べた日足を作る
function makeBars(closes, volume = 1e6) {
  const bars = [];
  let t = Date.UTC(2026, 4, 1, 6, 30) / 1000;
  for (let i = 0; i < closes.length; i++) {
    while ([0, 6].includes(new Date((t + 9 * 3600) * 1000).getUTCDay())) {
      t += DAY;
    }
    const c = closes[i];
    const prev = i > 0 ? closes[i - 1] : c;
    bars.push({ t, date: a.jstDate(t), open: prev, high: Math.max(prev, c) + 5, low: Math.min(prev, c) - 5, close: c, volume });
    t += DAY;
  }
  return bars;
}

function quoteOf(extra = {}) {
  return { symbol: '1234.T', longName: 'テスト工業', regularMarketPrice: 1000, ...extra };
}

const lastSec = (bars) => bars[bars.length - 1].t + 3600;

test('呼値: 標準の刻みで切り下げ・切り上げ', () => {
  assert.strictEqual(a.tickOf(3000), 1);
  assert.strictEqual(a.tickOf(3001), 5);
  assert.strictEqual(a.floorTick(2999.6), 2999);
  assert.strictEqual(a.floorTick(3003), 3000);
  assert.strictEqual(a.ceilTick(4501), 4505);
  assert.strictEqual(a.ceilTick(4505), 4505);
  assert.strictEqual(a.floorTick(6517), 6510);
});

test('営業日の足し算は土日を飛ばす', () => {
  assert.strictEqual(a.addBusinessDays('2026-10-30', 1), '2026-11-02');
  assert.strictEqual(a.addBusinessDays('2026-10-30', 10), '2026-11-13');
});

test('移動平均・RSI・ATR', () => {
  assert.strictEqual(a.sma([1, 2, 3, 4], 2), 3.5);
  assert.strictEqual(a.sma([1, 2, 3, 4], 2, 2), 1.5);
  assert.strictEqual(a.sma([1], 2), null);
  assert.strictEqual(a.rsi(Array.from({ length: 20 }, (_, i) => 100 + i)), 100);
  const flat = makeBars(Array(20).fill(1000));
  assert.strictEqual(a.atr(flat, 14), 10);
});

test('日足の解析は値の欠けた日を捨てる', () => {
  const json = { chart: { result: [{ timestamp: [1, 2], indicators: { quote: [{ open: [1, null], high: [2, 2], low: [0, 0], close: [1.5, 1], volume: [10, 20] }] } }] } };
  const bars = a.parseChart(json);
  assert.strictEqual(bars.length, 1);
  assert.strictEqual(bars[0].close, 1.5);
  assert.deepStrictEqual(a.parseChart({}), []);
});

test('決算: 7日以内の過去と未来だけを拾い、15時以降の発表は翌営業日が反応日', () => {
  const now = Date.UTC(2026, 10, 2, 0, 0) / 1000;
  const past = Date.UTC(2026, 9, 30, 6, 30) / 1000;
  const next = Date.UTC(2026, 10, 5, 6, 30) / 1000;
  assert.deepStrictEqual(a.earningsInfo({ earningsTimestamp: past, earningsTimestampStart: next }, now), { past, next });
  assert.deepStrictEqual(a.earningsInfo({ earningsTimestamp: now + 30 * DAY }, now), { past: null, next: null });
  const bars = [{ date: '2026-10-30' }, { date: '2026-11-02' }];
  assert.strictEqual(a.reactionIndex(bars, past), 1);
  assert.strictEqual(a.reactionIndex(bars, Date.UTC(2026, 9, 30, 2, 0) / 1000), 0);
  assert.strictEqual(a.reactionIndex([{ date: '2026-10-30' }], past), -1);
});

// 100日かけて1000→1495まで上げ、最後の6日で下げて25日線に近づく
function pullbackCloses() {
  const up = Array.from({ length: 100 }, (_, i) => 1000 + i * 5);
  const down = Array.from({ length: 6 }, (_, i) => 1495 - (i + 1) * 12);
  return [...up, ...down];
}

test('短期: 上昇中の押し目を拾い、損切りは7%以内・利確は買いより上', () => {
  const bars = makeBars(pullbackCloses());
  const r = a.analyzeShort(quoteOf(), bars, lastSec(bars));
  assert.strictEqual(r.excluded, undefined);
  assert.strictEqual(r.setup, '押し目');
  assert.strictEqual(r.code, '1234');
  assert.strictEqual(a.analyzeLong({ ...quoteOf({ trailingPE: 10 }), longName: 'ＴＯＹＯ　ＴＩＲＥ' }).name, 'TOYO TIRE');
  assert.ok(r.buy.low <= r.buy.high && r.buy.high <= r.price);
  const entry = (r.buy.low + r.buy.high) / 2;
  assert.ok(r.stop < r.buy.low && r.stop >= entry * 0.93);
  assert.ok(r.target > r.buy.high);
  assert.strictEqual(r.deadline, a.addBusinessDays(bars[bars.length - 1].date, 10));
  assert.ok(r.score >= 80);
});

test('短期: 決算の直前・売買代金が少ない・日足が足りないものは除外', () => {
  const bars = makeBars(pullbackCloses());
  const now = lastSec(bars);
  assert.strictEqual(a.analyzeShort(quoteOf({ earningsTimestamp: now + 3 * DAY }), bars, now).excluded, '決算発表の直前');
  const thin = makeBars(pullbackCloses(), 100);
  assert.strictEqual(a.analyzeShort(quoteOf(), thin, now).excluded, '売買代金が少ない');
  assert.strictEqual(a.analyzeShort(quoteOf(), bars.slice(-50), now).excluded, 'データ不足');
});

test('短期: 下降トレンドは点が低い', () => {
  const bars = makeBars(Array.from({ length: 100 }, (_, i) => 2000 - i * 5));
  const r = a.analyzeShort(quoteOf(), bars, lastSec(bars));
  assert.ok(r.warnings.some((w) => w.label === '下降トレンド' && w.term === 'trend'));
  assert.ok(r.score < 30);
});

test('長期: 割安・高配当は点が高く、損切りは10%下・利確は20%上(52週高値が手前ならその手前)', () => {
  const q = quoteOf({ forwardPE: 8, priceToBook: 0.7, dividendYield: 4.5, fiftyDayAverage: 980, twoHundredDayAverage: 900, fiftyTwoWeekHigh: 1500 });
  const r = a.analyzeLong(q);
  assert.ok(r.score >= 75);
  assert.strictEqual(r.setup, '高配当');
  assert.deepStrictEqual(r.buy, { low: 980, high: 1000 });
  assert.strictEqual(r.stop, a.ceilTick(990 * 0.9));
  assert.strictEqual(r.target, a.floorTick(990 * 1.2));
  assert.strictEqual(r.dividendYen, 4500);
  const capped = a.analyzeLong({ ...q, fiftyTwoWeekHigh: 1100 });
  assert.strictEqual(capped.target, a.floorTick(1100 * 0.99));
});

test('長期: 赤字(PER なし)は除外、200日線を大きく割ると注意が付く', () => {
  assert.strictEqual(a.analyzeLong(quoteOf({ trailingPE: -5 })).excluded, '赤字または利益のデータなし');
  const r = a.analyzeLong(quoteOf({ trailingPE: 12, twoHundredDayAverage: 1300 }));
  assert.ok(r.warnings.some((w) => w.text.includes('割安の罠')));
});

test('MACD: 下げから上げに変わった直後だけゴールデンクロス', () => {
  const vShape = [...Array.from({ length: 60 }, (_, i) => 2000 - i * 10), ...Array.from({ length: 3 }, (_, i) => 1420 + (i + 1) * 40)];
  assert.strictEqual(a.macd(vShape).goldenCross, true);
  assert.strictEqual(a.macd(Array.from({ length: 60 }, (_, i) => 1000 + i * 5)).goldenCross, false);
  assert.strictEqual(a.macd([1, 2, 3]), null);
});

test('業績の指標: ROE・配当性向・アナリスト評価', () => {
  const f = a.fundamentals({ epsTrailingTwelveMonths: 120, bookValue: 1000, dividendRate: 48, averageAnalystRating: '1.8 - Buy' });
  assert.strictEqual(Math.round(f.roe), 12);
  assert.strictEqual(Math.round(f.payout), 40);
  assert.strictEqual(f.rating, 1.8);
  const loss = a.fundamentals({ epsTrailingTwelveMonths: -5, bookValue: 1000, dividendRate: 10 });
  assert.deepStrictEqual([loss.eps, loss.roe, loss.payout, loss.rating], [-5, null, null, null]);
});

test('長期: 押しポイントは値と意味を文章で出し、用語集の id を持つ', () => {
  const r = a.analyzeLong(quoteOf({ trailingPE: 8, priceToBook: 0.7, dividendYield: 4, epsTrailingTwelveMonths: 125, bookValue: 1430, dividendRate: 40, averageAnalystRating: '2.0 - Buy' }));
  const pbr = r.reasons.find((p) => p.term === 'pbr');
  assert.strictEqual(pbr.label, 'PBR 0.7倍');
  assert.ok(pbr.text.includes('約3割安い'));
  assert.ok(r.reasons.some((p) => p.term === 'roe'));
  assert.ok(r.reasons.some((p) => p.term === 'payout' && p.label === '配当性向 32%'));
  assert.ok(r.reasons.some((p) => p.term === 'analyst'));
  assert.strictEqual(r.stats.payout, 32);
  // 稼ぐ力が弱い1倍割れは割安の罠かもしれないと出す
  const weak = a.analyzeLong(quoteOf({ trailingPE: 30, priceToBook: 0.6, epsTrailingTwelveMonths: 20, bookValue: 1000 }));
  assert.ok(weak.warnings.some((p) => p.term === 'roe' && p.text.includes('割安の罠')));
  // 利益より多く配当していると注意
  const over = a.analyzeLong(quoteOf({ trailingPE: 12, dividendYield: 5, epsTrailingTwelveMonths: 40, dividendRate: 50 }));
  assert.ok(over.warnings.some((p) => p.term === 'payout'));
});

test('短期: 赤字は減点・ROE が高ければ加点', () => {
  const bars = makeBars(pullbackCloses());
  const now = lastSec(bars);
  const plain = a.analyzeShort(quoteOf(), bars, now);
  const loss = a.analyzeShort(quoteOf({ epsTrailingTwelveMonths: -10 }), bars, now);
  const good = a.analyzeShort(quoteOf({ epsTrailingTwelveMonths: 150, bookValue: 1000 }), bars, now);
  assert.ok(loss.score < plain.score && loss.warnings.some((p) => p.term === 'eps'));
  assert.ok(good.score >= plain.score && good.reasons.some((p) => p.label === 'ROE 15%'));
  assert.ok(plain.reasons.every((p) => p.label && p.text && p.term));
});

test('クロス: いちばん新しい交差と、もうすぐ交差しそうかを出す', () => {
  // 3営業日前に下から上へ抜けた
  assert.deepStrictEqual(a.crossFromDiffs([-3, -2, -1, 1, 2, 3, 4]), { last: { kind: 'gc', daysAgo: 3 }, soon: null });
  // 抜けたあと今日また下に抜けたら、新しい方(デッドクロス)
  assert.deepStrictEqual(a.crossFromDiffs([-1, 1, 2, -1]).last, { kind: 'dc', daysAgo: 0 });
  // 差が1日1ずつ縮んで残り2 → あと2日でゴールデンクロスしそう
  assert.deepStrictEqual(a.crossFromDiffs([-6, -5, -4, -3, -2]), { last: null, soon: { kind: 'gc', inDays: 2 } });
  // 縮み方が遅いと「もうすぐ」にはしない
  assert.strictEqual(a.crossFromDiffs([-10, -9.9, -9.8, -9.7]).soon, null);
  assert.deepStrictEqual(a.crossFromDiffs([6, 5, 4, 3, 2]).soon, { kind: 'dc', inDays: 2 });

  // 日足から: 下げ続けたあと急に上げると 5日線が25日線を上抜く
  const closes = [...Array.from({ length: 60 }, (_, i) => 2000 - i * 10), ...Array.from({ length: 8 }, (_, i) => 1410 + (i + 1) * 60)];
  const c = a.crossInfo(closes, 'ma5_25');
  assert.strictEqual(c.type, 'ma5_25');
  assert.strictEqual(c.last.kind, 'gc');
  assert.ok(c.last.daysAgo < 8);
  assert.strictEqual(a.crossInfo(closes, 'macd').last.kind, 'gc');
});

test('クロスの条件: N日以内(今日を1日目)・もうすぐも含む・もうすぐだけ', () => {
  const gc2 = { last: { kind: 'gc', daysAgo: 2 }, soon: null };
  assert.strictEqual(a.matchCross(gc2, 'gc', null), true);
  assert.strictEqual(a.matchCross(gc2, 'gc', { days: 3, soon: false }), true);
  assert.strictEqual(a.matchCross(gc2, 'gc', { days: 2, soon: false }), false);
  assert.strictEqual(a.matchCross(gc2, 'dc', { days: 5, soon: true }), false);
  const soonDc = { last: { kind: 'gc', daysAgo: 15 }, soon: { kind: 'dc', inDays: 2 } };
  assert.strictEqual(a.matchCross(soonDc, 'dc', { days: 5, soon: true }), true);
  assert.strictEqual(a.matchCross(soonDc, 'dc', { days: 5, soon: false }), false);
  assert.strictEqual(a.matchCross(soonDc, 'dc', { days: null, soon: true }), true);
  // 除く: 条件に当たる銘柄は false、当たらない銘柄は true
  assert.strictEqual(a.matchCross(soonDc, 'dc', { days: 5, soon: true, not: true }), false);
  assert.strictEqual(a.matchCross(soonDc, 'dc', { days: 5, soon: false, not: true }), true);
  assert.strictEqual(a.matchCross(gc2, 'dc', { days: 20, soon: true, not: true }), true);
  const pts = a.crossPoint({ type: 'ma5_25', last: { kind: 'gc', daysAgo: 0 }, soon: null });
  assert.strictEqual(pts[0].good, true);
  assert.ok(pts[0].label.includes('5日線と25日線') && pts[0].text.startsWith('今日'));
});

test('PER・PBR の絞り込み: PER は以下、PBR は未満、値が無ければ外す', () => {
  const q = { forwardPE: 12, priceToBook: 0.9 };
  assert.strictEqual(a.matchValuation(q, { per: null, pbr: null }), true);
  assert.strictEqual(a.matchValuation(q, { per: 12, pbr: 1 }), true);
  assert.strictEqual(a.matchValuation(q, { per: 10, pbr: null }), false);
  assert.strictEqual(a.matchValuation(q, { per: null, pbr: 0.9 }), false);
  assert.strictEqual(a.matchValuation({ trailingPE: -3, priceToBook: 0.5 }, { per: 20, pbr: null }), false);
});

test('グラフ用: 表示する本数だけ切り出し、線と交差の位置を付ける', () => {
  const bars = makeBars([...Array.from({ length: 40 }, (_, i) => 2000 - i * 10), ...Array.from({ length: 10 }, (_, i) => 1610 + (i + 1) * 40)]);
  const s5 = a.chartSeries(bars, [5, 25], 30);
  assert.strictEqual(s5.bars.length, 30);
  assert.strictEqual(s5.ma[5].length, 30);
  assert.strictEqual(s5.bars[29].c, bars[49].close);
  assert.strictEqual(s5.ma[5][29], a.sma(bars.map((b) => b.close), 5));
  assert.ok(s5.crosses.some((x) => x.kind === 'gc' && x.index > 20));
  // 線を引くのに足りない前半は null
  assert.strictEqual(a.chartSeries(bars, [5, 25], 50).ma[25][0], null);
  // pc は前の足の終値(いちばん古い足は null)
  assert.strictEqual(s5.bars[0].pc, bars[19].close);
  assert.strictEqual(a.chartSeries(bars, [5, 25], 50).bars[0].pc, null);
});

test('地合い: 25日線・75日線との位置で3段階', () => {
  const up = makeBars(Array.from({ length: 100 }, (_, i) => 1000 + i * 5));
  assert.strictEqual(a.analyzeMarket(up).level, 'good');
  const down = makeBars(Array.from({ length: 100 }, (_, i) => 2000 - i * 5));
  assert.strictEqual(a.analyzeMarket(down).level, 'bad');
  assert.strictEqual(a.analyzeMarket(up.slice(-10)), null);
});

test('調査の条件の検証', () => {
  const p = (q) => s.parseScan(new URLSearchParams(q));
  assert.deepStrictEqual(p('mode=long&max=700000&min=300000'), { mode: 'long', max: 700000, min: 300000, per: null, pbr: null, cross: 'ma5_25', gc: null, dc: null });
  assert.strictEqual(p('max=700000').mode, 'short');
  assert.ok(p('max=5000').error);
  assert.ok(p('max=300000&min=300000').error);
  assert.ok(p('max=abc').error);
  const f = p('max=700000&per=15&pbr=1&cross=ma25_75&gc=5&gcSoon=1&dc=soon');
  assert.deepStrictEqual([f.per, f.pbr, f.cross, f.gc, f.dc], [15, 1, 'ma25_75', { days: 5, soon: true }, { days: null, soon: true }]);
  assert.deepStrictEqual([p('max=700000').per, p('max=700000').gc, p('max=700000').cross], [null, null, 'ma5_25']);
  assert.ok(p('max=700000&per=abc').error);
  assert.ok(p('max=700000&cross=constructor').error);
  assert.ok(p('max=700000&gc=99').error);
  assert.deepStrictEqual(p('max=700000&dc=not10&dcSoon=1').dc, { days: 10, soon: true, not: true });
  assert.ok(p('max=700000&dc=not99').error);
  assert.ok(p('max=700000&dc=notx').error);
});

test('配当: 履歴から権利確定月・直近1年の合計・次の確定月', () => {
  const at = (y, m, d) => Date.UTC(y, m - 1, d) / 1000;
  const json = { chart: { result: [{ events: { dividends: {
    a: { amount: 65, date: at(2024, 12, 27) },
    b: { amount: 60, date: at(2025, 6, 27) },
    c: { amount: 60, date: at(2025, 12, 29) },
    d: { amount: 60, date: at(2026, 6, 29) },
  } } }] } };
  assert.deepStrictEqual(a.parseDividends(json, at(2026, 11, 2)), { months: [6, 12], last12: 120, next: 12 });
  assert.deepStrictEqual(a.parseDividends(json, at(2027, 1, 10)).next, 6);
  assert.deepStrictEqual(a.parseDividends({}, at(2026, 11, 2)), { months: [], last12: 0, next: null });
});

const INCENTIVE_HTML = `
<table><tbody><tr><th scope="row">権利付き最終日</th><td>2027年3月29日、2027年9月28日</td></tr>
<tr><th scope="row">単元株数</th><td>100株</td></tr><tr><th scope="row">優待の種類</th><td>買い物券・プリペイドカード</td></tr></tbody></table>
<section><header><div><h2>株主優待の内容</h2></div></header>
<div><header><div><h3>自社商品券（1,000円券）</h3></div></header><table><tbody><tr><th scope="row">200株以上</th><td>1枚</td></tr></tbody></table></div>
<div><p>上記に加え、1年以上継続保有&amp;条件*</p><table><tbody><tr><th scope="row">1,000株以上</th><td>3枚加算</td></tr></tbody></table>
<p>*6か月以上の保有でも可</p><p> </p></div></section><section><h2>人気ランキング</h2><p>継続 関係ない段落</p></section>`;

test('優待: 権利付き最終日・種類・内容・最低株数・継続保有の条件を取り出す', () => {
  const y = a.parseIncentive(INCENTIVE_HTML);
  assert.strictEqual(y.none, false);
  assert.strictEqual(y.kinds, '買い物券・プリペイドカード');
  assert.deepStrictEqual(y.lastDates, ['2027-03-29', '2027-09-28']);
  assert.deepStrictEqual(y.months, [3, 9]);
  assert.strictEqual(y.minShares, 200);
  assert.deepStrictEqual(y.lines[0], { kind: 'title', text: '自社商品券（1,000円券）' });
  assert.deepStrictEqual(y.lines[1], { kind: 'row', shares: '200株以上', text: '1枚' });
  assert.deepStrictEqual(y.continuity.lines, ['上記に加え、1年以上継続保有&条件*']);
  assert.strictEqual(y.continuity.span, '1年以上');
  // 空の段落と、内容のセクションの外(ランキング)は拾わない
  assert.ok(y.lines.every((l) => l.text || l.shares));
  assert.ok(!y.lines.some((l) => l.text.includes('関係ない')));
  assert.deepStrictEqual(a.parseIncentive('<p>株主優待情報はありません</p>'), { none: true });
  assert.strictEqual(a.parseIncentive('<html>形が違うページ</html>'), null);
});

test('短期の保有中に来る権利確定(優待は権利付き最終日、配当は月末)', () => {
  assert.deepStrictEqual(a.rightsDuring('2026-11-20', '2026-12-04', [11, 5], []), [{ kind: '配当', date: '2026-11-30' }]);
  assert.deepStrictEqual(a.rightsDuring('2026-11-02', '2026-11-16', [11, 5], []), []);
  assert.deepStrictEqual(a.rightsDuring('2027-02-15', '2027-03-01', [3], ['2027-02-24', '2027-08-26']), [{ kind: '優待', date: '2027-02-24' }]);
});

test('スナップショット: その後の値段と、利確・損切りのどちらに先に届いたか', () => {
  const bars = makeBars([1000, 1010, 1020, 1060, 1100, 1080]);
  const since = bars[1].date;
  const item = { price: 1010, target: 1090, stop: 950 };
  const c = a.compareSince(item, bars, since);
  assert.strictEqual(c.price, 1080);
  assert.strictEqual(c.date, bars[5].date);
  assert.strictEqual(c.change, 6.9);
  assert.strictEqual(c.days, 4);
  // makeBars の高値は前日と当日の終値の高い方+5。1060 の日は高値 1065 で届かず、1100 の日(高値 1105)に届く
  assert.deepStrictEqual(c.hit, { kind: 'target', date: bars[4].date });
  assert.strictEqual(c.high, 1105);
  assert.strictEqual(c.low, 1005);
  assert.deepStrictEqual(a.compareSince({ ...item, target: 1024, stop: 1006 }, bars, since).hit, { kind: 'both', date: bars[2].date });
  assert.deepStrictEqual(a.compareSince({ ...item, target: 2000, stop: 1012 }, bars, since).hit, { kind: 'stop', date: bars[2].date });
  assert.strictEqual(a.compareSince({ ...item, target: 2000 }, bars, since).hit, null);
  // 保存した日より後の足が無ければ、高値・安値は null
  assert.strictEqual(a.compareSince(item, bars, bars[5].date).high, null);
  assert.strictEqual(a.compareSince(item, [], since), null);
});

test('スナップショット: 保存できるのは調べた結果の形をしたものだけ', () => {
  const ok = { mode: 'short', at: '2026-11-04T01:00:00.000Z', items: [{ code: '7203', price: 3000, target: 3200, stop: 2900 }] };
  assert.strictEqual(s.isScanResult(ok), true);
  assert.strictEqual(s.isScanResult({ ...ok, mode: 'x' }), false);
  assert.strictEqual(s.isScanResult({ ...ok, items: [{ code: '../x', price: 1, target: 1, stop: 1 }] }), false);
  assert.strictEqual(s.isScanResult({ ...ok, items: [{ code: '7203', price: 'a', target: 1, stop: 1 }] }), false);
  assert.strictEqual(s.isScanResult(null), false);
});
