// 席のメンバーが立ち寄る場所。教室=金魚の水槽(エサやり)、酒場=カウンター(お酒をもらう)、魔界=魔女の大釜(薬を汲む)。
// ctx.spot = { kind, x, z, rot, makeProp?, update(t, busy) }。x,z は立つ位置で、rot はそのとき向く角度。
// 置いた家具は ctx.nav に足して、歩く経路がよけるようにする
import * as THREE from 'three';
import { lampLight, propLabel } from './world.js';
import { magicCircle } from './tavern.js';

const FACE_PLUS_X = Math.PI / 2;

function tank(ctx) {
  const { kit, root } = ctx;
  const g = new THREE.Group();
  g.position.set(5.2, 0.12, -4.55);
  root.add(g);
  kit.box(g, 1.1, 0.9, 2.6, '#8a6b4a', 0, 0.45, 0);
  kit.box(g, 1.15, 0.06, 2.65, '#5a4430', 0, 0.93, 0);
  const water = new THREE.Mesh(new THREE.BoxGeometry(1.0, 0.8, 2.4), new THREE.MeshBasicMaterial({ color: '#5cc8f0', transparent: true, opacity: 0.32, depthWrite: false }));
  water.position.set(0, 1.36, 0);
  g.add(water);
  const glass = new THREE.Mesh(new THREE.BoxGeometry(1.04, 0.9, 2.44), new THREE.MeshBasicMaterial({ color: '#dff6ff', transparent: true, opacity: 0.12, depthWrite: false }));
  glass.position.set(0, 1.4, 0);
  g.add(glass);
  for (const sx of [-1, 1]) {
    for (const sz of [-1, 1]) {
      kit.box(g, 0.05, 0.9, 0.05, '#eef4f6', sx * 0.52, 1.4, sz * 1.22);
    }
    kit.box(g, 0.05, 0.05, 2.44, '#eef4f6', sx * 0.52, 1.85, 0);
  }
  for (const sz of [-1, 1]) {
    kit.box(g, 1.04, 0.05, 0.05, '#eef4f6', 0, 1.85, sz * 1.22);
  }
  kit.box(g, 0.98, 0.08, 2.38, '#efe0b0', 0, 1.0, 0);
  for (const [px, pz, h] of [[0.3, -0.8, 0.6], [0.25, 0.7, 0.5], [-0.3, -0.2, 0.4]]) {
    const w = kit.cyl(g, 0.02, 0.06, h, '#3cae5a', px, 1.04 + h / 2, pz, 5);
    w.rotation.z = px * 0.3;
    kit.cyl(g, 0.02, 0.05, h * 0.7, '#2f9a4a', px + 0.08, 1.04 + h * 0.35, pz + 0.06, 5);
  }
  kit.sph(g, 0.12, '#8a8a95', -0.25, 1.06, 0.55);
  kit.sph(g, 0.09, '#a0a0aa', -0.15, 1.05, 0.7);
  // 金魚
  const fish = [];
  for (let i = 0; i < 6; i++) {
    const f = new THREE.Group();
    const color = i % 3 === 0 ? '#ff7a1a' : i % 3 === 1 ? '#ff4a2a' : '#ffa82a';
    const body = kit.sph(f, 0.09, color, 0, 0, 0);
    body.scale.set(0.8, 0.85, 1.6);
    const tail = kit.geom('cone', 0.09, 0.2, 4);
    const tm = new THREE.Mesh(tail, kit.mat(color));
    tm.rotation.x = -Math.PI / 2;
    tm.position.z = -0.2;
    tm.scale.x = 0.4;
    f.add(tm);
    kit.sph(f, 0.018, '#111', 0.05, 0.03, 0.12);
    kit.sph(f, 0.018, '#111', -0.05, 0.03, 0.12);
    g.add(f);
    fish.push({ f, tm, ph: i * 1.7, sp: 0.45 + (i % 3) * 0.15, y: 1.25 + (i % 4) * 0.13 });
  }
  // エサのつぶ
  const food = [];
  for (let i = 0; i < 10; i++) {
    const m = new THREE.Mesh(new THREE.BoxGeometry(0.04, 0.02, 0.04), new THREE.MeshBasicMaterial({ color: '#c8a040' }));
    m.visible = false;
    g.add(m);
    food.push({ m, ph: i * 0.37 });
  }
  ctx.nav.rects.push([4.6, 5.8, -5.9, -3.2]);
  propLabel(ctx, g, '🐠 金魚の水槽', 0, 2.5, 0);
  let w = 0;
  return {
    kind: 'feed',
    x: 3.7,
    z: -4.55,
    rot: FACE_PLUS_X,
    update(t, busy) {
      w += ((busy ? 1 : 0) - w) * 0.06;
      for (const s of fish) {
        // ふだんは水槽の中を楕円にゆったり、エサの時間は水面のエサの下へ集まる
        const a = t * s.sp + s.ph;
        const wx = Math.sin(a * 1.3) * 0.32;
        const wz = Math.sin(a) * 0.95;
        const fx = -0.25 + Math.sin(a * 3) * 0.12;
        const fz = Math.cos(a * 2) * 0.2;
        const x = wx + (fx - wx) * w;
        const z = wz + (fz - wz) * w;
        const y = s.y + (1.65 - s.y) * w * 0.8 + Math.sin(a * 2) * 0.05;
        const dx = Math.cos(a * 1.3) * 0.32 * 1.3 * (1 - w) + Math.cos(a * 3) * 0.12 * 3 * w;
        const dz = Math.cos(a) * 0.95 * (1 - w) - Math.sin(a * 2) * 0.4 * w;
        s.f.position.set(x, y, z);
        s.f.rotation.y = Math.atan2(dx, dz * 1.0 + (busy ? 0 : 0));
        s.tm.rotation.z = Math.sin(t * 9 + s.ph) * 0.5;
      }
      food.forEach((p, i) => {
        p.m.visible = busy;
        if (busy) {
          const k = (t * 0.6 + p.ph) % 1;
          p.m.position.set(-0.25 + Math.sin(i * 2.1) * 0.15, 1.8 - k * 0.7, Math.cos(i * 1.7) * 0.25);
        }
      });
    },
  };
}

