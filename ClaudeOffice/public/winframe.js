// 窓の上下左右の辺と4つの角で大きさを変える(詳細の窓・別の窓で使う)。
// 窓は position: fixed・overflow: hidden が前提。CSS の resize: both だと xterm が右下の角まで広がって角をつかめないため。

const DIRS = ['n', 's', 'e', 'w', 'ne', 'nw', 'se', 'sw'];
const MARGIN = 8;

// 窓をブラウザの表示範囲に収める(大きすぎれば縮め、はみ出していれば中へ動かす)。
// 縮めたのは画面の都合なので保存はしない(保存はつまみを離したときだけ)
export function keepInView(el) {
  if (!el.isConnected || el.hidden) {
    return;
  }
  const vw = window.innerWidth;
  const vh = window.innerHeight;
  const r = el.getBoundingClientRect();
  const width = Math.min(r.width, Math.max(160, vw - MARGIN * 2));
  const height = Math.min(r.height, Math.max(120, vh - MARGIN * 2));
  const left = Math.max(0, Math.min(r.left, vw - width - MARGIN));
  const top = Math.max(0, Math.min(r.top, vh - height - MARGIN));
  if (width !== r.width || height !== r.height) {
    el.style.width = `${Math.floor(width)}px`;
    el.style.height = `${Math.floor(height)}px`;
  }
  if (left !== r.left || top !== r.top) {
    el.style.left = `${Math.round(left)}px`;
    el.style.top = `${Math.round(top)}px`;
  }
}

// Chrome の窓を小さくしたら、開いている窓をすべて中へ戻す
window.addEventListener('resize', () => {
  document.querySelectorAll('.win, .talk-win').forEach(keepInView);
});

// onEnd: つまみを離したとき(ここで大きさを保存する。画面に合わせて縮めたときは呼ばれない)
export function addResizeHandles(el, { minW = 320, minH = 220, onStart, onEnd } = {}) {
  for (const dir of DIRS) {
    const h = document.createElement('div');
    h.className = `rz rz-${dir}`;
    h.addEventListener('pointerdown', (e) => {
      if (e.button !== 0) {
        return;
      }
      e.preventDefault();
      e.stopPropagation();
      onStart?.();
      h.setPointerCapture(e.pointerId);
      const r = el.getBoundingClientRect();
      const sx = e.clientX;
      const sy = e.clientY;
      const move = (ev) => {
        const dx = ev.clientX - sx;
        const dy = ev.clientY - sy;
        let { left, top, width, height } = r;
        if (dir.includes('e')) {
          width = Math.min(window.innerWidth - left, Math.max(minW, r.width + dx));
        }
        if (dir.includes('s')) {
          height = Math.min(window.innerHeight - top, Math.max(minH, r.height + dy));
        }
        // 北・西側は位置も動かす(右下の角は固定したまま)。画面の外へは出さない
        if (dir.includes('w')) {
          left = Math.max(0, Math.min(r.right - minW, r.left + dx));
          width = r.right - left;
        }
        if (dir.includes('n')) {
          top = Math.max(0, Math.min(r.bottom - minH, r.top + dy));
          height = r.bottom - top;
        }
        el.style.left = `${Math.round(left)}px`;
        el.style.top = `${Math.round(top)}px`;
        el.style.width = `${Math.round(width)}px`;
        el.style.height = `${Math.round(height)}px`;
      };
      const up = () => {
        onEnd?.();
        h.removeEventListener('pointermove', move);
        h.removeEventListener('pointerup', up);
        h.removeEventListener('pointercancel', up);
      };
      h.addEventListener('pointermove', move);
      h.addEventListener('pointerup', up);
      h.addEventListener('pointercancel', up);
    });
    el.appendChild(h);
  }
}
