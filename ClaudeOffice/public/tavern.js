// 異世界スキン「ルイーダの酒場」: 夕暮れのラベンダー色の空と淡い大きな月。木組みの壁にステンドグラス、
// 赤い丸ラグの上の丸テーブル、光る酒瓶の棚。外はストーンサークルと水晶玉、錬金釜、パステルの木々
import * as THREE from 'three';
import { ROOM, island, clouds, tree, pine, lampLight, wallClock, calendar, archiveShelf, whiteboard, propLabel, sparkles, SHELF_POS, BOARD_POS } from './world.js';

// 空のキーフレーム(上・中・下・太陽色・太陽の強さ・半球光・ランプ・雲・星)。昼もラベンダー寄り
export const ISEKAI_KEYS = [
  ['#120c3a', '#2c2068', '#54408e', '#c8b8ff', 0.4, 0.5, 1.0, '#5a4a8a', 1.0],
  ['#160f42', '#33246f', '#5e4796', '#c8b8ff', 0.4, 0.5, 1.0, '#5e4e90', 0.9],
  ['#3a3a8e', '#9a6ab8', '#f2a8c0', '#ffc0a0', 0.65, 0.6, 0.6, '#e0b0d0', 0.3],
  ['#6a78d8', '#a8a2e8', '#f0d0e4', '#fff0e0', 1.0, 0.8, 0.2, '#fff4fc', 0.0],
  ['#6476dc', '#9ea8ec', '#e6d8f2', '#fff6ec', 1.15, 0.9, 0.0, '#ffffff', 0.0],
  ['#6a74d6', '#a49ee6', '#ead2ec', '#fff2e4', 1.1, 0.85, 0.1, '#ffffff', 0.0],
  ['#5a5ac8', '#b08ad0', '#f6b8c8', '#ffd0a8', 0.95, 0.75, 0.4, '#f8dce8', 0.1],
  ['#3e3a9e', '#8c64b8', '#ee9ab8', '#ffb090', 0.75, 0.6, 0.8, '#e4a8c8', 0.3],
  ['#1e1858', '#42307e', '#7a5aa8', '#d0c0ff', 0.45, 0.5, 1.0, '#5c4a8c', 0.8],
  ['#120c3a', '#2c2068', '#54408e', '#c8b8ff', 0.4, 0.5, 1.0, '#5a4a8a', 1.0],
];

const TIMBER = '#4a2c1c';
const PLASTER = '#ecdfc6';

// アーチ形のステンドグラス(色ガラスの格子)。rotY=0 で奥の壁、π/2 で左の壁
export function stainedGlass(kit, parent, x, y, z, w, h, rotY, palette, frame = TIMBER, glow = 1.0) {
  const W = 128;
  const H = Math.round((W * h) / w);
  const cv = document.createElement('canvas');
  cv.width = W;
  cv.height = H;
  const c = cv.getContext('2d');
  const r = W / 2;
  c.beginPath();
  c.moveTo(0, H);
  c.lineTo(0, r);
  c.arc(r, r, r, Math.PI, 0);
  c.lineTo(W, H);
  c.closePath();
  c.save();
  c.clip();
  const cols = 4;
  const cell = W / cols;
  for (let j = 0; j * cell < H; j++) {
    for (let i = 0; i < cols; i++) {
      c.fillStyle = palette[(i * 3 + j * 5 + ((i * j) % 3)) % palette.length];
      c.fillRect(i * cell, j * cell, cell, cell);
    }
  }
  c.strokeStyle = 'rgba(30, 18, 12, 0.95)';
  c.lineWidth = 5;
  for (let i = 1; i < cols; i++) {
    c.beginPath();
    c.moveTo(i * cell, 0);
    c.lineTo(i * cell, H);
    c.stroke();
  }
  for (let j = 1; j * cell < H; j++) {
    c.beginPath();
    c.moveTo(0, j * cell);
    c.lineTo(W, j * cell);
    c.stroke();
  }
  c.restore();
  c.lineWidth = 9;
  c.strokeStyle = frame;
  c.stroke();
  const tex = new THREE.CanvasTexture(cv);
  tex.colorSpace = THREE.SRGBColorSpace;
  const m = new THREE.Mesh(new THREE.PlaneGeometry(w, h), new THREE.MeshBasicMaterial({ map: tex, transparent: true, alphaTest: 0.5, toneMapped: false, color: new THREE.Color(glow, glow, glow) }));
  const g = new THREE.Group();
  g.position.set(x, y, z);
  g.rotation.y = rotY;
  m.position.z = 0.03;
  g.add(m);
  kit.box(g, w + 0.2, 0.14, 0.16, frame, 0, -h / 2 - 0.05, 0.06);
  parent.add(g);
  return m;
}

