// HUD・右パネル(在席/グループ/履歴)・タイムライン・詳細の窓・トースト
import { projectColor, timeBand } from './world.js';
import { deriveState, progressBadge, toolEmoji, elapsed, STATE_ORDER } from './people.js';
import { createChat } from './chat.js';

export function esc(s) {
  return String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

const WEEK = '日月火水木金土';
const pad = (n) => String(n).padStart(2, '0');

export function hhmm(ms) {
  const d = new Date(ms);
  return `${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

export function dateLabel(ms) {
  const d = new Date(ms);
  return `${d.getFullYear()}/${d.getMonth() + 1}/${d.getDate()}（${WEEK[d.getDay()]}）`;
}

export function dayKeyOf(ms) {
  const d = new Date(ms);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

function setIfChanged(el, html) {
  // 数秒ごとに描き直すので、同じ HTML なら差し替えない(スクロールやホバーを壊さない)
  if (el.__html !== html) {
    el.__html = html;
    el.innerHTML = html;
  }
}

// ---------------------------------------------------------------- トースト

export function toast(message, isError = false) {
  const el = document.createElement('div');
  el.className = `toast${isError ? ' err' : ''}`;
  el.textContent = message;
  document.getElementById('toasts').appendChild(el);
  setTimeout(() => el.remove(), isError ? 6000 : 3500);
}

export async function openInVscode(id) {
  try {
    const r = await fetch(`/api/open/${id}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' });
    const j = await r.json();
    if (r.ok) {
      toast('💻 VSCode で開いています…');
    } else {
      toast(`開けませんでした: ${j.error || r.status}`, true);
    }
  } catch {
    toast('サーバにつながりません', true);
  }
}

// ---------------------------------------------------------------- HUD(左上)

function meter(label, u) {
  if (!u) {
    return '';
  }
  const cls = u.percent >= 90 ? 'high' : u.percent >= 70 ? 'mid' : '';
  let reset = '';
  if (u.resetsAt) {
    const left = Math.max(0, u.resetsAt - Date.now());
    const h = Math.floor(left / 3600000);
    const m = Math.floor((left % 3600000) / 60000);
    const d = new Date(u.resetsAt);
    const span = h >= 24 ? `${Math.floor(h / 24)}日${h % 24}時間` : `${h}時間${m}分`;
    reset = `<div class="sub" style="margin-left:2.4em">⏳ あと${span}でリセット（${d.getMonth() + 1}/${d.getDate()}（${WEEK[d.getDay()]}） ${hhmm(u.resetsAt)}）</div>`;
  }
  return `<div class="row"><span style="width:2em">${label}</span><div class="meter ${cls}"><i style="width:${Math.min(100, u.percent)}%"></i></div><span style="width:3em;text-align:right">${u.percent}%</span></div>${reset}`;
}

