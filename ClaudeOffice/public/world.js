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
  classroomLife(ctx);
}

// ---------------------------------------------------------------- 異世界スキン「ルイーダの酒場」

// 空のキーフレーム(教室より紫・ピンク寄り。夜は星が濃い)
export const ISEKAI_KEYS = [
  ['#10062e', '#2a1252', '#5a2a78', '#b9a6ff', 0.35, 0.4, 1.0, '#5a4a8a', 1.0],
  ['#140a38', '#34195f', '#6b3a86', '#b9a6ff', 0.35, 0.4, 1.0, '#5e4e90', 0.9],
  ['#3a3a8a', '#c06a9c', '#ffb08a', '#ffb98a', 0.6, 0.5, 0.6, '#e0a8c0', 0.3],
  ['#5aa8e8', '#9ad8f0', '#f8e0c0', '#ffe6c0', 1.0, 0.75, 0.0, '#fff0f8', 0.0],
  ['#3d8fe8', '#7fd0f0', '#d0f2f0', '#fff6e8', 1.25, 0.9, 0.0, '#ffffff', 0.0],
  ['#4a98e6', '#86d0ee', '#d6f0ee', '#fff1dc', 1.15, 0.85, 0.0, '#ffffff', 0.0],
  ['#5a7ed8', '#eaa0a8', '#ffc690', '#ffc58a', 0.95, 0.7, 0.2, '#ffd6c8', 0.0],
  ['#3a3a90', '#d0607e', '#f98c6b', '#ff9a5e', 0.7, 0.55, 0.7, '#e49aa8', 0.2],
  ['#1c1458', '#4a2a86', '#8a4a8e', '#c0b0ff', 0.4, 0.45, 1.0, '#5c4a8c', 0.8],
  ['#10062e', '#2a1252', '#5a2a78', '#b9a6ff', 0.35, 0.4, 1.0, '#5a4a8a', 1.0],
];

// 部屋の真ん中(テーブルの間の通路)にある大クリスタルの位置
export const CRYSTAL_POS = { x: -3.1, z: -1.1 };

function glowDot() {
  const c = document.createElement('canvas');
  c.width = c.height = 32;
  const g = c.getContext('2d');
  const grad = g.createRadialGradient(16, 16, 0, 16, 16, 15);
  grad.addColorStop(0, 'rgba(255,255,255,1)');
  grad.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = grad;
  g.fillRect(0, 0, 32, 32);
  return new THREE.CanvasTexture(c);
}

// 看板(「ルイーダの酒場」)
function tavernSign(parent, x, y, z) {
  const canvas = document.createElement('canvas');
  canvas.width = 1024;
  canvas.height = 256;
  const c = canvas.getContext('2d');
  const wood = c.createLinearGradient(0, 0, 0, 256);
  wood.addColorStop(0, '#e3a565');
  wood.addColorStop(1, '#b9783d');
  c.fillStyle = wood;
  c.fillRect(0, 0, 1024, 256);
  c.strokeStyle = 'rgba(0,0,0,0.18)';
  c.lineWidth = 3;
  for (let i = 1; i < 5; i++) {
    c.beginPath();
    c.moveTo(0, i * 51);
    c.lineTo(1024, i * 51);
    c.stroke();
  }
  c.strokeStyle = '#e9c46a';
  c.lineWidth = 10;
  c.strokeRect(12, 12, 1000, 232);
  c.textAlign = 'center';
  c.fillStyle = '#2a1608';
  c.font = 'bold 128px "Yu Gothic UI", "Meiryo", sans-serif';
  c.fillText('ルイーダの酒場', 518, 158);
  c.fillStyle = '#ffe9a8';
  c.fillText('ルイーダの酒場', 512, 150);
  c.fillStyle = '#e9c46a';
  c.font = 'bold 34px "Segoe UI", sans-serif';
  c.fillText("Adventurers' Tavern", 512, 216);
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.anisotropy = 4;
  const m = new THREE.Mesh(new THREE.PlaneGeometry(4.6, 1.15), new THREE.MeshBasicMaterial({ map: tex, color: '#e0e0e0' }));
  m.position.set(x, y, z);
  parent.add(m);
}