// 木の床(板の継ぎ目と色むら)
function plankFloor(w, d, base, line) {
  const cv = document.createElement('canvas');
  cv.width = 1024;
  cv.height = Math.round((1024 * d) / w);
  const c = cv.getContext('2d');
  const ppu = 1024 / w;
  const row = 0.5 * ppu;
  for (let j = 0; j * row < cv.height; j++) {
    let x = -((j * 137) % 200);
    while (x < cv.width) {
      const len = (2.2 + ((j * 7 + x) % 5) * 0.4) * ppu;
      const col = new THREE.Color(base).offsetHSL(0, 0, (((j * 13 + Math.floor(x)) % 7) - 3) * 0.012);
      c.fillStyle = `#${col.getHexString()}`;
      c.fillRect(x, j * row, len, row);
      c.fillStyle = line;
      c.fillRect(x, j * row, 2, row);
      x += len;
    }
    c.fillStyle = line;
    c.fillRect(0, j * row, cv.width, 2);
  }
  const tex = new THREE.CanvasTexture(cv);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.anisotropy = 4;
  return tex;
}

// 地面に描く光る魔法陣(輪・星・ルーン)
export function magicCircle(radius, color, opacity = 0.8) {
  const cv = document.createElement('canvas');
  cv.width = cv.height = 512;
  const c = cv.getContext('2d');
  c.translate(256, 256);
  c.strokeStyle = color;
  c.fillStyle = color;
  c.lineWidth = 5;
  for (const r of [246, 230, 160]) {
    c.beginPath();
    c.arc(0, 0, r, 0, Math.PI * 2);
    c.stroke();
  }
  c.lineWidth = 4;
  c.beginPath();
  for (let i = 0; i <= 6; i++) {
    const a = ((i * 2) % 6) * (Math.PI / 3) - Math.PI / 2;
    c.lineTo(Math.cos(a) * 230, Math.sin(a) * 230);
  }
  c.stroke();
  c.beginPath();
  for (let i = 0; i <= 6; i++) {
    const a = ((i * 2 + 1) % 6) * (Math.PI / 3) - Math.PI / 2;
    c.lineTo(Math.cos(a) * 230, Math.sin(a) * 230);
  }
  c.stroke();
  c.font = 'bold 22px serif';
  c.textAlign = 'center';
  for (let i = 0; i < 28; i++) {
    c.save();
    c.rotate((i / 28) * Math.PI * 2);
    c.fillText(['ᚠ', 'ᚦ', 'ᚨ', 'ᛉ', 'ᛟ', 'ᛞ', 'ᚱ'][i % 7], 0, -232 + 26);
    c.restore();
  }
  const tex = new THREE.CanvasTexture(cv);
  tex.colorSpace = THREE.SRGBColorSpace;
  const m = new THREE.Mesh(new THREE.PlaneGeometry(radius * 2, radius * 2), new THREE.MeshBasicMaterial({ map: tex, transparent: true, opacity, depthWrite: false, toneMapped: false, blending: THREE.AdditiveBlending }));
  m.rotation.x = -Math.PI / 2;
  return m;
}

// 丸テーブル3席ずつ×4卓。席はテーブルの奥(カメラ向き)と手前の左右
const TABLES = [[-8.0, -6.0], [-2.8, -6.2], [-6.6, -0.8], [-1.3, -1.4]];
export const TAVERN_SEATS = TABLES.flatMap(([tx, tz], k) => [60, 180, 300].map((deg) => {
  const a = ((deg + k * 8) * Math.PI) / 180;
  const x = tx + Math.sin(a) * 1.55;
  const z = tz + Math.cos(a) * 1.55;
  return { x, z, rot: Math.atan2(tx - x, tz - z), table: { x: tx, z: tz } };
}));

