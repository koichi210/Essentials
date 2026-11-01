// Claude Office サーバ。Claude Code の会話記録(%USERPROFILE%\.claude)を読み取り専用で解析し、
// 3D 箱庭ダッシュボード(public/)へ JSON で渡す。npm パッケージは使わない(標準モジュールと node:sqlite のみ)。
'use strict';

const http = require('http');
const fs = require('fs');
const path = require('path');
const os = require('os');
const net = require('net');
const https = require('https');
const crypto = require('crypto');
const { spawn, execFileSync } = require('child_process');

const PORT = Number(process.env.PORT) || 8765;
const HOST = '127.0.0.1';
const HISTORY_DAYS = Number(process.env.HISTORY_DAYS || 0);
const CLAUDE_DIR = process.env.CLAUDE_CONFIG_DIR || path.join(os.homedir(), '.claude');
const DATA_DIR = process.env.CLAUDE_OFFICE_DATA || path.join(process.env.LOCALAPPDATA || os.homedir(), 'ClaudeOffice');
const PUBLIC_DIR = path.join(__dirname, 'public');
const NOTIFY_FILE = path.join(__dirname, 'notify.json');
const ARCHIVE_FILE = path.join(DATA_DIR, 'archive.json');
const ALLOWED_ORIGINS = new Set([`http://127.0.0.1:${PORT}`, `http://localhost:${PORT}`]);
const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const MAX_EVENTS = 400;
const ARCHIVE_EVENTS = 200;
const SEGMENT_GAP_MS = 10 * 60 * 1000;

// ---------------------------------------------------------------- ログ

function log(message) {
  // 件名や本文は書かない。何が起きたかだけを残す
  const line = `${new Date().toISOString()} ${message}`;
  console.log(line);
}

// ---------------------------------------------------------------- 秘密のマスク

const SECRET_KV_RE = /\b(token|password|secret|api_key)(\s*[=/:]\s*)("?)([^\s"',;]+)/gi;
const BEARER_RE = /\bBearer\s+[^\s"',;]+/gi;
const LONG_WORD_RE = /[A-Za-z0-9_\-+/=.]{20,}/g;

function charClass(c) {
  if (c >= 'a' && c <= 'z') {
    return 1;
  }
  if (c >= 'A' && c <= 'Z') {
    return 2;
  }
  if (c >= '0' && c <= '9') {
    return 3;
  }
  return 4;
}

// 20字以上で「ランダムに見える」語か。単語や URL のパスを巻き込まないよう、大小英字＋数字の混在と文字種の切り替わり率で見る
function looksRandom(word) {
  if (/^[0-9a-f]{32,}$/i.test(word) && /[0-9]/.test(word)) {
    return true;
  }
  if (!(/[a-z]/.test(word) && /[A-Z]/.test(word) && /[0-9]/.test(word))) {
    return false;
  }
  let switches = 0;
  for (let i = 1; i < word.length; i++) {
    if (charClass(word[i]) !== charClass(word[i - 1])) {
      switches++;
    }
  }
  return switches / (word.length - 1) > 0.3;
}

function mask(text) {
  if (typeof text !== 'string' || text.length === 0) {
    return text;
  }
  return text
    .replace(SECRET_KV_RE, (m, key, sep, quote) => `${key}${sep}${quote}••••`)
    .replace(BEARER_RE, 'Bearer ••••')
    .replace(LONG_WORD_RE, (w) => (looksRandom(w) ? '••••' : w));
}

function clip(text, max) {
  if (typeof text !== 'string') {
    return '';
  }
  const t = text.replace(/\s+/g, ' ').trim();
  return t.length > max ? `${t.slice(0, max)}…` : t;
}

// ---------------------------------------------------------------- 日付

function dayKey(ms) {
  const d = new Date(ms);
  const p = (n) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

function dayRange(key) {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(key || '');
  const base = m ? new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3])) : new Date();
  const from = new Date(base.getFullYear(), base.getMonth(), base.getDate()).getTime();
  const to = new Date(base.getFullYear(), base.getMonth(), base.getDate() + 1).getTime();
  return { from, to };
}

// ---------------------------------------------------------------- 会話記録(transcript)の解析

const sessions = new Map(); // id -> rec
let lastScan = 0;
let archiveDirty = false;

function newRecord(id, file, ws) {
  return {
    id, file, ws, offset: 0, carry: '', size: 0,
    title: '', titleSource: '', cwd: '', rootCwd: '', branch: '', model: '',
    firstPrompt: '', lastPrompt: '', prompts: 0, toolCalls: 0, tools: {},
    events: [], segments: [], daily: {}, state: 'prompt', lastTool: null,
    bg: {}, wakeAt: 0, asking: false, lastReply: '', start: 0, end: 0,
    subagents: [], transcriptGone: false,
  };
}

function daily(rec, t) {
  const k = dayKey(t);
  if (!rec.daily[k]) {
    rec.daily[k] = { prompts: 0, toolCalls: 0, tools: {} };
  }
  return rec.daily[k];
}

// 前の区間から10分以内なら同じ区間に延長し、超えたら新しい区間にする(タイムラインの棒・稼働時間に使う)
function touch(rec, t) {
  if (!t || Number.isNaN(t)) {
    return;
  }
  if (!rec.start || t < rec.start) {
    rec.start = t;
  }
  if (t > rec.end) {
    rec.end = t;
  }
  const last = rec.segments[rec.segments.length - 1];
  if (last && t >= last.s && t - last.e <= SEGMENT_GAP_MS) {
    last.e = Math.max(last.e, t);
  } else if (!last || t > last.e) {
    rec.segments.push({ s: t, e: t });
  }
}

function pushEvent(rec, ev) {
  rec.events.push(ev);
  if (rec.events.length > MAX_EVENTS) {
    rec.events.splice(0, rec.events.length - MAX_EVENTS);
  }
}

// ツールの表示名。コマンド本文は秘密を含みうるので出さず、説明やファイル名だけにする
function toolLabel(name, input) {
  const i = input || {};
  switch (name) {
    case 'Bash':
    case 'PowerShell':
      return i.description || '';
    case 'Read':
    case 'Edit':
    case 'Write':
    case 'NotebookEdit':
      return path.basename(String(i.file_path || i.notebook_path || ''));
    case 'Grep':
      return i.pattern ? `"${clip(String(i.pattern), 40)}"` : '';
    case 'Glob':
      return i.pattern || '';
    case 'Agent':
    case 'Task':
      return i.description || '';
    case 'Skill':
      return i.skill || i.name || '';
    case 'WebSearch':
      return i.query || '';
    case 'WebFetch':
      try {
        return new URL(i.url).host;
      } catch {
        return '';
      }
    case 'TodoWrite':
      return 'TODO更新';
    case 'ScheduleWakeup':
      return i.reason || '';
    default:
      return '';
  }
}

function textOf(content) {
  if (typeof content === 'string') {
    return content;
  }
  if (Array.isArray(content)) {
    return content.filter((b) => b && b.type === 'text').map((b) => b.text || '').join('\n');
  }
  return '';
}

