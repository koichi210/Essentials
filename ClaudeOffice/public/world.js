// 島・空・時刻・本棚・ホワイトボードなど全スキン共通の部品と、既定の教室スキン
import * as THREE from 'three';
import { CSS2DObject } from 'three/addons/renderers/CSS2DRenderer.js';

export const PROJECT_COLORS = ['#e07a5f', '#3d85c6', '#81b29a', '#e9b44c', '#9c6ade', '#e56b9f', '#2bb3a6', '#f4a261', '#6a994e', '#5c7aea'];

// 名前の FNV-1a ハッシュで10色から選ぶ(同じプロジェクトはいつも同じ色)
export function fnv1a(text) {
  let h = 0x811c9dc5;
  for (let i = 0; i < text.length; i++) {
    h ^= text.charCodeAt(i);
    h = Math.imul(h, 0x01000193) >>> 0;
  }
  return h >>> 0;
}

export function projectColor(name) {
  return PROJECT_COLORS[fnv1a(String(name || '')) % PROJECT_COLORS.length];
}

// ---------------------------------------------------------------- 時刻のキーフレーム

const HOURS = [0, 4.6, 5.6, 7, 12, 15.5, 17, 18.2, 19.4, 24];

// 各キーフレーム: 空(上・中・下)・太陽色・太陽の強さ・半球光・ランプ・雲・星
const DEFAULT_KEYS = [
  ['#0b1030', '#1b2350', '#2a3368', '#9fb4ff', 0.35, 0.35, 1.0, '#3a4270', 1.0],
  ['#0e1436', '#20295a', '#35407a', '#9fb4ff', 0.35, 0.35, 1.0, '#3e4677', 0.9],
  ['#2a3a78', '#a46a8c', '#f2a07b', '#ffb37a', 0.6, 0.5, 0.6, '#c99aa8', 0.3],
  ['#6fa8e8', '#a9cdf0', '#f6d9b6', '#ffe2b8', 1.0, 0.75, 0.0, '#f1f1f1', 0.0],
  ['#4f95e0', '#8cc0ef', '#d6ecfa', '#fff6e8', 1.25, 0.9, 0.0, '#ffffff', 0.0],
  ['#5a9be0', '#93c3ee', '#dcecf7', '#fff1dc', 1.15, 0.85, 0.0, '#ffffff', 0.0],
  ['#5b8fd2', '#e3a77e', '#f7c48e', '#ffc58a', 0.95, 0.7, 0.2, '#f7d6bd', 0.0],
  ['#3d4f98', '#d0716b', '#f39a6b', '#ff9a5e', 0.7, 0.55, 0.7, '#d79b98', 0.2],
  ['#18204f', '#3a3f7a', '#6a4f86', '#a8b8ff', 0.4, 0.4, 1.0, '#4b4f80', 0.8],
  ['#0b1030', '#1b2350', '#2a3368', '#9fb4ff', 0.35, 0.35, 1.0, '#3a4270', 1.0],
];

function smooth(t) {
  return t * t * (3 - 2 * t);
}

const tmpA = new THREE.Color();
const tmpB = new THREE.Color();

// 時刻(0〜24)の見た目をキーフレームの間で smoothstep 補間して返す
export function timeLook(hour, keys = DEFAULT_KEYS) {
  const h = ((hour % 24) + 24) % 24;
  let i = 0;
  while (i < HOURS.length - 2 && h >= HOURS[i + 1]) {
    i++;
  }
  const t = smooth((h - HOURS[i]) / (HOURS[i + 1] - HOURS[i]));
  const a = keys[i];
  const b = keys[i + 1];
  const col = (k) => new THREE.Color().copy(tmpA.set(a[k])).lerp(tmpB.set(b[k]), t);
  const num = (k) => a[k] + (b[k] - a[k]) * t;
  return {
    hour: h,
    top: col(0), mid: col(1), bottom: col(2), sunColor: col(3), sunPower: num(4),
    hemi: num(5), lamp: num(6), cloud: col(7), stars: num(8),
    night: h >= 19 || h < 5,
  };
}

export function timeBand(hour) {
  if (hour >= 4.5 && hour < 10) {
    return { key: 'morning', icon: '🌅', name: '朝' };
  }
  if (hour >= 10 && hour < 16) {
    return { key: 'day', icon: '🌞', name: '昼' };
  }
  if (hour >= 16 && hour < 19) {
    return { key: 'evening', icon: '🌇', name: '夕方' };
  }
  return { key: 'night', icon: '🌙', name: '夜' };
}

// ---------------------------------------------------------------- 空(全画面の板に描くシェーダ)

