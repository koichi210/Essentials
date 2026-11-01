// BlurPaint: スクリーンショットに矢印・枠・文字・番号・ぼかし等の注釈を入れる、ブラウザだけで動く軽量エディタ。
// 画像は「ベース画像(canvas) + 注釈オブジェクト配列」で持ち、保存・コピーのときだけ1枚に合成する。外部通信はしない。
(function () {
  'use strict';

  const SETTINGS_KEY = 'blur-paint.settings';
  const HISTORY_LIMIT = 100;
  const HIT_TOL = 6;
  const HANDLE_TOL = 7;
  const MIN_DRAG = 3;
  const UI_FONT = '"Segoe UI", "Yu Gothic UI", "Meiryo", sans-serif';
  const ACCENT = '#1a73e8';
  const SWATCHES = ['#e53935', '#fb8c00', '#fdd835', '#43a047', '#1e88e5', '#8e24aa', '#212121', '#ffffff'];
  const TOOL_KEYS = { v: 'select', a: 'arrow', r: 'rect', e: 'ellipse', l: 'line', p: 'pen', t: 'text', n: 'step', h: 'highlight', b: 'blur', c: 'crop' };
  const RECT_TYPES = ['rect', 'ellipse', 'highlight', 'blur', 'image'];
  const LINE_TYPES = ['arrow', 'line'];
  const STROKE_TYPES = ['arrow', 'line', 'rect', 'ellipse', 'pen'];
  const MIME = { png: 'image/png', jpeg: 'image/jpeg', webp: 'image/webp' };
  const EXT = { png: 'png', jpeg: 'jpg', webp: 'webp' };
  const IMAGE_EXT = /\.(png|jpe?g|jfif|gif|bmp|webp|ico|svg|avif)$/i;

  // プロパティバーに出すグループ(対象 = 選択中オブジェクトの種類、無ければ現在のツール)
  const GROUPS = {
    select: [],
    image: [],
    arrow: ['color', 'width', 'outline', 'shadow'],
    line: ['color', 'width', 'outline', 'shadow'],
    rect: ['color', 'width', 'outline', 'shadow'],
    ellipse: ['color', 'width', 'outline', 'shadow'],
    pen: ['color', 'width', 'outline', 'shadow'],
    text: ['color', 'size', 'textStyle', 'shadow'],
    step: ['color', 'size', 'shadow'],
    highlight: ['color'],
    blur: ['blurMode', 'blurLevel'],
    crop: ['crop'],
  };
  // 設定キー → オブジェクトの属性
  const ATTR = { color: 'color', hlColor: 'color', width: 'width', outline: 'outline', shadow: 'shadow', fontSize: 'size', textStyle: 'style', blurMode: 'mode', blurLevel: 'level' };
  const OBJ_ATTRS = {
    arrow: ['color', 'width', 'outline', 'shadow'],
    line: ['color', 'width', 'outline', 'shadow'],
    rect: ['color', 'width', 'outline', 'shadow'],
    ellipse: ['color', 'width', 'outline', 'shadow'],
    pen: ['color', 'width', 'outline', 'shadow'],
    text: ['color', 'size', 'style', 'shadow'],
    step: ['color', 'size', 'shadow'],
    highlight: ['color'],
    blur: ['mode', 'level'],
    image: [],
  };

  const DEFAULTS = {
    tool: 'arrow', color: '#e53935', hlColor: '#fdd835', width: 4, outline: 'none', shadow: true,
    fontSize: 28, textStyle: 'outline', blurMode: 'blur', blurLevel: 10, pasteMode: 'replace', saveFormat: 'png',
  };

  const $ = (id) => document.getElementById(id);
  const canvas = $('canvas');
  const ctx = canvas.getContext('2d');
  const stage = $('stage');
  const textInput = $('textInput');
  const measureCtx = document.createElement('canvas').getContext('2d');
  const filterSupported = typeof ctx.filter === 'string';

  const settings = loadSettings();
  const state = {
    base: null,
    objects: [],
    tool: settings.tool,
    selected: null,
    draft: null,
    crop: null,
    editing: null,
    drag: null,
    zoom: 1,
    fitMode: true,
    fileName: '',
    dirty: false,
    history: [],
    hIndex: -1,
  };

  // ---------------------------------------------------------------- 設定(localStorage は設定値のみ。失敗は無視)

  function loadSettings() {
    const s = { ...DEFAULTS };
    try {
      const saved = JSON.parse(localStorage.getItem(SETTINGS_KEY) || '{}');
      for (const k of Object.keys(DEFAULTS)) {
        if (saved[k] !== undefined && typeof saved[k] === typeof DEFAULTS[k]) {
          s[k] = saved[k];
        }
      }
    } catch (e) {
      // 読めなければ既定値
    }
    if (!Object.values(TOOL_KEYS).includes(s.tool) || s.tool === 'crop') {
      s.tool = DEFAULTS.tool;
    }
    return s;
  }

  function saveSettings() {
    try {
      localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings));
    } catch (e) {
      // 保存できなくても動く
    }
  }

  // ---------------------------------------------------------------- 小道具

  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
  const px = (n) => n / state.zoom; // 画面上の px を画像ピクセルへ

  function isLight(hex) {
    const h = String(hex).replace('#', '');
    const r = parseInt(h.slice(0, 2), 16) || 0;
    const g = parseInt(h.slice(2, 4), 16) || 0;
    const b = parseInt(h.slice(4, 6), 16) || 0;
    return (r * 299 + g * 587 + b * 114) / 1000 > 170;
  }

  function cloneObj(o) {
    // img(canvas) は参照のまま共有する
    const c = { ...o };
    if (o.points) {
      c.points = o.points.map((p) => [p[0], p[1]]);
    }
    return c;
  }

  // 矩形系の負の幅・高さを正規化した矩形
  function rectOf(o) {
    return {
      x: Math.min(o.x, o.x + o.w), y: Math.min(o.y, o.y + o.h), w: Math.abs(o.w), h: Math.abs(o.h),
    };
  }

  function normalize(o) {
    if (RECT_TYPES.includes(o.type)) {
      Object.assign(o, rectOf(o));
    }
  }

  function snap45(ox, oy, x, y) {
    const a = Math.round(Math.atan2(y - oy, x - ox) / (Math.PI / 4)) * (Math.PI / 4);
    const len = Math.hypot(x - ox, y - oy);
    return [ox + Math.cos(a) * len, oy + Math.sin(a) * len];
  }

  function nextStepNumber() {
    return state.objects.reduce((m, o) => (o.type === 'step' ? Math.max(m, o.n) : m), 0) + 1;
  }

  let toastTimer = null;
  function toast(message) {
    const el = $('toast');
    el.textContent = message;
    el.hidden = false;
    el.classList.remove('fade');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => {
      el.classList.add('fade');
      toastTimer = setTimeout(() => {
        el.hidden = true;
      }, 200);
    }, 2200);
  }

  // ---------------------------------------------------------------- 描画

  let renderQueued = false;
  function requestRender() {
    if (renderQueued) {
      return;
    }
    renderQueued = true;
    requestAnimationFrame(() => {
      renderQueued = false;
      if (state.base) {
        drawScene(ctx, true, null);
      }
      placeTextInput();
    });
  }

  // ① クリア → ② ベース画像 → ③ 注釈を配列順に → ④ UI 層(画面のみ)
  function drawScene(c, ui, format) {
    const W = c.canvas.width;
    const H = c.canvas.height;
    c.save();
    c.setTransform(1, 0, 0, 1, 0, 0);
    c.clearRect(0, 0, W, H);
    if (format === 'jpeg') {
      c.fillStyle = '#ffffff';
      c.fillRect(0, 0, W, H);
    }
    c.drawImage(state.base, 0, 0);
    for (const o of state.objects) {
      if (state.editing && state.editing.obj === o) {
        continue;
      }
      drawObj(c, o);
    }
    if (ui) {
      drawUI(c);
    }
    c.restore();
  }

  function applyShadow(c, k) {
    c.shadowColor = 'rgba(0,0,0,.45)';
    c.shadowBlur = 1.2 * k;
    c.shadowOffsetX = c.shadowOffsetY = Math.max(1, 0.35 * k);
  }

  function clearShadow(c) {
    c.shadowColor = 'transparent';
    c.shadowBlur = 0;
    c.shadowOffsetX = c.shadowOffsetY = 0;
  }

  const outlineColor = (o) => (o.outline === 'white' ? '#ffffff' : '#222222');
  const outlineExtra = (w) => 2 * Math.max(1.5, 0.45 * w);

  // 線系: 縁取りがあれば先に太く描き、影は最下層にだけ付ける
  function strokeWithOutline(c, o, buildPath) {
    c.lineCap = 'round';
    c.lineJoin = 'round';
    const k = 2 + o.width;
    if (o.outline !== 'none') {
      if (o.shadow) {
        applyShadow(c, k);
      }
      c.strokeStyle = outlineColor(o);
      c.lineWidth = o.width + outlineExtra(o.width);
      buildPath();
      c.stroke();
      clearShadow(c);
    } else if (o.shadow) {
      applyShadow(c, k);
    }
    c.strokeStyle = o.color;
    c.lineWidth = o.width;
    buildPath();
    c.stroke();
    clearShadow(c);
  }

  function drawObj(c, o) {
    c.save();
    switch (o.type) {
      case 'rect': {
        const r = rectOf(o);
        strokeWithOutline(c, o, () => {
          c.beginPath();
          c.rect(r.x, r.y, r.w, r.h);
        });
        break;
      }
      case 'ellipse': {
        const r = rectOf(o);
        strokeWithOutline(c, o, () => {
          c.beginPath();
          c.ellipse(r.x + r.w / 2, r.y + r.h / 2, r.w / 2, r.h / 2, 0, 0, Math.PI * 2);
        });
        break;
      }
      case 'line':
        strokeWithOutline(c, o, () => {
          c.beginPath();
          c.moveTo(o.x1, o.y1);
          c.lineTo(o.x2, o.y2);
        });
        break;
      case 'pen':
        if (o.points.length >= 2) {
          strokeWithOutline(c, o, () => penPath(c, o.points));
        }
        break;
      case 'arrow':
        drawArrow(c, o);
        break;
      case 'highlight': {
        const r = rectOf(o);
        c.globalCompositeOperation = 'multiply';
        c.fillStyle = o.color;
        c.fillRect(r.x, r.y, r.w, r.h);
        break;
      }
      case 'blur':
        drawBlur(c, o);
        break;
      case 'image': {
        const r = rectOf(o);
        c.drawImage(o.img, r.x, r.y, r.w, r.h);
        break;
      }
      case 'text':
        drawText(c, o);
        break;
      case 'step':
        drawStep(c, o);
        break;
    }
    c.restore();
  }

  // 中点を結ぶ二次ベジェで平滑化
  function penPath(c, pts) {
    c.beginPath();
    c.moveTo(pts[0][0], pts[0][1]);
    if (pts.length === 2) {
      c.lineTo(pts[1][0], pts[1][1]);
      return;
    }
    for (let i = 1; i < pts.length - 1; i++) {
      const mx = (pts[i][0] + pts[i + 1][0]) / 2;
      const my = (pts[i][1] + pts[i + 1][1]) / 2;
      c.quadraticCurveTo(pts[i][0], pts[i][1], mx, my);
    }
    const last = pts[pts.length - 1];
    c.lineTo(last[0], last[1]);
  }

  // 根元が細く先端が太い塗り矢印
  function arrowGeometry(o) {
    const w = o.width;
    const len = Math.hypot(o.x2 - o.x1, o.y2 - o.y1);
    const headLen = Math.min(0.8 * len, Math.max(16, 5 * w));
    const headHalf = 0.6 * headLen;
    const tipHalf = Math.min(0.5 * headHalf, Math.max(1.5, 0.8 * w));
    const rootHalf = Math.max(1, 0.35 * w);
    const b = len - headLen;
    const back = b - 0.12 * headLen;
    return { len, headLen, headHalf, tipHalf, rootHalf, b, back };
  }

  function drawArrow(c, o) {
    const g = arrowGeometry(o);
    if (g.len < 0.5) {
      return;
    }
    c.translate(o.x1, o.y1);
    c.rotate(Math.atan2(o.y2 - o.y1, o.x2 - o.x1));
    const path = () => {
      c.beginPath();
      c.moveTo(0, -g.rootHalf);
      c.lineTo(g.b, -g.tipHalf);
      c.lineTo(g.back, -g.headHalf);
      c.lineTo(g.len, 0);
      c.lineTo(g.back, g.headHalf);
      c.lineTo(g.b, g.tipHalf);
      c.lineTo(0, g.rootHalf);
      // 根元は半円
      c.arc(0, 0, g.rootHalf, Math.PI / 2, (Math.PI * 3) / 2);
      c.closePath();
    };
    const k = 2 + o.width;
    c.lineJoin = 'round';
    if (o.outline !== 'none') {
      if (o.shadow) {
        applyShadow(c, k);
      }
      c.strokeStyle = outlineColor(o);
      c.lineWidth = outlineExtra(o.width);
      path();
      c.stroke();
      clearShadow(c);
    } else if (o.shadow) {
      applyShadow(c, k);
    }
    c.fillStyle = o.color;
    path();
    c.fill();
    clearShadow(c);
  }

  function textMetrics(o) {
    const lines = o.text.split('\n');
    const lh = Math.round(1.25 * o.size);
    measureCtx.font = `bold ${o.size}px ${UI_FONT}`;
    const width = Math.max(1, ...lines.map((l) => measureCtx.measureText(l).width));
    const pad = o.style === 'box' ? 0.35 * o.size : o.style === 'outline' ? 0.12 * o.size : 0;
    return { lines, lh, width, height: lines.length * lh, pad };
  }

  function drawText(c, o) {
    const m = textMetrics(o);
    const s = o.size;
    const light = isLight(o.color);
    c.font = `bold ${s}px ${UI_FONT}`;
    c.textBaseline = 'top';
    c.lineJoin = 'round';
    const k = 0.15 * s;
    if (o.style === 'box') {
      if (o.shadow) {
        applyShadow(c, k);
      }
      c.beginPath();
      c.roundRect(o.x - m.pad, o.y - m.pad, m.width + 2 * m.pad, m.height + 2 * m.pad, 0.2 * s);
      c.fillStyle = light ? '#333' : '#fff';
      c.fill();
      clearShadow(c);
      c.strokeStyle = o.color;
      c.lineWidth = Math.max(1, 0.07 * s);
      c.stroke();
    }
    const lineY = (i) => o.y + i * m.lh + (m.lh - s) / 2;
    if (o.style === 'outline') {
      if (o.shadow) {
        applyShadow(c, k);
      }
      c.strokeStyle = light ? '#000' : '#fff';
      c.lineWidth = 0.2 * s;
      m.lines.forEach((l, i) => c.strokeText(l, o.x, lineY(i)));
      clearShadow(c);
    } else if (o.style === 'plain' && o.shadow) {
      applyShadow(c, k);
    }
    c.fillStyle = o.color;
    m.lines.forEach((l, i) => c.fillText(l, o.x, lineY(i)));
    clearShadow(c);
  }

  function stepRadius(o) {
    return 0.75 * o.size;
  }

  function drawStep(c, o) {
    const r = stepRadius(o);
    const light = isLight(o.color);
    if (o.shadow) {
      applyShadow(c, 0.3 * r);
    }
    c.beginPath();
    c.arc(o.x, o.y, r, 0, Math.PI * 2);
    c.fillStyle = o.color;
    c.fill();
    clearShadow(c);
    c.lineWidth = Math.max(1.5, 0.14 * r);
    c.strokeStyle = light ? '#333' : '#fff';
    c.stroke();
    const label = String(o.n);
    c.font = `bold ${r * 1.15 * (label.length >= 2 ? 0.95 : 1)}px ${UI_FONT}`;
    c.textAlign = 'center';
    c.textBaseline = 'middle';
    c.fillStyle = light ? '#222' : '#fff';
    c.fillText(label, o.x, o.y + 0.05 * r);
  }

  // ぼかし: それまでに描いた内容(c.canvas)を入力にする。端が薄くならないよう 3σ の余白を端1pxの引き伸ばしで埋める
  function drawBlur(c, o) {
    const src = c.canvas;
    const W = src.width;
    const H = src.height;
    const r = rectOf(o);
    const x = Math.max(0, Math.floor(r.x));
    const y = Math.max(0, Math.floor(r.y));
    const x2 = Math.min(W, Math.ceil(r.x + r.w));
    const y2 = Math.min(H, Math.ceil(r.y + r.h));
    const w = x2 - x;
    const h = y2 - y;
    if (w < 1 || h < 1) {
      return;
    }
    const sigma = o.level;
    c.setTransform(1, 0, 0, 1, 0, 0);
    c.globalCompositeOperation = 'source-over';
    if (o.mode === 'pixel') {
      const block = Math.max(2, Math.round(1.5 * sigma));
      const small = document.createElement('canvas');
      small.width = Math.max(1, Math.ceil(w / block));
      small.height = Math.max(1, Math.ceil(h / block));
      const sc = small.getContext('2d');
      sc.imageSmoothingEnabled = true;
      sc.drawImage(src, x, y, w, h, 0, 0, small.width, small.height);
      c.imageSmoothingEnabled = false;
      c.drawImage(small, 0, 0, small.width, small.height, x, y, w, h);
      c.imageSmoothingEnabled = true;
      return;
    }
    const pad = Math.ceil(3 * sigma);
    const tmp = document.createElement('canvas');
    tmp.width = w + 2 * pad;
    tmp.height = h + 2 * pad;
    const tc = tmp.getContext('2d');
    const sx0 = Math.max(0, x - pad);
    const sy0 = Math.max(0, y - pad);
    const sx1 = Math.min(W, x2 + pad);
    const sy1 = Math.min(H, y2 + pad);
    const dx = sx0 - (x - pad);
    const dy = sy0 - (y - pad);
    const cw = sx1 - sx0;
    const ch = sy1 - sy0;
    tc.drawImage(src, sx0, sy0, cw, ch, dx, dy, cw, ch);
    // 画像の外にはみ出した余白は、端の1pxを引き伸ばして埋める
    const right = tmp.width - (dx + cw);
    const bottom = tmp.height - (dy + ch);
    if (dx > 0) {
      tc.drawImage(tmp, dx, dy, 1, ch, 0, dy, dx, ch);
    }
    if (right > 0) {
      tc.drawImage(tmp, dx + cw - 1, dy, 1, ch, dx + cw, dy, right, ch);
    }
    if (dy > 0) {
      tc.drawImage(tmp, 0, dy, tmp.width, 1, 0, 0, tmp.width, dy);
    }
    if (bottom > 0) {
      tc.drawImage(tmp, 0, dy + ch - 1, tmp.width, 1, 0, dy + ch, tmp.width, bottom);
    }
    const out = document.createElement('canvas');
    out.width = tmp.width;
    out.height = tmp.height;
    const oc = out.getContext('2d');
    if (filterSupported) {
      oc.filter = `blur(${sigma}px)`;
      oc.drawImage(tmp, 0, 0);
      oc.filter = 'none';
    } else {
      // ctx.filter 非対応なら縮小→拡大(平滑化 ON)で代替
      const f = Math.max(1, sigma);
      const sm = document.createElement('canvas');
      sm.width = Math.max(1, Math.round(tmp.width / f));
      sm.height = Math.max(1, Math.round(tmp.height / f));
      sm.getContext('2d').drawImage(tmp, 0, 0, sm.width, sm.height);
      oc.imageSmoothingEnabled = true;
      oc.drawImage(sm, 0, 0, out.width, out.height);
    }
    c.drawImage(out, pad, pad, w, h, x, y, w, h);
  }

  // ---------------------------------------------------------------- UI 層(画面のみ。書き出しには含めない)

  function drawUI(c) {
    if (state.draft) {
      drawObj(c, state.draft);
    }
    c.lineWidth = px(1);
    // 選択・ぼかしツール中は全ぼかし範囲に青点線枠
    if (state.tool === 'select' || state.tool === 'blur') {
      c.setLineDash([px(3), px(3)]);
      c.strokeStyle = ACCENT;
      for (const o of state.objects.concat(state.draft && state.draft.type === 'blur' ? [state.draft] : [])) {
        if (o.type === 'blur') {
          const r = rectOf(o);
          c.strokeRect(r.x, r.y, r.w, r.h);
        }
      }
    }
    const sel = state.selected;
    if (sel && !(state.editing && state.editing.obj === sel)) {
      if (!LINE_TYPES.includes(sel.type)) {
        const b = bbox(sel);
        const m = px(3);
        c.setLineDash([px(4), px(3)]);
        c.strokeStyle = ACCENT;
        c.strokeRect(b.x - m, b.y - m, b.w + 2 * m, b.h + 2 * m);
      }
      c.setLineDash([]);
      const hs = px(8);
      for (const h of handlesOf(sel)) {
        c.fillStyle = '#fff';
        c.strokeStyle = ACCENT;
        c.lineWidth = px(1.5);
        c.fillRect(h.x - hs / 2, h.y - hs / 2, hs, hs);
        c.strokeRect(h.x - hs / 2, h.y - hs / 2, hs, hs);
      }
    }
    if (state.crop) {
      const r = rectOf(state.crop);
      c.setLineDash([]);
      c.fillStyle = 'rgba(0,0,0,.5)';
      c.beginPath();
      c.rect(0, 0, c.canvas.width, c.canvas.height);
      c.rect(r.x, r.y, r.w, r.h);
      c.fill('evenodd');
      c.setLineDash([px(5), px(4)]);
      c.strokeStyle = '#fff';
      c.lineWidth = px(1.5);
      c.strokeRect(r.x, r.y, r.w, r.h);
    }
    c.setLineDash([]);
  }

  // ---------------------------------------------------------------- 外接矩形・ハンドル・当たり判定

  function bbox(o) {
    switch (o.type) {
      case 'rect':
      case 'ellipse': {
        const r = rectOf(o);
        const e = o.width / 2;
        return { x: r.x - e, y: r.y - e, w: r.w + 2 * e, h: r.h + 2 * e };
      }
      case 'highlight':
      case 'blur':
      case 'image':
        return rectOf(o);
      case 'arrow':
      case 'line': {
        const e = o.type === 'arrow' ? arrowGeometry(o).headHalf : o.width / 2;
        const x = Math.min(o.x1, o.x2) - e;
        const y = Math.min(o.y1, o.y2) - e;
        return { x, y, w: Math.abs(o.x2 - o.x1) + 2 * e, h: Math.abs(o.y2 - o.y1) + 2 * e };
      }
      case 'pen': {
        const xs = o.points.map((p) => p[0]);
        const ys = o.points.map((p) => p[1]);
        const e = o.width / 2;
        const x = Math.min(...xs) - e;
        const y = Math.min(...ys) - e;
        return { x, y, w: Math.max(...xs) - Math.min(...xs) + 2 * e, h: Math.max(...ys) - Math.min(...ys) + 2 * e };
      }
      case 'text': {
        const m = textMetrics(o);
        return { x: o.x - m.pad, y: o.y - m.pad, w: m.width + 2 * m.pad, h: m.height + 2 * m.pad };
      }
      case 'step': {
        const r = stepRadius(o);
        return { x: o.x - r, y: o.y - r, w: 2 * r, h: 2 * r };
      }
    }
    return { x: 0, y: 0, w: 0, h: 0 };
  }

  function handlesOf(o) {
    if (RECT_TYPES.includes(o.type)) {
      const r = rectOf(o);
      const cx = r.x + r.w / 2;
      const cy = r.y + r.h / 2;
      return [
        { id: 'nw', x: r.x, y: r.y }, { id: 'n', x: cx, y: r.y }, { id: 'ne', x: r.x + r.w, y: r.y },
        { id: 'e', x: r.x + r.w, y: cy }, { id: 'se', x: r.x + r.w, y: r.y + r.h }, { id: 's', x: cx, y: r.y + r.h },
        { id: 'sw', x: r.x, y: r.y + r.h }, { id: 'w', x: r.x, y: cy },
      ];
    }
    if (LINE_TYPES.includes(o.type)) {
      return [{ id: 'p1', x: o.x1, y: o.y1 }, { id: 'p2', x: o.x2, y: o.y2 }];
    }
    return [];
  }

  function handleAt(p) {
    const o = state.selected;
    if (!o) {
      return null;
    }
    const tol = px(HANDLE_TOL);
    return handlesOf(o).find((h) => Math.abs(h.x - p.x) <= tol && Math.abs(h.y - p.y) <= tol) || null;
  }

  function distToSeg(px0, py0, x1, y1, x2, y2) {
    const dx = x2 - x1;
    const dy = y2 - y1;
    const l2 = dx * dx + dy * dy;
    let t = l2 ? ((px0 - x1) * dx + (py0 - y1) * dy) / l2 : 0;
    t = clamp(t, 0, 1);
    return Math.hypot(px0 - (x1 + t * dx), py0 - (y1 + t * dy));
  }

  function hitTest(o, p) {
    const tol = px(HIT_TOL);
    switch (o.type) {
      case 'rect': {
        // 枠線付近だけが当たり(内側の別オブジェクトを選べる)
        const r = rectOf(o);
        const e = tol + o.width / 2;
        const outer = p.x >= r.x - e && p.x <= r.x + r.w + e && p.y >= r.y - e && p.y <= r.y + r.h + e;
        const inner = p.x > r.x + e && p.x < r.x + r.w - e && p.y > r.y + e && p.y < r.y + r.h - e;
        return outer && !inner;
      }
      case 'ellipse': {
        const r = rectOf(o);
        const rx = Math.max(r.w / 2, 0.5);
        const ry = Math.max(r.h / 2, 0.5);
        const d = Math.hypot((p.x - r.x - rx) / rx, (p.y - r.y - ry) / ry);
        return Math.abs(d - 1) * Math.min(rx, ry) <= tol + o.width / 2;
      }
      case 'arrow':
      case 'line': {
        const e = o.type === 'arrow' ? Math.max(o.width, arrowGeometry(o).tipHalf) : o.width / 2;
        return distToSeg(p.x, p.y, o.x1, o.y1, o.x2, o.y2) <= tol + e;
      }
      case 'pen':
        for (let i = 1; i < o.points.length; i++) {
          const a = o.points[i - 1];
          const b = o.points[i];
          if (distToSeg(p.x, p.y, a[0], a[1], b[0], b[1]) <= tol + o.width / 2) {
            return true;
          }
        }
        return false;
      case 'step':
        return Math.hypot(p.x - o.x, p.y - o.y) <= stepRadius(o) + tol;
      default: {
        const b = bbox(o);
        return p.x >= b.x - tol && p.x <= b.x + b.w + tol && p.y >= b.y - tol && p.y <= b.y + b.h + tol;
      }
    }
  }

  // 最前面から当たり判定
  function objectAt(p) {
    for (let i = state.objects.length - 1; i >= 0; i--) {
      if (hitTest(state.objects[i], p)) {
        return state.objects[i];
      }
    }
    return null;
  }

  // ---------------------------------------------------------------- 履歴

  function snapshot() {
    return { base: state.base, objects: state.objects.map(cloneObj) };
  }

  function commit() {
    state.history = state.history.slice(0, state.hIndex + 1);
    state.history.push(snapshot());
    if (state.history.length > HISTORY_LIMIT) {
      state.history.splice(0, state.history.length - HISTORY_LIMIT);
    }
    state.hIndex = state.history.length - 1;
    if (state.history.length >= 2) {
      state.dirty = true;
    }
    refreshUI();
  }

  function restore(index) {
    const prevBase = state.base;
    const snap = state.history[index];
    state.hIndex = index;
    state.base = snap.base;
    state.objects = snap.objects.map(cloneObj);
    state.selected = null;
    state.draft = null;
    state.crop = null;
    state.drag = null;
    state.dirty = true;
    if (!prevBase || prevBase.width !== state.base.width || prevBase.height !== state.base.height) {
      setupCanvas();
      fit();
    }
    refreshUI();
  }

  function undo() {
    finishTextEdit(true);
    if (state.hIndex > 0) {
      restore(state.hIndex - 1);
    }
  }

  function redo() {
    finishTextEdit(true);
    if (state.hIndex < state.history.length - 1) {
      restore(state.hIndex + 1);
    }
  }

  // ---------------------------------------------------------------- ズーム

  function visibleSize() {
    return { w: Math.max(1, stage.clientWidth - 40), h: Math.max(1, stage.clientHeight - 40) };
  }

  function setupCanvas() {
    canvas.width = state.base.width;
    canvas.height = state.base.height;
    $('canvasBox').hidden = false;
    $('empty').hidden = true;
  }

  function applyZoom() {
    if (!state.base) {
      return;
    }
    canvas.style.width = `${state.base.width * state.zoom}px`;
    canvas.style.height = `${state.base.height * state.zoom}px`;
    $('zoomLabel').textContent = `${Math.round(state.zoom * 100)}%`;
    requestRender();
  }

  function fit() {
    if (!state.base) {
      return;
    }
    const v = visibleSize();
    state.zoom = Math.min(1, v.w / state.base.width, v.h / state.base.height);
    state.fitMode = true;
    applyZoom();
  }

  // anchor(clientX, clientY) の下の画像位置を固定したまま倍率を変える
  function setZoom(z, anchor) {
    if (!state.base) {
      return;
    }
    const before = canvas.getBoundingClientRect();
    const a = anchor || { x: stage.getBoundingClientRect().left + stage.clientWidth / 2, y: stage.getBoundingClientRect().top + stage.clientHeight / 2 };
    const ix = (a.x - before.left) / state.zoom;
    const iy = (a.y - before.top) / state.zoom;
    state.zoom = clamp(z, 0.05, 8);
    state.fitMode = false;
    applyZoom();
    const after = canvas.getBoundingClientRect();
    stage.scrollLeft += after.left + ix * state.zoom - a.x;
    stage.scrollTop += after.top + iy * state.zoom - a.y;
  }

  // ---------------------------------------------------------------- 座標

  function toImg(e) {
    const r = canvas.getBoundingClientRect();
    return { x: ((e.clientX - r.left) * canvas.width) / r.width, y: ((e.clientY - r.top) * canvas.height) / r.height };
  }

  // ---------------------------------------------------------------- オブジェクト生成

  function newObject(type, p) {
    switch (type) {
      case 'arrow':
      case 'line':
        return { type, x1: p.x, y1: p.y, x2: p.x, y2: p.y, color: settings.color, width: settings.width, outline: settings.outline, shadow: settings.shadow };
      case 'rect':
      case 'ellipse':
        return { type, x: p.x, y: p.y, w: 0, h: 0, color: settings.color, width: settings.width, outline: settings.outline, shadow: settings.shadow };
      case 'highlight':
        return { type, x: p.x, y: p.y, w: 0, h: 0, color: settings.hlColor };
      case 'blur':
        return { type, x: p.x, y: p.y, w: 0, h: 0, mode: settings.blurMode, level: settings.blurLevel };
      case 'pen':
        return { type, points: [[p.x, p.y]], color: settings.color, width: settings.width, outline: settings.outline, shadow: settings.shadow };
      case 'text':
        return { type, x: p.x, y: p.y - settings.fontSize * 0.6, text: '', size: settings.fontSize, style: settings.textStyle, color: settings.color, shadow: settings.shadow };
      case 'step':
        return { type, x: p.x, y: p.y, n: nextStepNumber(), size: settings.fontSize, color: settings.color, shadow: settings.shadow };
    }
    return null;
  }

  // ---------------------------------------------------------------- ポインター操作

  function onPointerDown(e) {
    if (e.button !== 0 || !state.base) {
      return;
    }
    // テキスト編集中は確定するだけで新しい操作は始めない
    if (state.editing) {
      finishTextEdit(true);
      e.preventDefault();
      return;
    }
    canvas.setPointerCapture(e.pointerId);
    const p = toImg(e);
    const tool = state.tool;
    if (tool === 'crop') {
      state.crop = { x: clamp(p.x, 0, canvas.width), y: clamp(p.y, 0, canvas.height), w: 0, h: 0 };
      state.drag = { kind: 'crop', start: p, moved: false };
      refreshUI();
      return;
    }
    // 選択中オブジェクトのハンドルは、どのツールでも最初に判定する
    const h = handleAt(p);
    if (h) {
      state.drag = { kind: 'resize', start: p, orig: cloneObj(state.selected), handle: h.id, moved: false };
      return;
    }
    if (tool === 'select') {
      const hit = objectAt(p);
      state.selected = hit;
      if (hit) {
        state.drag = { kind: 'move', start: p, orig: cloneObj(hit), moved: false };
      }
      refreshUI();
      return;
    }
    if (tool === 'text') {
      const hit = objectAt(p);
      if (hit && hit.type === 'text') {
        startTextEdit(hit, false);
      } else {
        state.selected = null;
        startTextEdit(newObject('text', p), true);
      }
      return;
    }
    state.selected = null;
    if (tool === 'step') {
      // 置いた直後はそのままドラッグで位置を調整できる
      const o = newObject('step', p);
      state.objects.push(o);
      state.selected = o;
      state.drag = { kind: 'move', start: p, orig: cloneObj(o), moved: true, created: true };
      refreshUI();
      return;
    }
    state.draft = newObject(tool, p);
    state.drag = { kind: 'create', start: p, moved: false, lastScreen: { x: e.clientX, y: e.clientY } };
    refreshUI();
  }

  function onPointerMove(e) {
    if (!state.base) {
      return;
    }
    const p = toImg(e);
    const d = state.drag;
    if (!d) {
      updateCursor(p);
      return;
    }
    const dx = p.x - d.start.x;
    const dy = p.y - d.start.y;
    if (Math.hypot(dx, dy) > 0) {
      d.moved = true;
    }
    if (d.kind === 'move') {
      moveObject(state.selected, d.orig, dx, dy);
    } else if (d.kind === 'resize') {
      resizeObject(state.selected, d.orig, d.handle, p, e.shiftKey);
    } else if (d.kind === 'crop') {
      state.crop.w = clamp(p.x, 0, canvas.width) - state.crop.x;
      state.crop.h = clamp(p.y, 0, canvas.height) - state.crop.y;
      updateCropSize();
    } else if (d.kind === 'create') {
      updateDraft(state.draft, d, p, e);
    }
    requestRender();
  }

  function updateDraft(o, d, p, e) {
    if (o.type === 'pen') {
      // 直前点から画面上で 1.5px 以上動いたら点を追加
      if (Math.hypot(e.clientX - d.lastScreen.x, e.clientY - d.lastScreen.y) >= 1.5) {
        o.points.push([p.x, p.y]);
        d.lastScreen = { x: e.clientX, y: e.clientY };
      }
    } else if (LINE_TYPES.includes(o.type)) {
      const q = e.shiftKey ? snap45(o.x1, o.y1, p.x, p.y) : [p.x, p.y];
      o.x2 = q[0];
      o.y2 = q[1];
    } else {
      let w = p.x - d.start.x;
      let h = p.y - d.start.y;
      if (e.shiftKey && (o.type === 'rect' || o.type === 'ellipse')) {
        // 長い方の辺に揃える
        const s = Math.max(Math.abs(w), Math.abs(h));
        w = Math.sign(w || 1) * s;
        h = Math.sign(h || 1) * s;
      }
      o.w = w;
      o.h = h;
    }
  }

  function moveObject(o, orig, dx, dy) {
    if (LINE_TYPES.includes(o.type)) {
      o.x1 = orig.x1 + dx;
      o.y1 = orig.y1 + dy;
      o.x2 = orig.x2 + dx;
      o.y2 = orig.y2 + dy;
    } else if (o.type === 'pen') {
      o.points = orig.points.map((q) => [q[0] + dx, q[1] + dy]);
    } else {
      o.x = orig.x + dx;
      o.y = orig.y + dy;
    }
  }

  function resizeObject(o, orig, handle, p, shift) {
    if (LINE_TYPES.includes(o.type)) {
      const other = handle === 'p1' ? [orig.x2, orig.y2] : [orig.x1, orig.y1];
      const q = shift ? snap45(other[0], other[1], p.x, p.y) : [p.x, p.y];
      if (handle === 'p1') {
        o.x1 = q[0];
        o.y1 = q[1];
      } else {
        o.x2 = q[0];
        o.y2 = q[1];
      }
      return;
    }
    const r = rectOf(orig);
    let x0 = r.x;
    let y0 = r.y;
    let x1 = r.x + r.w;
    let y1 = r.y + r.h;
    if (handle.includes('w')) {
      x0 = p.x;
    }
    if (handle.includes('e')) {
      x1 = p.x;
    }
    if (handle.includes('n')) {
      y0 = p.y;
    }
    if (handle.includes('s')) {
      y1 = p.y;
    }
    const corner = handle.length === 2;
    if (corner && (shift || o.type === 'image')) {
      // 対角を固定。画像は Shift なしでも元の縦横比、それ以外は正方形
      const ax = handle.includes('w') ? r.x + r.w : r.x;
      const ay = handle.includes('n') ? r.y + r.h : r.y;
      let w = p.x - ax;
      let h = p.y - ay;
      if (o.type === 'image') {
        const ratio = r.w / Math.max(1e-6, r.h);
        if (Math.abs(w) / Math.max(1e-6, Math.abs(h)) > ratio) {
          w = Math.sign(w || 1) * Math.abs(h) * ratio;
        } else {
          h = (Math.sign(h || 1) * Math.abs(w)) / ratio;
        }
      } else {
        const s = Math.max(Math.abs(w), Math.abs(h));
        w = Math.sign(w || 1) * s;
        h = Math.sign(h || 1) * s;
      }
      x0 = ax;
      y0 = ay;
      x1 = ax + w;
      y1 = ay + h;
    }
    o.x = x0;
    o.y = y0;
    o.w = x1 - x0;
    o.h = y1 - y0;
  }

  // 終了処理は window の pointerup/pointercancel と canvas の lostpointercapture の全てで呼ぶ(二重呼び出しは drag=null で無害化)
  function endDrag() {
    const d = state.drag;
    if (!d) {
      return;
    }
    state.drag = null;
    if (d.kind === 'create') {
      const o = state.draft;
      state.draft = null;
      if (o && bigEnough(o)) {
        normalize(o);
        state.objects.push(o);
        state.selected = o;
        commit();
      }
    } else if (d.kind === 'move' || d.kind === 'resize') {
      if (state.selected) {
        normalize(state.selected);
      }
      // 動いた時だけ履歴登録
      if (d.moved || d.created) {
        commit();
      }
    } else if (d.kind === 'crop') {
      const r = rectOf(state.crop);
      state.crop = r.w < 2 || r.h < 2 ? null : r;
    }
    refreshUI();
  }

  // 画面上で 3px 未満の図形は作らない(クリックだけで点や空の枠ができないように)
  function bigEnough(o) {
    const min = px(MIN_DRAG);
    if (o.type === 'pen') {
      return o.points.length >= 2;
    }
    if (LINE_TYPES.includes(o.type)) {
      return Math.hypot(o.x2 - o.x1, o.y2 - o.y1) >= min;
    }
    return Math.abs(o.w) >= min && Math.abs(o.h) >= min;
  }

  const RESIZE_CURSOR = { nw: 'nwse-resize', se: 'nwse-resize', ne: 'nesw-resize', sw: 'nesw-resize', n: 'ns-resize', s: 'ns-resize', e: 'ew-resize', w: 'ew-resize', p1: 'crosshair', p2: 'crosshair' };

  function updateCursor(p) {
    let cur = state.tool === 'select' ? 'default' : state.tool === 'text' ? 'text' : 'crosshair';
    if (state.tool !== 'crop') {
      const h = handleAt(p);
      if (h) {
        cur = RESIZE_CURSOR[h.id];
      } else if (state.tool === 'select' && objectAt(p)) {
        cur = 'move';
      }
    }
    canvas.style.cursor = cur;
  }

  // ---------------------------------------------------------------- テキスト編集

  function startTextEdit(obj, isNew) {
    state.editing = { obj, isNew, original: cloneObj(obj) };
    state.selected = isNew ? null : obj;
    if (isNew) {
      state.objects.push(obj);
    }
    textInput.value = obj.text;
    textInput.hidden = false;
    placeTextInput();
    refreshUI();
    textInput.focus();
    textInput.setSelectionRange(textInput.value.length, textInput.value.length);
  }

  // 位置・フォント・幅高を倍率込みで毎入力ごとに再計算する
  function placeTextInput() {
    const ed = state.editing;
    if (!ed) {
      return;
    }
    const o = ed.obj;
    const z = state.zoom;
    const s = o.size * z;
    textInput.style.left = `${o.x * z}px`;
    textInput.style.top = `${o.y * z}px`;
    textInput.style.fontSize = `${s}px`;
    textInput.style.fontFamily = UI_FONT;
    textInput.style.color = o.color;
    const dark = isLight(o.color) ? '#000' : '#fff';
    textInput.style.textShadow = o.style === 'outline' ? `0 0 ${Math.max(1, 0.12 * s)}px ${dark}, 0 0 ${Math.max(1, 0.12 * s)}px ${dark}` : 'none';
    const lines = textInput.value.split('\n').length;
    textInput.style.height = `${Math.round(1.25 * o.size) * lines * z + 2}px`;
    textInput.style.width = '0px';
    textInput.style.width = `${Math.max(s, textInput.scrollWidth + 4)}px`;
  }

  function finishTextEdit(accept) {
    const ed = state.editing;
    if (!ed) {
      return;
    }
    state.editing = null;
    textInput.hidden = true;
    const o = ed.obj;
    if (!accept) {
      if (ed.isNew) {
        state.objects = state.objects.filter((x) => x !== o);
      } else {
        Object.assign(o, ed.original);
      }
      state.selected = ed.isNew ? null : o;
      refreshUI();
      return;
    }
    const text = textInput.value.replace(/\s+$/, '');
    if (!text) {
      // 空なら新規は破棄・既存は削除
      state.objects = state.objects.filter((x) => x !== o);
      state.selected = null;
      if (!ed.isNew) {
        commit();
      }
      refreshUI();
      return;
    }
    const changed = ed.isNew || text !== ed.original.text;
    o.text = text;
    state.selected = o;
    if (changed) {
      commit();
    }
    refreshUI();
  }

  textInput.addEventListener('input', () => {
    if (state.editing) {
      state.editing.obj.text = textInput.value;
      placeTextInput();
    }
  });

  textInput.addEventListener('keydown', (e) => {
    // グローバルのショートカットに渡さない
    e.stopPropagation();
    // IME 変換中の Enter は何もしない(変換確定で文字が確定してしまうのを防ぐ)
    if (e.isComposing || e.keyCode === 229) {
      return;
    }
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      finishTextEdit(true);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      finishTextEdit(false);
    }
  });

  textInput.addEventListener('blur', () => finishTextEdit(true));

  // ---------------------------------------------------------------- 操作(削除・複製・矢印キー・トリミング)

  function deleteSelected() {
    if (!state.selected) {
      return;
    }
    state.objects = state.objects.filter((o) => o !== state.selected);
    state.selected = null;
    commit();
  }

  function duplicateSelected() {
    if (!state.selected) {
      return;
    }
    const c = cloneObj(state.selected);
    moveObject(c, cloneObj(c), 16, 16);
    if (c.type === 'step') {
      c.n = nextStepNumber();
    }
    state.objects.push(c);
    state.selected = c;
    commit();
  }

  function nudge(dx, dy) {
    if (!state.selected) {
      return;
    }
    moveObject(state.selected, cloneObj(state.selected), dx, dy);
    commit();
  }

  function runCrop() {
    if (!state.crop) {
      return;
    }
    const r = rectOf(state.crop);
    const x = Math.round(r.x);
    const y = Math.round(r.y);
    const w = Math.round(r.w);
    const h = Math.round(r.h);
    if (w < 2 || h < 2) {
      return;
    }
    const c = document.createElement('canvas');
    c.width = w;
    c.height = h;
    c.getContext('2d').drawImage(state.base, -x, -y);
    // トリミングは新しい canvas を作るので、旧画像は履歴内に残り undo で戻せる
    state.base = c;
    state.objects.forEach((o) => moveObject(o, cloneObj(o), -x, -y));
    state.crop = null;
    state.selected = null;
    setupCanvas();
    setTool('select');
    commit();
    fit();
  }

  function cancelCrop() {
    state.crop = null;
    refreshUI();
  }

  // ---------------------------------------------------------------- ツール

  function setTool(tool) {
    finishTextEdit(true);
    if (tool !== 'crop') {
      state.crop = null;
    }
    if (tool !== 'select') {
      state.selected = null;
    }
    state.tool = tool;
    if (tool !== 'crop') {
      settings.tool = tool;
      saveSettings();
    }
    refreshUI();
  }

  // ---------------------------------------------------------------- 読込

  function loadBlob(blob, name, allowOverlay) {
    const url = URL.createObjectURL(blob);
    const img = new Image();
    img.onload = () => {
      URL.revokeObjectURL(url);
      // naturalWidth が 0 の SVG 等は 1024×768
      const w = img.naturalWidth || 1024;
      const h = img.naturalHeight || 768;
      const c = document.createElement('canvas');
      c.width = w;
      c.height = h;
      c.getContext('2d').drawImage(img, 0, 0, w, h);
      finishTextEdit(true);
      if (allowOverlay && settings.pasteMode === 'overlay' && state.base) {
        addImageObject(c);
      } else {
        setBase(c, name);
      }
    };
    img.onerror = () => {
      URL.revokeObjectURL(url);
      toast('この画像形式は読み込めません');
    };
    img.src = url;
  }

  function setBase(c, name) {
    state.base = c;
    state.objects = [];
    state.selected = null;
    state.draft = null;
    state.crop = null;
    state.fileName = name || '';
    setupCanvas();
    // 新規画像の読込も履歴に1件積む(リセットしない)
    commit();
    fit();
  }

  // ベースの 90% に収まるよう縮小して中央に置く
  function addImageObject(c) {
    const W = state.base.width;
    const H = state.base.height;
    const s = Math.min(1, (0.9 * W) / c.width, (0.9 * H) / c.height);
    const w = c.width * s;
    const h = c.height * s;
    const o = { type: 'image', x: (W - w) / 2, y: (H - h) / 2, w, h, img: c };
    state.objects.push(o);
    state.tool = 'select';
    state.selected = o;
    commit();
  }

  function isImageFile(f) {
    return f && (/^image\//.test(f.type) || IMAGE_EXT.test(f.name || ''));
  }

  function openFilePicker() {
    finishTextEdit(true);
    $('fileInput').click();
  }

  $('fileInput').addEventListener('change', (e) => {
    const f = e.target.files[0];
    // 開くは常に新規ベース
    if (f) {
      loadBlob(f, f.name, false);
    }
    e.target.value = '';
  });

  function isFormElement(el) {
    if (!el) {
      return false;
    }
    const tag = el.tagName;
    return tag === 'TEXTAREA' || tag === 'SELECT' || (tag === 'INPUT' && el.type !== 'range');
  }

  // Ctrl+V は paste イベントの image/* ファイル項目
  document.addEventListener('paste', (e) => {
    if (state.editing || isFormElement(document.activeElement)) {
      return;
    }
    const items = e.clipboardData ? Array.from(e.clipboardData.items) : [];
    const item = items.find((i) => i.kind === 'file' && /^image\//.test(i.type));
    if (!item) {
      toast('クリップボードに画像がありません');
      return;
    }
    e.preventDefault();
    loadBlob(item.getAsFile(), '', true);
  });

  async function pasteFromButton() {
    finishTextEdit(true);
    if (!navigator.clipboard || !navigator.clipboard.read) {
      toast('Ctrl+V で貼り付けてください');
      return;
    }
    try {
      const items = await navigator.clipboard.read();
      for (const it of items) {
        const type = it.types.find((t) => /^image\//.test(t));
        if (type) {
          loadBlob(await it.getType(type), '', true);
          return;
        }
      }
      toast('クリップボードに画像がありません');
    } catch (err) {
      toast('Ctrl+V で貼り付けてください');
    }
  }

  // ドロップ(Files を含む時のみ)
  const hasFiles = (e) => e.dataTransfer && Array.from(e.dataTransfer.types || []).includes('Files');
  let dragDepth = 0;
  window.addEventListener('dragenter', (e) => {
    if (hasFiles(e)) {
      dragDepth++;
      stage.classList.add('dragover');
    }
  });
  window.addEventListener('dragleave', (e) => {
    if (hasFiles(e)) {
      dragDepth = Math.max(0, dragDepth - 1);
      if (!dragDepth) {
        stage.classList.remove('dragover');
      }
    }
  });
  window.addEventListener('dragover', (e) => {
    if (hasFiles(e)) {
      e.preventDefault();
      e.dataTransfer.dropEffect = 'copy';
    }
  });
  window.addEventListener('drop', (e) => {
    if (!hasFiles(e)) {
      return;
    }
    e.preventDefault();
    dragDepth = 0;
    stage.classList.remove('dragover');
    const f = Array.from(e.dataTransfer.files).find(isImageFile);
    if (f) {
      loadBlob(f, f.name, true);
    } else {
      toast('この画像形式は読み込めません');
    }
  });

  // ---------------------------------------------------------------- 書き出し

  function exportBlob(format) {
    finishTextEdit(true);
    const c = document.createElement('canvas');
    c.width = state.base.width;
    c.height = state.base.height;
    drawScene(c.getContext('2d'), false, format);
    return new Promise((resolve, reject) => {
      c.toBlob((b) => (b ? resolve(b) : reject(new Error('toBlob'))), MIME[format], 0.92);
    });
  }

  async function copyImage() {
    if (!state.base) {
      return;
    }
    try {
      // Blob ではなく Promise を渡す(生成待ちの間にユーザー操作の有効期限が切れないように)
      await navigator.clipboard.write([new ClipboardItem({ 'image/png': exportBlob('png') })]);
      state.dirty = false;
      toast('クリップボードにコピーしました');
    } catch (err) {
      toast('コピーできませんでした');
    }
  }

  function defaultFileName(format) {
    const ext = EXT[format];
    if (state.fileName) {
      return `${state.fileName.replace(/\.[^.]+$/, '')}_edit.${ext}`;
    }
    const d = new Date();
    const p = (n) => String(n).padStart(2, '0');
    return `image_${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}_${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}.${ext}`;
  }

  async function saveImage() {
    if (!state.base) {
      return;
    }
    const format = settings.saveFormat;
    const name = defaultFileName(format);
    let blob;
    try {
      blob = await exportBlob(format);
    } catch (err) {
      toast('保存できませんでした');
      return;
    }
    if (window.showSaveFilePicker) {
      try {
        const handle = await window.showSaveFilePicker({
          suggestedName: name,
          types: [{ description: format.toUpperCase(), accept: { [MIME[format]]: [`.${EXT[format]}`] } }],
        });
        const w = await handle.createWritable();
        await w.write(blob);
        await w.close();
        state.dirty = false;
        toast(`保存しました: ${handle.name}`);
        return;
      } catch (err) {
        // キャンセルは何もしない
        if (err && err.name === 'AbortError') {
          return;
        }
      }
    }
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = name;
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 1000);
    state.dirty = false;
    toast(`ダウンロードフォルダへ保存しました: ${name}`);
  }

  window.addEventListener('beforeunload', (e) => {
    if (state.dirty) {
      e.preventDefault();
      e.returnValue = '';
    }
  });

  // ---------------------------------------------------------------- プロパティバー

  function propTarget() {
    return state.selected ? state.selected.type : state.tool;
  }

  function propValue(key) {
    const o = state.selected;
    if (o) {
      if (key === 'color' && o.type === 'highlight') {
        return o.color;
      }
      const attr = ATTR[key];
      if (o[attr] !== undefined) {
        return o[attr];
      }
    }
    if (key === 'color' && propTarget() === 'highlight') {
      return settings.hlColor;
    }
    return settings[key];
  }

  // 「次に描く既定値」と「選択中オブジェクト(その属性を持つ場合)」の両方へ反映する
  function setProp(key, value, live) {
    const target = propTarget();
    const settingKey = key === 'color' && target === 'highlight' ? 'hlColor' : key;
    settings[settingKey] = value;
    saveSettings();
    const o = state.selected;
    let changed = false;
    if (o && OBJ_ATTRS[o.type].includes(ATTR[settingKey]) && o[ATTR[settingKey]] !== value) {
      o[ATTR[settingKey]] = value;
      changed = true;
    }
    if (o && !live && (changed || o.__pendingLive)) {
      delete o.__pendingLive;
      commit();
    } else if (o && live && changed) {
      o.__pendingLive = true;
    }
    refreshUI();
  }

  function buildSwatches() {
    const box = document.querySelector('.swatches');
    box.innerHTML = '';
    for (const c of SWATCHES) {
      const b = document.createElement('button');
      b.className = 'swatch';
      b.style.background = c;
      b.dataset.color = c;
      b.title = c;
      box.appendChild(b);
    }
    box.addEventListener('click', (e) => {
      const b = e.target.closest('.swatch');
      if (b) {
        setProp('color', b.dataset.color, false);
      }
    });
  }

  function bindRange(id, key) {
    const el = $(id);
    el.addEventListener('input', () => setProp(key, Number(el.value), true));
    el.addEventListener('change', () => setProp(key, Number(el.value), false));
  }

  function bindSelect(id, key) {
    $(id).addEventListener('change', (e) => setProp(key, e.target.value, false));
  }

  function hintText() {
    const t = propTarget();
    if (state.selected) {
      return '選択中: Delete で削除 / ハンドルで変形 / Esc で選択解除';
    }
    switch (t) {
      case 'select':
        return 'クリックで選択 / ドラッグで移動 / ダブルクリックでテキスト再編集';
      case 'arrow':
      case 'line':
        return 'ドラッグで描く (Shift で 45° 単位)';
      case 'rect':
      case 'ellipse':
        return 'ドラッグで描く (Shift で正方形 / 正円)';
      case 'pen':
        return 'ドラッグで自由に描く';
      case 'text':
        return 'クリックで入力 / Enter で確定・Shift+Enter で改行・Esc で取消';
      case 'step':
        return `クリックで番号を配置 (次は ${nextStepNumber()})`;
      case 'highlight':
        return 'ドラッグした範囲を蛍光ペンで塗る';
      case 'blur':
        return 'ドラッグした範囲をぼかす (機密を隠すならモザイク推奨)';
      case 'crop':
        return state.crop ? 'Enter で実行 / Esc で解除' : 'ドラッグで残す範囲を選ぶ';
    }
    return '';
  }

  function updateCropSize() {
    const r = state.crop ? rectOf(state.crop) : null;
    $('cropSize').textContent = r ? `${Math.round(r.w)} × ${Math.round(r.h)} px` : '';
    $('btnCropRun').disabled = !r;
  }

  function refreshProps() {
    const target = propTarget();
    const groups = GROUPS[target] || [];
    document.querySelectorAll('#props .group').forEach((g) => {
      if (!g.classList.contains('always')) {
        g.hidden = !groups.includes(g.dataset.group);
      }
    });
    const color = propValue('color');
    document.querySelectorAll('.swatch').forEach((b) => b.classList.toggle('on', b.dataset.color.toLowerCase() === String(color).toLowerCase()));
    $('colorPicker').value = color;
    for (const [id, key] of [['width', 'width'], ['fontSize', 'fontSize'], ['blurLevel', 'blurLevel']]) {
      $(id).value = propValue(key);
      $(`${id}Val`).textContent = propValue(key);
    }
    $('outline').value = propValue('outline');
    $('textStyle').value = propValue('textStyle');
    $('shadow').checked = !!propValue('shadow');
    $('blurMode').value = propValue('blurMode');
    $('pasteMode').value = settings.pasteMode;
    $('saveFormat').value = settings.saveFormat;
    $('hint').textContent = hintText();
    updateCropSize();
  }

  function refreshUI() {
    const has = !!state.base;
    document.querySelectorAll('.needs-image').forEach((el) => {
      el.disabled = !has;
    });
    $('btnUndo').disabled = state.hIndex <= 0;
    $('btnRedo').disabled = state.hIndex >= state.history.length - 1;
    $('sizeLabel').textContent = has ? `${state.base.width} × ${state.base.height} px` : '';
    document.querySelectorAll('.tool').forEach((b) => b.classList.toggle('active', b.dataset.tool === state.tool));
    refreshProps();
    requestRender();
  }

  // ---------------------------------------------------------------- キーボード

  function hasPageSelection() {
    const sel = window.getSelection();
    if (sel && String(sel).length > 0) {
      return true;
    }
    const el = document.activeElement;
    return !!(el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA') && el.selectionStart !== el.selectionEnd);
  }

  window.addEventListener('keydown', (e) => {
    // テキスト編集中はグローバルショートカット全体を無効(入力欄が stopPropagation する)
    if (state.editing) {
      return;
    }
    const ctrl = e.ctrlKey || e.metaKey;
    const key = e.key.toLowerCase();
    if (ctrl) {
      // Ctrl 系は常に有効
      if (key === 'z' && !e.shiftKey) {
        e.preventDefault();
        undo();
      } else if (key === 'y' || (key === 'z' && e.shiftKey)) {
        e.preventDefault();
        redo();
      } else if (key === 'c') {
        // 文字列が選択されていれば通常コピーに譲る
        if (!hasPageSelection() && state.base) {
          e.preventDefault();
          copyImage();
        }
      } else if (key === 's') {
        e.preventDefault();
        saveImage();
      } else if (key === 'o') {
        e.preventDefault();
        openFilePicker();
      } else if (key === 'd') {
        e.preventDefault();
        duplicateSelected();
      } else if (key === '0') {
        e.preventDefault();
        fit();
      } else if (key === '1') {
        e.preventDefault();
        setZoom(1);
      } else if (key === '+' || key === '=' || key === ';') {
        e.preventDefault();
        setZoom(state.zoom * 1.25);
      } else if (key === '-') {
        e.preventDefault();
        setZoom(state.zoom / 1.25);
      }
      return;
    }
    // 単キーはフォーム要素フォーカス中・Alt 併用時は無効
    if (e.altKey || isFormElement(document.activeElement)) {
      return;
    }
    if (TOOL_KEYS[key] && !e.shiftKey) {
      e.preventDefault();
      setTool(TOOL_KEYS[key]);
      return;
    }
    switch (e.key) {
      case 'Enter':
        if (state.crop) {
          e.preventDefault();
          runCrop();
        } else if (state.selected && state.selected.type === 'text') {
          e.preventDefault();
          startTextEdit(state.selected, false);
        }
        break;
      case 'Delete':
      case 'Backspace':
        if (state.selected) {
          e.preventDefault();
          deleteSelected();
        }
        break;
      case 'ArrowLeft':
      case 'ArrowRight':
      case 'ArrowUp':
      case 'ArrowDown':
        if (state.selected) {
          e.preventDefault();
          const step = e.shiftKey ? 10 : 1;
          nudge(e.key === 'ArrowLeft' ? -step : e.key === 'ArrowRight' ? step : 0, e.key === 'ArrowUp' ? -step : e.key === 'ArrowDown' ? step : 0);
        }
        break;
      case 'Escape':
        // トリミング範囲 → 選択 → 選択ツールへ(上から1つだけ)
        if (state.crop) {
          cancelCrop();
        } else if (state.selected) {
          state.selected = null;
          refreshUI();
        } else if (state.tool !== 'select') {
          setTool('select');
        }
        break;
    }
  });

  // ---------------------------------------------------------------- 結線

  canvas.addEventListener('pointerdown', onPointerDown);
  // キャンバスを押した既定動作でフォーカスが body へ移ると、開いたばかりのテキスト入力欄が blur で確定(空なら破棄)されてしまう
  canvas.addEventListener('mousedown', (e) => e.preventDefault());
  canvas.addEventListener('pointermove', onPointerMove);
  canvas.addEventListener('lostpointercapture', endDrag);
  window.addEventListener('pointerup', endDrag);
  window.addEventListener('pointercancel', endDrag);
  canvas.addEventListener('dblclick', (e) => {
    if (state.tool !== 'select' || !state.base) {
      return;
    }
    const hit = objectAt(toImg(e));
    if (hit && hit.type === 'text') {
      startTextEdit(hit, false);
    }
  });

  // Ctrl+ホイールはブラウザズームを止めて、カーソル位置を固定して ×1.1 / ÷1.1
  stage.addEventListener('wheel', (e) => {
    if (!e.ctrlKey || !state.base) {
      return;
    }
    e.preventDefault();
    setZoom(e.deltaY < 0 ? state.zoom * 1.1 : state.zoom / 1.1, { x: e.clientX, y: e.clientY });
  }, { passive: false });

  window.addEventListener('resize', () => {
    if (state.fitMode) {
      fit();
    }
  });

  document.querySelectorAll('.tool').forEach((b) => b.addEventListener('click', () => setTool(b.dataset.tool)));
  $('btnOpen').addEventListener('click', openFilePicker);
  $('btnOpen2').addEventListener('click', openFilePicker);
  $('btnPaste').addEventListener('click', pasteFromButton);
  $('btnUndo').addEventListener('click', undo);
  $('btnRedo').addEventListener('click', redo);
  $('btnZoomIn').addEventListener('click', () => setZoom(state.zoom * 1.25));
  $('btnZoomOut').addEventListener('click', () => setZoom(state.zoom / 1.25));
  $('btnFit').addEventListener('click', fit);
  $('btnActual').addEventListener('click', () => setZoom(1));
  $('btnCopy').addEventListener('click', copyImage);
  $('btnSave').addEventListener('click', saveImage);
  $('btnCropRun').addEventListener('click', runCrop);
  $('btnCropCancel').addEventListener('click', cancelCrop);
  $('saveFormat').addEventListener('change', (e) => {
    settings.saveFormat = e.target.value;
    saveSettings();
  });
  $('pasteMode').addEventListener('change', (e) => {
    settings.pasteMode = e.target.value;
    saveSettings();
  });
  $('colorPicker').addEventListener('input', (e) => setProp('color', e.target.value, true));
  $('colorPicker').addEventListener('change', (e) => setProp('color', e.target.value, false));
  bindRange('width', 'width');
  bindRange('fontSize', 'fontSize');
  bindRange('blurLevel', 'blurLevel');
  bindSelect('outline', 'outline');
  bindSelect('textStyle', 'textStyle');
  bindSelect('blurMode', 'blurMode');
  $('shadow').addEventListener('change', (e) => setProp('shadow', e.target.checked, false));

  // ボタンは mouseup で blur し、クリック後もショートカットが効くようにする
  document.addEventListener('mouseup', (e) => {
    const b = e.target.closest('button');
    if (b) {
      b.blur();
    }
  });

  buildSwatches();
  refreshUI();

  // 確認用(DevTools から状態を見るため)
  window.__blurPaint = { state, settings, exportBlob };
})();