// 人の依頼の本文を取り出す。人の依頼でなければ null
function humanPrompt(o) {
  if (o.isMeta || o.isCompactSummary || o.toolUseResult) {
    return null;
  }
  if (o.origin && o.origin.kind && o.origin.kind !== 'human') {
    return null;
  }
  const content = o.message && o.message.content;
  if (Array.isArray(content) && content.some((b) => b && b.type === 'tool_result')) {
    return null;
  }
  let text = textOf(content);
  const cmd = /<command-name>([\s\S]*?)<\/command-name>/.exec(text);
  if (cmd) {
    const args = /<command-args>([\s\S]*?)<\/command-args>/.exec(text);
    text = `${cmd[1].trim()} ${args ? args[1].trim() : ''}`;
  }
  text = text.replace(/<system-reminder>[\s\S]*?<\/system-reminder>/g, '').replace(/<ide_[a-z_]*>[\s\S]*?<\/ide_[a-z_]*>/g, '').trim();
  if (!text || text.startsWith('<') || text.startsWith('[Request interrupted')) {
    return null;
  }
  return text;
}

function taskIdsIn(text) {
  const ids = [];
  const re = /<task-id>([\s\S]*?)<\/task-id>/g;
  let m;
  while ((m = re.exec(text)) !== null) {
    ids.push(m[1].trim());
  }
  return ids;
}

function finishTasks(rec, text) {
  const status = /<status>([\s\S]*?)<\/status>/.exec(text);
  if (status && status[1].trim() === 'running') {
    return;
  }
  for (const id of taskIdsIn(text)) {
    if (rec.bg[id]) {
      rec.bg[id].done = true;
    }
  }
}

function parseLine(rec, o) {
  const t = o.timestamp ? Date.parse(o.timestamp) : 0;
  switch (o.type) {
    case 'custom-title':
      rec.title = clip(o.customTitle, 80);
      rec.titleSource = 'custom';
      return;
    case 'ai-title':
      if (rec.titleSource !== 'custom') {
        rec.title = clip(o.aiTitle, 80);
        rec.titleSource = 'ai';
      }
      return;
    case 'summary':
      if (!rec.title) {
        rec.title = clip(o.summary, 80);
        rec.titleSource = 'summary';
      }
      return;
    case 'queue-operation':
      if (o.operation === 'enqueue' && typeof o.content === 'string') {
        finishTasks(rec, o.content);
      }
      return;
    case 'user':
      parseUser(rec, o, t);
      return;
    case 'assistant':
      parseAssistant(rec, o, t);
      return;
    default:
      return;
  }
}

function parseUser(rec, o, t) {
  if (o.isSidechain) {
    return;
  }
  touch(rec, t);
  if (o.cwd) {
    rec.cwd = o.cwd;
    if (!rec.rootCwd) {
      rec.rootCwd = o.cwd;
    }
  }
  if (o.gitBranch) {
    rec.branch = o.gitBranch;
  }
  const kind = o.origin && o.origin.kind;
  if (kind === 'task-notification' || kind === 'peer') {
    finishTasks(rec, textOf(o.message && o.message.content));
  }
  const prompt = humanPrompt(o);
  if (prompt !== null) {
    rec.prompts++;
    daily(rec, t || Date.now()).prompts++;
    const short = clip(prompt, 200);
    if (!rec.firstPrompt) {
      rec.firstPrompt = short;
    }
    rec.lastPrompt = short;
    rec.state = 'prompt';
    rec.asking = false;
    pushEvent(rec, { t, k: 'prompt', l: short });
    return;
  }
  if (Array.isArray(o.message && o.message.content)) {
    rec.state = 'thinking';
    rec.asking = false;
  }
  const r = o.toolUseResult;
  if (r && typeof r === 'object') {
    if (r.isAsync && r.agentId) {
      rec.bg[r.agentId] = { kind: 'agent', t, done: false };
    }
    if (r.backgroundTaskId) {
      rec.bg[r.backgroundTaskId] = { kind: 'cmd', t, done: false };
    }
  }
}

function parseAssistant(rec, o, t) {
  if (o.isSidechain) {
    return;
  }
  touch(rec, t);
  const msg = o.message || {};
  if (msg.model && msg.model !== '<synthetic>') {
    rec.model = msg.model;
  }
  if (!Array.isArray(msg.content)) {
    return;
  }
  for (const b of msg.content) {
    if (!b) {
      continue;
    }
    if (b.type === 'tool_use') {
      rec.toolCalls++;
      rec.tools[b.name] = (rec.tools[b.name] || 0) + 1;
      const d = daily(rec, t || Date.now());
      d.toolCalls++;
      d.tools[b.name] = (d.tools[b.name] || 0) + 1;
      const l = clip(toolLabel(b.name, b.input), 80);
      rec.lastTool = { n: b.name, l, t };
      rec.state = 'tool';
      pushEvent(rec, { t, k: 'tool', n: b.name, l });
      if (b.name === 'AskUserQuestion') {
        rec.asking = true;
      }
      if (b.name === 'ScheduleWakeup' && b.input && Number(b.input.delaySeconds) > 0) {
        rec.wakeAt = (t || Date.now()) + Number(b.input.delaySeconds) * 1000;
      }
    } else if (b.type === 'text' && b.text && b.text.trim()) {
      rec.state = 'reply';
      const full = b.text.trim();
      rec.lastReply = full.length > 600 ? full.slice(-600) : full;
      pushEvent(rec, { t, k: 'reply', l: clip(full, 200) });
    }
  }
}

// 前回読んだ位置から追記分だけ読む。末尾の改行なし断片は次回へ持ち越し、サイズが縮んだら最初から
function readIncrement(rec) {
  let st;
  try {
    st = fs.statSync(rec.file);
  } catch {
    if (!rec.transcriptGone) {
      rec.transcriptGone = true;
      archiveDirty = true;
    }
    return false;
  }
  rec.transcriptGone = false;
  if (st.size < rec.offset) {
    const keep = { title: rec.title, titleSource: rec.titleSource };
    Object.assign(rec, newRecord(rec.id, rec.file, rec.ws), keep);
  }
  if (st.size === rec.offset) {
    return false;
  }
  const fd = fs.openSync(rec.file, 'r');
  try {
    const len = st.size - rec.offset;
    const buf = Buffer.alloc(len);
    fs.readSync(fd, buf, 0, len, rec.offset);
    rec.offset = st.size;
    const text = rec.carry + buf.toString('utf8');
    const lines = text.split('\n');
    rec.carry = lines.pop() || '';
    for (const line of lines) {
      if (!line.trim()) {
        continue;
      }
      try {
        parseLine(rec, JSON.parse(line));
      } catch {
        // 壊れた行は飛ばす
      }
    }
  } finally {
    fs.closeSync(fd);
  }
  rec.size = st.size;
  archiveDirty = true;
  return true;
}

function readSubagents(rec) {
  const dir = path.join(path.dirname(rec.file), rec.id, 'subagents');
  let names;
  try {
    names = fs.readdirSync(dir).filter((n) => n.endsWith('.meta.json'));
  } catch {
    return;
  }
  const now = Date.now();
  const list = [];
  for (const n of names) {
    const agentId = n.slice(0, -'.meta.json'.length);
    try {
      const meta = JSON.parse(fs.readFileSync(path.join(dir, n), 'utf8'));
      const st = fs.statSync(path.join(dir, `${agentId}.jsonl`));
      list.push({
        id: agentId,
        type: meta.agentType || 'general-purpose',
        desc: clip(meta.description || '', 80),
        start: st.birthtimeMs,
        end: st.mtimeMs,
        active: now - st.mtimeMs < 90 * 1000,
      });
    } catch {
      // 書き込み途中などで読めないものは次回
    }
  }
  rec.subagents = list.sort((a, b) => a.start - b.start);
}

