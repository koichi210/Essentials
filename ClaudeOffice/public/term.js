// 会話欄(💬 会話ログ ⇄ 🖥 本物のターミナル)・別の窓・右パネルの「🖥 箱庭のターミナル」。
// xterm は index.html の通常の <script> で読み込む(window.Terminal / window.FitAddon.FitAddon)。
import { addResizeHandles } from './winframe.js';

const THEME = { background: '#1b1d2a', foreground: '#e5e7eb', cursor: '#d97757', cursorAccent: '#1b1d2a', selectionBackground: 'rgba(217,119,87,.35)' };
const FONT = '"Cascadia Mono", Consolas, "BIZ UDGothic", "MS Gothic", monospace';
const KEYS_HELP = 'Enter 送信・Shift+Enter 改行・選んで Ctrl+C コピー・Ctrl+V 貼り付け・Esc 中断';
const TERM_SIZE_KEY = 'claude-office.term-size';

let opts = { toast: () => {}, onChange: () => {}, focusDetail: () => false };
let termsState = { ready: true, error: '', terms: [], dirs: [] };
let selectedDir = '';

function esc(s) {
  return String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function post(url, body) {
  return fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body || {}) });
}

function fontSize() {
  const fs = Number(getComputedStyle(document.documentElement).getPropertyValue('--fs')) || 1;
  return Math.round(13 * fs);
}

function xtermReady() {
  return typeof window.Terminal === 'function' && window.FitAddon && typeof window.FitAddon.FitAddon === 'function';
}

function newXterm(extra) {
  const term = new window.Terminal({ fontFamily: FONT, fontSize: fontSize(), theme: THEME, ...extra });
  const fit = new window.FitAddon.FitAddon();
  term.loadAddon(fit);
  return { term, fit };
}

function copySelection(term, e) {
  // 選んでいるときの Ctrl+C はコピー(選んでいなければ素通し＝claude の中断)
  if (e.type === 'keydown' && e.ctrlKey && !e.shiftKey && !e.altKey && (e.key === 'c' || e.key === 'C') && term.hasSelection()) {
    navigator.clipboard?.writeText(term.getSelection()).catch(() => {});
    term.clearSelection();
    e.preventDefault();
    return true;
  }
  return false;
}

// ---------------------------------------------------------------- 本物のターミナルの画面(view)。1会話に1つだけ

const views = new Map(); // id -> view
const exitedAt = new Map(); // id -> 終わった時刻(数秒は古い term:true を信じない)

function createView(id) {
  const el = document.createElement('div');
  el.className = 'term-view';
  const { term, fit } = newXterm({ cursorBlink: true, scrollback: 5000 });
  const view = { id, el, term, fit, opened: false, exited: false, code: null, closed: false, ctrl: null, pending: '', sending: false, timer: 0, resizeTimer: 0, sent: '' };
  term.attachCustomKeyEventHandler((e) => {
    if (e.key === 'Enter' && e.shiftKey && !e.ctrlKey && !e.altKey) {
      // claude の改行は ESC+CR
      if (e.type === 'keydown') {
        sendInput(view, '\x1b\r');
      }
      e.preventDefault();
      return false;
    }
    if (copySelection(term, e)) {
      return false;
    }
    if (e.ctrlKey && !e.shiftKey && !e.altKey && (e.key === 'v' || e.key === 'V')) {
      return false; // ブラウザの貼り付けに任せる
    }
    return true;
  });
  term.onData((d) => sendInput(view, d));
  view.ro = new ResizeObserver(() => scheduleFit(view));
  view.ro.observe(el);
  views.set(id, view);
  connect(view);
  return view;
}

// 打った文字は 8ms ためてから順番どおりに送る(1文字ずつ POST しない)
function sendInput(view, d) {
  if (view.exited || view.closed) {
    return;
  }
  view.pending += d;
  if (!view.timer) {
    view.timer = setTimeout(() => {
      view.timer = 0;
      flushInput(view);
    }, 8);
  }
}

async function flushInput(view) {
  if (view.sending) {
    return;
  }
  view.sending = true;
  while (view.pending && !view.exited) {
    // 本文の上限(64KB)に収まるよう、日本語や ESC のエスケープで膨らんでも大丈夫な長さで区切る
    const d = view.pending.slice(0, 8000);
    view.pending = view.pending.slice(d.length);
    try {
      await post(`/api/term/${view.id}/input`, { d });
    } catch {
      view.pending = '';
    }
  }
  view.sending = false;
}

