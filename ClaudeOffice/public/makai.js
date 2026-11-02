// 魔界スキン: 赤い月と夕焼け色の空。黒い島を溶岩のひびが走り、黒い尖塔が囲む。
// 館は黒い壁に魔王軍の旗とステンドグラス、奥に魔王の玉座、長い晩餐テーブル。外に魔界の門と墓場
import * as THREE from 'three';
import { ROOM, island, clouds, lampLight, wallClock, calendar, archiveShelf, whiteboard, makeBird, propLabel, sparkles, SHELF_POS, BOARD_POS } from './world.js';
import { stainedGlass } from './tavern.js';

// 空のキーフレーム(上・中・下・太陽色・太陽の強さ・半球光・ランプ・雲・星)。昼は赤紫〜橙、夜は暗い赤紫
export const MAKAI_KEYS = [
  ['#12061c', '#3a0c34', '#6a1a30', '#ff9ab0', 0.4, 0.55, 1.0, '#3a1028', 1.0],
  ['#14081e', '#3e0e36', '#701c32', '#ff9ab0', 0.4, 0.55, 1.0, '#3e122a', 0.9],
  ['#3a0c44', '#a02450', '#ec6a3a', '#ff8a5a', 0.6, 0.6, 0.8, '#8a2a44', 0.3],
  ['#5a1658', '#c22c56', '#f27a40', '#ffa070', 0.85, 0.7, 0.6, '#a03a4c', 0.0],
  ['#641a5e', '#c8305a', '#f4823e', '#ffb088', 1.0, 0.8, 0.5, '#a8404e', 0.0],
  ['#641a5e', '#c42e58', '#f27c3e', '#ffa880', 0.95, 0.78, 0.5, '#a43e4e', 0.0],
  ['#541654', '#c02a50', '#f06a36', '#ff8a52', 0.85, 0.7, 0.7, '#9a3448', 0.0],
  ['#36103e', '#962248', '#de5034', '#ff7a4a', 0.7, 0.6, 0.9, '#7a2840', 0.2],
  ['#1c0a28', '#4e1238', '#8a2234', '#ff9ab0', 0.45, 0.55, 1.0, '#401430', 0.8],
  ['#12061c', '#3a0c34', '#6a1a30', '#ff9ab0', 0.4, 0.55, 1.0, '#3a1028', 1.0],
];

const BONE = '#e8dfc8';
const IRON = '#2a2630';
const WALL = '#2a2030';

// ドクロ(頭蓋・あご・眼窩・鼻)。eyes=true で眼窩が赤く光る
export function skull(kit, parent, x, y, z, s = 1, rotY = 0, eyes = false) {
  const g = new THREE.Group();
  g.position.set(x, y, z);
  g.rotation.y = rotY;
  g.scale.setScalar(s);
  kit.sph(g, 0.28, BONE, 0, 0, 0);
  kit.box(g, 0.26, 0.12, 0.22, BONE, 0, -0.23, 0.05);
  kit.box(g, 0.22, 0.03, 0.02, '#2a2230', 0, -0.23, 0.17);
  for (const sx of [-1, 1]) {
    const socket = new THREE.Mesh(kit.geom('sph', 0.075, 8, 6), new THREE.MeshBasicMaterial({ color: eyes ? '#ff3a2a' : '#120a14', toneMapped: false }));
    socket.position.set(sx * 0.1, -0.01, 0.2);
    g.add(socket);
  }
  const nose = new THREE.Mesh(kit.geom('cone', 0.03, 0.07, 3), kit.mat('#120a14'));
  nose.position.set(0, -0.1, 0.27);
  nose.rotation.x = Math.PI;
  g.add(nose);
  parent.add(g);
  return g;
}

