// 会話欄(💬 会話ログ ＋ 箱庭で話す)・別の窓・右パネルの「🗨 箱庭で話す」。
// 会話ログは記録(.jsonl)から組み立てた読むだけの xterm。話している間は、その下に
// 返事の途中経過・ツールの承認カード・入力欄を出す(サーバが stream-json で claude を動かしている)。
// xterm は index.html の通常の <script> で読み込む(window.Terminal / window.FitAddon.FitAddon)。
import { addResizeHandles } from './winframe.js';

const THEME = { background: '#1b1d2a', foreground: '#e5e7eb', cursor: '#1b1d2a', selectionBackground: 'rgba(217,119,87,.35)' };
const FONT = '"Cascadia Mono", Consolas, "BIZ UDGothic", "MS Gothic", monospace';
const KEYS_HELP = 'Enter 送信・Shift+Enter 改行・Esc 中断';
const WIN_SIZE_KEY = 'claude-office.talk-size';

let opts = { toast: () => {}, onChange: () => {}, focusDetail: () => false };
let talksState = { talks: [], dirs: [] };
let selectedDir = '';

function esc(s) {
  return String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function post(url, body) {
  return fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body || {}) });
}

async function postJson(url, body) {
  try {
    const r = await post(url, body);
    const j = await r.json().catch(() => ({}));
    if (!r.ok) {
      opts.toast(`できませんでした: ${j.error || r.status}`, true);
      return null;
    }
    return j;
  } catch {
    opts.toast('サーバにつながりません', true);
    return null;
  }
}

function fontSize() {
  const fs = Number(getComputedStyle(document.documentElement).getPropertyValue('--fs')) || 1;
  return Math.round(13 * fs);
}

function xtermReady() {
  return typeof window.Terminal === 'function' && window.FitAddon && typeof window.FitAddon.FitAddon === 'function';
}

function tryFit(fit) {
  try {
    fit.fit(); // 隠れているときに投げる
  } catch {
    // 見えたときにやり直す
  }
}

const exitedAt = new Map(); // id -> 終わった時刻(数秒は古い talk:true を信じない)

function recentlyExited(id) {
  const t = exitedAt.get(id);
  return !!t && Date.now() - t < 6000;
}

// ---------------------------------------------------------------- 会話欄

const convos = new Map(); // id -> convo

// 詳細の窓の中段。詳細を描き直すたびに呼ばれる(要素自体は作り直さない)
export function mountConvo(slot, d) {
  if (!slot.isConnected) {
    return;
  }
  let c = convos.get(d.id);
  if (c && c.slot !== slot) {
    // 同じ会話の別の窓が開いていたら、窓を消して詳細の窓の会話欄へ移す
    const w = c.inWindow ? talkWins.get(d.id) : null;
    if (w) {
      removeTalkWin(w);
    } else {
      destroyConvo(c);
    }
    c = null;
  }
  if (!c) {
    c = createConvo(slot, d.id, false);
  }
  c.flags = {
    gone: !!d.transcriptGone,
    talk: !!d.talk && !recentlyExited(d.id),
    live: !!d.live && !d.talk,
    chat: !!d.chatRunning,
  };
  updateConvo(c);
}

export function unmountConvo(id) {
  const c = convos.get(id);
  if (c && !c.inWindow) {
    destroyConvo(c);
  }
}

