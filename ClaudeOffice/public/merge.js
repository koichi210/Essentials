// 動かない部品を同じ材質どうし1つのメッシュにまとめて、描く回数を減らす(見た目は同じ)。
// three.js は1回描くたびに行列を GPU へ送り、そのたびに JS のゴミが出る。家具やキャラの部品は数百個あるので、
// まとめると描く回数・ゴミ・JS ヒープがまとめて減る。
import * as THREE from 'three';

// 位置で決まる絵(uv)が要る材質はまとめない(まとめたジオメトリには uv を持ち越さないため)
const UV_MAPS = ['map', 'emissiveMap', 'alphaMap', 'normalMap', 'bumpMap', 'aoMap', 'lightMap', 'roughnessMap', 'metalnessMap', 'displacementMap', 'specularMap'];
const defaultBeforeRender = new THREE.Object3D().onBeforeRender;
const rel = new THREE.Matrix4();
const inv = new THREE.Matrix4();
const nrm = new THREE.Matrix3();
const v = new THREE.Vector3();

function dropArray() {
  this.array = null;
}

function noRaycast() {}

function mergeable(m) {
  if (!m.isMesh || m.isInstancedMesh || m.isSkinnedMesh || !m.visible || m.renderOrder !== 0 || m.onBeforeRender !== defaultBeforeRender) {
    return false;
  }
  if (Object.keys(m.userData).length) {
    // クリック判定などに使う印のある物はそのまま
    return false;
  }
  const mat = m.material;
  if (!mat || Array.isArray(mat) || mat.transparent || mat.vertexColors || UV_MAPS.some((k) => mat[k])) {
    // 半透明は物ごとに奥から順に描く必要があるので、まとめると重なりの見え方が変わる
    return false;
  }
  const g = m.geometry;
  const d = g.drawRange;
  return !!(g.attributes.position && g.attributes.normal) && !g.attributes.color && !Object.keys(g.morphAttributes).length && d.start === 0 && d.count === Infinity;
}

// root 以下の動かないメッシュをまとめる。
//   dynamic : 動く物(位置・向き・大きさ・表示を変える物)の集合。動く物そのものはまとめない
//   region  : 'parent'  = 同じ親の下でまとめる(親より上がどう動いても安全。動く物の見落としに強い)
//             'dynamic' = いちばん近い動く祖先(または root)の下でまとめる(動く物を全部 dynamic に入れたとき用)
//             'auto'    = 動く物・動く物を中に持つグループ(犬の体など、役者の入れ物)を境界にして、その下でまとめる。
//                         中に動く物が1つも無いグループ(ドクロ・椅子・窓枠など)は上の階層とまとめて1つにできる
//   recolor : (material) => 頂点色で描く共有の材質。返したら色違いの材質も1つにまとめる(色は頂点色へ)
//   keepArrays: false なら GPU へ送ったあと JS 側の頂点配列を捨て、クリック判定の対象から外す
// 戻り値: 新しく作ったジオメトリの配列(片付けで dispose する)
export function mergeStatic(root, { dynamic = new Set(), region = 'parent', recolor = null, keepArrays = false } = {}) {
  root.updateMatrixWorld(true);
  // 1. まとめられるメッシュ。子孫もぜんぶまとめられる物だけ(外すときに子を置き去りにしないため)
  const ok = new Set();
  const check = (o) => {
    let all = true;
    for (const c of o.children) {
      all = check(c) && all;
    }
    const good = all && o !== root && !dynamic.has(o) && mergeable(o);
    if (good) {
      ok.add(o);
    }
    return good;
  };
  check(root);
  // 動く物を中に持つ祖先(auto のときの境界)
  const holds = new Set();
  if (region === 'auto') {
    for (const d of dynamic) {
      for (let p = d.parent; p && !holds.has(p); p = p.parent) {
        holds.add(p);
      }
    }
  }
  // 2. まとめる先を決める。まとめられない物(途中に隠れた祖先・鏡写し)は、その祖先ごと外す
  const regionOf = (m) => {
    let r = m.parent;
    if (region === 'dynamic' || region === 'auto') {
      while (r !== root && !dynamic.has(r) && !holds.has(r)) {
        r = r.parent;
      }
    } else {
      // 輪郭線の殻を子に持つ部品などは、子も親と同じ先へ
      while (ok.has(r)) {
        r = r.parent;
      }
    }
    return r;
  };
  for (let changed = true; changed;) {
    changed = false;
    for (const m of ok) {
      const r = regionOf(m);
      let shown = true;
      for (let p = m.parent; p !== r; p = p.parent) {
        shown = shown && p.visible;
      }
      rel.copy(inv.copy(r.matrixWorld).invert()).multiply(m.matrixWorld);
      if (!shown || rel.determinant() <= 0) {
        for (let p = m; p && ok.has(p); p = p.parent) {
          ok.delete(p);
        }
        changed = true;
      }
    }
  }
  // 3. まとめる先・材質・影の設定ごとに仕分け
  const groups = new Map();
  const info = new Map();
  for (const m of ok) {
    const r = regionOf(m);
    const re = recolor ? recolor(m.material) : null;
    const mat = re || m.material;
    const key = `${r.id}|${mat.id}|${m.castShadow}|${m.receiveShadow}|${m.frustumCulled}`;
    if (!groups.has(key)) {
      groups.set(key, { r, mat, re: !!re, cast: m.castShadow, recv: m.receiveShadow, culled: m.frustumCulled, list: [] });
    }
    const g = groups.get(key);
    g.list.push(m);
    info.set(m, g);
  }
  // 4. 2つ以上ある組だけまとめる。ただし親がまとめられて外れる物は、1つでも作り直す(親と一緒に消えないように)
  const absorbed = new Set();
  for (const g of groups.values()) {
    if (g.list.length >= 2) {
      g.list.forEach((m) => absorbed.add(m));
    }
  }
  for (let changed = true; changed;) {
    changed = false;
    for (const m of ok) {
      if (!absorbed.has(m) && ok.has(m.parent) && absorbed.has(m.parent)) {
        info.get(m).list.forEach((x) => absorbed.add(x));
        changed = true;
      }
    }
  }
  const made = [];
  for (const g of groups.values()) {
    if (!g.list.some((m) => absorbed.has(m))) {
      continue;
    }
    const geo = build(g.list, g.r, g.re);
    const mesh = new THREE.Mesh(geo, g.mat);
    mesh.name = 'merged';
    mesh.castShadow = g.cast;
    mesh.receiveShadow = g.recv;
    mesh.frustumCulled = g.culled;
    if (!keepArrays) {
      for (const a of [geo.attributes.position, geo.attributes.normal, geo.attributes.color, geo.index]) {
        a?.onUpload(dropArray);
      }
      mesh.raycast = noRaycast;
    }
    g.r.add(mesh);
    made.push(geo);
  }
  // 5. まとめた元のメッシュを外す(親ごと外れる物は親だけ外す)
  for (const m of absorbed) {
    if (!absorbed.has(m.parent)) {
      m.parent.remove(m);
    }
  }
  return made;
}

