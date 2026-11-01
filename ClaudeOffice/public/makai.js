// 魔界スキン: ロウソクとドクロの不気味な館。赤黒い空、血の月、溶岩、墓場
import * as THREE from 'three';
import { CLASSROOM_SEATS, island, clouds, lampLight, wallClock, archiveShelf, whiteboard, makeBird } from './world.js';

// 空のキーフレーム(上・中・下・太陽色・太陽の強さ・半球光・ランプ・雲・星)。昼でも赤黒く、夜は星が濃い
export const MAKAI_KEYS = [
  ['#0a0412', '#240a30', '#4a1438', '#ff9ab0', 0.35, 0.5, 1.0, '#4a2038', 1.0],
  ['#0c0616', '#2a0c36', '#561a40', '#ff9ab0', 0.35, 0.5, 1.0, '#4e2440', 0.9],
  ['#2a0a3a', '#8a2048', '#e0603a', '#ff8a5a', 0.6, 0.55, 0.8, '#a04050', 0.3],
  ['#4a1450', '#a02a4a', '#f0804a', '#ff9a6a', 0.9, 0.7, 0.6, '#b05a60', 0.0],
  ['#5a1a58', '#b03050', '#f08a5a', '#ffb08a', 1.1, 0.85, 0.5, '#c06a6a', 0.0],
  ['#5a1a58', '#a82c4e', '#e87a58', '#ffa07a', 1.0, 0.8, 0.5, '#bc6068', 0.0],
  ['#4a1450', '#b0304a', '#f06a3a', '#ff8a52', 0.9, 0.7, 0.7, '#b85058', 0.0],
  ['#2a0c44', '#8a2048', '#e0503a', '#ff7a4a', 0.7, 0.55, 0.9, '#8a3048', 0.2],
  ['#140620', '#3a0e40', '#701a44', '#ff9ab0', 0.4, 0.5, 1.0, '#4e2440', 0.8],
  ['#0a0412', '#240a30', '#4a1438', '#ff9ab0', 0.35, 0.5, 1.0, '#4a2038', 1.0],
];

const BONE = '#e8dfc8';
const IRON = '#2a2630';