// 依頼掲示板(羊皮紙がいっぱい)
function questBoard(kit, parent, x, y, z) {
  const canvas = document.createElement('canvas');
  canvas.width = 320;
  canvas.height = 200;
  const c = canvas.getContext('2d');
  c.fillStyle = '#dcae70';
  c.fillRect(0, 0, 320, 200);
  const papers = ['#f3e4bf', '#efe0b0', '#f8ecd0', '#e8d5a5'];
  for (let i = 0; i < 9; i++) {
    const px = 14 + (i % 3) * 102 + ((i * 17) % 9);
    const py = 12 + Math.floor(i / 3) * 62 + ((i * 11) % 7);
    c.save();
    c.translate(px + 40, py + 25);
    c.rotate((((i * 37) % 11) - 5) * 0.012);
    c.fillStyle = papers[i % 4];
    c.fillRect(-40, -25, 80, 52);
    c.fillStyle = 'rgba(80,50,30,0.55)';
    for (let k = 0; k < 4; k++) {
      c.fillRect(-32, -14 + k * 10, 50 + ((i * k * 7) % 14), 3);
    }
    c.fillStyle = i % 3 === 0 ? '#c0392b' : '#3d85c6';
    c.beginPath();
    c.arc(26, 16, 5, 0, Math.PI * 2);
    c.fill();
    c.fillStyle = '#444';
    c.beginPath();
    c.arc(0, -22, 3, 0, Math.PI * 2);
    c.fill();
    c.restore();
  }
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  kit.box(parent, 2.7, 1.8, 0.1, '#b9783d', x, y, z);
  const face = new THREE.Mesh(new THREE.PlaneGeometry(2.5, 1.56), new THREE.MeshBasicMaterial({ map: tex, color: '#d8d8d8' }));
  face.position.set(x, y, z + 0.06);
  parent.add(face);
}

// 冒険者のテーブル。水晶玉の色が状態(作業中・考え中…)で変わる
function tavernTable(kit, parent, seat, i, glows) {
  const { x, z } = seat.desk;
  kit.box(parent, 2.0, 0.1, 1.0, '#e0a060', x, 1.0, z);
  kit.box(parent, 2.1, 0.04, 1.1, '#b9783d', x, 0.94, z);
  for (const dx of [-0.88, 0.88]) {
    for (const dz of [-0.38, 0.38]) {
      kit.box(parent, 0.12, 0.95, 0.12, '#a8693a', x + dx, 0.47, z + dz);
    }
  }
  // 水晶玉(台座つき)
  kit.cyl(parent, 0.16, 0.22, 0.12, '#e9c46a', x, 1.11, z - 0.05, 12);
  const orb = new THREE.Mesh(new THREE.SphereGeometry(0.24, 18, 14), new THREE.MeshBasicMaterial({ color: '#3a3f55', toneMapped: false }));
  orb.position.set(x, 1.4, z - 0.05);
  parent.add(orb);
  // ルーンの輪(ロゴ代わり)
  const ring = new THREE.Mesh(new THREE.TorusGeometry(0.3, 0.015, 6, 28), new THREE.MeshBasicMaterial({ color: '#e9c46a', toneMapped: false }));
  ring.position.set(x, 1.4, z - 0.05);
  ring.rotation.x = Math.PI / 2 - 0.4;
  parent.add(ring);
  glows[i] = { screen: orb, logo: ring };
  // ジョッキ・パン皿・ろうそく
  kit.cyl(parent, 0.12, 0.1, 0.22, '#c9a66b', x + 0.7, 1.16, z + 0.15, 10);
  kit.cyl(parent, 0.22, 0.2, 0.04, '#d8d2c4', x - 0.65, 1.07, z + 0.2, 14);
  kit.sph(parent, 0.11, '#d9a35c', x - 0.65, 1.14, z + 0.2);
  kit.cyl(parent, 0.04, 0.04, 0.2, '#f6efd8', x - 0.7, 1.15, z - 0.2, 8);
  kit.sph(parent, 0.04, '#ffd27a', x - 0.7, 1.3, z - 0.2, { emissive: '#ffb347', emissiveIntensity: 1 });
  // 背もたれ無しの丸椅子(しっぽが見えるように)
  const sx = seat.x;
  const sz = seat.z;
  kit.cyl(parent, 0.42, 0.4, 0.1, '#e3a565', sx, 0.61, sz, 16);
  kit.cyl(parent, 0.07, 0.07, 0.56, '#a8693a', sx, 0.3, sz, 8);
  kit.cyl(parent, 0.3, 0.32, 0.05, '#a8693a', sx, 0.03, sz, 12);
}

