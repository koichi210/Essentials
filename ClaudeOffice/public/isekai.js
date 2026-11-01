// 追加スキン「冒険者の酒場」(id: isekai)。楕円の浮島に木組みの酒場、丸テーブル4卓×3席
import * as THREE from 'three';
import { island, tree, lampLight, wallClock, calendar, archiveShelf, whiteboard, clouds } from './world.js';

const KEYS = [
  ['#120a2e', '#2a1650', '#4a2468', '#b9a6ff', 0.35, 0.4, 1.0, '#3c2c66', 1.0],
  ['#160c34', '#2e1856', '#52286e', '#b9a6ff', 0.35, 0.4, 1.0, '#40306a', 0.9],
  ['#3a2470', '#b0608e', '#f0a07c', '#ffb98a', 0.6, 0.5, 0.6, '#c39ab8', 0.3],
  ['#6a7fe0', '#b4a8ee', '#f2d2c0', '#ffe2c0', 0.95, 0.75, 0.1, '#f3eaff', 0.0],
  ['#5d86e6', '#a6b6f2', '#e6dcf7', '#fff4e6', 1.15, 0.85, 0.0, '#ffffff', 0.0],
  ['#6385e0', '#ab9ff0', '#e8d6f3', '#fff0dc', 1.05, 0.8, 0.0, '#ffffff', 0.0],
  ['#5c6ad0', '#d898a6', '#f6bc96', '#ffc08a', 0.9, 0.7, 0.3, '#f0d0d6', 0.0],
  ['#3a2c88', '#c0607e', '#f08a6a', '#ff9a6a', 0.65, 0.55, 0.8, '#c88aa8', 0.3],
  ['#1a1050', '#3b2378', '#6a3a8a', '#b9a6ff', 0.4, 0.45, 1.0, '#45306e', 0.8],
  ['#120a2e', '#2a1650', '#4a2468', '#b9a6ff', 0.35, 0.4, 1.0, '#3c2c66', 1.0],
];

const TABLES = [[-6.5, -6.2], [-0.5, -6.2], [-6.5, -0.8], [-0.5, -0.8]];