function createConvo(slot, id, inWindow) {
  slot.innerHTML = `<div class="convo"><div class="convo-bar"><span class="convo-mode">💬 会話ログ</span><span class="talk-status ellipsis"></span><span class="spacer"></span>
    <button class="btn talk" hidden title="この会話の claude を箱庭で動かして、ここから話す">💬 ここで話す</button>
    <button class="btn stop" hidden title="いまの返事を止める（Esc）">⏹ 中断</button>
    <button class="btn end" hidden>■ 終える</button></div>
    <div class="convo-body"><div class="log-wrap"></div><div class="talk-live" hidden></div><div class="talk-asks" hidden></div>
    <div class="talk-input" hidden><textarea rows="2" placeholder="💬 話しかける（Enter 送信・Shift+Enter 改行）"></textarea><button class="btn primary send">送信</button></div></div>
    <div class="convo-note"></div></div>`;
  const root = slot.firstElementChild;
  const q = (s) => root.querySelector(s);
  const c = {
    id, slot, root, inWindow,
    logWrap: q('.log-wrap'), liveEl: q('.talk-live'), asksEl: q('.talk-asks'), inputEl: q('.talk-input'), ta: q('textarea'),
    note: q('.convo-note'), modeEl: q('.convo-mode'), statusEl: q('.talk-status'),
    talkBtn: q('.talk'), stopBtn: q('.stop'), endBtn: q('.end'), sendBtn: q('.send'),
    flags: {}, talkHint: false, log: null, n: 0, h: '', timer: 0, polling: false, logMissing: false, talk: null, destroyed: false,
  };
  // 本体の「Esc で一番手前の窓を閉じる」に取られないようにする。Esc は返事の中断
  root.addEventListener('keydown', (e) => {
    e.stopPropagation();
    if (e.key === 'Escape' && c.talk && c.talk.busy) {
      e.preventDefault();
      interrupt(c);
    }
  });
  c.talkBtn.onclick = async () => {
    c.talkBtn.disabled = true;
    const id2 = await startTalk({ sessionId: id });
    c.talkBtn.disabled = false;
    if (id2 && !c.destroyed) {
      c.talkHint = true;
      updateConvo(c);
    }
  };
  c.stopBtn.onclick = () => interrupt(c);
  c.endBtn.onclick = () => {
    if (c.talk && c.talk.exited) {
      const w = talkWins.get(id);
      if (w) {
        removeTalkWin(w);
      }
    } else {
      endTalk(id);
    }
  };
  c.sendBtn.onclick = () => send(c);
  c.ta.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
      e.preventDefault();
      send(c);
    }
  });
  c.asksEl.addEventListener('click', (e) => onAskClick(c, e));
  convos.set(id, c);
  return c;
}

function destroyConvo(c) {
  c.destroyed = true;
  stopLog(c);
  closeTalk(c);
  if (convos.get(c.id) === c) {
    convos.delete(c.id);
  }
}

function updateConvo(c) {
  const f = c.flags;
  const wantTalk = c.talk ? true : (f.talk || c.talkHint) && !recentlyExited(c.id);
  if (wantTalk && !c.talk) {
    openTalk(c);
  }
  const gone = f.gone && !c.talk;
  if (gone) {
    stopLog(c);
  } else {
    startLog(c);
  }
  const t = c.talk;
  c.root.classList.toggle('gone', gone);
  c.root.classList.toggle('is-talk', !!t);
  c.modeEl.textContent = t ? '💬 箱庭で話してる' : '💬 会話ログ';
  c.talkBtn.hidden = !(!t && !f.live && !f.chat && !gone);
  c.stopBtn.hidden = !(t && !t.exited && t.busy);
  c.endBtn.hidden = !t || (t.exited && !c.inWindow);
  c.endBtn.textContent = t && t.exited ? '× 閉じる' : '■ 終える';
  c.inputEl.hidden = !t;
  c.ta.disabled = !t || t.exited;
  c.sendBtn.disabled = !t || t.exited;
  let note = '';
  if (gone) {
    note = '📦 元の記録が無いので、会話ログは出せないよ';
  } else if (t) {
    note = t.exited ? '' : KEYS_HELP;
  } else if (f.live) {
    note = '💻 VSCode（ほかのターミナル）で開いている会話だよ。ここは読むだけ。話すときはそっちで（閉じれば「💬 ここで話す」が出るよ）';
  } else if (f.chat) {
    note = '⏳ 箱庭から送った返事を作ってる途中だよ';
  }
  if (c.note.textContent !== note) {
    c.note.textContent = note;
  }
}

// ---------------------------------------------------------------- 会話ログ(読むだけの xterm)

