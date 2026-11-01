// 詳細の窓の「続きを送る」欄。応答は NDJSON の逐次ストリーム
import { toolEmoji } from './people.js';

// ブラウザ全体で同時に送れるのは1会話だけ
let busy = false;

export function createChat(id) {
  const el = document.createElement('div');
  el.className = 'chat';
  el.innerHTML = `<div class="warn-area"></div>
    <textarea placeholder="💬 続きを送る（Ctrl+Enter）"></textarea>
    <div class="row" style="margin-top:4px"><span class="status sub ellipsis" style="flex:1"></span><button class="btn stop" hidden>⏹ 停止</button><button class="btn primary send">送信</button></div>
    <div class="out"></div>`;
  const ta = el.querySelector('textarea');
  const sendBtn = el.querySelector('.send');
  const stopBtn = el.querySelector('.stop');
  const status = el.querySelector('.status');
  const out = el.querySelector('.out');
  let data = null;
  let running = false;

  const setRunning = (on) => {
    running = on;
    busy = on;
    sendBtn.disabled = on;
    stopBtn.hidden = !on;
  };

  async function send() {
    const text = ta.value;
    if (!text.trim() || running) {
      return;
    }
    if (busy) {
      status.textContent = '⚠️ 別の会話へ送信中です';
      return;
    }
    setRunning(true);
    ta.value = '';
    out.textContent = '';
    status.textContent = '⏳ 送信中…';
    const blocks = new Map();
    const lines = [];
    const draw = () => {
      out.textContent = [...lines, ...blocks.values()].join('\n\n');
      out.scrollTop = out.scrollHeight;
    };
    let ok = false;
    try {
      const r = await fetch(`/api/chat/${id}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ text }) });
      if (!r.ok) {
        const j = await r.json().catch(() => ({}));
        throw new Error(j.error || `エラー ${r.status}`);
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
          const ev = JSON.parse(p);
          if (ev.t === 'start') {
            status.textContent = ev.peer ? '📨 開いている会話へ届けています…' : `🚀 再開しました${ev.model ? `（${ev.model}）` : ''}`;
          } else if (ev.t === 'seen') {
            status.textContent = '👀 受け取られました';
          } else if (ev.t === 'text') {
            // ブロックごとに全文(マスク済み)が届くので置き換える
            blocks.set(ev.k, ev.d);
            draw();
          } else if (ev.t === 'tool') {
            lines.push(`${toolEmoji(ev.n)} ${ev.n}${ev.l ? `: ${ev.l}` : ''}`);
            draw();
          } else if (ev.t === 'done') {
            ok = ev.ok;
            const sec = ev.ms ? `（${Math.round(ev.ms / 1000)}秒）` : '';
            status.textContent = ev.stopped ? '⏹ 停止しました' : ev.ok ? `✅ 完了${sec}${ev.error ? ` ${ev.error}` : ''}` : `❌ ${ev.error || '失敗しました'}`;
          }
        }
      }
    } catch (e) {
      status.textContent = `❌ ${e.message}`;
    }
    if (!ok && !out.textContent) {
      // 失敗した文は入力欄に戻す
      ta.value = text;
    }
    setRunning(false);
  }

  sendBtn.onclick = send;
  stopBtn.onclick = () => {
    fetch(`/api/chat/${id}/stop`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' });
  };
  ta.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && e.ctrlKey) {
      e.preventDefault();
      send();
    }
  });

  return {
    el,
    update(d) {
      data = d;
      const warn = el.querySelector('.warn-area');
      let html = '';
      if (d.transcriptGone) {
        html = '';
        el.hidden = true;
      } else if (d.live) {
        // 常時表示だと窓が狭くなるので、説明は入力欄のツールチップに回す
        html = '';
      } else {
        html = '<div class="notice warn">⚠️ 閉じた会話を再開して送ります。ツールは確認なしで実行されます。</div>';
      }
      ta.title = d.live ? '開いている会話へは「@dashboard からのメッセージ」として届きます。本人の承認の代わりにはなりません。' : '';
      if (warn.__html !== html) {
        warn.__html = html;
        warn.innerHTML = html;
      }
      if (d.chatRunning && !running) {
        status.textContent = '⏳ この会話には送信中です';
      }
      return data;
    },
  };
}