function roundTable(kit, room, [tx, tz], seats, glows, flames) {
  // 赤い丸ラグ(金の縁取り)
  const rug = new THREE.Mesh(new THREE.CircleGeometry(2.25, 48), kit.mat('#7c1d2b'));
  rug.rotation.x = -Math.PI / 2;
  rug.position.set(tx, 0.15, tz);
  rug.receiveShadow = true;
  room.add(rug);
  for (const [r0, r1] of [[2.08, 2.18], [1.7, 1.74]]) {
    const ring = new THREE.Mesh(new THREE.RingGeometry(r0, r1, 48), kit.mat('#d9ad55'));
    ring.rotation.x = -Math.PI / 2;
    ring.position.set(tx, 0.155, tz);
    room.add(ring);
  }
  kit.cyl(room, 0.95, 0.95, 0.1, '#7a4a2e', tx, 1.0, tz, 28);
  kit.cyl(room, 0.97, 0.97, 0.04, '#5a3420', tx, 0.93, tz, 28);
  kit.cyl(room, 0.1, 0.14, 0.9, '#4a2c1c', tx, 0.5, tz, 8);
  kit.cyl(room, 0.5, 0.55, 0.08, '#4a2c1c', tx, 0.18, tz, 14);
  // ろうそく3本
  for (let k = 0; k < 3; k++) {
    const a = k * 2.1;
    const h = 0.18 + k * 0.06;
    const cx = tx + Math.cos(a) * 0.14;
    const cz = tz + Math.sin(a) * 0.14;
    kit.cyl(room, 0.045, 0.05, h, '#f6eed8', cx, 1.05 + h / 2, cz, 8);
    const f = new THREE.Mesh(kit.geom('cone', 0.04, 0.12, 6), new THREE.MeshBasicMaterial({ color: new THREE.Color('#ffc46a').multiplyScalar(1.6), toneMapped: false }));
    f.position.set(cx, 1.05 + h + 0.07, cz);
    room.add(f);
    flames.push(f);
  }
  seats.forEach((s) => {
    const dx = s.x - tx;
    const dz = s.z - tz;
    const len = Math.hypot(dx, dz);
    const ux = dx / len;
    const uz = dz / len;
    // 席の前に水晶玉(状態で色が変わる)と羊皮紙
    const ox = tx + ux * 0.6;
    const oz = tz + uz * 0.6;
    kit.cyl(room, 0.09, 0.13, 0.08, '#d9ad55', ox, 1.09, oz, 10);
    const orb = new THREE.Mesh(new THREE.SphereGeometry(0.15, 16, 12), new THREE.MeshBasicMaterial({ color: '#3a3f55', toneMapped: false }));
    orb.position.set(ox, 1.27, oz);
    room.add(orb);
    const halo = new THREE.Mesh(new THREE.TorusGeometry(0.2, 0.012, 6, 24), new THREE.MeshBasicMaterial({ color: '#e9c46a', toneMapped: false }));
    halo.position.set(ox, 1.27, oz);
    halo.rotation.x = Math.PI / 2;
    room.add(halo);
    glows.push({ screen: orb, logo: halo });
    const paper = kit.box(room, 0.32, 0.01, 0.24, '#f3e6c4', tx + ux * 0.45 - uz * 0.3, 1.06, tz + uz * 0.45 + ux * 0.3);
    paper.rotation.y = Math.atan2(ux, uz) + 0.3;
    // 背もたれ付きの木の椅子(テーブル向き)
    const ch = new THREE.Group();
    ch.position.set(s.x, 0, s.z);
    ch.rotation.y = s.rot;
    kit.cyl(ch, 0.38, 0.38, 0.08, '#8a5634', 0, 0.6, 0, 14);
    for (const [lx, lz] of [[-0.24, -0.24], [0.24, -0.24], [-0.24, 0.24], [0.24, 0.24]]) {
      kit.box(ch, 0.06, 0.58, 0.06, '#5a3420', lx, 0.29, lz);
    }
    kit.box(ch, 0.62, 0.6, 0.07, '#8a5634', 0, 1.0, -0.36);
    kit.box(ch, 0.07, 0.68, 0.07, '#5a3420', -0.3, 0.96, -0.36);
    kit.box(ch, 0.07, 0.68, 0.07, '#5a3420', 0.3, 0.96, -0.36);
    room.add(ch);
  });
}