function startLog(c) {
  if (c.log) {
    retimeLog(c);
    return;
  }
  if (!xtermReady()) {
    c.logWrap.textContent = 'xterm.js を読み込めませんでした';
    return;
  }
  const el = document.createElement('div');
  el.className = 'log-view';
  c.logWrap.appendChild(el);
  const term = new window.Terminal({ fontFamily: FONT, fontSize: fontSize(), theme: THEME, disableStdin: true, convertEol: true, scrollback: 20000, cursorBlink: false });
  const fit = new window.FitAddon.FitAddon();
  term.loadAddon(fit);
  term.attachCustomKeyEventHandler((e) => {
    // 選んでいるときの Ctrl+C はコピー
    if (e.type === 'keydown' && e.ctrlKey && (e.key === 'c' || e.key === 'C') && term.hasSelection()) {
      navigator.clipboard?.writeText(term.getSelection()).catch(() => {});
      return false;
    }
    return true;
  });
  term.open(el);
  term.write('\x1b[?25l');
  const ro = new ResizeObserver(() => tryFit(fit));
  ro.observe(el);
  c.log = { el, term, fit, ro, every: 0 };
  c.n = 0;
  c.h = '';
  c.logMissing = false;
  requestAnimationFrame(() => tryFit(fit));
  pollLog(c);
  retimeLog(c);
}

// 話している間は 1 秒ごと、それ以外は 2 秒ごとに続きを取る
function retimeLog(c) {
  const every = c.talk ? 1000 : 2000;
  if (c.log && c.log.every !== every) {
    c.log.every = every;
    clearInterval(c.timer);
    c.timer = setInterval(() => pollLog(c), every);
  }
}

function stopLog(c) {
  clearInterval(c.timer);
  c.timer = 0;
  if (c.log) {
    c.log.ro.disconnect();
    c.log.term.dispose();
    c.log.el.remove();
    c.log = null;
  }
}

function resetLog(c) {
  if (c.log) {
    c.log.term.reset();
    c.log.term.write('\x1b[?25l');
  }
  c.n = 0;
  c.h = '';
  c.logMissing = false;
}

async function pollLog(c) {
  if (c.polling || !c.log) {
    return;
  }
  c.polling = true;
  const log = c.log;
  try {
    const r = await fetch(`/api/session/${c.id}/log?n=${c.n}&h=${encodeURIComponent(c.h)}`);
    const j = await r.json();
    if (c.destroyed || c.log !== log) {
      return;
    }
    if (!r.ok) {
      if (c.talk) {
        // 新しい会話はまだ記録が無い
        if (!c.logMissing) {
          c.logMissing = true;
          log.term.write('\x1b[2m（まだ発言がないよ。下の欄から話しかけてね）\x1b[0m\n');
        }
      } else {
        c.flags.gone = true;
        updateConvo(c);
      }
      return;
    }
    if (c.logMissing) {
      resetLog(c);
    }
    c.flags.live = !!j.live && !j.talk;
    c.flags.chat = !!j.chatRunning;
    c.flags.talk = !!j.talk && !recentlyExited(c.id);
    if (j.reset) {
      log.term.reset();
      log.term.write('\x1b[?25l');
      if (j.skipped) {
        log.term.write(`\x1b[2m（古い ${j.skipped} 件は省略）\x1b[0m\n`);
      }
    }
    if (j.pieces.length) {
      log.term.write(j.pieces.map((p) => p.s).join(''));
      c.h = j.pieces[j.pieces.length - 1].u;
    } else if (!j.total) {
      c.h = '';
    }
    c.n = j.total;
    updateConvo(c);
  } catch {
    // 次の回
  } finally {
    c.polling = false;
  }
}

// ---------------------------------------------------------------- 箱庭で話す(stream をつないで途中経過・承認・入力)

function openTalk(c) {
  c.talk = { ctrl: null, exited: false, busy: false, asks: new Map(), live: new Map(), status: '' };
  connect(c, c.talk);
  retimeLog(c);
  requestAnimationFrame(() => c.ta.focus());
}

function closeTalk(c) {
  if (!c.talk) {
    return;
  }
  c.talk.closed = true;
  c.talk.ctrl?.abort();
  c.talk = null;
  c.liveEl.hidden = true;
  c.liveEl.textContent = '';
  c.asksEl.hidden = true;
  c.asksEl.innerHTML = '';
  c.statusEl.textContent = '';
  retimeLog(c);
}

