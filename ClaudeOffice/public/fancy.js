// 教室スキンの「標準」モード用の豪華でハデな演出。動作モード「軽量」(エフェクト OFF)では丸ごと非表示にして、いまの見た目のまま。
// 色は HDR(1 を超える値)にして、ブルームで光らせる。
import * as THREE from 'three';
import { CLASSROOM_SEATS } from './world.js';

const ADD = { blending: THREE.AdditiveBlending, depthWrite: false, transparent: true, toneMapped: false };

function dotTexture(star) {
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

const hdr = (h, s, l, k) => new THREE.Color().setHSL(h, s, l).multiplyScalar(k);

export function buildFancy(ctx) {
  const g = new THREE.Group();
  ctx.root.add(g);
  const dot = dotTexture(false);
  const starTex = dotTexture(true);
  const R = { x0: -12, x1: 6, z0: -10, z1: 3 };
  const parts = [];
  const rnd = (a, b) => a + Math.random() * (b - a);

  // ---- ネオン: 奥壁と左壁の上端を、色が流れる光の帯で縁取る
  const neon = [];
  const neonMats = [];
  const addNeon = (x, y, z, w, d) => {
    const m = new THREE.MeshBasicMaterial({ color: '#fff', toneMapped: false });
    const mesh = new THREE.Mesh(new THREE.BoxGeometry(w, 0.16, d), m);
    mesh.position.set(x, y, z);
    g.add(mesh);
    neon.push(mesh);
    neonMats.push(m);
  };
  for (let x = R.x0; x < R.x1; x += 1.5) {
    addNeon(x + 0.75, 5.3, R.z0 - 0.15, 1.46, 0.2);
  }
  for (let z = R.z0; z < R.z1; z += 1.5) {
    addNeon(R.x0 - 0.15, 5.3, z + 0.75, 0.2, 1.46);
  }
  // 床の手前側と右側のふち
  for (let x = R.x0; x < R.x1; x += 1.5) {
    addNeon(x + 0.75, 0.2, R.z1 + 0.1, 1.46, 0.14);
  }
  for (let z = R.z0; z < R.z1; z += 1.5) {
    addNeon(R.x1 + 0.1, 0.2, z + 0.75, 0.14, 1.46);
  }

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

  // ---- 回転するスポットライト(光の円錐)
  const beams = [];
  const beamColors = [0.92, 0.55, 0.12, 0.78];
  [[-9, -8], [3, -8], [-9, 1], [3, 1]].forEach(([x, z], i) => {
    const pivot = new THREE.Group();
    pivot.position.set(x, 10.5, z);
    const cone = new THREE.Mesh(
      new THREE.ConeGeometry(1.5, 10.5, 24, 1, true),
      new THREE.MeshBasicMaterial({ ...ADD, color: hdr(beamColors[i], 1, 0.6, 1), opacity: 0.12, side: THREE.DoubleSide }),
    );
    cone.position.y = -5.25;
    pivot.add(cone);
    g.add(pivot);
    beams.push({ pivot, cone, phase: i * 1.7, hue: beamColors[i] });
  });

  // ---- キラキラ(部屋の中で舞う光の粒)
  const GN = 150;
  const gPos = new Float32Array(GN * 3);
  const gCol = new Float32Array(GN * 3);
  const gSeed = [];
  for (let i = 0; i < GN; i++) {
    gSeed.push([rnd(R.x0 + 0.5, R.x1 - 0.5), rnd(0, 6), rnd(R.z0 + 0.5, R.z1 - 0.5), rnd(0.2, 0.7), rnd(0, 6.28)]);
    const c = hdr([0.12, 0.92, 0.5, 0.08][i % 4], 0.9, 0.7, 1.6);
    gCol.set([c.r, c.g, c.b], i * 3);
  }
  const gGeo = new THREE.BufferGeometry();
  gGeo.setAttribute('position', new THREE.BufferAttribute(gPos, 3));
  gGeo.setAttribute('color', new THREE.BufferAttribute(gCol, 3));
  const glitter = new THREE.Points(gGeo, new THREE.PointsMaterial({ ...ADD, map: starTex, size: 11, sizeAttenuation: false, vertexColors: true, opacity: 0.9 }));
  g.add(glitter);

  // ---- 紙吹雪(島の上から降る)
  const CN = 170;
  const confetti = new THREE.InstancedMesh(new THREE.PlaneGeometry(0.3, 0.18), new THREE.MeshBasicMaterial({ side: THREE.DoubleSide, toneMapped: false }), CN);
  const cSeed = [];
  for (let i = 0; i < CN; i++) {
    cSeed.push([rnd(-17, 15), rnd(0, 1), rnd(-15, 13), rnd(0.9, 1.8), rnd(2, 6), rnd(2, 6)]);
    confetti.setColorAt(i, hdr(Math.random(), 1, 0.6, 1.8));
  }
  confetti.frustumCulled = false;
  g.add(confetti);
  const dummy = new THREE.Object3D();

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

  // ---- 花火(夜と夕方に打ち上がる)
  const fireworks = [];
  const FN = 72;
  for (let b = 0; b < 4; b++) {
    const pos = new Float32Array(FN * 3);
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    const mat = new THREE.PointsMaterial({ ...ADD, map: dot, size: 14, sizeAttenuation: false, color: '#fff', opacity: 0 });
    const pts = new THREE.Points(geo, mat);
    pts.frustumCulled = false;
    pts.visible = false;
    g.add(pts);
    fireworks.push({ pts, pos, vel: new Float32Array(FN * 3), mat, age: -1, life: 2.4 });
  }
  let nextFirework = 1;
  const launch = (f) => {
    const ox = rnd(-12, -2);
    const oy = rnd(7, 11);
    const oz = rnd(-15, -9);
    const color = hdr(Math.random(), 1, 0.6, 2.6);
    f.mat.color.copy(color);
    for (let i = 0; i < FN; i++) {
      const a = Math.random() * Math.PI * 2;
      const e = Math.acos(rnd(-1, 1));
      const sp = rnd(3, 7);
      f.pos.set([ox, oy, oz], i * 3);
      f.vel.set([Math.sin(e) * Math.cos(a) * sp, Math.cos(e) * sp, Math.sin(e) * Math.sin(a) * sp], i * 3);
    }
    f.age = 0;
    f.pts.visible = true;
  };

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

  let last = 0;
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
      const dt = Math.min(0.1, Math.max(0, t - last));
      last = t;
      if (!g.visible) {
        return;
      }
      const night = Math.max(look.lamp, look.stars);
      // ネオンの色が流れる
      neon.forEach((m, i) => {
        neonMats[i].color.setHSL(((t * 0.12 + i * 0.045) % 1), 1, 0.6).multiplyScalar(2.4);
      });
      ball.rotation.y = t * 0.7;
      ballRoot.rotation.z = Math.sin(t * 0.8) * 0.03;
      for (const b of beams) {
        b.pivot.rotation.x = Math.sin(t * 0.7 + b.phase) * 0.55;
        b.pivot.rotation.z = Math.cos(t * 0.5 + b.phase) * 0.55;
        b.cone.material.color.setHSL((b.hue + t * 0.05) % 1, 1, 0.6);
        b.cone.material.opacity = 0.04 + 0.07 * night;
      }
      for (let i = 0; i < GN; i++) {
        const s = gSeed[i];
        const y = (s[1] + t * s[3]) % 6;
        gPos[i * 3] = s[0] + Math.sin(t * 0.8 + s[4]) * 0.4;
        gPos[i * 3 + 1] = y;
        gPos[i * 3 + 2] = s[2] + Math.cos(t * 0.7 + s[4]) * 0.4;
      }
      gGeo.attributes.position.needsUpdate = true;
      glitter.material.opacity = 0.55 + 0.4 * Math.sin(t * 5) * Math.sin(t * 3.1) + 0.2 * night;
      for (let i = 0; i < CN; i++) {
        const s = cSeed[i];
        const y = 17 - ((t * s[3] + s[1] * 17) % 17);
        dummy.position.set(s[0] + Math.sin(t + s[1] * 9) * 0.8, y, s[2]);
        dummy.rotation.set(t * s[4], t * s[5], 0);
        dummy.updateMatrix();
        confetti.setMatrixAt(i, dummy.matrix);
      }
      confetti.instanceMatrix.needsUpdate = true;
      rbMats.forEach((m) => {
        m.opacity = 0.18 + 0.42 * (1 - night);
      });
      // 花火: 夜と夕方に打ち上げる
      nextFirework -= dt;
      if (nextFirework <= 0 && look.stars > 0.2) {
        const free = fireworks.find((f) => f.age < 0);
        if (free) {
          launch(free);
        }
        nextFirework = rnd(0.7, 1.8);
      }
      for (const f of fireworks) {
        if (f.age < 0) {
          continue;
        }
        f.age += dt;
        for (let i = 0; i < FN; i++) {
          f.vel[i * 3 + 1] -= 4.5 * dt;
          f.pos[i * 3] += f.vel[i * 3] * dt;
          f.pos[i * 3 + 1] += f.vel[i * 3 + 1] * dt;
          f.pos[i * 3 + 2] += f.vel[i * 3 + 2] * dt;
          f.vel[i * 3] *= 0.985;
          f.vel[i * 3 + 2] *= 0.985;
        }
        f.pts.geometry.attributes.position.needsUpdate = true;
        f.mat.opacity = Math.max(0, 1 - f.age / f.life);
        if (f.age > f.life) {
          f.age = -1;
          f.pts.visible = false;
        }
      }
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