function scan(force) {
  const now = Date.now();
  if (!force && now - lastScan < 1500) {
    return;
  }
  lastScan = now;
  const root = path.join(CLAUDE_DIR, 'projects');
  let wss = [];
  try {
    wss = fs.readdirSync(root);
  } catch {
    return;
  }
  const minMtime = HISTORY_DAYS > 0 ? now - HISTORY_DAYS * 86400000 : 0;
  for (const ws of wss) {
    const dir = path.join(root, ws);
    let files;
    try {
      files = fs.readdirSync(dir).filter((f) => f.endsWith('.jsonl'));
    } catch {
      continue;
    }
    for (const f of files) {
      const id = f.slice(0, -6);
      if (!UUID_RE.test(id)) {
        continue;
      }
      const file = path.join(dir, f);
      let rec = sessions.get(id);
      if (!rec) {
        let st;
        try {
          st = fs.statSync(file);
        } catch {
          continue;
        }
        if (st.mtimeMs < minMtime) {
          continue;
        }
        rec = newRecord(id, file, ws);
        sessions.set(id, rec);
      } else if (rec.transcriptGone && rec.file !== file) {
        rec.file = file;
        rec.ws = ws;
      }
      if (readIncrement(rec)) {
        readSubagents(rec);
      } else if (rec.subagents.some((s) => s.active)) {
        readSubagents(rec);
      }
    }
  }
  for (const rec of sessions.values()) {
    if (!rec.transcriptGone && !fs.existsSync(rec.file)) {
      rec.transcriptGone = true;
      archiveDirty = true;
    }
  }
}

// ---------------------------------------------------------------- 在席情報

let liveCache = { at: 0, map: new Map() };

function isAlive(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch (e) {
    // 他ユーザーのプロセス等で権限が無いだけなら生きている
    return e.code === 'EPERM';
  }
}

function readLive() {
  const now = Date.now();
  if (now - liveCache.at < 1000) {
    return liveCache.map;
  }
  const map = new Map();
  const dir = path.join(CLAUDE_DIR, 'sessions');
  let names = [];
  try {
    names = fs.readdirSync(dir).filter((n) => /^\d+\.json$/.test(n));
  } catch {
    // 在席なし
  }
  for (const n of names) {
    try {
      const j = JSON.parse(fs.readFileSync(path.join(dir, n), 'utf8'));
      if (!j.sessionId || !isAlive(Number(j.pid))) {
        continue;
      }
      map.set(j.sessionId, {
        pid: j.pid,
        sessionId: j.sessionId,
        name: clip(j.name || '', 60),
        status: j.status || 'idle',
        statusSince: j.statusUpdatedAt || j.updatedAt || j.startedAt || 0,
        startedAt: j.startedAt || 0,
        cwd: j.cwd || '',
        entrypoint: j.entrypoint || '',
        kind: j.kind || '',
        peerProtocol: j.peerProtocol,
        messagingSocketPath: j.messagingSocketPath || '',
      });
    } catch {
      // 書き込み途中は次回
    }
  }
  liveCache = { at: now, map };
  return map;
}

function publicLive(l) {
  return {
    pid: l.pid, sessionId: l.sessionId, name: mask(l.name), status: l.status, statusSince: l.statusSince,
    startedAt: l.startedAt, cwd: l.cwd, entrypoint: l.entrypoint, kind: l.kind,
  };
}

// 受信口の鍵(peerToken)。画面へは決して返さない
function peerToken(pid) {
  const dir = path.join(CLAUDE_DIR, 'sessions');
  try {
    const f = fs.readdirSync(dir).find((n) => n.startsWith(`${pid}.`) && n.endsWith('.key'));
    if (!f) {
      return null;
    }
    const raw = fs.readFileSync(path.join(dir, f), 'utf8');
    try {
      return JSON.parse(raw).peerToken || null;
    } catch {
      return raw.trim() || null;
    }
  } catch {
    return null;
  }
}

function hasPipe(l) {
  return !!(l && Number(l.peerProtocol) === 1 && l.messagingSocketPath && peerToken(l.pid));
}

// ---------------------------------------------------------------- 依頼が全部終わったか(progress)

function lastLine(text) {
  const lines = String(text || '').split('\n').map((s) => s.trim()).filter(Boolean);
  return lines.length ? lines[lines.length - 1] : '';
}