export function renderHUD(store, actions) {
  const el = document.getElementById('hud');
  if (!el.__built) {
    el.__built = true;
    el.innerHTML = `
      <div class="row"><span class="logo"><span class="star">✳</span> Claude Office</span><span class="spacer"></span><span class="conn"></span><button class="btn gear" title="設定">⚙️</button></div>
      <div class="row"><div class="clock"></div><span class="spacer"></span><button class="btn fs-up" title="文字を大きく">A</button><button class="btn fs-down" title="文字を小さく" style="font-size:0.8em">A</button></div>
      <div class="date sub"></div>
      <div class="row chips" style="margin-top:6px;flex-wrap:wrap"></div>
      <hr class="sep"><div class="acct"></div>
      <div class="settings" hidden></div>`;
    el.querySelector('.gear').onclick = () => {
      const s = el.querySelector('.settings');
      s.hidden = !s.hidden;
    };
    el.querySelector('.fs-up').onclick = () => actions.fontStep(1);
    el.querySelector('.fs-down').onclick = () => actions.fontStep(-1);
    el.querySelector('.settings').addEventListener('click', (e) => {
      const b = e.target.closest('[data-set]');
      if (b) {
        actions.setOption(b.dataset.set, b.dataset.val);
      }
    });
    el.querySelector('.settings').addEventListener('change', (e) => {
      if (e.target.matches('select[data-skin]')) {
        actions.setOption('skin', e.target.value);
      }
    });
  }
  const now = new Date();
  const conn = el.querySelector('.conn');
  conn.className = `conn ${store.connected ? 'ok' : 'ng'}`;
  conn.textContent = store.connected ? '● 接続中' : '● サーバ停止中';
  el.querySelector('.clock').textContent = `${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}`;
  el.querySelector('.date').textContent = `${now.getFullYear()}/${now.getMonth() + 1}/${now.getDate()}（${WEEK[now.getDay()]}）`;
  const band = timeBand(store.viewHour);
  const live = store.state ? store.state.live : [];
  const working = live.filter((l) => l.live.status === 'busy').length;
  setIfChanged(el.querySelector('.chips'), `<span class="chip">${band.icon} ${band.name}</span><span class="chip">👥 ${live.length}人在席・⚡ ${working}人稼働</span>${store.previewHour !== null ? `<span class="chip">🕰 ${pad(Math.floor(store.previewHour))}:${pad(Math.round((store.previewHour % 1) * 60))} を表示中</span>` : ''}`);
  const a = store.account || {};
  setIfChanged(el.querySelector('.acct'), `<div class="row" style="margin-bottom:4px">👤 <span class="ellipsis">${esc(a.email || '(未ログイン)')}</span></div>
    ${a.usage ? meter('5h', a.usage.fiveHour) + meter('週', a.usage.sevenDay) : ''}
    ${a.error ? `<div class="sub">⚠️ ${esc(a.error)}</div>` : ''}`);
  const o = store.options;
  const seg = (key, items) => `<div class="seg">${items.map(([v, label]) => `<button class="btn ${String(o[key]) === v ? 'on' : ''}" data-set="${key}" data-val="${v}">${label}</button>`).join('')}</div>`;
  setIfChanged(el.querySelector('.settings'), `<hr class="sep"><div class="h">⚙️ 設定</div>
    <div class="row"><span class="label">🎨 スキン</span><select data-skin>
      <option value="default" ${o.skin === 'default' ? 'selected' : ''}>🏫 教室（Default）</option>
      <option value="isekai" ${o.skin === 'isekai' ? 'selected' : ''}>🍺 ルイーダの酒場</option>
      <option value="makai" ${o.skin === 'makai' ? 'selected' : ''}>💀 魔界</option></select></div>
    <div class="row"><span class="label">🔔 通知</span><button class="btn" style="flex:1" data-set="notify" data-val="open">設定画面を開く</button></div>
    <div class="row"><span class="label">⚡ 動作</span>${seg('mode', [['standard', '☀️ 標準'], ['light', '🍃 軽量'], ['ultra', '🪶 超軽量']])}</div>
    <div class="sub" style="margin:2px 0 4px">${o.mode === 'custom' ? '（カスタム）' : ''} ▼細かく設定</div>
    <div class="row"><span class="label">✨ エフェクト</span>${seg('effects', [['on', '✨ ON'], ['off', '💤 OFF']])}</div>
    <div class="row"><span class="label">🔗 FPS</span>${seg('fps', [['60', '60 fps'], ['30', '30 fps']])}</div>
    <div class="row"><span class="label">🌓 影</span>${seg('shadows', [['on', '🌓 ON'], ['off', '💤 OFF']])}</div>
    <div class="row"><span class="label">🖼️ 画質</span>${seg('resolution', [['high', '🔍 くっきり'], ['low', '🍃 粗め']])}</div>`);
}

// ---------------------------------------------------------------- 右パネル

function stateChip(st) {
  return `<span class="chip state-${st.key}">${st.emoji} ${st.name}</span>`;
}

