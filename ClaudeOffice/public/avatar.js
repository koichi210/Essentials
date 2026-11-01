// 手続き生成のちびキャラ(外部素材なし)。見た目はセッション ID のハッシュで決め、服の色はプロジェクト色
import * as THREE from 'three';
import { fnv1a } from './world.js';

const SKIN_TONES = ['#ffe0c7', '#f6d0b1', '#eec09a', '#d9a37c', '#fbe3d3', '#e8b892'];
const EYE_COLORS = ['#3b2a20', '#2d4f8a', '#2f6b4f', '#7a3b8f', '#8a4a20', '#333a44'];
const HAIR_COLORS = ['#3a2a22', '#6b4a2f', '#c99a5b', '#e8d18a', '#2b2f3a', '#a6463b', '#7f8ea6', '#d77fa1'];
const CLOTHES = ['パーカー', 'ブレザー', 'カーディガン', 'セーラー'];

// トゥーン陰影(4段)の階調テクスチャ
const gradient = (() => {
  const data = new Uint8Array([90, 150, 210, 255]);
  const t = new THREE.DataTexture(data, 4, 1, THREE.RedFormat);
  t.minFilter = THREE.NearestFilter;
  t.magFilter = THREE.NearestFilter;
  t.generateMipmaps = false;
  t.needsUpdate = true;
  return t;
})();

const outlineMat = new THREE.MeshBasicMaterial({ color: '#2a2230', side: THREE.BackSide });
const toonCache = new Map();
const geoCache = new Map();

function toon(color) {
  if (!toonCache.has(color)) {
    toonCache.set(color, new THREE.MeshToonMaterial({ color, gradientMap: gradient }));
  }
  return toonCache.get(color);
}

function geo(kind, ...args) {
  const key = `${kind}|${args.join(',')}`;
  if (!geoCache.has(key)) {
    const Ctor = { sph: THREE.SphereGeometry, cyl: THREE.CylinderGeometry, box: THREE.BoxGeometry, cone: THREE.ConeGeometry, cap: THREE.CapsuleGeometry, torus: THREE.TorusGeometry };
    geoCache.set(key, new Ctor[kind](...args));
  }
  return geoCache.get(key);
}

// 裏返した殻を少し大きく重ねて輪郭線にする
function part(parent, g, color, x = 0, y = 0, z = 0, outline = true) {
  const m = new THREE.Mesh(g, toon(color));
  m.position.set(x, y, z);
  m.castShadow = true;
  if (outline) {
    const o = new THREE.Mesh(g, outlineMat);
    o.scale.setScalar(1.07);
    m.add(o);
  }
  parent.add(m);
  return m;
}

function lighten(hex, k) {
  return `#${new THREE.Color(hex).lerp(new THREE.Color('#ffffff'), k).getHexString()}`;
}

function darken(hex, k) {
  return `#${new THREE.Color(hex).lerp(new THREE.Color('#000000'), k).getHexString()}`;
}

// ---------------------------------------------------------------- 顔(Canvas)