function endsWithQuestion(text) {
  // 末尾の絵文字・記号を除いてから ?/？ で終わるかを見る
  const stripped = lastLine(text).replace(/[\s\p{Extended_Pictographic}\u{FE0F}\u{200D}!！。.、,…~〜♪）)」』】*_`]+$/u, '');
  return /[?？]$/.test(stripped);
}

function progressOf(rec, live, treatIdle) {
  const now = Date.now();
  const last = mask(lastLine(rec.lastReply));
  if (live && live.status === 'busy' && !treatIdle) {
    return { k: 'working', last };
  }
  const since = live ? live.startedAt || 0 : 0;
  let agents = 0;
  let cmds = 0;
  for (const b of Object.values(rec.bg)) {
    if (!b.done && b.t >= since) {
      if (b.kind === 'agent') {
        agents++;
      } else {
        cmds++;
      }
    }
  }
  agents = Math.max(agents, rec.subagents.filter((s) => s.active).length);
  const wake = rec.wakeAt > now ? rec.wakeAt : 0;
  if (live && (agents > 0 || cmds > 0 || wake)) {
    return { k: 'background', agents, cmds, wake, last };
  }
  if (rec.asking || endsWithQuestion(rec.lastReply)) {
    return { k: 'question', last };
  }
  return { k: 'done', last };
}

// ---------------------------------------------------------------- VSCode のグループ

let groupCache = { at: 0, value: { groups: {}, hidden: [] } };
let sqliteModule;
try {
  sqliteModule = require('node:sqlite');
} catch {
  sqliteModule = null;
}

function readGroups() {
  const now = Date.now();
  if (now - groupCache.at < 10000) {
    return groupCache.value;
  }
  groupCache.at = now;
  if (!sqliteModule) {
    return groupCache.value;
  }
  const merged = { groups: {}, hidden: [] };
  let any = false;
  for (const product of ['Code', 'Code - Insiders']) {
    const file = path.join(process.env.APPDATA || '', product, 'User', 'globalStorage', 'state.vscdb');
    if (!fs.existsSync(file)) {
      continue;
    }
    try {
      const db = new sqliteModule.DatabaseSync(file, { readOnly: true });
      try {
        const row = db.prepare('SELECT value FROM ItemTable WHERE key = ?').get('Anthropic.claude-code');
        if (row && row.value) {
          const j = JSON.parse(typeof row.value === 'string' ? row.value : Buffer.from(row.value).toString('utf8'));
          for (const [ws, list] of Object.entries(j.sessionGroups || {})) {
            merged.groups[ws] = (merged.groups[ws] || []).concat(Array.isArray(list) ? list : []);
          }
          merged.hidden = merged.hidden.concat(Array.isArray(j.hiddenSessionIds) ? j.hiddenSessionIds : []);
          any = true;
        }
      } finally {
        db.close();
      }
    } catch {
      // 書き込み中は読めないので前回値を使う
      return groupCache.value;
    }
  }
  if (any) {
    groupCache.value = merged;
  }
  return groupCache.value;
}

function groupOf(id) {
  const g = readGroups();
  for (const [ws, list] of Object.entries(g.groups)) {
    for (const item of list) {
      if (item && Array.isArray(item.sessionIds) && item.sessionIds.includes(id)) {
        return { ws, id: item.id, name: mask(item.name || '') };
      }
    }
  }
  return null;
}

// ---------------------------------------------------------------- 一覧用の形

function projectName(rec) {
  if (rec.rootCwd) {
    return path.basename(rec.rootCwd.replace(/[\\/]+$/, '')) || rec.rootCwd;
  }
  const parts = String(rec.ws || '').split('-').filter(Boolean);
  return parts.length ? parts[parts.length - 1] : '(不明)';
}

function light(rec, live) {
  const hidden = readGroups().hidden.includes(rec.id);
  return {
    id: rec.id,
    project: projectName(rec),
    cwd: rec.cwd,
    rootCwd: rec.rootCwd,
    branch: rec.branch,
    title: mask(rec.title || (live && live.name) || ''),
    firstPrompt: mask(rec.firstPrompt),
    lastPrompt: mask(rec.lastPrompt),
    start: rec.start,
    end: rec.end,
    prompts: rec.prompts,
    toolCalls: rec.toolCalls,
    segments: rec.segments,
    state: rec.state,
    lastTool: rec.lastTool ? { n: rec.lastTool.n, l: mask(rec.lastTool.l), t: rec.lastTool.t } : null,
    group: groupOf(rec.id),
    hidden,
    transcriptGone: rec.transcriptGone,
    isLive: !!live,
  };
}

function withoutSegments(o) {
  const { segments, ...rest } = o;
  return rest;
}

function maskedEvents(events, limit) {
  return events.slice(-limit).map((e) => ({ t: e.t, k: e.k, n: e.n, l: mask(e.l) }));
}

function recordFor(id) {
  let rec = sessions.get(id);
  if (!rec) {
    rec = newRecord(id, '', '');
    rec.transcriptGone = true;
  }
  return rec;
}

function stateResponse(dateKey, wantArchive) {
  scan(false);
  const live = readLive();
  const range = dayRange(dateKey);
  const liveList = [];
  for (const [id, l] of live) {
    const rec = sessions.get(id) || recordFor(id);
    liveList.push({
      ...light(rec, l),
      live: publicLive(l),
      recent: maskedEvents(rec.events, 5),
      subagents: rec.subagents.filter((s) => s.active).map((s) => ({ ...s, desc: mask(s.desc) })),
      progress: progressOf(rec, l, false),
    });
  }
  const day = [];
  const stats = { sessions: 0, prompts: 0, toolCalls: 0, tools: {}, activeMs: 0 };
  const key = dayKey(range.from);
  for (const rec of sessions.values()) {
    const overlap = rec.segments.filter((s) => s.e >= range.from && s.s < range.to);
    const isLive = live.has(rec.id);
    if (overlap.length === 0 && !(isLive && dayKey(Date.now()) === key)) {
      continue;
    }
    day.push(light(rec, live.get(rec.id)));
    stats.sessions++;
    for (const s of overlap) {
      stats.activeMs += Math.min(s.e, range.to) - Math.max(s.s, range.from);
    }
    const d = rec.daily[key];
    if (d) {
      stats.prompts += d.prompts;
      stats.toolCalls += d.toolCalls;
      for (const [n, c] of Object.entries(d.tools)) {
        stats.tools[n] = (stats.tools[n] || 0) + c;
      }
    }
  }
  day.sort((a, b) => a.start - b.start);
  const out = { now: Date.now(), date: range, live: liveList, day, stats };
  if (wantArchive) {
    out.archive = [...sessions.values()]
      .filter((r) => r.start)
      .sort((a, b) => b.end - a.end)
      .slice(0, 160)
      .map((r) => ({ id: r.id, project: projectName(r), title: mask(r.title || r.firstPrompt), start: r.start, end: r.end, toolCalls: r.toolCalls }));
  }
  return out;
}

function sessionsResponse() {
  scan(false);
  const live = readLive();
  const list = [...sessions.values()].filter((r) => r.start).sort((a, b) => b.end - a.end).map((r) => withoutSegments(light(r, live.get(r.id))));
  const g = readGroups();
  const groups = {};
  for (const [ws, items] of Object.entries(g.groups)) {
    groups[ws] = items.map((i) => ({ id: i.id, name: mask(i.name || ''), sessionIds: i.sessionIds || [] }));
  }
  return { sessions: list, groups, hidden: g.hidden };
}

function sessionResponse(id) {
  scan(false);
  const rec = sessions.get(id);
  if (!rec) {
    return null;
  }
  const l = readLive().get(id);
  return {
    ...light(rec, l),
    model: rec.model,
    tools: rec.tools,
    events: maskedEvents(rec.events, MAX_EVENTS),
    subagents: rec.subagents.map((s) => ({ ...s, desc: mask(s.desc) })),
    live: l ? publicLive(l) : null,
    chatRunning: chats.has(id),
    progress: progressOf(rec, l, false),
  };
}

// ---------------------------------------------------------------- 要約の保存(archive.json)

function loadArchive() {
  try {
    const j = JSON.parse(fs.readFileSync(ARCHIVE_FILE, 'utf8'));
    for (const s of j.sessions || []) {
      if (!s || !UUID_RE.test(s.id) || sessions.has(s.id)) {
        continue;
      }
      const rec = Object.assign(newRecord(s.id, s.file || '', s.ws || ''), s, { offset: 0, carry: '' });
      // 記録が残っていれば読み直すので、要約は記録が消えたものだけに使う
      if (rec.file && fs.existsSync(rec.file)) {
        continue;
      }
      rec.transcriptGone = true;
      sessions.set(rec.id, rec);
    }
    log(`archive: ${sessions.size} 件を読み込み`);
  } catch {
    // 初回は無い
  }
}

function saveArchive(force) {
  if (!force && !archiveDirty) {
    return;
  }
  archiveDirty = false;
  const list = [];
  for (const r of sessions.values()) {
    if (!r.start) {
      continue;
    }
    const { offset, carry, size, ...rest } = r;
    rest.events = r.events.slice(-ARCHIVE_EVENTS);
    list.push(rest);
  }
  try {
    fs.mkdirSync(DATA_DIR, { recursive: true });
    const tmp = `${ARCHIVE_FILE}.tmp`;
    fs.writeFileSync(tmp, JSON.stringify({ version: 1, savedAt: Date.now(), sessions: list }));
    fs.renameSync(tmp, ARCHIVE_FILE);
  } catch (e) {
    log(`archive の保存に失敗: ${e.code || e.message}`);
  }
}

// ---------------------------------------------------------------- アカウント・使用率

let account = { email: '', org: '', usage: null, usageAt: 0, error: '' };
let usageFetching = false;

function readJson(file) {
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch {
    return null;
  }
}

function accountResponse() {
  const cfg = readJson(process.env.CLAUDE_CONFIG_DIR ? path.join(CLAUDE_DIR, '.claude.json') : path.join(os.homedir(), '.claude.json')) || {};
  const oa = cfg.oauthAccount || {};
  account.email = oa.emailAddress || '';
  account.org = oa.organizationName || '';
  if (Date.now() - account.usageAt > 2 * 60 * 1000 && !usageFetching) {
    fetchUsage();
  }
  return account;
}

function fetchUsage() {
  const cred = readJson(path.join(CLAUDE_DIR, '.credentials.json'));
  const token = cred && cred.claudeAiOauth && cred.claudeAiOauth.accessToken;
  if (!token) {
    account.error = 'ログイン情報がありません';
    account.usageAt = Date.now();
    return;
  }
  usageFetching = true;
  const req = https.request({
    host: 'api.anthropic.com',
    path: '/api/oauth/usage',
    method: 'GET',
    timeout: 10000,
    headers: { Authorization: `Bearer ${token}`, 'anthropic-beta': 'oauth-2025-04-20', 'User-Agent': 'claude-office' },
  }, (res) => {
    let body = '';
    res.on('data', (c) => {
      body += c;
    });
    res.on('end', () => {
      usageFetching = false;
      account.usageAt = Date.now();
      if (res.statusCode !== 200) {
        // 失敗時は前回値を残す
        account.error = `使用率を取得できません (${res.statusCode})`;
        return;
      }
      try {
        const j = JSON.parse(body);
        const pick = (x) => (x ? { percent: Math.round(Number(x.utilization) || 0), resetsAt: x.resets_at ? Date.parse(x.resets_at) : 0 } : null);
        account.usage = { fiveHour: pick(j.five_hour), sevenDay: pick(j.seven_day) };
        account.error = '';
      } catch {
        account.error = '使用率の形式が読めません';
      }
    });
  });
  req.on('timeout', () => req.destroy(new Error('timeout')));
  req.on('error', () => {
    usageFetching = false;
    account.usageAt = Date.now();
    account.error = '使用率を取得できません';
  });
  req.end();
}

// ---------------------------------------------------------------- 子プロセス用の環境変数

// VSCode や Claude Code の中から起動されたときの環境変数を外さないと、窓や会話の入れ子に吸い込まれる
function cleanEnv(prefixes, names) {
  const env = { ...process.env };
  for (const k of Object.keys(env)) {
    if (prefixes.some((p) => k.toUpperCase().startsWith(p)) || names.includes(k.toUpperCase())) {
      delete env[k];
    }
  }
  return env;
}

// ---------------------------------------------------------------- VSCode で開く

function stripJsonc(text) {
  return text.replace(/("(?:\\.|[^"\\])*")|\/\/[^\n]*|\/\*[\s\S]*?\*\//g, (m, str) => str || '').replace(/,(\s*[}\]])/g, '$1');
}

function uriToPath(uri) {
  if (!uri) {
    return '';
  }
  try {
    return decodeURIComponent(String(uri).replace(/^file:\/\/\//, '')).replace(/\//g, '\\');
  } catch {
    return '';
  }
}

function norm(p) {
  try {
    return fs.realpathSync.native(p).toLowerCase();
  } catch {
    return path.resolve(p).toLowerCase();
  }
}

function workspaceFolders(file) {
  try {
    const j = JSON.parse(stripJsonc(fs.readFileSync(file, 'utf8')));
    return (j.folders || []).map((f) => norm(path.resolve(path.dirname(file), f.path || uriToPath(f.uri) || '')));
  } catch {
    return [];
  }
}

// 開く対象: 開いている .code-workspace のうちフォルダを含むもの(先頭フォルダ一致を優先) → 開いているフォルダ窓 → backupWorkspaces → フォルダそのもの
function vscodeTarget(rootCwd) {
  const want = norm(rootCwd);
  const storage = readJson(path.join(process.env.APPDATA || '', 'Code', 'User', 'globalStorage', 'storage.json')) || {};
  const ws = storage.windowsState || {};
  const windows = [ws.lastActiveWindow, ...(ws.openedWindows || [])].filter(Boolean);
  const workspaces = windows.map((w) => w.workspace && uriToPath(w.workspace.configURIPath || w.workspace.configPath)).filter(Boolean);
  let best = null;
  for (const file of workspaces) {
    const folders = workspaceFolders(file);
    if (folders[0] === want) {
      return file;
    }
    if (!best && folders.includes(want)) {
      best = file;
    }
  }
  if (best) {
    return best;
  }
  for (const w of windows) {
    if (w.folder && norm(uriToPath(w.folder)) === want) {
      return uriToPath(w.folder);
    }
  }
  const backup = storage.backupWorkspaces || {};
  for (const w of backup.workspaces || []) {
    const file = uriToPath(w.configURIPath);
    if (file && workspaceFolders(file).includes(want)) {
      return file;
    }
  }
  return rootCwd;
}

function findCodeCli() {
  const candidates = [
    path.join(process.env.LOCALAPPDATA || '', 'Programs', 'Microsoft VS Code', 'bin', 'code.cmd'),
    path.join(process.env.ProgramFiles || 'C:\\Program Files', 'Microsoft VS Code', 'bin', 'code.cmd'),
  ];
  try {
    const where = execFileSync('where.exe', ['code.cmd'], { encoding: 'utf8', windowsHide: true }).split(/\r?\n/).filter(Boolean);
    candidates.push(...where);
  } catch {
    // PATH に無い
  }
  for (const cmd of candidates) {
    try {
      const text = fs.readFileSync(cmd, 'utf8');
      const m = /\\([^\\"]+)\\resources\\app\\out\\cli\.js/.exec(text);
      const root = path.resolve(path.dirname(cmd), '..');
      const exe = path.join(root, 'Code.exe');
      const cli = m ? path.join(root, m[1], 'resources', 'app', 'out', 'cli.js') : path.join(root, 'resources', 'app', 'out', 'cli.js');
      if (fs.existsSync(exe) && fs.existsSync(cli)) {
        return { exe, cli };
      }
    } catch {
      // 次の候補
    }
  }
  return null;
}

function runCode(code, args) {
  const env = cleanEnv(['VSCODE_', 'ELECTRON_'], []);
  env.ELECTRON_RUN_AS_NODE = '1';
  const child = spawn(code.exe, [code.cli, ...args], { env, detached: true, stdio: 'ignore', windowsHide: true, shell: false });
  child.unref();
}

function openInVscode(id) {
  scan(false);
  const rec = sessions.get(id);
  if (!rec || rec.transcriptGone) {
    return '会話の記録がありません';
  }
  if (!rec.rootCwd || !fs.existsSync(rec.rootCwd)) {
    return 'フォルダがありません';
  }
  const l = readLive().get(id);
  if (l && l.entrypoint !== 'claude-vscode') {
    return 'ターミナルで開いている会話です';
  }
  const code = findCodeCli();
  if (!code) {
    return 'VSCode が見つかりません';
  }
  runCode(code, [vscodeTarget(rec.rootCwd)]);
  setTimeout(() => runCode(code, ['--open-url', `vscode://anthropic.claude-code/open?session=${id}`]), 1500);
  log('VSCode で開く');
  return null;
}

// ---------------------------------------------------------------- 会話の続きを送る

const chats = new Map(); // id -> { stop() }

function findClaudeExe() {
  const ext = path.join(os.homedir(), '.vscode', 'extensions');
  try {
    const dirs = fs.readdirSync(ext).filter((d) => d.startsWith('anthropic.claude-code-'));
    const ver = (d) => (/claude-code-(\d+)\.(\d+)\.(\d+)/.exec(d) || [0, 0, 0, 0]).slice(1).map(Number);
    dirs.sort((a, b) => {
      const x = ver(a);
      const y = ver(b);
      return y[0] - x[0] || y[1] - x[1] || y[2] - x[2];
    });
    for (const d of dirs) {
      const exe = path.join(ext, d, 'resources', 'native-binary', 'claude.exe');
      if (fs.existsSync(exe)) {
        return exe;
      }
    }
  } catch {
    // 拡張が無い
  }
  return 'claude';
}

function chatPrecheck(id, text) {
  scan(true);
  const rec = sessions.get(id);
  if (!rec) {
    return 'セッションがありません';
  }
  if (rec.transcriptGone) {
    return '要約しか残っていない会話です';
  }
  if (typeof text !== 'string' || !text.trim()) {
    return '本文が空です';
  }
  if (text.length > 20000) {
    return '本文が長すぎます(20,000字まで)';
  }
  if (!rec.rootCwd || !fs.existsSync(rec.rootCwd)) {
    return 'フォルダがありません';
  }
  if (chats.has(id)) {
    return 'この会話には送信中です';
  }
  const l = readLive().get(id);
  if (l && !hasPipe(l)) {
    return '開いている会話ですが受信口がありません';
  }
  return null;
}

function writeEvent(res, ev) {
  if (!res.writableEnded && !res.destroyed) {
    res.write(`${JSON.stringify(ev)}\n`);
  }
}

// 閉じた会話: claude -p --resume で続きを送り、stream-json をブロックごとに全文マスクして流す
function chatResume(id, text, res) {
  const rec = sessions.get(id);
  const started = Date.now();
  const env = cleanEnv(['CLAUDE_CODE_', 'CLAUDE_AGENT_SDK_'], ['CLAUDECODE', 'CLAUDE_PID', 'CLAUDE_EFFORT']);
  const args = ['-p', '--resume', id, '--output-format', 'stream-json', '--verbose', '--include-partial-messages', '--permission-mode', 'bypassPermissions'];
  const child = spawn(findClaudeExe(), args, { cwd: rec.rootCwd, env, windowsHide: true, shell: false });
  let done = false;
  const blocks = new Map();
  let buf = '';
  const finish = (ok, extra) => {
    if (done) {
      return;
    }
    done = true;
    clearTimeout(timer);
    chats.delete(id);
    writeEvent(res, { t: 'done', ok, ms: Date.now() - started, ...extra });
    res.end();
    log(`続きを送る(resume) 終了 ok=${ok}`);
  };
  const kill = () => {
    try {
      execFileSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { windowsHide: true, stdio: 'ignore' });
    } catch {
      // 既に終わっている
    }
  };
  const timer = setTimeout(() => {
    kill();
    finish(false, { error: '30分で打ち切りました' });
  }, 30 * 60 * 1000);
  chats.set(id, {
    stop: () => {
      kill();
      finish(false, { stopped: true });
    },
  });
  child.stdout.on('data', (c) => {
    buf += c.toString('utf8');
    const lines = buf.split('\n');
    buf = lines.pop() || '';
    for (const line of lines) {
      let o;
      try {
        o = JSON.parse(line);
      } catch {
        continue;
      }
      if (o.type === 'system' && o.subtype === 'init') {
        writeEvent(res, { t: 'start', model: o.model || '' });
      } else if (o.type === 'stream_event' && o.event) {
        const ev = o.event;
        if (ev.type === 'content_block_start' && ev.content_block && ev.content_block.type === 'tool_use') {
          writeEvent(res, { t: 'tool', n: ev.content_block.name, l: '' });
        } else if (ev.type === 'content_block_delta' && ev.delta && ev.delta.type === 'text_delta') {
          // 断片だと秘密をマスクしきれないので、ブロックごとに連結した全文をマスクして送る
          const key = `${o.message_id || o.uuid || 'm'}:${ev.index}`;
          const full = (blocks.get(key) || '') + ev.delta.text;
          blocks.set(key, full);
          writeEvent(res, { t: 'text', k: key, d: mask(full) });
        }
      } else if (o.type === 'assistant' && o.message && Array.isArray(o.message.content)) {
        for (const b of o.message.content) {
          if (b.type === 'tool_use') {
            writeEvent(res, { t: 'tool', n: b.name, l: mask(clip(toolLabel(b.name, b.input), 80)) });
          }
        }
      } else if (o.type === 'result') {
        finish(!o.is_error, o.is_error ? { error: clip(mask(String(o.result || 'エラー')), 200) } : {});
      }
    }
  });
  child.on('error', () => finish(false, { error: 'claude を起動できません' }));
  child.on('exit', (code) => finish(code === 0, code === 0 ? {} : { error: `終了コード ${code}` }));
  child.stdin.end(text, 'utf8');
}

