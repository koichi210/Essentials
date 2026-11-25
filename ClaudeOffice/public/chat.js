// 詳細の窓の下の送信欄。
// - どこでも開いていない会話: 箱庭で claude を動かし始めて送る(本人の発言として届く。以降は会話欄で話す)
// - VSCode 等で開いている会話: その claude の受信口へ送る(「別のセッションから」として届く)。応答は NDJSON の逐次ストリーム
import { toolEmoji } from './people.js';
import { talkFromChat } from './talk.js';

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
    if (data && !data.live) {
      setRunning(true);
      ta.value = '';
      out.textContent = '';
      status.textContent = '⏳ 箱庭で話し始めています…';
      const ok = await talkFromChat(id, text);
      status.textContent = ok ? '' : '❌ 送れませんでした';
      if (!ok) {
        ta.value = text; // 送れなかった文は戻す
      }
      setRunning(false);
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
      // 箱庭で話している間は、話すのは会話欄で(送信欄は隠す)
      el.hidden = !!(d.transcriptGone || d.talk);
      if (d.transcriptGone || d.talk) {
        html = '';
      } else if (d.live) {
        // 本人の発言にはならないことを、送る前に分かるようにする
        html = '<div class="notice">🔁 VSCode（ほかのターミナル）で開いている会話へは「別のセッションから」として届くよ。push など本人の承認が要る頼みは VSCode で。VSCode のタブを閉じれば、箱庭から本人として話せるよ</div>';
      } else {
        html = '<div class="notice">💬 送ると箱庭で話し始めるよ（本人の発言として届いて、ツールの承認は会話欄のカードで出るよ）</div>';
      }
      ta.placeholder = d.live ? '🔁 別のセッションとして送る（Ctrl+Enter）' : '💬 本人として送る（Ctrl+Enter）';
      ta.title = '';
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
