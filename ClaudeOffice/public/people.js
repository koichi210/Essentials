// 机と席割り・キャラの様子の判定(deriveState)

export const TOOL_EMOJI = {
  Read: '📖', Edit: '✏️', Write: '📝', NotebookEdit: '📓', Bash: '💻', PowerShell: '🐚', Grep: '🔎', Glob: '🗂️',
  Agent: '🐣', Task: '🐣', Skill: '🧩', WebSearch: '🌐', WebFetch: '🌍', TodoWrite: '✅', ScheduleWakeup: '⏰',
  AskUserQuestion: '❓',
};

export function toolEmoji(name) {
  return TOOL_EMOJI[name] || '🛠️';
}

export const STATE_ORDER = { working: 0, thinking: 1, waiting: 2, sleeping: 3, ended: 4 };

// キャラの様子。live が無ければ終了、busy ならツール/返事で作業中か考え中、idle は30分で入力待ち→おやすみ
export function deriveState(item, now) {
  const live = item.live;
  if (!live) {
    return { key: 'ended', emoji: '🌙', name: '終了', action: '' };
  }
  if (live.status === 'busy') {
    if (item.state === 'tool' && item.lastTool) {
      const l = item.lastTool;
      return { key: 'working', emoji: toolEmoji(l.n), name: '作業中', action: l.l ? `${l.n}: ${l.l}` : l.n, since: l.t };
    }
    if (item.state === 'reply') {
      return { key: 'working', emoji: '💬', name: '作業中', action: '返信を書いてる', since: live.statusSince };
    }
    return { key: 'thinking', emoji: '💭', name: '考え中', action: '考え中', since: live.statusSince };
  }
  const idleMs = now - (live.statusSince || now);
  if (live.status === 'idle' && idleMs >= 30 * 60 * 1000) {
    return { key: 'sleeping', emoji: '💤', name: 'おやすみ', action: 'おやすみ中', since: live.statusSince };
  }
  const text = live.status === 'idle' ? '入力待ち' : live.status;
  return { key: 'waiting', emoji: '✋', name: '入力待ち', action: text, since: live.statusSince };
}

export function progressBadge(p) {
  if (!p) {
    return null;
  }
  if (p.k === 'background') {
    const parts = [];
    if (p.agents) {
      parts.push(`🐣${p.agents}`);
    }
    if (p.cmds) {
      parts.push(`⚙️${p.cmds}`);
    }
    if (p.wake) {
      parts.push(`⏰${new Date(p.wake).toTimeString().slice(0, 5)}`);
    }
    return { icon: '⏳', text: `裏で作業中 ${parts.join(' ')}`, cls: 'bg' };
  }
  if (p.k === 'question') {
    return { icon: '❓', text: '返事待ち', cls: 'q' };
  }
  if (p.k === 'done') {
    return { icon: '✅', text: '完了', cls: 'done' };
  }
  return null;
}

export function elapsed(ms) {
  if (!ms || ms < 0) {
    return '';
  }
  const m = Math.floor(ms / 60000);
  if (m < 1) {
    return `${Math.floor(ms / 1000)}秒`;
  }
  if (m < 60) {
    return `${m}分`;
  }
  return `${Math.floor(m / 60)}時間${m % 60}分`;
}

// 席割り: 開始の早い順。同じプロジェクトの仲間がいれば一番近い空き席、いなければ他の人から一番遠い席。13人目以降は一覧だけ
export function assignSeats(liveList, seats, previous) {
  const result = new Map();
  const used = new Set();
  const sorted = [...liveList].sort((a, b) => (a.live.startedAt || a.start) - (b.live.startedAt || b.start));
  for (const item of sorted) {
    const prev = previous.get(item.id);
    if (prev !== undefined && !used.has(prev) && prev < seats.length) {
      result.set(item.id, prev);
      used.add(prev);
    }
  }
  const dist = (a, b) => Math.hypot(seats[a].x - seats[b].x, seats[a].z - seats[b].z);
  for (const item of sorted) {
    if (result.has(item.id)) {
      continue;
    }
    const free = seats.map((_, i) => i).filter((i) => !used.has(i));
    if (free.length === 0) {
      break;
    }
    const mates = [...result.entries()].filter(([id]) => sorted.find((s) => s.id === id).project === item.project).map(([, s]) => s);
    let pick;
    if (mates.length) {
      pick = free.reduce((best, i) => (Math.min(...mates.map((m) => dist(i, m))) < Math.min(...mates.map((m) => dist(best, m))) ? i : best), free[0]);
    } else if (used.size) {
      pick = free.reduce((best, i) => (Math.min(...[...used].map((u) => dist(i, u))) > Math.min(...[...used].map((u) => dist(best, u))) ? i : best), free[0]);
    } else {
      pick = free[0];
    }
    result.set(item.id, pick);
    used.add(pick);
  }
  return result;
}