function drawFace(c, look, expr) {
  c.clearRect(0, 0, 128, 128);
  c.lineCap = 'round';
  c.strokeStyle = '#2a2230';
  c.fillStyle = '#2a2230';
  c.lineWidth = 5;
  const eyes = [[42, 60], [86, 60]];
  for (const [x, y] of eyes) {
    if (expr === 'blink' || expr === 'sleep') {
      c.beginPath();
      c.moveTo(x - 9, y + 2);
      c.quadraticCurveTo(x, y + 7, x + 9, y + 2);
      c.stroke();
    } else if (expr === 'smile') {
      c.beginPath();
      c.moveTo(x - 9, y + 3);
      c.quadraticCurveTo(x, y - 8, x + 9, y + 3);
      c.stroke();
    } else {
      const h = expr === 'focus' ? 9 : 13;
      c.fillStyle = look.eye;
      c.beginPath();
      c.ellipse(x, y + (expr === 'up' ? -3 : 0), 7, h, 0, 0, Math.PI * 2);
      c.fill();
      c.fillStyle = '#ffffff';
      c.beginPath();
      c.arc(x + 2, y - 4 + (expr === 'up' ? -3 : 0), 3, 0, Math.PI * 2);
      c.fill();
      if (expr === 'focus') {
        c.strokeStyle = '#2a2230';
        c.beginPath();
        c.moveTo(x - 10, y - 15);
        c.lineTo(x + 8, y - 12);
        c.stroke();
      }
    }
  }
  c.fillStyle = 'rgba(255,120,130,0.35)';
  c.beginPath();
  c.ellipse(30, 80, 9, 5, 0, 0, Math.PI * 2);
  c.ellipse(98, 80, 9, 5, 0, 0, Math.PI * 2);
  c.fill();
  c.strokeStyle = '#7a3b3b';
  c.lineWidth = 4;
  c.beginPath();
  if (expr === 'smile') {
    c.fillStyle = '#c44d5a';
    c.moveTo(54, 84);
    c.quadraticCurveTo(64, 98, 74, 84);
    c.closePath();
    c.fill();
  } else if (expr === 'up') {
    c.arc(64, 88, 4, 0, Math.PI * 2);
    c.stroke();
  } else if (expr === 'sleep') {
    c.moveTo(58, 88);
    c.lineTo(70, 88);
    c.stroke();
  } else {
    c.moveTo(58, 87);
    c.quadraticCurveTo(64, 91, 70, 87);
    c.stroke();
  }
}

// ---------------------------------------------------------------- 髪型6種

function hair(head, style, color) {
  const r = 0.44;
  const cap = part(head, new THREE.SphereGeometry(r * 1.06, 24, 16, 0, Math.PI * 2, 0, Math.PI * 0.55), color, 0, 0.03, -0.02);
  cap.rotation.x = -0.15;
  const bangs = (n, len) => {
    for (let i = 0; i < n; i++) {
      const a = -0.6 + (1.2 * i) / (n - 1);
      const b = part(head, geo('cone', 0.09, len, 6), color, Math.sin(a) * 0.36, 0.2, Math.cos(a) * 0.36, false);
      b.rotation.set(Math.PI - 0.5, 0, -a * 0.6);
    }
  };
  switch (style) {
    case 0:
      bangs(5, 0.28);
      break;
    case 1: {
      bangs(4, 0.3);
      const back = part(head, geo('cap', 0.36, 0.5, 4, 12), color, 0, -0.32, -0.16);
      back.scale.set(1.1, 1, 0.75);
      break;
    }
    case 2: {
      bangs(5, 0.26);
      for (const s of [-1, 1]) {
        const tail = part(head, geo('cap', 0.11, 0.45, 4, 8), color, s * 0.48, -0.15, -0.12);
        tail.rotation.z = s * 0.3;
      }
      break;
    }
    case 3: {
      bangs(5, 0.24);
      part(head, geo('sph', 0.17, 12, 10), color, 0, 0.36, -0.36);
      break;
    }
    case 4: {
      for (let i = 0; i < 7; i++) {
        const a = (i / 7) * Math.PI * 2;
        const s = part(head, geo('cone', 0.1, 0.32, 6), color, Math.sin(a) * 0.3, 0.38, Math.cos(a) * 0.3, false);
        s.rotation.set(Math.cos(a) * 0.7, 0, -Math.sin(a) * 0.7);
      }
      bangs(3, 0.2);
      break;
    }
    default: {
      bangs(6, 0.3);
      for (const s of [-1, 1]) {
        const side = part(head, geo('cap', 0.12, 0.35, 4, 8), color, s * 0.4, -0.12, 0.08);
        side.scale.z = 0.8;
      }
    }
  }
}

// ---------------------------------------------------------------- 追加スキンの飾り