export function createSky() {
  const uniforms = {
    uTop: { value: new THREE.Color() },
    uMid: { value: new THREE.Color() },
    uBottom: { value: new THREE.Color() },
    uSun: { value: new THREE.Vector3(0.5, 0.8, 0) },
    uMoon: { value: new THREE.Vector3(0.5, 0.8, 0) },
    uMoon2: { value: new THREE.Vector3(0, 0, 0) },
    uSunColor: { value: new THREE.Color('#fff2c0') },
    uMoonColor: { value: new THREE.Color('#f4f1e0') },
    uStars: { value: 0 },
    uAurora: { value: 0 },
    uCrack: { value: 0 },
    // 大きな月(酒場=淡い月、魔界=赤い月): x, y, 半径, 見え方
    uBig: { value: new THREE.Vector4(0, 0, 0, 0) },
    uBigColor: { value: new THREE.Color('#ffffff') },
    uTime: { value: 0 },
    uAspect: { value: 1 },
  };
  const mat = new THREE.ShaderMaterial({
    uniforms,
    depthWrite: false,
    depthTest: false,
    vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = vec4(position.xy, 1.0, 1.0); }',
    fragmentShader: `
      varying vec2 vUv;
      uniform vec3 uTop, uMid, uBottom, uSun, uMoon, uMoon2, uSunColor, uMoonColor, uBigColor;
      uniform vec4 uBig;
      uniform float uStars, uAurora, uCrack, uTime, uAspect;
      float hash(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
      float disc(vec2 uv, vec3 c, float r){ vec2 d = uv - c.xy; d.x *= uAspect; return smoothstep(r, r * 0.92, length(d)) * c.z; }
      float glow(vec2 uv, vec3 c, float r){ vec2 d = uv - c.xy; d.x *= uAspect; return exp(-length(d) / r) * c.z; }
      void main(){
        float y = vUv.y;
        vec3 col = mix(uBottom, uMid, smoothstep(0.0, 0.5, y));
        col = mix(col, uTop, smoothstep(0.5, 1.0, y));
        vec2 g = vUv * vec2(uAspect, 1.0) * 140.0;
        vec2 cell = floor(g);
        float s = hash(cell);
        float tw = 0.6 + 0.4 * sin(uTime * 2.0 + s * 40.0);
        float star = step(0.985, s) * smoothstep(0.35, 0.0, length(fract(g) - 0.5)) * tw;
        col += vec3(star) * uStars * smoothstep(0.25, 0.7, y);
        if (uAurora > 0.0) {
          float band = sin(vUv.x * 9.0 + uTime * 0.3 + sin(vUv.x * 3.0 + uTime * 0.2) * 2.0) * 0.05 + 0.72;
          float a = exp(-pow((y - band) * 14.0, 2.0)) * (0.6 + 0.4 * sin(vUv.x * 30.0 + uTime));
          col += mix(vec3(0.2, 1.0, 0.6), vec3(0.7, 0.3, 1.0), vUv.x) * a * uAurora;
        }
        if (uCrack > 0.0) {
          float cx = 0.62 + sin(y * 20.0) * 0.012 + sin(y * 53.0) * 0.006;
          float c = exp(-abs(vUv.x - cx) * 260.0) * smoothstep(0.45, 0.75, y) * smoothstep(1.0, 0.8, y);
          col += vec3(1.0, 0.25, 0.8) * c * uCrack * (0.7 + 0.3 * sin(uTime * 3.0));
        }
        if (uBig.w > 0.0) {
          vec2 d = vUv - uBig.xy;
          d.x *= uAspect;
          float r = length(d) / uBig.z;
          vec2 q = d / uBig.z * 3.0;
          float mott = 0.82 + 0.1 * sin(q.x * 2.3 + sin(q.y * 1.7) * 2.0) + 0.08 * sin(q.y * 3.1 - q.x * 1.3);
          col = mix(col, uBigColor * mott, smoothstep(1.0, 0.97, r) * uBig.w);
          col += uBigColor * exp(-max(r - 1.0, 0.0) * 3.0) * 0.18 * uBig.w * step(1.0, r);
        }
        vec3 sunC = mix(uSunColor, vec3(1.0, 0.82, 0.35), 0.5);
        col = mix(col, sunC * 1.2, disc(vUv, uSun, 0.045));
        col += sunC * glow(vUv, uSun, 0.09) * 0.6;
        col = mix(col, uMoonColor, disc(vUv, uMoon, 0.05));
        col += uMoonColor * glow(vUv, uMoon, 0.07) * 0.25;
        col = mix(col, uMoonColor * vec3(0.9, 0.85, 1.0), disc(vUv, uMoon2, 0.025));
        gl_FragColor = vec4(col, 1.0);
        #include <colorspace_fragment>
      }`,
  });
  const mesh = new THREE.Mesh(new THREE.PlaneGeometry(2, 2), mat);
  mesh.frustumCulled = false;
  mesh.renderOrder = -1000;
  return { mesh, uniforms };
}

// 太陽は6〜18時、月は18〜6時に弧を描く。戻り値は画面の uv と見えている度合い
export function arcPositions(hour) {
  const arc = (t) => ({ x: 0.12 + 0.76 * t, y: 0.5 + 0.4 * Math.sin(Math.PI * t) });
  const sunT = (hour - 6) / 12;
  const moonH = hour >= 18 ? hour - 18 : hour + 6;
  const moonT = moonH / 12;
  const sunOn = sunT > 0 && sunT < 1 ? Math.min(1, Math.sin(Math.PI * sunT) * 3) : 0;
  const moonOn = moonT > 0 && moonT < 1 ? Math.min(1, Math.sin(Math.PI * moonT) * 3) : 0;
  return { sun: { ...arc(Math.min(1, Math.max(0, sunT))), on: sunOn }, moon: { ...arc(Math.min(1, Math.max(0, moonT))), on: moonOn }, sunT, moonT };
}

// ---------------------------------------------------------------- 部品

export class Kit {
  constructor(lambert) {
    // 追加スキンは Lambert(物理ベースは初回コンパイルで固まるため)。マテリアルは色ごとに使い回す
    this.lambert = lambert;
    this.mats = new Map();
    this.geoms = new Map();
  }

  mat(color, opts = {}) {
    const key = `${color}|${JSON.stringify(opts)}`;
    if (!this.mats.has(key)) {
      const Ctor = this.lambert ? THREE.MeshLambertMaterial : THREE.MeshStandardMaterial;
      const params = { color, ...opts };
      if (!this.lambert && params.roughness === undefined) {
        params.roughness = 0.85;
      }
      this.mats.set(key, new Ctor(params));
    }
    return this.mats.get(key);
  }

  geom(kind, ...args) {
    const key = `${kind}|${args.join(',')}`;
    if (!this.geoms.has(key)) {
      const Ctor = { box: THREE.BoxGeometry, cyl: THREE.CylinderGeometry, sph: THREE.SphereGeometry, cone: THREE.ConeGeometry, torus: THREE.TorusGeometry, ico: THREE.IcosahedronGeometry, oct: THREE.OctahedronGeometry }[kind];
      this.geoms.set(key, new Ctor(...args));
    }
    return this.geoms.get(key);
  }

  box(parent, w, h, d, color, x, y, z, opts) {
    const m = new THREE.Mesh(this.geom('box', w, h, d), this.mat(color, opts));
    m.position.set(x, y, z);
    m.castShadow = true;
    m.receiveShadow = true;
    parent.add(m);
    return m;
  }

  cyl(parent, rt, rb, h, color, x, y, z, seg = 16, opts) {
    const m = new THREE.Mesh(this.geom('cyl', rt, rb, h, seg), this.mat(color, opts));
    m.position.set(x, y, z);
    m.castShadow = true;
    m.receiveShadow = true;
    parent.add(m);
    return m;
  }

  sph(parent, r, color, x, y, z, opts) {
    const m = new THREE.Mesh(this.geom('sph', r, 20, 14), this.mat(color, opts));
    m.position.set(x, y, z);
    m.castShadow = true;
    parent.add(m);
    return m;
  }

  dispose() {
    for (const m of this.mats.values()) {
      m.dispose();
    }
    for (const g of this.geoms.values()) {
      g.dispose();
    }
  }
}

