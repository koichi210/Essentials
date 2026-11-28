// 入口: 描画ループ・ポーリング・クリック判定・詳細の窓・設定
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { CSS2DRenderer, CSS2DObject } from 'three/addons/renderers/CSS2DRenderer.js';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';
import { Kit, createSky, timeLook, arcPositions, buildClassroom, projectColor, wallClock } from './world.js';
import { buildTavern } from './tavern.js';
import { buildMakai } from './makai.js';
import { buildSpot } from './spots.js';
import { mergeStatic } from './merge.js';
import { createWander } from './wander.js';
import { createAvatar } from './avatar.js';
import { deriveState, assignSeats, elapsed } from './people.js';
import { renderHUD, renderSide, renderTimeline, openDetail, refreshWindows, closeTopWindow, focusDetail, toast, openInVscode, esc, dayKeyOf, dateLabel, hhmm } from './ui.js';
import { initTalks, talkFontChanged } from './talk.js';
import { openNotifyDialog } from './notify.js';

const FONT_STEPS = [0.9, 1, 1.1, 1.25, 1.4, 1.6];
const MODES = {
  standard: { effects: 'on', fps: '60', shadows: 'on', resolution: 'high', ultra: 'off' },
  light: { effects: 'off', fps: '30', shadows: 'off', resolution: 'low', ultra: 'off' },
  // 超軽量: 壁・時計・人(●の玉)だけを描く
  ultra: { effects: 'off', fps: '30', shadows: 'off', resolution: 'low', ultra: 'on' },
};
const SKINS = ['default', 'isekai', 'makai'];
const SUB_EMOJI = { Explore: '🔍', Plan: '🗺️', 'general-purpose': '🤖' };
const SUB_COLOR = { Explore: '#2bb3a6', Plan: '#e9b44c', 'general-purpose': '#9c6ade' };
const GLOW = { working: '#7fd6ff', thinking: '#b7a6ff', waiting: '#ffd27a', sleeping: '#3a3f55' };

// ---------------------------------------------------------------- 保存(localStorage はすべて try/catch。使えなくても動く)

function load(key, def) {
  try {
    const v = localStorage.getItem(`claude-office.${key}`);
    return v === null ? def : v;
  } catch {
    return def;
  }
}

function save(key, value) {
  try {
    localStorage.setItem(`claude-office.${key}`, value);
  } catch {
    // 保存できなくても動く
  }
}

const params = new URLSearchParams(location.search);
const store = {
  state: null,
  account: null,
  sessions: null,
  connected: true,
  date: dayKeyOf(Date.now()),
  previewHour: params.has('t') ? Math.max(0, Math.min(24, Number(params.get('t')) || 0)) : null,
  viewHour: 12,
  tab: ['groups', 'history'].includes(params.get('tab')) ? params.get('tab') : 'live',
  search: '',
  projFilter: '',
  openGroups: new Set(JSON.parse(load('open-groups', '[]') || '[]')),
  options: {
    // 教室(default)と異世界の酒場(isekai)。保存済みの古い値(makai など)は教室に戻す
    skin: [params.get('skin'), load('skin', 'default')].find((s) => SKINS.includes(s)) || 'default',
    effects: load('effects', 'on'),
    shadows: load('shadows', 'on'),
    fps: load('fps', '60'),
    resolution: load('resolution', 'high'),
    ultra: load('ultra', 'off'),
    mode: 'standard',
  },
};
let fontScale = Number(load('font-scale', '1')) || 1;

function computeMode() {
  const o = store.options;
  store.options.mode = Object.entries(MODES).find(([, m]) => Object.entries(m).every(([k, v]) => o[k] === v))?.[0] || 'custom';
}
computeMode();

// ---------------------------------------------------------------- 描画の土台

const stage = document.getElementById('stage');
// 光のにじみ(ブルーム)を使うスキン。にじみを使うときは、場面を別のバッファに描いてから画面へ写すだけなので、
// 画面側のなめらか処理(MSAA: 画面の4倍のバッファ)と奥行きのバッファは使われない → 作らない(見た目は同じで GPU のメモリが減る)。
// この設定は WebGL を作ったあとは変えられないので、にじみの有無が変わるときは描画の土台(canvas)ごと作り直す。
// カメラ操作とクリックは canvas ではなく #stage で受けるので、作り直しても付け直さなくてよい
const BLOOM_SKINS = new Set(['isekai', 'makai']);
let renderer = null;
let rendererForComposer = null;
let composer = null;
let bloomPass = null;

function setupRenderer(forComposer) {
  if (renderer && rendererForComposer === forComposer) {
    return;
  }
  if (renderer) {
    composer?.dispose();
    composer = null;
    bloomPass = null;
    renderer.dispose();
    renderer.forceContextLoss();
    renderer.domElement.remove();
  }
  renderer = new THREE.WebGLRenderer(forComposer ? { antialias: false, depth: false } : { antialias: true });
  rendererForComposer = forComposer;
  renderer.toneMapping = THREE.NeutralToneMapping;
  renderer.toneMappingExposure = 1.0;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  renderer.shadowMap.autoUpdate = false;
  stage.appendChild(renderer.domElement);
  if (window.__claudeOffice) {
    window.__claudeOffice.renderer = renderer;
  }
}
const labelRenderer = new CSS2DRenderer({ element: document.getElementById('labels') });