// 島の上面: 黒い岩肌に、光る溶岩のひび(map と emissiveMap を同じ絵から作る)
function lavaGround() {
  const N = 1024;
  const make = (base) => {
    const cv = document.createElement('canvas');
    cv.width = cv.height = N;
    const c = cv.getContext('2d');
    c.fillStyle = base;
    c.fillRect(0, 0, N, N);
    return { cv, c };
  };
  const albedo = make('#1d1922');
  const glow = make('#000000');
  // ざらつき
  for (let i = 0; i < 900; i++) {
    const x = (i * 397) % N;
    const y = (i * 631) % N;
    albedo.c.fillStyle = i % 2 ? 'rgba(255,255,255,0.025)' : 'rgba(0,0,0,0.12)';
    albedo.c.fillRect(x, y, 6 + (i % 9), 4 + (i % 5));
  }
  // 中心(館の下)を避けて、外側の輪に枝分かれするひびを描く
  let seed = 7;
  const rnd = () => {
    seed = (seed * 16807) % 2147483647;
    return seed / 2147483647;
  };
  const cracks = [];
  for (let k = 0; k < 16; k++) {
    const a = (k / 16) * Math.PI * 2 + rnd() * 0.3;
    const r0 = 0.5 + rnd() * 0.12;
    let x = 512 + Math.cos(a) * r0 * 512;
    let y = 512 + Math.sin(a) * r0 * 512;
    let dir = a + (rnd() - 0.5) * 1.6;
    const pts = [[x, y]];
    const len = 6 + Math.floor(rnd() * 6);
    for (let s = 0; s < len; s++) {
      dir += (rnd() - 0.5) * 0.9;
      x += Math.cos(dir) * 26;
      y += Math.sin(dir) * 26;
      if (Math.hypot(x - 512, y - 512) > 490) {
        break;
      }
      pts.push([x, y]);
      if (rnd() < 0.18) {
        const b = [[x, y]];
        let bx = x;
        let by = y;
        let bd = dir + (rnd() < 0.5 ? 1 : -1) * 0.9;
        for (let q = 0; q < 3; q++) {
          bd += (rnd() - 0.5) * 0.6;
          bx += Math.cos(bd) * 20;
          by += Math.sin(bd) * 20;
          b.push([bx, by]);
        }
        cracks.push({ pts: b, w: 0.6 });
      }
    }
    cracks.push({ pts, w: 1 });
  }
  for (const { c } of [albedo, glow]) {
    for (const [width, color, blur] of [[16, 'rgba(255,70,10,0.55)', 18], [7, '#ff7a1a', 6], [2.5, '#ffd27a', 0]]) {
      c.lineCap = 'round';
      c.lineJoin = 'round';
      c.shadowColor = color;
      c.shadowBlur = blur;
      c.strokeStyle = color;
      for (const cr of cracks) {
        c.lineWidth = width * cr.w;
        c.beginPath();
        cr.pts.forEach(([x, y], i) => (i ? c.lineTo(x, y) : c.moveTo(x, y)));
        c.stroke();
      }
    }
  }
  const tex = (cv) => {
    const t = new THREE.CanvasTexture(cv);
    t.colorSpace = THREE.SRGBColorSpace;
    t.anisotropy = 4;
    return t;
  };
  return new THREE.MeshStandardMaterial({ map: tex(albedo.cv), emissiveMap: tex(glow.cv), emissive: '#ffffff', emissiveIntensity: 1.6, roughness: 0.95 });
}

// 黒い床に紫の格子と、晩餐の席を囲む赤い縁取り
function gridFloor(R) {
  const w = R.x1 - R.x0;
  const d = R.z1 - R.z0;
  const ppu = 56;
  const cv = document.createElement('canvas');
  cv.width = w * ppu;
  cv.height = d * ppu;
  const c = cv.getContext('2d');
  c.fillStyle = '#17121c';
  c.fillRect(0, 0, cv.width, cv.height);
  c.strokeStyle = '#3a2c52';
  c.lineWidth = 2;
  for (let x = 0; x <= w; x++) {
    c.beginPath();
    c.moveTo(x * ppu, 0);
    c.lineTo(x * ppu, cv.height);
    c.stroke();
  }
  for (let z = 0; z <= d; z++) {
    c.beginPath();
    c.moveTo(0, z * ppu);
    c.lineTo(cv.width, z * ppu);
    c.stroke();
  }
  const px = (x) => (x - R.x0) * ppu;
  const pz = (z) => (z - R.z0) * ppu;
  c.strokeStyle = '#c4243a';
  c.lineWidth = 5;
  c.strokeRect(px(-10.8), pz(-8.2), px(0.2) - px(-10.8), pz(2.0) - pz(-8.2));
  c.lineWidth = 2;
  c.strokeRect(px(-10.5), pz(-7.9), px(-0.1) - px(-10.5), pz(1.7) - pz(-7.9));
  c.fillStyle = '#c4243a';
  for (let x = -10.2; x < 0; x += 0.6) {
    c.fillRect(px(x), pz(-8.05) - 3, 10, 6);
    c.fillRect(px(x), pz(1.85) - 3, 10, 6);
  }
  const t = new THREE.CanvasTexture(cv);
  t.colorSpace = THREE.SRGBColorSpace;
  t.anisotropy = 4;
  return t;
}