// 開いている会話: 名前付きパイプ(受信口)へ auth と user を書き、transcript の増分から返事を流す
function chatPipe(id, text, res) {
  const rec = sessions.get(id);
  const l = readLive().get(id);
  const token = peerToken(l.pid);
  const msgId = crypto.randomUUID();
  const started = Date.now();
  let from = 0;
  try {
    from = fs.statSync(rec.file).size;
  } catch {
    from = rec.offset;
  }
  let done = false;
  let seen = false;
  let replied = false;
  let idleSince = 0;
  let carry = '';
  let timer = null;
  const finish = (ok, extra) => {
    if (done) {
      return;
    }
    done = true;
    clearInterval(timer);
    chats.delete(id);
    writeEvent(res, { t: 'done', ok, ms: Date.now() - started, ...extra });
    res.end();
    log(`続きを送る(受信口) 終了 ok=${ok}`);
  };
  chats.set(id, { stop: () => finish(false, { stopped: true }) });
  writeEvent(res, { t: 'start', peer: true });
  const sock = net.connect(l.messagingSocketPath, () => {
    sock.write(`${JSON.stringify({ type: 'auth', token })}\n`);
    // "now" は相手の作業を中断してしまうので next で順番待ちに入れる
    sock.write(`${JSON.stringify({ type: 'user', message: { role: 'user', content: text }, priority: 'next', from: 'dashboard', msg_id: msgId })}\n`);
    sock.end();
  });
  sock.on('error', () => finish(false, { error: '受信口に送れませんでした' }));
  timer = setInterval(() => {
    let st;
    try {
      st = fs.statSync(rec.file);
    } catch {
      return;
    }
    if (st.size > from) {
      const fd = fs.openSync(rec.file, 'r');
      const buf = Buffer.alloc(st.size - from);
      fs.readSync(fd, buf, 0, buf.length, from);
      fs.closeSync(fd);
      from = st.size;
      const lines = (carry + buf.toString('utf8')).split('\n');
      carry = lines.pop() || '';
      for (const line of lines) {
        let o;
        try {
          o = JSON.parse(line);
        } catch {
          continue;
        }
        if (!seen && o.type === 'user' && o.origin && o.origin.msg_id === msgId) {
          seen = true;
          writeEvent(res, { t: 'seen' });
          continue;
        }
        if (!seen) {
          continue;
        }
        if (o.type === 'assistant' && o.message && Array.isArray(o.message.content)) {
          o.message.content.forEach((b, i) => {
            if (b.type === 'text' && b.text) {
              replied = true;
              writeEvent(res, { t: 'text', k: `${o.uuid || o.message.id}:${i}`, d: mask(b.text) });
            } else if (b.type === 'tool_use') {
              writeEvent(res, { t: 'tool', n: b.name, l: mask(clip(toolLabel(b.name, b.input), 80)) });
            }
          });
        } else if (o.type === 'system' && (o.subtype === 'stop_hook_summary' || o.subtype === 'turn_duration')) {
          finish(true);
          return;
        }
      }
    }
    const live = readLive().get(id);
    const idle = !live || live.status === 'idle';
    if (idle) {
      idleSince = idleSince || Date.now();
    } else {
      idleSince = 0;
    }
    if (replied && idleSince && Date.now() - idleSince > 3000) {
      finish(true);
    } else if (!seen && idleSince && Date.now() - idleSince > 90000) {
      finish(true, { error: '受け取られていません(保留かも)' });
    } else if (Date.now() - started > 30 * 60 * 1000) {
      finish(false, { error: '30分で打ち切りました' });
    }
  }, 500);
}