// 浮島。slab=四角い板(上面の芝と厚い土の層)、round=角の丸い島(下は岩の逆円錐)。
// opts.topMat で上面のマテリアルを差し替えられる(魔界の溶岩のひび割れなど)
export function island(kit, parent, w, d, opts = {}) {
  const top = opts.top || '#86c95c';
  const edge = opts.edge || top;
  const soil = opts.soil || '#8a5a36';
  const deep = opts.deep || '#6a4228';
  const rock = opts.rock || '#5d5560';
  const g = new THREE.Group();
  const topMat = opts.topMat || kit.mat(top);
  if (opts.shape === 'round') {
    const seg = 64;
    const t = new THREE.Mesh(new THREE.CylinderGeometry(1, 1, 0.8, seg), [kit.mat(edge), topMat, kit.mat(edge)]);
    t.scale.set(w / 2, 1, d / 2);
    t.position.y = -0.4;
    t.receiveShadow = true;
    g.add(t);
    const s1 = new THREE.Mesh(new THREE.CylinderGeometry(1, 0.9, 2.6, seg), kit.mat(soil));
    s1.scale.set(w / 2 - 0.05, 1, d / 2 - 0.05);
    s1.position.y = -2.1;
    g.add(s1);
    const s2 = new THREE.Mesh(new THREE.ConeGeometry(0.9, 7, 14), kit.mat(rock, { flatShading: true }));
    s2.scale.set(w / 2, 1, d / 2);
    s2.rotation.x = Math.PI;
    s2.position.y = -6.9;
    g.add(s2);
  } else {
    const t = new THREE.Mesh(kit.geom('box', w, 0.8, d), [kit.mat(edge), kit.mat(edge), topMat, kit.mat(edge), kit.mat(edge), kit.mat(edge)]);
    t.position.y = -0.4;
    t.receiveShadow = true;
    g.add(t);
    kit.box(g, w - 0.1, 3.0, d - 0.1, soil, 0, -2.3, 0).castShadow = false;
    kit.box(g, w - 0.6, 1.2, d - 0.6, deep, 0, -4.4, 0).castShadow = false;
  }
  parent.add(g);
  return g;
}

// 雲。島に重ならないよう、奥の空の高い所をゆっくり流れる
export function clouds(kit, parent, color, count = 4) {
  const list = [];
  const m = new THREE.MeshLambertMaterial({ color, transparent: true, opacity: 0.95 });
  const glow = new THREE.Color();
  for (let i = 0; i < count; i++) {
    const c = new THREE.Group();
    for (let k = 0; k < 4; k++) {
      const s = new THREE.Mesh(kit.geom('ico', 1, 1), m);
      s.scale.set(1.5 + (k % 2) * 0.5, 1.1 + (k % 3) * 0.25, 1.3);
      s.position.set(k * 1.5 - 2.2, (k % 2) * 0.5, (k % 3) * 0.3);
      c.add(s);
    }
    c.userData.phase = i / count;
    c.scale.setScalar(0.8 + (i % 3) * 0.25);
    parent.add(c);
    list.push(c);
  }
  return {
    list,
    update(t, cloudColor) {
      // 陰の側も灰色にならないよう、自分で少し光らせる
      m.color.copy(cloudColor);
      m.emissive.copy(glow.copy(cloudColor).multiplyScalar(0.55));
      for (const c of list) {
        // 島より奥(-x 側の縁 → -z 側の縁)をゆっくり流れる。奥にあるので島に隠れることはあっても島を隠さない
        const u = ((c.userData.phase + t * 0.002) % 1) * 76;
        const y = 3 + ((c.userData.phase * 7) % 1) * 6;
        c.position.set(u < 38 ? -26 : -26 + (u - 38), y, u < 38 ? 14 - u : -24);
      }
    },
  };
}

export function tree(kit, parent, x, z, s = 1, leaf = '#3f8f4a') {
  kit.cyl(parent, 0.18 * s, 0.25 * s, 1.2 * s, '#6b4a2f', x, 0.6 * s, z, 8);
  const t = new THREE.Mesh(kit.geom('ico', 1.1 * s, 0), kit.mat(leaf, { flatShading: true }));
  t.position.set(x, 1.9 * s, z);
  t.castShadow = true;
  parent.add(t);
  const t2 = new THREE.Mesh(kit.geom('ico', 0.8 * s, 0), kit.mat(leaf, { flatShading: true }));
  t2.position.set(x + 0.2 * s, 2.7 * s, z - 0.1 * s);
  t2.castShadow = true;
  parent.add(t2);
}

export function plant(kit, parent, x, y, z, s = 1) {
  kit.cyl(parent, 0.22 * s, 0.17 * s, 0.35 * s, '#b5653a', x, y + 0.17 * s, z, 10);
  const leaf = new THREE.Mesh(kit.geom('ico', 0.32 * s, 0), kit.mat('#4c9a52', { flatShading: true }));
  leaf.position.set(x, y + 0.55 * s, z);
  leaf.castShadow = true;
  parent.add(leaf);
}

// 針葉樹(円錐を3段)
export function pine(kit, parent, x, z, s = 1, leaf = '#2f6b4f') {
  kit.cyl(parent, 0.14 * s, 0.2 * s, 0.9 * s, '#5a3e2a', x, 0.45 * s, z, 6);
  [[1.25, 1.5, 1.4], [1.0, 1.3, 2.3], [0.7, 1.1, 3.1]].forEach(([r, h, y]) => {
    const c = new THREE.Mesh(kit.geom('cone', r * s, h * s, 7), kit.mat(leaf, { flatShading: true }));
    c.position.set(x, y * s, z);
    c.castShadow = true;
    parent.add(c);
  });
}

// 物の名札(「アーカイブ」「コーヒー」など)。スキンを作り直すときに消せるよう ctx.labels に入れる
export function propLabel(ctx, parent, text, x, y, z) {
  const el = document.createElement('div');
  el.className = 'prop-label';
  el.textContent = text;
  const o = new CSS2DObject(el);
  o.position.set(x, y, z);
  parent.add(o);
  (ctx.labels ||= []).push(o);
  return o;
}

export function glowDot() {
  const c = document.createElement('canvas');
  c.width = c.height = 64;
  const g = c.getContext('2d');
  const grad = g.createRadialGradient(32, 32, 0, 32, 32, 30);
  grad.addColorStop(0, 'rgba(255,255,255,1)');
  grad.addColorStop(0.3, 'rgba(255,255,255,0.6)');
  grad.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = grad;
  g.fillRect(0, 0, 64, 64);
  return new THREE.CanvasTexture(c);
}