async function connect(c, t) {
  if (t.closed || t.exited) {
    return;
  }
  const ctrl = new AbortController();
  t.ctrl = ctrl;
  try {
    const r = await fetch(`/api/talk/${c.id}/stream`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}', signal: ctrl.signal });
    if (r.status === 404) {
      onEvent(c, t, { t: 'exit', code: null, err: 'もう終わってる（サーバが再起動したのかも）' });
      return;
    }
    if (!r.ok) {
      throw new Error(String(r.status));
    }
    const reader = r.body.getReader();
    const dec = new TextDecoder();
    let buf = '';
    for (;;) {
      const { value, done } = await reader.read();
      if (done) {
        break;
      }
      buf += dec.decode(value, { stream: true });
      const parts = buf.split('\n');
      buf = parts.pop();
      for (const p of parts) {
        if (!p.trim()) {
          continue;
        }
        try {
          onEvent(c, t, JSON.parse(p));
        } catch {
          // 壊れた行
        }
        if (t.exited) {
          return;
        }
      }
    }
  } catch {
    // 切れた
  }
  if (!t.exited && !t.closed) {
    setTimeout(() => connect(c, t), 3000);
  }
}

function onEvent(c, t, ev) {
  if (t.closed || c.talk !== t) {
    return;
  }
  switch (ev.t) {
    case 'hello':
      t.busy = !!ev.busy;
      t.asks = new Map((ev.asks || []).map((a) => [a.rid, a]));
      t.live = new Map((ev.live || []).map((x) => [x.k, x.d]));
      t.status = t.busy ? '⏳ 考え中…' : '';
      renderAsks(c);
      break;
    case 'busy':
      t.busy = true;
      t.status = '⏳ 考え中…';
      break;
    case 'text':
      t.live.set(ev.k, ev.d);
      t.status = '✍️ 書いてる…';
      break;
    case 'textDone':
      for (const k of [...t.live.keys()]) {
        if (k.startsWith(`${ev.m}:`)) {
          t.live.delete(k);
        }
      }
      break;
    case 'tool':
      t.status = `⎿ ${ev.n}${ev.l ? ` ${ev.l}` : ''}`;
      break;
    case 'idle':
      t.busy = false;
      t.live.clear();
      t.status = ev.ok ? '' : `❌ ${ev.error || 'エラー'}`;
      break;
    case 'ask':
      t.asks.set(ev.ask.rid, ev.ask);
      renderAsks(c);
      break;
    case 'askDone':
      t.asks.delete(ev.rid);
      renderAsks(c);
      break;
    case 'exit':
      t.exited = true;
      t.busy = false;
      t.live.clear();
      t.asks.clear();
      renderAsks(c);
      t.status = `🌙 claude が終わったよ${ev.err ? `: ${ev.err}` : ''}`;
      exitedAt.set(c.id, Date.now());
      refreshTalks();
      opts.onChange();
      if (!c.inWindow) {
        // 1.5 秒見せてから会話ログだけに戻す(話した分も読み直す)
        setTimeout(() => {
          if (c.talk === t) {
            closeTalk(c);
            c.talkHint = false;
            c.flags.talk = false;
            resetLog(c);
            updateConvo(c);
          }
        }, 1500);
      }
      break;
    default:
      return;
  }
  renderLive(c);
  updateConvo(c);
}

function renderLive(c) {
  const t = c.talk;
  if (!t) {
    return;
  }
  c.statusEl.textContent = t.status;
  const text = [...t.live.values()].join('\n\n');
  c.liveEl.hidden = !text;
  if (c.liveEl.textContent !== text) {
    c.liveEl.textContent = text;
    c.liveEl.scrollTop = c.liveEl.scrollHeight;
  }
}

async function send(c) {
  const t = c.talk;
  const text = c.ta.value;
  if (!t || t.exited || !text.trim()) {
    return;
  }
  c.ta.value = '';
  const j = await postJson(`/api/talk/${c.id}/send`, { text });
  if (!j) {
    c.ta.value = text; // 送れなかった文は戻す
  }
}

