// 追加スキン「魔界」(id: makai)。溶岩の割れ目が光る黒い浮島の魔王城・玉座の間。長机の両側12席
import * as THREE from 'three';
import { island, lampLight, wallClock, calendar, archiveShelf, whiteboard, clouds } from './world.js';

const KEYS = [
  ['#05020a', '#1a0612', '#3a0a1a', '#ff6a5a', 0.3, 0.35, 1.0, '#2a1020', 0.9],
  ['#06030c', '#1c0714', '#3e0c1c', '#ff6a5a', 0.3, 0.35, 1.0, '#2c1222', 0.8],
  ['#1a0820', '#5a1a30', '#a03a30', '#ff8a5a', 0.45, 0.4, 0.8, '#4a1a2a', 0.4],
  ['#2a0a2a', '#6a1a3a', '#b0402a', '#ffa070', 0.65, 0.5, 0.5, '#5a2030', 0.2],
  ['#300c30', '#701c3c', '#c04a30', '#ffb080', 0.8, 0.55, 0.4, '#602434', 0.1],
  ['#2e0b2e', '#6c1b3a', '#bc462e', '#ffa878', 0.75, 0.55, 0.45, '#5e2232', 0.1],
  ['#240826', '#621632', '#b03a28', '#ff8a5a', 0.6, 0.5, 0.6, '#521c2c', 0.2],
  ['#16051a', '#4a1028', '#902a24', '#ff6a5a', 0.45, 0.42, 0.9, '#3a1424', 0.5],
  ['#07030c', '#1e0816', '#420e1e', '#ff6a5a', 0.32, 0.36, 1.0, '#2c1222', 0.8],
  ['#05020a', '#1a0612', '#3a0a1a', '#ff6a5a', 0.3, 0.35, 1.0, '#2a1020', 0.9],
];

