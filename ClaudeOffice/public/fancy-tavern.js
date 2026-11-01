// 酒場スキンの「標準」モード用の、ハデな宴会演出。動作モード「軽量」(エフェクト OFF)では丸ごと非表示にして、いまの見た目のまま。
// 色は HDR(1 を超える値)にして、ブルームで光らせる。
import * as THREE from 'three';
import { CLASSROOM_SEATS, CRYSTAL_POS } from './world.js';
import { ADD, dotTexture, hdr } from './fancy.js';

export function buildTavernFancy(ctx) {
  const g = new THREE.Group();
  ctx.root.add(g);
  const starTex = dotTexture(true);
  const R = { x0: -12, x1: 6, z0: -10, z1: 3 };
  const rnd = (a, b) => a + Math.random() * (b - a);

  // ---- 魔法のシャンデリア(回る水晶と、まわりを巡る光球)
  const chand = new THREE.Group();
  chand.position.set(CRYSTAL_POS.x, 6.2, CRYSTAL_POS.z);
  const core = new THREE.Mesh(new THREE.OctahedronGeometry(0.9, 0), new THREE.MeshBasicMaterial({ color: hdr(0.52, 0.9, 0.65, 2.4), toneMapped: false }));
  core.scale.y = 1.6;
  chand.add(core);
  const orbs = [];
  for (let i = 0; i < 8; i++) {
    const m = new THREE.Mesh(new THREE.SphereGeometry(0.2, 10, 8), new THREE.MeshBasicMaterial({ color: '#fff', toneMapped: false }));
    chand.add(m);
    orbs.push(m);
  }
  const ringA = new THREE.Mesh(new THREE.TorusGeometry(1.7, 0.04, 6, 48), new THREE.MeshBasicMaterial({ color: hdr(0.12, 1, 0.6, 2.4), toneMapped: false }));
  ringA.rotation.x = Math.PI / 2;
  chand.add(ringA);
  const cord = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, 4, 6), new THREE.MeshBasicMaterial({ color: '#c9a24a' }));
  cord.position.y = 2.9;
  chand.add(cord);
  g.add(chand);

  // ---- 床の魔法陣(絨毯の上で回って脈打つ)
  const circle = new THREE.Group();
  circle.position.set(CRYSTAL_POS.x, 0.2, CRYSTAL_POS.z);
  const circleMats = [];
  const addRing = (r0, r1, seg) => {
    const m = new THREE.MeshBasicMaterial({ ...ADD, color: '#fff', opacity: 0.55, side: THREE.DoubleSide });
    const mesh = new THREE.Mesh(new THREE.RingGeometry(r0, r1, seg), m);
    mesh.rotation.x = -Math.PI / 2;
    circle.add(mesh);
    circleMats.push(m);
    return mesh;
  };
  addRing(4.0, 4.15, 64);
  addRing(3.5, 3.58, 64);
  const star1 = addRing(2.5, 2.6, 3);
  const star2 = addRing(2.5, 2.6, 3);
  star2.rotation.z = Math.PI / 3;
  const inner = addRing(1.4, 1.46, 5);
  g.add(circle);

  // ---- キラキラ(部屋に舞う光の粒)
  const makePoints = (n, opts) => {
    const pos = new Float32Array(n * 3);
    const col = new Float32Array(n * 3);
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    geo.setAttribute('color', new THREE.BufferAttribute(col, 3));
    const pts = new THREE.Points(geo, new THREE.PointsMaterial({ ...ADD, sizeAttenuation: false, vertexColors: true, ...opts }));
    pts.frustumCulled = false;
    g.add(pts);
    return { pos, col, geo, pts };
  };
  const GN = 170;
  const glitter = makePoints(GN, { map: starTex, size: 11, opacity: 0.9 });
  const gSeed = [];
  for (let i = 0; i < GN; i++) {
    gSeed.push([rnd(R.x0 + 0.5, R.x1 - 0.5), rnd(0, 6), rnd(R.z0 + 0.5, R.z1 - 0.5), rnd(0.2, 0.7), rnd(0, 6.28)]);
    const c = hdr([0.12, 0.85, 0.5, 0.08][i % 4], 0.9, 0.7, 1.6);
    glitter.col.set([c.r, c.g, c.b], i * 3);
  }

  // ---- 浮かぶ提灯(ふわふわ上下、色とりどり)
  const lanterns = [];
  [[-14, 4.5], [-13, 7], [9.5, -6], [11, -4.5], [12, -7.5], [-15, -2], [8, 4], [-6, 9], [0, 11]].forEach(([x, z], i) => {
    const grp = new THREE.Group();
    grp.position.set(x, 0, z);
    const body = new THREE.Mesh(new THREE.SphereGeometry(0.5, 14, 10), new THREE.MeshBasicMaterial({ color: hdr(i / 9, 0.9, 0.55, 1.8), toneMapped: false }));
    body.scale.y = 1.2;
    body.position.y = 4.2 + (i % 3) * 0.6;
    const tail = new THREE.Mesh(new THREE.CylinderGeometry(0.015, 0.015, 3.8, 4), new THREE.MeshBasicMaterial({ color: '#d8c8a8' }));
    tail.position.y = 1.9;
    grp.add(body, tail);
    g.add(grp);
    lanterns.push({ grp, phase: i * 1.3 });
  });

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
      chand.rotation.y = t * 0.6;
      core.rotation.y = -t * 1.4;
      core.material.color.setHSL((t * 0.08) % 1, 0.9, 0.65).multiplyScalar(2.4);
      orbs.forEach((o, i) => {
        const a = t * 1.2 + (i / 8) * Math.PI * 2;
        o.position.set(Math.cos(a) * 1.7, Math.sin(t * 2 + i) * 0.3, Math.sin(a) * 1.7);
        o.material.color.setHSL((i / 8 + t * 0.1) % 1, 1, 0.6).multiplyScalar(2.6);
      });
      circle.rotation.y = t * 0.25;
      star1.rotation.z = t * 0.4;
      star2.rotation.z = Math.PI / 3 - t * 0.4;
      inner.rotation.z = -t * 0.7;
      circleMats.forEach((m, i) => {
        m.color.setHSL((0.55 + i * 0.1 + t * 0.05) % 1, 0.9, 0.6).multiplyScalar(2.2);
        m.opacity = 0.4 + 0.25 * Math.sin(t * 2 + i);
      });
      for (let i = 0; i < GN; i++) {
        const s = gSeed[i];
        glitter.pos[i * 3] = s[0] + Math.sin(t * 0.8 + s[4]) * 0.4;
        glitter.pos[i * 3 + 1] = (s[1] + t * s[3]) % 6;
        glitter.pos[i * 3 + 2] = s[2] + Math.cos(t * 0.7 + s[4]) * 0.4;
      }
      glitter.geo.attributes.position.needsUpdate = true;
      glitter.pts.material.opacity = 0.5 + 0.35 * Math.sin(t * 5) * Math.sin(t * 3.1);
      lanterns.forEach((l, i) => {
        l.grp.position.y = Math.sin(t * 0.9 + l.phase) * 0.4;
        l.grp.rotation.y = t * 0.2 + i;
      });
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
