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
import { createWander } from './wander.js';
import { createAvatar } from './avatar.js';
import { deriveState, assignSeats, elapsed } from './people.js';
import { renderHUD, renderSide, renderTimeline, openDetail, refreshWindows, closeTopWindow, toast, openInVscode, esc, dayKeyOf, dateLabel, hhmm } from './ui.js';
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
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.toneMapping = THREE.NeutralToneMapping;
renderer.toneMappingExposure = 1.0;
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.shadowMap.autoUpdate = false;
stage.appendChild(renderer.domElement);
const labelRenderer = new CSS2DRenderer({ element: document.getElementById('labels') });

const scene = new THREE.Scene();
const VIEW_H = 30;
const camera = new THREE.OrthographicCamera(-20, 20, VIEW_H / 2, -VIEW_H / 2, 0.1, 300);
camera.position.set(32, 30, 34);
camera.zoom = 1.1;
const controls = new OrbitControls(camera, renderer.domElement);
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

let composer = null;
let bloomPass = null;

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
const avatars = new Map(); // id -> { avatar, bubble, tag, floaty, subs:[], seat }
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
    s.avatar.dispose();
  }
  a.avatar.root.parent?.remove(a.avatar.root);
  a.avatar.dispose();
}

// NPC(酒場=女主人、教室=女性教師)。セッションではないので席には座らず、持ち場でにこにこ立っている
const NPCS = {
  isekai: { id: 'npc-louida', name: 'ルイーダ', color: '#c0392b', pos: [4.0, 0.14, -7.7], rot: 0, overrides: { race: 'human', job: 5, hair: '#a6463b', style: 1, eye: '#2d4f8a', skin: '#ffe0c7', clothes: 0, headphones: false } },
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
  const tag = label('nametag', avatar.root, -0.15);
  tag.element.innerHTML = `<span class="dot" style="background:${def.color}"></span>${def.name}`;
  ctx.npc = { avatar, tag };
}

function removeNpc() {
  if (ctx && ctx.npc) {
    ctx.npc.tag.element.remove();
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
    ctx.labels?.forEach((l) => l.element.remove());
    scene.remove(ctx.root);
    ctx.kit.dispose();
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
  addNpc(skin);
  // 立ち寄り先(水槽・お酒の棚・血の池)と、席を立って歩き回る仕組み
  buildSpot(ctx, skin);
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
  renderer.shadowMap.enabled = o.shadows === 'on';
  sunLight.castShadow = o.shadows === 'on';
  scene.traverse((m) => {
    if (m.material) {
      m.material.needsUpdate = true;
    }
  });
  // ブルームは追加スキンだけ(教室は直接描く)
  if (ctx.bloom && ctx.effects && o.ultra !== 'on') {
    if (!composer) {
      composer = new EffectComposer(renderer);
      composer.addPass(new RenderPass(scene, camera));
      bloomPass = new UnrealBloomPass(new THREE.Vector2(window.innerWidth / 2, window.innerHeight / 2), 0.8, 0.5, ctx.bloomThreshold ?? 0.75);
      composer.addPass(bloomPass);
      composer.addPass(new OutputPass());
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
      a.dot = new THREE.Mesh(new THREE.SphereGeometry(0.42, 14, 10), new THREE.MeshBasicMaterial({ color }));
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
  const r = renderer.domElement.getBoundingClientRect();
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
        return { kind: 'book', session: o.userData.session };
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
renderer.domElement.addEventListener('pointerdown', (e) => {
  downAt = { x: e.clientX, y: e.clientY };
});
let clickTimer = null;
renderer.domElement.addEventListener('click', (e) => {
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
renderer.domElement.addEventListener('dblclick', (e) => {
  clearTimeout(clickTimer);
  const p = ctx && pick(e);
  if (p && (p.kind === 'person' || p.kind === 'book')) {
    openInVscode(p.kind === 'person' ? p.id : p.session.id);
  }
});
renderer.domElement.addEventListener('pointermove', (e) => {
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
    renderer.domElement.style.cursor = 'pointer';
  } else {
    tip.hidden = true;
    renderer.domElement.style.cursor = p ? 'pointer' : '';
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
    if (key === 'skin') {
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
function frame() {
  requestAnimationFrame(frame);
  const t = clock.getElapsedTime();
  const minDt = store.options.fps === '30' ? 1 / 31 : 1 / 61;
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
    if (g && a.state) {
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
  window.__claudeOffice = { store, avatars, renderer, toast, hhmm };
}

start();