// 魔王軍の旗(角のあるドクロの紋章と「魔」)
function demonBanner() {
  const cv = document.createElement('canvas');
  cv.width = 128;
  cv.height = 320;
  const c = cv.getContext('2d');
  c.beginPath();
  c.moveTo(0, 0);
  c.lineTo(128, 0);
  c.lineTo(128, 280);
  c.lineTo(64, 320);
  c.lineTo(0, 280);
  c.closePath();
  const grad = c.createLinearGradient(0, 0, 0, 320);
  grad.addColorStop(0, '#2a0a12');
  grad.addColorStop(1, '#14060c');
  c.fillStyle = grad;
  c.fill();
  c.lineWidth = 8;
  c.strokeStyle = '#d8a84a';
  c.stroke();
  c.fillStyle = '#d8a84a';
  c.strokeStyle = '#d8a84a';
  // 角
  c.lineWidth = 9;
  c.beginPath();
  c.arc(64, 96, 42, Math.PI * 1.05, Math.PI * 1.4);
  c.stroke();
  c.beginPath();
  c.arc(64, 96, 42, Math.PI * 1.6, Math.PI * 1.95);
  c.stroke();
  // ドクロ
  c.beginPath();
  c.arc(64, 100, 24, 0, Math.PI * 2);
  c.fill();
  c.fillRect(50, 112, 28, 20);
  c.fillStyle = '#14060c';
  c.beginPath();
  c.arc(55, 100, 6, 0, Math.PI * 2);
  c.arc(73, 100, 6, 0, Math.PI * 2);
  c.fill();
  c.fillStyle = '#d8a84a';
  c.font = 'bold 64px "Yu Mincho", "MS Mincho", serif';
  c.textAlign = 'center';
  c.fillText('魔', 64, 220);
  const t = new THREE.CanvasTexture(cv);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

// 渦を巻く門の中身
function swirlTexture() {
  const cv = document.createElement('canvas');
  cv.width = cv.height = 256;
  const c = cv.getContext('2d');
  const g = c.createRadialGradient(128, 128, 4, 128, 128, 128);
  g.addColorStop(0, '#ffd8ff');
  g.addColorStop(0.35, '#9a3ae8');
  g.addColorStop(1, '#2a0a50');
  c.fillStyle = g;
  c.fillRect(0, 0, 256, 256);
  c.strokeStyle = 'rgba(255, 210, 255, 0.75)';
  c.lineWidth = 6;
  for (let arm = 0; arm < 3; arm++) {
    c.beginPath();
    for (let k = 0; k < 120; k++) {
      const a = (k / 120) * Math.PI * 3.2 + (arm * Math.PI * 2) / 3;
      const r = (k / 120) * 120;
      c.lineTo(128 + Math.cos(a) * r, 128 + Math.sin(a) * r);
    }
    c.stroke();
  }
  const t = new THREE.CanvasTexture(cv);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

// 晩餐テーブル2卓(各6席。奥側と手前側に3席ずつ向かい合う)
const TABLES = [{ x: -5.6, z: -5.4 }, { x: -5.6, z: -0.6 }];
export const MAKAI_SEATS = TABLES.flatMap((t) => [-2.6, 0, 2.6].flatMap((dx) => [
  { x: t.x + dx, z: t.z - 1.15, rot: 0, table: t },
  { x: t.x + dx, z: t.z + 1.15, rot: Math.PI, table: t },
]));

export function buildMakai(ctx) {
  const { kit, root, lights } = ctx;
  const R = ROOM;
  const flames = [];
  const flameMat = new THREE.MeshBasicMaterial({ color: new THREE.Color('#ffc04a').multiplyScalar(1.7), toneMapped: false });
  const flame = (parent, x, y, z, s = 1) => {
    const f = new THREE.Mesh(kit.geom('cone', 0.06 * s, 0.2 * s, 6), flameMat);
    f.position.set(x, y, z);
    parent.add(f);
    flames.push({ f, ph: Math.random() * 6.3 });
  };
  const candle = (parent, x, y, z, h = 0.25) => {
    kit.cyl(parent, 0.05, 0.055, h, '#efe4c8', x, y + h / 2, z, 8);
    flame(parent, x, y + h + 0.1, z);
  };
  const candelabra = (parent, x, y, z) => {
    kit.cyl(parent, 0.14, 0.18, 0.06, '#b8902a', x, y + 0.03, z, 10);
    kit.cyl(parent, 0.03, 0.03, 0.42, '#b8902a', x, y + 0.24, z, 6);
    kit.box(parent, 0.5, 0.03, 0.03, '#b8902a', x, y + 0.44, z);
    for (const dx of [-0.24, 0, 0.24]) {
      candle(parent, x + dx, y + 0.45 + (dx ? 0 : 0.08), z, 0.18);
    }
  };

  // ---- 島: 黒い岩肌に溶岩のひび、光る縁、手前に溶岩の滝
  island(kit, root, 40, 34, { shape: 'round', topMat: lavaGround(), edge: '#1a1418', soil: '#241a20', rock: '#2e2228' });
  const rimMat = new THREE.MeshBasicMaterial({ color: new THREE.Color('#ff6a1a').multiplyScalar(1.5), toneMapped: false });
  const rim = new THREE.Mesh(new THREE.CylinderGeometry(1, 1, 0.1, 96, 1, true), rimMat);
  rim.scale.set(20.03, 1, 17.03);
  rim.position.y = -0.03;
  root.add(rim);
  const fallTex = (() => {
    const cv = document.createElement('canvas');
    cv.width = 32;
    cv.height = 256;
    const c = cv.getContext('2d');
    for (let y = 0; y < 256; y += 8) {
      c.fillStyle = y % 32 < 16 ? '#ffb040' : '#ff5a10';
      c.fillRect(0, y, 32, 8);
      c.fillStyle = '#ffe0a0';
      c.fillRect((y * 7) % 28, y, 4, 8);
    }
    const t = new THREE.CanvasTexture(cv);
    t.colorSpace = THREE.SRGBColorSpace;
    t.wrapS = t.wrapT = THREE.RepeatWrapping;
    return t;
  })();
  const fallA = Math.PI * 0.3;
  const fall = new THREE.Mesh(new THREE.PlaneGeometry(1.6, 9), new THREE.MeshBasicMaterial({ map: fallTex, toneMapped: false, color: new THREE.Color(1.3, 1.3, 1.3), side: THREE.DoubleSide }));
  fall.position.set(Math.cos(fallA) * 20.1, -4.5, Math.sin(fallA) * 17.1);
  fall.lookAt(Math.cos(fallA) * 40, -4.5, Math.sin(fallA) * 34);
  root.add(fall);

  // ---- 館
  const room = new THREE.Group();
  root.add(room);
  const cx = (R.x0 + R.x1) / 2;
  const cz = (R.z0 + R.z1) / 2;
  kit.box(room, R.x1 - R.x0 + 0.4, 0.14, R.z1 - R.z0 + 0.4, '#0e0a12', cx - 0.2, 0.07, cz - 0.2);
  const floor = new THREE.Mesh(new THREE.PlaneGeometry(R.x1 - R.x0, R.z1 - R.z0), kit.mat('#ffffff', { map: gridFloor(R) }));
  floor.rotation.x = -Math.PI / 2;
  floor.position.set(cx, 0.142, cz);
  floor.receiveShadow = true;
  room.add(floor);
  // 壁(黒)と柱、上の縁の紫の光
  kit.box(room, R.x1 - R.x0 + 0.4, 5.2, 0.4, WALL, cx - 0.2, 2.6, R.z0 - 0.2);
  kit.box(room, 0.4, 5.2, R.z1 - R.z0, WALL, R.x0 - 0.2, 2.6, cz);
  kit.box(room, R.x1 - R.x0 + 0.6, 0.3, 0.6, '#16101a', cx - 0.3, 5.3, R.z0 - 0.2);
  kit.box(room, 0.6, 0.3, R.z1 - R.z0 + 0.1, '#16101a', R.x0 - 0.2, 5.3, cz);
  const edgeMat = new THREE.MeshBasicMaterial({ color: new THREE.Color('#b05aff').multiplyScalar(1.2), toneMapped: false });
  const e1 = new THREE.Mesh(kit.geom('box', R.x1 - R.x0 + 0.6, 0.05, 0.05), edgeMat);
  e1.position.set(cx - 0.3, 5.47, R.z0 + 0.1);
  room.add(e1);
  const e2 = new THREE.Mesh(kit.geom('box', 0.05, 0.05, R.z1 - R.z0 + 0.1), edgeMat);
  e2.position.set(R.x0 + 0.1, 5.47, cz);
  room.add(e2);
  for (const x of [R.x0 + 0.15, -7.0, -4.0, 0.3, 4.0, R.x1 - 0.15]) {
    kit.box(room, 0.3, 5.2, 0.2, '#352a3c', x, 2.6, R.z0 + 0.1);
  }
  for (const z of [R.z0 + 0.15, -3.6, R.z1 - 0.15]) {
    kit.box(room, 0.2, 5.2, 0.3, '#352a3c', R.x0 + 0.1, 2.6, z);
  }
  // ステンドグラス・旗・時計・カレンダー
  const glass = ['#7a2ad8', '#c42a8a', '#e8443a', '#f39c34', '#3a4ad8', '#5a1ab0'];
  stainedGlass(kit, room, -10.3, 3.0, R.z0 + 0.02, 1.3, 2.6, 0, glass, '#120c16', 1.1);
  stainedGlass(kit, room, -1.2, 3.0, R.z0 + 0.02, 1.3, 2.6, 0, glass, '#120c16', 1.1);
  stainedGlass(kit, room, 5.0, 3.0, R.z0 + 0.02, 1.3, 2.6, 0, glass, '#120c16', 1.1);
  stainedGlass(kit, room, R.x0 + 0.02, 3.0, 1.9, 1.2, 2.4, Math.PI / 2, glass, '#120c16', 1.1);
  const bannerTex = demonBanner();
  for (const bx of [-8.6, -2.6, 2.4]) {
    const b = new THREE.Mesh(new THREE.PlaneGeometry(1.0, 2.5), new THREE.MeshLambertMaterial({ map: bannerTex, transparent: true, alphaTest: 0.5 }));
    b.position.set(bx, 3.2, R.z0 + 0.05);
    room.add(b);
    kit.box(room, 1.2, 0.07, 0.07, '#b8902a', bx, 4.48, R.z0 + 0.08);
  }
  ctx.clock = wallClock(kit, room, 0.6, 3.9, R.z0 + 0.25, 0, '#b8902a');
  ctx.calendar = calendar(room, 0.6, 2.2, R.z0 + 0.25, 0);
  // 魔王の玉座(段の上、角飾りつき)と、そこから伸びる赤い絨毯
  const th = new THREE.Group();
  th.position.set(-5.6, 0, R.z0 + 1.1);
  kit.box(th, 3.4, 0.25, 1.9, '#2e2436', 0, 0.27, 0);
  kit.box(th, 2.6, 0.25, 1.4, '#3a2e44', 0, 0.52, -0.1);
  kit.box(th, 1.3, 0.5, 0.9, '#2a1a20', 0, 0.9, -0.05);
  kit.box(th, 1.1, 0.12, 0.8, '#a01a2c', 0, 1.2, 0);
  kit.box(th, 1.5, 3.4, 0.25, '#1e141e', 0, 2.3, -0.5);
  kit.box(th, 1.1, 2.8, 0.05, '#a01a2c', 0, 2.3, -0.36);
  for (const s of [-1, 1]) {
    kit.box(th, 0.2, 0.6, 0.9, '#1e141e', s * 0.7, 1.35, -0.05);
    // 角: 外へ張り出してから上へ反る(円錐を3つつなぐ)
    [[0.17, 0.5, 0.95, 4.05, -1.1], [0.12, 0.45, 1.25, 4.4, -0.5], [0.07, 0.4, 1.35, 4.8, 0.1]].forEach(([r, h, x, y, rz]) => {
      const seg = new THREE.Mesh(kit.geom('cone', r, h, 8), kit.mat('#efe6d0'));
      seg.position.set(s * x * 0.85, y - 0.1, -0.5);
      seg.rotation.z = s * rz;
      th.add(seg);
    });
  }
  skull(kit, th, 0, 4.15, -0.4, 0.8, 0, true);
  room.add(th);
  propLabel(ctx, room, '👑 魔王の玉座', -5.6, 5.4, R.z0 + 1.0);
  kit.box(room, 1.6, 0.02, 9.6, '#8a1424', -5.6, 0.152, -3.4);
  kit.box(room, 1.8, 0.015, 9.6, '#c8a040', -5.6, 0.148, -3.4);
  // 晩餐テーブルと椅子。席の前に魔眼の水晶(状態で色が変わる)
  ctx.glows = [];
  for (const t of TABLES) {
    kit.box(room, 8.6, 0.12, 1.4, '#2e1c22', t.x, 1.0, t.z);
    kit.box(room, 8.8, 0.02, 0.8, '#9a1a2c', t.x, 1.07, t.z);
    kit.box(room, 8.8, 0.025, 0.86, '#c8a040', t.x, 1.065, t.z);
    for (const dx of [-4.0, 4.0]) {
      for (const dz of [-0.5, 0.5]) {
        kit.box(room, 0.14, 0.95, 0.14, '#1a1016', t.x + dx, 0.5, t.z + dz);
      }
    }
    for (const dx of [-1.3, 1.3]) {
      candelabra(room, t.x + dx, 1.08, t.z);
    }
    skull(kit, room, t.x, 1.3, t.z, 0.5, Math.PI / 4);
  }
  MAKAI_SEATS.forEach((s) => {
    const dir = s.rot === 0 ? 1 : -1;
    const ch = new THREE.Group();
    ch.position.set(s.x, 0, s.z);
    ch.rotation.y = s.rot;
    kit.box(ch, 0.8, 0.1, 0.75, '#1e141e', 0, 0.58, 0);
    kit.box(ch, 0.7, 0.06, 0.65, '#8a1a2c', 0, 0.65, 0);
    kit.box(ch, 0.8, 1.6, 0.1, '#1e141e', 0, 1.3, -0.38);
    kit.box(ch, 0.6, 1.1, 0.04, '#8a1a2c', 0, 1.35, -0.32);
    const tip = new THREE.Mesh(kit.geom('cone', 0.09, 0.3, 4), kit.mat('#1e141e'));
    for (const lx of [-0.36, 0.36]) {
      const k = tip.clone();
      k.position.set(lx, 2.2, -0.38);
      ch.add(k);
      kit.box(ch, 0.07, 0.56, 0.07, '#1e141e', lx, 0.28, 0.3);
    }
    room.add(ch);
    const ox = s.x;
    const oz = s.table.z - dir * 0.35;
    kit.cyl(room, 0.08, 0.12, 0.08, '#b8902a', ox, 1.11, oz, 10);
    const orb = new THREE.Mesh(new THREE.SphereGeometry(0.14, 16, 12), new THREE.MeshBasicMaterial({ color: '#3a3f55', toneMapped: false }));
    orb.position.set(ox, 1.28, oz);
    room.add(orb);
    const halo = new THREE.Mesh(new THREE.TorusGeometry(0.19, 0.012, 6, 24), new THREE.MeshBasicMaterial({ color: '#ff3a5a', toneMapped: false }));
    halo.position.set(ox, 1.28, oz);
    halo.rotation.x = Math.PI / 2;
    room.add(halo);
    ctx.glows.push({ screen: orb, logo: halo });
    kit.cyl(room, 0.2, 0.18, 0.03, '#c8c0d0', ox + 0.6, 1.08, oz, 14);
    kit.cyl(room, 0.06, 0.03, 0.22, '#b8902a', ox - 0.55, 1.18, oz, 8);
  });
  // 闇の魔水晶(右奥)
  const crystal = new THREE.Group();
  crystal.position.set(4.4, 0, -7.2);
  kit.cyl(crystal, 0.55, 0.7, 0.3, '#2e2436', 0, 0.15, 0, 8);
  kit.cyl(crystal, 0.2, 0.3, 1.0, '#2e2436', 0, 0.8, 0, 8);
  kit.cyl(crystal, 0.45, 0.25, 0.2, '#b8902a', 0, 1.35, 0, 8);
  const gem = new THREE.Mesh(kit.geom('oct', 0.55, 0), new THREE.MeshStandardMaterial({ color: '#c86aff', emissive: '#9a2aff', emissiveIntensity: 1.4, flatShading: true, roughness: 0.2 }));
  gem.scale.y = 1.9;
  gem.position.y = 2.6;
  crystal.add(gem);
  room.add(crystal);
  propLabel(ctx, room, '💎 闇の魔水晶', 4.4, 4.4, -7.2);
  const gemLight = lampLight(room, lights, 4.4, 2.6, -6.4, '#c070ff', 10, 6);
  // 背の高い燭台(四隅)
  for (const [lx, lz] of [[-11.0, -9.0], [-11.0, 2.3], [2.0, -9.0], [1.6, 2.3]]) {
    kit.cyl(room, 0.25, 0.3, 0.15, IRON, lx, 0.22, lz, 8);
    kit.cyl(room, 0.05, 0.05, 2.4, IRON, lx, 1.4, lz, 6);
    kit.cyl(room, 0.3, 0.1, 0.12, '#b8902a', lx, 2.6, lz, 8);
    for (let k = 0; k < 3; k++) {
      const a = (k / 3) * Math.PI * 2;
      candle(room, lx + Math.cos(a) * 0.15, 2.66, lz + Math.sin(a) * 0.15, 0.22);
    }
  }
  const hall = lampLight(room, lights, -5.6, 3.6, -3.0, '#ffb070', 16, 6);
  // 禁書庫(アーカイブ)・魔王軍の掲示板
  ctx.shelf = archiveShelf(kit, room, '#2a1a30');
  propLabel(ctx, room, '📕 禁書庫', SHELF_POS.x + 0.3, 4.9, SHELF_POS.z);
  ctx.board = whiteboard(kit, room, '#2a1a30', '#e8dcc8');
  propLabel(ctx, room, '📜 魔王軍の掲示板', BOARD_POS.x + 0.3, 4.3, BOARD_POS.z);

  // ---- 外: 黒い尖塔・枯れ木・墓場・魔界の門・溶岩の池と橋・骨
  const spireMat = kit.mat('#141018', { flatShading: true });
  [
    [-19, -3, 8], [-17, -9, 9], [-13, -14, 7], [-6, -16, 8.5], [2, -16.5, 6], [9, -14, 7.5], [15, -10, 6], [18, -4, 5],
    [-19, 5, 5], [-16, 10, 4], [18, 5, 3.5], [14, 11, 3], [-9, 15.5, 3], [9, 15, 2.6], [-1, 16.5, 2.4],
  ].forEach(([x, z, h], i) => {
    const sp = new THREE.Mesh(kit.geom('cone', 0.55 + (i % 3) * 0.15, h, 5), spireMat);
    sp.position.set(x, h / 2, z);
    sp.castShadow = true;
    root.add(sp);
    if (h > 5) {
      const s2 = new THREE.Mesh(kit.geom('cone', 0.35, h * 0.55, 5), spireMat);
      s2.position.set(x + 0.8, h * 0.27, z + 0.5);
      root.add(s2);
    }
  });
  const deadTree = (x, z, s = 1) => {
    kit.cyl(root, 0.14 * s, 0.26 * s, 3.0 * s, '#1a1218', x, 1.5 * s, z, 6);
    for (const [bx, by, rz, len] of [[0.5, 2.1, -0.9, 1.3], [-0.5, 2.5, 0.9, 1.1], [0.1, 2.9, -0.4, 0.9]]) {
      const b = kit.cyl(root, 0.04 * s, 0.09 * s, len * s, '#1a1218', x + bx * s * 0.7, by * s, z, 5);
      b.rotation.z = rz;
    }
  };
  [[-15, -6, 1.1], [13, -6, 1.2], [11, 8.5, 1.0], [-6, 12.5, 1.1], [5.5, 12.0, 0.9], [16, 0, 1.0]].forEach(([x, z, s]) => deadTree(x, z, s));
  // 墓場(左手前)
  [[-15.8, 1.5, 0], [-14.6, 4.2, 1], [-16.2, 6.2, 0], [-8.2, 11.2, 1], [-10.5, 12.0, 0]].forEach(([x, z, cross], i) => {
    const lean = (((i * 37) % 9) - 4) * 0.04;
    if (cross) {
      kit.box(root, 0.2, 1.4, 0.2, '#4a4252', x, 0.7, z).rotation.z = lean;
      kit.box(root, 0.8, 0.2, 0.2, '#4a4252', x, 1.05, z).rotation.z = lean;
    } else {
      kit.box(root, 0.8, 1.0, 0.24, '#4a4252', x, 0.5, z).rotation.z = lean;
      kit.cyl(root, 0.4, 0.4, 0.24, '#4a4252', x, 1.0, z, 12).rotation.x = Math.PI / 2;
    }
  });
  // 魔界の門(石のアーチと紫の渦)
  const gate = new THREE.Group();
  gate.position.set(-11.2, 0, 7.4);
  gate.rotation.y = Math.PI / 4;
  for (const s of [-1, 1]) {
    kit.box(gate, 0.6, 3.4, 0.6, '#2e2436', s * 1.6, 1.7, 0);
    skull(kit, gate, s * 1.6, 3.65, 0.1, 0.6, 0);
  }
  kit.box(gate, 3.8, 0.5, 0.7, '#2e2436', 0, 3.4, 0);
  const swirl = new THREE.Mesh(new THREE.CircleGeometry(1.35, 40), new THREE.MeshBasicMaterial({ map: swirlTexture(), toneMapped: false, color: new THREE.Color(1.25, 1.25, 1.25), side: THREE.DoubleSide }));
  swirl.position.set(0, 1.65, 0);
  gate.add(swirl);
  const ringG = new THREE.Mesh(kit.geom('torus', 1.4, 0.1, 8, 40), new THREE.MeshBasicMaterial({ color: new THREE.Color('#e0a0ff').multiplyScalar(1.3), toneMapped: false }));
  ringG.position.set(0, 1.65, 0);
  gate.add(ringG);
  root.add(gate);
  propLabel(ctx, gate, '🌀 魔界の門', 0, 4.4, 0);
  // 溶岩の池と、渡した木の橋
  const pool = new THREE.Mesh(new THREE.CircleGeometry(1, 40), new THREE.MeshBasicMaterial({ color: new THREE.Color('#ffb040').multiplyScalar(1.3), toneMapped: false }));
  pool.rotation.x = -Math.PI / 2;
  pool.scale.set(3.0, 2.0, 1);
  pool.position.set(3.0, 0.03, 9.5);
  root.add(pool);
  for (let i = 0; i < 12; i++) {
    const a = (i / 12) * Math.PI * 2;
    const r = kit.sph(root, 0.45 + (i % 3) * 0.12, '#1e181e', 3.0 + Math.cos(a) * 3.1, 0.1, 9.5 + Math.sin(a) * 2.1);
    r.scale.y = 0.5;
  }
  for (let i = 0; i < 9; i++) {
    const p = kit.box(root, 0.5, 0.1, 1.5, '#6a4428', -0.4 + i * 0.75, 0.35 + Math.sin((i / 8) * Math.PI) * 0.25, 9.5);
    p.rotation.z = ((i * 13) % 5 - 2) * 0.02;
  }
  for (const s of [-1, 1]) {
    kit.box(root, 6.8, 0.06, 0.06, '#3a2a20', 2.6, 0.95, 9.5 + s * 0.7);
  }
  const lavaLight = lampLight(root, lights, 3.0, 1.5, 9.5, '#ff8a3a', 10, 6);
  // 骨とドクロ
  for (let i = 0; i < 16; i++) {
    const a = i * 2.39;
    const r = 11 + (i % 4) * 1.6;
    const bx = Math.cos(a) * r * 1.05;
    const bz = Math.sin(a) * r * 0.9;
    if (bx > -13 && bx < 7 && bz > -11 && bz < 4) {
      continue;
    }
    if (i % 3 === 0) {
      skull(kit, root, bx, 0.22, bz, 0.7, a);
    } else {
      kit.box(root, 0.5, 0.07, 0.08, BONE, bx, 0.05, bz).rotation.y = a;
      kit.sph(root, 0.07, BONE, bx + Math.cos(a) * 0.25, 0.07, bz - Math.sin(a) * 0.25);
    }
  }
  // 遠くの浮島(尖塔つき)
  for (const [ix, iy, iz, s] of [[-34, 6, -26, 1.0], [24, 10, -32, 1.2]]) {
    const g = new THREE.Group();
    g.position.set(ix, iy, iz);
    g.scale.setScalar(s);
    island(kit, g, 8, 7, { shape: 'round', top: '#1d1922', edge: '#1d1922', soil: '#241a20', rock: '#2e2228' });
    const sp = new THREE.Mesh(kit.geom('cone', 0.6, 4, 5), spireMat);
    sp.position.y = 2;
    g.add(sp);
    root.add(g);
  }
  // コウモリ(館の上をぐるぐる)
  const bats = [0, 1, 2, 3].map((i) => {
    const b = makeBird(kit, '#1a1020');
    b.g.scale.setScalar(0.75);
    root.add(b.g);
    return { ...b, i, r: 7 + i * 2.4, h: 7 + (i % 3) * 1.2, sp: 0.35 + i * 0.07 };
  });
  // 「標準」モードだけの演出: 舞い上がる火の粉と、ただよう鬼火
  const fx = new THREE.Group();
  root.add(fx);
  const embers = sparkles(fx, { count: 70, box: [-18, 16, 0.2, 9, -14, 15], colors: ['#ff8a3a', '#ffb050', '#ff4a2a', '#ffd27a'], size: 4, mode: 'rise' });
  const wisps = sparkles(fx, { count: 14, box: [-17, 14, 0.8, 3, -12, 14], colors: ['#7affa0', '#5aff8a'], size: 14, speed: 0.6 });
  ctx.fancy = {
    group: fx,
    update(t) {
      embers.update(t);
      wisps.update(t);
    },
  };

  ctx.seats = MAKAI_SEATS;
  ctx.nav = {
    ...R,
    rects: [...TABLES.map((t) => [t.x - 4.3, t.x + 4.3, t.z - 0.7, t.z + 0.7]), [-7.4, -3.8, R.z0, R.z0 + 2.1], [R.x0, R.x0 + 1.0, -8.8, -4.2], [-0.3, 1.5, R.z0, R.z0 + 0.6]],
    circles: [[4.4, -7.2, 0.8], [-11.0, -9.0, 0.35], [-11.0, 2.3, 0.35], [2.0, -9.0, 0.35], [1.6, 2.3, 0.35]],
  };
  ctx.cloudsObj = clouds(kit, root, '#5a1a30', 3);
  ctx.keys = MAKAI_KEYS;
  ctx.hemi = { sky: '#ffb8d0', ground: '#7a3a70', boost: 2.1 };
  ctx.sky = { aurora: 0, crack: 0, moon2: false, moonColor: '#ffc0a0', big: { x: 0.7, y: 0.8, r: 0.13, color: '#ff6a3a', always: true } };
  ctx.panel = 'makai';
  ctx.update = (t) => {
    for (const l of [gemLight, hall, lavaLight]) {
      l.intensity = Math.max(l.intensity, l.userData.power * 0.7) * (1 + Math.sin(t * 9 + l.position.x) * 0.08 + Math.sin(t * 23 + l.position.z) * 0.05);
    }
    for (const f of flames) {
      f.f.scale.set(1 + Math.sin(t * 12 + f.ph) * 0.15, 1 + Math.sin(t * 9 + f.ph * 2) * 0.25, 1);
    }
    fallTex.offset.y = t * 0.35;
    swirl.rotation.z = -t * 0.9;
    gem.rotation.y = t * 0.5;
    gem.position.y = 2.6 + Math.sin(t * 1.2) * 0.12;
    rimMat.color.set('#ff6a1a').multiplyScalar(1.3 + Math.sin(t * 1.3) * 0.2);
    pool.material.color.set('#ffb040').multiplyScalar(1.2 + Math.sin(t * 1.7) * 0.15);
    bats.forEach((b) => {
      const at = (u) => [-3 + Math.cos(u * b.sp + b.i * 1.3) * b.r, -3 + Math.sin((u * b.sp + b.i * 1.3) * 1.2) * b.r * 0.7];
      const [x0, z0] = at(t);
      const [x1, z1] = at(t + 0.1);
      b.g.position.set(x0, b.h + Math.sin(t * 1.7 + b.i) * 0.6, z0);
      b.g.rotation.y = Math.atan2(x1 - x0, z1 - z0);
      const flap = Math.sin(t * 16 + b.i) * 0.9;
      for (const w of b.wings) {
        w.pivot.rotation.z = w.s * flap;
      }
    });
  };
}
