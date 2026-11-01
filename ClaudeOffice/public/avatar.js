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
    race: raceFor(id),
    job: fnv1a(`${id}:job`) % 5,
    color: colorHex,
  };
}

// ---------------------------------------------------------------- 異世界スキン: 亜人(耳・しっぽ)と冒険者の職業装備

// 重み(合計100)。人間は少なめで、猫耳が一番多い
const RACES = [['neko', 28], ['inu', 12], ['kitsune', 14], ['usagi', 8], ['elf', 8], ['ryu', 6], ['human', 24]];
// 魔界スキンの種族(角・コウモリの羽・赤い目の悪魔が中心)
const RACES_MAKAI = [['akuma', 40], ['kyuuketsuki', 20], ['neko', 15], ['ryu', 10], ['human', 15]];
const MAKAI_TONES = ['#e8d8e8', '#d8c4e0', '#c8b8d8', '#f0d0d0', '#b8d8d0', '#c9a0c0'];
export const RACE_NAMES = { akuma: '悪魔', kyuuketsuki: '吸血鬼', neko: '猫耳族', inu: '犬耳族', kitsune: '狐耳族', usagi: '兎耳族', elf: 'エルフ', ryu: '竜人', human: '人間' };
export const JOB_NAMES = ['戦士', '魔法使い', '僧侶', '盗賊', '弓使い'];

function raceFor(id, table = RACES) {
  let n = fnv1a(`${id}:race`) % 100;
  for (const [key, weight] of table) {
    if (n < weight) {
      return key;
    }
    n -= weight;
  }
  return 'human';
}

// 耳。頭グループに付ける
function addEars(head, look) {
  const inner = '#ffb3c1';
  const hairColor = look.hair;
  for (const s of [-1, 1]) {
    const g = new THREE.Group();
    head.add(g);
    switch (look.race) {
      case 'neko':
        g.position.set(s * 0.27, 0.5, -0.02);
        g.rotation.z = -s * 0.35;
        part(g, geo('cone', 0.14, 0.34, 4), hairColor, 0, 0.12, 0);
        part(g, geo('cone', 0.08, 0.24, 4), inner, 0, 0.1, 0.06, false);
        break;
      case 'kitsune':
        g.position.set(s * 0.26, 0.5, -0.04);
        g.rotation.z = -s * 0.2;
        part(g, geo('cone', 0.15, 0.44, 4), hairColor, 0, 0.14, 0);
        part(g, geo('cone', 0.09, 0.3, 4), '#fff0dc', 0, 0.12, 0.06, false);
        part(g, geo('cone', 0.075, 0.14, 4), '#2a2230', 0, 0.36, 0, false);
        break;
      case 'inu':
        // たれ耳
        g.position.set(s * 0.4, 0.22, 0);
        g.rotation.z = s * 0.45;
        part(g, geo('cap', 0.1, 0.26, 4, 8), darken(hairColor, 0.2), 0, 0, 0).scale.z = 0.55;
        break;
      case 'usagi':
        g.position.set(s * 0.16, 0.74, -0.06);
        g.rotation.set(-0.2, 0, -s * 0.18);
        part(g, geo('cap', 0.07, 0.5, 4, 8), '#fbf6f0', 0, 0, 0).scale.z = 0.6;
        part(g, geo('cap', 0.035, 0.38, 4, 8), inner, 0, 0, 0.05, false);
        break;
      case 'akuma':
        // 小さな尖り耳と、うしろに反った赤黒い角
        g.position.set(s * 0.2, 0.5, -0.02);
        g.rotation.set(-0.35, 0, -s * 0.3);
        part(g, geo('cone', 0.075, 0.34, 5), '#4a1020', 0, 0.14, 0);
        part(head, geo('cone', 0.06, 0.26, 4), look.skin, s * 0.43, 0.02, 0).rotation.z = -s * 1.2;
        break;
      case 'kyuuketsuki':
      case 'elf':
        g.position.set(s * 0.42, 0.06, 0);
        g.rotation.z = -s * (Math.PI / 2 - 0.35);
        part(g, geo('cone', 0.08, 0.4, 4), look.skin, 0, 0, 0);
        break;
      case 'ryu':
        g.position.set(s * 0.22, 0.5, 0.02);
        g.rotation.set(-0.5, 0, -s * 0.3);
        part(g, geo('cone', 0.07, 0.3, 5), '#efe6d0', 0, 0.1, 0);
        break;
      default:
        break;
    }
  }
  if (look.race === 'kyuuketsuki') {
    // 牙
    for (const s of [-1, 1]) {
      part(head, geo('cone', 0.025, 0.09, 4), '#ffffff', s * 0.06, -0.2, 0.42, false).rotation.x = Math.PI;
    }
  }
}