function badgeHtml(p) {
  const b = progressBadge(p);
  return b ? `<span class="chip badge ${b.cls}" title="${esc(p.last || '')}">${b.icon} ${esc(b.text)}</span>` : '';
}

function liveCard(item, now) {
  const st = deriveState(item, now);
  const color = projectColor(item.project);
  const subs = item.subagents && item.subagents.length ? `<span class="chip">🐣×${item.subagents.length}</span>` : '';
  const badge = st.key === 'working' || st.key === 'thinking' ? '' : badgeHtml(item.progress);
  return `<div class="card" data-id="${item.id}"><div class="band" style="background:${color}"></div>
    <div class="row"><b class="ellipsis">${esc(item.project)}</b><span class="ellipsis sub">${esc(item.live.name || '')}</span><span class="spacer"></span>${stateChip(st)}</div>
    <div class="t">${esc(item.title || item.firstPrompt || '(無題)')}</div>
    <div class="row sub"><span class="ellipsis">${st.emoji} ${esc(st.action || '')}</span><span class="spacer"></span><span>${elapsed(now - (st.since || now))}</span></div>
    <div class="row" style="margin-top:3px;gap:4px">${subs}${badge}</div></div>`;
}

function summaryHtml(stats, label) {
  const s = stats || { sessions: 0, prompts: 0, toolCalls: 0, tools: {}, activeMs: 0 };
  const top = Object.entries(s.tools || {}).sort((a, b) => b[1] - a[1]).slice(0, 6);
  const max = top.length ? top[0][1] : 1;
  return `<div class="h">📋 ${esc(label)}のまとめ</div>
    <div class="stats4"><div><b>${s.sessions}</b>💬 会話</div><div><b>${s.prompts}</b>🙋 依頼</div><div><b>${s.toolCalls}</b>🛠️ ツール</div><div><b>${(s.activeMs / 3600000).toFixed(1)}h</b>⏱️ 稼働</div></div>
    <div class="bars" style="margin-top:8px">${top.map(([n, v]) => `<div class="bar"><span>${toolEmoji(n)} ${esc(n)}</span><i style="width:${(130 * v) / max}px"></i><span class="sub">${v}</span></div>`).join('')}</div>`;
}

function itemRow(s, now) {
  const st = s.isLive ? '🟢 ' : s.transcriptGone ? '📦 ' : '';
  return `<div class="item row" data-id="${s.id}"><span class="dot" style="background:${projectColor(s.project)}"></span><span class="ellipsis" style="flex:1">${st}${esc(s.title || s.firstPrompt || '(無題)')}</span><span class="sub">${s.end ? hhmm(s.end) : ''}</span></div>`;
}

function match(s, q) {
  if (!q) {
    return true;
  }
  return `${s.title} ${s.firstPrompt} ${s.lastPrompt} ${s.project} ${s.branch}`.toLowerCase().includes(q);
}