// ---------------------------------------------------------------- 通知(notify.json と WinRT トースト)

const NOTIFY_DEFAULTS = {
  enabled: true, done: true, question: true,
  doneText: '✅ {name}：完了したよ', questionText: '❓ {name}：質問があるよ',
  duration: 'short', sound: true, quiet: false, quietFrom: '22:00', quietTo: '08:00', sameMinutes: 10,
};

function normalizeNotify(s) {
  const o = { ...NOTIFY_DEFAULTS };
  const src = s && typeof s === 'object' ? s : {};
  for (const k of ['enabled', 'done', 'question', 'sound', 'quiet']) {
    if (typeof src[k] === 'boolean') {
      o[k] = src[k];
    }
  }
  for (const k of ['doneText', 'questionText']) {
    if (typeof src[k] === 'string') {
      o[k] = src[k].slice(0, 120);
    }
  }
  if (['short', 'long', 'sticky'].includes(src.duration)) {
    o.duration = src.duration;
  }
  for (const k of ['quietFrom', 'quietTo']) {
    if (typeof src[k] === 'string' && /^([01]\d|2[0-3]):[0-5]\d$/.test(src[k])) {
      o[k] = src[k];
    }
  }
  const n = Number(src.sameMinutes);
  if (Number.isFinite(n)) {
    o.sameMinutes = Math.max(0, Math.min(240, Math.round(n)));
  }
  return o;
}