function interrupt(c) {
  postJson(`/api/talk/${c.id}/interrupt`, {});
}

// ---------------------------------------------------------------- 承認カード(ツールを使っていい？・質問・計画)

function askHtml(a) {
  const head = `<div class="ask-h">🔐 <b>${esc(a.tool)}</b> を使っていい？ <span class="ask-desc">${esc(a.desc)}</span></div>`;
  if (a.questions) {
    const qs = a.questions.map((q, qi) => `<div class="ask-q" data-qi="${qi}"><div class="ask-qt">${q.header ? `<span class="chip">${esc(q.header)}</span> ` : ''}${esc(q.question)}</div>
      <div class="ask-opts">${q.options.map((o, oi) => `<button class="btn opt" data-qi="${qi}" data-oi="${oi}" title="${esc(o.description)}">${esc(o.label)}</button>`).join('')}</div>
      <input class="ask-other" data-qi="${qi}" placeholder="その他（自由に書く）"></div>`).join('');
    return `<div class="ask" data-rid="${esc(a.rid)}"><div class="ask-h">❓ <b>質問が来たよ</b></div>${qs}
      <div class="ask-btns"><button class="btn primary" data-act="answer">回答する</button><button class="btn" data-act="deny">🚫 答えない</button></div></div>`;
  }
  const plan = a.tool === 'ExitPlanMode';
  return `<div class="ask" data-rid="${esc(a.rid)}">${plan ? '<div class="ask-h">📋 <b>この計画で進めていい？</b></div>' : head}
    ${a.detail ? `<pre class="ask-detail">${esc(a.detail)}</pre>` : ''}
    <div class="ask-btns"><button class="btn primary" data-act="allow">✅ ${plan ? 'この計画で進める' : '許可'}</button>
    ${a.canAlways ? '<button class="btn" data-act="always" title="この会話の間は、同じ種類の操作を聞かずに許可する">✅ この会話中は許可</button>' : ''}
    <button class="btn" data-act="deny">🚫 ${plan ? 'まだ' : '許可しない'}</button></div></div>`;
}

function renderAsks(c) {
  const t = c.talk;
  const list = t ? [...t.asks.values()] : [];
  const key = list.map((a) => a.rid).join(',');
  if (c.asksEl.__key === key) {
    return;
  }
  c.asksEl.__key = key;
  c.asksEl.innerHTML = list.map(askHtml).join('');
  c.asksEl.hidden = !list.length;
  if (list.length) {
    // 承認待ちは見落とさないよう音の代わりに入力欄の上へ寄せて見せる
    c.asksEl.scrollTop = 0;
  }
}

function onAskClick(c, e) {
  const opt = e.target.closest('.opt');
  if (opt) {
    const box = opt.closest('.ask');
    const a = c.talk && c.talk.asks.get(box.dataset.rid);
    const q = a && a.questions && a.questions[Number(opt.dataset.qi)];
    if (q && !q.multiSelect) {
      box.querySelectorAll(`.opt[data-qi="${opt.dataset.qi}"]`).forEach((b) => b.classList.remove('on'));
    }
    opt.classList.toggle('on');
    return;
  }
  const b = e.target.closest('[data-act]');
  if (!b || !c.talk) {
    return;
  }
  const box = b.closest('.ask');
  const a = c.talk.asks.get(box.dataset.rid);
  if (!a) {
    return;
  }
  const act = b.dataset.act;
  const body = { rid: a.rid, allow: act !== 'deny', always: act === 'always' };
  if (act === 'answer') {
    const answers = {};
    a.questions.forEach((q, qi) => {
      const picked = [...box.querySelectorAll(`.opt.on[data-qi="${qi}"]`)].map((x) => q.options[Number(x.dataset.oi)].label);
      const other = box.querySelector(`.ask-other[data-qi="${qi}"]`).value.trim();
      if (other) {
        picked.push(other);
      }
      if (picked.length) {
        answers[q.question] = picked.join(', ');
      }
    });
    if (Object.keys(answers).length < a.questions.length) {
      opts.toast('すべての質問に答えてね', true);
      return;
    }
    body.answers = answers;
  }
  box.querySelectorAll('button').forEach((x) => {
    x.disabled = true;
  });
  postJson(`/api/talk/${c.id}/answer`, body);
}

