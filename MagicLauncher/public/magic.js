/*
 * Magic — 魔法陣の描画部品（stable ポータルの portal/magic.js と同じもの）。
 * MagicLauncher での違いは sigil() の lite オプション（目盛りを省いて軽くする）と、stars() の色指定だけ。
 *
 * 読み込み順:
 *   three.min.js (r128) → OrbitControls.js → CopyShader / LuminosityHighPassShader /
 *   EffectComposer / RenderPass / ShaderPass / UnrealBloomPass（すべて r128 の examples/js）→ magic.js
 * ES Module にしていないのは、ビルド工程なしでそのまま読めるようにするため。
 *
 * 魔法陣の図柄は、同心円・弧・目盛り・幾何学の記号を乱数で組み合わせたオリジナル。
 * 既存作品の図案や文字はそのまま使わない（ツール名から決まる種で毎回同じ形になる）。
 */
(function () {
  'use strict';
  var T = window.THREE;

  // ---------------------------------------------------------------- 小物
  function hash(str) {
    var h = 2166136261;
    for (var i = 0; i < str.length; i++) { h ^= str.charCodeAt(i); h = Math.imul(h, 16777619); }
    return h >>> 0;
  }
  function rng(seed) {
    var a = typeof seed === 'string' ? hash(seed) : seed >>> 0;
    return function () {
      a |= 0; a = (a + 0x6D2B79F5) | 0;
      var t = Math.imul(a ^ (a >>> 15), 1 | a);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }
  function clamp01(x) { return x < 0 ? 0 : x > 1 ? 1 : x; }
  function ease(x) { x = clamp01(x); return x < 0.5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2; }
  function damp(cur, target, k, dt) { return cur + (target - cur) * (1 - Math.exp(-k * dt)); }
  function color(c) { return c instanceof T.Color ? c.clone() : new T.Color(c); }

  // matPool があるあいだ(sigil の組み立て中)は、同じ色・同じ濃さの材質を 1 つにまとめる
  var matPool = null;
  function basic(col, opacity) {
    var c = color(col), op = opacity == null ? 1 : opacity, key = c.getHexString() + ':' + op;
    if (matPool && matPool[key]) return matPool[key];
    var m = new T.MeshBasicMaterial({
      color: c, transparent: true, opacity: op,
      depthWrite: false, side: T.DoubleSide, blending: T.AdditiveBlending,
    });
    if (matPool) matPool[key] = m;
    return m;
  }

  // ---------------------------------------------------------------- テクスチャ
  var glowTex = null;
  function glowTexture() {
    if (glowTex) return glowTex;
    var c = document.createElement('canvas'); c.width = c.height = 128;
    var ctx = c.getContext('2d');
    var g = ctx.createRadialGradient(64, 64, 0, 64, 64, 64);
    g.addColorStop(0, 'rgba(255,255,255,1)');
    g.addColorStop(0.25, 'rgba(255,255,255,0.55)');
    g.addColorStop(0.6, 'rgba(255,255,255,0.12)');
    g.addColorStop(1, 'rgba(255,255,255,0)');
    ctx.fillStyle = g; ctx.fillRect(0, 0, 128, 128);
    glowTex = new T.CanvasTexture(c);
    return glowTex;
  }

  // 円周に沿って幾何学記号を並べた輪（キャンバスに極座標で描いて板に貼る）
  function glyphRingTexture(seed, opts) {
    opts = opts || {};
    var R = rng(seed);
    var size = 1024, c = document.createElement('canvas');
    c.width = c.height = size;
    var ctx = c.getContext('2d');
    var cx = size / 2, rOut = size * 0.49, rIn = size * (opts.inner || 0.42);
    ctx.strokeStyle = '#fff'; ctx.fillStyle = '#fff';
    ctx.lineWidth = 3;
    ctx.beginPath(); ctx.arc(cx, cx, rOut, 0, Math.PI * 2); ctx.stroke();
    ctx.beginPath(); ctx.arc(cx, cx, rIn, 0, Math.PI * 2); ctx.stroke();
    var n = opts.count || 48;
    var mid = (rOut + rIn) / 2, h = (rOut - rIn) * 0.62;
    for (var i = 0; i < n; i++) {
      var a = (i / n) * Math.PI * 2;
      ctx.save();
      ctx.translate(cx + Math.cos(a) * mid, cx + Math.sin(a) * mid);
      ctx.rotate(a + Math.PI / 2);
      ctx.lineWidth = 2.6;
      drawGlyph(ctx, R, h);
      ctx.restore();
    }
    var tex = new T.CanvasTexture(c);
    tex.anisotropy = 4;
    // GPU へ送った後は 1024 四方のキャンバス(約 4MB)を持っておく必要がないので縮めて手放す（描き直すことはない）
    tex.onUpdate = function () { c.width = c.height = 1; tex.onUpdate = null; };
    return tex;
  }
  // 記号1文字分：縦棒・横棒・小円・三角・点の組み合わせ（文字体系ではない、ただの図形）
  function drawGlyph(ctx, R, h) {
    var w = h * 0.55, kind = Math.floor(R() * 6);
    ctx.beginPath();
    if (kind === 0) { ctx.moveTo(0, -h / 2); ctx.lineTo(0, h / 2); ctx.moveTo(-w / 2, -h / 6); ctx.lineTo(w / 2, -h / 6); }
    else if (kind === 1) { ctx.arc(0, -h / 6, w / 3, 0, Math.PI * 2); ctx.moveTo(0, h / 6); ctx.lineTo(0, h / 2); }
    else if (kind === 2) { ctx.moveTo(-w / 2, h / 2); ctx.lineTo(0, -h / 2); ctx.lineTo(w / 2, h / 2); ctx.closePath(); ctx.moveTo(w / 6, h / 5); ctx.arc(0, h / 5, w / 6, 0, Math.PI * 2); }
    else if (kind === 3) { ctx.moveTo(-w / 3, -h / 2); ctx.lineTo(-w / 3, h / 2); ctx.moveTo(w / 3, -h / 2); ctx.lineTo(w / 3, h / 2); ctx.moveTo(-w / 3, 0); ctx.lineTo(w / 3, -h / 4); }
    else if (kind === 4) { ctx.rect(-w / 3, -h / 3, w * 2 / 3, h * 2 / 3); ctx.moveTo(0, -h / 2); ctx.lineTo(0, -h / 3); }
    else { ctx.moveTo(-w / 2, 0); ctx.lineTo(w / 2, 0); ctx.moveTo(0, -h / 2); ctx.arc(0, -h / 2 + w / 4, w / 4, -Math.PI / 2, Math.PI * 1.5); }
    ctx.stroke();
    if (R() < 0.45) { ctx.beginPath(); ctx.arc((R() - 0.5) * w, h / 2 + 6, 3.5, 0, Math.PI * 2); ctx.fill(); }
  }

  // ---------------------------------------------------------------- 部品
  // 形(ジオメトリ)は大きさ 1 のものを使い回し、メッシュの scale で大きさを合わせる（同じ形を何百個も持たない）。
  // 使い回しの形は userData.shared = true。片付けで dispose しないこと（isShared で見分ける）
  var geoCache = {};
  function sharedGeo(key, make) {
    var g = geoCache[key];
    if (!g) { g = geoCache[key] = make(); g.userData.shared = true; }
    return g;
  }
  function isShared(o) { return !!(o && o.userData && o.userData.shared); }
  function unitCircle(seg) { return sharedGeo('circle' + (seg || 48), function () { return new T.CircleGeometry(1, seg || 48); }); }
  var unitPlane = function () { return sharedGeo('plane', function () { return new T.PlaneGeometry(1, 1); }); };
  // 輪は外径で割った形(内径の比・弧の長さ・分割数)で使い回す。弧の始まりはメッシュの回転で付ける
  function ring(r, width, col, opacity, start, len, seg) {
    var out = r + width / 2, inn = Math.max(0, (r - width / 2) / out);
    var L = len == null ? Math.PI * 2 : len, n = seg || 96;
    var key = 'ring' + inn.toFixed(4) + ':' + L.toFixed(4) + ':' + n;
    var m = new T.Mesh(sharedGeo(key, function () { return new T.RingGeometry(inn, 1, n, 1, 0, L); }), basic(col, opacity));
    m.scale.set(out, out, 1);
    m.rotation.z = start || 0;
    return m;
  }
  function disc(r, col, opacity) {
    var m = new T.Mesh(unitCircle(48), basic(col, opacity));
    m.scale.set(r, r, 1);
    return m;
  }
  function bar(x1, y1, x2, y2, width, col, opacity) {
    var dx = x2 - x1, dy = y2 - y1, len = Math.sqrt(dx * dx + dy * dy);
    var m = new T.Mesh(unitPlane(), basic(col, opacity));
    m.scale.set(len, width, 1);
    m.position.set((x1 + x2) / 2, (y1 + y2) / 2, 0);
    m.rotation.z = Math.atan2(dy, dx);
    return m;
  }

  function glow(col, size, opacity) {
    var s = new T.Sprite(new T.SpriteMaterial({
      map: glowTexture(), color: color(col), transparent: true, opacity: opacity == null ? 0.6 : opacity,
      depthWrite: false, blending: T.AdditiveBlending,
    }));
    s.scale.set(size, size, 1);
    return s;
  }

  /*
   * sigil(seed, { radius, color, detail, lite }) — 同心円の魔法陣1つ（XY 平面、半径 radius）。
   * lite: 目盛りの輪を省く（件数が多いとき用。乱数の消費は同じなので図柄の残りは変わらない）。
   * 戻り値の group.userData:
   *   spin: [{ obj, speed }]  … tick() で回す
   *   mats: [Material]        … setOpacity() でまとめて薄くする
   */
  function sigil(seed, o) {
    o = o || {};
    var R = rng(seed), r = o.radius || 1, col = color(o.color || '#ff5fd2');
    var g = new T.Group(), spin = [], mats = [];
    // 材質は同じ色・濃さどうしで共有(setOpacity は魔法陣 1 つ分まとめて変えるので共有しても同じ見た目)
    function add(m, parent) {
      (parent || g).add(m);
      if (mats.indexOf(m.material) < 0) { mats.push(m.material); m.material.userData.base = m.material.opacity; }
      return m;
    }
    matPool = {};
    var lw = r * 0.022;

    // 外側の二重円
    add(ring(r, lw * 1.2, col, 0.95));
    add(ring(r * 0.9, lw * 0.7, col, 0.7));

    // 回る弧（途中で切れている円）
    var arcG = new T.Group(); g.add(arcG); spin.push({ obj: arcG, speed: (R() < 0.5 ? -1 : 1) * (0.15 + R() * 0.35) });
    var nArc = 2 + Math.floor(R() * 3), base = R() * Math.PI * 2;
    for (var i = 0; i < nArc; i++) {
      var len = (Math.PI * 2 / nArc) * (0.55 + R() * 0.3);
      add(ring(r * 0.74, lw * 1.6, col, 0.9, base + i * Math.PI * 2 / nArc, len, 48), arcG);
    }
    // 弧と弧の切れ目に渡す短い棒（目印）
    var nBar = 1 + Math.floor(R() * 3);
    for (i = 0; i < nBar; i++) {
      var a = R() * Math.PI * 2;
      add(bar(Math.cos(a) * r * 0.74, Math.sin(a) * r * 0.74, Math.cos(a) * r * 0.9, Math.sin(a) * r * 0.9, lw * 1.1, col, 0.85), arcG);
    }

    // 目盛りの輪（ある魔法陣とない魔法陣がある）
    if (o.detail || R() < 0.55) {
      var tickSpeed = (R() < 0.5 ? -1 : 1) * (0.05 + R() * 0.1);
      var nt = 24 + Math.floor(R() * 3) * 12;
      var tickG = new T.Group();
      if (!o.lite) { g.add(tickG); spin.push({ obj: tickG, speed: tickSpeed }); } else nt = 0;
      for (i = 0; i < nt; i++) {
        var ta = (i / nt) * Math.PI * 2, long = i % 6 === 0;
        var r1 = r * 0.82, r2 = r * (long ? 0.88 : 0.85);
        add(bar(Math.cos(ta) * r1, Math.sin(ta) * r1, Math.cos(ta) * r2, Math.sin(ta) * r2, lw * 0.5, col, long ? 0.8 : 0.45), tickG);
      }
    }

    // 内側の円
    add(ring(r * 0.55, lw * 1.0, col, 0.85));
    if (R() < 0.6) add(ring(r * 0.46, lw * 0.6, col, 0.55));
    // 内側の回る記号（三角 or 四角 or 小円の連なり）
    var innerG = new T.Group(); g.add(innerG); spin.push({ obj: innerG, speed: (R() < 0.5 ? -1 : 1) * (0.2 + R() * 0.4) });
    var kind = Math.floor(R() * 3), k;
    if (kind === 0 || o.detail) {
      var sides = 3 + Math.floor(R() * 2) * 3; // 3 or 6
      for (k = 0; k < sides; k++) {
        var a1 = (k / sides) * Math.PI * 2, a2 = ((k + 1) / sides) * Math.PI * 2, rr = r * 0.46;
        add(bar(Math.cos(a1) * rr, Math.sin(a1) * rr, Math.cos(a2) * rr, Math.sin(a2) * rr, lw * 0.6, col, 0.6), innerG);
      }
    }
    if (kind === 1) {
      for (k = 0; k < 4; k++) {
        var b1 = (k / 4) * Math.PI * 2 + Math.PI / 4, b2 = ((k + 1) / 4) * Math.PI * 2 + Math.PI / 4, rq = r * 0.44;
        add(bar(Math.cos(b1) * rq, Math.sin(b1) * rq, Math.cos(b2) * rq, Math.sin(b2) * rq, lw * 0.6, col, 0.6), innerG);
      }
    }
    if (kind === 2) {
      var nb = 6 + Math.floor(R() * 3) * 2;
      for (k = 0; k < nb; k++) {
        var ba = (k / nb) * Math.PI * 2, bead = disc(r * 0.035, col, 0.85);
        bead.position.set(Math.cos(ba) * r * 0.65, Math.sin(ba) * r * 0.65, 0);
        add(bead, innerG);
      }
    }
    // 中心
    add(ring(r * 0.27, lw * 1.3, col, 0.95));
    add(disc(r * 0.13, col.clone().lerp(new T.Color('#ffffff'), 0.55), 0.95));

    matPool = null;
    g.userData.spin = spin;
    g.userData.mats = mats;
    return g;
  }

  // 結晶（中核）: 外側ワイヤーの二十面体＋内側の面
  function crystal(col, size) {
    col = color(col);
    var g = new T.Group();
    var inner = new T.Mesh(new T.IcosahedronGeometry(size * 0.62, 0), new T.MeshBasicMaterial({
      color: col.clone().lerp(new T.Color('#ffffff'), 0.25), transparent: true, opacity: 0.55, depthWrite: false, blending: T.AdditiveBlending,
    }));
    var edges = new T.LineSegments(new T.EdgesGeometry(new T.IcosahedronGeometry(size * 0.62, 0)), new T.LineBasicMaterial({ color: '#ffffff', transparent: true, opacity: 0.8 }));
    var cage = new T.LineSegments(new T.EdgesGeometry(new T.IcosahedronGeometry(size, 1)), new T.LineBasicMaterial({ color: col, transparent: true, opacity: 0.55 }));
    g.add(inner, edges, cage, glow(col, size * 4, 0.5));
    g.userData.spinParts = [inner, edges, cage];
    g.userData.mats = [inner.material, edges.material, cage.material];
    g.userData.mats.forEach(function (m) { m.userData.base = m.opacity; });
    return g;
  }

  function setOpacity(obj, f) {
    (obj.userData.mats || []).forEach(function (m) { m.opacity = (m.userData.base == null ? 1 : m.userData.base) * f; });
  }
  function tickSpin(obj, dt) {
    (obj.userData.spin || []).forEach(function (s) { s.obj.rotation.z += s.speed * dt; });
    (obj.userData.spinParts || []).forEach(function (p, i) { p.rotation.y += dt * (0.25 + i * 0.12); p.rotation.x += dt * 0.07 * (i + 1); });
  }

  // 背景の星
  function stars(n, spread, seed, col) {
    var R = rng(seed || 'stars'), pos = new Float32Array(n * 3);
    for (var i = 0; i < n; i++) {
      pos[i * 3] = (R() - 0.5) * spread; pos[i * 3 + 1] = (R() - 0.5) * spread; pos[i * 3 + 2] = (R() - 0.5) * spread;
    }
    var geo = new T.BufferGeometry(); geo.setAttribute('position', new T.BufferAttribute(pos, 3));
    return new T.Points(geo, new T.PointsMaterial({ color: col || '#c9b8ff', size: 0.06, transparent: true, opacity: 0.55, depthWrite: false }));
  }

  // 背景の煙（ゆっくり動くもや）
  function smoke(w, h, tint) {
    var mat = new T.ShaderMaterial({
      transparent: true, depthWrite: false,
      uniforms: { uTime: { value: 0 }, uTint: { value: color(tint || '#3a1450') } },
      vertexShader: 'varying vec2 vUv; void main(){ vUv=uv; gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.0); }',
      fragmentShader: [
        'varying vec2 vUv; uniform float uTime; uniform vec3 uTint;',
        'float h(vec2 p){ return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453); }',
        'float n(vec2 p){ vec2 i=floor(p), f=fract(p); f=f*f*(3.0-2.0*f);',
        '  return mix(mix(h(i),h(i+vec2(1,0)),f.x), mix(h(i+vec2(0,1)),h(i+vec2(1,1)),f.x), f.y); }',
        'float fbm(vec2 p){ float v=0.0, a=0.5; for(int i=0;i<5;i++){ v+=a*n(p); p*=2.03; a*=0.5; } return v; }',
        'void main(){',
        '  vec2 p=vUv*3.0; float t=uTime*0.03;',
        '  float f=fbm(p+vec2(t,-t*0.7)+fbm(p*1.7-vec2(t*0.5,t)));',
        '  float d=distance(vUv,vec2(0.5));',
        '  float a=smoothstep(0.4,0.95,f)*(1.0-smoothstep(0.15,0.7,d))*0.42;',
        '  gl_FragColor=vec4(uTint*1.6, a);',
        '}',
      ].join('\n'),
    });
    var m = new T.Mesh(new T.PlaneGeometry(w, h), mat);
    m.userData.uniforms = mat.uniforms;
    return m;
  }

  // ---------------------------------------------------------------- 線と光の粒
  // 2点を、斜め45°→まっすぐ の2本の線分でつなぐ（回路っぽい折れ線）。a, b は [x, y]
  function octi(a, b, straightFirst) {
    var dx = b[0] - a[0], dy = b[1] - a[1], ax = Math.abs(dx), ay = Math.abs(dy);
    var d = Math.min(ax, ay), sx = Math.sign(dx), sy = Math.sign(dy), m;
    if (straightFirst) m = ax > ay ? [b[0] - sx * d, a[1]] : [a[0], b[1] - sy * d];
    else m = [a[0] + sx * d, a[1] + sy * d];
    return [a, m, b];
  }

  // 折れ線を幅のある帯（メッシュ）にする。pts は Vector3 の配列、normal は帯を寝かせる面の法線
  function ribbon(pts, width, col, opacity, normal) {
    normal = normal || new T.Vector3(0, 0, 1);
    var pos = [], idx = [], v = 0;
    for (var i = 0; i < pts.length - 1; i++) {
      var p0 = pts[i], p1 = pts[i + 1];
      var d = p1.clone().sub(p0); if (d.lengthSq() < 1e-8) continue;
      d.normalize();
      var s = new T.Vector3().crossVectors(normal, d).normalize().multiplyScalar(width / 2);
      var e0 = p0.clone().addScaledVector(d, -width / 2), e1 = p1.clone().addScaledVector(d, width / 2);
      [e0.clone().add(s), e0.clone().sub(s), e1.clone().add(s), e1.clone().sub(s)].forEach(function (q) { pos.push(q.x, q.y, q.z); });
      idx.push(v, v + 1, v + 2, v + 1, v + 3, v + 2); v += 4;
    }
    var geo = new T.BufferGeometry();
    geo.setAttribute('position', new T.Float32BufferAttribute(pos, 3));
    geo.setIndex(idx);
    var mat = new T.MeshBasicMaterial({ color: color(col), transparent: true, opacity: opacity == null ? 0.7 : opacity, depthWrite: false, side: T.DoubleSide });
    mat.userData.base = mat.opacity;
    var m = new T.Mesh(geo, mat);
    m.userData.mats = [mat];
    return m;
  }

  // 折れ線の長さと、長さ比 u (0..1) の位置
  function Path(pts) {
    this.pts = pts; this.lens = []; var L = 0;
    for (var i = 0; i < pts.length - 1; i++) { var l = pts[i].distanceTo(pts[i + 1]); this.lens.push(l); L += l; }
    this.length = L;
  }
  Path.prototype.at = function (u, out) {
    out = out || new T.Vector3();
    var d = clamp01(u) * this.length;
    for (var i = 0; i < this.lens.length; i++) {
      if (d <= this.lens[i] || i === this.lens.length - 1) return out.copy(this.pts[i]).lerp(this.pts[i + 1], this.lens[i] ? d / this.lens[i] : 0);
      d -= this.lens[i];
    }
    return out.copy(this.pts[this.pts.length - 1]);
  };

  // 光の粒の流れ。add(path, color, { count, speed, size }) で流れを足し、update(dt) で動かす
  function Flow(scene) { this.scene = scene; this.streams = []; }
  Flow.prototype.add = function (path, col, o) {
    o = o || {};
    var n = o.count || 3, list = [];
    for (var i = 0; i < n; i++) {
      var s = glow(col, o.size || 0.35, 0.95);
      this.scene.add(s);
      list.push({ s: s, u: i / n });
    }
    var st = { path: path, list: list, speed: (o.speed || 1.6) / Math.max(path.length, 0.01), on: o.on !== false, k: o.on === false ? 0 : 1 };
    this.streams.push(st);
    return st;
  };
  Flow.prototype.update = function (dt) {
    this.streams.forEach(function (st) {
      st.k = damp(st.k, st.on ? 1 : 0, 6, dt);
      st.list.forEach(function (p) {
        p.u = (p.u + st.speed * dt) % 1;
        st.path.at(p.u, p.s.position);
        var fade = Math.sin(p.u * Math.PI); // 端で消える
        p.s.material.opacity = 0.95 * fade * st.k;
        p.s.visible = st.k > 0.01;
      });
    });
  };
  Flow.prototype.clear = function () {
    var sc = this.scene;
    this.streams.forEach(function (st) { st.list.forEach(function (p) { sc.remove(p.s); p.s.material.dispose(); }); });
    this.streams = [];
  };

  // ---------------------------------------------------------------- HTML ラベル
  function Labels(container) {
    this.root = document.createElement('div');
    this.root.className = 'mg-labels';
    container.appendChild(this.root);
    this.list = [];
  }
  Labels.prototype.add = function (el, target, offset) {
    this.root.appendChild(el);
    var item = { el: el, target: target, offset: offset || new T.Vector3(), visible: true };
    this.list.push(item);
    return item;
  };
  var _v = null;
  Labels.prototype.update = function (camera, w, h) {
    _v = _v || new T.Vector3();
    this.list.forEach(function (it) {
      if (it.target.isObject3D) it.target.getWorldPosition(_v); else _v.copy(it.target);
      _v.add(it.offset).project(camera);
      var hide = !it.visible || _v.z > 1 || _v.z < -1;
      it.el.style.display = hide ? 'none' : '';
      if (!hide) {
        var x = (_v.x * 0.5 + 0.5) * w, y = (-_v.y * 0.5 + 0.5) * h;
        it.el.style.transform = 'translate(' + x.toFixed(1) + 'px,' + y.toFixed(1) + 'px) translate(-50%,-50%)';
        it.el.style.zIndex = String(Math.round((1 - _v.z) * 10000));
      }
    });
  };

  // ---------------------------------------------------------------- 描画のひな形
  // stage(container, { fov, bloom }) → { renderer, scene, camera, composer, render(), size() }
  function stage(container, o) {
    o = o || {};
    var renderer;
    // 光のにじみ(bloom)を使うときは、絵は別のバッファに描いてから画面へ写すだけなので、
    // 画面側のなめらか処理(4 倍の大きさのバッファ)と奥行きのバッファは使われない → 作らない（見た目は同じでメモリが減る）
    var useComposer = o.bloom !== false && !!(T.EffectComposer && T.UnrealBloomPass);
    try {
      renderer = new T.WebGLRenderer({ antialias: !useComposer, depth: !useComposer, stencil: false, alpha: false, powerPreference: 'high-performance' });
    } catch (e) { return null; }
    if (!renderer.getContext()) return null;
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.setClearColor(o.clear || '#07040f', 1);
    container.appendChild(renderer.domElement);
    var scene = new T.Scene();
    var camera = new T.PerspectiveCamera(o.fov || 45, 1, 0.1, 500);
    var composer = null, bloom = null;
    if (useComposer) {
      composer = new T.EffectComposer(renderer);
      composer.addPass(new T.RenderPass(scene, camera));
      bloom = new T.UnrealBloomPass(new T.Vector2(256, 256), o.bloomStrength || 1.15, o.bloomRadius || 0.55, o.bloomThreshold == null ? 0.12 : o.bloomThreshold);
      composer.addPass(bloom);
      // 場面を描くバッファ(readBuffer。RenderPass もにじみも入れ替えをしないのでずっと同じ)だけは奥行きが要る
      // (結晶の線が奥行きで後ろの光を隠している)。残り(使われない writeBuffer と、にじみ用の画面全体の板を描くだけのバッファ)の
      // 奥行きは作らない。GPU 側の確保は初めて描くときなので、その前に外せば確保されない（大きさを変えても引き継がれる）
      [composer.writeBuffer, bloom.renderTargetBright]
        .concat(bloom.renderTargetsHorizontal || [], bloom.renderTargetsVertical || [])
        .forEach(function (rt) { if (rt) rt.depthBuffer = false; });
    }
    var st = { renderer: renderer, scene: scene, camera: camera, composer: composer, bloom: bloom, w: 1, h: 1 };
    st.size = function () {
      var w = container.clientWidth || 1, h = container.clientHeight || 1;
      if (w === st.w && h === st.h) return false;
      st.w = w; st.h = h;
      renderer.setSize(w, h, false);
      renderer.domElement.style.width = w + 'px'; renderer.domElement.style.height = h + 'px';
      camera.aspect = w / h; camera.updateProjectionMatrix();
      if (composer) {
        if (composer.setPixelRatio) composer.setPixelRatio(renderer.getPixelRatio());
        composer.setSize(w, h);
      }
      return true;
    };
    st.render = function () { if (composer) composer.render(); else renderer.render(scene, camera); };
    st.size();
    return st;
  }

  // 他のページへ移る前のフェード
  function leaveTo(href) {
    var veil = document.querySelector('.mg-veil');
    if (!veil) { veil = document.createElement('div'); veil.className = 'mg-veil'; document.body.appendChild(veil); }
    requestAnimationFrame(function () { veil.classList.add('on'); });
    setTimeout(function () { location.href = href; }, 420);
  }
  // 戻るボタンで戻ってきたとき、フェードが掛かったままにならないように
  window.addEventListener('pageshow', function () {
    var veil = document.querySelector('.mg-veil'); if (veil) veil.classList.remove('on');
  });

  function esc(s) { return String(s == null ? '' : s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); }

  window.Magic = {
    hash: hash, rng: rng, ease: ease, damp: damp, clamp01: clamp01, esc: esc,
    glowTexture: glowTexture, glyphRingTexture: glyphRingTexture,
    ring: ring, disc: disc, bar: bar, unitCircle: unitCircle, isShared: isShared, glow: glow, sigil: sigil, crystal: crystal,
    setOpacity: setOpacity, tickSpin: tickSpin, stars: stars, smoke: smoke,
    octi: octi, ribbon: ribbon, Path: Path, Flow: Flow, Labels: Labels, stage: stage, leaveTo: leaveTo,
  };
})();
