// 魔界スキンの「標準」モード用の、禍々しくて豪華な演出。動作モード「軽量」(エフェクト OFF)では丸ごと非表示。
// 色は HDR(1 を超える値)にして、ブルームで光らせる。
import * as THREE from 'three';
import { CLASSROOM_SEATS } from './world.js';
import { ADD, dotTexture } from './fancy.js';

const CENTER = { x: -3.2, z: -2.6 };

export function buildMakaiFancy(ctx) {
  const g = new THREE.Group();
  ctx.root.add(g);
  const R = { x0: -12, x1: 6, z0: -10, z1: 3 };
  const rnd = (a, b) => a + Math.random() * (b - a);
  const red = (k) => new THREE.Color('#ff1a2a').multiplyScalar(k);
  const basic = (color, extra = {}) => new THREE.MeshBasicMaterial({ color, toneMapped: false, ...extra });

  // ---- 床の魔法陣(赤く脈打ちながら逆回転する二重の輪と星)
  const circle = new THREE.Group();
  circle.position.set(CENTER.x, 0.2, CENTER.z);
  const circleMats = [];
  const addRing = (r0, r1, seg) => {
    const m = new THREE.MeshBasicMaterial({ ...ADD, color: '#fff', opacity: 0.7, side: THREE.DoubleSide });
    const mesh = new THREE.Mesh(new THREE.RingGeometry(r0, r1, seg), m);
    mesh.rotation.x = -Math.PI / 2;
    circle.add(mesh);
    circleMats.push(m);
    return mesh;
  };
  addRing(4.2, 4.4, 64);
  addRing(3.6, 3.68, 64);
  const star1 = addRing(2.7, 2.82, 5);
  const star2 = addRing(2.7, 2.82, 5);
  star2.rotation.z = Math.PI / 5;
  const inner = addRing(1.4, 1.5, 6);
  g.add(circle);

  // ---- 魔法陣の縁から噴き上がる炎の柱
  const pillars = [];
  for (let i = 0; i < 10; i++) {
    const a = (i / 10) * Math.PI * 2;
    const f = new THREE.Mesh(new THREE.ConeGeometry(0.28, 2.4, 7, 1, true), new THREE.MeshBasicMaterial({ ...ADD, color: new THREE.Color('#ff3a1a').multiplyScalar(2.2), opacity: 0.75, side: THREE.DoubleSide }));
    f.position.set(CENTER.x + Math.cos(a) * 4.1, 1.35, CENTER.z + Math.sin(a) * 4.1);
    g.add(f);
    pillars.push({ f, ph: i * 1.7 });
  }

  // ---- 天井から吊られた、赤い魔晶と巡るドクロ
  const chand = new THREE.Group();
  chand.position.set(CENTER.x, 6.0, CENTER.z);
  const core = new THREE.Mesh(new THREE.OctahedronGeometry(0.8, 0), basic(red(2.6)));
  core.scale.y = 1.7;
  chand.add(core);
  const orbit = [];
  for (let i = 0; i < 6; i++) {
    const s = new THREE.Group();
    const head = new THREE.Mesh(new THREE.SphereGeometry(0.28, 10, 8), basic('#f4ecd8'));
    s.add(head);
    for (const sx of [-1, 1]) {
      const e = new THREE.Mesh(new THREE.SphereGeometry(0.07, 6, 6), basic(red(3)));
      e.position.set(sx * 0.1, 0.02, 0.24);
      s.add(e);
    }
    chand.add(s);
    orbit.push(s);
  }
  const cord = new THREE.Mesh(new THREE.CylinderGeometry(0.03, 0.03, 4, 6), basic('#3a2a3a'));
  cord.position.y = 2.9;
  chand.add(cord);
  g.add(chand);

  // ---- 魔法陣のまわりを巡る燃えるロウソク
  const candles = [];
  for (let i = 0; i < 12; i++) {
    const grp = new THREE.Group();
    const wax = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.07, 0.35, 8), basic('#f1e6c8'));
    const fl = new THREE.Mesh(new THREE.ConeGeometry(0.07, 0.22, 6), basic(new THREE.Color('#ffb347').multiplyScalar(2.2)));
    fl.position.y = 0.28;
    grp.add(wax, fl);
    g.add(grp);
    candles.push({ grp, fl, i });
  }

  // ---- 立ちのぼる魂(赤と青緑の光の粒が上へ昇る)
  const SN = 190;
  const pos = new Float32Array(SN * 3);
  const col = new Float32Array(SN * 3);
  const sgeo = new THREE.BufferGeometry();
  sgeo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  sgeo.setAttribute('color', new THREE.BufferAttribute(col, 3));
  const souls = new THREE.Points(sgeo, new THREE.PointsMaterial({ ...ADD, sizeAttenuation: false, vertexColors: true, map: dotTexture(true), size: 10, opacity: 0.9 }));
  souls.frustumCulled = false;
  g.add(souls);
  const seed = [];
  for (let i = 0; i < SN; i++) {
    seed.push([rnd(R.x0 - 4, R.x1 + 4), rnd(0, 9), rnd(R.z0, R.z1 + 12), rnd(0.35, 1.0), rnd(0, 6.28)]);
    const c = i % 3 === 0 ? new THREE.Color('#7dffd8').multiplyScalar(1.8) : red(1.8);
    col.set([c.r, c.g, c.b], i * 3);
  }

  // ---- ゆらゆら漂う亡霊(半透明、黒い目と口)
  const ghosts = [];
  for (let i = 0; i < 5; i++) {
    const grp = new THREE.Group();
    const mat = basic(new THREE.Color('#cfe8ff').multiplyScalar(1.2), { transparent: true, opacity: 0.55 });
    const head = new THREE.Mesh(new THREE.SphereGeometry(0.5, 12, 10), mat);
    head.position.y = 0.6;
    const body = new THREE.Mesh(new THREE.ConeGeometry(0.62, 1.4, 12, 1, true), mat.clone());
    body.material.side = THREE.DoubleSide;
    body.rotation.x = Math.PI;
    body.position.y = -0.1;
    grp.add(head, body);
    for (const sx of [-1, 1]) {
      const e = new THREE.Mesh(new THREE.SphereGeometry(0.1, 8, 6), basic('#100818'));
      e.scale.y = 1.6;
      e.position.set(sx * 0.2, 0.68, 0.43);
      grp.add(e);
    }
    const mouth = new THREE.Mesh(new THREE.SphereGeometry(0.12, 8, 6), basic('#100818'));
    mouth.scale.set(1, 1.5, 0.5);
    mouth.position.set(0, 0.35, 0.46);
    grp.add(mouth);
    g.add(grp);
    ghosts.push({ grp, i, r: 5 + i * 1.8, sp: 0.18 + i * 0.04, h: 2.2 + (i % 3) * 0.9 });
  }

  // ---- 奥の丸窓から差し込む血の光(淡い光の筋)
  const beam = new THREE.Mesh(new THREE.PlaneGeometry(2.6, 9), new THREE.MeshBasicMaterial({ ...ADD, color: new THREE.Color('#ff2a1a').multiplyScalar(1.4), opacity: 0.16, side: THREE.DoubleSide }));
  beam.position.set(-1.2, 2.6, R.z0 + 3.2);
  beam.rotation.set(-0.5, 0, 0);
  g.add(beam);

  // ---- 赤い稲妻(ときどき空を裂く)
  const bolt = new THREE.Line(new THREE.BufferGeometry(), new THREE.LineBasicMaterial({ color: new THREE.Color('#ff5a5a').multiplyScalar(3), toneMapped: false }));
  bolt.frustumCulled = false;
  bolt.visible = false;
  g.add(bolt);
  let nextBolt = 3;
  let boltEnd = 0;
  const strike = (t) => {
    const side = Math.random() < 0.5;
    let x = side ? rnd(-26, -16) : rnd(14, 22);
    let z = side ? rnd(-20, -8) : rnd(-24, -10);
    const pts = [];
    for (let y = 26; y > 2; y -= 2.4) {
      pts.push(x, y, z);
      x += rnd(-1.4, 1.4);
      z += rnd(-1.4, 1.4);
    }
    bolt.geometry.setAttribute('position', new THREE.Float32BufferAttribute(pts, 3));
    bolt.visible = true;
    boltEnd = t + 0.18;
    nextBolt = t + rnd(3, 8);
  };

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
    update(t) {
      if (!g.visible) {
        return;
      }
      circle.rotation.y = t * 0.2;
      star1.rotation.z = t * 0.35;
      star2.rotation.z = Math.PI / 5 - t * 0.35;
      inner.rotation.z = -t * 0.8;
      circleMats.forEach((m, i) => {
        m.color.copy(red(2.2 + Math.sin(t * 2.4 + i) * 0.8));
        m.opacity = 0.5 + 0.3 * Math.sin(t * 2.4 + i * 0.8);
      });
      pillars.forEach(({ f, ph }) => {
        f.scale.set(1 + Math.sin(t * 9 + ph) * 0.2, 0.8 + Math.sin(t * 5 + ph * 2) * 0.35 + Math.sin(t * 0.7 + ph) * 0.2, 1);
        f.rotation.y = t * 2 + ph;
      });
      chand.rotation.y = t * 0.5;
      core.rotation.y = -t * 1.3;
      core.material.color.copy(red(2.2 + Math.sin(t * 3) * 0.8));
      orbit.forEach((s, i) => {
        const a = t * 0.9 + (i / orbit.length) * Math.PI * 2;
        s.position.set(Math.cos(a) * 1.8, Math.sin(t * 1.6 + i) * 0.3, Math.sin(a) * 1.8);
        s.rotation.y = -a + Math.PI / 2;
      });
      candles.forEach(({ grp, fl, i }) => {
        const a = -t * 0.45 + (i / candles.length) * Math.PI * 2;
        grp.position.set(CENTER.x + Math.cos(a) * 3.1, 0.8 + Math.sin(t * 1.4 + i) * 0.25, CENTER.z + Math.sin(a) * 3.1);
        fl.scale.set(1, 1 + Math.sin(t * 13 + i * 2) * 0.3, 1);
      });
      for (let i = 0; i < SN; i++) {
        const s = seed[i];
        pos[i * 3] = s[0] + Math.sin(t * 0.8 + s[4]) * 0.5;
        pos[i * 3 + 1] = (s[1] + t * s[3]) % 9;
        pos[i * 3 + 2] = s[2] + Math.cos(t * 0.6 + s[4]) * 0.5;
      }
      sgeo.attributes.position.needsUpdate = true;
      souls.material.opacity = 0.65 + 0.3 * Math.sin(t * 3.3);
      ghosts.forEach(({ grp, i, r, sp, h }) => {
        const a = t * sp + i * 1.3;
        const x = CENTER.x + Math.cos(a) * r;
        const z = CENTER.z + Math.sin(a * 1.1) * r * 0.8;
        grp.position.set(x, h + Math.sin(t * 1.3 + i) * 0.4, z);
        grp.rotation.y = Math.atan2(Math.cos(a) * -1, Math.sin(a * 1.1)) + Math.PI;
        grp.rotation.z = Math.sin(t * 1.1 + i) * 0.12;
        grp.children[0].material.opacity = 0.4 + 0.2 * Math.sin(t * 1.7 + i * 2);
      });
      beam.material.opacity = 0.12 + 0.06 * Math.sin(t * 1.8);
      if (t > nextBolt && !bolt.visible) {
        strike(t);
      }
      if (bolt.visible && t > boltEnd) {
        bolt.visible = false;
      }
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