function sendResize(view, redraw) {
  const { cols, rows } = view.term;
  const key = `${cols}x${rows}`;
  if (!redraw && key === view.sent) {
    return;
  }
  view.sent = key;
  post(`/api/term/${view.id}/resize`, { cols, rows, redraw: !!redraw }).catch(() => {});
}

function tryFit(fit) {
  try {
    fit.fit(); // 隠れているときに投げる
  } catch {
    // 見えたときにやり直す
  }
}

function scheduleFit(view) {
  if (!view.opened || view.closed || !view.el.isConnected) {
    return;
  }
  tryFit(view.fit);
  clearTimeout(view.resizeTimer);
  view.resizeTimer = setTimeout(() => {
    if (!view.exited && !view.closed) {
      sendResize(view, false);
    }
  }, 120);
}

async function connect(view) {
  if (view.closed || view.exited) {
    return;
  }
  const ctrl = new AbortController();
  view.ctrl = ctrl;
  let first = true;
  try {
    const r = await fetch(`/api/term/${view.id}/stream`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}', signal: ctrl.signal });
    if (r.status === 404) {
      markExit(view, null, 'もう終わってる（サーバが再起動したのかも）');
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
        let ev;
        try {
          ev = JSON.parse(p);
        } catch {
          continue;
        }
        if (ev.t === 'hello') {
          view.term.reset();
          first = true;
        } else if (ev.t === 'data') {
          view.term.write(ev.d);
          if (first) {
            // 覚えている出力を流したあと、claude に今の大きさで描き直させる
            first = false;
            tryFit(view.fit);
            sendResize(view, true);
          }
        } else if (ev.t === 'exit') {
          markExit(view, ev.code);
          return;
        }
      }
    }
  } catch {
    // 切れた
  }
  if (!view.exited && !view.closed) {
    setTimeout(() => connect(view), 3000);
  }
}

function markExit(view, code, message) {
  if (view.exited) {
    return;
  }
  view.exited = true;
  view.code = code;
  exitedAt.set(view.id, Date.now());
  view.term.write(`\r\n\x1b[2m── ${message || `claude が終わったよ${code !== null && code !== undefined ? `（終了コード ${code}）` : ''}`} ──\x1b[0m\r\n`);
  const c = convos.get(view.id);
  if (c && c.view === view) {
    updateConvo(c);
    // 1.5 秒見せてから view を捨てて会話ログへ(話した分も読み直す)
    setTimeout(() => {
      if (convos.get(view.id) === c && c.view === view) {
        backToLog(c);
      }
    }, 1500);
  }
  const w = termWins.get(view.id);
  if (w && w.view === view) {
    updateTermWin(w);
  }
  refreshTerms();
  opts.onChange();
}

function placeView(view, parent) {
  if (view.el.parentNode !== parent) {
    parent.appendChild(view.el);
  }
  if (!view.opened) {
    view.opened = true;
    view.term.open(view.el);
  }
  requestAnimationFrame(() => {
    scheduleFit(view);
    view.term.refresh(0, view.term.rows - 1);
    view.term.focus();
  });
}

function disposeView(view) {
  if (view.closed) {
    return;
  }
  view.closed = true;
  view.ctrl?.abort();
  view.ro.disconnect();
  clearTimeout(view.timer);
  clearTimeout(view.resizeTimer);
  view.term.dispose();
  view.el.remove();
  if (views.get(view.id) === view) {
    views.delete(view.id);
  }
}

// ---------------------------------------------------------------- 会話欄(詳細の窓の中段)

const convos = new Map(); // id -> convo

function recentlyExited(id) {
  const t = exitedAt.get(id);
  return !!t && Date.now() - t < 6000;
}

export function mountConvo(slot, d) {
  if (!slot.isConnected) {
    return;
  }
  let c = convos.get(d.id);
  if (c && c.slot !== slot) {
    destroyConvo(c);
    c = null;
  }
  if (!c) {
    c = createConvo(slot, d.id);
  }
  c.flags = {
    gone: !!d.transcriptGone,
    term: !!d.term && !recentlyExited(d.id),
    live: !!d.live && !d.term,
    chat: !!d.chatRunning,
  };
  updateConvo(c);
}

export function unmountConvo(id) {
  const c = convos.get(id);
  if (c) {
    destroyConvo(c);
  }
}