// ギルドのクエスト板(自立型の大きな掲示板。ランク付きの依頼書がびっしり)
function guildBoard(kit, parent, x, z, rotY) {
  const g = new THREE.Group();
  g.position.set(x, 0, z);
  g.rotation.y = rotY;
  const canvas = document.createElement('canvas');
  canvas.width = 640;
  canvas.height = 400;
  const c = canvas.getContext('2d');
  c.fillStyle = '#dcae70';
  c.fillRect(0, 0, 640, 400);
  c.fillStyle = '#7d5a37';
  for (let i = 0; i < 40; i++) {
    c.fillRect((i * 97) % 640, (i * 53) % 400, 4, 2);
  }
  // 見出しのリボン
  c.fillStyle = '#8f2a3d';
  c.fillRect(150, 8, 340, 52);
  c.fillStyle = '#e9c46a';
  c.fillRect(150, 8, 340, 5);
  c.fillRect(150, 55, 340, 5);
  c.textAlign = 'center';
  c.font = 'bold 36px "Yu Gothic UI", "Meiryo", sans-serif';
  c.fillText('冒険者ギルド 依頼板', 320, 46);
  const ranks = ['S', 'A', 'A', 'B', 'B', 'B', 'C', 'C', 'D', 'E'];
  const rankColor = { S: '#c0392b', A: '#d9822b', B: '#3d85c6', C: '#2f9e5a', D: '#7a6a9a', E: '#777' };
  const papers = ['#f3e4bf', '#efe0b0', '#f8ecd0', '#e8d5a5'];
  for (let i = 0; i < 10; i++) {
    const px = 18 + (i % 5) * 122 + ((i * 13) % 8);
    const py = 80 + Math.floor(i / 5) * 160 + ((i * 7) % 9);
    c.save();
    c.translate(px + 52, py + 62);
    c.rotate((((i * 29) % 11) - 5) * 0.014);
    c.fillStyle = 'rgba(0,0,0,0.18)';
    c.fillRect(-48, -54, 104, 128);
    c.fillStyle = papers[i % 4];
    c.fillRect(-52, -58, 104, 128);
    c.fillStyle = rankColor[ranks[i]];
    c.font = 'bold 52px "Segoe UI", sans-serif';
    c.fillText(ranks[i], 0, -12);
    c.fillStyle = 'rgba(80,50,30,0.6)';
    for (let k = 0; k < 4; k++) {
      c.fillRect(-38, 6 + k * 14, 60 + ((i * k * 11) % 16), 4);
    }
    c.fillStyle = '#c0392b';
    c.beginPath();
    c.arc(34, 52, 7, 0, Math.PI * 2);
    c.fill();
    c.fillStyle = '#444';
    c.beginPath();
    c.arc(0, -52, 4, 0, Math.PI * 2);
    c.fill();
    c.restore();
  }
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.anisotropy = 4;
  for (const dx of [-1.8, 1.8]) {
    kit.box(g, 0.2, 3.8, 0.2, '#a8693a', dx, 1.9, 0);
  }
  kit.box(g, 3.9, 2.5, 0.14, '#b9783d', 0, 2.5, 0);
  const face = new THREE.Mesh(new THREE.PlaneGeometry(3.6, 2.25), new THREE.MeshBasicMaterial({ map: tex, color: '#d8d8d8' }));
  face.position.set(0, 2.5, 0.08);
  g.add(face);
  // 小さな屋根と、ぶら下がるランタン
  const roof = kit.box(g, 4.4, 0.15, 1.0, '#e0584a', 0, 3.95, 0.2);
  roof.rotation.x = 0.25;
  kit.box(g, 4.3, 0.08, 0.1, '#e9c46a', 0, 3.78, 0.62);
  kit.box(g, 0.24, 0.3, 0.24, '#ffdc8a', 1.5, 3.45, 0.4, { emissive: '#ffb347', emissiveIntensity: 0.9 });
  kit.box(g, 3.9, 0.12, 0.5, '#a8693a', 0, 1.15, 0.2);
  parent.add(g);
}

