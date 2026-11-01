// 席のメンバーが立ち寄る場所。教室=金魚の水槽(エサやり)、酒場=お酒の棚(取りに行く)、魔界=血の池(汲みに行く)。
// ctx.spot = { kind, x, z, rot, makeProp?, update(t, busy) }。x,z は立つ位置(右端の通路の横)で、rot はそのとき向く角度
import * as THREE from 'three';
import { skull } from './makai.js';

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

function sakeRack(ctx) {
  const { kit, root } = ctx;
  const g = new THREE.Group();
  g.position.set(0, 0.12, 0);
  root.add(g);
  kit.box(g, 0.12, 2.3, 2.4, '#8a5a30', 5.85, 1.15, -0.9);
  for (const z of [-2.1, 0.3]) {
    kit.box(g, 0.7, 2.3, 0.1, '#a8693a', 5.55, 1.15, z);
  }
  const colors = ['#e24a4a', '#3d85c6', '#f0c020', '#2f9e5a', '#9c6ade', '#ff8a3a'];
  [0.5, 1.2, 1.9].forEach((y, r) => {
    kit.box(g, 0.6, 0.07, 2.4, '#c98a4a', 5.55, y, -0.9);
    for (let k = 0; k < 6; k++) {
      const c = colors[(k + r * 2) % colors.length];
      const z = -1.85 + k * 0.4;
      kit.cyl(g, 0.09, 0.1, 0.34, c, 5.55, y + 0.21, z, 8, { emissive: c, emissiveIntensity: 0.25 });
      kit.cyl(g, 0.035, 0.05, 0.14, c, 5.55, y + 0.46, z, 6);
    }
  });
  // 酒樽
  for (const [bx, bz] of [[4.7, -1.75], [4.75, -0.3]]) {
    kit.cyl(g, 0.42, 0.4, 0.9, '#a86a30', bx, 0.45, bz, 14);
    for (const y of [0.18, 0.72]) {
      kit.cyl(g, 0.44, 0.44, 0.06, '#4a3a2a', bx, y, bz, 14);
    }
    kit.cyl(g, 0.1, 0.1, 0.12, '#d8b060', bx - 0.38, 0.35, bz, 8).rotation.z = Math.PI / 2;
  }
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
  return { kind: 'sake', x: 3.7, z: -1.05, rot: FACE_PLUS_X, makeProp: mug, update() {} };
}

function bloodPond(ctx) {
  const { kit, root } = ctx;
  const g = new THREE.Group();
  g.position.set(5.4, 0.12, -4.55);
  root.add(g);
  const R = 1.2;
  kit.cyl(g, R + 0.25, R + 0.3, 0.3, '#3a2a40', 0, 0.15, 0, 20);
  const pool = new THREE.Mesh(new THREE.CircleGeometry(R, 32), new THREE.MeshBasicMaterial({ color: new THREE.Color('#c00a1c').multiplyScalar(1.15), toneMapped: false }));
  pool.rotation.x = -Math.PI / 2;
  pool.position.y = 0.32;
  g.add(pool);
  const ripples = [0, 1].map(() => {
    const m = new THREE.Mesh(new THREE.RingGeometry(0.9, 1, 32), new THREE.MeshBasicMaterial({ color: '#ff6a6a', transparent: true, toneMapped: false, side: THREE.DoubleSide }));
    m.rotation.x = -Math.PI / 2;
    m.position.y = 0.33;
    g.add(m);
    return m;
  });
  for (let i = 0; i < 6; i++) {
    const a = (i / 6) * Math.PI * 2 + 0.3;
    skull(kit, g, Math.cos(a) * (R + 0.28), 0.5, Math.sin(a) * (R + 0.28), 0.45, -a + Math.PI / 2);
  }
  const bubbles = [];
  for (let i = 0; i < 5; i++) {
    const b = new THREE.Mesh(new THREE.SphereGeometry(0.1, 8, 6), new THREE.MeshBasicMaterial({ color: '#ff3a3a', toneMapped: false }));
    g.add(b);
    bubbles.push({ b, ph: i * 1.3, x: Math.cos(i * 2.4) * 0.6, z: Math.sin(i * 2.4) * 0.6 });
  }
  const cup = () => {
    const m = new THREE.Group();
    kit.cyl(m, 0.1, 0.06, 0.16, '#d8b040', 0, 0, 0, 10);
    kit.cyl(m, 0.095, 0.095, 0.02, '#c00a1c', 0, 0.075, 0, 10, { emissive: '#ff1a1a', emissiveIntensity: 1 });
    kit.cyl(m, 0.03, 0.03, 0.1, '#d8b040', 0, -0.12, 0, 6);
    m.position.set(0, -0.5, 0.1);
    m.rotation.x = 1.2;
    return m;
  };
  return {
    kind: 'blood',
    x: 3.6,
    z: -4.55,
    rot: FACE_PLUS_X,
    makeProp: cup,
    update(t, busy) {
      pool.material.color.set('#c00a1c').multiplyScalar(1.1 + Math.sin(t * 1.5) * 0.15);
      ripples.forEach((r, i) => {
        const k = ((t * (busy ? 0.9 : 0.35)) + i * 0.5) % 1;
        r.scale.setScalar(0.2 + k * 0.9);
        r.material.opacity = (1 - k) * 0.6;
      });
      bubbles.forEach(({ b, ph, x, z }) => {
        const k = (t * 0.5 + ph) % 1.6;
        b.position.set(x, 0.33 + Math.max(0, Math.sin(Math.min(k, 0.6) / 0.6 * Math.PI)) * 0.12, z);
        b.visible = k < 0.6;
      });
    },
  };
}

export function buildSpot(ctx, skin) {
  if (skin === 'makai') {
    ctx.spot = bloodPond(ctx);
  } else if (skin === 'isekai') {
    ctx.spot = sakeRack(ctx);
  } else {
    ctx.spot = tank(ctx);
  }
}
