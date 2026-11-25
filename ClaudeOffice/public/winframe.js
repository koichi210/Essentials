// 窓の上下左右の辺と4つの角で大きさを変える(詳細の窓・別の窓で使う)。
// 窓は position: fixed・overflow: hidden が前提。CSS の resize: both だと xterm が右下の角まで広がって角をつかめないため。

const DIRS = ['n', 's', 'e', 'w', 'ne', 'nw', 'se', 'sw'];

export function addResizeHandles(el, { minW = 320, minH = 220, onStart } = {}) {
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