// 光の粒(画面上の大きさ固定)。mode: float=ふわふわ漂う / rise=下から上へ昇って消える
export function sparkles(parent, { count, box, colors, size = 5, mode = 'float', speed = 1 }) {
  const pos = new Float32Array(count * 3);
  const col = new Float32Array(count * 3);
  const [x0, x1, y0, y1, z0, z1] = box;
  const seeds = Array.from({ length: count }, (_, i) => {
    const c = new THREE.Color(colors[i % colors.length]);
    col.set([c.r, c.g, c.b], i * 3);
    return { x: x0 + Math.random() * (x1 - x0), y: Math.random(), z: z0 + Math.random() * (z1 - z0), p: Math.random() * 6.3, v: 0.5 + Math.random() };
  });
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  geo.setAttribute('color', new THREE.BufferAttribute(col, 3));
  const mat = new THREE.PointsMaterial({ size, sizeAttenuation: false, map: glowDot(), vertexColors: true, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, toneMapped: false });
  const pts = new THREE.Points(geo, mat);
  pts.frustumCulled = false;
  parent.add(pts);
  return {
    points: pts,
    material: mat,
    update(t) {
      seeds.forEach((s, i) => {
        let y;
        if (mode === 'rise') {
          y = y0 + ((s.y + t * 0.05 * s.v * speed) % 1) * (y1 - y0);
        } else {
          y = y0 + s.y * (y1 - y0) + Math.sin(t * 0.7 * speed + s.p * 2) * 0.5;
        }
        pos[i * 3] = s.x + Math.sin(t * 0.3 * speed + s.p) * 0.8;
        pos[i * 3 + 1] = y;
        pos[i * 3 + 2] = s.z + Math.cos(t * 0.25 * speed + s.p) * 0.8;
      });
      geo.attributes.position.needsUpdate = true;
    },
  };
}

// 夜に灯るランプ。lights に入れておき、時刻で点灯度を変える
export function lampLight(parent, lights, x, y, z, color = '#ffc77a', dist = 9, power = 6) {
  const l = new THREE.PointLight(color, 0, dist, 1.6);
  l.position.set(x, y, z);
  l.userData.power = power;
  parent.add(l);
  lights.push(l);
  return l;
}

// 時計(文字盤は Canvas、針はメッシュ)とカレンダー。現在時刻に同期する
export function wallClock(kit, parent, x, y, z, rotY, frameColor = '#5a4636') {
  const g = new THREE.Group();
  g.position.set(x, y, z);
  g.rotation.y = rotY;
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = 128;
  const c = canvas.getContext('2d');
  c.fillStyle = '#fbf7ee';
  c.beginPath();
  c.arc(64, 64, 62, 0, Math.PI * 2);
  c.fill();
  c.fillStyle = '#333';
  for (let i = 0; i < 12; i++) {
    const a = (i / 12) * Math.PI * 2;
    c.fillRect(64 + Math.sin(a) * 50 - 2, 64 - Math.cos(a) * 50 - 2, 4, 4);
  }
  const face = new THREE.Mesh(new THREE.CircleGeometry(0.8, 32), new THREE.MeshBasicMaterial({ map: new THREE.CanvasTexture(canvas), color: '#d0d0d0' }));
  face.position.z = 0.06;
  g.add(face);
  const rim = new THREE.Mesh(kit.geom('torus', 0.82, 0.07, 8, 32), kit.mat(frameColor));
  rim.position.z = 0.06;
  g.add(rim);
  const hand = (len, w, color) => {
    const pivot = new THREE.Group();
    const m = new THREE.Mesh(new THREE.BoxGeometry(w, len, 0.02), new THREE.MeshBasicMaterial({ color }));
    m.position.y = len / 2;
    pivot.add(m);
    pivot.position.z = 0.09;
    g.add(pivot);
    return pivot;
  };
  const hh = hand(0.42, 0.07, '#222');
  const mh = hand(0.62, 0.05, '#222');
  const sh = hand(0.66, 0.02, '#d33');
  parent.add(g);
  return {
    group: g,
    update(date) {
      const s = date.getSeconds();
      const m = date.getMinutes() + s / 60;
      const h = (date.getHours() % 12) + m / 60;
      hh.rotation.z = -(h / 12) * Math.PI * 2;
      mh.rotation.z = -(m / 60) * Math.PI * 2;
      sh.rotation.z = -(s / 60) * Math.PI * 2;
    },
  };
}

export function calendar(parent, x, y, z, rotY) {
  const canvas = document.createElement('canvas');
  canvas.width = 128;
  canvas.height = 160;
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  const m = new THREE.Mesh(new THREE.PlaneGeometry(0.9, 1.12), new THREE.MeshBasicMaterial({ map: tex, color: '#d0d0d0' }));
  m.position.set(x, y, z);
  m.rotation.y = rotY;
  parent.add(m);
  let last = '';
  return {
    update(date) {
      const key = date.toDateString();
      if (key === last) {
        return;
      }
      last = key;
      const c = canvas.getContext('2d');
      c.fillStyle = '#fff';
      c.fillRect(0, 0, 128, 160);
      c.fillStyle = '#d64545';
      c.fillRect(0, 0, 128, 38);
      c.fillStyle = '#fff';
      c.font = 'bold 22px "Segoe UI", sans-serif';
      c.textAlign = 'center';
      c.fillText(`${date.getFullYear()}/${date.getMonth() + 1}`, 64, 28);
      c.fillStyle = '#333';
      c.font = 'bold 76px "Segoe UI", sans-serif';
      c.fillText(String(date.getDate()), 64, 118);
      c.font = '20px "Yu Gothic UI", sans-serif';
      c.fillText(`${'日月火水木金土'[date.getDay()]}曜日`, 64, 148);
      tex.needsUpdate = true;
    },
  };
}

// ---------------------------------------------------------------- 本棚(アーカイブ)とホワイトボード。位置は全スキン共通

export const SHELF_POS = { x: -11.6, z: -6.5 };
export const BOARD_POS = { x: -11.75, y: 2.6, z: -1.2 };

export function archiveShelf(kit, parent, woodColor = '#6b4428') {
  const g = new THREE.Group();
  g.position.set(SHELF_POS.x, 0, SHELF_POS.z);
  g.rotation.y = Math.PI / 2;
  const W = 4.6;
  kit.box(g, W, 4.4, 0.12, woodColor, 0, 2.2, -0.55);
  kit.box(g, 0.12, 4.4, 1.1, woodColor, -W / 2, 2.2, 0);
  kit.box(g, 0.12, 4.4, 1.1, woodColor, W / 2, 2.2, 0);
  for (let i = 0; i <= 4; i++) {
    kit.box(g, W, 0.1, 1.1, woodColor, 0, 0.1 + i * 1.05, 0);
  }
  parent.add(g);
  const books = new THREE.Group();
  g.add(books);
  const geo = new THREE.BoxGeometry(1, 1, 0.8);
  const mats = new Map();
  return {
    group: g,
    books,
    // 過去セッション最大160冊(4段)。色=プロジェクト色、高さ=ツール数の log、厚み=ID のハッシュ
    setBooks(list) {
      while (books.children.length) {
        books.remove(books.children[0]);
      }
      let row = 0;
      let x = -W / 2 + 0.12;
      for (const s of list.slice(0, 160)) {
        const thick = 0.07 + (fnv1a(s.id) % 5) * 0.012;
        if (x + thick > W / 2 - 0.1) {
          row++;
          x = -W / 2 + 0.12;
          if (row >= 4) {
            break;
          }
        }
        const h = Math.min(0.92, 0.45 + Math.log10(1 + (s.toolCalls || 0)) * 0.17);
        // 同じプロジェクトの本が並んでも1冊ずつ見分けられるよう、明るさを3段階ずらす
        const shade = (fnv1a(`${s.id}:b`) % 3) - 1;
        const color = `#${new THREE.Color(projectColor(s.project)).offsetHSL(0, 0, shade * 0.07).getHexString()}`;
        if (!mats.has(color)) {
          // 棚の奥は影で暗くなるので、少し自分で光らせて色を読めるようにする
          mats.set(color, kit.mat(color, { emissive: color, emissiveIntensity: 0.35 }));
        }
        const b = new THREE.Mesh(geo, mats.get(color));
        b.scale.set(thick, h, 1);
        b.position.set(x + thick / 2, 0.15 + row * 1.05 + h / 2, 0.02);
        b.userData = { kind: 'book', session: s };
        b.castShadow = true;
        books.add(b);
        x += thick + 0.02;
      }
    },
  };
}