export function buildIsekai(ctx) {
  const { kit, root, lights } = ctx;
  ctx.keys = KEYS;
  ctx.sky = { aurora: 0.8, crack: 0, moon2: true, moonColor: '#f6efd8' };
  ctx.fog = new THREE.FogExp2('#3a2a5a', 0.008);
  ctx.bloom = true;
  island(kit, root, 36, 30, { ellipse: true, top: '#5aa860', soil: '#6a4a32', rock: '#5c5870' });
  const room = new THREE.Group();
  root.add(room);
  const R = { x0: -12, x1: 6, z0: -10, z1: 3 };
  kit.box(room, R.x1 - R.x0, 0.14, R.z1 - R.z0, '#8a5a35', -3, 0.07, -3.5);
  kit.box(room, 14, 0.03, 10, '#7a2f3a', -3.5, 0.15, -3.5);
  // 木組みの壁(漆喰＋梁)
  kit.box(room, R.x1 - R.x0, 5.2, 0.3, '#efe3c8', -3, 2.6, R.z0 - 0.15);
  kit.box(room, 0.3, 5.2, R.z1 - R.z0, '#efe3c8', R.x0 - 0.15, 2.6, -3.5);
  for (let x = R.x0; x <= R.x1; x += 3) {
    kit.box(room, 0.28, 5.3, 0.36, '#5a3820', x, 2.65, R.z0 - 0.1);
  }
  for (let z = R.z0; z <= R.z1; z += 3.25) {
    kit.box(room, 0.36, 5.3, 0.28, '#5a3820', R.x0 - 0.1, 2.65, z);
  }
  kit.box(room, R.x1 - R.x0, 0.3, 0.4, '#5a3820', -3, 5.2, R.z0 - 0.1);
  kit.box(room, 0.4, 0.3, R.z1 - R.z0, '#5a3820', R.x0 - 0.1, 5.2, -3.5);
  kit.box(room, R.x1 - R.x0, 0.25, 0.36, '#5a3820', -3, 1.5, R.z0 - 0.1);
  // ステンドグラス風の窓
  for (const wx of [-4.5, 3]) {
    ['#e05a7a', '#5a8ae0', '#e0c05a', '#5ae0a0'].forEach((c, i) => {
      kit.box(room, 0.6, 1.0, 0.05, c, wx - 0.9 + i * 0.6, 3.2, R.z0 + 0.04, { emissive: c, emissiveIntensity: 0.6 });
    });
  }
  ctx.clock = wallClock(kit, room, -1.2, 3.4, R.z0 + 0.02, 0, '#7a4a20');
  ctx.calendar = calendar(room, 0.6, 3.2, R.z0 + 0.04, 0);
  // カウンターと酒樽
  kit.box(room, 1.0, 1.1, 5, '#6b4428', 4.6, 0.55, -5.5);
  for (let i = 0; i < 3; i++) {
    kit.cyl(room, 0.45, 0.45, 0.9, '#8a5a35', 5.3, 0.45, -8.8 + i * 1.0, 12);
  }
  ctx.seats = [];
  ctx.glows = [];
  ctx.rings = [];
  for (const [cx, cz] of TABLES) {
    kit.cyl(room, 1.25, 1.25, 0.1, '#8a5a35', cx, 1.0, cz, 20);
    kit.cyl(room, 0.15, 0.25, 1.0, '#5a3820', cx, 0.5, cz, 10);
    kit.cyl(room, 0.05, 0.05, 0.22, '#f5ecd0', cx, 1.16, cz, 8, { emissive: '#ffd27a', emissiveIntensity: 0.5 });
    lampLight(room, lights, cx, 1.6, cz, '#ffb060', 5, 2.5);
    kit.box(room, 0.38, 0.08, 0.28, '#5a2a6a', cx + 0.45, 1.09, cz - 0.4);
    for (let k = 0; k < 3; k++) {
      const a = Math.PI / 6 + (k * 2 * Math.PI) / 3;
      const sx = cx + Math.sin(a) * 1.75;
      const sz = cz + Math.cos(a) * 1.75;
      ctx.seats.push({ x: sx, z: sz, rot: Math.atan2(cx - sx, cz - sz) });
      kit.cyl(room, 0.35, 0.35, 0.08, '#6b4428', sx, 0.62, sz, 12);
      kit.cyl(room, 0.06, 0.06, 0.6, '#5a3820', sx, 0.3, sz, 6);
      // 席ごとの水晶玉(状態で光る)とリング
      const bx = cx + Math.sin(a) * 0.7;
      const bz = cz + Math.cos(a) * 0.7;
      const ball = new THREE.Mesh(kit.geom('sph', 0.2, 16, 12), new THREE.MeshBasicMaterial({ color: '#8a7ab8', toneMapped: false }));
      ball.position.set(bx, 1.3, bz);
      room.add(ball);
      const ring = new THREE.Mesh(kit.geom('torus', 0.28, 0.02, 6, 24), new THREE.MeshBasicMaterial({ color: '#e9c46a', toneMapped: false }));
      ring.position.copy(ball.position);
      room.add(ring);
      ctx.rings.push(ring);
      ctx.glows.push({ screen: ball, logo: ring });
      kit.cyl(room, 0.09, 0.08, 0.2, '#c9a24a', cx + Math.sin(a + 0.5) * 0.85, 1.15, cz + Math.cos(a + 0.5) * 0.85, 8);
    }
  }
  ctx.shelf = archiveShelf(kit, room, '#5a3820');
  ctx.board = whiteboard(kit, room, '#5a3820', '#f3e4c4');
  // 外: 木・ランタン・魔法陣
  for (const [tx, tz, s] of [[-15, -9, 1.2], [10, -9, 1.1], [13, -2, 1.3], [-14, 7, 1], [11, 8, 1.1], [-8, 11, 1]]) {
    tree(kit, root, tx, tz, s, '#2f6f5a');
  }
  for (const [px, pz] of [[-3, 7.5], [8, 3]]) {
    kit.cyl(root, 0.07, 0.09, 3, '#3a2a1a', px, 1.5, pz, 8);
    kit.box(root, 0.4, 0.5, 0.4, '#ffcf7a', px, 3.2, pz, { emissive: '#ffb040', emissiveIntensity: 0.8 });
    lampLight(root, lights, px, 3.1, pz, '#ffc070', 9, 6);
  }
  const circle = new THREE.Mesh(new THREE.RingGeometry(1.6, 1.8, 48), new THREE.MeshBasicMaterial({ color: '#7ad0ff', toneMapped: false, transparent: true, opacity: 0.8 }));
  circle.rotation.x = -Math.PI / 2;
  circle.position.set(4, 0.05, 8);
  root.add(circle);
  // ホタル
  const n = 70;
  const pos = new Float32Array(n * 3);
  const seeds = [];
  for (let i = 0; i < n; i++) {
    seeds.push([(Math.random() - 0.5) * 32, 0.5 + Math.random() * 3, (Math.random() - 0.5) * 26, Math.random() * 10]);
  }
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  const flies = new THREE.Points(geo, new THREE.PointsMaterial({ color: '#d8ff7a', size: 0.22, transparent: true, opacity: 0.9, toneMapped: false }));
  root.add(flies);
  ctx.cloudsObj = clouds(kit, root, '#d8c8ff');
  ctx.update = (t, look) => {
    for (const r of ctx.rings) {
      r.rotation.x = t * 1.3;
      r.rotation.y = t * 0.9;
    }
    seeds.forEach((s, i) => {
      pos[i * 3] = s[0] + Math.sin(t * 0.5 + s[3]) * 1.2;
      pos[i * 3 + 1] = s[1] + Math.sin(t * 0.9 + s[3] * 2) * 0.5;
      pos[i * 3 + 2] = s[2] + Math.cos(t * 0.4 + s[3]) * 1.2;
    });
    geo.attributes.position.needsUpdate = true;
    flies.material.opacity = 0.3 + look.stars * 0.7;
    flies.visible = ctx.effects;
  };
}