function groupsHtml(store) {
  const data = store.sessions;
  if (!data) {
    return '<div class="sub">読み込み中…</div>';
  }
  const q = store.search.trim().toLowerCase();
  const byId = new Map(data.sessions.map((s) => [s.id, s]));
  const grouped = new Set();
  const isOpen = (key, def) => (store.openGroups.has(key) ? !def : def) || !!q;
  let html = '';
  for (const [ws, groups] of Object.entries(data.groups)) {
    const wsName = ws.split(/[\\/]/).filter(Boolean).pop() || ws;
    const wsHit = q && wsName.toLowerCase().includes(q);
    let inner = '';
    for (const g of groups) {
      const items = g.sessionIds.map((id) => byId.get(id)).filter(Boolean);
      items.forEach((s) => grouped.add(s.id));
      const gHit = wsHit || (q && g.name.toLowerCase().includes(q));
      const shown = items.filter((s) => gHit || match(s, q));
      if (q && !shown.length) {
        continue;
      }
      const key = `g:${ws}:${g.id}`;
      inner += `<details data-key="${esc(key)}" ${isOpen(key, true) ? 'open' : ''}><summary>🗂️ ${esc(g.name)} <span class="sub">${items.length}</span></summary>${shown.map((s) => itemRow(s)).join('')}</details>`;
    }
    if (q && !inner) {
      continue;
    }
    const key = `w:${ws}`;
    html += `<details data-key="${esc(key)}" ${isOpen(key, true) ? 'open' : ''}><summary>🏢 ${esc(wsName)}</summary>${inner || '<div class="sub item">グループなし</div>'}</details>`;
  }
  const hidden = new Set(data.hidden);
  const loose = data.sessions.filter((s) => !grouped.has(s.id) && !hidden.has(s.id) && match(s, q));
  const archived = data.sessions.filter((s) => hidden.has(s.id) && match(s, q));
  html += `<details data-key="loose" ${isOpen('loose', false) ? 'open' : ''}><summary>📁 グループなし <span class="sub">${loose.length}</span></summary>${loose.slice(0, 400).map((s) => itemRow(s)).join('')}</details>`;
  html += `<details data-key="archived" ${isOpen('archived', false) ? 'open' : ''}><summary>🗃️ アーカイブ <span class="sub">${archived.length}</span></summary>${archived.slice(0, 400).map((s) => itemRow(s)).join('')}</details>`;
  return `<div class="tree">${html}</div>`;
}

function historyHtml(store) {
  const data = store.sessions;
  if (!data) {
    return '<div class="sub">読み込み中…</div>';
  }
  const q = store.search.trim().toLowerCase();
  const list = data.sessions.filter((s) => (!store.projFilter || s.project === store.projFilter) && match(s, q)).slice(0, 400);
  const today = dayKeyOf(Date.now());
  const yesterday = dayKeyOf(Date.now() - 86400000);
  let html = '';
  let last = '';
  for (const s of list) {
    const k = dayKeyOf(s.end || s.start);
    if (k !== last) {
      last = k;
      html += `<div class="h">${k === today ? '今日' : k === yesterday ? '昨日' : dateLabel(s.end || s.start)}</div>`;
    }
    html += itemRow(s);
  }
  return html || '<div class="sub">見つかりません</div>';
}