function createConvo(slot, id) {
  slot.innerHTML = `<div class="convo"><div class="convo-bar"><span class="convo-mode"></span><span class="spacer"></span>
    <button class="btn talk" hidden title="この会話の claude をここで動かして、キーボードで話す">🖥 ここで話す</button>
    <button class="btn end" hidden>■ 終える</button></div>
    <div class="convo-body"></div><div class="convo-note"></div></div>`;
  const root = slot.firstElementChild;
  const c = {
    id, slot, root, body: root.querySelector('.convo-body'), note: root.querySelector('.convo-note'), modeEl: root.querySelector('.convo-mode'),
    talk: root.querySelector('.talk'), end: root.querySelector('.end'),
    mode: '', flags: {}, termHint: false, log: null, n: 0, h: '', timer: 0, polling: false, view: null, destroyed: false,
  };
  // 本体の「Esc で一番手前の窓を閉じる」に取られないようにする
  root.addEventListener('keydown', (e) => e.stopPropagation());
  c.talk.onclick = async () => {
    c.talk.disabled = true;
    const size = c.log ? { cols: c.log.term.cols, rows: c.log.term.rows } : {};
    const id2 = await startTerm({ sessionId: id, ...size });
    c.talk.disabled = false;
    if (id2 && !c.destroyed) {
      c.termHint = true;
      updateConvo(c);
    }
  };
  c.end.onclick = () => endTerm(id);
  convos.set(id, c);
  return c;
}

function destroyConvo(c) {
  c.destroyed = true;
  stopLog(c);
  if (c.view && c.view.el.parentNode === c.body) {
    disposeView(c.view);
  }
  c.view = null;
  if (convos.get(c.id) === c) {
    convos.delete(c.id);
  }
}

function updateConvo(c) {
  const f = c.flags;
  // 終わった view も 1.5 秒は見せる(backToLog が捨てるまでターミナルのまま)
  const wantTerm = c.view && !c.view.closed ? true : f.term || c.termHint;
  let mode;
  if (wantTerm) {
    mode = 'term';
  } else if (f.gone) {
    mode = 'gone';
  } else {
    mode = 'log';
  }
  if (mode === 'term') {
    enterTerm(c);
  } else if (mode === 'log') {
    enterLog(c);
  } else if (c.mode !== 'gone') {
    stopLog(c);
    c.mode = 'gone';
  }
  c.root.classList.toggle('gone', c.mode === 'gone');
  c.root.classList.toggle('is-term', c.mode === 'term');
  c.modeEl.textContent = c.mode === 'term' ? '🖥 本物のターミナル' : '💬 会話ログ';
  c.talk.hidden = !(c.mode === 'log' && !f.live && !f.chat && !f.gone && termsState.ready);
  c.end.hidden = !(c.mode === 'term' && c.view && !c.view.exited);
  let note = '';
  if (c.mode === 'gone') {
    note = '📦 元の記録が無いので、会話ログは出せないよ';
  } else if (c.mode === 'term') {
    note = KEYS_HELP;
  } else if (f.live) {
    note = '💻 VSCode（ほかのターミナル）で開いている会話だよ。ここは読むだけ。話すときはそっちで（閉じれば「🖥 ここで話す」が出るよ）';
  } else if (f.chat) {
    note = '⏳ 箱庭から送った返事を作ってる途中だよ';
  }
  if (c.note.textContent !== note) {
    c.note.textContent = note;
  }
}