const scene = new THREE.Scene();
const VIEW_H = 30;
const camera = new THREE.OrthographicCamera(-20, 20, VIEW_H / 2, -VIEW_H / 2, 0.1, 300);
camera.position.set(32, 30, 34);
camera.zoom = 1.1;
const controls = new OrbitControls(camera, stage);
controls.target.set(-1, 1, -2);
controls.enableDamping = true;
controls.minZoom = 0.6;
controls.maxZoom = 4;
controls.minPolarAngle = 0.35;
controls.maxPolarAngle = 1.3;
controls.zoomToCursor = true;
controls.update();

const sky = createSky();
scene.add(sky.mesh);
const hemi = new THREE.HemisphereLight('#dfe8ff', '#5a6a3a', 0.8);
scene.add(hemi);
const sunLight = new THREE.DirectionalLight('#ffffff', 1.2);
sunLight.castShadow = true;
sunLight.shadow.mapSize.set(2048, 2048);
Object.assign(sunLight.shadow.camera, { left: -24, right: 24, top: 24, bottom: -24, near: 1, far: 120 });
sunLight.shadow.bias = -0.0005;
scene.add(sunLight);
scene.add(sunLight.target);

// 島は右パネルを避けた空きスペースの中央に来るようカメラをずらす
function resize() {
  const w = window.innerWidth;
  const h = window.innerHeight;
  const pr = store.options.resolution === 'high' ? Math.min(2, window.devicePixelRatio || 1) : 1;
  renderer.setPixelRatio(pr);
  renderer.setSize(w, h);
  labelRenderer.setSize(w, h);
  const panel = Math.min(w * 0.4, 362 * fontScale);
  const fullW = w + panel;
  const halfW = (VIEW_H / 2) * (fullW / h);
  camera.left = -halfW;
  camera.right = halfW;
  camera.top = VIEW_H / 2;
  camera.bottom = -VIEW_H / 2;
  camera.setViewOffset(fullW, h, panel, 0, w, h);
  camera.updateProjectionMatrix();
  if (composer) {
    composer.setPixelRatio(pr);
    composer.setSize(w, h);
    bloomPass.resolution.set(w / 2, h / 2);
  }
}
window.addEventListener('resize', resize);

// ---------------------------------------------------------------- スキン

let ctx = null;
let wander = null;

// にじみを使うか(スキン・エフェクト・超軽量で決まる)。スキンを作る前は名前で判断する
function wantComposer() {
  const o = store.options;
  const bloom = ctx ? ctx.bloom : BLOOM_SKINS.has(o.skin);
  return !!bloom && o.effects === 'on' && o.ultra !== 'on';
}
setupRenderer(wantComposer());
const avatars = new Map(); // id -> { avatar, bubble, tag, floaty, subs:[], seat }
// 超軽量用の●の玉の形(全員で共有)
const DOT_GEO = new THREE.SphereGeometry(0.42, 14, 10);
let seatMap = new Map();

function clearAvatars() {
  for (const a of avatars.values()) {
    removeAvatar(a);
  }
  avatars.clear();
  seatMap = new Map();
}

function removeAvatar(a) {
  wander?.release(a);
  for (const o of [a.bubble, a.tag, a.floaty]) {
    o.element.remove();
  }
  for (const s of a.subs) {
    s.label.element.remove();
    s.avatar.root.parent?.remove(s.avatar.root);
    s.avatar.dispose();
  }
  a.avatar.root.parent?.remove(a.avatar.root);
  a.avatar.dispose();
  a.dot.material.dispose();
}

// スキンを作り直すときの片付け。Kit で作らなかった形・材質・絵(床や溶岩の Canvas など)も GPU から消す
function disposeTree(root) {
  const items = new Set();
  root.traverse((o) => {
    if (o.geometry) {
      items.add(o.geometry);
    }
    for (const m of Array.isArray(o.material) ? o.material : o.material ? [o.material] : []) {
      items.add(m);
      for (const k of Object.keys(m)) {
        if (m[k] && m[k].isTexture) {
          items.add(m[k]);
        }
      }
    }
  });
  items.forEach((x) => x.dispose());
}

