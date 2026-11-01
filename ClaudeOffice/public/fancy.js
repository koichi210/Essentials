// 教室スキンの「標準」モード用の豪華でハデな演出。動作モード「軽量」(エフェクト OFF)では丸ごと非表示にして、いまの見た目のまま。
// 色は HDR(1 を超える値)にして、ブルームで光らせる。
import * as THREE from 'three';
import { CLASSROOM_SEATS } from './world.js';

export const ADD = { blending: THREE.AdditiveBlending, depthWrite: false, transparent: true, toneMapped: false };

export function dotTexture(star) {
  const c = document.createElement('canvas');
  c.width = c.height = 64;
  const g = c.getContext('2d');
  const grad = g.createRadialGradient(32, 32, 0, 32, 32, 30);
  grad.addColorStop(0, 'rgba(255,255,255,1)');
  grad.addColorStop(0.35, 'rgba(255,255,255,0.55)');
  grad.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = grad;
  g.fillRect(0, 0, 64, 64);
  if (star) {
    g.strokeStyle = 'rgba(255,255,255,0.9)';
    g.lineWidth = 2;
    g.beginPath();
    g.moveTo(32, 2);
    g.lineTo(32, 62);
    g.moveTo(2, 32);
    g.lineTo(62, 32);
    g.stroke();
  }
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

export const hdr = (h, s, l, k) => new THREE.Color().setHSL(h, s, l).multiplyScalar(k);

export function buildFancy(ctx) {
  const g = new THREE.Group();
  ctx.root.add(g);
  const dot = dotTexture(false);
  const parts = [];
  const rnd = (a, b) => a + Math.random() * (b - a);

  // ---- ミラーボール
  const ballGeo = new THREE.IcosahedronGeometry(0.85, 1);
  const cols = [];
  for (let i = 0; i < ballGeo.attributes.position.count / 3; i++) {
    const c = new THREE.Color().setHSL(Math.random(), 0.25, 0.55 + Math.random() * 0.4).multiplyScalar(1.6);
    cols.push(c.r, c.g, c.b, c.r, c.g, c.b, c.r, c.g, c.b);
  }
  ballGeo.setAttribute('color', new THREE.BufferAttribute(new Float32Array(cols), 3));
  const ball = new THREE.Mesh(ballGeo, new THREE.MeshBasicMaterial({ vertexColors: true, toneMapped: false }));
  const ballRoot = new THREE.Group();
  ballRoot.position.set(-3.2, 6.6, -3.2);
  ballRoot.add(ball);
  const cord = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, 4, 6), new THREE.MeshBasicMaterial({ color: '#cfd4dc' }));
  cord.position.y = 2.4;
  ballRoot.add(cord);
  g.add(ballRoot);

  // ---- 虹(昼にくっきり、夜はほんのり)
  const rainbow = new THREE.Group();
  const rbMats = [];
  ['#ff3b3b', '#ff9a2e', '#ffe14a', '#4ecb59', '#3aa0ff', '#4a5bd8', '#a64ad8'].forEach((c, i) => {
    const m = new THREE.MeshBasicMaterial({ color: c, transparent: true, opacity: 0.5, toneMapped: false, depthWrite: false });
    const ring = new THREE.Mesh(new THREE.TorusGeometry(22 - i * 0.6, 0.3, 6, 80, Math.PI), m);
    rainbow.add(ring);
    rbMats.push(m);
  });
  rainbow.position.set(-3, -0.5, -17);
  g.add(rainbow);

  // ---- 風船
  const balloons = [];
  [[-14, 4.5], [-13, 6.5], [9.5, -6], [11, -4.5], [12, -7], [-15, -2]].forEach(([x, z], i) => {
    const grp = new THREE.Group();
    grp.position.set(x, 0, z);
    const color = hdr(i / 6 + 0.02, 0.85, 0.55, 1);
    const b = new THREE.Mesh(new THREE.SphereGeometry(0.62, 20, 14), new THREE.MeshBasicMaterial({ color, toneMapped: false }));
    b.scale.y = 1.25;
    b.position.y = 4.2;
    const s = new THREE.Mesh(new THREE.CylinderGeometry(0.015, 0.015, 3.6, 4), new THREE.MeshBasicMaterial({ color: '#dddddd' }));
    s.position.y = 1.8;
    grp.add(b, s);
    g.add(grp);
    balloons.push({ grp, phase: i * 1.3 });
  });

  // ---- 噴水(池の上)
  const WN = 110;
  const wPos = new Float32Array(WN * 3);
  const wSeed = [];
  for (let i = 0; i < WN; i++) {
    wSeed.push([rnd(0, 6.28), rnd(0.4, 1.6), rnd(2.2, 4.2), Math.random()]);
  }
  const wGeo = new THREE.BufferGeometry();
  wGeo.setAttribute('position', new THREE.BufferAttribute(wPos, 3));
  const fountain = new THREE.Points(wGeo, new THREE.PointsMaterial({ ...ADD, map: dot, size: 9, sizeAttenuation: false, color: hdr(0.55, 0.8, 0.8, 2) }));
  fountain.frustumCulled = false;
  g.add(fountain);

  // ---- 席の足もとの光の輪(在席者の様子の色で光る)
  const rings = CLASSROOM_SEATS.map((s) => {
    const m = new THREE.MeshBasicMaterial({ ...ADD, color: '#fff', opacity: 0.8, side: THREE.DoubleSide });
    const r = new THREE.Mesh(new THREE.RingGeometry(0.75, 1.0, 40), m);
    r.rotation.x = -Math.PI / 2;
    r.position.set(s.x, 0.22, s.z);
    r.visible = false;
    g.add(r);
    return r;
  });

  return {
    group: g,
    setSeat(i, hex) {
      const r = rings[i];
      if (r) {
        r.visible = true;
        r.material.color.set(hex).multiplyScalar(2.2);
      }
    },
    clearSeats() {
      rings.forEach((r) => {
        r.visible = false;
      });
    },
    update(t, look) {
      if (!g.visible) {
        return;
      }
      const night = Math.max(look.lamp, look.stars);
      ball.rotation.y = t * 0.7;
      ballRoot.rotation.z = Math.sin(t * 0.8) * 0.03;
      rbMats.forEach((m) => {
        m.opacity = 0.18 + 0.42 * (1 - night);
      });
      balloons.forEach((b, i) => {
        b.grp.position.y = Math.sin(t * 0.9 + b.phase) * 0.35;
        b.grp.rotation.z = Math.sin(t * 0.6 + b.phase) * 0.06;
        b.grp.rotation.y = t * 0.2 + i;
      });
      for (let i = 0; i < WN; i++) {
        const s = wSeed[i];
        const u = (t * 0.9 + s[3]) % 1;
        wPos[i * 3] = 7 + Math.cos(s[0]) * s[1] * u * 1.6;
        wPos[i * 3 + 1] = 0.15 + 4 * u * (1 - u) * s[2];
        wPos[i * 3 + 2] = 7 + Math.sin(s[0]) * s[1] * u * 1.6;
      }
      wGeo.attributes.position.needsUpdate = true;
      rings.forEach((r, i) => {
        if (r.visible) {
          const k = 1 + 0.1 * Math.sin(t * 3 + i);
          r.scale.set(k, k, 1);
          r.material.opacity = 0.6 + 0.3 * Math.sin(t * 4 + i * 0.7);
        }
      });
    },
  };
}