function animalParts(head, body, kind, color) {
  const ear = (x, shape) => {
    const e = part(head, shape, color, x, 0.42, -0.02);
    e.rotation.z = -x * 0.8;
    return e;
  };
  if (kind === 'cat' || kind === 'fox' || kind === 'wolf') {
    ear(-0.24, geo('cone', 0.12, 0.26, 4));
    ear(0.24, geo('cone', 0.12, 0.26, 4));
  } else if (kind === 'rabbit') {
    for (const x of [-0.13, 0.13]) {
      const e = part(head, geo('cap', 0.07, 0.4, 4, 8), color, x, 0.62, -0.05);
      e.rotation.z = -x;
    }
  } else if (kind === 'elf') {
    for (const s of [-1, 1]) {
      const e = part(head, geo('cone', 0.06, 0.3, 6), lighten('#f6d0b1', 0), s * 0.46, 0.0, 0);
      e.rotation.z = -s * 1.2;
    }
    return;
  }
  const tail = part(body, kind === 'rabbit' ? geo('sph', 0.12, 10, 8) : geo('cap', 0.07, 0.42, 4, 8), color, 0, 0.1, -0.3);
  tail.rotation.x = kind === 'rabbit' ? 0 : -0.9;
}

function demonParts(head, body, role) {
  for (const s of [-1, 1]) {
    const h = part(head, geo('cone', 0.07, 0.3, 8), '#2b2430', s * 0.22, 0.45, 0);
    h.rotation.z = -s * 0.35;
    const w = part(body, geo('cone', 0.32, 0.5, 3), '#3b2a4a', s * 0.32, 0.55, -0.3);
    w.rotation.set(0.3, 0, s * 1.6);
    w.scale.z = 0.2;
  }
  const tail = part(body, geo('cyl', 0.03, 0.03, 0.5, 6), '#2b2430', 0, 0.05, -0.35);
  tail.rotation.x = -1.0;
  const tip = part(tail, geo('cone', 0.09, 0.14, 4), '#c0233a', 0, 0.3, 0);
  tip.rotation.y = Math.PI / 4;
  if (role === 0) {
    const crown = part(head, geo('cyl', 0.2, 0.22, 0.14, 6), '#e9b44c', 0, 0.48, 0);
    crown.rotation.y = 0.3;
  }
}

function jobHat(head, job) {
  if (job === 0) {
    const h = part(head, geo('cone', 0.34, 0.6, 12), '#5c3d8f', 0, 0.62, -0.03);
    h.rotation.x = -0.2;
    part(head, geo('cyl', 0.5, 0.5, 0.04, 16), '#5c3d8f', 0, 0.38, 0);
  } else if (job === 1) {
    part(head, new THREE.SphereGeometry(0.47, 16, 12, 0, Math.PI * 2, 0, Math.PI * 0.5), '#9aa3b0', 0, 0.02, 0);
  } else if (job === 2) {
    const h = part(head, geo('cone', 0.3, 0.3, 8), '#3f7a3a', 0.05, 0.48, 0);
    h.rotation.z = -0.4;
  } else if (job === 3) {
    part(head, geo('cyl', 0.2, 0.26, 0.35, 12), '#f3efe2', 0, 0.55, 0);
  } else {
    part(head, geo('torus', 0.3, 0.05, 6, 16), '#b5653a', 0, 0.36, 0).rotation.x = Math.PI / 2;
  }
}

// ---------------------------------------------------------------- キャラ本体