// 動かない家具を材質ごとに1つにまとめる。どれが動くかは、毎フレームの処理を時刻・昼夜・作業中/待ちを変えて
// 実際に回し、位置・向き・大きさ・表示が変わった物を「動く物」とする。動く物を中に持つグループ(犬・鳥・時計など)は
// 境界として、その中だけでまとめる(試した間にたまたま止まっていた役者の体も、入れ物ごと動けるように)。
// 時刻は今より少し前を使い、経過時間で動く処理(犬の歩きなど)が止まらないようにする
function bakeWorld() {
  const snap = [];
  const mats = new Map();
  // 色は 1 を超えて光らせる物もあるので、丸めずに生の値で比べる
  const rgb = (c) => (c ? `${c.r},${c.g},${c.b}` : '');
  const matKey = (m) => `${rgb(m.color)}|${rgb(m.emissive)}|${m.emissiveIntensity}|${m.opacity}|${m.visible}`;
  ctx.root.traverse((o) => {
    snap.push([o, o.position.clone(), o.quaternion.clone(), o.scale.clone(), o.visible]);
    for (const m of Array.isArray(o.material) ? o.material : o.material ? [o.material] : []) {
      mats.set(m, matKey(m));
    }
  });
  const moved = new Set();
  const changed = new Set();
  const t0 = Math.max(0, clock.getElapsedTime() - 0.6);
  for (let k = 0; k < 12; k++) {
    const t = t0 + k * 0.05;
    const hour = (k * 2.1) % 24;
    const look = timeLook(hour, ctx.keys);
    ctx.update?.(t, look);
    ctx.fancy?.update(t, look);
    ctx.spot?.update(t, k % 2 === 1);
    ctx.cloudsObj?.update(t, look.cloud);
    ctx.clock?.update(new Date(2026, 0, 1, Math.floor(hour), k * 5, k * 7));
    for (const [o, p, q, s, vis] of snap) {
      if (!moved.has(o) && (!o.position.equals(p) || !o.quaternion.equals(q) || !o.scale.equals(s) || o.visible !== vis)) {
        moved.add(o);
      }
    }
    for (const [m, key] of mats) {
      if (matKey(m) !== key) {
        changed.add(m);
      }
    }
  }
  // 設定で表示を切り替える物
  if (ctx.fancy) {
    moved.add(ctx.fancy.group);
  }
  // 光を受ける普通の材質で、色だけが違う物(光らない・絵なし・不透明・試した間に色が変わらない)は、
  // 頂点色にして色違いもまとめる。陰影の計算は同じ(材質の色(白) × 頂点色)
  const shared = new Map();
  const recolor = (m) => {
    if (!m.isMeshStandardMaterial || m.isMeshPhysicalMaterial || changed.has(m) || m.emissive.getHex() !== 0 || m.envMap || m.alphaTest || m.opacity !== 1) {
      return null;
    }
    const sig = [m.roughness, m.metalness, m.flatShading, m.side, m.fog, m.toneMapped, m.wireframe, m.depthTest, m.depthWrite, m.colorWrite, m.polygonOffset, m.dithering, m.blending].join('|');
    if (!shared.has(sig)) {
      const v = m.clone();
      v.color.set('#ffffff');
      v.vertexColors = true;
      shared.set(sig, v);
    }
    return shared.get(sig);
  };
  ctx.moved = moved;
  mergeStatic(ctx.root, { dynamic: moved, region: 'auto', recolor });
}

// NPC(酒場=女主人、教室=女性教師)。セッションではないので席には座らず、持ち場でにこにこ立っている
const NPCS = {
  isekai: { id: 'npc-louida', name: '酒場の主人', color: '#c0392b', pos: [4.0, 0.14, -7.7], rot: 0, overrides: { race: 'human', job: 5, hair: '#a6463b', style: 1, eye: '#2d4f8a', skin: '#ffe0c7', clothes: 0, headphones: false } },
  default: { id: 'npc-teacher', name: '先生', color: '#3d4a7a', pos: [-10.3, 0.12, -1.2], rot: Math.PI / 2, overrides: { race: 'human', job: 6, hair: '#3a2a22', style: 3, eye: '#333a44', skin: '#fbe3d3', clothes: 1, headphones: false } },
};

function addNpc(skin) {
  const def = NPCS[skin];
  if (!def) {
    return;
  }
  const avatar = createAvatar({ id: def.id, color: def.color, skin, standing: true, scale: 1.08, overrides: def.overrides });
  avatar.root.position.set(...def.pos);
  avatar.root.rotation.y = def.rot;
  ctx.root.add(avatar.root);
  // 名札は出さない(家具の名札と同じく、見ればわかるので)
  ctx.npc = { avatar };
}

function removeNpc() {
  if (ctx && ctx.npc) {
    ctx.npc.avatar.root.parent?.remove(ctx.npc.avatar.root);
    ctx.npc.avatar.dispose();
    ctx.npc = null;
  }
}