// しっぽ。体グループに付けて、戻り値を update で振る(人間とエルフは無し)
function addTail(body, look) {
  if (look.race === 'human' || look.race === 'elf' || look.race === 'kyuuketsuki') {
    return null;
  }
  const tail = new THREE.Group();
  tail.position.set(0, 0.16, -0.2);
  body.add(tail);
  const c = look.hair;
  switch (look.race) {
    case 'neko':
      // 細長く、先がくるっと上に向く
      for (let i = 0; i < 6; i++) {
        part(tail, geo('sph', 0.08 - i * 0.004, 8, 6), c, 0, 0.0175 * i * i, -0.05 - i * 0.1);
      }
      break;
    case 'akuma':
      // 細長くて、先がハート型のスペード
      for (let i = 0; i < 6; i++) {
        part(tail, geo('sph', 0.05, 8, 6), '#7a1428', 0, 0.012 * i * i, -0.05 - i * 0.1);
      }
      part(tail, geo('cone', 0.12, 0.2, 3), '#c0203a', 0, 0.45, -0.6).rotation.x = -1.5;
      break;
    case 'inu': {
      const t = part(tail, geo('cone', 0.1, 0.46, 6), c, 0, 0.14, -0.17);
      t.rotation.x = -0.9;
      break;
    }
    case 'kitsune': {
      const t = part(tail, geo('sph', 0.17, 12, 10), c, 0, 0.12, -0.3);
      t.scale.set(0.9, 0.9, 1.9);
      t.rotation.x = 0.5;
      const tip = part(tail, geo('sph', 0.12, 10, 8), '#ffffff', 0, 0.27, -0.56);
      tip.scale.set(0.9, 0.9, 1.3);
      tip.rotation.x = 0.5;
      break;
    }
    case 'usagi':
      part(tail, geo('sph', 0.13, 10, 8), '#ffffff', 0, 0.1, -0.2);
      break;
    default: {
      // 竜人: 太くて長いしっぽ
      const t = part(tail, geo('cone', 0.13, 0.75, 6), '#79c7a0', 0, 0.08, -0.36);
      t.rotation.x = -1.35;
      break;
    }
  }
  return tail;
}

// コウモリの羽(悪魔・吸血鬼)。update でぱたぱた動かす
function addWings(body) {
  return [-1, 1].map((s) => {
    const pivot = new THREE.Group();
    pivot.position.set(s * 0.14, 0.62, -0.26);
    body.add(pivot);
    const w = part(pivot, geo('cone', 0.24, 0.66, 3), '#3a1030', s * 0.3, 0.12, 0, false);
    w.scale.z = 0.12;
    w.rotation.z = -s * 1.0;
    return { pivot, s };
  });
}

