// 通知の設定ダイアログ。保存すると notify.json に書かれる
import { esc, toast } from './ui.js';

const HOOK = `{"type":"command","shell":"bash","timeout":10,"async":true,
 "command":"curl -s -m 3 -X POST -H 'Content-Type: application/json' --data-binary @- http://127.0.0.1:8765/api/hook/stop >/dev/null 2>&1 || true"}`;

function form(s) {
  return `<div class="h">🔔 通知の設定</div>
    <label class="row"><input type="checkbox" name="enabled" ${s.enabled ? 'checked' : ''}> 返事が終わったら Windows 通知を出す</label>
    <label class="row"><input type="checkbox" name="done" ${s.done ? 'checked' : ''}> ✅ 完了したとき</label>
    <input type="text" name="doneText" maxlength="120" value="${esc(s.doneText)}">
    <label class="row" style="margin-top:6px"><input type="checkbox" name="question" ${s.question ? 'checked' : ''}> ❓ 質問で終わったとき</label>
    <input type="text" name="questionText" maxlength="120" value="${esc(s.questionText)}">
    <div class="sub">{name}＝会話の名前、{title}＝タイトル</div>
    <div class="row" style="margin-top:8px">表示時間 <select name="duration">
      <option value="short" ${s.duration === 'short' ? 'selected' : ''}>短め</option>
      <option value="long" ${s.duration === 'long' ? 'selected' : ''}>長め</option>
      <option value="sticky" ${s.duration === 'sticky' ? 'selected' : ''}>閉じるまで</option></select>
      <label class="row"><input type="checkbox" name="sound" ${s.sound ? 'checked' : ''}> 🔊 音</label></div>
    <div class="row" style="margin-top:6px"><label class="row"><input type="checkbox" name="quiet" ${s.quiet ? 'checked' : ''}> 🌙 出さない時間帯</label>
      <input type="time" name="quietFrom" value="${esc(s.quietFrom)}">〜<input type="time" name="quietTo" value="${esc(s.quietTo)}"></div>
    <div class="row" style="margin-top:6px">同じ内容は <input type="number" name="sameMinutes" min="0" max="240" value="${s.sameMinutes}" style="width:5em"> 分間出さない</div>
    <div class="row" style="margin-top:10px;flex-wrap:wrap"><button class="btn" data-a="test-done">✅ テスト</button><button class="btn" data-a="test-question">❓ テスト</button><span class="spacer"></span><button class="btn" data-a="reset">既定に戻す</button><button class="btn" data-a="close">閉じる</button><button class="btn primary" data-a="save">保存</button></div>
    <details style="margin-top:8px"><summary class="sub">Claude Code の Stop フック(~/.claude/settings.json の hooks.Stop[].hooks[] に追加)</summary><pre>${esc(HOOK)}</pre></details>`;
}

function read(dlg) {
  const v = (n) => dlg.querySelector(`[name=${n}]`);
  return {
    enabled: v('enabled').checked, done: v('done').checked, question: v('question').checked,
    doneText: v('doneText').value, questionText: v('questionText').value, duration: v('duration').value,
    sound: v('sound').checked, quiet: v('quiet').checked, quietFrom: v('quietFrom').value, quietTo: v('quietTo').value,
    sameMinutes: Number(v('sameMinutes').value),
  };
}

const post = (url, body) => fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });

export async function openNotifyDialog() {
  let info;
  try {
    info = await (await fetch('/api/notify-settings')).json();
  } catch {
    toast('サーバにつながりません', true);
    return;
  }
  const dlg = document.createElement('dialog');
  dlg.className = 'notify';
  const render = (s) => {
    dlg.innerHTML = `${form(s)}<div class="sub" style="margin-top:6px">📄 ${esc(info.file)}</div>`;
  };
  render(info.settings);
  document.body.appendChild(dlg);
  dlg.addEventListener('close', () => dlg.remove());
  dlg.addEventListener('click', async (e) => {
    const a = e.target.closest('[data-a]');
    if (!a) {
      return;
    }
    const act = a.dataset.a;
    if (act === 'close') {
      dlg.close();
    } else if (act === 'reset') {
      render(info.defaults);
    } else if (act === 'save') {
      const r = await post('/api/notify-settings', { settings: read(dlg) });
      if (r.ok) {
        toast('🔔 通知の設定を保存しました');
        dlg.close();
      } else {
        toast('保存できませんでした', true);
      }
    } else if (act.startsWith('test-')) {
      // 保存前の値で出す(時間帯と ON/OFF は見ない)
      await post('/api/notify-test', { settings: read(dlg), kind: act.slice(5) });
      toast('🔔 テスト通知を出しました');
    }
  });
  dlg.showModal();
}