async function buildSkin() {
  const loading = document.getElementById('loading');
  loading.hidden = false;
  clearAvatars();
  if (ctx) {
    removeNpc();
    scene.remove(ctx.root);
    ctx.kit.dispose();
    disposeTree(ctx.root);
  }
  const skin = store.options.skin;
  document.body.classList.remove(...SKINS.map((s) => `skin-${s}`));
  document.body.classList.add(`skin-${skin}`);
  ctx = { kit: new Kit(false), root: new THREE.Group(), lights: [], keys: undefined, sky: { aurora: 0, crack: 0, moon2: false, moonColor: '#f4f1e0' }, bloom: false, fog: null, effects: true };
  scene.add(ctx.root);
  // 酒場と魔界は光る物(炎・酒瓶・溶岩)をブルームでにじませる。ctx.fancy は「標準」モードだけの演出(きらきら・火の粉)
  if (skin === 'makai') {
    buildMakai(ctx);
    ctx.bloomScale = 0.7;
    ctx.bloom = true;
    ctx.bloomThreshold = 1.0;
  } else if (skin === 'isekai') {
    buildTavern(ctx);
    ctx.bloomScale = 0.55;
    ctx.bloom = true;
    ctx.bloomThreshold = 1.0;
  } else {
    buildClassroom(ctx);
  }
  // 立ち寄り先(水槽・カウンター・大釜)と、席を立って歩き回る仕組み
  buildSpot(ctx, skin);
  bakeWorld();
  addNpc(skin);
  wander = createWander(ctx);
  ctx.ultra = null;
  // 点光源は4灯まで
  ctx.lights.forEach((l, i) => {
    l.visible = i < 4;
  });
  scene.fog = ctx.fog;
  hemi.color.set(ctx.hemi ? ctx.hemi.sky : '#dfe8ff');
  hemi.groundColor.set(ctx.hemi ? ctx.hemi.ground : '#5a6a3a');
  sky.uniforms.uAurora.value = ctx.sky.aurora;
  sky.uniforms.uCrack.value = ctx.sky.crack;
  sky.uniforms.uMoonColor.value.set(ctx.sky.moonColor);
  if (ctx.sky.big) {
    sky.uniforms.uBigColor.value.set(ctx.sky.big.color);
  }
  applyOptions();
  if (store.state && store.state.archive) {
    ctx.shelf.setBooks(store.state.archive);
  }
  syncPeople();
  // シェーダを先に組み立てて、終わるまで描かない(最長8秒で打ち切り)
  try {
    await Promise.race([renderer.compileAsync(scene, camera), new Promise((r) => setTimeout(r, 8000))]);
  } catch {
    // 古いブラウザは compileAsync が無い
  }
  renderer.shadowMap.needsUpdate = true;
  loading.hidden = true;
}

function applyOptions() {
  const o = store.options;
  ctx.effects = o.effects === 'on';
  applyUltra();
  if (ctx.fancy) {
    ctx.fancy.group.visible = ctx.effects;
  }
  // にじみの有無が変わったら描画の土台を作り直す(スキンを作り直す途中で、まだ何も描いていないときだけ呼ばれる)
  setupRenderer(wantComposer());
  renderer.shadowMap.enabled = o.shadows === 'on';
  sunLight.castShadow = o.shadows === 'on';
  scene.traverse((m) => {
    if (m.material) {
      m.material.needsUpdate = true;
    }
  });
  // ブルームは追加スキンだけ(教室は直接描く)
  if (wantComposer()) {
    if (!composer) {
      composer = new EffectComposer(renderer);
      composer.addPass(new RenderPass(scene, camera));
      bloomPass = new UnrealBloomPass(new THREE.Vector2(window.innerWidth / 2, window.innerHeight / 2), 0.8, 0.5, ctx.bloomThreshold ?? 0.75);
      composer.addPass(bloomPass);
      // 最後の OutputPass は画面へ写すだけなので、読み書きのバッファを入れ替えさせない。
      // こうすると場面はいつも readBuffer に描かれ、writeBuffer(画面と同じ大きさ)は一度も使われず GPU に確保されない
      const output = new OutputPass();
      output.needsSwap = false;
      composer.addPass(output);
      // にじみ用のバッファは画面全体の板を描くだけなので奥行きは要らない(readBuffer の奥行きは場面に要るので残す)。
      // GPU に確保されるのは初めて描くときなので、その前に外せば確保されない(大きさを変えても引き継がれる)
      [composer.writeBuffer, bloomPass.renderTargetBright, ...bloomPass.renderTargetsHorizontal, ...bloomPass.renderTargetsVertical].forEach((rt) => {
        rt.depthBuffer = false;
      });
    }
  } else if (composer) {
    composer.dispose();
    composer = null;
    bloomPass = null;
  }
  resize();
}

// ---------------------------------------------------------------- 超軽量表示(壁・時計・人の玉だけ)

function buildUltra() {
  const { kit } = ctx;
  const R = { x0: -12, x1: 6, z0: -10, z1: 3 };
  const g = new THREE.Group();
  kit.box(g, R.x1 - R.x0, 0.12, R.z1 - R.z0, '#ece6da', (R.x0 + R.x1) / 2, 0.06, (R.z0 + R.z1) / 2);
  kit.box(g, R.x1 - R.x0, 5, 0.3, '#fffdf8', (R.x0 + R.x1) / 2, 2.5, R.z0 - 0.15);
  kit.box(g, 0.3, 5, R.z1 - R.z0, '#f2eee4', R.x0 - 0.15, 2.5, (R.z0 + R.z1) / 2);
  ctx.ultraClock = wallClock(kit, g, 2.0, 3.6, R.z0 + 0.02, 0, '#5a4636');
  ctx.root.add(g);
  ctx.ultra = g;
}