// 職業ごとの小物(戦士=肩当て・胸当て / 魔法使い=額の宝石・マント / 僧侶=天使の輪・十字 / 盗賊=スカーフ / 弓使い=矢筒・羽根)
function addJobGear(body, head, look) {
  switch (look.job) {
    case 0:
      for (const s of [-1, 1]) {
        part(body, geo('sph', 0.17, 10, 8), '#aab2bf', s * 0.33, 0.8, 0);
      }
      part(body, geo('box', 0.36, 0.3, 0.06), '#c9d1db', 0, 0.5, 0.26, false);
      break;
    case 1:
      part(body, geo('box', 0.6, 0.72, 0.05), '#4a2f7a', 0, 0.42, -0.27, false);
      part(head, geo('torus', 0.45, 0.025, 6, 24), '#e9c46a', 0, 0.14, 0).rotation.x = Math.PI / 2 - 0.25;
      part(head, geo('sph', 0.06, 8, 6), '#6ad4ff', 0, 0.3, 0.41, false);
      break;
    case 2:
      part(head, geo('torus', 0.3, 0.03, 6, 24), '#f4d35e', 0, 0.8, -0.05, false).rotation.x = Math.PI / 2;
      part(body, geo('box', 0.07, 0.34, 0.04), '#f4d35e', 0, 0.5, 0.29, false);
      part(body, geo('box', 0.22, 0.07, 0.04), '#f4d35e', 0, 0.56, 0.29, false);
      break;
    case 3:
      part(body, geo('torus', 0.21, 0.08, 6, 14), '#2f6b4f', 0, 0.82, 0).rotation.x = Math.PI / 2;
      part(body, geo('box', 0.1, 0.28, 0.05), '#2f6b4f', 0.1, 0.66, 0.2, false);
      break;
    case 5:
      // 酒場の女主人: エプロンとフリルのカチューシャ
      part(body, geo('box', 0.42, 0.5, 0.05), '#ffffff', 0, 0.38, 0.28, false);
      part(body, geo('box', 0.5, 0.06, 0.06), '#ffffff', 0, 0.62, 0.27, false);
      part(head, geo('torus', 0.44, 0.04, 6, 24), '#ffffff', 0, 0.2, 0, false).rotation.x = Math.PI / 2 - 0.35;
      part(body, geo('sph', 0.07, 8, 6), '#e9c46a', 0, 0.78, 0.26, false);
      break;
    case 6:
      // 先生: 眼鏡とえんじ色のリボン
      for (const s of [-1, 1]) {
        part(head, geo('torus', 0.09, 0.015, 6, 16), '#2a2230', s * 0.15, 0.04, 0.44, false);
      }
      part(head, geo('box', 0.08, 0.015, 0.015), '#2a2230', 0, 0.05, 0.45, false);
      part(body, geo('cone', 0.09, 0.16, 4), '#8f2a3d', 0, 0.74, 0.3, false).rotation.x = Math.PI;
      break;
    default: {
      const q = part(body, geo('cyl', 0.08, 0.08, 0.5, 8), '#8a5a35', 0.2, 0.55, -0.3);
      q.rotation.z = 0.35;
      for (let i = 0; i < 3; i++) {
        part(q, geo('box', 0.02, 0.2, 0.02), ['#e56b6f', '#f4d35e', '#ffffff'][i], (i - 1) * 0.04, 0.34, 0, false);
      }
      part(head, geo('cone', 0.04, 0.3, 4), '#6a994e', 0.34, 0.4, 0, false).rotation.z = -0.9;
      break;
    }
  }
}