export function whiteboard(kit, parent, frameColor = '#cfd4dc', paper = '#ffffff') {
  const canvas = document.createElement('canvas');
  canvas.width = 512;
  canvas.height = 320;
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.anisotropy = 4;
  const g = new THREE.Group();
  g.position.set(BOARD_POS.x, BOARD_POS.y, BOARD_POS.z);
  g.rotation.y = Math.PI / 2;
  kit.box(g, 4.2, 2.7, 0.1, frameColor, 0, 0, -0.05);
  const face = new THREE.Mesh(new THREE.PlaneGeometry(4, 2.5), new THREE.MeshBasicMaterial({ map: tex, toneMapped: false }));
  face.position.z = 0.01;
  face.userData = { kind: 'board' };
  g.add(face);
  parent.add(g);
  let last = '';
  return {
    group: g,
    face,
    // 表示日のまとめ(セッション・依頼・ツール・稼働＋ツール上位5の棒)を描く
    draw(stats, label) {
      const key = JSON.stringify([stats, label]);
      if (key === last) {
        return;
      }
      last = key;
      const c = canvas.getContext('2d');
      c.fillStyle = paper;
      c.fillRect(0, 0, 512, 320);
      c.fillStyle = '#2e3a59';
      c.font = 'bold 28px "Yu Gothic UI", sans-serif';
      c.fillText(`📋 ${label}のまとめ`, 22, 44);
      const s = stats || { sessions: 0, prompts: 0, toolCalls: 0, tools: {}, activeMs: 0 };
      const boxes = [['会話', s.sessions], ['依頼', s.prompts], ['ツール', s.toolCalls], ['稼働', `${(s.activeMs / 3600000).toFixed(1)}h`]];
      boxes.forEach(([n, v], i) => {
        const x = 22 + i * 122;
        c.strokeStyle = '#c9cfdb';
        c.lineWidth = 2;
        c.strokeRect(x, 64, 110, 76);
        c.fillStyle = '#d97757';
        c.font = 'bold 34px "Segoe UI", sans-serif';
        c.fillText(String(v), x + 12, 108);
        c.fillStyle = '#556';
        c.font = '18px "Yu Gothic UI", sans-serif';
        c.fillText(n, x + 12, 132);
      });
      const top = Object.entries(s.tools || {}).sort((a, b) => b[1] - a[1]).slice(0, 5);
      const max = top.length ? top[0][1] : 1;
      top.forEach(([n, v], i) => {
        const y = 168 + i * 29;
        c.fillStyle = '#333';
        c.font = '18px "Segoe UI", sans-serif';
        c.fillText(n, 22, y + 17);
        c.fillStyle = '#e07a5f';
        c.fillRect(150, y + 3, (300 * v) / max, 17);
        c.fillStyle = '#555';
        c.fillText(String(v), 160 + (300 * v) / max, y + 17);
      });
      tex.needsUpdate = true;
    },
  };
}

// ---------------------------------------------------------------- 教室スキン(既定)

// 部屋の範囲(全スキン共通)。奥(-z)と左(-x)に壁、手前と右は開いている
export const ROOM = { x0: -12, x1: 6, z0: -10, z1: 3 };

export const CLASSROOM_SEATS = (() => {
  const seats = [];
  for (const z of [-6.8, -3.3, 0.2]) {
    for (const x of [-7.6, -4.6, -1.6, 1.4]) {
      // 席は机の手前(+z 側)。キャラは奥の机を向く
      seats.push({ x, z: z + 1.0, rot: Math.PI, desk: { x, z } });
    }
  }
  return seats;
})();

function deskSet(kit, parent, seat, i, glows) {
  const { x, z } = seat.desk;
  kit.box(parent, 2.0, 0.08, 1.0, '#dcbc8e', x, 1.0, z);
  for (const dx of [-0.92, 0.92]) {
    kit.box(parent, 0.06, 0.96, 0.9, '#a3a8b2', x + dx, 0.5, z);
  }
  // 椅子(黒い背もたれ)
  kit.box(parent, 0.86, 0.1, 0.8, '#2c3038', x, 0.6, z + 1.0);
  kit.box(parent, 0.86, 0.8, 0.1, '#2c3038', x, 1.05, z + 1.4);
  kit.cyl(parent, 0.05, 0.05, 0.55, '#6b6f78', x, 0.3, z + 1.0, 6);
  kit.cyl(parent, 0.3, 0.3, 0.05, '#3a3e48', x, 0.04, z + 1.0, 10);
  // モニター
  kit.box(parent, 1.1, 0.68, 0.06, '#1b1d22', x, 1.52, z - 0.25);
  kit.box(parent, 0.1, 0.4, 0.08, '#1b1d22', x, 1.22, z - 0.28);
  kit.box(parent, 0.4, 0.03, 0.26, '#1b1d22', x, 1.05, z - 0.28);
  // 画面と✳ロゴ。状態で光る色を変える
  const screen = new THREE.Mesh(new THREE.PlaneGeometry(1.0, 0.58), new THREE.MeshBasicMaterial({ color: '#2a2f3a', toneMapped: false }));
  screen.position.set(x, 1.52, z - 0.215);
  parent.add(screen);
  const logo = new THREE.Mesh(new THREE.CircleGeometry(0.07, 12), new THREE.MeshBasicMaterial({ color: '#d97757', toneMapped: false }));
  logo.position.set(x, 1.52, z - 0.21);
  parent.add(logo);
  glows[i] = { screen, logo };
  kit.box(parent, 0.8, 0.03, 0.26, '#f1f2f5', x, 1.055, z + 0.2);
  kit.box(parent, 0.14, 0.03, 0.2, '#f1f2f5', x + 0.6, 1.055, z + 0.22);
  kit.cyl(parent, 0.07, 0.06, 0.14, '#ffffff', x - 0.72, 1.11, z + 0.1, 10);
  if (i % 4 === 1) {
    plant(kit, parent, x - 0.75, 1.04, z - 0.25, 0.55);
  }
}

