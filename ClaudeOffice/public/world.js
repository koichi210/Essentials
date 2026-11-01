// 島・空・時刻・本棚・ホワイトボードなど全スキン共通の部品と、既定の教室スキン
import * as THREE from 'three';

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
      uniform vec3 uTop, uMid, uBottom, uSun, uMoon, uMoon2, uSunColor, uMoonColor;
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

// 浮島: 上面の板と、下に土と岩の逆円錐
export function island(kit, parent, w, d, opts = {}) {
  const top = opts.top || '#6fbf5a';
  const soil = opts.soil || '#7a5236';
  const rock = opts.rock || '#6b6b78';
  const g = new THREE.Group();
  if (opts.ellipse) {
    const m = new THREE.Mesh(new THREE.CylinderGeometry(1, 1, 1.2, 48), kit.mat(top));
    m.scale.set(w / 2, 1, d / 2);
    m.position.y = -0.6;
    m.receiveShadow = true;
    g.add(m);
    const s = new THREE.Mesh(new THREE.ConeGeometry(1, 9, 48), kit.mat(soil));
    s.scale.set(w / 2 - 0.3, 1, d / 2 - 0.3);
    s.rotation.x = Math.PI;
    s.position.y = -5.7;
    g.add(s);
  } else {
    kit.box(g, w, 1.2, d, top, 0, -0.6, 0);
    const s = new THREE.Mesh(new THREE.ConeGeometry(Math.max(w, d) * 0.62, 8, 4), kit.mat(soil));
    s.rotation.set(Math.PI, Math.PI / 4, 0);
    s.scale.set(w / Math.max(w, d), 1, d / Math.max(w, d));
    s.position.y = -5.2;
    g.add(s);
  }
  const r = new THREE.Mesh(new THREE.ConeGeometry(Math.min(w, d) * 0.3, 6, 7), kit.mat(rock));
  r.rotation.x = Math.PI;
  r.position.y = -10;
  g.add(r);
  parent.add(g);
  return g;
}