// 超軽量のON/OFFに合わせて、表示するものを切り替える(人の追加・スキン構築のたびに呼ぶ)
function applyUltra() {
  if (!ctx) {
    return;
  }
  const on = store.options.ultra === 'on';
  if (on && !ctx.ultra) {
    buildUltra();
  }
  const people = new Set([...avatars.values()].map((a) => a.avatar.root));
  for (const c of ctx.root.children) {
    if (c === ctx.ultra) {
      c.visible = on;
    } else if (!people.has(c)) {
      c.visible = !on;
    }
  }
  for (const a of avatars.values()) {
    a.avatar.body.visible = !on;
    a.dot.visible = on;
    a.subs.forEach((s) => {
      s.avatar.root.visible = !on;
    });
  }
}

// ---------------------------------------------------------------- 人(キャラ・吹き出し・名札・サブエージェント)

function label(className, parent, y) {
  const el = document.createElement('div');
  el.className = className;
  const obj = new CSS2DObject(el);
  obj.position.set(0, y, 0);
  parent.add(obj);
  return obj;
}

function syncPeople() {
  if (!ctx || !store.state) {
    return;
  }
  const live = store.state.live;
  seatMap = assignSeats(live, ctx.seats, seatMap);
  const now = Date.now();
  for (const [id, a] of avatars) {
    if (!seatMap.has(id)) {
      removeAvatar(a);
      avatars.delete(id);
    }
  }
  for (const item of live) {
    const seatIdx = seatMap.get(item.id);
    if (seatIdx === undefined) {
      continue;
    }
    let a = avatars.get(item.id);
    const color = projectColor(item.project);
    if (!a || a.seat !== seatIdx) {
      if (a) {
        removeAvatar(a);
      }
      const avatar = createAvatar({ id: item.id, color, skin: store.options.skin });
      const seat = ctx.seats[seatIdx];
      avatar.root.position.set(seat.x, 0, seat.z);
      avatar.root.rotation.y = seat.rot;
      avatar.root.userData.sessionId = item.id;
      ctx.root.add(avatar.root);
      a = { avatar, seat: seatIdx, subs: [], move: wander.init(clock.getElapsedTime()) };
      // 超軽量用の●の玉(プロジェクトの色)
      a.dot = new THREE.Mesh(DOT_GEO, new THREE.MeshBasicMaterial({ color }));
      a.dot.position.y = 1.1;
      a.dot.visible = false;
      avatar.root.add(a.dot);
      a.bubble = label('bubble', avatar.root, 2.85);
      a.tag = label('nametag', avatar.root, -0.15);
      a.floaty = label('floaty', avatar.head, 0.9);
      for (const o of [a.bubble, a.tag]) {
        o.element.addEventListener('click', () => selectPerson(item.id));
        o.element.addEventListener('dblclick', () => openInVscode(item.id));
      }
      avatars.set(item.id, a);
    }
    a.item = item;
    const st = deriveState(item, now);
    a.state = st;
    const wait = st.since ? ` · ${elapsed(now - st.since)}` : '';
    const action = (st.action || '').length > 34 ? `${st.action.slice(0, 34)}…` : st.action;
    const bubbleHtml = `<span class="em">${st.emoji}</span> ${esc(action)}<span style="opacity:.6">${wait}</span>`;
    if (a.bubble.element.__html !== bubbleHtml) {
      a.bubble.element.__html = bubbleHtml;
      a.bubble.element.innerHTML = bubbleHtml;
    }
    a.bubble.element.className = `bubble ${st.key}`;
    const tagHtml = `<span class="dot" style="background:${color}"></span>${esc(item.project)}${item.live.name ? ` <span style="opacity:.7">${esc(item.live.name)}</span>` : ''}`;
    if (a.tag.element.__html !== tagHtml) {
      a.tag.element.__html = tagHtml;
      a.tag.element.innerHTML = tagHtml;
    }
    const floatyText = st.key === 'sleeping' ? 'Z z' : st.key === 'thinking' ? '？' : '';
    if (a.floaty.element.__text !== floatyText) {
      a.floaty.element.__text = floatyText;
      a.floaty.element.innerHTML = floatyText ? `<span>${floatyText}</span>` : '';
    }
    syncSubagents(a, item);
  }
  applyUltra();
}