function loadNotify() {
  return normalizeNotify(readJson(NOTIFY_FILE));
}

function saveNotify(settings) {
  const o = normalizeNotify(settings);
  // 手で開いても読みやすいよう CRLF で保存する
  fs.writeFileSync(NOTIFY_FILE, JSON.stringify(o, null, 2).replace(/\n/g, '\r\n'), 'utf8');
  return o;
}

function minutesOf(hhmm) {
  const [h, m] = hhmm.split(':').map(Number);
  return h * 60 + m;
}

// 日またぎ(22:00〜08:00)にも対応する
function inQuiet(s, now) {
  if (!s.quiet) {
    return false;
  }
  const cur = now.getHours() * 60 + now.getMinutes();
  const from = minutesOf(s.quietFrom);
  const to = minutesOf(s.quietTo);
  return from <= to ? cur >= from && cur < to : cur >= from || cur < to;
}

function xmlEscape(s) {
  return String(s).replace(/[<>&"']/g, (c) => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;', "'": '&apos;' }[c]));
}

function showToast(s, kind, rec, live) {
  const name = (live && live.name) || rec.title || projectName(rec);
  const title = rec.title || '';
  const text = (kind === 'question' ? s.questionText : s.doneText).replace(/\{name\}/g, name).replace(/\{title\}/g, title);
  const url = `http://127.0.0.1:${PORT}/?session=${rec.id}`;
  const attrs = [`activationType="protocol"`, `launch="${xmlEscape(url)}"`];
  if (s.duration === 'long') {
    attrs.push('duration="long"');
  }
  if (s.duration === 'sticky') {
    attrs.push('scenario="reminder"');
  }
  const lines = [text, title, lastLine(rec.lastReply)].filter(Boolean).map((l) => `<text>${xmlEscape(clip(mask(l), 200))}</text>`).join('');
  let xml = `<toast ${attrs.join(' ')}><visual><binding template="ToastGeneric">${lines}</binding></visual>`;
  if (!s.sound) {
    xml += '<audio silent="true"/>';
  }
  if (s.duration === 'sticky') {
    xml += `<actions><action content="箱庭で開く" activationType="protocol" arguments="${xmlEscape(url)}"/><action content="閉じる" activationType="system" arguments="dismiss"/></actions>`;
  }
  xml += '</toast>';
  const appId = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\\WindowsPowerShell\\v1.0\\powershell.exe';
  const ps = [
    '[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null',
    '[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] > $null',
    '$x = New-Object Windows.Data.Xml.Dom.XmlDocument',
    `$x.LoadXml('${xml.replace(/'/g, "''")}')`,
    '$t = [Windows.UI.Notifications.ToastNotification]::new($x)',
    `$t.Tag = '${rec.id}'`,
    "$t.Group = 'claude-office'",
    `[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('${appId}').Show($t)`,
  ].join('\n');
  const encoded = Buffer.from(ps, 'utf16le').toString('base64');
  const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-WindowStyle', 'Hidden', '-EncodedCommand', encoded], {
    windowsHide: true, stdio: 'ignore', env: cleanEnv(['VSCODE_', 'CLAUDE_CODE_'], ['CLAUDECODE']),
  });
  child.on('error', () => log('トーストを出せませんでした'));
  log(`通知: ${kind}`);
}

const lastNotified = new Map(); // id -> { kind, reply, at }

// Stop フック直後は返事が書き切れていないので1.5秒待ち、在席が busy のままでも idle とみなして判定する
function onHookStop(id) {
  setTimeout(() => {
    scan(true);
    const rec = sessions.get(id);
    if (!rec) {
      return;
    }
    const s = loadNotify();
    if (!s.enabled || inQuiet(s, new Date())) {
      return;
    }
    const live = readLive().get(id);
    const p = progressOf(rec, live, true);
    if (p.k !== 'done' && p.k !== 'question') {
      return;
    }
    if ((p.k === 'done' && !s.done) || (p.k === 'question' && !s.question)) {
      return;
    }
    const prev = lastNotified.get(id);
    if (prev && prev.kind === p.k && prev.reply === rec.lastReply && Date.now() - prev.at < s.sameMinutes * 60000) {
      return;
    }
    lastNotified.set(id, { kind: p.k, reply: rec.lastReply, at: Date.now() });
    showToast(s, p.k, rec, live);
  }, 1500);
}