export function renderSide(store, actions) {
  const el = document.getElementById('side');
  if (!el.__built) {
    el.__built = true;
    el.innerHTML = `<div class="tabs"><button class="btn" data-tab="live">🏢 在席</button><button class="btn" data-tab="groups">🗂️ グループ</button><button class="btn" data-tab="history">📚 履歴</button></div>
      <div class="filters" style="padding:0 10px"></div><div class="side-body"></div>`;
    el.querySelector('.tabs').onclick = (e) => {
      const b = e.target.closest('[data-tab]');
      if (b) {
        actions.setTab(b.dataset.tab);
      }
    };
    const body = el.querySelector('.side-body');
    let clickTimer = null;
    body.addEventListener('click', (e) => {
      const it = e.target.closest('[data-id]');
      if (!it || e.target.closest('summary')) {
        return;
      }
      // シングルクリックは詳細、ダブルクリックは VSCode で開く。ダブルクリックの1回目で窓を開かないよう少し待つ
      clearTimeout(clickTimer);
      clickTimer = setTimeout(() => actions.openDetail(it.dataset.id), 230);
    });
    body.addEventListener('dblclick', (e) => {
      const it = e.target.closest('[data-id]');
      if (it) {
        clearTimeout(clickTimer);
        openInVscode(it.dataset.id);
      }
    });
    body.addEventListener('toggle', (e) => {
      const d = e.target;
      if (d.tagName === 'DETAILS' && d.dataset.key && !store.search) {
        const def = !(d.dataset.key === 'loose' || d.dataset.key === 'archived');
        if (d.open === def) {
          store.openGroups.delete(d.dataset.key);
        } else {
          store.openGroups.add(d.dataset.key);
        }
        actions.saveOpenGroups();
      }
    }, true);
  }
  el.querySelectorAll('.tabs .btn').forEach((b) => b.classList.toggle('on', b.dataset.tab === store.tab));
  const filters = el.querySelector('.filters');
  if (filters.__tab !== store.tab) {
    filters.__tab = store.tab;
    if (store.tab === 'live') {
      filters.innerHTML = '';
    } else {
      filters.innerHTML = `<input class="search" placeholder="🔍 検索" value="${esc(store.search)}">${store.tab === 'history' ? '<select class="search proj" style="margin-top:4px"></select>' : ''}`;
      filters.querySelector('input').oninput = (e) => {
        store.search = e.target.value;
        renderSide(store, actions);
      };
      const sel = filters.querySelector('.proj');
      if (sel) {
        sel.onchange = (e) => {
          store.projFilter = e.target.value;
          renderSide(store, actions);
        };
      }
    }
  }
  const sel = filters.querySelector('.proj');
  if (sel && store.sessions) {
    const projects = [...new Set(store.sessions.sessions.map((s) => s.project))].sort();
    setIfChanged(sel, `<option value="">📁 すべてのプロジェクト</option>${projects.map((p) => `<option ${p === store.projFilter ? 'selected' : ''}>${esc(p)}</option>`).join('')}`);
  }
  const body = el.querySelector('.side-body');
  const now = Date.now();
  let html;
  if (store.tab === 'groups') {
    html = groupsHtml(store);
  } else if (store.tab === 'history') {
    html = historyHtml(store);
  } else {
    const live = store.state ? [...store.state.live] : [];
    live.sort((a, b) => {
      const sa = STATE_ORDER[deriveState(a, now).key];
      const sb = STATE_ORDER[deriveState(b, now).key];
      return sa - sb || (b.end || 0) - (a.end || 0);
    });
    const label = store.date === dayKeyOf(now) ? '今日' : dateLabel(new Date(store.date).getTime());
    html = `<div class="h">🏢 いま在席中のメンバー</div>${live.map((l) => liveCard(l, now)).join('') || '<div class="sub">在席中の会話はありません</div>'}${summaryHtml(store.state && store.state.stats, label)}`;
  }
  setIfChanged(body, html);
}

// ---------------------------------------------------------------- タイムライン(下)

export function timelineRange(day, from) {
  let lo = Infinity;
  let hi = -Infinity;
  for (const s of day) {
    for (const g of s.segments || []) {
      if (g.e >= from && g.s < from + 86400000) {
        lo = Math.min(lo, Math.max(g.s, from));
        hi = Math.max(hi, Math.min(g.e, from + 86400000));
      }
    }
  }
  if (lo === Infinity) {
    return { lo: from + 8 * 3600000, hi: from + 20 * 3600000 };
  }
  // 記録のある時間±15分(最低6時間)
  lo -= 15 * 60000;
  hi += 15 * 60000;
  if (hi - lo < 6 * 3600000) {
    const mid = (lo + hi) / 2;
    lo = mid - 3 * 3600000;
    hi = mid + 3 * 3600000;
  }
  return { lo: Math.max(from, lo), hi: Math.min(from + 86400000, hi) };
}