// ---------------------------------------------------------------- 別の窓(詳細の窓を開いていない会話用)

const talkWins = new Map(); // id -> { id, el, body, convo, ro }
let winZ = 10;

function loadWinSize() {
  try {
    const j = JSON.parse(localStorage.getItem(WIN_SIZE_KEY) || 'null');
    return j && j.w && j.h ? j : null;
  } catch {
    return null;
  }
}

function openTalkWin(id, title) {
  const existing = talkWins.get(id);
  if (existing) {
    existing.el.hidden = false;
    existing.el.style.zIndex = String(++winZ);
    return;
  }
  const el = document.createElement('section');
  el.className = 'panel talk-win';
  const size = loadWinSize();
  const w0 = size ? parseInt(size.w, 10) : 620;
  const h0 = size ? parseInt(size.h, 10) : 520;
  const hud = document.getElementById('hud').getBoundingClientRect();
  const left = Math.max(0, Math.min(hud.right + 16, window.innerWidth / 3));
  const top = 40 + talkWins.size * 28;
  el.style.left = `${Math.round(left)}px`;
  el.style.top = `${top}px`;
  el.style.width = `${Math.min(w0, window.innerWidth - left)}px`;
  el.style.height = `${Math.min(h0, window.innerHeight - top)}px`;
  el.style.zIndex = String(++winZ);
  el.innerHTML = `<div class="head"><span class="ttl ellipsis">💬 ${esc(title || '(新しい会話)')}</span><span class="spacer"></span>
    <button class="btn hide" title="隠す（claude は動いたまま。右パネルの一覧から戻せるよ）">＿</button></div><div class="tw-body"></div>`;
  document.getElementById('talk-windows').appendChild(el);
  const w = { id, el, body: el.querySelector('.tw-body') };
  talkWins.set(id, w);
  el.addEventListener('mousedown', () => {
    el.style.zIndex = String(++winZ);
  });
  el.querySelector('.hide').onclick = () => {
    el.hidden = true;
  };
  // 見出しのドラッグで移動(画面の外には出さない)
  const head = el.querySelector('.head');
  head.addEventListener('pointerdown', (e) => {
    if (e.button !== 0 || e.target.closest('button')) {
      return;
    }
    head.setPointerCapture(e.pointerId);
    const sx = e.clientX - el.offsetLeft;
    const sy = e.clientY - el.offsetTop;
    const move = (ev) => {
      el.style.left = `${Math.max(0, Math.min(window.innerWidth - el.offsetWidth, ev.clientX - sx))}px`;
      el.style.top = `${Math.max(0, Math.min(window.innerHeight - el.offsetHeight, ev.clientY - sy))}px`;
    };
    const up = () => {
      head.removeEventListener('pointermove', move);
      head.removeEventListener('pointerup', up);
    };
    head.addEventListener('pointermove', move);
    head.addEventListener('pointerup', up);
  });
  addResizeHandles(el, { minW: 360, minH: 260 });
  // 大きさは保存する(開いた直後の1回は保存しない)
  let firstObserve = true;
  w.ro = new ResizeObserver(() => {
    if (firstObserve) {
      firstObserve = false;
      return;
    }
    try {
      localStorage.setItem(WIN_SIZE_KEY, JSON.stringify({ w: el.style.width, h: el.style.height }));
    } catch {
      // 保存できなくても動く
    }
  });
  w.ro.observe(el);
  const old = convos.get(id);
  if (old) {
    destroyConvo(old);
  }
  w.convo = createConvo(w.body, id, true);
  w.convo.flags = { gone: false, talk: true, live: false, chat: false };
  w.convo.talkHint = true;
  updateConvo(w.convo);
}

function removeTalkWin(w) {
  w.ro?.disconnect();
  if (w.convo) {
    destroyConvo(w.convo);
  }
  w.el.remove();
  talkWins.delete(w.id);
}

// ---------------------------------------------------------------- 起動・終了