// サブエージェント(作業中のみ)は机の周りに最大4体の小さな立ち姿で跳ねる
function syncSubagents(a, item) {
  const subs = (item.subagents || []).slice(0, 4);
  while (a.subs.length > subs.length) {
    const s = a.subs.pop();
    s.label.element.remove();
    s.avatar.root.parent?.remove(s.avatar.root);
    s.avatar.dispose();
  }
  subs.forEach((sub, i) => {
    if (!a.subs[i] || a.subs[i].id !== sub.id) {
      if (a.subs[i]) {
        a.subs[i].label.element.remove();
        a.subs[i].avatar.root.parent?.remove(a.subs[i].avatar.root);
      }
      const avatar = createAvatar({ id: sub.id, color: SUB_COLOR[sub.type] || '#2bb3a6', skin: store.options.skin, standing: true, scale: 0.55 });
      const seat = ctx.seats[a.seat];
      const ang = seat.rot + Math.PI / 2 + (i - 1.5) * 0.7;
      avatar.root.position.set(seat.x + Math.sin(ang) * 1.5, 0, seat.z + Math.cos(ang) * 1.5);
      avatar.root.rotation.y = seat.rot + Math.PI;
      ctx.root.add(avatar.root);
      const l = label('floaty', avatar.root, 2.1);
      l.element.innerHTML = `<span>${SUB_EMOJI[sub.type] || '🤖'}</span>`;
      a.subs[i] = { id: sub.id, avatar, label: l };
    }
  });
}

// ---------------------------------------------------------------- カメラの寄り(操作で中断)

let focus = null;
controls.addEventListener('start', () => {
  focus = null;
});

function selectPerson(id) {
  const a = avatars.get(id);
  if (a) {
    const seat = ctx.seats[a.seat];
    focus = { target: new THREE.Vector3(seat.x, 1.4, seat.z), zoom: 1.9 };
  }
  openDetail(id);
}

// ---------------------------------------------------------------- クリック判定

const ray = new THREE.Raycaster();
const pointer = new THREE.Vector2();
const tip = document.getElementById('tip');

function pick(e) {
  const r = stage.getBoundingClientRect();
  pointer.set(((e.clientX - r.left) / r.width) * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1);
  ray.setFromCamera(pointer, camera);
  const hits = ray.intersectObjects(ctx.root.children, true);
  for (const h of hits) {
    let o = h.object;
    let shown = true;
    for (let p = o; p; p = p.parent) {
      shown = shown && p.visible;
    }
    if (!shown) {
      continue;
    }
    while (o) {
      if (o.userData.kind === 'book') {
        // 本棚は1つの InstancedMesh。何冊目に当たったかで会話を引く
        const session = o.userData.sessions ? o.userData.sessions[h.instanceId] : o.userData.session;
        if (session) {
          return { kind: 'book', session };
        }
      }
      if (o.userData.sessionId) {
        return { kind: 'person', id: o.userData.sessionId };
      }
      if (o.userData.kind === 'board') {
        return { kind: 'board' };
      }
      o = o.parent;
    }
  }
  return null;
}

let downAt = null;
stage.addEventListener('pointerdown', (e) => {
  downAt = { x: e.clientX, y: e.clientY };
});
let clickTimer = null;
stage.addEventListener('click', (e) => {
  if (!ctx || (downAt && Math.hypot(e.clientX - downAt.x, e.clientY - downAt.y) > 5)) {
    return;
  }
  const p = pick(e);
  clearTimeout(clickTimer);
  if (!p) {
    return;
  }
  clickTimer = setTimeout(() => {
    if (p.kind === 'person') {
      selectPerson(p.id);
    } else if (p.kind === 'book') {
      store.date = dayKeyOf(p.session.end || p.session.start);
      openDetail(p.session.id);
      poll();
    } else if (p.kind === 'board') {
      store.tab = 'live';
      renderSide(store, actions);
    }
  }, 230);
});
stage.addEventListener('dblclick', (e) => {
  clearTimeout(clickTimer);
  const p = ctx && pick(e);
  if (p && (p.kind === 'person' || p.kind === 'book')) {
    openInVscode(p.kind === 'person' ? p.id : p.session.id);
  }
});
stage.addEventListener('pointermove', (e) => {
  if (!ctx) {
    return;
  }
  const p = pick(e);
  if (p && p.kind === 'book') {
    const s = p.session;
    tip.innerHTML = `${esc(s.title || '(無題)')}<br>${esc(s.project)}・${dateLabel(s.end || s.start)}・🛠️${s.toolCalls}`;
    tip.style.left = `${e.clientX + 14}px`;
    tip.style.top = `${e.clientY + 10}px`;
    tip.hidden = false;
    stage.style.cursor = 'pointer';
  } else {
    tip.hidden = true;
    stage.style.cursor = p ? 'pointer' : '';
  }
});
window.addEventListener('keydown', (e) => {
  if (e.key === 'Escape' && !document.querySelector('dialog[open]')) {
    closeTopWindow();
  }
});

// ---------------------------------------------------------------- UI の操作