export function lookFor(id, colorHex) {
  const h = fnv1a(id);
  const pick = (arr, salt) => arr[(fnv1a(`${id}:${salt}`) >>> 0) % arr.length];
  return {
    skin: pick(SKIN_TONES, 's'),
    eye: pick(EYE_COLORS, 'e'),
    hair: pick(HAIR_COLORS, 'h'),
    style: h % 6,
    clothes: (h >>> 3) % 4,
    clothesName: CLOTHES[(h >>> 3) % 4],
    headphones: ((h >>> 7) % 4) === 0,
    animal: ['cat', 'fox', 'rabbit', 'elf', 'wolf'][(h >>> 9) % 5],
    job: (h >>> 12) % 5,
    role: (h >>> 15) % 5,
    color: colorHex,
  };
}

export function createAvatar({ id, color, skin = 'default', standing = false, scale = 1 }) {
  const look = lookFor(id, color);
  const root = new THREE.Group();
  const body = new THREE.Group();
  root.add(body);
  body.position.y = standing ? 0.75 : 0.66;
  // 胴体(服の種類で形と飾りを変える)
  const torso = part(body, geo('cap', 0.3, 0.38, 4, 12), look.color, 0, 0.42, 0);
  torso.scale.set(1, 1, 0.8);
  const accent = lighten(look.color, 0.55);
  if (look.clothes === 0) {
    part(body, geo('torus', 0.2, 0.07, 6, 14), look.color, 0, 0.78, -0.12).rotation.x = 1.2;
    part(body, geo('box', 0.3, 0.12, 0.05), darken(look.color, 0.2), 0, 0.3, 0.27, false);
  } else if (look.clothes === 1) {
    part(body, geo('box', 0.16, 0.36, 0.05), '#ffffff', 0, 0.55, 0.27, false);
    part(body, geo('box', 0.06, 0.24, 0.05), '#c0392b', 0, 0.52, 0.29, false);
  } else if (look.clothes === 2) {
    part(body, geo('box', 0.12, 0.4, 0.05), accent, 0, 0.5, 0.27, false);
    for (let i = 0; i < 3; i++) {
      part(body, geo('sph', 0.025, 6, 6), '#f0e0c0', 0.09, 0.62 - i * 0.12, 0.29, false);
    }
  } else {
    part(body, geo('box', 0.55, 0.12, 0.32), '#ffffff', 0, 0.74, -0.05, false);
    part(body, geo('cone', 0.08, 0.15, 4), '#d64545', 0, 0.62, 0.29, false).rotation.x = Math.PI;
  }
  // 頭
  const head = new THREE.Group();
  head.position.y = 1.15;
  body.add(head);
  part(head, geo('sph', 0.44, 24, 18), look.skin);
  const faceCanvas = document.createElement('canvas');
  faceCanvas.width = faceCanvas.height = 128;
  const faceTex = new THREE.CanvasTexture(faceCanvas);
  faceTex.colorSpace = THREE.SRGBColorSpace;
  const face = new THREE.Mesh(
    new THREE.SphereGeometry(0.447, 20, 14, Math.PI / 2 - 0.75, 1.5, Math.PI / 2 - 0.65, 1.3),
    new THREE.MeshBasicMaterial({ map: faceTex, transparent: true, depthWrite: false }),
  );
  head.add(face);
  hair(head, look.style, look.hair);
  if (look.headphones && skin === 'default') {
    const band = part(head, geo('torus', 0.47, 0.035, 6, 20, Math.PI), '#2b2f3a', 0, 0.02, 0);
    band.rotation.y = Math.PI / 2;
    for (const s of [-1, 1]) {
      part(head, geo('cyl', 0.12, 0.12, 0.08, 12), '#2b2f3a', s * 0.46, 0, 0).rotation.z = Math.PI / 2;
    }
  }
  if (skin === 'isekai') {
    animalParts(head, body, look.animal, look.hair);
    jobHat(head, look.job);
  } else if (skin === 'makai') {
    demonParts(head, body, look.role);
  }
  // 腕(肩を支点に動かす)
  const arms = [-1, 1].map((s) => {
    const pivot = new THREE.Group();
    pivot.position.set(s * 0.36, 0.68, 0);
    body.add(pivot);
    const upper = part(pivot, geo('cap', 0.08, 0.32, 4, 8), look.color, 0, -0.2, 0);
    part(upper, geo('sph', 0.085, 10, 8), look.skin, 0, -0.24, 0);
    pivot.rotation.z = s * 0.15;
    return pivot;
  });
  // 脚(座っているときは前へ曲げる)
  for (const s of [-1, 1]) {
    const leg = new THREE.Group();
    leg.position.set(s * 0.14, 0.05, 0);
    body.add(leg);
    const thigh = part(leg, geo('cap', 0.1, 0.3, 4, 8), '#3a3f4f', 0, -0.2, 0);
    if (!standing) {
      leg.rotation.x = -Math.PI / 2;
      const shin = part(thigh, geo('cap', 0.09, 0.3, 4, 8), '#3a3f4f', 0, -0.22, 0.2);
      shin.rotation.x = Math.PI / 2;
    }
  }
  root.scale.setScalar(scale);

  let expr = '';
  let blinkUntil = 0;
  let nextBlink = Math.random() * 4;
  const setExpr = (e) => {
    if (e !== expr) {
      expr = e;
      drawFace(faceCanvas.getContext('2d'), look, e);
      faceTex.needsUpdate = true;
    }
  };
  setExpr('normal');
  const phase = (fnv1a(id) % 1000) / 160;

  return {
    root,
    head,
    look,
    // state: working / thinking / waiting / sleeping
    update(t, state) {
      const tt = t + phase;
      let e = 'normal';
      body.rotation.set(0, 0, 0);
      head.rotation.set(0, 0, 0);
      body.position.y = standing ? 0.75 : 0.66;
      arms[0].rotation.set(0, 0, -0.15);
      arms[1].rotation.set(0, 0, 0.15);
      if (standing) {
        // サブエージェントは跳ねる
        body.position.y += Math.abs(Math.sin(tt * 5)) * 0.35;
        arms[0].rotation.z = -0.6 - Math.sin(tt * 10) * 0.2;
        arms[1].rotation.z = 0.6 + Math.sin(tt * 10) * 0.2;
        e = 'smile';
      } else if (state === 'working') {
        // タイピングと集中顔
        e = 'focus';
        body.position.y += Math.abs(Math.sin(tt * 6)) * 0.03;
        arms[0].rotation.x = -1.2 + Math.sin(tt * 18) * 0.12;
        arms[1].rotation.x = -1.2 + Math.sin(tt * 18 + 1.5) * 0.12;
        head.rotation.x = 0.12;
      } else if (state === 'thinking') {
        // 手をあごに、頭をかしげる
        e = 'up';
        arms[1].rotation.set(-2.2, 0, 0.5);
        arms[0].rotation.x = -0.9;
        head.rotation.z = 0.22 + Math.sin(tt * 1.2) * 0.05;
        body.position.y += Math.sin(tt * 1.5) * 0.02;
      } else if (state === 'waiting') {
        // 笑顔で手をふる
        e = 'smile';
        arms[1].rotation.set(0, 0, 2.5 + Math.sin(tt * 8) * 0.35);
        head.rotation.z = Math.sin(tt * 2) * 0.06;
      } else if (state === 'sleeping') {
        // 突っ伏す
        e = 'sleep';
        body.rotation.x = 0.75;
        body.position.y -= 0.05;
        arms[0].rotation.x = -1.6;
        arms[1].rotation.x = -1.6;
        head.rotation.x = 0.3;
      }
      // まばたき
      if (e !== 'sleep' && e !== 'smile') {
        if (t > nextBlink) {
          blinkUntil = t + 0.12;
          nextBlink = t + 2.5 + Math.random() * 3;
        }
        if (t < blinkUntil) {
          e = 'blink';
        }
      }
      setExpr(e);
    },
    dispose() {
      faceTex.dispose();
      face.material.dispose();
      face.geometry.dispose();
    },
  };
}