// ドクロ(頭蓋・あご・眼窩・鼻・歯)。眼窩はうっすら赤く光る
export function skull(kit, parent, x, y, z, s = 1, rotY = 0, eyes = null) {
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

export function buildMakai(ctx) {
  const { kit, root, lights } = ctx;
  const flames = [];
  const flameMat = new THREE.MeshBasicMaterial({ color: '#ffb347', toneMapped: false });
  const ghostMat = new THREE.MeshBasicMaterial({ color: '#7dffd8', toneMapped: false, transparent: true, opacity: 0.85 });
  const flame = (parent, x, y, z, s = 1, mat = flameMat) => {
    const f = new THREE.Mesh(kit.geom('cone', 0.05 * s, 0.16 * s, 6), mat);
    f.position.set(x, y, z);
    parent.add(f);
    flames.push({ f, ph: Math.random() * 6.3, s });
    return f;
  };
  // ロウソク(蝋が垂れている)
  const candle = (parent, x, y, z, h = 0.3) => {
    kit.cyl(parent, 0.05, 0.055, h, '#f1e6c8', x, y + h / 2, z, 8);
    kit.sph(parent, 0.035, '#f1e6c8', x + 0.045, y + h - 0.05, z);
    flame(parent, x, y + h + 0.08, z);
  };
  const candelabra = (parent, x, y, z, arms = 3, s = 1) => {
    kit.cyl(parent, 0.14 * s, 0.18 * s, 0.06, IRON, x, y + 0.03, z, 10);
    kit.cyl(parent, 0.03 * s, 0.03 * s, 0.4 * s, IRON, x, y + 0.22 * s, z, 6);
    for (let i = 0; i < arms; i++) {
      const dx = (i - (arms - 1) / 2) * 0.16 * s;
      kit.box(parent, 0.16 * s, 0.025, 0.025, IRON, x + dx / 2, y + 0.42 * s, z);
      candle(parent, x + dx, y + 0.43 * s, z, 0.22);
    }
  };

  island(kit, root, 32, 26, { top: '#4a3858', soil: '#3a2430', rock: '#6a4a5f' });
  const R = { x0: -12, x1: 6, z0: -10, z1: 3 };
  const room = new THREE.Group();
  root.add(room);
  const stone = '#b090c0';
  const trim = '#1e0e24';
  // 床(黒曜石)と、赤く光る魔法陣
  kit.box(room, R.x1 - R.x0, 0.12, R.z1 - R.z0, '#9a88a8', (R.x0 + R.x1) / 2, 0.06, (R.z0 + R.z1) / 2);
  // 床のタイル(市松)。家具やキャラの輪郭が分かるよう明暗をはっきり
  const tv = document.createElement('canvas');
  tv.width = tv.height = 128;
  const tc = tv.getContext('2d');
  tc.fillStyle = '#a08cb0';
  tc.fillRect(0, 0, 128, 128);
  tc.fillStyle = '#5c4a6c';
  tc.fillRect(0, 0, 64, 64);
  tc.fillRect(64, 64, 64, 64);
  tc.strokeStyle = '#1e0e24';
  tc.lineWidth = 4;
  tc.strokeRect(0, 0, 128, 128);
  const tileTex = new THREE.CanvasTexture(tv);
  tileTex.colorSpace = THREE.SRGBColorSpace;
  tileTex.wrapS = tileTex.wrapT = THREE.RepeatWrapping;
  tileTex.repeat.set((R.x1 - R.x0) / 2, (R.z1 - R.z0) / 2);
  const tiles = new THREE.Mesh(new THREE.PlaneGeometry(R.x1 - R.x0, R.z1 - R.z0), new THREE.MeshBasicMaterial({ map: tileTex }));
  tiles.rotation.x = -Math.PI / 2;
  tiles.position.set((R.x0 + R.x1) / 2, 0.125, (R.z0 + R.z1) / 2);
  room.add(tiles);
  const cv = document.createElement('canvas');
  cv.width = cv.height = 512;
  const c = cv.getContext('2d');
  c.translate(256, 256);
  c.strokeStyle = '#ff2a3a';
  c.lineWidth = 6;
  for (const r of [240, 226, 170]) {
    c.beginPath();
    c.arc(0, 0, r, 0, Math.PI * 2);
    c.stroke();
  }
  c.beginPath();
  for (let i = 0; i < 6; i++) {
    const a = ((i * 2) % 5) * ((Math.PI * 2) / 5) - Math.PI / 2;
    const x = Math.cos(a) * 226;
    const y = Math.sin(a) * 226;
    if (i === 0) {
      c.moveTo(x, y);
    } else {
      c.lineTo(x, y);
    }
  }
  c.closePath();
  c.stroke();
  c.lineWidth = 3;
  c.font = 'bold 26px serif';
  c.fillStyle = '#ff2a3a';
  c.textAlign = 'center';
  for (let i = 0; i < 24; i++) {
    c.save();
    c.rotate((i / 24) * Math.PI * 2);
    c.fillText(['ᚠ', 'ᚦ', 'ᚨ', 'ᛉ', 'ᛟ', 'ᛞ'][i % 6], 0, -198);
    c.restore();
  }
  const tex = new THREE.CanvasTexture(cv);
  tex.colorSpace = THREE.SRGBColorSpace;
  const seal = new THREE.Mesh(new THREE.PlaneGeometry(10.5, 10.5), new THREE.MeshBasicMaterial({ map: tex, transparent: true, toneMapped: false, opacity: 0.9 }));
  seal.rotation.x = -Math.PI / 2;
  seal.position.set(-3.2, 0.14, -2.6);
  room.add(seal);
  // 壁と骨の柱
  kit.box(room, R.x1 - R.x0, 5, 0.3, stone, (R.x0 + R.x1) / 2, 2.5, R.z0 - 0.15);
  kit.box(room, 0.3, 5, R.z1 - R.z0, stone, R.x0 - 0.15, 2.5, (R.z0 + R.z1) / 2);
  kit.box(room, R.x1 - R.x0, 0.3, 0.4, trim, (R.x0 + R.x1) / 2, 5.1, R.z0 - 0.15);
  kit.box(room, 0.4, 0.3, R.z1 - R.z0, trim, R.x0 - 0.15, 5.1, (R.z0 + R.z1) / 2);
  const edge = { emissive: '#ff1a2a', emissiveIntensity: 1.2 };
  kit.box(room, R.x1 - R.x0, 0.12, 0.08, '#ff3a3a', (R.x0 + R.x1) / 2, 0.3, R.z0 + 0.02, edge);
  kit.box(room, 0.08, 0.12, R.z1 - R.z0, '#ff3a3a', R.x0 + 0.02, 0.3, (R.z0 + R.z1) / 2, edge);
  kit.box(room, R.x1 - R.x0, 0.1, 0.08, '#ff3a3a', (R.x0 + R.x1) / 2, 4.85, R.z0 + 0.02, edge);
  kit.box(room, 0.08, 0.1, R.z1 - R.z0, '#ff3a3a', R.x0 + 0.02, 4.85, (R.z0 + R.z1) / 2, edge);
  kit.box(room, R.x1 - R.x0, 0.06, 0.1, '#ff3a3a', (R.x0 + R.x1) / 2, 0.14, R.z1 - 0.05, edge);
  kit.box(room, 0.1, 0.06, R.z1 - R.z0, '#ff3a3a', R.x1 - 0.05, 0.14, (R.z0 + R.z1) / 2, edge);
  for (let x = R.x0 + 1.5; x <= R.x1; x += 3) {
    kit.cyl(room, 0.22, 0.26, 4.6, BONE, x, 2.4, R.z0 + 0.1, 8);
    skull(kit, room, x, 4.85, R.z0 + 0.35, 0.8, 0);
  }
  for (let z = R.z0 + 1.5; z <= R.z1; z += 3.25) {
    kit.cyl(room, 0.22, 0.26, 4.6, BONE, R.x0 + 0.1, 2.4, z, 8);
    skull(kit, room, R.x0 + 0.35, 4.85, z, 0.8, Math.PI / 2);
  }
  // 奥の壁: 血のように赤い大きな丸窓と、細い窓
  const eye = new THREE.Mesh(new THREE.CircleGeometry(1.35, 32), new THREE.MeshBasicMaterial({ color: new THREE.Color('#ff3a2a').multiplyScalar(1.4), toneMapped: false }));
  eye.position.set(-1.2, 3.0, R.z0 + 0.04);
  room.add(eye);
  kit.box(room, 2.9, 0.1, 0.1, trim, -1.2, 3.0, R.z0 + 0.08);
  kit.box(room, 0.1, 2.9, 0.1, trim, -1.2, 3.0, R.z0 + 0.08);
  const pupil = new THREE.Mesh(new THREE.CircleGeometry(0.28, 20), new THREE.MeshBasicMaterial({ color: '#120408', toneMapped: false }));
  pupil.scale.x = 0.45;
  pupil.position.set(-1.2, 3.0, R.z0 + 0.06);
  room.add(pupil);
  for (const wx of [-8.2, 4.4]) {
    kit.box(room, 0.7, 2.2, 0.05, '#a02ae0', wx, 2.8, R.z0 + 0.03, { emissive: '#8a1ad0', emissiveIntensity: 0.9 });
    kit.box(room, 0.9, 0.1, 0.1, trim, wx, 3.9, R.z0 + 0.06);
    kit.box(room, 0.9, 0.1, 0.1, trim, wx, 1.7, R.z0 + 0.06);
  }
  ctx.clock = wallClock(kit, room, 2.0, 3.6, R.z0 + 0.02, 0, '#e8dfc8');
  // 深紅の旗とドクロの紋章
  for (const bx of [-10.6, 5.5]) {
    kit.box(room, 0.8, 2.0, 0.05, '#7a1428', bx, 3.3, R.z0 + 0.05);
    skull(kit, room, bx, 3.5, R.z0 + 0.15, 0.55, 0);
  }
  // 地獄の火鉢(奥の隅) と、壁際の燭台
  const braziers = [[-10.6, -8.6], [4.6, -8.6]];
  braziers.forEach(([bx, bz]) => {
    kit.cyl(room, 0.1, 0.12, 1.3, IRON, bx, 0.65, bz, 6);
    kit.cyl(room, 0.55, 0.3, 0.4, IRON, bx, 1.5, bz, 10);
    for (const [dx, h] of [[-0.18, 0.7], [0.12, 0.95], [0.2, 0.6]]) {
      const f = new THREE.Mesh(kit.geom('cone', 0.24, h, 6), new THREE.MeshBasicMaterial({ color: '#ff5a1a', toneMapped: false }));
      f.position.set(bx + dx, 1.7 + h / 2, bz + dx * 0.4);
      room.add(f);
      flames.push({ f, ph: Math.random() * 6.3, s: 1 });
    }
  });
  const flick = braziers.map(([bx, bz]) => lampLight(room, lights, bx, 2.6, bz + 0.6, '#ff6a3a', 14, 9));
  // 吊りシャンデリア(ロウソクの輪)
  const chand = new THREE.Group();
  chand.position.set(-3.1, 4.6, -2.8);
  const ring = new THREE.Mesh(kit.geom('torus', 1.4, 0.06, 6, 24), kit.mat(IRON));
  ring.rotation.x = Math.PI / 2;
  chand.add(ring);
  for (let i = 0; i < 8; i++) {
    const a = (i / 8) * Math.PI * 2;
    candle(chand, Math.cos(a) * 1.4, 0.05, Math.sin(a) * 1.4, 0.22);
  }
  kit.cyl(chand, 0.03, 0.03, 3, IRON, 0, 1.5, 0, 6);
  room.add(chand);
  flick.push(lampLight(room, lights, -3.1, 4.2, -2.8, '#ffb878', 12, 6));
  // テーブル・椅子
  ctx.glows = [];
  CLASSROOM_SEATS.forEach((seat, i) => {
    const { x, z } = seat.desk;
    kit.box(room, 2.0, 0.1, 1.0, '#a8607a', x, 1.0, z);
    kit.box(room, 2.1, 0.06, 1.1, '#1e0a14', x, 0.94, z);
    for (const dx of [-0.88, 0.88]) {
      for (const dz of [-0.38, 0.38]) {
        kit.cyl(room, 0.07, 0.09, 0.95, BONE, x + dx, 0.47, z + dz, 6);
      }
    }
    // 魔眼の水晶(状態で色が変わる)
    kit.cyl(room, 0.14, 0.2, 0.12, IRON, x, 1.11, z - 0.05, 8);
    const orb = new THREE.Mesh(new THREE.SphereGeometry(0.22, 16, 12), new THREE.MeshBasicMaterial({ color: '#3a3f55', toneMapped: false }));
    orb.position.set(x, 1.38, z - 0.05);
    room.add(orb);
    const ringM = new THREE.Mesh(new THREE.TorusGeometry(0.3, 0.015, 6, 28), new THREE.MeshBasicMaterial({ color: '#ff2a3a', toneMapped: false }));
    ringM.position.set(x, 1.38, z - 0.05);
    ringM.rotation.x = Math.PI / 2 - 0.4;
    room.add(ringM);
    ctx.glows[i] = { screen: orb, logo: ringM };
    candelabra(room, x + 0.68, 1.05, z + 0.1, 3, 0.8);
    skull(kit, room, x - 0.68, 1.3, z + 0.12, 0.45, Math.PI * 0.9);
    kit.cyl(room, 0.42, 0.4, 0.08, '#d0304a', seat.x, 0.64, seat.z, 14);
    kit.cyl(room, 0.4, 0.4, 0.06, IRON, seat.x, 0.58, seat.z, 14);
    kit.cyl(room, 0.07, 0.07, 0.56, BONE, seat.x, 0.3, seat.z, 6);
    kit.cyl(room, 0.3, 0.32, 0.05, IRON, seat.x, 0.03, seat.z, 10);
  });
  // ドクロの山(手前右)と、背の高い燭台
  // 血だまりと、窓から垂れる血
  [[-3.2, -2.6, 1.5, 0.9], [-8.5, -6, 1.1, 0.7], [2.4, -7.5, 1.3, 0.8], [-9, 1.5, 0.9, 0.6]].forEach(([bx, bz, rx, rz]) => {
    const p = new THREE.Mesh(new THREE.CircleGeometry(1, 20), new THREE.MeshBasicMaterial({ color: '#a0101c' }));
    p.scale.set(rx, rz, 1);
    p.rotation.x = -Math.PI / 2;
    p.position.set(bx, 0.135, bz);
    room.add(p);
  });
  [[-2.2, 1.6], [-1.6, 2.4], [-0.9, 1.3], [-0.3, 2.0], [-0.1, 1.5]].forEach(([dx, len]) => {
    kit.box(room, 0.1, len, 0.04, '#b0101c', dx - 1.1 + 0.2, 1.65 - len / 2 + 0.1, R.z0 + 0.1);
  });
  // 壁の鎖(手かせ付き)
  for (const cx of [-6.4, -5.9, 3.0]) {
    for (let k = 0; k < 9; k++) {
      const l = new THREE.Mesh(kit.geom('torus', 0.07, 0.02, 6, 10), kit.mat('#d0c8d8'));
      l.position.set(cx, 4.6 - k * 0.2, R.z0 + 0.15);
      l.rotation.y = k % 2 ? 0 : Math.PI / 2;
      room.add(l);
    }
    const sh = new THREE.Mesh(kit.geom('torus', 0.2, 0.04, 6, 14), kit.mat('#d0c8d8'));
    sh.position.set(cx, 2.65, R.z0 + 0.16);
    room.add(sh);
  }
  // 蜘蛛の巣(隅)
  const wv = document.createElement('canvas');
  wv.width = wv.height = 128;
  const wc = wv.getContext('2d');
  wc.strokeStyle = 'rgba(255,255,255,0.85)';
  wc.lineWidth = 2;
  for (let k = 0; k <= 6; k++) {
    const a = (k / 6) * (Math.PI / 2);
    wc.beginPath();
    wc.moveTo(0, 0);
    wc.lineTo(Math.cos(a) * 128, Math.sin(a) * 128);
    wc.stroke();
  }
  for (let r = 24; r <= 120; r += 24) {
    wc.beginPath();
    for (let k = 0; k <= 6; k++) {
      const a = (k / 6) * (Math.PI / 2);
      const rr = r - (k % 2 ? 8 : 0);
      wc.lineTo(Math.cos(a) * rr, Math.sin(a) * rr);
    }
    wc.stroke();
  }
  const webTex = new THREE.CanvasTexture(wv);
  for (const [wx, sx] of [[R.x0 + 0.1, 1], [R.x1 - 0.1, -1]]) {
    const web = new THREE.Mesh(new THREE.PlaneGeometry(2.6, 2.6), new THREE.MeshBasicMaterial({ map: webTex, transparent: true, side: THREE.DoubleSide }));
    web.scale.x = sx;
    web.rotation.z = 0;
    web.position.set(wx + sx * 1.3, 4.7 - 1.3, R.z0 + 0.08);
    web.scale.y = -1;
    room.add(web);
  }
  const pile = [[0, 0, 0, 1], [0.5, 0, 0.1, 1], [-0.45, 0, 0.15, 1], [0.1, 0, 0.55, 1], [0.25, 0.42, 0.15, 1], [-0.2, 0.4, 0.3, 1], [0.05, 0.8, 0.25, 1]];
  pile.forEach(([px, py, pz], i) => {
    skull(kit, room, 5.2 + px, 0.38 + py, 1.5 + pz, 1, i * 1.7, i === 6);
  });
  for (const [lx, lz] of [[-10.8, 2.0], [1.0, 2.4], [5.4, -0.8]]) {
    kit.cyl(room, 0.1, 0.16, 0.2, IRON, lx, 0.1, lz, 8);
    kit.cyl(room, 0.04, 0.04, 2.2, IRON, lx, 1.2, lz, 6);
    kit.cyl(room, 0.25, 0.08, 0.1, IRON, lx, 2.3, lz, 8);
    for (let k = 0; k < 5; k++) {
      const a = (k / 5) * Math.PI * 2;
      candle(room, lx + Math.cos(a) * 0.18, 2.35, lz + Math.sin(a) * 0.18, 0.2);
    }
  }
  ctx.shelf = archiveShelf(kit, room, '#2a1a30');
  ctx.board = whiteboard(kit, room, '#2a1a30', '#e8dcc8');

  // ---- 外: 枯れ木・墓場・溶岩・鬼火・ドクロの杭
  const deadTree = (x, z, s = 1) => {
    kit.cyl(root, 0.16 * s, 0.3 * s, 3.2 * s, '#241824', x, 1.6 * s, z, 6);
    for (const [bx, by, rz, len] of [[0.5, 2.2, -0.9, 1.4], [-0.5, 2.6, 0.9, 1.2], [0.1, 3.0, -0.4, 1.0]]) {
      const b = kit.cyl(root, 0.05 * s, 0.1 * s, len * s, '#241824', x + bx * s * 0.7, by * s, z, 5);
      b.rotation.z = rz;
    }
  };
  [[-14, -11, 1.3], [9, -10, 1.1], [12, -4, 1.4], [-14, 6, 1.1], [11, 9, 1.2], [13, 2, 1], [-9, 10.5, 1.2], [-3, 11, 1]].forEach(([x, z, s]) => deadTree(x, z, s));
  // 墓石と十字架
  [[-12, 9, 0], [-10, 11, 1], [-7, 8.5, 0], [0, 10.5, 1], [3, 9.2, 0], [7.5, 11, 1], [11.5, 6, 0]].forEach(([x, z, cross], i) => {
    const lean = ((i * 37) % 9 - 4) * 0.03;
    if (cross) {
      kit.box(root, 0.22, 1.5, 0.22, '#b8b0c4', x, 0.75, z).rotation.z = lean;
      kit.box(root, 0.9, 0.22, 0.22, '#b8b0c4', x, 1.15, z).rotation.z = lean;
    } else {
      kit.box(root, 0.85, 1.1, 0.24, '#b8b0c4', x, 0.55, z).rotation.z = lean;
      kit.cyl(root, 0.425, 0.425, 0.24, '#b8b0c4', x, 1.1, z, 12).rotation.x = Math.PI / 2;
    }
    kit.box(root, 1.1, 0.12, 0.7, '#2a1c30', x, 0.06, z + 0.4);
  });
  // 立てかけた棺(ふたが少し開いて赤い光が漏れる)
  [[-2.5, 6.2, 0.3], [5.2, 5.2, -0.4]].forEach(([cx, cz, ry]) => {
    const cof = new THREE.Group();
    cof.position.set(cx, 0, cz);
    cof.rotation.y = ry;
    cof.rotation.x = -0.12;
    kit.box(cof, 1.3, 2.8, 0.5, '#6a3a28', 0, 1.45, 0);
    kit.box(cof, 1.0, 2.4, 0.06, '#a08060', 0, 1.45, 0.28);
    kit.box(cof, 0.1, 1.9, 0.07, '#d8c8a0', 0, 1.5, 0.3);
    kit.box(cof, 0.7, 0.1, 0.07, '#d8c8a0', 0, 2.1, 0.3);
    kit.box(cof, 0.5, 2.3, 0.04, '#ff2a2a', 0.78, 1.5, 0.16, { emissive: '#ff1a1a', emissiveIntensity: 1.2 });
    root.add(cof);
  });
  // 地面から伸びる骨の手
  [[-10.5, 5.5], [1.8, 6.4], [9, 8], [-6, 11.5], [-13, 0.5]].forEach(([hx, hz], i) => {
    const hand = new THREE.Group();
    hand.position.set(hx, 0, hz);
    hand.rotation.y = i * 1.3;
    kit.cyl(hand, 0.07, 0.1, 1.1, BONE, 0, 0.55, 0, 6);
    kit.box(hand, 0.28, 0.2, 0.1, BONE, 0, 1.2, 0);
    for (let f = 0; f < 4; f++) {
      const fg = kit.cyl(hand, 0.025, 0.03, 0.4, BONE, -0.1 + f * 0.067, 1.45, 0, 4);
      fg.rotation.z = (f - 1.5) * 0.16;
    }
    const th = kit.cyl(hand, 0.025, 0.03, 0.3, BONE, 0.17, 1.28, 0, 4);
    th.rotation.z = -0.9;
    root.add(hand);
  });
  // 溶岩の池(ぼんやり光る)
  const lavas = [];
  for (const [lx, lz, r] of [[-8, 7, 2.0], [8.5, 6, 1.8], [-14, -3, 1.6]]) {
    const lava = new THREE.Mesh(new THREE.CircleGeometry(r, 28), new THREE.MeshBasicMaterial({ color: new THREE.Color('#ff3a10'), toneMapped: false }));
    lava.rotation.x = -Math.PI / 2;
    lava.position.set(lx, 0.03, lz);
    root.add(lava);
    lavas.push(lava);
    // 溶岩の縁(黒い岩)で輪郭をはっきりさせる
    const rim = new THREE.Mesh(new THREE.RingGeometry(r, r + 0.35, 28), new THREE.MeshBasicMaterial({ color: '#1a0e1a' }));
    rim.rotation.x = -Math.PI / 2;
    rim.position.set(lx, 0.035, lz);
    root.add(rim);
  }
  flick.push(lampLight(root, lights, -8, 1, 7, '#ff5a2a', 10, 6));
  // ドクロの杭とロウソクの塊
  for (const [px, pz] of [[-4, 7.5], [4, 6.5], [-1, 8.5]]) {
    kit.cyl(root, 0.06, 0.08, 2.6, '#3a2c30', px, 1.3, pz, 6);
    skull(kit, root, px, 2.75, pz, 0.8, 0.6, true);
  }
  for (let i = 0; i < 6; i++) {
    candle(root, -2 + Math.cos(i * 1.9) * 0.4, 0.04, 8.6 + Math.sin(i * 1.9) * 0.4, 0.15 + (i % 3) * 0.08);
  }
  // 骨のかけら・光るキノコ
  for (let i = 0; i < 18; i++) {
    const bx = -13 + ((i * 7.7) % 26);
    const bz = 5 + ((i * 3.9) % 8);
    kit.box(root, 0.3, 0.06, 0.07, BONE, bx, 0.05, bz).rotation.y = i;
    if (i % 3 === 0) {
      kit.sph(root, 0.14, '#c080ff', bx + 0.4, 0.2, bz + 0.3, { emissive: '#a040ff', emissiveIntensity: 1 });
    }
  }
  // 鬼火(青緑の炎がふわふわ漂う)
  const wisps = [];
  for (let i = 0; i < 9; i++) {
    const w = new THREE.Mesh(kit.geom('sph', 0.14, 8, 6), ghostMat);
    w.scale.y = 1.5;
    root.add(w);
    wisps.push({ w, x: -12 + ((i * 5.3) % 24), z: -7 + ((i * 4.1) % 20), ph: i * 1.7 });
  }
  // 浮かぶ赤い結晶
  const crystals = [];
  for (const [cx, cy, cz, s] of [[-15, 4, -2, 1.1], [14, 5, -9, 1.0], [-11, 3.2, 12, 0.8], [15, 3.5, 7, 0.9]]) {
    const m = new THREE.Mesh(kit.geom('oct', 0.7 * s, 0), kit.mat('#ff3a5a', { emissive: '#d0103a', emissiveIntensity: 0.9, flatShading: true }));
    m.scale.y = 1.8;
    m.position.set(cx, cy, cz);
    m.userData.base = cy;
    root.add(m);
    crystals.push(m);
  }
  // 遠くの浮島(枯れ木つき)
  for (const [ix, iy, iz, s] of [[-30, 8, -26, 1.0], [22, 12, -30, 1.3], [-32, 3, 14, 0.9]]) {
    const g = new THREE.Group();
    g.position.set(ix, iy, iz);
    g.scale.setScalar(s);
    island(kit, g, 9, 8, { top: '#3a2c3c', soil: '#2a1a24', rock: '#4a2f3f', ellipse: true });
    kit.cyl(g, 0.15, 0.25, 3, '#241824', 0, 1.5, 0, 6);
    root.add(g);
  }
  // コウモリ(部屋の上をぐるぐる)
  const bats = [0, 1, 2, 3, 4].map((i) => {
    const b = makeBird(kit, '#1a1020');
    b.g.scale.setScalar(0.8);
    root.add(b.g);
    return { ...b, i, r: 6 + i * 2.2, h: 6 + (i % 3) * 1.2, sp: 0.35 + i * 0.07 };
  });

  ctx.seats = CLASSROOM_SEATS;
  ctx.cloudsObj = clouds(kit, root, '#6a2a3a');
  ctx.keys = MAKAI_KEYS;
  ctx.hemi = { sky: '#d890b0', ground: '#8a4a70', boost: 3.0 };
  ctx.sky = { aurora: 0, crack: 0.9, moon2: false, moonColor: '#ffb4a0' };
  ctx.panel = 'makai';
  ctx.update = (t) => {
    // ロウソクの炎と地獄の火は不規則にゆらぐ
    for (const l of flick) {
      l.intensity = Math.max(l.intensity, l.userData.power * 0.7) * (1 + Math.sin(t * 11 + l.position.x) * 0.1 + Math.sin(t * 27 + l.position.z) * 0.07);
    }
    for (const f of flames) {
      f.f.scale.set(1 + Math.sin(t * 12 + f.ph) * 0.15, 1 + Math.sin(t * 9 + f.ph * 2) * 0.25, 1);
    }
    lavas.forEach((l, i) => {
      l.material.color.setHSL(0.01 + Math.sin(t * 0.8 + i) * 0.01, 1, 0.45).multiplyScalar(0.9 + Math.sin(t * 1.3 + i * 2) * 0.15);
    });
    wisps.forEach(({ w, x, z, ph }) => {
      w.position.set(x + Math.sin(t * 0.5 + ph) * 1.2, 1.2 + Math.sin(t * 0.9 + ph) * 0.5, z + Math.cos(t * 0.4 + ph) * 1.2);
    });
    crystals.forEach((m, i) => {
      m.position.y = m.userData.base + Math.sin(t * 0.8 + i * 1.7) * 0.5;
      m.rotation.y = t * 0.5 + i;
    });
    chand.rotation.y = Math.sin(t * 0.4) * 0.15;
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