const actions = {
  openDetail: (id) => openDetail(id),
  setTab(tab) {
    store.tab = tab;
    store.search = '';
    renderSide(store, actions);
    if (tab !== 'live') {
      pollSessions();
    }
  },
  saveOpenGroups() {
    save('open-groups', JSON.stringify([...store.openGroups]));
  },
  shiftDate(step) {
    if (step === 0) {
      store.date = dayKeyOf(Date.now());
    } else {
      const d = new Date(`${store.date}T12:00:00`);
      d.setDate(d.getDate() + step);
      store.date = dayKeyOf(d.getTime());
    }
    poll();
  },
  setPreview(h) {
    store.previewHour = h;
    renderHUD(store, actions);
  },
  fontStep(dir) {
    const i = FONT_STEPS.findIndex((v) => v >= fontScale - 0.001);
    fontScale = FONT_STEPS[Math.max(0, Math.min(FONT_STEPS.length - 1, (i < 0 ? 1 : i) + dir))];
    save('font-scale', String(fontScale));
    document.documentElement.style.setProperty('--fs', String(fontScale));
    resize();
    talkFontChanged();
  },
  async setOption(key, value) {
    if (key === 'notify') {
      openNotifyDialog();
      return;
    }
    if (key === 'mode') {
      Object.assign(store.options, MODES[value]);
      for (const [k, v] of Object.entries(MODES[value])) {
        save(k, v);
      }
    } else {
      store.options[key] = value;
      save(key, value);
    }
    computeMode();
    renderHUD(store, actions);
    if (key === 'skin' || wantComposer() !== rendererForComposer) {
      // にじみの有無が変わるときは描画の土台を作り直すので、場面も作り直す
      // (一度 GPU へ送ったら Canvas を縮めている絵があり、新しい土台へは送り直せないため)
      await buildSkin();
    } else {
      document.getElementById('loading').hidden = false;
      applyOptions();
      try {
        await Promise.race([renderer.compileAsync(scene, camera), new Promise((r) => setTimeout(r, 8000))]);
      } catch {
        // compileAsync が無い
      }
      renderer.shadowMap.needsUpdate = true;
      document.getElementById('loading').hidden = true;
    }
  },
};
document.documentElement.style.setProperty('--fs', String(fontScale));

// ---------------------------------------------------------------- ポーリング

let lastArchive = 0;

async function poll() {
  const withArchive = Date.now() - lastArchive > 60000;
  try {
    const r = await fetch(`/api/state?date=${store.date}${withArchive ? '&archive=1' : ''}`);
    const s = await r.json();
    store.connected = true;
    if (withArchive && s.archive) {
      lastArchive = Date.now();
      ctx?.shelf.setBooks(s.archive);
    } else if (store.state && store.state.archive) {
      s.archive = store.state.archive;
    }
    store.state = s;
  } catch {
    store.connected = false;
  }
  syncPeople();
  renderSide(store, actions);
  renderTimeline(store, actions);
  const label = store.date === dayKeyOf(Date.now()) ? '今日' : dateLabel(new Date(`${store.date}T12:00:00`).getTime());
  ctx?.board.draw(store.state && store.state.stats, label);
}

async function pollAccount() {
  try {
    store.account = await (await fetch('/api/account')).json();
  } catch {
    // 前回値のまま
  }
}

async function pollSessions() {
  try {
    store.sessions = await (await fetch('/api/sessions')).json();
    renderSide(store, actions);
  } catch {
    // 前回値のまま
  }
}

// ---------------------------------------------------------------- 描画ループ

const clock = new THREE.Clock();
let lastFrame = 0;
let lastShadow = 0;
let lastHud = 0;

function updateTime(t) {
  const now = new Date();
  const hour = store.previewHour !== null ? store.previewHour : now.getHours() + now.getMinutes() / 60 + now.getSeconds() / 3600;
  store.viewHour = hour;
  const look = timeLook(hour, ctx.keys);
  document.body.classList.toggle('night', look.night);
  const u = sky.uniforms;
  u.uTop.value.copy(look.top);
  u.uMid.value.copy(look.mid);
  u.uBottom.value.copy(look.bottom);
  u.uStars.value = look.stars;
  u.uTime.value = t;
  u.uAspect.value = window.innerWidth / window.innerHeight;
  const arc = arcPositions(hour);
  // 大きな月を出すスキンは、ふつうの太陽と月の円は描かない(光の向きは同じように動く)
  const discs = ctx.sky.big ? 0 : 1;
  u.uSun.value.set(arc.sun.x, arc.sun.y, arc.sun.on * discs);
  u.uMoon.value.set(arc.moon.x, arc.moon.y, arc.moon.on * discs);
  u.uMoon2.value.set(arc.moon.x + 0.07, arc.moon.y + 0.06, ctx.sky.moon2 ? arc.moon.on : 0);
  u.uSunColor.value.copy(look.sunColor);
  const big = ctx.sky.big;
  u.uBig.value.set(big ? big.x : 0, big ? big.y : 0, big ? big.r : 0, big ? (big.always ? 1 : 0.45 + look.stars * 0.55) : 0);
  hemi.intensity = look.hemi * (ctx.hemi ? ctx.hemi.boost : 1) * (ctx.lightBoost?.hemi ?? 1);
  // 平行光は昼は太陽・夜は月から
  const day = arc.sun.on > 0;
  const tt = day ? arc.sunT : arc.moonT;
  const ang = Math.PI * Math.min(1, Math.max(0, tt));
  sunLight.position.set(Math.cos(ang) * -40 + 10, 12 + Math.sin(ang) * 30, 26);
  sunLight.target.position.set(-2, 0, -3);
  sunLight.color.copy(look.sunColor);
  sunLight.intensity = look.sunPower * (day ? 1 : 0.6) * (ctx.lightBoost?.sun ?? 1);
  for (const l of ctx.lights) {
    l.intensity = look.lamp * l.userData.power;
  }
  ctx.cloudsObj?.update(t, look.cloud);
  if (bloomPass) {
    bloomPass.strength = (0.35 + look.stars * 0.75) * (ctx.bloomScale ?? 1);
  }
  ctx.update?.(t, look);
  if (ctx.effects) {
    ctx.fancy?.update(t, look);
  }
  const d = store.previewHour !== null ? new Date(new Date().setHours(Math.floor(hour), Math.round((hour % 1) * 60), 0)) : now;
  ctx.clock?.update(d);
  ctx.ultraClock?.update(d);
  ctx.calendar?.update(now);
}