export function renderTimeline(store, actions) {
  const el = document.getElementById('timeline');
  if (!el.__built) {
    el.__built = true;
    el.innerHTML = `<div class="row"><button class="btn prev">◀</button><b class="dl"></b><button class="btn next">▶</button><button class="btn today">今日</button><span class="spacer"></span>
      <label class="row">🕰 時間プレビュー <input type="checkbox" class="pv"></label><input type="range" class="pvr" min="0" max="24" step="0.25" style="width:140px" disabled><button class="btn fold">▾</button></div>
      <div class="tl-body"></div>`;
    el.querySelector('.prev').onclick = () => actions.shiftDate(-1);
    el.querySelector('.next').onclick = () => actions.shiftDate(1);
    el.querySelector('.today').onclick = () => actions.shiftDate(0);
    el.querySelector('.fold').onclick = () => el.classList.toggle('collapsed');
    const pv = el.querySelector('.pv');
    const pvr = el.querySelector('.pvr');
    pv.onchange = () => {
      pvr.disabled = !pv.checked;
      actions.setPreview(pv.checked ? Number(pvr.value) : null);
    };
    pvr.oninput = () => actions.setPreview(Number(pvr.value));
    let clickTimer = null;
    const body = el.querySelector('.tl-body');
    body.addEventListener('click', (e) => {
      const it = e.target.closest('[data-id]');
      if (it) {
        clearTimeout(clickTimer);
        clickTimer = setTimeout(() => actions.openDetail(it.dataset.id), 230);
      }
    });
    body.addEventListener('dblclick', (e) => {
      const it = e.target.closest('[data-id]');
      if (it) {
        clearTimeout(clickTimer);
        openInVscode(it.dataset.id);
      }
    });
  }
  const pv = el.querySelector('.pv');
  const pvr = el.querySelector('.pvr');
  if (store.previewHour !== null && !pv.checked) {
    pv.checked = true;
    pvr.disabled = false;
    pvr.value = store.previewHour;
  }
  const from = new Date(`${store.date}T00:00:00`).getTime();
  const isToday = store.date === dayKeyOf(Date.now());
  el.querySelector('.dl').textContent = `${dateLabel(from)}${isToday ? '・今日' : ''}`;
  const day = store.state ? store.state.day : [];
  const { lo, hi } = timelineRange(day, from);
  const pct = (t) => (((t - lo) / (hi - lo)) * 100).toFixed(3);
  let hours = '';
  for (let h = Math.ceil((lo - from) / 3600000); h <= Math.floor((hi - from) / 3600000); h++) {
    hours += `<span style="left:${pct(from + h * 3600000)}%">${h}時</span>`;
  }
  const byProject = new Map();
  for (const s of day) {
    if (!byProject.has(s.project)) {
      byProject.set(s.project, []);
    }
    byProject.get(s.project).push(s);
  }
  const liveIds = new Set((store.state ? store.state.live : []).map((l) => l.id));
  let rows = '';
  for (const [proj, list] of byProject) {
    const color = projectColor(proj);
    rows += `<div class="tl-proj row"><span class="dot" style="background:${color}"></span>${esc(proj)} <span class="sub">${list.length}件</span></div>`;
    for (const s of list) {
      const segs = (s.segments || []).filter((g) => g.e >= lo && g.s <= hi);
      const live = liveIds.has(s.id);
      const bars = segs.map((g, i) => {
        const a = Math.max(g.s, lo);
        const b = Math.min(g.e, hi);
        const strong = live && i === segs.length - 1;
        return `<div class="tl-bar${strong ? ' strong' : ''}" data-id="${s.id}" style="left:${pct(a)}%;width:${Math.max(0.3, pct(b) - pct(a))}%;background:${color}" title="${hhmm(g.s)}〜${hhmm(g.e)}"></div>`;
      }).join('');
      rows += `<div class="tl-row"><div class="tl-name" data-id="${s.id}">${live ? '🟢 ' : ''}${esc(s.title || s.firstPrompt || '(無題)')}</div><div class="tl-track">${bars}</div></div>`;
    }
  }
  const nowLine = isToday && Date.now() >= lo && Date.now() <= hi ? `<div class="tl-now" style="left:calc(190px + (100% - 190px) * ${pct(Date.now()) / 100})"></div>` : '';
  setIfChanged(el.querySelector('.tl-body'), `<div class="tl-grid"><div class="row"><div style="width:190px;flex:none"></div><div class="tl-hours" style="flex:1">${hours}</div></div>${rows || '<div class="sub">この日の記録はありません</div>'}${nowLine}</div>`);
}