export function buildMakai(ctx) {
  const { kit, root, lights } = ctx;
  ctx.keys = KEYS;
  ctx.sky = { aurora: 0, crack: 1, moon2: false, moonColor: '#ff3a3a' };
  ctx.fog = null;
  // 黒い素材ばかりで夜は沈むので、半球光を強めて赤みを足す
  ctx.hemi = { boost: 2.2, sky: '#ffb8c8', ground: '#5a1a2a' };
  ctx.bloom = true;
  island(kit, root, 34, 28, { top: '#1c1a22', soil: '#24141a', rock: '#160c12' });
  // 溶岩の割れ目
  const lava = new THREE.MeshBasicMaterial({ color: '#ff5a1a', toneMapped: false });
  for (let i = 0; i < 26; i++) {
    const len = 1.5 + (i % 5) * 0.8;
    const m = new THREE.Mesh(kit.geom('box', len, 0.06, 0.14), lava);
    const a = i * 2.39;
    const r = 10 + (i % 4) * 1.6;
    m.position.set(-3 + Math.cos(a) * r, 0.02, -3 + Math.sin(a) * r * 0.75);
    m.rotation.y = a * 1.7;
    root.add(m);
  }
  const room = new THREE.Group();
  root.add(room);
  const R = { x0: -12, x1: 6, z0: -10, z1: 3 };
  kit.box(room, R.x1 - R.x0, 0.14, R.z1 - R.z0, '#2a2430', -3, 0.07, -3.5);
  kit.box(room, 3, 0.03, 12, '#7a1020', -2, 0.15, -3.5);
  kit.box(room, R.x1 - R.x0, 5.6, 0.4, '#24202c', -3, 2.8, R.z0 - 0.2);
  kit.box(room, 0.4, 5.6, R.z1 - R.z0, '#24202c', R.x0 - 0.2, 2.8, -3.5);
  for (let x = R.x0 + 1.5; x < R.x1; x += 4.5) {
    kit.box(room, 0.9, 2.4, 0.05, '#8a1020', x, 3.6, R.z0 + 0.03);
    kit.box(room, 0.35, 0.35, 0.06, '#e9b44c', x, 4.2, R.z0 + 0.06);
  }
  // 玉座
  kit.box(room, 1.6, 0.5, 1.2, '#3a1020', -2, 0.25, R.z0 + 1.2);
  kit.box(room, 1.6, 3.0, 0.3, '#5a1428', -2, 1.9, R.z0 + 0.7);
  ctx.clock = wallClock(kit, room, 2.8, 3.6, R.z0 + 0.04, 0, '#8a1020');
  ctx.calendar = calendar(room, 4.4, 3.4, R.z0 + 0.04, 0);
  // 長机の両側12席
  kit.box(room, 12.5, 0.12, 2.0, '#1a1418', -2.0, 1.0, -3.5);
  for (const x of [-7.8, 3.8]) {
    for (const z of [-4.2, -2.8]) {
      kit.box(room, 0.18, 1.0, 0.18, '#120c10', x, 0.5, z);
    }
  }
  ctx.seats = [];
  ctx.glows = [];
  const xs = [-7, -4.6, -2.2, 0.2, 2.6, 5.0].map((x) => x - 1.6);
  for (const side of [-1, 1]) {
    for (const x of xs) {
      const z = -3.5 + side * 1.6;
      ctx.seats.push({ x, z, rot: side < 0 ? 0 : Math.PI });
      kit.box(room, 0.8, 0.1, 0.8, '#2a0a14', x, 0.62, z);
      kit.box(room, 0.8, 1.4, 0.12, '#2a0a14', x, 1.25, z + side * 0.42);
      const ball = new THREE.Mesh(kit.geom('sph', 0.2, 16, 12), new THREE.MeshBasicMaterial({ color: '#4a2a5a', toneMapped: false }));
      ball.position.set(x, 1.28, -3.5 + side * 0.55);
      room.add(ball);
      const cup = kit.cyl(room, 0.08, 0.05, 0.22, '#3a3a40', x + 0.45, 1.17, -3.5 + side * 0.5, 8);
      ctx.glows.push({ screen: ball, logo: cup });
    }
  }
  // どくろの燭台
  for (const x of [-6, -2, 2]) {
    kit.sph(room, 0.16, '#e8e2d0', x, 1.2, -3.5);
    kit.cyl(room, 0.04, 0.04, 0.3, '#f5ecd0', x, 1.45, -3.5, 6, { emissive: '#ff9a3a', emissiveIntensity: 1 });
    lampLight(room, lights, x, 1.9, -3.5, '#ff7a3a', 6, 3);
  }
  ctx.shelf = archiveShelf(kit, room, '#2a1a24');
  ctx.board = whiteboard(kit, room, '#3a2a34', '#e6dad6');
  // 外: とげ岩と魔法陣
  for (const [tx, tz, s] of [[-15, -8, 1.4], [10, -9, 1.2], [13, 0, 1.6], [-13, 7, 1.1], [10, 8, 1.3]]) {
    const m = new THREE.Mesh(kit.geom('cone', 0.8 * s, 3.5 * s, 5), kit.mat('#1a1420', { flatShading: true }));
    m.position.set(tx, 1.75 * s, tz);
    m.castShadow = true;
    root.add(m);
  }
  const circle = new THREE.Mesh(new THREE.RingGeometry(1.6, 1.85, 48), new THREE.MeshBasicMaterial({ color: '#7aff6a', toneMapped: false, transparent: true, opacity: 0.85 }));
  circle.rotation.x = -Math.PI / 2;
  circle.position.set(3, 0.05, 7);
  root.add(circle);
  lampLight(root, lights, 3, 1, 7, '#7aff6a', 7, 4);
  // 火の粉
  const n = 90;
  const pos = new Float32Array(n * 3);
  const seeds = [];
  for (let i = 0; i < n; i++) {
    seeds.push([(Math.random() - 0.5) * 30, Math.random() * 8, (Math.random() - 0.5) * 24, 0.4 + Math.random()]);
  }
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  const embers = new THREE.Points(geo, new THREE.PointsMaterial({ color: '#ff8a3a', size: 0.16, toneMapped: false }));
  root.add(embers);
  ctx.cloudsObj = clouds(kit, root, '#2a1020');
  ctx.update = (t) => {
    lava.color.setHSL(0.04, 1, 0.5 + Math.sin(t * 1.5) * 0.08);
    seeds.forEach((s, i) => {
      pos[i * 3] = s[0] + Math.sin(t + i) * 0.3;
      pos[i * 3 + 1] = (s[1] + t * s[3]) % 8;
      pos[i * 3 + 2] = s[2];
    });
    geo.attributes.position.needsUpdate = true;
    embers.visible = ctx.effects;
  };
}