// 壁の窓(白い枠と、外の空が透ける淡い水色のガラス)。rotY=0 で奥の壁、π/2 で左の壁
function windowPane(kit, parent, x, z, w, rotY = 0) {
  const g = new THREE.Group();
  g.position.set(x, 0, z);
  g.rotation.y = rotY;
  kit.box(g, w, 2.0, 0.04, '#bfe4f7', 0, 2.95, 0.02, { transparent: true, opacity: 0.7 });
  for (const y of [1.9, 4.0]) {
    kit.box(g, w + 0.24, 0.14, 0.18, '#ffffff', 0, y, 0.06);
  }
  for (const dx of [-w / 2, 0, w / 2]) {
    kit.box(g, 0.12, 2.1, 0.16, '#ffffff', dx, 2.95, 0.06);
  }
  parent.add(g);
}

export function buildClassroom(ctx) {
  const { kit, root, lights } = ctx;
  const R = ROOM;
  island(kit, root, 34, 28, { top: '#8fd16a', edge: '#7fc35e', soil: '#9c6c45', deep: '#7c5235' });
  const room = new THREE.Group();
  root.add(room);
  const cx = (R.x0 + R.x1) / 2;
  const cz = (R.z0 + R.z1) / 2;
  // 床: 木の床と、真ん中の薄いグレーのカーペット
  kit.box(room, R.x1 - R.x0 + 0.4, 0.14, R.z1 - R.z0 + 0.4, '#d8ad7c', cx - 0.2, 0.07, cz - 0.2);
  for (let z = R.z0 + 0.6; z < R.z1; z += 0.75) {
    kit.box(room, R.x1 - R.x0, 0.004, 0.025, '#c39463', cx, 0.142, z);
  }
  kit.box(room, 13.4, 0.025, 10.2, '#ececf0', -3.1, 0.155, -2.6);
  // 壁(白)と、上のふち・床の幅木
  const wall = '#f8f7f3';
  kit.box(room, R.x1 - R.x0 + 0.4, 5, 0.4, wall, cx - 0.2, 2.5, R.z0 - 0.2);
  kit.box(room, 0.4, 5, R.z1 - R.z0, wall, R.x0 - 0.2, 2.5, cz);
  kit.box(room, R.x1 - R.x0 + 0.5, 0.22, 0.5, '#dcdde2', cx - 0.25, 5.1, R.z0 - 0.2);
  kit.box(room, 0.5, 0.22, R.z1 - R.z0 + 0.05, '#dcdde2', R.x0 - 0.2, 5.1, cz);
  kit.box(room, R.x1 - R.x0, 0.22, 0.05, '#cdb89a', cx, 0.25, R.z0 + 0.02);
  kit.box(room, 0.05, 0.22, R.z1 - R.z0, '#cdb89a', R.x0 + 0.02, 0.25, cz);
  // 窓(奥2つ・左1つ)
  windowPane(kit, room, -7.6, R.z0 + 0.02, 2.6);
  windowPane(kit, room, 2.9, R.z0 + 0.02, 2.6);
  windowPane(kit, room, R.x0 + 0.02, 1.9, 1.6, Math.PI / 2);
  // 奥の壁: ポスター・時計・カレンダー
  kit.box(room, 1.2, 1.6, 0.04, '#d97757', -4.6, 3.0, R.z0 + 0.03);
  const sun = new THREE.Mesh(kit.geom('oct', 0.24, 0), kit.mat('#fff4ea'));
  sun.position.set(-4.6, 3.25, R.z0 + 0.08);
  sun.scale.z = 0.2;
  room.add(sun);
  kit.box(room, 0.7, 0.05, 0.05, '#f6d2c4', -4.6, 2.65, R.z0 + 0.06);
  kit.box(room, 0.5, 0.05, 0.05, '#f6d2c4', -4.6, 2.5, R.z0 + 0.06);
  ctx.clock = wallClock(kit, room, -2.4, 3.3, R.z0 + 0.02, 0, '#3a3f48');
  ctx.calendar = calendar(room, -0.6, 3.1, R.z0 + 0.04, 0);
  // 机12席
  ctx.glows = [];
  CLASSROOM_SEATS.forEach((s, i) => deskSet(kit, room, s, i, ctx.glows));
  // コーヒーコーナー(右手前)
  kit.box(room, 1.0, 1.05, 2.6, '#eef0f3', 5.2, 0.6, 0.2);
  kit.box(room, 1.1, 0.08, 2.7, '#8a6446', 5.2, 1.16, 0.2);
  kit.box(room, 0.55, 0.65, 0.5, '#24262c', 5.3, 1.52, -0.5);
  kit.box(room, 0.3, 0.05, 0.3, '#3a3e48', 5.0, 1.22, -0.5);
  kit.cyl(room, 0.06, 0.05, 0.12, '#ffffff', 5.0, 1.3, -0.5, 8);
  kit.cyl(room, 0.24, 0.24, 0.6, '#a8cbe6', 5.3, 1.5, 0.9, 14, { transparent: true, opacity: 0.85 });
  kit.box(room, 0.5, 0.2, 0.5, '#dfe3e8', 5.3, 1.28, 0.9);
  for (const mz of [0.0, 0.25]) {
    kit.cyl(room, 0.07, 0.06, 0.13, '#ffffff', 4.9, 1.27, mz, 8);
  }
  propLabel(ctx, room, '☕ コーヒー', 5.2, 2.6, 0.2);
  // 観葉植物
  plant(kit, room, 5.0, 0.14, 2.3, 1.5);
  plant(kit, room, -10.9, 0.14, 2.4, 1.3);
  plant(kit, room, 5.0, 0.14, -9.1, 1.3);
  plant(kit, room, -10.9, 0.14, -9.2, 1.2);
  // フロアランプ
  for (const [lx, lz] of [[-10.6, -3.6], [3.4, 2.4]]) {
    kit.cyl(room, 0.04, 0.04, 2.6, '#2c3038', lx, 1.3, lz, 6);
    kit.cyl(room, 0.28, 0.06, 0.06, '#2c3038', lx, 0.05, lz, 12);
    kit.cyl(room, 0.28, 0.42, 0.45, '#fff4dc', lx, 2.75, lz, 16, { emissive: '#ffd08a', emissiveIntensity: 0.35 });
    lampLight(room, lights, lx, 2.6, lz, '#ffd59a', 9, 4);
  }
  ctx.shelf = archiveShelf(kit, room, '#5e3d25');
  propLabel(ctx, room, '📚 アーカイブ', SHELF_POS.x + 0.3, 4.9, SHELF_POS.z);
  ctx.board = whiteboard(kit, room, '#d4d8de');
  // ---- 外: 木・池・飛び石・ベンチ・街灯・花
  for (const [tx, tz, s] of [[-15, -12, 1.2], [9, -11.5, 1.1], [13, -5, 1.3], [-15, 7, 1], [12, 9, 1.2], [14, 2, 0.9], [-10, 11, 1], [-15.5, -3, 0.9], [4, 12, 0.85]]) {
    tree(kit, root, tx, tz, s, '#4f9e4c');
  }
  const pond = new THREE.Mesh(new THREE.CircleGeometry(1, 40), kit.mat('#7cc4ea'));
  pond.rotation.x = -Math.PI / 2;
  pond.scale.set(3.2, 2.0, 1);
  pond.position.set(6.5, 0.02, 8.0);
  pond.receiveShadow = true;
  root.add(pond);
  const pondRim = new THREE.Mesh(new THREE.RingGeometry(1, 1.08, 40), kit.mat('#d9e4ea'));
  pondRim.rotation.x = -Math.PI / 2;
  pondRim.scale.set(3.2, 2.0, 1);
  pondRim.position.set(6.5, 0.025, 8.0);
  root.add(pondRim);
  for (let i = 0; i < 5; i++) {
    kit.cyl(root, 0.42, 0.46, 0.1, '#e4e2dc', -1.5 + i * 1.15, 0.05, 5.0 + (i % 2) * 0.55, 12);
  }
  kit.box(root, 2.2, 0.12, 0.65, '#a26a3e', 1.6, 0.62, 9.6);
  kit.box(root, 2.2, 0.5, 0.1, '#a26a3e', 1.6, 0.95, 9.9);
  kit.box(root, 0.1, 0.62, 0.55, '#2c3038', 0.7, 0.31, 9.6);
  kit.box(root, 0.1, 0.62, 0.55, '#2c3038', 2.5, 0.31, 9.6);
  for (const [px, pz] of [[-3.6, 7.6], [9.5, 3.5]]) {
    kit.cyl(root, 0.07, 0.1, 3.2, '#2c3038', px, 1.6, pz, 8);
    kit.sph(root, 0.3, '#fffbea', px, 3.35, pz, { emissive: '#ffe7a8', emissiveIntensity: 0.5 });
    lampLight(root, lights, px, 3.2, pz, '#ffe2a8', 8, 5);
  }
  for (let i = 0; i < 34; i++) {
    const a = i * 2.4;
    const fx = -12 + (i % 17) * 1.5 + Math.sin(a) * 0.5;
    const fz = 5.5 + Math.floor(i / 17) * 5.5 + Math.cos(a) * 0.7;
    if (Math.hypot(fx - 6.5, (fz - 8) * 1.6) < 4.2) {
      continue;
    }
    kit.sph(root, 0.12, ['#ff9fb4', '#ffd76a', '#c7a6ee', '#ffffff'][i % 4], fx, 0.12, fz);
  }
  ctx.seats = CLASSROOM_SEATS;
  ctx.nav = {
    ...R,
    rects: [
      ...CLASSROOM_SEATS.map((s) => [s.desk.x - 1.0, s.desk.x + 1.0, s.desk.z - 0.5, s.desk.z + 0.5]),
      [4.7, 5.8, -1.1, 1.5],
      [R.x0, R.x0 + 1.0, -8.8, -4.2],
    ],
    circles: [[-10.6, -3.6, 0.3], [3.4, 2.4, 0.3], [5.0, 2.3, 0.4], [-10.9, 2.4, 0.4], [5.0, -9.1, 0.4], [-10.9, -9.2, 0.4], [-10.3, -1.2, 0.4]],
  };
  ctx.cloudsObj = clouds(kit, root, '#ffffff');
  // 白い壁とカーペットが灰色に沈まないよう、日差しを強めにする
  ctx.hemi = { sky: '#ffffff', ground: '#d8d0c0', boost: 1 };
  ctx.lightBoost = { sun: 1.8, hemi: 2.2 };
  ctx.panel = 'default';
  classroomLife(ctx);
}