// ---------------------------------------------------------------- 詳細の窓

const windows = []; // { id, el, chat }
let cascade = 0;
let zTop = 30;

function loadSize() {
  try {
    const j = JSON.parse(localStorage.getItem('claude-office.detail-size') || 'null');
    return j && j.w && j.h ? j : null;
  } catch {
    return null;
  }
}

function saveSize(w, h) {
  try {
    localStorage.setItem('claude-office.detail-size', JSON.stringify({ w, h }));
  } catch {
    // 保存できなくても動く
  }
}

function front(win) {
  win.el.style.zIndex = String(++zTop);
  const i = windows.indexOf(win);
  windows.splice(i, 1);
  windows.push(win);
}

export function closeTopWindow() {
  const w = windows.pop();
  if (w) {
    w.el.remove();
  }
}

export function openDetail(id) {
  const existing = windows.find((w) => w.id === id);
  if (existing) {
    // 同じものを再選択したら閉じる
    windows.splice(windows.indexOf(existing), 1);
    existing.el.remove();
    return;
  }
  const el = document.createElement('div');
  el.className = 'panel win';
  const size = loadSize();
  if (size) {
    el.style.width = size.w;
    el.style.height = size.h;
  }
  const off = (cascade++ % 6) * 28;
  el.style.left = `${360 + off}px`;
  el.style.top = `${70 + off}px`;
  el.innerHTML = '<div class="head"><span class="ttl ellipsis" style="flex:1">読み込み中…</span><button class="x" title="閉じる">✕</button></div><div class="content"><div class="top"></div><div class="chatslot"></div><div class="rest"></div></div>';
  document.getElementById('windows').appendChild(el);
  const win = { id, el, chat: null, data: null };
  windows.push(win);
  front(win);
  el.querySelector('.x').onclick = () => {
    windows.splice(windows.indexOf(win), 1);
    el.remove();
  };
  el.addEventListener('mousedown', () => front(win));
  // 見出しドラッグで移動
  const head = el.querySelector('.head');
  head.addEventListener('mousedown', (e) => {
    if (e.target.closest('button')) {
      return;
    }
    const sx = e.clientX - el.offsetLeft;
    const sy = e.clientY - el.offsetTop;
    const move = (ev) => {
      el.style.left = `${Math.max(0, ev.clientX - sx)}px`;
      el.style.top = `${Math.max(0, ev.clientY - sy)}px`;
    };
    const up = () => {
      window.removeEventListener('mousemove', move);
      window.removeEventListener('mouseup', up);
    };
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
  });
  // 大きさは style の値で保存する(offsetWidth だと枠の分だけ太っていく)
  new ResizeObserver(() => {
    if (el.style.width && el.style.height) {
      saveSize(el.style.width, el.style.height);
    }
  }).observe(el);
  el.querySelector('.rest').addEventListener('dblclick', () => openInVscode(id));
  refreshWindow(win);
}

export function refreshWindows() {
  for (const w of windows) {
    refreshWindow(w);
  }
}

