/*
 * MagicLauncher の画面（index.html から読む）。画面・操作・検索・複数の魔法陣・待機中の動き・Light 版。
 * 魔法陣の描画部品は magic.js。three.js は Light 版では読み込まない（必要なときだけ順に読み込む）。
 * ビルド工程なし・ES Module にしない。
 */
(function () {
  'use strict';

  // ================================================================ 定数
  const LITE_KEY = 'magic-launcher-lite';
  const POLL_MS = 10 * 1000;
  const UNTAGGED = '\u0000untagged'; // タグなしの項目をまとめる仮のタグ
  const CIRCLE_PALETTE = ['#3f7fe0', '#19b6d2', '#7b6cf6', '#1fc7a0', '#b06cf0', '#e0a43a', '#5fd0ff', '#f07a5c'];
  const KIND_EMOJI = { web: '🌐', link: '🔗', folder: '📁', file: '📄', missing: '⚠️', unknown: '❔' };
  const EXEC_RE = /\.(exe|bat|cmd|com|ps1|vbs|vbe|js|jse|wsf|wsh|msi|scr|lnk)$/i;
  const MAX_RESULTS = 60;
  const VENDOR = [
    'vendor/three.min.js', 'vendor/OrbitControls.js', 'vendor/CopyShader.js', 'vendor/LuminosityHighPassShader.js',
    'vendor/EffectComposer.js', 'vendor/RenderPass.js', 'vendor/ShaderPass.js', 'vendor/UnrealBloomPass.js', 'magic.js',
  ];

  // 魔法陣の組み立て（2.1）
  const HUB_R = 2.3;
  const GAP = 1.1;
  const RING_STEP = 2.9;
  const HUB_CLEAR = HUB_R * 1.6; // 枝の曲がり角をこれより中心に近づけない(中心の魔法陣の記号の輪の外)
  const AVOID_R = 1.5; // 回り道を選ぶとき、ほかの円の中心からこれより近くを通る道は使わない(円の半径 1.15 + すき間)
  const OCTI_DIRS = [[1, 0], [0, 1], [1, 1], [1, -1]]; // 八方向の直線の向き(逆向きは同じ直線)
  const FLOAT_SCALE = 0.3; // 漂うときの大きさの揺れ幅(×0.7〜×1.3)
  const FRAME_SPIN = 0.05; // 外枠の記号の輪が時計回りに回る速さ(rad/秒。約 2 分で 1 周)
  const CIRCLE_GAP = 1.6; // 魔法陣どうしの間隔
  const LITE_ITEMS = 48; // これを超えたら目盛りを省き、光の粒を 1 本 1 粒にする

  const $ = (id) => document.getElementById(id);
  const S = {
    state: null, view: null,
    query: '', tags: [], // 絞り込み（検索語・タグ）
    match: null, matchCount: 0, tagMatch: {},
    results: [], sel: 0,
    lite: readLite(), mode: 'none', // '3d' | 'list'
    topH: 92, modal: null,
  };
  let scene3d = null;

  // ================================================================ 小物
  function readLite() {
    try { return localStorage.getItem(LITE_KEY) === '1'; } catch (e) { return false; }
  }
  function writeLite(on) {
    try { localStorage.setItem(LITE_KEY, on ? '1' : '0'); } catch (e) { /* 保存できなくても動く */ }
  }
  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  }
  function norm(s) { return String(s == null ? '' : s).normalize('NFKC').toLowerCase(); }
  function fnv(str) {
    let h = 2166136261;
    for (let i = 0; i < str.length; i++) { h ^= str.charCodeAt(i); h = Math.imul(h, 16777619); }
    return h >>> 0;
  }
  function clamp(x, a, b) { return x < a ? a : x > b ? b : x; }
  function stripQuotes(s) {
    let t = String(s || '').trim();
    while (t.length >= 2 && t[0] === '"' && t[t.length - 1] === '"') t = t.slice(1, -1).trim();
    return t;
  }
  function isUrl(s) { return /^[a-z][a-z0-9+.-]+:/i.test(String(s || '').trim()); }
  // 名前が空なら、URL はホスト名、パスは末尾の名前
  function derivedName(p) {
    p = stripQuotes(p);
    if (!p) return '';
    if (isUrl(p)) {
      try { const u = new URL(p); return u.hostname || p.replace(/^[a-z0-9+.-]+:\/*/i, '').split(/[/?#]/)[0] || p; } catch (e) { return p; }
    }
    const t = p.replace(/[\\/]+$/, '');
    const parts = t.split(/[\\/]/);
    return parts[parts.length - 1] || p;
  }
  function el(tag, cls, html) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html != null) e.innerHTML = html;
    return e;
  }
  function setC(e, c) { e.style.setProperty('--c', c); return e; }

  function api(path, body) {
    const opt = body === undefined
      ? { method: 'GET', headers: { 'X-Launcher': '1' } }
      : { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Launcher': '1' }, body: JSON.stringify(body) };
    return fetch(path, opt).then((r) => r.json().catch(() => ({})).then((j) => {
      if (!r.ok) { const e = new Error(j.error || ('HTTP ' + r.status)); e.status = r.status; throw e; }
      return j;
    }));
  }

  function toast(msg, isErr) {
    const box = $('ml-toasts');
    const t = el('div', 'ml-toast' + (isErr ? ' err' : ''));
    t.textContent = msg;
    box.appendChild(t);
    while (box.children.length > 4) box.removeChild(box.firstChild);
    setTimeout(() => { t.style.transition = 'opacity .4s'; t.style.opacity = '0'; setTimeout(() => t.remove(), 420); }, isErr ? 6000 : 3500);
  }

  // ================================================================ 画面用のデータ
  function buildView(state) {
    const tagsMap = state.tags || {};
    const items = state.items.map((it, idx) => {
      const name = (it.name || '').trim() || derivedName(it.path);
      const emoji = it.kind === 'missing' ? KIND_EMOJI.missing : (it.emoji || KIND_EMOJI[it.kind] || '📄');
      return {
        id: it.id, raw: it, name, path: it.path, tags: it.tags, note: it.note, kind: it.kind, emoji, order: idx,
        first: it.tags[0] || UNTAGGED,
        hay: { name: norm(name), tags: it.tags.map(norm), path: norm(it.path), all: norm([name, it.path, it.tags.join(' '), it.note].join('\n')) },
      };
    });
    const used = {};
    items.forEach((it) => it.tags.forEach((t) => { used[t] = true; }));
    const tagOrder = Object.keys(tagsMap).filter((t) => used[t]); // tags のキーの順 = 並ぶ順
    Object.keys(used).forEach((t) => { if (tagOrder.indexOf(t) < 0) tagOrder.push(t); });
    const first = state.circles[0] || state.title || 'Launcher';
    const circleOrder = [];
    const addC = (n) => { if (circleOrder.indexOf(n) < 0) circleOrder.push(n); };
    addC(first);
    state.circles.forEach(addC);
    const tags = {};
    tagOrder.forEach((t) => {
      const info = tagsMap[t] || {};
      const c = (info.circle || '').trim() || first; // 空欄は最初の魔法陣
      addC(c);
      tags[t] = { name: t, label: t, color: info.color || '#7fb0ff', circle: c, items: [], count: 0 };
    });
    const untagged = items.filter((it) => !it.tags.length);
    if (untagged.length) {
      tags[UNTAGGED] = { name: UNTAGGED, label: 'タグなし', color: '#8aa4c8', circle: first, items: [], count: 0, pseudo: true };
      tagOrder.push(UNTAGGED);
    }
    items.forEach((it) => {
      tags[it.first].items.push(it);
      if (it.tags.length) it.tags.forEach((t) => { tags[t].count++; }); else tags[UNTAGGED].count++;
    });
    let circles = circleOrder.map((name) => ({ name, tags: [], items: [] }));
    const byName = {};
    circles.forEach((c) => { byName[c.name] = c; });
    tagOrder.forEach((t) => { const c = byName[tags[t].circle]; c.tags.push(tags[t]); c.items = c.items.concat(tags[t].items); });
    circles = circles.filter((c) => c.tags.length); // 最後のタグが抜けた魔法陣は消える
    if (!circles.length) circles = [{ name: first, tags: [], items: [] }];
    // 魔法陣の色: 名前のハッシュでパレットから重ならないように選ぶ（名前順に割り当て → 並べ替えても変わらない）
    const auto = {};
    const taken = {};
    circles.map((c) => c.name).sort().forEach((n) => {
      let i = fnv(n) % CIRCLE_PALETTE.length;
      for (let k = 0; k < CIRCLE_PALETTE.length && taken[CIRCLE_PALETTE[i]]; k++) i = (i + 1) % CIRCLE_PALETTE.length;
      taken[CIRCLE_PALETTE[i]] = true;
      auto[n] = CIRCLE_PALETTE[i];
    });
    circles.forEach((c) => { c.autoColor = auto[c.name]; c.color = state.circleColors[c.name] || auto[c.name]; });
    const byId = {};
    items.forEach((it) => { byId[it.id] = it; });
    return { items, byId, tags, tagOrder, circles, first, total: items.length };
  }

  // ================================================================ 検索・絞り込み（2.4）
  function words() { return norm(S.query).split(/\s+/).filter(Boolean); }
  function filterActive() { return S.query.trim() !== '' || S.tags.length > 0; }
  function score(it, ws) {
    let total = 0;
    for (const w of ws) {
      if (it.hay.all.indexOf(w) < 0) return -1; // 空白区切りは AND
      let s = 10;
      if (it.hay.name.startsWith(w)) s = 100;
      else if (it.hay.name.indexOf(w) >= 0) s = 60;
      else if (it.hay.tags.indexOf(w) >= 0) s = 50;
      else if (it.hay.tags.some((t) => t.indexOf(w) >= 0)) s = 40;
      else if (it.hay.path.indexOf(w) >= 0) s = 20;
      total += s;
    }
    return total;
  }
  function hasTag(it, t) { return t === UNTAGGED ? !it.tags.length : it.tags.indexOf(t) >= 0; }

  function applyFilter(flyToTags) {
    const v = S.view;
    if (!v) return;
    S.tags = S.tags.filter((t) => v.tags[t]);
    const ws = words();
    const scored = [];
    S.match = {};
    S.matchCount = 0;
    S.tagMatch = {};
    v.items.forEach((it) => {
      const sc = ws.length ? score(it, ws) : 0;
      const ok = sc >= 0 && S.tags.every((t) => hasTag(it, t)); // 複数のタグは全部持つものだけ
      S.match[it.id] = ok;
      if (ok) {
        S.matchCount++;
        scored.push({ it, sc });
        if (it.tags.length) it.tags.forEach((t) => { S.tagMatch[t] = true; }); else S.tagMatch[UNTAGGED] = true;
      }
    });
    scored.sort((a, b) => b.sc - a.sc || a.it.name.localeCompare(b.it.name, 'ja'));
    S.results = scored.slice(0, MAX_RESULTS).map((x) => x.it);
    if (S.sel >= S.results.length) S.sel = 0;
    const active = filterActive();
    $('ml-count').textContent = active ? S.matchCount + ' / ' + v.total + ' 件' : '';
    document.querySelector('.ml-search').classList.toggle('active', active);
    renderLegend();
    renderDrop();
    renderLists();
    if (scene3d) scene3d.onFilter(flyToTags);
  }

  function toggleTag(t) {
    const i = S.tags.indexOf(t);
    if (i >= 0) S.tags.splice(i, 1); else S.tags.push(t);
    applyFilter(true);
  }
  function clearFilter() {
    S.query = '';
    $('ml-q').value = '';
    S.tags = [];
    closeDrop();
    applyFilter(false);
    if (scene3d) scene3d.home();
  }

  // ---------------------------------------------------------------- 検索のプルダウン
  function renderDrop() {
    const drop = $('ml-drop');
    if (!S.query.trim()) { drop.classList.remove('open'); drop.innerHTML = ''; return; }
    if (!S.results.length) {
      drop.innerHTML = '<div class="none">見つかりません</div>';
    } else {
      drop.innerHTML = S.results.map((it, i) => {
        const tg = it.tags.map((t) => '<span class="t" style="--c:' + esc((S.view.tags[t] || {}).color) + '">#' + esc(t) + '</span>').join('');
        return '<div class="r' + (i === S.sel ? ' sel' : '') + '" data-id="' + esc(it.id) + '" data-i="' + i + '" role="option">' +
          '<span class="e">' + esc(it.emoji) + '</span><span class="n">' + esc(it.name) + tg + '</span><span class="p">' + esc(it.path) + '</span></div>';
      }).join('');
    }
    if (document.activeElement === $('ml-q')) drop.classList.add('open');
  }
  function openDrop() { if (S.query.trim()) { $('ml-drop').classList.add('open'); } }
  function closeDrop() { $('ml-drop').classList.remove('open'); }
  function moveSel(d) {
    if (!S.results.length) return;
    S.sel = (S.sel + d + S.results.length) % S.results.length;
    renderDrop();
    const r = $('ml-drop').querySelector('.r.sel');
    if (r) r.scrollIntoView({ block: 'nearest' });
  }

  // ================================================================ 開く
  function openItem(it) {
    if (!it) return;
    api('/api/open', { id: it.id }).then((r) => {
      toast((r.dryRun ? '🧪 DRY RUN（開かずに確認）: ' + r.opened : it.emoji + ' 開きました: ' + it.name));
    }).catch((e) => {
      if (e.status === 404) toast('⚠️ 見つかりません: ' + it.name + '（' + e.message + '）', true);
      else if (e.status === 504) toast('⏳ 応答がありません: ' + it.name + '（ネットワークの場所が止まっているかも）', true);
      else toast('開けませんでした: ' + it.name + '（' + e.message + '）', true);
    });
  }

  // ================================================================ 状態の読み込み・変更チェック
  function setState(s) {
    S.state = s;
    S.view = buildView(s);
    $('ml-title').textContent = s.title + (s.dryRun ? '  · DRY RUN' : '');
    document.title = s.title + ' — MagicLauncher';
    const banner = $('ml-banner');
    if (s.error) {
      banner.textContent = '⚠️ 設定ファイルを読めません：' + s.error + '（' + s.configPath + '）。直すまで保存はしません（空の内容で上書きしないため）。';
      banner.hidden = false;
    } else {
      banner.hidden = true;
    }
    applyFilter(false);
    if (scene3d) scene3d.build(S.view);
    if (S.modal === 'settings') renderSettings(false);
    measureTop();
  }
  function reload() {
    return api('/api/state').then(setState).catch((e) => toast('読み込めませんでした: ' + e.message, true));
  }
  function checkVersion() {
    if (!S.state) return;
    api('/api/version').then((v) => {
      if (v.version !== S.state.version || v.configPath !== S.state.configPath) reload(); // 版数か場所が変わっていたら読み直す
    }).catch(() => { /* サーバが止まっている間は黙って待つ */ });
  }
  function mutate(path, body, okMsg) {
    return api(path, body).then((r) => { if (okMsg) toast(okMsg); return reload().then(() => r); }).catch((e) => {
      toast(e.message, true);
      if (e.status === 404 || e.status === 409) reload();
      throw e;
    });
  }

  // ================================================================ 上部（凡例）
  function renderLegend() {
    const lg = $('ml-legend');
    const v = S.view;
    if (!v) return;
    let html = '';
    v.circles.forEach((c) => {
      if (!c.tags.length) return;
      if (v.circles.length > 1) html += '<span class="grp" style="--c:' + esc(c.color) + '">' + esc(c.name) + '</span>';
      c.tags.forEach((t) => {
        html += '<button type="button" class="ml-chip' + (S.tags.indexOf(t.name) >= 0 ? ' on' : '') + '" style="--c:' + esc(t.color) +
          '" data-tag="' + esc(t.name) + '"><i></i>' + esc(t.label) + ' <small>' + t.count + '</small></button>';
      });
    });
    lg.innerHTML = html;
    measureTop();
  }
  function measureTop() {
    const h = $('ml-top').offsetHeight;
    if (h && h !== S.topH) {
      S.topH = h;
      document.documentElement.style.setProperty('--top-h', h + 'px');
    }
  }

  // ================================================================ 一覧（Light 版・WebGL なし・☰）
  function listHtml() {
    const v = S.view;
    if (!v) return '';
    if (!v.total) return '<div class="ml-empty">まだ登録がありません。「＋ 追加」から登録してください。</div>';
    const active = filterActive();
    let html = '';
    v.circles.forEach((c) => {
      let inner = '';
      c.tags.forEach((t) => {
        const its = t.items.filter((it) => !active || S.match[it.id]);
        if (active && !its.length) return;
        inner += '<div class="ml-card" style="--c:' + esc(t.color) + '"><h3 data-tag="' + esc(t.name) + '">' + esc(t.label) +
          ' <small>' + t.items.length + '</small></h3><ul>' + its.map(liHtml).join('') + '</ul></div>';
      });
      if (!inner) return;
      if (v.circles.length > 1) html += '<h2 class="ml-circle-head" style="--c:' + esc(c.color) + '">' + esc(c.name) + '</h2>';
      html += inner;
    });
    return html || '<div class="ml-empty">見つかりません</div>';
  }
  function liHtml(it) {
    const more = it.tags.slice(1).map((t) => '#' + t).join(' ');
    return '<li class="ml-li' + (it.kind === 'missing' ? ' missing' : '') + '" data-id="' + esc(it.id) + '" title="' + esc(it.path + (it.note ? '\n' + it.note : '')) + '">' +
      '<span class="e">' + esc(it.emoji) + '</span><span class="n">' + esc(it.name) + (more ? '<span class="tg">' + esc(more) + '</span>' : '') +
      '</span><span class="p">' + esc(it.path) + '</span></li>';
  }
  function renderLists() {
    const lv = $('ml-listview');
    if (S.mode === 'list') lv.innerHTML = '<div class="ml-grid">' + listHtml() + '</div>';
    const panel = $('ml-listpanel');
    if (panel.classList.contains('open')) panel.innerHTML = '<div class="ml-panel-list">' + listHtml() + '</div>';
  }
  function bindListEvents(container) {
    container.addEventListener('click', (e) => {
      const li = e.target.closest('.ml-li');
      if (li) { openItem(S.view.byId[li.dataset.id]); return; }
      const h = e.target.closest('[data-tag]');
      if (h) toggleTag(h.dataset.tag);
    });
    container.addEventListener('contextmenu', (e) => {
      const li = e.target.closest('.ml-li');
      const h = e.target.closest('[data-tag]');
      if (!li && !h) return;
      e.preventDefault();
      if (li) editItem(S.view.byId[li.dataset.id]);
      else newItem(h.dataset.tag === UNTAGGED ? [] : [h.dataset.tag]);
    });
  }

  // ================================================================ 登録・編集ダイアログ（2.5）
  const D = { item: null, tags: [], dragIdx: -1 };
  function newItem(tags) { openItemDialog(null, (tags || S.tags).filter((t) => t !== UNTAGGED)); }
  function editItem(it) { if (it) openItemDialog(it, it.tags.slice()); }

  function openItemDialog(it, tags) {
    D.item = it;
    D.tags = tags.slice();
    $('ml-item-title').textContent = it ? '編集' : '登録';
    $('f-path').value = it ? it.path : '';
    $('f-name').value = it ? (it.raw.name || '') : '';
    $('f-emoji').value = it ? (it.raw.emoji || '') : '';
    $('f-note').value = it ? (it.note || '') : '';
    $('f-tag').value = '';
    $('f-delete').hidden = !it;
    onPathChange();
    renderTagBox();
    showModal('item');
    setTimeout(() => (it ? $('f-name') : $('f-path')).focus(), 30);
  }
  function onPathChange() {
    const p = stripQuotes($('f-path').value);
    $('f-name').placeholder = derivedName(p) || '空ならパスから付けます';
    const warn = $('f-warn');
    if (EXEC_RE.test(p)) {
      warn.textContent = '⚠️ ' + (p.match(EXEC_RE)[0]) + ' はクリックで実行されます。設定ファイルを共有フォルダに置くなら、書き込める人に注意してください。';
      warn.hidden = false;
    } else {
      warn.hidden = true;
    }
  }
  function tagColor(t) {
    const v = S.state && S.state.tags[t];
    return v ? v.color : '#7fb0ff';
  }
  function renderTagBox() {
    const box = $('f-tagbox');
    box.querySelectorAll('.ml-tagchip').forEach((c) => c.remove());
    const input = $('f-tag');
    D.tags.forEach((t, i) => {
      const c = setC(el('span', 'ml-tagchip', esc(t) + '<button type="button" aria-label="外す">×</button>'), tagColor(t));
      c.draggable = true;
      c.dataset.i = i;
      c.title = 'クリックで先頭へ・ドラッグで並べ替え';
      box.insertBefore(c, input);
    });
    const known = Object.keys((S.state && S.state.tags) || {});
    const q = norm(input.value.trim());
    const cands = known.filter((t) => D.tags.indexOf(t) < 0 && (!q || norm(t).indexOf(q) >= 0));
    $('f-cands').innerHTML = cands.map((t) => '<button type="button" style="--c:' + esc(tagColor(t)) + '" data-t="' + esc(t) + '">' + esc(t) + '</button>').join('');
  }
  function addTag(name) {
    name = String(name || '').trim();
    if (!name) return;
    const i = D.tags.indexOf(name);
    if (i >= 0) D.tags.splice(i, 1);
    D.tags.unshift(name); // 足したタグは先頭に入る
    renderTagBox();
  }
  function commitTagInput() {
    const input = $('f-tag');
    const parts = input.value.split(/[,、，]/);
    input.value = '';
    parts.reverse().forEach(addTag);
    renderTagBox();
  }
  function saveItem() {
    commitTagInput();
    const p = stripQuotes($('f-path').value);
    if (!p) { toast('パスを入れてください', true); $('f-path').focus(); return; }
    const item = { name: $('f-name').value.trim(), path: p, tags: D.tags.slice(), emoji: $('f-emoji').value.trim(), note: $('f-note').value };
    if (D.item) item.id = D.item.id;
    $('f-save').disabled = true;
    mutate('/api/item', { item }, D.item ? '保存しました' : '登録しました: ' + (item.name || derivedName(p)))
      .then(() => hideModal()).catch(() => {}).then(() => { $('f-save').disabled = false; });
  }
  function deleteItem() {
    if (!D.item) return;
    if (!confirm('「' + D.item.name + '」を削除しますか？')) return;
    mutate('/api/item/delete', { id: D.item.id }, '削除しました: ' + D.item.name).then(() => hideModal()).catch(() => {});
  }
  function bindItemDialog() {
    $('ml-item-form').addEventListener('submit', (e) => { e.preventDefault(); saveItem(); });
    $('f-cancel').addEventListener('click', hideModal);
    $('f-delete').addEventListener('click', deleteItem);
    $('f-path').addEventListener('input', onPathChange);
    $('f-path').addEventListener('blur', () => { $('f-path').value = stripQuotes($('f-path').value); onPathChange(); });
    $('f-path').addEventListener('paste', () => setTimeout(() => { $('f-path').value = stripQuotes($('f-path').value); onPathChange(); }, 0));
    $('f-name').addEventListener('keydown', (e) => {
      // 名前が空欄のときにスペースを押すと、透かし文字（パスから作った名前）が入る
      if (e.key === ' ' && !e.isComposing && $('f-name').value === '' && $('f-name').placeholder && derivedName($('f-path').value)) {
        e.preventDefault();
        $('f-name').value = derivedName($('f-path').value);
      }
    });
    const tagInput = $('f-tag');
    tagInput.addEventListener('keydown', (e) => {
      if (e.isComposing) return;
      if (e.key === 'Enter' || e.key === ',') {
        if (tagInput.value.trim()) { e.preventDefault(); commitTagInput(); } else if (e.key === ',') e.preventDefault();
      } else if (e.key === 'Backspace' && !tagInput.value && D.tags.length) {
        D.tags.pop();
        renderTagBox();
      }
    });
    tagInput.addEventListener('input', () => {
      if (/[,，、]/.test(tagInput.value)) commitTagInput(); else renderTagBox();
    });
    const box = $('f-tagbox');
    box.addEventListener('click', (e) => {
      const chip = e.target.closest('.ml-tagchip');
      if (!chip) { tagInput.focus(); return; }
      const i = Number(chip.dataset.i);
      if (e.target.tagName === 'BUTTON') D.tags.splice(i, 1);
      else { const t = D.tags.splice(i, 1)[0]; D.tags.unshift(t); } // クリックで先頭へ
      renderTagBox();
    });
    box.addEventListener('dragstart', (e) => {
      const chip = e.target.closest('.ml-tagchip');
      if (!chip) return;
      D.dragIdx = Number(chip.dataset.i);
      chip.classList.add('dragging');
      e.dataTransfer.effectAllowed = 'move';
      e.dataTransfer.setData('text/plain', D.tags[D.dragIdx]);
    });
    box.addEventListener('dragover', (e) => { if (D.dragIdx >= 0) e.preventDefault(); });
    box.addEventListener('drop', (e) => {
      if (D.dragIdx < 0) return;
      e.preventDefault();
      const chip = e.target.closest('.ml-tagchip');
      let to = chip ? Number(chip.dataset.i) : D.tags.length - 1;
      const t = D.tags.splice(D.dragIdx, 1)[0];
      if (to > D.tags.length) to = D.tags.length;
      D.tags.splice(to, 0, t);
      D.dragIdx = -1;
      renderTagBox();
    });
    box.addEventListener('dragend', () => { D.dragIdx = -1; renderTagBox(); });
    $('f-cands').addEventListener('click', (e) => {
      const b = e.target.closest('button[data-t]');
      if (b) { addTag(b.dataset.t); tagInput.value = ''; renderTagBox(); }
    });
  }

  // ================================================================ 設定（2.6）
  function renderSettings(force) {
    const box = $('ml-settings');
    if (!force && box.contains(document.activeElement) && document.activeElement.tagName === 'INPUT') return;
    const s = S.state;
    const v = S.view;
    if (!s || !v) return;
    let status;
    if (s.error) status = '⚠️ 読めません：' + s.error;
    else if (!s.exists) status = 'まだファイルがありません（最初に保存したときに作られます）';
    else status = v.total + ' 件を読み込みました';
    const cols = [[0, '横一列（折り返さない）'], [1, '1 つ'], [2, '2 つ'], [3, '3 つ'], [4, '4 つ']];
    const idles = [['orbit', '反時計回りに回る'], ['float', 'ふわふわ漂う（大きさも変わる）'], ['off', '動かさない']];
    let html = '<h2>⚙ 設定</h2>';
    html += '<section><h3>設定ファイルの場所 <small>登録したパスの一覧を保存する JSON。OneDrive などに置くと複数の PC で共有できます（%USERPROFILE% なども使えます）</small></h3>' +
      '<input type="text" id="s-path" spellcheck="false" value="' + esc(s.configPathRaw) + '" placeholder="' + esc(s.defaultPath) + '">' +
      '<p>いま使っている場所: ' + esc(s.configPath) + (s.isDefaultPath ? '（既定＝ランチャー本体の隣）' : '') + ' ／ ' + esc(status) + '</p>' +
      '<div class="between"><button type="button" class="ml-btn" id="s-path-reset"' + (s.isDefaultPath ? ' disabled' : '') + '>既定に戻す</button>' +
      '<button type="button" class="ml-btn primary" id="s-path-use">この場所を使う</button></div></section>';
    html += '<section><h3>魔法陣を横に並べる数</h3><div class="btns">' +
      cols.map((c) => '<button type="button" class="ml-btn' + (s.columns === c[0] ? ' on' : '') + '" data-cols="' + c[0] + '">' + c[1] + '</button>').join('') +
      (s.columns > 4 ? '<button type="button" class="ml-btn on" disabled>' + s.columns + ' つ（ファイルで指定）</button>' : '') +
      '</div><p>この数を超えた魔法陣は、下の段へ折り返します（2 つなら 2 列、3 つなら 3 列）。</p></section>';
    html += '<section><h3>待機中の動き</h3><div class="btns">' +
      idles.map((c) => '<button type="button" class="ml-btn' + (s.idle === c[0] ? ' on' : '') + '" data-idle="' + c[0] + '">' + c[1] + '</button>').join('') +
      '</div><p>マウスがブラウザの外に出て 1 秒たつと、魔法陣がゆっくり動き出します。マウスを戻すと、魔法陣が全部窓に入る全体表示に戻ります（マウスが上にある間・キー操作中・絞り込み中・ダイアログ中は動きません）。</p></section>';
    html += '<section><h3>魔法陣の色</h3>' + v.circles.map((c) =>
      '<div class="ml-swatch-row"><input type="color" data-circle="' + esc(c.name) + '" value="' + esc(c.color) + '"><span class="nm">' + esc(c.name) + '</span>' +
      (s.circleColors[c.name] ? '<button type="button" class="ml-btn" data-circle-auto="' + esc(c.name) + '">自動の色に戻す</button>' : '') + '</div>').join('') + '</section>';
    const tagNames = v.tagOrder.filter((t) => t !== UNTAGGED);
    html += '<section><h3>タグの色と、置く魔法陣</h3><p>右の欄に同じ名前を入れたタグが、1 つの魔法陣にまとまって、左から横に並びます（名前は自由。新しい名前を入れると魔法陣が増えます）。空欄のタグとタグなしは最初の魔法陣（' +
      esc(v.first) + '）に入ります。魔法陣の左右の並びは、画面で中心をドラッグして入れ替えられます。</p>' +
      (tagNames.length ? tagNames.map((t) => '<div class="ml-swatch-row"><input type="color" data-tag="' + esc(t) + '" value="' + esc(tagColor(t)) + '"><span class="nm">' + esc(t) +
        '</span><input type="text" data-tag-circle="' + esc(t) + '" value="' + esc((s.tags[t] || {}).circle || '') + '" placeholder="' + esc(v.first) + '"></div>').join('') : '<p>まだタグがありません。</p>') +
      '</section>';
    html += '<div class="close-row"><button type="button" class="ml-btn primary" id="s-close">閉じる</button></div>';
    box.innerHTML = html;
  }
  function bindSettings() {
    const box = $('ml-settings');
    box.addEventListener('click', (e) => {
      const b = e.target.closest('button');
      if (!b) return;
      if (b.id === 's-close') hideModal();
      else if (b.id === 's-path-reset') mutate('/api/config', { path: '' }, '設定ファイルの場所を既定に戻しました').catch(() => {});
      else if (b.id === 's-path-use') mutate('/api/config', { path: $('s-path').value }, '設定ファイルの場所を変えました').catch(() => {});
      else if (b.dataset.cols != null) mutate('/api/layout', { columns: Number(b.dataset.cols) }).catch(() => {});
      else if (b.dataset.idle) mutate('/api/layout', { idle: b.dataset.idle }).catch(() => {});
      else if (b.dataset.circleAuto) mutate('/api/circle', { name: b.dataset.circleAuto, color: '' }).catch(() => {});
    });
    box.addEventListener('change', (e) => {
      const t = e.target;
      if (t.dataset.circle) mutate('/api/circle', { name: t.dataset.circle, color: t.value }).catch(() => {});
      else if (t.dataset.tag) mutate('/api/tag', { name: t.dataset.tag, color: t.value }).catch(() => {});
      else if (t.dataset.tagCircle != null) mutate('/api/tag', { name: t.dataset.tagCircle, circle: t.value.trim() }).catch(() => {});
    });
    box.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' && e.target.dataset && e.target.dataset.tagCircle != null) e.target.blur();
      if (e.key === 'Enter' && e.target.id === 's-path') $('s-path-use').click();
    });
  }

  // ---------------------------------------------------------------- モーダル共通
  function showModal(which) {
    S.modal = which;
    $('ml-item-dlg').hidden = which !== 'item';
    $('ml-rename-dlg').hidden = which !== 'rename';
    $('ml-settings-dlg').hidden = which !== 'settings';
    closeDrop();
  }
  function hideModal() {
    S.modal = null;
    $('ml-item-dlg').hidden = true;
    $('ml-rename-dlg').hidden = true;
    $('ml-settings-dlg').hidden = true;
  }

  // ================================================================ 名前の変更（タグ・魔法陣を右クリック）
  const RN = { kind: null, from: '' }; // 名前の変更ダイアログの対象
  function openRename(kind, from) {
    RN.kind = kind;
    RN.from = from;
    const isTag = kind === 'tag';
    $('ml-rename-title').textContent = isTag ? 'タグの名前を変更' : '魔法陣の名前を変更';
    $('ml-rename-label').textContent = isTag ? '新しいタグの名前（このタグが付いた項目もまとめて変わります）' : '新しい魔法陣の名前';
    $('r-add').hidden = !isTag;
    $('r-name').value = from;
    showModal('rename');
    setTimeout(() => { $('r-name').focus(); $('r-name').select(); }, 30);
  }
  function saveRename() {
    const to = $('r-name').value.trim();
    if (!to) { toast('名前を入れてください', true); $('r-name').focus(); return; }
    if (to === RN.from) { hideModal(); return; }
    const isTag = RN.kind === 'tag';
    $('r-save').disabled = true;
    mutate(isTag ? '/api/tag/rename' : '/api/circle/rename', { from: RN.from, to }, '名前を「' + to + '」に変えました')
      .then(() => {
        if (isTag) S.tags = S.tags.map((t) => (t === RN.from ? to : t)); // 絞り込み中のタグも付け替える
        hideModal();
      })
      .catch(() => {}).then(() => { $('r-save').disabled = false; });
  }
  function bindRenameDialog() {
    $('ml-rename-form').addEventListener('submit', (e) => { e.preventDefault(); saveRename(); });
    $('r-cancel').addEventListener('click', hideModal);
    $('r-add').addEventListener('click', () => { const t = RN.from; hideModal(); newItem([t]); }); // 以前の「タグを右クリックで追加」の代わり
  }

  // ================================================================ 3D（魔法陣）
  function loadScripts(list) {
    return list.reduce((p, src) => p.then(() => new Promise((resolve, reject) => {
      const s = document.createElement('script');
      s.src = src;
      s.onload = resolve;
      s.onerror = () => reject(new Error('読み込めません: ' + src));
      document.head.appendChild(s);
    })), Promise.resolve());
  }

  function create3D(root) {
    const T = window.THREE;
    const M = window.Magic;
    const st = M.stage(root, { fov: 45, clear: '#030814', bloomStrength: 1.15, bloomRadius: 0.55, bloomThreshold: 0.12 });
    if (!st) return null;
    const scene = st.scene;
    const camera = st.camera;
    const canvas = st.renderer.domElement;
    camera.far = 2000;
    camera.updateProjectionMatrix();
    scene.add(camera);
    const TAN = Math.tan((camera.fov * Math.PI / 180) / 2);

    const controls = new T.OrbitControls(camera, canvas);
    controls.enableDamping = true;
    controls.dampingFactor = 0.08;
    controls.screenSpacePanning = true; // 右ドラッグ = 移動
    controls.rotateSpeed = 0.5;
    controls.zoomSpeed = 1.2;
    controls.minAzimuthAngle = -0.7; controls.maxAzimuthAngle = 0.7;
    controls.minPolarAngle = Math.PI / 2 - 0.7; controls.maxPolarAngle = Math.PI / 2 + 0.7;
    controls.minDistance = 4; controls.maxDistance = 900;

    // ---------------------------------------------------------------- 背景（星と流れる煙）
    const smoke1 = M.smoke(1, 1, '#0d2a66');
    const smoke2 = M.smoke(1, 1, '#06304a');
    smoke1.position.z = -300; smoke1.renderOrder = -2;
    smoke2.position.set(0.2, -0.15, -310); smoke2.rotation.z = 1.3; smoke2.renderOrder = -3;
    camera.add(smoke1, smoke2);
    let starField = null;

    const board = new T.Group();
    scene.add(board);
    const labelsRoot = el('div', 'ml-labels');
    root.appendChild(labelsRoot);
    const frameTex = M.glyphRingTexture('ml-frame', { count: 72, inner: 0.455 });
    const hubTex = M.glyphRingTexture('ml-hub', { count: 30, inner: 0.4 });
    const WHITE = new T.Color('#ffffff');

    let circles = [], nodes = [], tagNodes = [], hitList = [], labels = [], secLines = [];
    let tagByName = {};
    let layoutBox = { minX: -14, maxX: 14, minY: -14, maxY: 14 };
    let built = false, appearStart = 0, pendingView = null;
    let hover = null, hoverTag = null;
    let fly = null, atHome = true;
    let time = 0;
    let measureAll = true;
    let lastFs = -1, lastFar = null, lastSmall = null;

    function v3(x, y) { return new T.Vector3(x, y, 0); }
    function order(obj, n) { obj.traverse((o) => { o.renderOrder = n; }); }
    function occluder(r) {
      const m = new T.Mesh(new T.CircleGeometry(r, 40), new T.MeshBasicMaterial({ color: '#030814', transparent: true, opacity: 1, depthWrite: false }));
      m.renderOrder = 2;
      return m;
    }
    function hitMesh(r, ref) {
      const m = new T.Mesh(new T.CircleGeometry(r, 24), new T.MeshBasicMaterial({ visible: false }));
      m.userData.ref = ref;
      hitList.push(m);
      return m;
    }
    function anchor(g, x, y) { const o = new T.Object3D(); o.position.set(x, y, 0.1); g.add(o); return o; }
    function addLabel(e, anc, opts) {
      e._mlRef = opts.ref;
      labelsRoot.appendChild(e);
      const l = { el: e, anchor: anc, kind: opts.kind, fixed: !!opts.fixed, center: !!opts.center, R: opts.R || 0, circle: opts.circle,
        w: 0, h: 0, x: 0, y: 0, shown: true, op: -1, fsz: -1 };
      labels.push(l);
      return l;
    }
    function addTrace(C, pts, col, op, w, meta) {
      const m = M.ribbon(pts, w, col, op);
      m.renderOrder = 1;
      m.position.z = -0.01;
      C.group.add(m);
      const tr = { mesh: m, tag: meta.tag, item: meta.item || null, k: 0, pts };
      C.traces.push(tr);
      return tr;
    }
    // 点線の帯（2 つ目以降のタグの線）
    function dashRibbon(pts, width, col) {
      const pos = [], idx = [];
      let vi = 0, on = true, left = 0.34;
      const N = new T.Vector3(0, 0, 1);
      for (let i = 0; i < pts.length - 1; i++) {
        const a = pts[i], b = pts[i + 1], L = a.distanceTo(b);
        if (L < 1e-6) continue;
        const dir = b.clone().sub(a).normalize();
        const side = new T.Vector3().crossVectors(N, dir).normalize().multiplyScalar(width / 2);
        let d = 0;
        while (d < L - 1e-6) {
          const step = Math.min(left, L - d);
          if (on) {
            const p0 = a.clone().addScaledVector(dir, d), p1 = a.clone().addScaledVector(dir, d + step);
            [p0.clone().add(side), p0.clone().sub(side), p1.clone().add(side), p1.clone().sub(side)].forEach((q) => pos.push(q.x, q.y, q.z));
            idx.push(vi, vi + 1, vi + 2, vi + 1, vi + 3, vi + 2);
            vi += 4;
          }
          d += step;
          left -= step;
          if (left <= 1e-6) { on = !on; left = on ? 0.34 : 0.24; }
        }
      }
      const geo = new T.BufferGeometry();
      geo.setAttribute('position', new T.Float32BufferAttribute(pos, 3));
      geo.setIndex(idx);
      const m = new T.Mesh(geo, new T.MeshBasicMaterial({ color: col, transparent: true, opacity: 0, depthWrite: false, side: T.DoubleSide, blending: T.AdditiveBlending }));
      m.renderOrder = 1;
      m.visible = false;
      return m;
    }

    // ---------------------------------------------------------------- 組み立て
    function clearBoard() {
      circles.forEach((C) => C.flow.clear());
      board.traverse((o) => {
        if (o.geometry && !o.isSprite) o.geometry.dispose();
        if (o.material) o.material.dispose();
      });
      while (board.children.length) board.remove(board.children[0]);
      labels.forEach((l) => l.el.remove());
      circles = []; nodes = []; tagNodes = []; hitList = []; labels = []; secLines = []; tagByName = {};
      hover = null; hoverTag = null;
    }

    // 点 q から線分 a-b までの距離
    function segDist(q, a, b) {
      const dx = b[0] - a[0], dy = b[1] - a[1], len2 = dx * dx + dy * dy;
      const t = len2 ? Math.min(1, Math.max(0, ((q[0] - a[0]) * dx + (q[1] - a[1]) * dy) / len2)) : 0;
      return Math.hypot(a[0] + t * dx - q[0], a[1] + t * dy - q[1]);
    }

    // 分岐点 J → 円 P の枝の道筋。八方向の直線 2 本(曲がり角は 1 つ)。
    // ふだんは M.octi の折れ線。その曲がり角が中心の魔法陣の上に来ると角が隠れて中心から生えて見えるので、
    // そのときだけ八方向の直線 2 本の組み合わせから選び直す。分岐点から出る向きはそのままで中心を通り過ぎ、
    // その先で曲がる道を優先し、無ければほかの向きも試す。
    function branchRoute(J, P, straightFirst, avoid) {
      const pts = M.octi(J, P, straightFirst);
      if (Math.hypot(pts[1][0], pts[1][1]) >= HUB_CLEAR) return pts;
      const lead = pts[1][0] !== J[0] || pts[1][1] !== J[1] ? pts[1] : P; // 元の道が分岐点から出る向き
      const u0 = [Math.sign(lead[0] - J[0]), Math.sign(lead[1] - J[1])];
      return bentRoute(J, P, [u0], avoid) || bentRoute(J, P, OCTI_DIRS, avoid) || pts;
    }

    // J から dirs のどれかの向きに出て 1 回だけ曲がり P へ着く道のうち、条件に合う最短のもの(無ければ null)。
    // 条件: 曲がり角が中心の外・曲がった後の直線は中心を通らない・鋭く折り返さない・ほかの円(avoid)の上を通らない
    function bentRoute(J, P, dirs, avoid) {
      const rx = P[0] - J[0], ry = P[1] - J[1];
      let best = null, bestLen = Infinity;
      for (const u of dirs) {
        for (const v of OCTI_DIRS) {
          const det = v[0] * u[1] - u[0] * v[1]; // J + s*u = P + t*v を s について解く
          if (Math.abs(det) < 1e-9) continue;
          const s = (v[0] * ry - rx * v[1]) / det;
          const B = [J[0] + s * u[0], J[1] + s * u[1]];
          if (Math.hypot(B[0], B[1]) < HUB_CLEAR || segDist([0, 0], B, P) < HUB_CLEAR) continue;
          if ((J[0] - B[0]) * (P[0] - B[0]) + (J[1] - B[1]) * (P[1] - B[1]) > 1e-9) continue;
          if ((avoid || []).some((q) => segDist(q, J, B) < AVOID_R || segDist(q, B, P) < AVOID_R)) continue;
          const len = Math.hypot(B[0] - J[0], B[1] - J[1]) + Math.hypot(P[0] - B[0], P[1] - B[1]);
          if (len < bestLen) {
            bestLen = len;
            best = [J, B, P];
          }
        }
      }
      return best;
    }

    function buildCircle(cv, many) {
      const g = new T.Group();
      board.add(g);
      const tags = cv.tags;
      let total = 0;
      tags.forEach((t) => { t._w = Math.max(t.items.length, 1); total += t._w + GAP; });
      if (!total) total = 1;
      const unit = (Math.PI * 2) / total;
      const juncR = Math.max(5.2, 0.75 * Math.sqrt(total), tags.length * 1.25 / (Math.PI * 2));
      const ring0 = juncR + 2.6;
      let rings = Math.max(1, Math.ceil(2.75 * total / (Math.PI * 2 * ring0)));
      if (tags.some((t) => t.items.length >= 2)) rings = Math.max(2, rings);
      const frameR = Math.max(13.4, ring0 + (rings - 1) * RING_STEP + 2.9);
      const col = new T.Color(cv.color);
      const centerCol = col.clone().lerp(WHITE, 0.3 + M.rng(cv.name + '#center')() * 0.3); // 中心は外枠色を白へ 30〜60% 寄せる

      const C = { view: cv, name: cv.name, group: g, frameR, ext: frameR + 0.5, color: cv.color, nodes: [], tagNodes: [], traces: [],
        base: new T.Vector2(), flow: new M.Flow(g), seed: M.rng(cv.name + '#idle')(), fl: null, sc: 1, ip: new T.Vector2(), isc: 1 };

      // 外枠: 記号の輪のテクスチャ＋二重円
      const frame = new T.Group();
      frame.position.z = -0.4;
      g.add(frame);
      C.frameGlyph = new T.Mesh(new T.PlaneGeometry(frameR * 2, frameR * 2), new T.MeshBasicMaterial({
        map: frameTex, color: col, transparent: true, opacity: 0.34, depthWrite: false, blending: T.AdditiveBlending,
      }));
      frame.add(C.frameGlyph);
      frame.add(M.ring(frameR + 0.45, 0.05, col, 0.55));
      C.frameRing2 = M.ring(frameR * 0.9, 0.03, col, 0.25);
      frame.add(C.frameRing2);

      // 中心: 魔法陣＋記号の輪＋回る結晶
      C.hub = M.sigil(cv.name + '#hub', { radius: HUB_R, color: centerCol, detail: true });
      C.hub.position.z = 0.02;
      order(C.hub, 3);
      g.add(C.hub);
      g.add(occluder(HUB_R * 1.02));
      C.hubGlyph = new T.Mesh(new T.PlaneGeometry(HUB_R * 3.1, HUB_R * 3.1), new T.MeshBasicMaterial({
        map: hubTex, color: '#bcdcff', transparent: true, opacity: 0.5, depthWrite: false, blending: T.AdditiveBlending,
      }));
      C.hubGlyph.position.z = 0.01;
      g.add(C.hubGlyph);
      C.core = M.crystal('#8ff0ff', HUB_R * 0.32);
      C.core.position.z = 0.6;
      g.add(C.core);
      C.hubRef = { type: 'hub', circle: C };
      g.add(hitMesh(HUB_R * 1.05, C.hubRef));

      let used = 0;
      const w0 = tags.length ? tags[0]._w : 0;
      // 同じタグの円は扇形の中に並べ、輪を内→外へジグザグに使う(枝の回り道がほかの円を避けられるよう先に全部の位置を決める)
      const itemPos = new Map();
      tags.forEach((tv) => {
        const start = Math.PI / 2 - (used - w0 / 2) * unit;
        used += tv._w + GAP;
        tv.items.forEach((it, i) => {
          const a = start - (i + 0.5) * unit;
          const dist = ring0 + (i % rings) * RING_STEP;
          itemPos.set(it, { dist, P: [Math.cos(a) * dist, Math.sin(a) * dist] });
        });
      });
      used = 0;
      tags.forEach((tv, ti) => {
        // 最初のタグの真ん中を真上に置き、時計回りに並べる
        const mid = Math.PI / 2 - (used + tv._w / 2 - w0 / 2) * unit;
        const crowded = (tv._w + GAP) * unit * juncR < 1.7; // 混んでいるときは 1 つおきに内側へ
        used += tv._w + GAP;
        const jr = juncR - (crowded && ti % 2 === 1 ? 1.0 : 0);
        const J = [Math.cos(mid) * jr, Math.sin(mid) * jr];
        const tcol = new T.Color(tv.color);
        const trunkPts = [v3(Math.cos(mid) * HUB_R, Math.sin(mid) * HUB_R), v3(J[0], J[1])];
        const trunk = addTrace(C, trunkPts, tcol, 0.55, 0.075, { tag: tv.name }); // 幹（中心→分岐点）
        const junction = new T.Group();
        junction.add(M.ring(0.36, 0.06, tcol, 0.95), M.ring(0.2, 0.04, tcol, 0.7), M.disc(0.09, '#ffffff', 0.9));
        junction.position.set(J[0], J[1], 0.02);
        order(junction, 3);
        g.add(junction);
        const jOcc = occluder(0.4);
        jOcc.position.set(J[0], J[1], 0.01);
        g.add(jOcc);
        const tn = { type: 'tag', tag: tv, circle: C, pos: J, junction, trunk, dim: 1 };
        const jHit = hitMesh(0.6, tn);
        jHit.position.set(J[0], J[1], 0.05);
        g.add(jHit);
        const te = setC(el('div', 'ml-tag'), tv.color);
        te.textContent = tv.label;
        tn.label = addLabel(te, anchor(g, J[0], J[1] - 0.5), { ref: tn, kind: 'tag', circle: C });
        C.tagNodes.push(tn);
        tagNodes.push(tn);
        tagByName[tv.name] = tn;

        tv.items.forEach((it, i) => {
          const { dist, P } = itemPos.get(it);
          const R = 0.85 + M.rng(it.id + '-size')() * 0.3;
          const s = M.sigil(it.id, { radius: R, color: tcol, lite: many });
          s.position.set(P[0], P[1], 0.02);
          order(s, 3);
          const occ = occluder(R * 1.02);
          occ.position.set(P[0], P[1], 0.01);
          g.add(occ, s);
          const halo = M.glow(tcol, R * 4.2, 0);
          halo.position.set(P[0], P[1], -0.05);
          g.add(halo);
          const avoid = [];
          itemPos.forEach((o, other) => { if (other !== it) avoid.push(o.P); });
          const route = branchRoute(J, P, i % 2 === 1, avoid).map((p) => v3(p[0], p[1])); // 枝: 八方向の折れ線（交互に曲がる順を変える）
          const branch = addTrace(C, route, tcol, 0.5, 0.06, { tag: tv.name, item: it.id });
          const path = new M.Path([trunkPts[0]].concat(route));
          const stream = C.flow.add(path, tcol, { count: many ? 1 : 2, speed: 2.4 + M.rng(it.id + '-sp')(), size: 0.42 });
          const hot = C.flow.add(path, '#ffffff', { count: 3, speed: 5.5, size: 0.5, on: false });
          const node = { type: 'item', it, tag: tv, circle: C, pos: P, R, sigil: s, halo, branch, stream, hot,
            hover: 0, dim: 1, appear: 0, delay: 0.25 + dist * 0.045 + i * 0.03 };
          const hit = hitMesh(R * 1.15, node);
          hit.position.set(P[0], P[1], 0.05);
          g.add(hit);
          const missing = it.kind === 'missing';
          const em = setC(el('div', 'ml-emoji' + (missing ? ' missing' : '')), tv.color);
          em.textContent = it.emoji;
          em.title = it.name + '\n' + it.path;
          node.emojiLabel = addLabel(em, anchor(g, P[0], P[1]), { ref: node, kind: 'emoji', center: true, R, circle: C });
          const nm = setC(el('div', 'ml-name' + (missing ? ' missing' : ''), '<span>' + esc(it.name) + '</span>'), tv.color);
          nm.title = it.path + (it.note ? '\n' + it.note : '') + (missing ? '\n（この PC には見つかりません）' : '');
          node.nameLabel = addLabel(nm, anchor(g, P[0], P[1] - R - 0.25), { ref: node, kind: 'name', circle: C });
          C.nodes.push(node);
          nodes.push(node);
        });
      });

      const hl = setC(el('div', 'ml-hub'), cv.color);
      C.hubLabel = addLabel(hl, anchor(g, 0, -HUB_R - 0.35), { ref: C.hubRef, kind: 'hub', fixed: true, circle: C });
      return C;
    }

    // 窓が縦長か(上部のバーを除いた、魔法陣を描ける範囲で比べる)
    function isPortrait() { return st.h - S.topH > st.w; }
    let layoutPortrait = false; // いまの並びを決めたときの向き

    // 魔法陣を横に columns 個ずつ並べ、超えたら下の段へ（0 = 横一列）。
    // 縦長の窓では行と列を入れ替える（横一列なら縦一列）
    function layoutGrid() {
      const n = circles.length;
      const colsSetting = S.state.columns || 0;
      let cols = colsSetting === 0 ? n : Math.min(colsSetting, n);
      layoutPortrait = isPortrait();
      if (layoutPortrait) cols = Math.max(1, Math.ceil(n / Math.max(1, cols)));
      const colW = [], rowH = [];
      circles.forEach((C, i) => {
        const r = Math.floor(i / cols), k = i % cols;
        colW[k] = Math.max(colW[k] || 0, C.ext);
        rowH[r] = Math.max(rowH[r] || 0, C.ext);
      });
      const xs = [], ys = [];
      let x = 0;
      colW.forEach((w, k) => { xs[k] = x + w; x += 2 * w + CIRCLE_GAP; });
      const W = x - CIRCLE_GAP;
      let y = 0;
      rowH.forEach((h, r) => { ys[r] = y + h; y += 2 * h + CIRCLE_GAP; });
      const H = y - CIRCLE_GAP;
      circles.forEach((C, i) => {
        const r = Math.floor(i / cols), k = i % cols;
        C.base.set(xs[k] - W / 2, -(ys[r] - H / 2));
        C.group.position.set(C.base.x, C.base.y, 0);
        C.ip.copy(C.base);
      });
      layoutBox = { minX: -W / 2, maxX: W / 2, minY: -H / 2, maxY: H / 2 };
    }

    // 2 つ目以降のタグの分岐点から、選んだときだけ光る線
    function buildSecondary() {
      nodes.forEach((n) => {
        n.it.tags.slice(1).forEach((t) => {
          const tn = tagByName[t];
          if (!tn) return;
          let mesh, cross;
          if (tn.circle === n.circle) {
            const avoid = n.circle.nodes.filter((o) => o !== n).map((o) => o.pos);
            mesh = dashRibbon(branchRoute(tn.pos, n.pos, true, avoid).map((p) => v3(p[0], p[1])), 0.05, new T.Color(tn.tag.color));
            n.circle.group.add(mesh);
            cross = false;
          } else {
            const a = v3(tn.circle.base.x + tn.pos[0], tn.circle.base.y + tn.pos[1]);
            const b = v3(n.circle.base.x + n.pos[0], n.circle.base.y + n.pos[1]);
            mesh = dashRibbon([a, b], 0.06, new T.Color(tn.tag.color));
            board.add(mesh);
            cross = true; // 魔法陣をまたぐ線（待機中に動いている間は消す）
          }
          mesh.position.z = -0.005;
          secLines.push({ mesh, tag: t, item: n.it.id, cross, k: 0 });
        });
      });
    }

    function rebuildStars() {
      if (starField) { scene.remove(starField); starField.geometry.dispose(); starField.material.dispose(); }
      const ext = Math.max(layoutBox.maxX - layoutBox.minX, layoutBox.maxY - layoutBox.minY);
      starField = M.stars(1800, Math.max(260, ext * 5), 'ml-stars', '#b8d4ff');
      starField.material.size = 1.7;
      starField.material.sizeAttenuation = false;
      starField.material.opacity = 0.7;
      starField.position.z = -60;
      scene.add(starField);
    }

    function build(view) {
      if (drag || press) { pendingView = view; return; }
      pendingView = null;
      const keepIdle = idle.blend > 0;
      clearBoard();
      const many = view.total > LITE_ITEMS;
      view.circles.forEach((cv) => circles.push(buildCircle(cv, many)));
      layoutGrid();
      buildSecondary();
      rebuildStars();
      updateHubLabels();
      updateTagLabels();
      if (!built) {
        built = true;
        appearStart = time; // 読み込み時だけ、中心から近い順に少しずつ遅らせてフェードイン
        home(true);
      } else {
        appearStart = -1000;
        if (atHome && !fly) home(true);
      }
      if (keepIdle) initIdleLayout();
      measureAll = true;
    }

    function updateHubLabels() {
      const active = filterActive();
      circles.forEach((C) => {
        const total = C.view.items.length;
        const n = C.view.items.filter((it) => S.match[it.id]).length;
        let sub;
        if (!S.view.total) sub = '＋ 追加 から登録';
        else if (active) sub = n + ' / ' + total + ' 件';
        else sub = total + ' 件';
        C.hubLabel.el.innerHTML = '<b>' + esc(C.name) + '</b><span>' + esc(sub) + '</span>';
      });
      measureAll = true;
    }
    function updateTagLabels() {
      const active = filterActive();
      tagNodes.forEach((tn) => {
        tn.label.el.classList.toggle('sel', S.tags.indexOf(tn.tag.name) >= 0);
        tn.dimTarget = !active || S.tagMatch[tn.tag.name] ? 1 : 0.25;
        tn.label.el.classList.toggle('ml-dim', tn.dimTarget < 1);
      });
    }

    // ---------------------------------------------------------------- カメラ
    // 箱（ワールド座標）が上部のバーを避けて画面に収まるカメラ位置
    function fitBox(box, margin) {
      const topPx = S.topH + 6, botPx = 22;
      const availH = Math.max(120, st.h - topPx - botPx);
      const w = Math.max(1, box.maxX - box.minX), h = Math.max(1, box.maxY - box.minY);
      const d = Math.max(h * st.h / (2 * TAN * availH), w * st.h / (2 * TAN * st.w)) * (margin || 1.04);
      const ppu = st.h / (2 * d * TAN);
      const offPx = topPx + availH / 2 - st.h / 2; // 使える範囲の中心は画面の中心より下
      const cx = (box.minX + box.maxX) / 2, cy = (box.minY + box.maxY) / 2 + offPx / ppu;
      return { pos: new T.Vector3(cx, cy, d), target: new T.Vector3(cx, cy, 0), d };
    }
    function flyTo(view, dur, instant) {
      if (instant) {
        camera.position.copy(view.pos);
        controls.target.copy(view.target);
        controls.update();
        fly = null;
        return;
      }
      fly = { from: camera.position.clone(), fromT: controls.target.clone(), to: view.pos, toT: view.target, t: 0, dur: dur || 0.8 };
    }
    function home(instant) {
      atHome = true;
      flyTo(fitBox(layoutBox, 1.04), 0.8, instant);
    }
    function circleBox(C) {
      const p = C.group.position;
      return { minX: p.x - C.ext, maxX: p.x + C.ext, minY: p.y - C.ext, maxY: p.y + C.ext };
    }
    function focusCircle(C) { atHome = false; flyTo(fitBox(circleBox(C), 1.03), 0.7); }
    function focusNodes(list) {
      if (!list.length) return;
      const box = { minX: Infinity, maxX: -Infinity, minY: Infinity, maxY: -Infinity };
      const wp = new T.Vector3();
      list.forEach((n) => {
        n.sigil.getWorldPosition(wp);
        box.minX = Math.min(box.minX, wp.x - n.R - 1.5); box.maxX = Math.max(box.maxX, wp.x + n.R + 1.5);
        box.minY = Math.min(box.minY, wp.y - n.R - 2); box.maxY = Math.max(box.maxY, wp.y + n.R + 1.5);
      });
      // 1 つだけでも寄りすぎないよう、最低 16×16 の範囲にする
      const cx = (box.minX + box.maxX) / 2, cy = (box.minY + box.maxY) / 2;
      const hw = Math.max(8, (box.maxX - box.minX) / 2), hh = Math.max(8, (box.maxY - box.minY) / 2);
      atHome = false;
      flyTo(fitBox({ minX: cx - hw, maxX: cx + hw, minY: cy - hh, maxY: cy + hh }, 1.05), 0.8);
    }
    controls.addEventListener('start', () => { atHome = false; fly = null; });

    function onFilter(flyToTags) {
      updateHubLabels();
      updateTagLabels();
      if (flyToTags) {
        if (S.tags.length) focusNodes(nodes.filter((n) => S.match[n.it.id])); // カメラを合う円へ寄せる
        else home();
      }
    }

    // ---------------------------------------------------------------- 当たり判定
    const ray = new T.Raycaster();
    const mouse = new T.Vector2();
    const plane = new T.Plane(new T.Vector3(0, 0, 1), 0);
    const PRIORITY = { item: 3, tag: 2, hub: 1 };
    function refFromEl(e) {
      while (e && e !== document.body) { if (e._mlRef) return e._mlRef; e = e.parentElement; }
      return null;
    }
    // 画面の (x, y) にあるもの: item / tag / hub / area（魔法陣の中の空き）/ none（外）。3D の外（上部バーなど）は null
    function targetAt(x, y) {
      const e = document.elementFromPoint(x, y);
      const r = refFromEl(e);
      if (r) return r;
      if (e !== canvas) return null;
      const rect = canvas.getBoundingClientRect();
      mouse.set(((x - rect.left) / rect.width) * 2 - 1, -((y - rect.top) / rect.height) * 2 + 1);
      ray.setFromCamera(mouse, camera);
      let best = null, bp = 0;
      ray.intersectObjects(hitList).forEach((h) => {
        const ref = h.object.userData.ref, p = PRIORITY[ref.type] || 0;
        if (p > bp) { best = ref; bp = p; }
      });
      if (best) return best;
      const pt = new T.Vector3();
      if (ray.ray.intersectPlane(plane, pt)) {
        for (const C of circles) {
          const local = C.group.worldToLocal(pt.clone());
          if (Math.hypot(local.x, local.y) <= C.frameR + 0.5) return { type: 'area', circle: C };
        }
      }
      return { type: 'none' };
    }

    // ---------------------------------------------------------------- 操作（2.3）
    let press = null, drag = null;
    function draggable(t) { return t && (t.type === 'item' || (t.type === 'tag' && !t.tag.pseudo) || t.type === 'hub'); }
    root.addEventListener('pointerdown', (e) => {
      if (e.button !== 0 && e.button !== 2) return;
      const t = targetAt(e.clientX, e.clientY);
      press = { x: e.clientX, y: e.clientY, button: e.button, target: t };
      if (e.button === 0 && draggable(t)) controls.enabled = false; // 円・タグ・中心は傾けずにドラッグで入れ替え
      closeDrop();
      if (document.activeElement && document.activeElement.blur && document.activeElement.id === 'ml-q') document.activeElement.blur();
    }, true);
    window.addEventListener('pointermove', (e) => {
      if (press && press.button === 0 && draggable(press.target)) {
        if (!drag && Math.abs(e.clientX - press.x) + Math.abs(e.clientY - press.y) > 6) startDrag(press.target);
        if (drag) moveDrag(e);
        return;
      }
      if (!e.buttons && !S.modal) updateHover(e);
    });
    window.addEventListener('pointerup', (e) => {
      if (!press) return;
      const p = press;
      press = null;
      controls.enabled = true;
      if (drag) { finishDrag(); flushPending(); return; }
      flushPending();
      if (Math.abs(e.clientX - p.x) + Math.abs(e.clientY - p.y) > 6) return; // 傾け・移動だった
      click(p.target, p.button);
    });
    labelsRoot.addEventListener('contextmenu', (e) => e.preventDefault());
    root.addEventListener('contextmenu', (e) => e.preventDefault());
    labelsRoot.addEventListener('wheel', (e) => {
      // ラベルの上でもホイールでズームできるように 3D へ渡す
      e.preventDefault();
      canvas.dispatchEvent(new WheelEvent('wheel', { deltaX: e.deltaX, deltaY: e.deltaY, deltaMode: e.deltaMode, clientX: e.clientX, clientY: e.clientY }));
    }, { passive: false });

    function click(t, button) {
      if (!t) return;
      if (button === 0) {
        if (t.type === 'item') openItem(t.it);
        else if (t.type === 'tag') toggleTag(t.tag.name);
        else if (t.type === 'hub' || t.type === 'area') focusCircle(t.circle); // 魔法陣の中の空きは、その魔法陣へ寄る
        else home(); // 外は全体へ戻る
      } else if (button === 2) {
        if (t.type === 'item') editItem(t.it);
        else if (t.type === 'tag') { if (t.tag.pseudo) newItem(S.tags); else openRename('tag', t.tag.name); }
        else if (t.type === 'hub') openRename('circle', t.circle.name);
        else newItem(S.tags);
      }
    }

    function updateHover(e) {
      const t = targetAt(e.clientX, e.clientY);
      const n = t && t.type === 'item' ? t : null;
      const tg = t && t.type === 'tag' ? t : null;
      if (n !== hover) {
        if (hover) { hover.nameLabel.el.classList.remove('on'); hover.emojiLabel.el.classList.remove('on'); }
        hover = n;
        if (hover) { hover.nameLabel.el.classList.add('on'); hover.emojiLabel.el.classList.add('on'); }
      }
      hoverTag = tg;
      canvas.style.cursor = t && (t.type === 'item' || t.type === 'tag' || t.type === 'hub') ? 'pointer' : '';
    }

    function labelOf(t) {
      if (!t) return null;
      if (t.type === 'item') return t.nameLabel.el;
      if (t.type === 'tag') return t.label.el;
      if (t.type === 'hub' || t.type === 'area') return t.circle.hubLabel.el;
      return null;
    }
    function startDrag(src) {
      const g = el('div', 'ml-ghost');
      if (src.type === 'item') { setC(g, src.tag.color); g.innerHTML = esc(src.it.emoji + ' ' + src.it.name) + '<small>同じ先頭タグの円に重ねて入れ替え</small>'; }
      else if (src.type === 'tag') { setC(g, src.tag.color); g.innerHTML = '#' + esc(src.tag.label) + '<small>タグに重ねて入れ替え／別の魔法陣の空き所へ移す</small>'; }
      else { setC(g, src.circle.color); g.innerHTML = esc(src.circle.name) + '<small>別の魔法陣の中心に重ねて並びを入れ替え</small>'; }
      document.body.appendChild(g);
      drag = { src, ghost: g, drop: null, hl: null };
      if (hover) { hover.nameLabel.el.classList.remove('on'); hover.emojiLabel.el.classList.remove('on'); hover = null; }
    }
    function dropFor(src, t) {
      if (!t) return null;
      if (src.type === 'item') return t.type === 'item' && t !== src && t.tag === src.tag ? { kind: 'items', t } : null;
      if (src.type === 'tag') {
        if (t.type === 'tag' && t !== src && !t.tag.pseudo) return { kind: 'tags', t };
        if ((t.type === 'area' || t.type === 'hub') && t.circle !== src.circle) return { kind: 'move', t };
        return null;
      }
      if (src.type === 'hub') return t.type === 'hub' && t.circle !== src.circle ? { kind: 'circles', t } : null;
      return null;
    }
    function moveDrag(e) {
      drag.ghost.style.left = e.clientX + 'px';
      drag.ghost.style.top = e.clientY + 'px';
      const d = dropFor(drag.src, targetAt(e.clientX, e.clientY));
      const hl = d ? labelOf(d.t) : null;
      if (hl !== drag.hl) {
        if (drag.hl) drag.hl.classList.remove('ml-drop-target');
        if (hl) hl.classList.add('ml-drop-target');
        drag.hl = hl;
      }
      drag.drop = d;
    }
    function endDrag() {
      if (!drag) return;
      if (drag.hl) drag.hl.classList.remove('ml-drop-target');
      drag.ghost.remove();
      drag = null;
    }
    function cancelDrag() { endDrag(); press = null; controls.enabled = true; flushPending(); }
    function finishDrag() {
      const src = drag.src, d = drag.drop;
      endDrag();
      if (!d) return; // 何もない所で離すと取り消し
      if (d.kind === 'items') mutate('/api/items/swap', { a: src.it.id, b: d.t.it.id }).catch(() => {});
      else if (d.kind === 'tags') mutate('/api/tags/swap', { a: src.tag.name, b: d.t.tag.name }).catch(() => {});
      else if (d.kind === 'move') mutate('/api/tag', { name: src.tag.name, circle: d.t.circle.name }, '#' + src.tag.label + ' を「' + d.t.circle.name + '」へ移しました').catch(() => {});
      else if (d.kind === 'circles') {
        const order = circles.map((C) => C.name);
        const a = order.indexOf(src.circle.name), b = order.indexOf(d.t.circle.name);
        order[a] = d.t.circle.name;
        order[b] = src.circle.name;
        mutate('/api/circles', { order }).catch(() => {});
      }
    }
    function flushPending() { if (pendingView && !drag && !press) build(pendingView); }

    // ---------------------------------------------------------------- 待機中の動き（2.7）
    const idle = { on: false, blend: 0, t: 0, saved: null, mode: 'orbit', applied: false, theta: 0, Rview: 0, kmax: 1, rect: null };
    let mouseOut = true, mouseOutAt = performance.now(); // 起動直後はマウスが外にいる扱い
    let lastKey = -1e9;
    document.addEventListener('mouseout', (e) => { if (!e.relatedTarget) { mouseOut = true; mouseOutAt = performance.now(); } });
    document.addEventListener('mousemove', () => { mouseOut = false; });
    document.addEventListener('mouseover', () => { mouseOut = false; });
    document.addEventListener('keydown', () => { lastKey = performance.now(); }, true);

    function idleWanted(now) {
      const mode = S.state ? S.state.idle : 'off';
      // タグを選んで寄っている（絞り込み中）ときも、マウスが外れたら動き出す
      return mode !== 'off' && mouseOut && now - mouseOutAt > 1000 && now - lastKey > 1000 &&
        !S.modal && !drag && !press && circles.length > 0;
    }
    function homeHalf() {
      const h = fitBox(layoutBox, 1.04);
      const halfH = h.d * TAN, halfW = halfH * (st.w / st.h);
      const availFrac = Math.max(120, st.h - S.topH - 28) / st.h;
      return { h, halfW, halfHa: halfH * availFrac, cy: h.target.y };
    }
    function initIdleLayout() {
      idle.mode = S.state.idle === 'float' || circles.length === 1 ? 'float' : 'orbit'; // 魔法陣が 1 つなら orbit でも漂う
      const hh = homeHalf();
      if (idle.mode === 'orbit') {
        let Rdisk = 0;
        circles.forEach((C) => { Rdisk = Math.max(Rdisk, C.base.length() + C.ext); });
        idle.Rview = Math.max(Math.min(hh.halfW, hh.halfHa) / 1.04, Rdisk * 1.06);
        let kmax = Infinity;
        circles.forEach((C) => { const d = C.base.length(); if (d > 0.01) kmax = Math.min(kmax, (idle.Rview - C.ext) / d); });
        idle.kmax = Math.max(1, isFinite(kmax) ? kmax : 1);
        idle.view = fitBox({ minX: -idle.Rview, maxX: idle.Rview, minY: -idle.Rview, maxY: idle.Rview }, 1.0);
      } else {
        idle.view = hh.h;
        idle.rect = { cx: hh.h.target.x, cy: hh.h.target.y, hw: hh.halfW, hh: hh.halfHa };
        circles.forEach((C, i) => {
          if (!C.fl) C.fl = { x: C.base.x, y: C.base.y, a: M.rng(C.name + '#dir')() * Math.PI * 2 };
          C.fl.seed = i * 1.7 + C.seed * 10;
        });
        idle.speed = 0.07 * Math.min(hh.halfW, hh.halfHa);
      }
    }
    function startIdle() {
      idle.on = true;
      if (idle.blend <= 0) {
        idle.saved = { pos: camera.position.clone(), target: controls.target.clone(), atHome };
        idle.t = 0;
        circles.forEach((C) => { C.fl = null; });
      }
      initIdleLayout();
      flyTo(idle.view, 1.0);
    }
    function stopIdle() {
      idle.on = false;
      // マウスが戻ったら、魔法陣が全部窓に入る全体表示へ戻る
      atHome = true;
      flyTo(fitBox(layoutBox, 1.04), 1.0);
    }
    function updateIdle(dt, now) {
      const want = idleWanted(now);
      if (want && !idle.on) startIdle();
      else if (!want && idle.on) stopIdle();
      if (idle.on) idle.t += dt;
      idle.blend = clamp(idle.blend + (idle.on ? dt : -dt) / 1.0, 0, 1);
      const b = M.ease(idle.blend);
      if (idle.blend <= 0) {
        if (idle.applied) {
          circles.forEach((C) => { C.group.position.set(C.base.x, C.base.y, 0); C.group.scale.setScalar(1); C.sc = 1; });
          board.rotation.z = 0;
          idle.applied = false;
        }
        return;
      }
      idle.applied = true;
      if (idle.on) {
        if (idle.mode === 'orbit') {
          // 全体の中心まわりに反時計回り（1 周 90 秒）＋ 20 秒で 1 往復、外へ離れてまたくっつく
          idle.theta = (idle.t * Math.PI * 2 / 90) % (Math.PI * 2);
          const s = (1 - Math.cos(idle.t * Math.PI * 2 / 20)) / 2;
          const k = 1 + (idle.kmax - 1) * s;
          circles.forEach((C) => { C.ip.set(C.base.x * k, C.base.y * k); });
          circles.forEach((C) => {
            // 離れた隙間に合わせて不規則に拡大（最大 ×1.2・ぶつからない・はみ出さない）
            let sc = 1 + 0.2 * s * (0.35 + 0.65 * C.seed);
            sc = Math.min(sc, (idle.Rview - C.ip.length()) / C.ext);
            circles.forEach((D2) => {
              if (D2 !== C) sc = Math.min(sc, (C.ip.distanceTo(D2.ip) - 0.4) / (C.ext + D2.ext));
            });
            C.isc = Math.max(1, sc);
          });
        } else {
          // 画面いっぱいを不規則に漂い、大きさも魔法陣ごとに不規則に大きく・小さくなる（窓からははみ出さない）
          idle.theta = 0;
          const r = idle.rect;
          circles.forEach((C) => {
            const f = C.fl;
            f.a += (Math.sin(idle.t * 0.37 + f.seed) * 0.6 + Math.sin(idle.t * 0.13 + f.seed * 3) * 0.4) * dt * 0.9;
            f.x += Math.cos(f.a) * idle.speed * dt;
            f.y += Math.sin(f.a) * idle.speed * dt;
            const wave = Math.sin(idle.t * 0.29 + f.seed * 1.3) * 0.6 + Math.sin(idle.t * 0.47 + f.seed * 2.9) * 0.4; // -1〜1
            C.isc = Math.min(1 + FLOAT_SCALE * wave, Math.min(r.hw, r.hh) / C.ext);
            const e = C.ext * C.isc;
            const lx = Math.max(0, r.hw - e), ly = Math.max(0, r.hh - e);
            if (f.x < r.cx - lx) { f.x = r.cx - lx; f.a = Math.PI - f.a; }
            if (f.x > r.cx + lx) { f.x = r.cx + lx; f.a = Math.PI - f.a; }
            if (f.y < r.cy - ly) { f.y = r.cy - ly; f.a = -f.a; }
            if (f.y > r.cy + ly) { f.y = r.cy + ly; f.a = -f.a; }
            C.ip.set(f.x, f.y);
          });
        }
      }
      let th = idle.theta;
      if (th > Math.PI) th -= Math.PI * 2;
      board.rotation.z = th * b;
      circles.forEach((C) => {
        C.group.position.set(C.base.x + (C.ip.x - C.base.x) * b, C.base.y + (C.ip.y - C.base.y) * b, 0);
        C.sc = 1 + (C.isc - 1) * b;
        C.group.scale.setScalar(C.sc);
      });
    }

    // ---------------------------------------------------------------- ラベル（毎フレーム画面座標へ投影）
    const wp = new T.Vector3();
    function updateLabels() {
      const dist = camera.position.distanceTo(controls.target);
      const ppu = st.h / (2 * dist * TAN); // 1 目盛り（ワールド 1）あたりの px
      const far = ppu < 5; // 全体表示でも名前は出す。豆粒ほど引いたときだけ隠して絵文字だけ
      if (far !== lastFar) { labelsRoot.classList.toggle('far', far); lastFar = far; measureAll = true; }
      const small = ppu < 14; // 引きの表示では名前の札を細く短く
      if (small !== lastSmall) { labelsRoot.classList.toggle('small', small); lastSmall = small; measureAll = true; }
      const fs = Math.round(clamp(ppu * 0.42, 10, 26) * 2) / 2; // 寄ると魔法陣と同じ割合で文字も大きく
      if (fs !== lastFs) {
        lastFs = fs;
        labelsRoot.style.setProperty('--fs', fs + 'px');
        labelsRoot.style.setProperty('--fs-tag', Math.round(clamp(ppu * 0.4, 9, 22)) + 'px');
        labelsRoot.style.setProperty('--fs-hub', Math.round(clamp(ppu * 0.72, 12, 46)) + 'px');
        measureAll = true;
      }
      const W = st.w, H = st.h;
      labels.forEach((l) => {
        l.anchor.getWorldPosition(wp);
        wp.project(camera);
        l.vis = wp.z < 1 && wp.z > -1;
        l.x = (wp.x * 0.5 + 0.5) * W;
        l.y = (-wp.y * 0.5 + 0.5) * H;
        if (l.kind === 'emoji') {
          const px = Math.round(clamp(l.R * (l.circle ? l.circle.sc : 1) * ppu * 0.78, 9, 64));
          if (px !== l.fsz) { l.fsz = px; l.el.style.fontSize = px + 'px'; }
          l.w = l.h = px * 1.6;
        }
      });
      if (measureAll) {
        labels.forEach((l) => { if (l.kind !== 'emoji') { l.w = l.el.offsetWidth; l.h = l.el.offsetHeight; } });
        measureAll = false;
      }
      // 名前どうしが重なったら、下の方を重ならない所まで下へずらす（中心の名前は動かない側）
      const placed = [];
      const flowL = [];
      labels.forEach((l) => {
        if (!l.vis || l.center) return;
        if (l.kind === 'name' && far) return;
        if (l.fixed) { l.top = l.y; placed.push([l.x - l.w / 2, l.y, l.x + l.w / 2, l.y + l.h]); } else flowL.push(l);
      });
      flowL.sort((a, b) => a.y - b.y);
      flowL.forEach((l) => {
        let top = l.y;
        const x0 = l.x - l.w / 2, x1 = l.x + l.w / 2;
        for (let guard = 0; guard < 40; guard++) {
          let hit = null;
          for (const r of placed) {
            if (x0 < r[2] && x1 > r[0] && top < r[3] && top + l.h > r[1]) { hit = r; break; }
          }
          if (!hit) break;
          top = hit[3] + 1;
        }
        l.top = top;
        placed.push([x0, top, x1, top + l.h]);
      });
      labels.forEach((l) => {
        if (!l.vis) { if (l.shown) { l.el.style.display = 'none'; l.shown = false; } return; }
        if (!l.shown) { l.el.style.display = ''; l.shown = true; }
        if (l.kind === 'name' && far) return; // 引きの表示では名前は CSS で隠れている（位置は計算しない）
        const x = l.x - l.w / 2;
        const y = l.center ? l.y - l.h / 2 : (l.top == null ? l.y : l.top);
        l.el.style.transform = 'translate(' + x.toFixed(1) + 'px,' + y.toFixed(1) + 'px)';
      });
    }

    // ---------------------------------------------------------------- 毎フレーム
    const clock = new T.Clock();
    function sizeBackground() {
      const hh = 2 * 300 * TAN * 1.25;
      smoke1.scale.set(hh * camera.aspect, hh, 1);
      smoke2.scale.set(hh * camera.aspect * 1.1, hh * 1.1, 1);
    }
    sizeBackground();
    let frameErrors = 0;
    function frame() {
      requestAnimationFrame(frame); // 1 フレームで例外が出ても、次のフレームは描く（固まらない）
      try {
        frameBody();
      } catch (e) {
        if (frameErrors++ < 5) console.error(e);
      }
    }
    function frameBody() {
      const raw = clock.getDelta();
      const dt = Math.min(raw, 0.05);
      time += raw;
      const now = performance.now();
      if (st.size()) {
        sizeBackground();
        measureTop();
        if (built && isPortrait() !== layoutPortrait && S.view) build(S.view); // 縦長⇔横長が変わったら並べ直す
        if (!fly && idle.blend <= 0) home(true); // 窓の大きさが変わったら、魔法陣が全部入る大きさに合わせ直す
        measureAll = true;
      }

      smoke1.userData.uniforms.uTime.value = time;
      smoke2.userData.uniforms.uTime.value = time * 0.8;
      if (starField) starField.rotation.z += dt * 0.003;

      updateIdle(Math.min(raw, 0.3), now); // 回る速さ・戻る 1 秒は実時間で（重い端末でも長引かない）
      const active = filterActive();
      const crossHidden = idle.blend > 0;

      circles.forEach((C) => {
        C.frameGlyph.rotation.z -= dt * FRAME_SPIN; // マイナス = 時計回り
        C.frameRing2.rotation.z += dt * 0.02;
        C.hubGlyph.rotation.z += dt * 0.05;
        M.tickSpin(C.hub, dt);
        M.tickSpin(C.core, dt);
        const ah = M.ease((time - appearStart - 0.05) / 0.8);
        C.hub.scale.setScalar(0.4 + 0.6 * ah);
        M.setOpacity(C.hub, ah);
        C.hubGlyph.material.opacity = 0.5 * ah;
      });
      const traceIn = M.ease((time - appearStart - 0.25) / 0.9);

      nodes.forEach((n) => {
        n.appear = M.ease((time - appearStart - n.delay) / 0.7); // 中心から近い順に少しずつ遅らせてフェードイン
        const on = hover === n || (drag && drag.drop && drag.drop.t === n);
        n.hover = M.damp(n.hover, on ? 1 : 0, 10, dt);
        const m = !active || S.match[n.it.id];
        n.dim = M.damp(n.dim, m ? 1 : 0.18, 8, dt); // 合わない円は暗く
        n.sigil.scale.setScalar((0.3 + 0.7 * n.appear) * (1 + 0.14 * n.hover));
        M.tickSpin(n.sigil, dt * (1 + n.hover * 2.5));
        M.setOpacity(n.sigil, n.appear * n.dim * (0.85 + 0.15 * n.hover + 0.08 * Math.sin(time * 1.4 + n.delay * 9)));
        n.halo.material.opacity = (0.06 + 0.45 * n.hover) * n.appear * n.dim; // ホバーで円が光る
        n.stream.on = n.appear > 0.5 && m;
        n.hot.on = on;
        const op = Math.round(Math.min(1, n.appear * 1.2) * (0.28 + 0.72 * n.dim) * 50) / 50;
        if (op !== n.nameLabel.op) { n.nameLabel.op = op; n.nameLabel.el.style.opacity = op; n.emojiLabel.el.style.opacity = op; }
      });
      circles.forEach((C) => C.traces.forEach((tr) => {
        const hot = (hover && (tr.item === hover.it.id || (!tr.item && tr.tag === hover.tag.name))) || (hoverTag && tr.tag === hoverTag.tag.name);
        const dim = active && !(tr.item ? S.match[tr.item] : S.tagMatch[tr.tag]);
        const target = (hot ? 1 : (hover ? 0.3 : 0.5)) * (dim ? 0.22 : 1);
        tr.k = M.damp(tr.k, target, 8, dt);
        tr.mesh.material.opacity = tr.k * traceIn;
      }));
      secLines.forEach((sl) => {
        let want = (hover && hover.it.id === sl.item) || S.tags.indexOf(sl.tag) >= 0 || (hoverTag && hoverTag.tag.name === sl.tag);
        if (sl.cross && crossHidden) want = false;
        if (S.tags.length && !S.match[sl.item]) want = false;
        sl.k = M.damp(sl.k, want ? 0.9 : 0, 8, dt);
        sl.mesh.material.opacity = sl.k;
        sl.mesh.visible = sl.k > 0.01;
      });
      circles.forEach((C) => C.flow.update(dt));

      if (fly) {
        fly.t += Math.min(raw, 0.3) / fly.dur;
        const e = M.ease(fly.t);
        camera.position.lerpVectors(fly.from, fly.to, e);
        controls.target.lerpVectors(fly.fromT, fly.toT, e);
        if (fly.t >= 1) fly = null;
      }
      controls.update();
      scene.updateMatrixWorld(); // 作り直した直後や待機中の移動でも、ラベルが 1 フレーム遅れないように
      updateLabels();
      st.render();
    }
    requestAnimationFrame(frame);

    return {
      build, onFilter, home: () => home(false), cancelDrag, isDragging: () => !!drag,
      debug: () => ({ circles: circles.map((C) => ({ name: C.name, x: C.group.position.x, y: C.group.position.y, sc: C.sc, frameR: C.frameR })),
        nodes: nodes.length, tags: tagNodes.length, secLines: secLines.length, idle: { on: idle.on, blend: idle.blend, mode: idle.mode, theta: board.rotation.z },
        why: { mouseOut, outFor: performance.now() - mouseOutAt, keyAgo: performance.now() - lastKey, press: !!press, drag: !!drag, filter: filterActive(), modal: S.modal },
        camera: camera.position.toArray(), far: lastFar, fs: lastFs }),
    };
  }

  // ================================================================ 起動
  function startList(reason) {
    S.mode = 'list';
    document.body.classList.add(S.lite ? 'lite' : 'no-webgl');
    $('ml-stage').hidden = true;
    $('ml-listview').hidden = false;
    $('ml-list-btn').disabled = true;
    $('ml-home').textContent = '⟲ 先頭へ';
    if (reason) toast(reason, true);
    renderLists();
  }

  function bindUI() {
    const q = $('ml-q');
    q.addEventListener('input', () => { S.query = q.value; S.sel = 0; applyFilter(false); openDrop(); });
    q.addEventListener('focus', openDrop);
    q.addEventListener('keydown', (e) => {
      if (e.isComposing) return;
      if (e.key === 'ArrowDown') { e.preventDefault(); openDrop(); moveSel(1); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); openDrop(); moveSel(-1); }
      else if (e.key === 'Enter') { e.preventDefault(); openItem(S.results[S.sel]); }
      else if (e.key === 'Escape') { e.preventDefault(); clearFilter(); q.blur(); }
    });
    $('ml-clear').addEventListener('click', () => { clearFilter(); });
    const drop = $('ml-drop');
    drop.addEventListener('mousedown', (e) => e.preventDefault()); // 入力欄のフォーカスを保つ
    drop.addEventListener('click', (e) => { const r = e.target.closest('.r'); if (r) openItem(S.view.byId[r.dataset.id]); });
    drop.addEventListener('contextmenu', (e) => { const r = e.target.closest('.r'); if (r) { e.preventDefault(); editItem(S.view.byId[r.dataset.id]); } });
    drop.addEventListener('mousemove', (e) => {
      const r = e.target.closest('.r');
      if (r && Number(r.dataset.i) !== S.sel) { S.sel = Number(r.dataset.i); drop.querySelectorAll('.r').forEach((x, i) => x.classList.toggle('sel', i === S.sel)); }
    });
    // プルダウンの外をクリックしたら閉じる（絞り込みは続く）
    document.addEventListener('pointerdown', (e) => { if (!e.target.closest('.ml-search')) closeDrop(); }, true);

    const lg = $('ml-legend');
    lg.addEventListener('click', (e) => { const b = e.target.closest('[data-tag]'); if (b) toggleTag(b.dataset.tag); });
    lg.addEventListener('contextmenu', (e) => {
      e.preventDefault();
      const b = e.target.closest('[data-tag]');
      newItem(b ? (b.dataset.tag === UNTAGGED ? [] : [b.dataset.tag]) : S.tags);
    });
    bindListEvents($('ml-listview'));
    bindListEvents($('ml-listpanel'));
    $('ml-listview').addEventListener('contextmenu', (e) => { if (!e.defaultPrevented) { e.preventDefault(); newItem(S.tags); } });
    // ブラウザ既定の右クリックメニューはどこでも出さない(入力欄の上も。貼り付けは Ctrl+V)。
    // document で受けるので、上の各所の右クリック処理が先に動く
    document.addEventListener('contextmenu', (e) => e.preventDefault());

    $('ml-add').addEventListener('click', () => newItem(S.tags));
    $('ml-home').addEventListener('click', () => { if (scene3d) scene3d.home(); else window.scrollTo({ top: 0, behavior: 'smooth' }); });
    $('ml-list-btn').addEventListener('click', () => {
      const p = $('ml-listpanel');
      p.classList.toggle('open');
      $('ml-list-btn').classList.toggle('on', p.classList.contains('open'));
      renderLists();
    });
    $('ml-settings-btn').addEventListener('click', () => { renderSettings(true); showModal('settings'); });
    const liteBtn = $('ml-lite');
    liteBtn.setAttribute('aria-checked', S.lite ? 'true' : 'false');
    liteBtn.addEventListener('click', () => { writeLite(!S.lite); location.reload(); }); // 切り替えで再読込（設定ファイルには書かない）

    bindItemDialog();
    bindRenameDialog();
    bindSettings();

    document.addEventListener('keydown', (e) => {
      if (S.modal) {
        if (e.key === 'Escape') { e.preventDefault(); hideModal(); }
        return;
      }
      if (scene3d && scene3d.isDragging()) {
        if (e.key === 'Escape') { e.preventDefault(); scene3d.cancelDrag(); } // Esc でドラッグを取り消し
        return;
      }
      const t = e.target;
      if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) return;
      if (e.key === 'Escape') { clearFilter(); return; }
      if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) $('ml-q').focus(); // キーを打つと検索欄へ
    });
    window.addEventListener('resize', measureTop);
  }

  function boot() {
    bindUI();
    measureTop();
    api('/api/state').then((s) => {
      setState(s);
      if (S.lite) { startList(); return; } // Light 版は three.js を読まず、10 秒ごとの変更チェックもしない
      setInterval(checkVersion, POLL_MS);
      document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') checkVersion(); });
      loadScripts(VENDOR).then(() => {
        scene3d = create3D($('ml-stage'));
        if (!scene3d) { startList('WebGL が使えないため、一覧で表示しています'); return; }
        S.mode = '3d';
        scene3d.build(S.view);
        scene3d.onFilter(false);
        window.__magicLauncher = { debug: scene3d.debug, state: () => S };
      }).catch((e) => startList('3D を読み込めないため、一覧で表示しています（' + e.message + '）'));
    }).catch((e) => {
      const b = $('ml-banner');
      b.textContent = 'サーバにつながりません: ' + e.message + '（start.bat で起動してください）';
      b.hidden = false;
    });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot); else boot();
})();