// ---------------------------------------------------------------- 教室の生き物(鳥と犬)。エフェクトの ON/OFF に関係なく動く

export function makeBird(kit, color) {
  const g = new THREE.Group();
  const body = new THREE.Mesh(kit.geom('sph', 0.2, 10, 8), kit.mat(color));
  body.scale.set(1, 0.85, 1.7);
  g.add(body);
  const head = new THREE.Mesh(kit.geom('sph', 0.12, 10, 8), kit.mat(color));
  head.position.set(0, 0.07, 0.32);
  g.add(head);
  const beak = new THREE.Mesh(kit.geom('cone', 0.045, 0.14, 6), kit.mat('#f4a23a'));
  beak.rotation.x = Math.PI / 2;
  beak.position.set(0, 0.06, 0.46);
  g.add(beak);
  const tail = new THREE.Mesh(kit.geom('box', 0.16, 0.03, 0.26), kit.mat(color));
  tail.position.set(0, 0, -0.38);
  g.add(tail);
  const wings = [-1, 1].map((s) => {
    const pivot = new THREE.Group();
    pivot.position.set(s * 0.14, 0.05, 0);
    const w = new THREE.Mesh(kit.geom('box', 0.55, 0.03, 0.3), kit.mat(color));
    w.position.x = s * 0.27;
    pivot.add(w);
    g.add(pivot);
    return { pivot, s };
  });
  return { g, wings };
}