let prevT = 0;
// 描く回数の上限。設定の fps(60/30)が上限で、操作していないときは控えめにする(見た目・機能は同じで、動きの細かさだけ):
//  - ほかのアプリを使っている(窓にフォーカスが無い)= 横で眺めているときは 20 回/秒
//  - フォーカスはあるが 3 秒操作が無いときは 30 回/秒
//  - マウス・ホイール・キー操作の直後と、人へ寄っていくカメラの移動中は設定どおり
const FPS_AWAY = 20, FPS_CALM = 30;
let lastInput = -1e9;
for (const ev of ['pointermove', 'pointerdown', 'pointerup', 'wheel', 'keydown']) {
  window.addEventListener(ev, () => { lastInput = performance.now(); }, { capture: true, passive: true });
}
function fpsCap() {
  const max = store.options.fps === '30' ? 30 : 60;
  if (focus) {
    return max;
  }
  if (!document.hasFocus()) {
    return Math.min(max, FPS_AWAY);
  }
  return performance.now() - lastInput > 3000 ? Math.min(max, FPS_CALM) : max;
}
function frame() {
  requestAnimationFrame(frame);
  const t = clock.getElapsedTime();
  const minDt = 1 / (fpsCap() + 1);
  if (t - lastFrame < minDt) {
    return;
  }
  lastFrame = t;
  if (!ctx) {
    return;
  }
  updateTime(t);
  ctx.npc?.avatar.update(t, 'npc');
  const dt = Math.min(0.1, t - prevT);
  prevT = t;
  let busy = false;
  for (const a of avatars.values()) {
    const motion = wander.update(a, t, dt);
    busy = busy || motion.mode === 'act';
    a.avatar.update(t, a.state ? a.state.key : 'waiting', motion);
    for (const s of a.subs) {
      s.avatar.update(t, 'working');
    }
    const g = ctx.glows[a.seat];
    if (g && a.state && g.key !== a.state.key) {
      // 色の文字列の読み取りはゴミが出るので、状態が変わったときだけ
      g.key = a.state.key;
      g.screen.material.color.set(GLOW[a.state.key] || GLOW.sleeping);
    }
  }
  ctx.spot?.update(t, busy);
  if (focus) {
    controls.target.lerp(focus.target, 0.08);
    camera.zoom += (focus.zoom - camera.zoom) * 0.08;
    camera.updateProjectionMatrix();
    if (Math.abs(camera.zoom - focus.zoom) < 0.01) {
      focus = null;
    }
  }
  controls.update();
  // 影の描き直しは0.25秒ごと
  if (renderer.shadowMap.enabled && t - lastShadow > 0.25) {
    lastShadow = t;
    renderer.shadowMap.needsUpdate = true;
  }
  if (composer) {
    composer.render();
  } else {
    renderer.render(scene, camera);
  }
  labelRenderer.render(scene, camera);
  if (t - lastHud > 0.5) {
    lastHud = t;
    renderHUD(store, actions);
  }
}

// ---------------------------------------------------------------- 起動

async function start() {
  resize();
  renderHUD(store, actions);
  await poll();
  // 会話欄・別の窓は HTML なので 3D の準備を待たない。起動・終了したら開いている詳細の窓を描き直す
  initTalks({ toast, onChange: refreshWindows, focusDetail });
  await buildSkin();
  pollAccount().then(() => renderHUD(store, actions));
  pollSessions();
  setInterval(poll, 3000);
  setInterval(() => pollAccount().then(() => renderHUD(store, actions)), 60000);
  setInterval(pollSessions, 30000);
  setInterval(refreshWindows, 3000);
  frame();
  // URL 引数: ?session=<id> / ?first で詳細を開いて開始
  const sid = params.get('session');
  if (sid) {
    openDetail(sid);
  } else if (params.has('first') && store.state && store.state.live.length) {
    selectPerson(store.state.live[0].id);
  }
  if (store.previewHour !== null) {
    renderTimeline(store, actions);
  }
  window.__claudeOffice = { store, avatars, renderer, scene, camera, toast, hhmm, get ctx() { return ctx; } };
}

start();