// 酒場: カウンターの前でジョッキを受け取る(カウンターと酒瓶の棚は tavern.js が置く)
function barCounter(ctx) {
  const { kit } = ctx;
  const mug = () => {
    const m = new THREE.Group();
    kit.cyl(m, 0.1, 0.09, 0.2, '#e8c070', 0, 0, 0, 10);
    kit.cyl(m, 0.1, 0.1, 0.05, '#ffffff', 0, 0.12, 0, 10);
    const h = new THREE.Mesh(kit.geom('torus', 0.06, 0.018, 6, 10), kit.mat('#e8c070'));
    h.position.x = 0.12;
    m.add(h);
    m.position.set(0, -0.5, 0.1);
    m.rotation.x = 1.2;
    return m;
  };
  return { kind: 'sake', x: 4.0, z: -5.2, rot: Math.PI, makeProp: mug, update() {} };
}

// 魔界: 緑に光る魔法陣の上の大釜。ぐつぐつ泡立ち、汲みに来ると湯気が増える
function cauldron(ctx) {
  const { kit, root, lights } = ctx;
  const g = new THREE.Group();
  g.position.set(3.4, 0.14, -0.6);
  root.add(g);
  const seal = magicCircle(1.7, '#6aff8a', 0.85);
  seal.position.y = 0.02;
  g.add(seal);
  const pot = new THREE.Mesh(kit.geom('sph', 0.8, 22, 16), kit.mat('#1e1a22'));
  pot.scale.y = 0.8;
  pot.position.y = 0.7;
  pot.castShadow = true;
  g.add(pot);
  kit.cyl(g, 0.68, 0.68, 0.12, '#2e2836', 0, 1.33, 0, 22);
  for (let i = 0; i < 3; i++) {
    const a = (i / 3) * Math.PI * 2;
    kit.box(g, 0.12, 0.4, 0.12, '#2e2836', Math.cos(a) * 0.55, 0.2, Math.sin(a) * 0.55);
  }
  const brewMat = new THREE.MeshBasicMaterial({ color: new THREE.Color('#5aff7a').multiplyScalar(1.4), toneMapped: false });
  const brew = new THREE.Mesh(new THREE.CircleGeometry(0.62, 24), brewMat);
  brew.rotation.x = -Math.PI / 2;
  brew.position.y = 1.4;
  g.add(brew);
  const bubbles = [];
  for (let i = 0; i < 7; i++) {
    const b = new THREE.Mesh(kit.geom('sph', 0.09, 8, 6), brewMat);
    g.add(b);
    bubbles.push({ b, ph: i * 0.37, x: Math.cos(i * 2.4) * 0.35, z: Math.sin(i * 2.4) * 0.35 });
  }
  lampLight(g, lights, 0, 1.8, 0.4, '#6aff8a', 8, 5);
  propLabel(ctx, g, '🧪 魔女の大釜', 0, 2.6, 0);
  ctx.nav.circles.push([3.4, -0.6, 0.9]);
  const cup = () => {
    const m = new THREE.Group();
    kit.cyl(m, 0.1, 0.06, 0.16, '#b8902a', 0, 0, 0, 10);
    kit.cyl(m, 0.095, 0.095, 0.02, '#5aff7a', 0, 0.075, 0, 10, { emissive: '#3aff6a', emissiveIntensity: 1 });
    kit.cyl(m, 0.03, 0.03, 0.1, '#b8902a', 0, -0.12, 0, 6);
    m.position.set(0, -0.5, 0.1);
    m.rotation.x = 1.2;
    return m;
  };
  return {
    kind: 'blood',
    x: 2.0,
    z: -0.6,
    rot: FACE_PLUS_X,
    makeProp: cup,
    update(t, busy) {
      seal.rotation.z = t * 0.15;
      bubbles.forEach(({ b, ph, x, z }) => {
        const k = (t * (busy ? 1.1 : 0.6) + ph) % 1.2;
        b.visible = k < 1;
        b.position.set(x, 1.4 + k * 0.35, z);
        b.scale.setScalar(0.5 + k * 0.8);
      });
    },
  };
}

export function buildSpot(ctx, skin) {
  if (skin === 'makai') {
    ctx.spot = cauldron(ctx);
  } else if (skin === 'isekai') {
    ctx.spot = barCounter(ctx);
  } else {
    ctx.spot = tank(ctx);
  }
}