// list のメッシュを r から見た座標で1つのジオメトリにする
function build(list, r, withColor) {
  inv.copy(r.matrixWorld).invert();
  let nv = 0;
  let ni = 0;
  for (const m of list) {
    const g = m.geometry;
    nv += g.attributes.position.count;
    ni += g.index ? g.index.count : g.attributes.position.count;
  }
  const pos = new Float32Array(nv * 3);
  const nor = new Float32Array(nv * 3);
  const col = withColor ? new Float32Array(nv * 3) : null;
  const idx = nv > 65535 ? new Uint32Array(ni) : new Uint16Array(ni);
  let ov = 0;
  let oi = 0;
  for (const m of list) {
    const g = m.geometry;
    const P = g.attributes.position;
    const N = g.attributes.normal;
    rel.copy(inv).multiply(m.matrixWorld);
    nrm.getNormalMatrix(rel);
    for (let i = 0; i < P.count; i++) {
      const o = (ov + i) * 3;
      v.fromBufferAttribute(P, i).applyMatrix4(rel);
      pos[o] = v.x;
      pos[o + 1] = v.y;
      pos[o + 2] = v.z;
      v.fromBufferAttribute(N, i).applyMatrix3(nrm).normalize();
      nor[o] = v.x;
      nor[o + 1] = v.y;
      nor[o + 2] = v.z;
    }
    if (col) {
      // 材質の色(線形)をそのまま頂点色へ。シェーダは「材質の色(白) × 頂点色」なので同じ色になる
      const c = m.material.color;
      for (let i = 0; i < P.count; i++) {
        col[(ov + i) * 3] = c.r;
        col[(ov + i) * 3 + 1] = c.g;
        col[(ov + i) * 3 + 2] = c.b;
      }
    }
    if (g.index) {
      for (let i = 0; i < g.index.count; i++) {
        idx[oi + i] = g.index.getX(i) + ov;
      }
      oi += g.index.count;
    } else {
      for (let i = 0; i < P.count; i++) {
        idx[oi + i] = ov + i;
      }
      oi += P.count;
    }
    ov += P.count;
  }
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  geo.setAttribute('normal', new THREE.BufferAttribute(nor, 3));
  if (col) {
    geo.setAttribute('color', new THREE.BufferAttribute(col, 3));
  }
  geo.setIndex(new THREE.BufferAttribute(idx, 1));
  // 画面外の判定に使う(配列を捨てる前に計算しておく)
  geo.computeBoundingSphere();
  geo.computeBoundingBox();
  return geo;
}