function makeDog(kit, coat, patch) {
  const g = new THREE.Group();
  const add = (geom, color, x, y, z) => {
    const m = new THREE.Mesh(geom, kit.mat(color));
    m.position.set(x, y, z);
    m.castShadow = true;
    g.add(m);
    return m;
  };
  add(kit.geom('box', 0.5, 0.45, 1.0), coat, 0, 0.72, 0);
  add(kit.geom('box', 0.52, 0.3, 0.5), patch, 0, 0.8, -0.1);
  const head = new THREE.Group();
  head.position.set(0, 1.1, 0.55);
  g.add(head);
  const hm = new THREE.Mesh(kit.geom('box', 0.4, 0.38, 0.4), kit.mat(coat));
  hm.castShadow = true;
  head.add(hm);
  const snout = new THREE.Mesh(kit.geom('box', 0.2, 0.18, 0.24), kit.mat(patch));
  snout.position.set(0, -0.06, 0.28);
  head.add(snout);
  const nose = new THREE.Mesh(kit.geom('box', 0.08, 0.06, 0.05), kit.mat('#222'));
  nose.position.set(0, 0.0, 0.41);
  head.add(nose);
  for (const s of [-1, 1]) {
    const ear = new THREE.Mesh(kit.geom('box', 0.1, 0.26, 0.16), kit.mat(patch));
    ear.position.set(s * 0.23, 0.08, -0.02);
    ear.rotation.z = s * 0.3;
    head.add(ear);
    const eye = new THREE.Mesh(kit.geom('sph', 0.035, 6, 6), kit.mat('#222'));
    eye.position.set(s * 0.1, 0.07, 0.2);
    head.add(eye);
  }
  const tail = new THREE.Group();
  tail.position.set(0, 0.92, -0.5);
  const tm = new THREE.Mesh(kit.geom('box', 0.1, 0.4, 0.1), kit.mat(patch));
  tm.position.y = 0.2;
  tail.add(tm);
  tail.rotation.x = -0.5;
  g.add(tail);
  const legs = [[-0.17, 0.38], [0.17, 0.38], [-0.17, -0.38], [0.17, -0.38]].map(([x, z]) => {
    const pivot = new THREE.Group();
    pivot.position.set(x, 0.5, z);
    const l = new THREE.Mesh(kit.geom('box', 0.14, 0.5, 0.14), kit.mat(coat));
    l.position.y = -0.25;
    l.castShadow = true;
    pivot.add(l);
    g.add(pivot);
    return pivot;
  });
  return { g, legs, tail, head };
}

// 鳥3〜4羽は島の上をランダムっぽく飛び回り、犬2匹は教室の外の芝生を歩く
function classroomLife(ctx) {
  const { kit, root } = ctx;
  // ---- 鳥: 周期の違うサインを重ねて、ふらふらと不規則に飛ぶ
  const birds = ['#ffffff', '#6fb3e8', '#f2c94c', '#b5835a'].map((color, i) => {
    const b = makeBird(kit, color);
    root.add(b.g);
    const r = (a, c) => a + Math.random() * (c - a);
    return {
      ...b,
      p: { cx: r(-6, 2), cz: r(-6, 2), ax: r(9, 15), az: r(8, 12), y: r(6, 9), f: [r(0.07, 0.12), r(0.05, 0.1), r(0.19, 0.31), r(0.13, 0.23)], ph: [r(0, 6.3), r(0, 6.3), r(0, 6.3), r(0, 6.3)], flap: r(11, 15), i },
    };
  });
  const birdPos = (p, t, out) => {
    out.set(
      p.cx + p.ax * Math.sin(t * p.f[0] + p.ph[0]) + 2.2 * Math.sin(t * p.f[2] + p.ph[2]),
      p.y + 1.6 * Math.sin(t * p.f[3] + p.ph[3]) + 0.5 * Math.sin(t * 1.3 + p.i),
      p.cz + p.az * Math.sin(t * p.f[1] + p.ph[1]) + 2.2 * Math.cos(t * p.f[2] * 1.3 + p.ph[0]),
    );
  };
  const a = new THREE.Vector3();
  const b = new THREE.Vector3();
  // ---- 犬: 教室の外の芝生を、池をよけた周回路で歩く
  const route = [[-11, 5.5], [-7, 8.5], [-3, 10.5], [1, 6.8], [4.5, 10.5], [9.5, 11], [11, 5.2], [5, 4.3], [-4, 5.3]];
  const dogs = [[ '#d9a066', '#fff3dc', 0, 1], ['#4a3a30', '#c9a27a', 5, -1]].map(([coat, patch, start, dir]) => {
    const d = makeDog(kit, coat, patch);
    d.g.scale.setScalar(0.85);
    root.add(d.g);
    const [x, z] = route[start];
    d.g.position.set(x, 0, z);
    return { ...d, idx: start, dir, wait: Math.random() * 3, heading: 0, phase: Math.random() * 6 };
  });
  let last = 0;
  ctx.update = (t) => {
    const dt = Math.min(0.1, Math.max(0, t - last));
    last = t;
    for (const bd of birds) {
      birdPos(bd.p, t, a);
      birdPos(bd.p, t + 0.1, b);
      bd.g.position.copy(a);
      const yaw = Math.atan2(b.x - a.x, b.z - a.z);
      const prev = bd.g.rotation.y;
      let diff = yaw - prev;
      diff = Math.atan2(Math.sin(diff), Math.cos(diff));
      bd.g.rotation.y = prev + diff * 0.2;
      bd.g.rotation.z = -diff * 1.2;
      bd.g.rotation.x = -(b.y - a.y) * 1.5;
      const flap = Math.sin(t * bd.p.flap + bd.p.i) * 0.8;
      for (const w of bd.wings) {
        w.pivot.rotation.z = w.s * flap;
      }
    }
    for (const dg of dogs) {
      dg.phase += dt;
      if (dg.wait > 0) {
        // 立ち止まってしっぽを振る
        dg.wait -= dt;
        dg.legs.forEach((l) => {
          l.rotation.x *= 0.8;
        });
        dg.tail.rotation.z = Math.sin(t * 10 + dg.phase) * 0.5;
        dg.head.rotation.x = 0.35 + Math.sin(t * 2) * 0.05;
        continue;
      }
      dg.head.rotation.x = 0;
      const [tx, tz] = route[dg.idx];
      const dx = tx - dg.g.position.x;
      const dz = tz - dg.g.position.z;
      const dist = Math.hypot(dx, dz);
      if (dist < 0.4) {
        dg.idx = (dg.idx + dg.dir + route.length) % route.length;
        dg.wait = Math.random() < 0.4 ? 1 + Math.random() * 2.5 : 0;
        continue;
      }
      const want = Math.atan2(dx, dz);
      let diff = want - dg.heading;
      diff = Math.atan2(Math.sin(diff), Math.cos(diff));
      dg.heading += diff * Math.min(1, dt * 4);
      dg.g.rotation.y = dg.heading;
      const speed = 1.6;
      dg.g.position.x += Math.sin(dg.heading) * speed * dt;
      dg.g.position.z += Math.cos(dg.heading) * speed * dt;
      const swing = Math.sin(dg.phase * 9);
      dg.legs.forEach((l, i) => {
        l.rotation.x = swing * 0.6 * (i === 0 || i === 3 ? 1 : -1);
      });
      dg.g.position.y = Math.abs(Math.sin(dg.phase * 9)) * 0.04;
      dg.tail.rotation.z = Math.sin(t * 6 + dg.phase) * 0.25;
    }
  };
}