// overrides: NPC(酒場の女主人・先生)の見た目を決め打ちするための上書き
export function createAvatar({ id, color, skin = 'default', standing = false, scale = 1, overrides = {} }) {
  const look = lookFor(id, color);
  if (skin === 'makai') {
    Object.assign(look, { race: raceFor(id, RACES_MAKAI), eye: '#ff3a3a', skin: MAKAI_TONES[fnv1a(`${id}:ms`) % MAKAI_TONES.length] });
  }
  Object.assign(look, overrides);
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
  let tail = null;
  let wings = [];
  if (skin === 'isekai' || skin === 'makai') {
    addEars(head, look);
    tail = addTail(body, look);
    addJobGear(body, head, look);
    if (skin === 'makai' && (look.race === 'akuma' || look.race === 'kyuuketsuki')) {
      wings = addWings(body);
    }
  } else if (look.job >= 5) {
    addJobGear(body, head, look);
  }
  if (look.headphones && skin === 'default') {
    const band = part(head, geo('torus', 0.47, 0.035, 6, 20, Math.PI), '#2b2f3a', 0, 0.02, 0);
    band.rotation.y = Math.PI / 2;
    for (const s of [-1, 1]) {
      part(head, geo('cyl', 0.12, 0.12, 0.08, 12), '#2b2f3a', s * 0.46, 0, 0).rotation.z = Math.PI / 2;
    }
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
  // 脚(座っているときは前へ曲げる)。動き回るときは update で立ち姿に切り替える
  const legs = [];
  for (const s of [-1, 1]) {
    const leg = new THREE.Group();
    leg.position.set(s * 0.14, 0.05, 0);
    body.add(leg);
    const thigh = part(leg, geo('cap', 0.1, 0.3, 4, 8), '#3a3f4f', 0, -0.2, 0);
    let shin = null;
    if (!standing) {
      leg.rotation.x = -Math.PI / 2;
      shin = part(thigh, geo('cap', 0.09, 0.3, 4, 8), '#3a3f4f', 0, -0.22, 0.2);
      shin.rotation.x = Math.PI / 2;
    }
    legs.push({ leg, shin });
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
    body,
    arms,
    look,
    // state: working / thinking / waiting / sleeping
    // motion: 動き回るとき { mode: 'seated'|'walk'|'act', phase(歩きの位相), act('feed'|'sake'|'blood'), actT, carry }
    update(t, state, motion = null) {
      const moving = !standing && motion && motion.mode !== 'seated';
      const tt = t + phase;
      let e = 'normal';
      body.rotation.set(0, 0, 0);
      head.rotation.set(0, 0, 0);
      body.position.y = standing ? 0.75 : moving ? 0.55 : 0.66;
      if (!standing) {
        legs.forEach(({ leg, shin }, k) => {
          shin.visible = !moving;
          leg.rotation.x = !moving ? -Math.PI / 2 : motion.mode === 'walk' ? Math.sin(motion.phase + k * Math.PI) * 0.6 : 0;
        });
      }
      arms[0].rotation.set(0, 0, -0.15);
      arms[1].rotation.set(0, 0, 0.15);
      if (state === 'npc') {
        // NPC: にこにこ立って、ときどき手をふる
        e = 'smile';
        body.position.y += Math.sin(tt * 1.6) * 0.015;
        head.rotation.z = Math.sin(tt * 0.9) * 0.05;
        if (tt % 9 < 2) {
          arms[1].rotation.set(0, 0, 2.5 + Math.sin(tt * 8) * 0.35);
        }
      } else if (moving) {
        if (motion.mode === 'walk') {
          // 歩く: 腕を振る(物を持っているときは片手を前に)
          e = 'normal';
          body.position.y += Math.abs(Math.sin(motion.phase)) * 0.04;
          arms[0].rotation.x = Math.sin(motion.phase) * 0.5;
          arms[1].rotation.x = -Math.sin(motion.phase) * 0.5;
          if (motion.carry) {
            arms[1].rotation.x = -1.2;
          }
        } else {
          // 用事をする(金魚にエサ・お酒を取る・血を汲む)
          e = 'smile';
          const at = motion.actT || 0;
          if (motion.act === 'feed') {
            head.rotation.x = 0.2;
            arms[1].rotation.set(-1.3 + Math.sin(tt * 12) * 0.25, 0, 0.1);
            arms[0].rotation.x = -0.4;
          } else if (motion.act === 'sake') {
            arms[1].rotation.set(-1.5 + Math.sin(at * 2) * 0.15, 0, 0);
            arms[0].rotation.set(-1.3, 0, 0);
            head.rotation.x = -0.1;
          } else {
            const dip = Math.abs(Math.sin(at * 1.6));
            body.rotation.x = 0.25 + dip * 0.35;
            arms[0].rotation.x = -1.0 - dip * 0.6;
            arms[1].rotation.x = -1.0 - dip * 0.6;
            head.rotation.x = 0.2;
          }
          if (motion.carry) {
            arms[1].rotation.set(-1.2, 0, 0);
          }
        }
      } else if (standing) {
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
      // しっぽ: 待ちはぶんぶん・作業中はゆらゆら・寝てるときはだらん
      if (tail) {
        const amp = state === 'sleeping' ? 0.05 : state === 'working' ? 0.2 : 0.45;
        tail.rotation.y = Math.sin(tt * (state === 'waiting' ? 8 : 3)) * amp;
      }
      for (const w of wings) {
        w.pivot.rotation.y = w.s * (0.45 + Math.sin(tt * 4) * 0.25 * (state === 'sleeping' ? 0.2 : 1));
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