async function startTalk(target) {
  const j = await postJson('/api/talk/start', target);
  if (!j || !j.ok) {
    return null;
  }
  exitedAt.delete(j.id);
  refreshTalks();
  opts.onChange();
  return j.id;
}

function endTalk(id) {
  if (!window.confirm('この会話の claude を終えるよ。作業中なら途中で止まるけど、いい？（記録はそこまで残るよ）')) {
    return;
  }
  postJson(`/api/talk/${id}/kill`, {});
}

// 詳細の窓が開いていればその会話欄、無ければ別の窓で見せる
export function showTalk(id, title) {
  if (opts.focusDetail(id)) {
    // 別の窓で開いていたら、窓を消して詳細の窓の会話欄へ移す
    const w = talkWins.get(id);
    if (w) {
      removeTalkWin(w);
    }
    const c = convos.get(id);
    if (c) {
      c.talkHint = true;
      exitedAt.delete(id);
      updateConvo(c);
    }
    opts.onChange();
    return;
  }
  openTalkWin(id, title);
}

// 文字サイズ(A)を変えたら会話ログの文字も変える
export function talkFontChanged() {
  const size = fontSize();
  for (const c of convos.values()) {
    if (c.log) {
      c.log.term.options.fontSize = size;
      tryFit(c.log.fit);
    }
  }
}

// ---------------------------------------------------------------- 右パネル「🏢 在席」の上の「💬 箱庭で話す」

let talksKey = '';

async function refreshTalks() {
  try {
    talksState = await (await fetch('/api/talks')).json();
  } catch {
    return;
  }
  const key = talksState.talks.filter((t) => !t.exited).map((t) => t.id).join(',');
  if (key !== talksKey) {
    // 始まった・終わったら開いている詳細の窓を描き直す
    talksKey = key;
    opts.onChange();
  }
  renderTalkBox();
}

function renderTalkBox() {
  const box = document.querySelector('#side .talk-box');
  if (!box) {
    return;
  }
  const s = talksState;
  const running = s.talks.filter((t) => !t.exited);
  if (!selectedDir || !s.dirs.some((d) => d.dir === selectedDir)) {
    selectedDir = s.dirs.length ? s.dirs[0].dir : '';
  }
  const html = `<div class="h">💬 箱庭で話す</div>
    ${running.map((t) => `<button class="btn talk-btn ellipsis" data-talk="${esc(t.id)}" title="${esc(t.dir)}">${t.asks ? '🔐' : t.busy ? '⏳' : '💬'} <b>${esc(t.project)}</b> ${esc(t.title)}</button>`).join('')}
    <div class="row talk-new-row"><select class="talk-dir">${s.dirs.map((d) => `<option value="${esc(d.dir)}" ${d.dir === selectedDir ? 'selected' : ''} title="${esc(d.dir)}">📁 ${esc(d.project)}</option>`).join('')}</select>
      <button class="btn talk-new" ${s.dirs.length ? '' : 'disabled'}>＋ 新しい会話</button></div>`;
  if (box.__html !== html) {
    box.__html = html;
    box.innerHTML = html;
  }
}

export function initTalks(o) {
  opts = { ...opts, ...o };
  // 確認用(window.__claudeOffice と同じく画面の外から状態を見る)
  window.__claudeOfficeTalk = { convos, talkWins };
  const side = document.getElementById('side');
  side.addEventListener('click', async (e) => {
    const b = e.target.closest('.talk-box [data-talk]');
    if (b) {
      const t = talksState.talks.find((x) => x.id === b.dataset.talk);
      showTalk(b.dataset.talk, t ? `${t.project} ${t.title}` : '');
      return;
    }
    if (e.target.closest('.talk-box .talk-new')) {
      const dir = talksState.dirs.find((d) => d.dir === selectedDir);
      if (dir) {
        const id = await startTalk({ cwd: dir.dir });
        if (id) {
          showTalk(id, `${dir.project} (新しい会話)`);
        }
      }
    }
  });
  side.addEventListener('change', (e) => {
    if (e.target.matches('.talk-box .talk-dir')) {
      selectedDir = e.target.value;
    }
  });
  refreshTalks();
  setInterval(refreshTalks, 5000);
}