async function refreshWindow(win) {
  let d;
  try {
    const r = await fetch(`/api/session/${win.id}`);
    if (!r.ok) {
      win.el.querySelector('.ttl').textContent = '見つかりません';
      return;
    }
    d = await r.json();
  } catch {
    return;
  }
  win.data = d;
  const now = Date.now();
  const st = deriveState(d, now);
  const color = projectColor(d.project);
  win.el.querySelector('.ttl').textContent = d.title || d.firstPrompt || '(無題)';
  const chips = [
    `<span class="chip"><span class="dot" style="background:${color}"></span>${esc(d.project)}</span>`,
    d.group ? `<span class="chip">🗂️ ${esc(d.group.name)}</span>` : '',
    d.hidden ? '<span class="chip">🗃️ アーカイブ済み</span>' : '',
    d.branch ? `<span class="chip">🌿 ${esc(d.branch)}</span>` : '',
    d.model ? `<span class="chip">🧠 ${esc(d.model)}</span>` : '',
    d.live ? stateChip(st) : '<span class="chip">🌙 終了</span>',
    d.live ? badgeHtml(d.progress) : '',
    d.live && d.live.name ? `<span class="chip">🏷️ ${esc(d.live.name)}</span>` : '',
  ].join('');
  const active = (d.segments || []).reduce((a, s) => a + (s.e - s.s), 0);
  const canOpen = !d.transcriptGone && !(d.live && d.live.entrypoint !== 'claude-vscode');
  const top = `<div class="chips">${chips}</div>
    ${d.transcriptGone ? '<div class="notice">📦 要約のみ（会話の記録は削除済みのため、再開・送信はできません）</div>' : ''}
    <div class="kv"><span class="sub">開始</span><span>${d.start ? `${dateLabel(d.start)} ${hhmm(d.start)}` : '-'}</span>
    <span class="sub">最終</span><span>${d.end ? `${dateLabel(d.end)} ${hhmm(d.end)}` : '-'}</span>
    <span class="sub">依頼</span><span>${d.prompts} 回</span><span class="sub">ツール</span><span>${d.toolCalls} 回</span>
    <span class="sub">稼働</span><span>${elapsed(active) || '-'}</span></div>
    <div style="margin:8px 0"><button class="btn vsc" ${canOpen ? '' : 'disabled'}>💻 VSCode の Claude Code で開く</button></div>`;
  const topEl = win.el.querySelector('.top');
  setIfChanged(topEl, top);
  const btn = topEl.querySelector('.vsc');
  if (btn) {
    btn.onclick = () => openInVscode(d.id);
  }
  if (!win.chat) {
    win.chat = createChat(d.id);
    win.el.querySelector('.chatslot').appendChild(win.chat.el);
  }
  win.chat.update(d);
  const tools = Object.entries(d.tools || {}).sort((a, b) => b[1] - a[1]).slice(0, 8);
  const max = tools.length ? tools[0][1] : 1;
  const acts = [...(d.events || [])].reverse().slice(0, 200).map((e) => {
    const em = e.k === 'prompt' ? '🙋' : e.k === 'reply' ? '💬' : toolEmoji(e.n);
    const text = e.k === 'tool' ? `${e.n}${e.l ? `: ${e.l}` : ''}` : e.l;
    return `<div class="act"><span class="tm">${e.t ? hhmm(e.t) : ''}</span><span>${em}</span><span class="ellipsis">${esc(text)}</span></div>`;
  }).join('');
  const rest = `<div class="h">🙋 最初と最後の依頼</div><div class="notice" style="background:var(--chip-bg)">${esc(d.firstPrompt || '-')}</div>${d.lastPrompt && d.lastPrompt !== d.firstPrompt ? `<div class="notice" style="background:var(--chip-bg)">${esc(d.lastPrompt)}</div>` : ''}
    <div class="h">🛠️ よく使ったツール</div><div class="bars">${tools.map(([n, v]) => `<div class="bar"><span>${toolEmoji(n)} ${esc(n)}</span><i style="width:${(160 * v) / max}px"></i><span class="sub">${v}</span></div>`).join('') || '<div class="sub">なし</div>'}</div>
    <div class="h">🐣 サブエージェント</div>${(d.subagents || []).map((s) => `<div class="act"><span class="tm">${hhmm(s.start)}</span><span>${s.active ? '🟢' : '⚪'}</span><span class="ellipsis">${esc(s.type)}: ${esc(s.desc)}</span></div>`).join('') || '<div class="sub">なし</div>'}
    <div class="h">⛺ アクティビティ</div>${acts || '<div class="sub">なし</div>'}`;
  setIfChanged(win.el.querySelector('.rest'), rest);
}