export function buildTavern(ctx) {
  const { kit, root, lights } = ctx;
  const R = ROOM;
  const flick = [];
  const flames = [];
  island(kit, root, 40, 34, { shape: 'round', top: '#86d06c', edge: '#5fae54', soil: '#6e4a34', rock: '#4e4660' });
  const room = new THREE.Group();
  root.add(room);
  const cx = (R.x0 + R.x1) / 2;
  const cz = (R.z0 + R.z1) / 2;
  // 床(濃い木の板張り)
  kit.box(room, R.x1 - R.x0 + 0.4, 0.14, R.z1 - R.z0 + 0.4, '#4e2e1c', cx - 0.2, 0.07, cz - 0.2);
  const floor = new THREE.Mesh(new THREE.PlaneGeometry(R.x1 - R.x0, R.z1 - R.z0), kit.mat('#ffffff', { map: plankFloor(R.x1 - R.x0, R.z1 - R.z0, '#7a4a2c', '#4a2a18') }));
  floor.rotation.x = -Math.PI / 2;
  floor.position.set(cx, 0.142, cz);
  floor.receiveShadow = true;
  room.add(floor);
  // 壁: しっくいの壁に濃い木組み(柱・腰板・上の梁)
  kit.box(room, R.x1 - R.x0 + 0.4, 5, 0.4, PLASTER, cx - 0.2, 2.5, R.z0 - 0.2);
  kit.box(room, 0.4, 5, R.z1 - R.z0, PLASTER, R.x0 - 0.2, 2.5, cz);
  kit.box(room, R.x1 - R.x0, 1.1, 0.06, '#5e3a24', cx, 0.65, R.z0 + 0.03);
  kit.box(room, 0.06, 1.1, R.z1 - R.z0, '#5e3a24', R.x0 + 0.03, 0.65, cz);
  for (const y of [1.2, 4.85]) {
    kit.box(room, R.x1 - R.x0, 0.2, 0.12, TIMBER, cx, y, R.z0 + 0.06);
    kit.box(room, 0.12, 0.2, R.z1 - R.z0, TIMBER, R.x0 + 0.06, y, cz);
  }
  kit.box(room, R.x1 - R.x0 + 0.6, 0.35, 0.6, '#3a2216', cx - 0.3, 5.15, R.z0 - 0.2);
  kit.box(room, 0.6, 0.35, R.z1 - R.z0 + 0.1, '#3a2216', R.x0 - 0.2, 5.15, cz);
  for (const x of [R.x0 + 0.1, -9, -6, -3, 0, 3, R.x1 - 0.1]) {
    kit.box(room, 0.24, 5, 0.14, TIMBER, x, 2.5, R.z0 + 0.07);
  }
  for (const z of [R.z0 + 0.1, -0.2, R.z1 - 0.1]) {
    kit.box(room, 0.14, 5, 0.24, TIMBER, R.x0 + 0.07, 2.5, z);
  }
  // ステンドグラス(奥に2つ・左に1つ)
  const glassColors = ['#e8443a', '#f39c34', '#f5d547', '#5cc06a', '#3d8fe0', '#8a5ad8', '#e85aa8'];
  stainedGlass(kit, room, -10.5, 3.0, R.z0 + 0.02, 1.5, 2.6, 0, glassColors);
  stainedGlass(kit, room, 1.5, 3.0, R.z0 + 0.02, 1.5, 2.6, 0, glassColors);
  stainedGlass(kit, room, R.x0 + 0.02, 3.0, 1.4, 1.2, 2.3, Math.PI / 2, glassColors);
  // 暖炉(石積み・炎)
  const hearthX = -4.5;
  kit.box(room, 2.8, 3.0, 0.9, '#8f857a', hearthX, 1.5, R.z0 + 0.45);
  kit.box(room, 1.6, 4.8, 0.6, '#857b70', hearthX, 2.6, R.z0 + 0.3);
  kit.box(room, 3.1, 0.2, 1.1, '#5e3a24', hearthX, 3.05, R.z0 + 0.5);
  kit.box(room, 1.7, 1.3, 0.3, '#1a1210', hearthX, 0.85, R.z0 + 0.82);
  for (const [dx, ry] of [[-0.25, 0.3], [0.2, -0.3]]) {
    kit.cyl(room, 0.09, 0.09, 1.0, '#5a3420', hearthX + dx, 0.32, R.z0 + 0.95, 6).rotation.set(0, ry, Math.PI / 2);
  }
  for (const [dx, h, color] of [[-0.3, 0.7, '#ff7a1a'], [0, 1.0, '#ffa52a'], [0.3, 0.6, '#ff6a10']]) {
    const f = new THREE.Mesh(kit.geom('cone', 0.24, h, 6), new THREE.MeshBasicMaterial({ color: new THREE.Color(color).multiplyScalar(1.5), toneMapped: false }));
    f.position.set(hearthX + dx, 0.4 + h / 2, R.z0 + 0.95);
    room.add(f);
    flames.push(f);
  }
  flick.push(lampLight(room, lights, hearthX, 1.3, R.z0 + 1.8, '#ff9a4c', 13, 7));
  // 時計・カレンダー・ギルドの旗
  ctx.clock = wallClock(kit, room, -1.5, 3.7, R.z0 + 0.02, 0, '#c9a24a');
  ctx.calendar = calendar(room, -1.5, 2.05, R.z0 + 0.04, 0);
  const banner = new THREE.Group();
  banner.position.set(-7.5, 3.2, R.z0 + 0.06);
  kit.box(banner, 1.1, 0.08, 0.08, '#c9a24a', 0, 1.1, 0.02);
  kit.box(banner, 0.9, 2.0, 0.03, '#7a1d2a', 0, 0.05, 0);
  const tip = new THREE.Mesh(kit.geom('cone', 0.64, 0.5, 3), kit.mat('#7a1d2a'));
  tip.rotation.z = Math.PI;
  tip.scale.z = 0.05;
  tip.position.y = -1.05;
  banner.add(tip);
  for (const s of [-1, 1]) {
    kit.box(banner, 0.06, 1.0, 0.02, '#e2bd62', 0, 0.2, 0.03).rotation.z = s * 0.6;
  }
  kit.box(banner, 0.4, 0.05, 0.02, '#e2bd62', 0, -0.2, 0.03);
  room.add(banner);
  // 光る酒瓶の棚(奥の壁の右)とカウンター
  kit.box(room, 2.9, 3.6, 0.5, '#3e2416', 4.5, 1.95, R.z0 + 0.25);
  const bottleColors = ['#ff9ad5', '#9ad8ff', '#c6ff9a', '#ffe08a', '#d0a8ff', '#ffb08a', '#8affd8'];
  [1.0, 1.75, 2.5, 3.25].forEach((y, r) => {
    kit.box(room, 2.7, 0.06, 0.45, '#6a4228', 4.5, y, R.z0 + 0.5);
    for (let k = 0; k < 7; k++) {
      const c = new THREE.Color(bottleColors[(k + r * 3) % bottleColors.length]);
      const mat = new THREE.MeshBasicMaterial({ color: c.clone().multiplyScalar(1.15), toneMapped: false });
      const bx = 3.35 + k * 0.38;
      const b = new THREE.Mesh(kit.geom('cyl', 0.08, 0.1, 0.32, 8), mat);
      b.position.set(bx, y + 0.19, R.z0 + 0.5);
      room.add(b);
      const n = new THREE.Mesh(kit.geom('cyl', 0.03, 0.045, 0.12, 6), mat);
      n.position.set(bx, y + 0.41, R.z0 + 0.5);
      room.add(n);
    }
  });
  propLabel(ctx, room, '🍷 酒場カウンター', 4.5, 4.4, R.z0 + 0.4);
  kit.box(room, 3.6, 1.15, 0.9, '#4a2c1c', 4.2, 0.6, -6.4);
  kit.box(room, 3.8, 0.1, 1.05, '#8a5634', 4.2, 1.22, -6.4);
  kit.box(room, 3.6, 0.1, 0.05, '#c9a24a', 4.2, 0.95, -5.93);
  kit.cyl(room, 0.08, 0.08, 0.45, '#c9a24a', 3.2, 1.5, -6.5, 8);
  kit.box(room, 0.35, 0.06, 0.06, '#c9a24a', 3.35, 1.66, -6.5);
  for (const mx of [4.6, 5.3]) {
    kit.cyl(room, 0.1, 0.09, 0.2, '#f1d27a', mx, 1.37, -6.3, 10);
  }
  for (const sx of [2.9, 5.1]) {
    kit.cyl(room, 0.3, 0.28, 0.08, '#8a5634', sx, 0.85, -5.45, 12);
    kit.cyl(room, 0.05, 0.05, 0.8, '#4a2c1c', sx, 0.42, -5.45, 6);
  }
  // 酒樽
  for (const [bx, by, bz] of [[5.3, 0.6, -3.4], [5.3, 0.6, -2.4], [5.3, 1.5, -2.9], [4.6, 0.6, 2.2]]) {
    kit.cyl(room, 0.45, 0.42, 0.95, '#8a5634', bx, by, bz, 14);
    for (const dy of [-0.3, 0.3]) {
      kit.cyl(room, 0.47, 0.47, 0.06, '#3a2a20', bx, by + dy, bz, 14);
    }
  }
  // 魔導書庫(アーカイブ)・クエストボード
  ctx.shelf = archiveShelf(kit, room, '#3e2416');
  propLabel(ctx, room, '📖 魔導書庫', SHELF_POS.x + 0.3, 4.9, SHELF_POS.z);
  ctx.board = whiteboard(kit, room, '#5e3a24', '#f1e4c4');
  propLabel(ctx, room, '📜 クエストボード', BOARD_POS.x + 0.3, 4.3, BOARD_POS.z);
  // ろうそくのシャンデリア
  const chand = new THREE.Group();
  chand.position.set(-4.6, 4.3, -3.6);
  const ringM = new THREE.Mesh(kit.geom('torus', 1.1, 0.05, 6, 24), kit.mat('#2a2018'));
  ringM.rotation.x = Math.PI / 2;
  chand.add(ringM);
  kit.cyl(chand, 0.025, 0.025, 1.5, '#2a2018', 0, 0.75, 0, 6);
  for (let i = 0; i < 8; i++) {
    const a = (i / 8) * Math.PI * 2;
    kit.cyl(chand, 0.04, 0.04, 0.2, '#f6eed8', Math.cos(a) * 1.1, 0.12, Math.sin(a) * 1.1, 6);
    const f = new THREE.Mesh(kit.geom('cone', 0.04, 0.12, 6), new THREE.MeshBasicMaterial({ color: new THREE.Color('#ffc46a').multiplyScalar(1.6), toneMapped: false }));
    f.position.set(Math.cos(a) * 1.1, 0.29, Math.sin(a) * 1.1);
    chand.add(f);
    flames.push(f);
  }
  room.add(chand);
  flick.push(lampLight(room, lights, -4.6, 3.9, -3.6, '#ffc07a', 13, 5));
  // 丸テーブル
  ctx.glows = [];
  TABLES.forEach((t, k) => roundTable(kit, room, t, TAVERN_SEATS.slice(k * 3, k * 3 + 3), ctx.glows, flames));

  // ---- 外: 水晶玉・錬金釜・ストーンサークル・キノコ・パステルの木
  const orbStand = new THREE.Group();
  orbStand.position.set(-1.0, 0, 7.0);
  kit.cyl(orbStand, 0.8, 0.95, 0.3, '#8d8a9a', 0, 0.15, 0, 10);
  kit.cyl(orbStand, 0.35, 0.5, 1.1, '#a5a2b2', 0, 0.85, 0, 10);
  kit.cyl(orbStand, 0.6, 0.4, 0.25, '#8d8a9a', 0, 1.5, 0, 10);
  const big = new THREE.Mesh(new THREE.SphereGeometry(0.85, 28, 20), new THREE.MeshStandardMaterial({ color: '#f6d8ff', emissive: '#d890ff', emissiveIntensity: 0.9, transparent: true, opacity: 0.8, roughness: 0.1 }));
  big.position.y = 2.45;
  orbStand.add(big);
  const core = new THREE.Mesh(kit.geom('oct', 0.35, 0), new THREE.MeshBasicMaterial({ color: new THREE.Color('#ffffff').multiplyScalar(1.6), toneMapped: false }));
  core.position.y = 2.45;
  core.scale.y = 1.6;
  orbStand.add(core);
  root.add(orbStand);
  flick.push(lampLight(root, lights, -1.0, 2.6, 7.6, '#e8a8ff', 9, 4));
  const pot = new THREE.Group();
  pot.position.set(3.4, 0, 6.2);
  const bowl = new THREE.Mesh(kit.geom('sph', 0.75, 20, 14), kit.mat('#2a2a32'));
  bowl.scale.y = 0.8;
  bowl.position.y = 0.6;
  bowl.castShadow = true;
  pot.add(bowl);
  kit.cyl(pot, 0.62, 0.62, 0.1, '#3a3a44', 0, 1.2, 0, 20);
  const brew = new THREE.Mesh(new THREE.CircleGeometry(0.56, 24), new THREE.MeshBasicMaterial({ color: new THREE.Color('#5aff7a').multiplyScalar(1.3), toneMapped: false }));
  brew.rotation.x = -Math.PI / 2;
  brew.position.y = 1.26;
  pot.add(brew);
  const bubbles = [0, 1, 2, 3].map((i) => {
    const b = new THREE.Mesh(kit.geom('sph', 0.08, 8, 6), brew.material);
    pot.add(b);
    return { b, ph: i * 0.7, x: Math.cos(i * 2) * 0.3, z: Math.sin(i * 2) * 0.3 };
  });
  root.add(pot);
  propLabel(ctx, root, '🧪 錬金釜', 3.4, 2.1, 6.2);
  // ストーンサークル(光るルーンと魔法陣)
  const sc = { x: -9.5, z: 8.0 };
  const seal = magicCircle(3.4, '#6ff0ff', 0.7);
  seal.position.set(sc.x, 0.03, sc.z);
  root.add(seal);
  const runes = [];
  for (let i = 0; i < 7; i++) {
    const a = (i / 7) * Math.PI * 2 + 0.3;
    const h = 1.6 + ((i * 5) % 3) * 0.4;
    const st = new THREE.Group();
    st.position.set(sc.x + Math.cos(a) * 3.0, 0, sc.z + Math.sin(a) * 3.0);
    st.rotation.y = -a + Math.PI / 2;
    kit.box(st, 0.6, h, 0.42, '#8f8c9e', 0, h / 2, 0);
    const rune = new THREE.Mesh(kit.geom('box', 0.1, h * 0.5, 0.02), new THREE.MeshBasicMaterial({ color: new THREE.Color('#7ff4ff').multiplyScalar(1.4), toneMapped: false }));
    rune.position.set(0, h * 0.5, 0.22);
    st.add(rune);
    runes.push(rune);
    root.add(st);
  }
  // キノコ
  const mushroom = (x, z, s, cap, dots = true) => {
    kit.cyl(root, 0.18 * s, 0.25 * s, 0.9 * s, '#f3ead8', x, 0.45 * s, z, 10);
    const c = new THREE.Mesh(kit.geom('sph', 0.65 * s, 18, 10, 0, Math.PI * 2, 0, Math.PI / 2), kit.mat(cap));
    c.position.set(x, 0.85 * s, z);
    c.scale.y = 0.75;
    c.castShadow = true;
    root.add(c);
    if (dots) {
      for (let k = 0; k < 5; k++) {
        kit.sph(root, 0.08 * s, '#ffffff', x + Math.cos(k * 1.3) * 0.38 * s, 0.85 * s + 0.36 * s, z + Math.sin(k * 1.3) * 0.38 * s);
      }
    }
  };
  [[-15.5, 2.5, 1.3], [7.5, 11.0, 1.1], [13.5, 5.5, 0.9], [-4.5, 12.0, 0.8]].forEach(([x, z, s]) => mushroom(x, z, s, '#e2453f'));
  [[-13, 12, 0.5], [0.8, 11.6, 0.45], [10.5, 8.5, 0.5], [-6, 5.5, 0.4]].forEach(([x, z, s]) => mushroom(x, z, s, '#7fc8ff', false));
  // 木(パステルの広葉樹と、濃い針葉樹)
  [[-16, -6, 1.2, '#e9a3d0'], [10, -12, 1.2, '#e8c26a'], [14, -2, 1.3, '#b8a0e8'], [13, 10, 1.0, '#e9a3d0'], [-12.5, 13.5, 1.0, '#e8c26a'], [16, 4, 0.9, '#7fd8c0']].forEach(([x, z, s, c]) => tree(kit, root, x, z, s, c));
  [[-17, 2, 1.1], [-14, -12, 1.2], [8.5, -14, 1.0], [16.5, -8, 1.1], [5, 14, 0.9]].forEach(([x, z, s]) => pine(kit, root, x, z, s, '#22584a'));
  // 石畳の小道と街灯(ランタン)
  for (let i = 0; i < 6; i++) {
    kit.cyl(root, 0.42, 0.46, 0.08, '#c9c2b6', -2.2 + (i % 2) * 0.5, 0.04, 3.9 + i * 0.75, 10);
  }
  kit.cyl(root, 0.07, 0.1, 3.4, '#2a2420', -4.6, 1.7, 4.8, 8);
  kit.box(root, 0.6, 0.06, 0.06, '#2a2420', -4.35, 3.35, 4.8);
  kit.box(root, 0.32, 0.42, 0.32, '#ffe3a8', -4.1, 3.05, 4.8, { emissive: '#ffb347', emissiveIntensity: 1.2 });
  flick.push(lampLight(root, lights, -4.1, 2.9, 5.2, '#ffc27a', 9, 4));
  // 浮かぶ小さな結晶
  const crystals = [];
  for (const [x, y, z, c] of [[-18, 3.5, -4, '#ffffff'], [17, 4.5, 2, '#ffc0ec'], [-6, 5, 14, '#c8f4ff'], [12, 3, -13, '#ffffff']]) {
    const m = new THREE.Mesh(kit.geom('oct', 0.35, 0), new THREE.MeshBasicMaterial({ color: new THREE.Color(c).multiplyScalar(1.2), toneMapped: false }));
    m.scale.y = 1.7;
    m.position.set(x, y, z);
    m.userData.base = y;
    root.add(m);
    crystals.push(m);
  }
  // 「標準」モードだけの演出: 島じゅうに漂うきらきら
  const fx = new THREE.Group();
  root.add(fx);
  const glitter = sparkles(fx, { count: 90, box: [-18, 16, 0.4, 6, -14, 15], colors: ['#ffffff', '#ffd6f4', '#d8c8ff', '#b8f4ff', '#fff2b0'], size: 5 });
  ctx.fancy = { group: fx, update: (t) => glitter.update(t) };

  ctx.seats = TAVERN_SEATS;
  ctx.nav = {
    ...R,
    rects: [[2.2, 6.0, -6.95, -5.85], [R.x0, R.x0 + 1.0, -8.8, -4.2], [hearthX - 1.5, hearthX + 1.5, R.z0, R.z0 + 1.0], [3.0, 6.0, R.z0, R.z0 + 0.6], [4.8, 5.8, -3.9, -1.9]],
    circles: [...TABLES.map(([x, z]) => [x, z, 1.0]), [2.9, -5.45, 0.3], [5.1, -5.45, 0.3], [4.6, 2.2, 0.5]],
  };
  ctx.cloudsObj = clouds(kit, root, '#fff4fc');
  ctx.keys = ISEKAI_KEYS;
  ctx.hemi = { sky: '#f6eaff', ground: '#8a7a9a', boost: 1.25 };
  ctx.sky = { aurora: 0, crack: 0, moon2: false, moonColor: '#f4f0ff', big: { x: 0.66, y: 0.86, r: 0.12, color: '#f2ecff' } };
  ctx.panel = 'isekai';
  ctx.update = (t) => {
    // 暖炉・ろうそく・ランタンは昼もほんのり灯り、ゆらめく
    for (const l of flick) {
      l.intensity = Math.max(l.intensity, l.userData.power * 0.6) * (1 + Math.sin(t * 9 + l.position.x) * 0.08 + Math.sin(t * 23 + l.position.z) * 0.05);
    }
    flames.forEach((f, i) => {
      f.scale.set(1 + Math.sin(t * 11 + i) * 0.1, 1 + Math.sin(t * 8 + i * 2) * 0.18, 1);
    });
    core.rotation.y = t * 0.8;
    big.material.emissiveIntensity = 0.75 + Math.sin(t * 1.5) * 0.15;
    bubbles.forEach(({ b, ph, x, z }) => {
      const k = (t * 0.6 + ph) % 1.4;
      b.visible = k < 1;
      b.position.set(x, 1.26 + k * 0.25, z);
      b.scale.setScalar(0.6 + k * 0.6);
    });
    runes.forEach((r, i) => {
      r.material.color.set('#7ff4ff').multiplyScalar(1.1 + Math.sin(t * 2 + i) * 0.3);
    });
    seal.rotation.z = t * 0.05;
    crystals.forEach((m, i) => {
      m.position.y = m.userData.base + Math.sin(t * 0.8 + i * 1.7) * 0.4;
      m.rotation.y = t * 0.5 + i;
    });
    chand.rotation.y = Math.sin(t * 0.4) * 0.1;
  };
}