// 外の雲7つ。奥側の弧をゆっくり流れる
export function clouds(kit, parent, color) {
  const list = [];
  for (let i = 0; i < 7; i++) {
    const c = new THREE.Group();
    const m = new THREE.MeshLambertMaterial({ color, transparent: true, opacity: 0.92 });
    for (let k = 0; k < 4; k++) {
      const s = new THREE.Mesh(kit.geom('sph', 1, 12, 8), m);
      s.scale.set(1.6 + k * 0.3, 0.9, 1.2);
      s.position.set(k * 1.4 - 2, (k % 2) * 0.4, 0);
      c.add(s);
    }
    c.userData.phase = i / 7;
    parent.add(c);
    list.push(c);
  }
  return {
    list,
    update(t, cloudColor) {
      for (const c of list) {
        const a = Math.PI * (0.65 + ((c.userData.phase + t * 0.004) % 1) * 1.1);
        c.position.set(-2 + Math.cos(a) * 34, 9 + Math.sin(a * 3) * 1.5, -6 + Math.sin(a) * -26);
        c.children[0].material.color.copy(cloudColor);
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
        const color = projectColor(s.project);
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
        x += thick + 0.008;
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
  kit.box(parent, 2.0, 0.08, 1.0, '#c8a67a', x, 1.0, z);
  for (const dx of [-0.9, 0.9]) {
    for (const dz of [-0.4, 0.4]) {
      kit.box(parent, 0.07, 1.0, 0.07, '#6b6f78', x + dx, 0.5, z + dz);
    }
  }
  kit.box(parent, 0.9, 0.08, 0.85, '#3d4250', x, 0.62, z + 1.0);
  kit.box(parent, 0.9, 0.9, 0.08, '#3d4250', x, 1.05, z + 1.42);
  kit.cyl(parent, 0.05, 0.05, 0.6, '#555', x, 0.3, z + 1.0, 6);
  kit.box(parent, 1.1, 0.68, 0.06, '#1d1f26', x, 1.5, z - 0.25);
  kit.box(parent, 0.12, 0.4, 0.1, '#1d1f26', x, 1.2, z - 0.28);
  // 画面と✳ロゴ。状態で光る色を変える
  const screen = new THREE.Mesh(new THREE.PlaneGeometry(1.0, 0.58), new THREE.MeshBasicMaterial({ color: '#2a2f3a', toneMapped: false }));
  screen.position.set(x, 1.5, z - 0.215);
  parent.add(screen);
  const logo = new THREE.Mesh(new THREE.CircleGeometry(0.07, 12), new THREE.MeshBasicMaterial({ color: '#d97757', toneMapped: false }));
  logo.position.set(x, 1.5, z - 0.21);
  parent.add(logo);
  glows[i] = { screen, logo };
  kit.box(parent, 0.8, 0.03, 0.28, '#e9eaee', x, 1.06, z + 0.22);
  kit.cyl(parent, 0.07, 0.06, 0.14, '#ffffff', x + 0.7, 1.11, z + 0.1, 10);
  if (i % 3 === 1) {
    plant(kit, parent, x - 0.75, 1.04, z - 0.2, 0.6);
  }
}

export function buildClassroom(ctx) {
  const { kit, root, lights } = ctx;
  island(kit, root, 32, 26);
  const R = { x0: -12, x1: 6, z0: -10, z1: 3 };
  const room = new THREE.Group();
  root.add(room);
  kit.box(room, R.x1 - R.x0, 0.12, R.z1 - R.z0, '#b07a4a', (R.x0 + R.x1) / 2, 0.06, (R.z0 + R.z1) / 2);
  kit.box(room, 13, 0.03, 9, '#e8e1d3', -3.2, 0.13, -2.6);
  // 奥壁(窓付き)と左壁
  const wall = '#f3eadb';
  kit.box(room, R.x1 - R.x0, 5, 0.3, wall, (R.x0 + R.x1) / 2, 2.5, R.z0 - 0.15);
  kit.box(room, 0.3, 5, R.z1 - R.z0, wall, R.x0 - 0.15, 2.5, (R.z0 + R.z1) / 2);
  kit.box(room, R.x1 - R.x0, 0.25, 0.35, '#5b6fd6', (R.x0 + R.x1) / 2, 5.05, R.z0 - 0.15);
  kit.box(room, 0.35, 0.25, R.z1 - R.z0, '#5b6fd6', R.x0 - 0.15, 5.05, (R.z0 + R.z1) / 2);
  for (const wx of [-5.5, 2.8]) {
    kit.box(room, 2.6, 1.9, 0.05, '#9fd3f0', wx, 2.9, R.z0 + 0.03, { transparent: true, opacity: 0.55 });
    kit.box(room, 2.8, 0.12, 0.12, '#ffffff', wx, 1.9, R.z0 + 0.06);
    kit.box(room, 2.8, 0.12, 0.12, '#ffffff', wx, 3.9, R.z0 + 0.06);
    kit.box(room, 0.1, 2.0, 0.12, '#ffffff', wx, 2.9, R.z0 + 0.06);
  }
  kit.box(room, 0.05, 1.9, 2.4, '#9fd3f0', R.x0 + 0.03, 2.9, 1.0, { transparent: true, opacity: 0.55 });
  // 奥壁: 時計・カレンダー・ポスター
  ctx.clock = wallClock(kit, room, -1.4, 3.3, R.z0 + 0.02, 0);
  ctx.calendar = calendar(room, 0.3, 3.1, R.z0 + 0.04, 0);
  kit.box(room, 1.1, 1.5, 0.04, '#f2c14e', -3.0, 3.0, R.z0 + 0.03);
  kit.box(room, 0.5, 0.5, 0.05, '#d97757', -3.0, 3.25, R.z0 + 0.05);
  // 机12席
  ctx.glows = [];
  CLASSROOM_SEATS.forEach((s, i) => deskSet(kit, room, s, i, ctx.glows));
  // コーヒーコーナー
  kit.box(room, 2.4, 1.0, 0.9, '#5a4a3f', 4.6, 0.5, -1.0);
  kit.box(room, 0.5, 0.6, 0.45, '#222', 4.3, 1.3, -1.0);
  kit.cyl(room, 0.2, 0.2, 0.55, '#8aa7c4', 5.2, 1.28, -1.0, 12);
  plant(kit, room, 5.2, 0.12, 1.6, 1.4);
  plant(kit, room, -10.8, 0.12, 1.8, 1.3);
  plant(kit, room, 4.9, 0.12, -8.8, 1.2);
  // フロアランプ
  for (const [lx, lz] of [[-10.6, -8.6], [3.6, 1.8]]) {
    kit.cyl(room, 0.04, 0.04, 2.6, '#444', lx, 1.3, lz, 6);
    kit.cyl(room, 0.3, 0.45, 0.45, '#fff4d6', lx, 2.75, lz, 16, { emissive: '#ffcf7a', emissiveIntensity: 0.4 });
    lampLight(room, lights, lx, 2.6, lz, '#ffcf88', 9, 5);
  }
  ctx.shelf = archiveShelf(kit, room);
  ctx.board = whiteboard(kit, room);
  // 外: 木・池・飛び石・ベンチ・街灯・花
  for (const [tx, tz, s] of [[-14, -11, 1.2], [9, -10, 1.1], [12, -4, 1.3], [-14, 6, 1], [11, 8, 1.2], [13, 2, 0.9], [-9, 10, 1]]) {
    tree(kit, root, tx, tz, s);
  }
  const pond = new THREE.Mesh(new THREE.CircleGeometry(2.4, 32), kit.mat('#5fa8d8', { transparent: true, opacity: 0.85 }));
  pond.rotation.x = -Math.PI / 2;
  pond.position.set(7, 0.02, 7);
  root.add(pond);
  for (let i = 0; i < 5; i++) {
    kit.cyl(root, 0.4, 0.45, 0.1, '#b9b4aa', -2 + i * 1.1, 0.05, 5 + (i % 2) * 0.6, 10);
  }
  kit.box(root, 2.2, 0.12, 0.6, '#8a5a35', 2, 0.6, 9.5);
  kit.box(root, 0.1, 0.6, 0.5, '#444', 1.1, 0.3, 9.5);
  kit.box(root, 0.1, 0.6, 0.5, '#444', 2.9, 0.3, 9.5);
  for (const [px, pz] of [[-4, 7.5], [8, 2]]) {
    kit.cyl(root, 0.07, 0.09, 3.2, '#3a3f48', px, 1.6, pz, 8);
    kit.sph(root, 0.3, '#fff8e0', px, 3.35, pz, { emissive: '#ffe08a', emissiveIntensity: 0.5 });
    lampLight(root, lights, px, 3.2, pz, '#ffe0a0', 8, 6);
  }
  for (let i = 0; i < 26; i++) {
    const a = i * 2.4;
    const fx = -10 + (i % 13) * 1.6 + Math.sin(a) * 0.4;
    const fz = 6.5 + Math.floor(i / 13) * 3.5 + Math.cos(a) * 0.4;
    kit.sph(root, 0.13, ['#ff8fa3', '#ffd166', '#c19ee0', '#ffffff'][i % 4], fx, 0.15, fz);
  }
  ctx.seats = CLASSROOM_SEATS;
  ctx.cloudsObj = clouds(kit, root, '#ffffff');
  ctx.panel = 'default';
}