// ---------------------------------------------------------------- HTTP

const MIME = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8', '.png': 'image/png', '.svg': 'image/svg+xml', '.ico': 'image/x-icon',
};

function sendJson(res, code, obj) {
  const body = JSON.stringify(obj);
  res.writeHead(code, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-cache' });
  res.end(body);
}

function serveStatic(req, res, pathname) {
  const rel = pathname === '/' ? 'index.html' : decodeURIComponent(pathname).replace(/^\/+/, '');
  const file = path.resolve(PUBLIC_DIR, rel);
  if (file !== PUBLIC_DIR && !file.startsWith(PUBLIC_DIR + path.sep)) {
    res.writeHead(403);
    res.end();
    return;
  }
  fs.readFile(file, (err, data) => {
    if (err) {
      res.writeHead(404);
      res.end();
      return;
    }
    res.writeHead(200, { 'Content-Type': MIME[path.extname(file).toLowerCase()] || 'application/octet-stream', 'Cache-Control': 'no-cache' });
    res.end(data);
  });
}

// POST は Content-Type が JSON で、Origin が無いか自分自身のときだけ受ける(他サイトからの CSRF 対策)
function postAllowed(req) {
  const type = String(req.headers['content-type'] || '').split(';')[0].trim().toLowerCase();
  const origin = req.headers.origin;
  return type === 'application/json' && (!origin || ALLOWED_ORIGINS.has(origin));
}

function readBody(req, limit) {
  return new Promise((resolve, reject) => {
    let size = 0;
    const chunks = [];
    req.on('data', (c) => {
      size += c.length;
      if (size > limit) {
        reject(Object.assign(new Error('too large'), { status: 413 }));
        req.destroy();
        return;
      }
      chunks.push(c);
    });
    req.on('end', () => {
      try {
        const text = Buffer.concat(chunks).toString('utf8');
        resolve(text ? JSON.parse(text) : {});
      } catch {
        reject(Object.assign(new Error('bad json'), { status: 400 }));
      }
    });
    req.on('error', reject);
  });
}

async function handle(req, res) {
  const url = new URL(req.url, `http://${HOST}:${PORT}`);
  const p = url.pathname;
  if (req.method === 'GET') {
    if (p === '/api/state') {
      sendJson(res, 200, stateResponse(url.searchParams.get('date'), url.searchParams.get('archive') === '1'));
      return;
    }
    if (p === '/api/sessions') {
      sendJson(res, 200, sessionsResponse());
      return;
    }
    if (p.startsWith('/api/session/')) {
      const id = p.slice('/api/session/'.length);
      const s = UUID_RE.test(id) ? sessionResponse(id) : null;
      sendJson(res, s ? 200 : 404, s || { error: 'not found' });
      return;
    }
    if (p === '/api/account') {
      sendJson(res, 200, accountResponse());
      return;
    }
    if (p === '/api/notify-settings') {
      sendJson(res, 200, { settings: loadNotify(), defaults: NOTIFY_DEFAULTS, file: NOTIFY_FILE });
      return;
    }
    if (p.startsWith('/api/')) {
      sendJson(res, 404, { error: 'not found' });
      return;
    }
    serveStatic(req, res, p);
    return;
  }
  if (req.method !== 'POST') {
    res.writeHead(405);
    res.end();
    return;
  }
  if (!postAllowed(req)) {
    sendJson(res, 403, { error: 'forbidden' });
    return;
  }
  let m;
  try {
    if (p === '/api/hook/stop') {
      const body = await readBody(req, 4 * 1024 * 1024);
      sendJson(res, 200, { ok: true });
      if (UUID_RE.test(body.session_id || '')) {
        onHookStop(body.session_id);
      }
      return;
    }
    if ((m = /^\/api\/open\/([^/]+)$/.exec(p))) {
      await readBody(req, 64 * 1024);
      const err = UUID_RE.test(m[1]) ? openInVscode(m[1]) : '会話 ID が不正です';
      sendJson(res, err ? 400 : 200, err ? { error: err } : { ok: true });
      return;
    }
    if ((m = /^\/api\/chat\/([^/]+)\/stop$/.exec(p))) {
      await readBody(req, 64 * 1024);
      const c = UUID_RE.test(m[1]) && chats.get(m[1]);
      if (c) {
        c.stop();
      }
      sendJson(res, 200, { ok: !!c });
      return;
    }
    if ((m = /^\/api\/chat\/([^/]+)$/.exec(p))) {
      const body = await readBody(req, 80 * 1024);
      const id = m[1];
      const err = UUID_RE.test(id) ? chatPrecheck(id, body.text) : '会話 ID が不正です';
      if (err) {
        sendJson(res, 409, { error: err });
        return;
      }
      res.writeHead(200, { 'Content-Type': 'application/x-ndjson; charset=utf-8', 'Cache-Control': 'no-cache' });
      // ページを閉じても送信は止めない
      const l = readLive().get(id);
      if (l) {
        chatPipe(id, body.text, res);
      } else {
        chatResume(id, body.text, res);
      }
      return;
    }
    if (p === '/api/notify-settings') {
      const body = await readBody(req, 64 * 1024);
      sendJson(res, 200, { settings: saveNotify(body.settings || body), defaults: NOTIFY_DEFAULTS, file: NOTIFY_FILE });
      return;
    }
    if (p === '/api/notify-test') {
      const body = await readBody(req, 64 * 1024);
      // 保存前の値で出す。時間帯と ON/OFF は見ない
      const s = normalizeNotify(body.settings);
      const kind = body.kind === 'question' ? 'question' : 'done';
      const fake = newRecord('00000000-0000-0000-0000-000000000000', '', '');
      fake.title = 'テスト通知';
      fake.lastReply = kind === 'question' ? 'この内容で進めてよいですか？' : '作業が終わりました。';
      showToast(s, kind, fake, { name: 'テスト' });
      sendJson(res, 200, { ok: true });
      return;
    }
  } catch (e) {
    sendJson(res, e.status || 500, { error: e.status === 413 ? 'too large' : 'bad request' });
    return;
  }
  sendJson(res, 404, { error: 'not found' });
}

// ---------------------------------------------------------------- 起動

module.exports = { mask, looksRandom, humanPrompt, toolLabel, endsWithQuestion, progressOf, newRecord, parseLine, normalizeNotify, inQuiet, stripJsonc, projectName, touch };

if (require.main === module) {
  loadArchive();
  scan(true);
  saveArchive(true);
  setInterval(() => saveArchive(false), 60 * 1000);
  const server = http.createServer((req, res) => {
    handle(req, res).catch(() => {
      if (!res.headersSent) {
        sendJson(res, 500, { error: 'server error' });
      }
    });
  });
  server.listen(PORT, HOST, () => log(`Claude Office: http://${HOST}:${PORT}/ (${sessions.size} 件)`));
  const shutdown = () => {
    saveArchive(true);
    process.exit(0);
  };
  process.on('SIGINT', shutdown);
  process.on('SIGTERM', shutdown);
  process.on('SIGBREAK', shutdown);
}