function enterLog(c) {
  if (c.mode === 'log') {
    return;
  }
  c.mode = 'log';
  if (!xtermReady()) {
    c.body.textContent = 'xterm.js を読み込めませんでした';
    return;
  }
  const el = document.createElement('div');
  el.className = 'log-view';
  c.body.appendChild(el);
  const { term, fit } = newXterm({ disableStdin: true, convertEol: true, scrollback: 20000, cursorBlink: false });
  term.attachCustomKeyEventHandler((e) => !copySelection(term, e));
  term.open(el);
  term.write('\x1b[?25l');
  const ro = new ResizeObserver(() => tryFit(fit));
  ro.observe(el);
  c.log = { el, term, fit, ro };
  c.n = 0;
  c.h = '';
  requestAnimationFrame(() => tryFit(fit));
  pollLog(c);
  c.timer = setInterval(() => pollLog(c), 2000);
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

async function pollLog(c) {
  if (c.polling || c.mode !== 'log' || !c.log) {
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
      c.flags.gone = true;
      updateConvo(c);
      return;
    }
    c.flags.live = !!j.live && !j.term;
    c.flags.chat = !!j.chatRunning;
    c.flags.term = !!j.term && !recentlyExited(c.id);
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

function enterTerm(c) {
  if (c.mode === 'term' && c.view && !c.view.closed) {
    return;
  }
  stopLog(c);
  c.mode = 'term';
  if (!xtermReady()) {
    c.body.textContent = 'xterm.js を読み込めませんでした';
    return;
  }
  // 別の窓で開いていたら、窓を消して view をここへ動かす
  const w = termWins.get(c.id);
  if (w) {
    removeTermWin(w, false);
  }
  const view = views.get(c.id) || createView(c.id);
  c.view = view;
  placeView(view, c.body);
}

function backToLog(c) {
  if (c.view) {
    disposeView(c.view);
    c.view = null;
  }
  c.termHint = false;
  c.flags.term = false;
  c.mode = '';
  updateConvo(c);
  opts.onChange();
}

// ---------------------------------------------------------------- 別の窓(詳細の窓を開いていない会話用)

const termWins = new Map(); // id -> { id, el, view, title }
let termZ = 10;

function loadTermSize() {
  try {
    const j = JSON.parse(localStorage.getItem(TERM_SIZE_KEY) || 'null');
    return j && j.w && j.h ? j : null;
  } catch {
    return null;
  }
}

function openTermWin(id, title) {
  const existing = termWins.get(id);
  if (existing) {
    existing.el.hidden = false;
    existing.el.style.zIndex = String(++termZ);
    if (existing.view && !existing.view.closed) {
      placeView(existing.view, existing.body);
    }
    return;
  }
  if (!xtermReady()) {
    opts.toast('xterm.js を読み込めませんでした', true);
    return;
  }
  const el = document.createElement('section');
  el.className = 'panel term-win';
  const size = loadTermSize();
  const w0 = size ? parseInt(size.w, 10) : 720;
  const h0 = size ? parseInt(size.h, 10) : 440;
  const hud = document.getElementById('hud').getBoundingClientRect();
  const left = Math.max(0, Math.min(hud.right + 16, window.innerWidth / 3));
  const top = 40 + termWins.size * 28;
  el.style.left = `${Math.round(left)}px`;
  el.style.top = `${top}px`;
  el.style.width = `${Math.min(w0, window.innerWidth - left)}px`;
  el.style.height = `${Math.min(h0, window.innerHeight - top)}px`;
  el.style.zIndex = String(++termZ);
  el.innerHTML = `<div class="head"><span class="ttl ellipsis">🖥 ${esc(title || '(新しい会話)')}</span><span class="st sub"></span><span class="spacer"></span>
    <button class="btn hide" title="隠す（claude は動いたまま。右パネルの一覧から戻せるよ）">＿</button><button class="btn end">■ 終える</button></div>
    <div class="tw-body"></div><div class="tw-keys sub">${KEYS_HELP}</div>`;
  document.getElementById('term-windows').appendChild(el);
  const w = { id, el, body: el.querySelector('.tw-body'), view: null, title };
  termWins.set(id, w);
  el.addEventListener('keydown', (e) => e.stopPropagation());
  el.addEventListener('mousedown', () => {
    el.style.zIndex = String(++termZ);
  });
  el.querySelector('.hide').onclick = () => {
    el.hidden = true;
  };
  el.querySelector('.end').onclick = () => {
    if (w.view && w.view.exited) {
      removeTermWin(w, true);
    } else {
      endTerm(id);
    }
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
  addResizeHandles(el, { minW: 360, minH: 220 });
  // 大きさは保存する(開いた直後の1回は保存しない)
  let firstObserve = true;
  w.ro = new ResizeObserver(() => {
    if (firstObserve) {
      firstObserve = false;
      return;
    }
    try {
      localStorage.setItem(TERM_SIZE_KEY, JSON.stringify({ w: el.style.width, h: el.style.height }));
    } catch {
      // 保存できなくても動く
    }
  });
  w.ro.observe(el);
  w.view = views.get(id) || createView(id);
  placeView(w.view, w.body);
  updateTermWin(w);
}

function updateTermWin(w) {
  const exited = !w.view || w.view.exited;
  w.el.querySelector('.st').textContent = exited ? '・終わったよ' : '・動いてるよ';
  const end = w.el.querySelector('.end');
  end.textContent = exited ? '× 閉じる' : '■ 終える';
}

function removeTermWin(w, dispose) {
  w.ro?.disconnect();
  if (dispose && w.view) {
    disposeView(w.view);
  }
  w.el.remove();
  termWins.delete(w.id);
}

// ---------------------------------------------------------------- 起動・終了

async function startTerm(target) {
  try {
    const r = await post('/api/term/start', target);
    const j = await r.json();
    if (!r.ok || !j.ok) {
      opts.toast(`開けませんでした: ${j.error || r.status}`, true);
      return null;
    }
    exitedAt.delete(j.id);
    refreshTerms();
    opts.onChange();
    return j.id;
  } catch {
    opts.toast('サーバにつながりません', true);
    return null;
  }
}

function endTerm(id) {
  if (!window.confirm('この会話の claude を終えるよ。作業中なら途中で止まるけど、いい？（記録はそこまで残るよ）')) {
    return;
  }
  post(`/api/term/${id}/kill`, {}).catch(() => opts.toast('サーバにつながりません', true));
}

// 起動して、詳細の窓が開いていればその会話欄、無ければ別の窓で見せる
async function startAndShow(target, title) {
  const id = await startTerm(target);
  if (id) {
    showTerm(id, title);
  }
  return id;
}

export { startAndShow as startTerm };

export function showTerm(id, title) {
  if (opts.focusDetail(id)) {
    const c = convos.get(id);
    if (c) {
      c.termHint = true;
      exitedAt.delete(id);
      updateConvo(c);
      return;
    }
  }
  openTermWin(id, title);
}

// 文字サイズ(A)を変えたら全 xterm の fontSize を変えて合わせ直す
export function termFontChanged() {
  const size = fontSize();
  for (const v of views.values()) {
    v.term.options.fontSize = size;
    scheduleFit(v);
  }
  for (const c of convos.values()) {
    if (c.log) {
      c.log.term.options.fontSize = size;
      tryFit(c.log.fit);
    }
  }
}

// ---------------------------------------------------------------- 右パネル「🏢 在席」の上の「🖥 箱庭のターミナル」

let termsKey = '';

async function refreshTerms() {
  try {
    termsState = await (await fetch('/api/terms')).json();
  } catch {
    return;
  }
  const key = termsState.terms.filter((t) => !t.exited).map((t) => t.id).join(',');
  if (key !== termsKey) {
    // 起動・終了したら開いている詳細の窓を描き直す
    termsKey = key;
    opts.onChange();
  }
  renderTermBox();
}

function renderTermBox() {
  const box = document.querySelector('#side .term-box');
  if (!box) {
    return;
  }
  const s = termsState;
  const running = s.terms.filter((t) => !t.exited);
  if (!selectedDir || !s.dirs.some((d) => d.dir === selectedDir)) {
    selectedDir = s.dirs.length ? s.dirs[0].dir : '';
  }
  const html = `<div class="h">🖥 箱庭のターミナル</div>
    ${running.map((t) => `<button class="btn term-btn ellipsis" data-term="${esc(t.id)}" title="${esc(t.dir)}">🖥 <b>${esc(t.project)}</b> ${esc(t.title)}</button>`).join('')}
    ${s.ready ? `<div class="row term-new-row"><select class="term-dir">${s.dirs.map((d) => `<option value="${esc(d.dir)}" ${d.dir === selectedDir ? 'selected' : ''} title="${esc(d.dir)}">📁 ${esc(d.project)}</option>`).join('')}</select>
      <button class="btn term-new" ${s.dirs.length ? '' : 'disabled'}>＋ 新しい会話</button></div>`
    : `<div class="notice warn">ターミナルの部品(node-pty)を読み込めなかった: ${esc(s.error)}</div>`}`;
  if (box.__html !== html) {
    box.__html = html;
    box.innerHTML = html;
  }
}

export function initTerms(o) {
  opts = { ...opts, ...o };
  const side = document.getElementById('side');
  side.addEventListener('click', (e) => {
    const b = e.target.closest('.term-box [data-term]');
    if (b) {
      const t = termsState.terms.find((x) => x.id === b.dataset.term);
      showTerm(b.dataset.term, t ? `${t.project} ${t.title}` : '');
      return;
    }
    if (e.target.closest('.term-box .term-new')) {
      const dir = termsState.dirs.find((d) => d.dir === selectedDir);
      if (dir) {
        startAndShow({ cwd: dir.dir }, `${dir.project} (新しい会話)`);
      }
    }
  });
  side.addEventListener('change', (e) => {
    if (e.target.matches('.term-box .term-dir')) {
      selectedDir = e.target.value;
    }
  });
  refreshTerms();
  setInterval(refreshTerms, 5000);
}