export function buildTavern(ctx) {
  const { kit, root, lights } = ctx;
  island(kit, root, 32, 26, { top: '#6fd08a', soil: '#6b4a3a', rock: '#5d5d78' });
  const R = { x0: -12, x1: 6, z0: -10, z1: 3 };
  const room = new THREE.Group();
  root.add(room);
  const wallC = '#f3e5c8';
  const beam = '#b9783d';
  const flick = [];
  // 床(板張り)と赤い絨毯
  kit.box(room, R.x1 - R.x0, 0.12, R.z1 - R.z0, '#c98f52', (R.x0 + R.x1) / 2, 0.06, (R.z0 + R.z1) / 2);
  for (let z = R.z0 + 1; z < R.z1; z += 1.3) {
    kit.box(room, R.x1 - R.x0, 0.005, 0.03, '#a8703a', (R.x0 + R.x1) / 2, 0.125, z);
  }
  kit.box(room, 13.4, 0.025, 9.4, '#e9c46a', -3.2, 0.125, -2.6);
  kit.box(room, 13, 0.03, 9, '#e0475b', -3.2, 0.13, -2.6);
  // 石壁と梁
  kit.box(room, R.x1 - R.x0, 5, 0.3, wallC, (R.x0 + R.x1) / 2, 2.5, R.z0 - 0.15);
  kit.box(room, 0.3, 5, R.z1 - R.z0, wallC, R.x0 - 0.15, 2.5, (R.z0 + R.z1) / 2);
  kit.box(room, R.x1 - R.x0, 0.3, 0.4, beam, (R.x0 + R.x1) / 2, 5.1, R.z0 - 0.15);
  kit.box(room, 0.4, 0.3, R.z1 - R.z0, beam, R.x0 - 0.15, 5.1, (R.z0 + R.z1) / 2);
  kit.box(room, R.x1 - R.x0, 0.2, 0.34, beam, (R.x0 + R.x1) / 2, 0.2, R.z0 - 0.12);
  for (let x = R.x0; x <= R.x1; x += 3) {
    kit.box(room, 0.3, 5, 0.4, beam, x, 2.5, R.z0 - 0.12);
  }
  for (let z = R.z0; z <= R.z1; z += 3.25) {
    kit.box(room, 0.4, 5, 0.3, beam, R.x0 - 0.12, 2.5, z);
  }
  // 窓(あたたかい光)
  for (const wx of [-9.0, 4.2]) {
    kit.box(room, 1.6, 1.7, 0.05, '#ffd9a0', wx, 2.9, R.z0 + 0.03, { emissive: '#ffb347', emissiveIntensity: 0.55 });
    kit.box(room, 1.8, 0.1, 0.12, beam, wx, 1.98, R.z0 + 0.06);
    kit.box(room, 1.8, 0.1, 0.12, beam, wx, 3.82, R.z0 + 0.06);
    kit.box(room, 0.08, 1.8, 0.12, beam, wx, 2.9, R.z0 + 0.06);
    kit.box(room, 1.6, 0.08, 0.12, beam, wx, 2.9, R.z0 + 0.06);
  }
  // 暖炉
  const fx = -5.5;
  kit.box(room, 3.0, 3.4, 1.0, '#c4b098', fx, 1.7, R.z0 + 0.5);
  kit.box(room, 3.4, 0.2, 1.2, beam, fx, 3.5, R.z0 + 0.55);
  kit.box(room, 1.7, 1.5, 0.3, '#1a1210', fx, 0.9, R.z0 + 1.02);
  const flames = [];
  for (const [dx, h, color] of [[-0.35, 0.8, '#ff7a1a'], [0, 1.1, '#ff9a2a'], [0.35, 0.7, '#ff6a10']]) {
    const f = new THREE.Mesh(kit.geom('cone', 0.28, h, 6), new THREE.MeshBasicMaterial({ color, toneMapped: false }));
    f.position.set(fx + dx, 0.35 + h / 2, R.z0 + 1.05);
    room.add(f);
    flames.push(f);
  }
  flick.push(lampLight(room, lights, fx, 1.2, R.z0 + 1.6, '#ff8a3c', 14, 8));
  // 看板・掲示板・時計・旗
  tavernSign(room, -1.0, 4.0, R.z0 + 0.06);
  questBoard(kit, room, -1.0, 2.0, R.z0 + 0.06);
  ctx.clock = wallClock(kit, room, 2.0, 3.4, R.z0 + 0.02, 0, '#c9a24a');
  for (const [bx, color] of [[-10.8, '#2a4d8f'], [5.5, '#8f2a3d']]) {
    kit.box(room, 0.7, 1.9, 0.05, color, bx, 3.4, R.z0 + 0.05);
    kit.box(room, 0.6, 0.12, 0.08, '#e9c46a', bx, 4.36, R.z0 + 0.06);
    const e = new THREE.Mesh(kit.geom('oct', 0.2, 0), kit.mat('#e9c46a'));
    e.position.set(bx, 3.5, R.z0 + 0.12);
    e.scale.z = 0.4;
    room.add(e);
  }
  // 吊りランタン
  for (const [lx, lz] of [[-10.6, -8.6], [3.6, -8.6]]) {
    kit.cyl(room, 0.04, 0.04, 1.0, '#333', lx, 4.4, lz, 6);
    kit.box(room, 0.4, 0.5, 0.4, '#ffdc8a', lx, 3.65, lz, { emissive: '#ffb347', emissiveIntensity: 0.8 });
    kit.box(room, 0.5, 0.08, 0.5, '#333', lx, 3.95, lz);
    flick.push(lampLight(room, lights, lx, 3.5, lz + 0.3, '#ffb868', 11, 5));
  }
  ctx.glows = [];
  CLASSROOM_SEATS.forEach((s, i) => tavernTable(kit, room, s, i, ctx.glows));
  // カウンター(右手前)と樽
  kit.box(room, 1.0, 1.2, 5.0, '#b9783d', 5.1, 0.6, -4.8);
  kit.box(room, 1.3, 0.12, 5.3, '#e3a565', 5.1, 1.26, -4.8);
  for (const [bz, color] of [[-6.3, '#3d85c6'], [-5.6, '#c0392b'], [-4.9, '#2f9e5a'], [-3.4, '#9c6ade']]) {
    kit.cyl(room, 0.1, 0.12, 0.42, color, 5.1, 1.52, bz, 8);
    kit.cyl(room, 0.04, 0.05, 0.16, color, 5.1, 1.82, bz, 6);
  }
  for (const stz of [-6.2, -4.8, -3.4]) {
    kit.cyl(room, 0.28, 0.26, 0.08, '#e3a565', 3.9, 0.8, stz, 12);
    kit.cyl(room, 0.05, 0.05, 0.7, '#a8693a', 3.9, 0.4, stz, 6);
  }
  for (const [bx, bz, s] of [[5.4, -9.0, 1], [4.4, -9.2, 0.9], [5.2, -8.1, 0.8]]) {
    kit.cyl(room, 0.45 * s, 0.45 * s, 1.0 * s, '#e3a565', bx, 0.62 * s, bz, 12);
    kit.cyl(room, 0.47 * s, 0.47 * s, 0.08, '#444', bx, 0.35 * s, bz, 12);
    kit.cyl(room, 0.47 * s, 0.47 * s, 0.08, '#444', bx, 0.9 * s, bz, 12);
  }
  kit.box(room, 0.9, 0.9, 0.9, '#dcae70', 5.2, 0.45, 1.7);
  kit.box(room, 0.7, 0.7, 0.7, '#e3a565', 5.2, 1.25, 1.7);
  // カウンターの内側(女主人の持ち場): 床を足して、背面に酒棚、卓上にビールサーバー
  kit.box(room, 1.8, 0.12, 5.4, '#c98f52', 6.7, 0.06, -4.8);
  // 背面は低い戸棚(高い棚だとカメラ側から女主人が隠れる)
  kit.box(room, 0.6, 1.0, 5.0, '#b9783d', 7.4, 0.62, -4.8);
  kit.box(room, 0.7, 0.08, 5.1, '#a8693a', 7.4, 1.16, -4.8);
  for (let k = 0; k < 9; k++) {
    const col = ['#3d85c6', '#c0392b', '#2f9e5a', '#9c6ade', '#e9c46a'][k % 5];
    kit.cyl(room, 0.09, 0.11, 0.4, col, 7.4, 1.4, -6.8 + k * 0.5, 8);
    kit.cyl(room, 0.035, 0.045, 0.14, col, 7.4, 1.66, -6.8 + k * 0.5, 6);
  }
  kit.cyl(room, 0.12, 0.12, 0.5, '#b9783d', 5.3, 1.6, -3.0, 8);
  kit.box(room, 0.5, 0.08, 0.08, '#d9d9e0', 5.15, 1.88, -3.0);
  for (const mz of [-5.9, -4.2]) {
    kit.cyl(room, 0.11, 0.09, 0.2, '#f1d27a', 4.8, 1.42, mz, 10);
  }
  ctx.shelf = archiveShelf(kit, room, '#b9783d');
  ctx.board = whiteboard(kit, room, '#c98a4a', '#f1e3bd');
  guildBoard(kit, room, 2.6, 2.3, 0.15);
  // 部屋の真ん中の大クリスタル(台座の上でゆっくり回って浮かぶ)
  const bigCrystal = new THREE.Group();
  bigCrystal.position.set(CRYSTAL_POS.x, 0, CRYSTAL_POS.z);
  room.add(bigCrystal);
  kit.cyl(bigCrystal, 0.5, 0.62, 0.35, '#7d776b', 0, 0.3, 0, 8);
  kit.cyl(bigCrystal, 0.4, 0.46, 0.1, '#e9c46a', 0, 0.52, 0, 8);
  const crystalBody = new THREE.Group();
  crystalBody.position.y = 2.5;
  bigCrystal.add(crystalBody);
  const shell = new THREE.Mesh(kit.geom('oct', 0.62, 0), kit.mat('#9be8ff', { emissive: '#38b6ff', emissiveIntensity: 0.9, transparent: true, opacity: 0.82, flatShading: true }));
  shell.scale.set(1, 2.1, 1);
  crystalBody.add(shell);
  const glowCore = new THREE.Mesh(kit.geom('oct', 0.34, 0), new THREE.MeshBasicMaterial({ color: new THREE.Color('#8ff0ff').multiplyScalar(2.2), toneMapped: false }));
  glowCore.scale.set(1, 2.1, 1);
  crystalBody.add(glowCore);
  const shards = [0, 1, 2, 3].map((i) => {
    const m = new THREE.Mesh(kit.geom('oct', 0.2, 0), kit.mat('#c9b6ff', { emissive: '#8a5ae8', emissiveIntensity: 0.9, flatShading: true }));
    m.scale.y = 1.8;
    m.userData.a = (i / 4) * Math.PI * 2;
    bigCrystal.add(m);
    return m;
  });

  // ---- 外: 異世界の森
  const leaves = ['#4fc9b0', '#b07ae0', '#ff8fb8', '#5ac86a'];
  [[-14, -11, 1.4], [9, -10, 1.2], [12, -4, 1.5], [-14, 6, 1.1], [11, 8, 1.3], [13, 2, 1], [-9, 10, 1.1]].forEach(([tx, tz, s], i) => {
    tree(kit, root, tx, tz, s, leaves[i % 4]);
  });
  // 巨大キノコと光るキノコ
  for (const [mx, mz, s] of [[-12, 9, 1.4], [8, 11, 1.1], [-3, 11.5, 0.9]]) {
    kit.cyl(root, 0.25 * s, 0.35 * s, 1.6 * s, '#f1e6d2', mx, 0.8 * s, mz, 8);
    const cap = new THREE.Mesh(kit.geom('sph', 1.1 * s, 16, 10, 0, Math.PI * 2, 0, Math.PI / 2), kit.mat('#d94f5f'));
    cap.position.set(mx, 1.6 * s, mz);
    cap.castShadow = true;
    root.add(cap);
    for (let k = 0; k < 4; k++) {
      kit.sph(root, 0.12 * s, '#ffffff', mx + Math.cos(k * 1.6) * 0.6 * s, 1.6 * s + 0.65 * s, mz + Math.sin(k * 1.6) * 0.6 * s);
    }
  }
  for (let i = 0; i < 14; i++) {
    const a = i * 2.1;
    const gx = -10 + (i % 7) * 3 + Math.sin(a);
    const gz = 7 + Math.floor(i / 7) * 4 + Math.cos(a);
    const col = ['#6ad4ff', '#c19ee0', '#7dffb0'][i % 3];
    kit.cyl(root, 0.04, 0.05, 0.22, '#e8dcc0', gx, 0.11, gz, 6);
    kit.sph(root, 0.12, col, gx, 0.26, gz, { emissive: col, emissiveIntensity: 0.9 });
  }
  // 石畳・井戸・道しるべ・街灯
  for (let i = 0; i < 5; i++) {
    kit.cyl(root, 0.4, 0.45, 0.1, '#a8a294', -2 + i * 1.1, 0.05, 5 + (i % 2) * 0.6, 10);
  }
  kit.cyl(root, 0.9, 0.95, 0.9, '#f3e5c8', 7.5, 0.45, 6.5, 14);
  kit.cyl(root, 0.62, 0.62, 0.05, '#4aa8d8', 7.5, 0.88, 6.5, 14);
  kit.box(root, 0.1, 1.7, 0.1, beam, 6.7, 1.6, 6.5);
  kit.box(root, 0.1, 1.7, 0.1, beam, 8.3, 1.6, 6.5);
  kit.box(root, 1.9, 0.12, 0.9, '#e0584a', 7.5, 2.5, 6.5);
  kit.box(root, 0.12, 2.2, 0.12, beam, 1.0, 1.1, 8.8);
  kit.box(root, 1.1, 0.35, 0.08, '#e3a565', 1.3, 1.9, 8.8).rotation.z = 0.08;
  kit.box(root, 1.1, 0.35, 0.08, '#e3a565', 0.7, 1.4, 8.8).rotation.z = -0.08;
  for (const [px, pz] of [[-4, 7.5], [3, 6.5]]) {
    kit.cyl(root, 0.07, 0.09, 3.2, '#3a3f48', px, 1.6, pz, 8);
    kit.sph(root, 0.3, '#b8f0ff', px, 3.35, pz, { emissive: '#6ad4ff', emissiveIntensity: 0.9 });
    flick.push(lampLight(root, lights, px, 3.2, pz, '#9fe0ff', 8, 6));
  }
  // 浮かぶ水晶(ゆっくり上下・回転)
  const crystals = [];
  for (const [cx, cy, cz, s] of [[-15, 4, -2, 1.2], [14, 5, -9, 1.0], [-11, 3.2, 12, 0.8], [15, 3.5, 7, 0.9], [-16, 6, -9, 0.7]]) {
    const m = new THREE.Mesh(kit.geom('oct', 0.7 * s, 0), kit.mat('#7fe8ff', { emissive: '#3fb8ff', emissiveIntensity: 0.9, flatShading: true }));
    m.scale.y = 1.8;
    m.position.set(cx, cy, cz);
    m.userData.base = cy;
    root.add(m);
    crystals.push(m);
  }
  // 遠くの浮島
  for (const [ix, iy, iz, s] of [[-30, 8, -26, 1.0], [22, 12, -30, 1.3], [-32, 3, 14, 0.9]]) {
    const g = new THREE.Group();
    g.position.set(ix, iy, iz);
    g.scale.setScalar(s);
    island(kit, g, 9, 8, { top: '#5fb96e', soil: '#6b4a3a', rock: '#5d5d78', ellipse: true });
    tree(kit, g, 0, 0, 1.2, '#ff8fb8');
    root.add(g);
  }
  // ホタル(画面上の大きさ固定の光の粒)
  const FLIES = 46;
  const pos = new Float32Array(FLIES * 3);
  const seeds = Array.from({ length: FLIES }, (_, i) => ({ x: -13 + ((i * 7.3) % 28), y: 0.8 + ((i * 1.7) % 4), z: -9 + ((i * 5.1) % 22), p: i * 1.3 }));
  const flyGeo = new THREE.BufferGeometry();
  flyGeo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  const flyMat = new THREE.PointsMaterial({ color: '#d8ff7a', size: 5, sizeAttenuation: false, map: glowDot(), transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, toneMapped: false });
  const flies = new THREE.Points(flyGeo, flyMat);
  flies.frustumCulled = false;
  root.add(flies);

  ctx.seats = CLASSROOM_SEATS;
  ctx.cloudsObj = clouds(kit, root, '#fff0f8');
  ctx.keys = ISEKAI_KEYS;
  ctx.hemi = { sky: '#fff3e0', ground: '#a09080', boost: 1.5 };
  ctx.sky = { aurora: 0.45, crack: 0, moon2: true, moonColor: '#dfeaff' };
  ctx.bloom = false;
  ctx.panel = 'isekai';
  ctx.update = (t, look) => {
    // 暖炉・ランタンは昼もほんのり灯り、ゆらめく
    for (const l of flick) {
      l.intensity = Math.max(l.intensity, l.userData.power * 0.8) * (1 + Math.sin(t * 9 + l.position.x) * 0.08 + Math.sin(t * 23 + l.position.z) * 0.05);
    }
    flames.forEach((f, i) => {
      f.scale.set(1 + Math.sin(t * 11 + i) * 0.1, 1 + Math.sin(t * 8 + i * 2) * 0.18, 1);
    });
    crystals.forEach((m, i) => {
      m.position.y = m.userData.base + Math.sin(t * 0.8 + i * 1.7) * 0.5;
      m.rotation.y = t * 0.5 + i;
    });
    crystalBody.position.y = 2.5 + Math.sin(t * 1.1) * 0.15;
    crystalBody.rotation.y = t * 0.6;
    glowCore.scale.setScalar(1 + Math.sin(t * 3) * 0.06);
    glowCore.scale.y *= 2.1;
    shards.forEach((m, i) => {
      const a = m.userData.a + t * 0.9;
      m.position.set(Math.cos(a) * 1.0, 2.2 + Math.sin(t * 1.7 + i * 1.6) * 0.45, Math.sin(a) * 1.0);
      m.rotation.y = t * 1.5;
    });
    seeds.forEach((s, i) => {
      pos[i * 3] = s.x + Math.sin(t * 0.4 + s.p) * 1.5;
      pos[i * 3 + 1] = s.y + Math.sin(t * 0.7 + s.p * 2) * 0.6;
      pos[i * 3 + 2] = s.z + Math.cos(t * 0.35 + s.p) * 1.5;
    });
    flyGeo.attributes.position.needsUpdate = true;
    flyMat.opacity = look.night ? 1 : 0.35;
  };
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
