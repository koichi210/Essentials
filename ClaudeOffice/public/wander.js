// 席のメンバーが、仕事中でも席を立って歩き回る。床を細かいマス目に切って家具を避ける経路探索と、行動の状態機械。
// 家具の位置はスキンごとに ctx.nav が持つ。立ち寄り先(ctx.spot)はスキンごとに spots.js が用意する。

const WALK_SPEED = 2.3;
const STEP = 0.5;
const PAD = 0.4;

// ctx.nav = { x0, x1, z0, z1, rects: [[x0, x1, z0, z1]...], circles: [[x, z, r]...] }
function makeFree(nav) {
  return (x, z) => {
    if (x < nav.x0 + PAD || x > nav.x1 - PAD || z < nav.z0 + PAD || z > nav.z1 - PAD) {
      return false;
    }
    for (const [a, b, c, d] of nav.rects) {
      if (x > a - PAD && x < b + PAD && z > c - PAD && z < d + PAD) {
        return false;
      }
    }
    for (const [cx, cz, r] of nav.circles) {
      if (Math.hypot(x - cx, z - cz) < r + PAD) {
        return false;
      }
    }
    return true;
  };
}

// 床のマス目(8方向)。席と立ち寄り先は、いちばん近い空きマスにつなぐ
function buildGraph(ctx) {
  const nav = ctx.nav;
  const free = makeFree(nav);
  const nodes = new Map();
  const node = (id, x, z) => {
    nodes.set(id, { id, x, z, n: [] });
    return nodes.get(id);
  };
  const link = (a, b) => {
    nodes.get(a).n.push(b);
    nodes.get(b).n.push(a);
  };
  const nx = Math.floor((nav.x1 - nav.x0) / STEP);
  const nz = Math.floor((nav.z1 - nav.z0) / STEP);
  const gid = (i, j) => `g${i}_${j}`;
  const ok = (i, j) => nodes.has(gid(i, j));
  for (let i = 0; i <= nx; i++) {
    for (let j = 0; j <= nz; j++) {
      const x = nav.x0 + i * STEP;
      const z = nav.z0 + j * STEP;
      if (free(x, z)) {
        node(gid(i, j), x, z);
      }
    }
  }
  for (let i = 0; i <= nx; i++) {
    for (let j = 0; j <= nz; j++) {
      if (!ok(i, j)) {
        continue;
      }
      if (ok(i + 1, j)) {
        link(gid(i, j), gid(i + 1, j));
      }
      if (ok(i, j + 1)) {
        link(gid(i, j), gid(i, j + 1));
      }
      // 斜めは角を削らないときだけ
      if (ok(i + 1, j + 1) && ok(i + 1, j) && ok(i, j + 1)) {
        link(gid(i, j), gid(i + 1, j + 1));
      }
      if (j > 0 && ok(i + 1, j - 1) && ok(i + 1, j) && ok(i, j - 1)) {
        link(gid(i, j), gid(i + 1, j - 1));
      }
    }
  }
  const ids = [...nodes.keys()];
  const nearest = (x, z) => ids.reduce((best, id) => {
    const a = nodes.get(id);
    const b = nodes.get(best);
    return Math.hypot(a.x - x, a.z - z) < Math.hypot(b.x - x, b.z - z) ? id : best;
  }, ids[0]);
  ctx.seats.forEach((s, k) => {
    const near = nearest(s.x, s.z);
    node(`s${k}`, s.x, s.z);
    link(`s${k}`, near);
  });
  if (ctx.spot) {
    const near = nearest(ctx.spot.x, ctx.spot.z);
    node('spot', ctx.spot.x, ctx.spot.z);
    link('spot', near);
  }
  return { nodes, ids, free };
}

// 2点の間に家具がないか(0.2 刻みで調べる)
function clear(graph, a, b) {
  const d = Math.hypot(b.x - a.x, b.z - a.z);
  const n = Math.ceil(d / 0.2);
  for (let k = 1; k < n; k++) {
    if (!graph.free(a.x + ((b.x - a.x) * k) / n, a.z + ((b.z - a.z) * k) / n)) {
      return false;
    }
  }
  return true;
}

function route(graph, from, to) {
  const dist = new Map([[from, 0]]);
  const prev = new Map();
  const todo = new Set([from]);
  const goal = graph.nodes.get(to);
  const h = (n) => Math.hypot(n.x - goal.x, n.z - goal.z);
  while (todo.size) {
    let cur = null;
    let curF = Infinity;
    for (const id of todo) {
      const f = dist.get(id) + h(graph.nodes.get(id));
      if (f < curF) {
        cur = id;
        curF = f;
      }
    }
    todo.delete(cur);
    if (cur === to) {
      break;
    }
    const a = graph.nodes.get(cur);
    for (const nb of a.n) {
      const b = graph.nodes.get(nb);
      const d = dist.get(cur) + Math.hypot(a.x - b.x, a.z - b.z);
      if (d < (dist.get(nb) ?? Infinity)) {
        dist.set(nb, d);
        prev.set(nb, cur);
        todo.add(nb);
      }
    }
  }
  const path = [];
  for (let id = to; id !== undefined; id = prev.get(id)) {
    path.unshift(graph.nodes.get(id));
  }
  // マス目のカクカクを、見通せる所まで一気に歩くように間引く(席と立ち寄り先の点は残す)
  const out = [path[0]];
  let k = 0;
  while (k < path.length - 1) {
    let far = k + 1;
    if (path[k].id.startsWith('g')) {
      while (far + 1 < path.length && path[far + 1].id.startsWith('g') && clear(graph, path[k], path[far + 1])) {
        far++;
      }
    }
    out.push(path[far]);
    k = far;
  }
  return out.slice(1);
}

const rand = (a, b) => a + Math.random() * (b - a);

export function createWander(ctx) {
  const graph = buildGraph(ctx);
  const spot = ctx.spot;
  const angleTo = (from, to) => {
    let d = to - from;
    while (d > Math.PI) {
      d -= Math.PI * 2;
    }
    while (d < -Math.PI) {
      d += Math.PI * 2;
    }
    return from + d;
  };
  const randomStop = () => graph.ids[Math.floor(Math.random() * graph.ids.length)];

  const startWalk = (m, fromId, toId) => {
    m.path = route(graph, fromId, toId);
    m.at = fromId;
    m.mode = 'walk';
  };

  return {
    // 座っている状態で作る
    init(t) {
      return { mode: 'seated', nextAt: t + rand(3, 10), at: null, path: [], phase: 0, trip: null, actT: 0, carry: false, act: null };
    },
    // 毎フレーム。戻り値の motion を avatar.update に渡す。a.seat は席の番号
    update(a, t, dt) {
      const m = a.move;
      const root = a.avatar.root;
      const key = a.state ? a.state.key : 'waiting';
      const seatId = `s${a.seat}`;
      const canRoam = key !== 'sleeping';
      if (m.mode === 'seated') {
        if (canRoam && t >= m.nextAt) {
          const useSpot = spot && !spot.user && Math.random() < 0.5;
          m.trip = useSpot ? { kind: 'spot', stops: 0 } : { kind: 'pace', stops: 1 + Math.floor(Math.random() * 2) };
          if (useSpot) {
            spot.user = a.item?.id || true;
          }
          if (m.trip.kind === 'pace') {
            startWalk(m, seatId, randomStop());
          } else {
            startWalk(m, seatId, 'spot');
          }
        }
        return m;
      }
      // 席を外している途中で眠ったり入力待ちになったら、席へ戻る
      if (!canRoam && m.trip && m.trip.kind !== 'back') {
        if (m.trip.kind === 'spot') {
          this.release(a);
        }
        m.trip = { kind: 'back', stops: 0 };
        const from = m.path.length ? m.path[0].id : m.at;
        m.path = [...(m.path.length ? [m.path[0]] : []), ...route(graph, from, seatId)];
        m.mode = 'walk';
      }
      if (m.mode === 'act') {
        m.actT += dt;
        if (m.actT >= m.actLen) {
          this.release(a);
          if (spot.makeProp) {
            m.carry = true;
            if (!m.prop) {
              m.prop = spot.makeProp();
              a.avatar.arms[1].add(m.prop);
            }
          }
          m.trip = { kind: 'back', stops: 0 };
          startWalk(m, 'spot', seatId);
        }
        return m;
      }
      // 歩く
      const target = m.path[0];
      if (!target) {
        this.arrive(a, t);
        return m;
      }
      const dx = target.x - root.position.x;
      const dz = target.z - root.position.z;
      const d = Math.hypot(dx, dz);
      const step = WALK_SPEED * dt;
      m.phase += dt * 9;
      root.rotation.y = angleTo(root.rotation.y, Math.atan2(dx, dz)) * 0.25 + root.rotation.y * 0.75;
      if (d <= step) {
        root.position.x = target.x;
        root.position.z = target.z;
        m.at = target.id;
        m.path.shift();
      } else {
        root.position.x += (dx / d) * step;
        root.position.z += (dz / d) * step;
      }
      return m;
    },
    release(a) {
      if (spot && spot.user === (a.item?.id || true)) {
        spot.user = null;
      }
    },
    arrive(a, t) {
      const m = a.move;
      const root = a.avatar.root;
      const seat = ctx.seats[a.seat];
      if (m.trip.kind === 'spot' && m.at === 'spot') {
        m.mode = 'act';
        m.act = spot.kind;
        m.actT = 0;
        m.actLen = spot.kind === 'feed' ? 5 : 4;
        root.rotation.y = spot.rot;
      } else if (m.trip.kind === 'pace' && m.trip.stops > 0) {
        // 通路で立ち止まって、また別の場所へ
        m.trip.stops--;
        startWalk(m, m.at, randomStop());
      } else if (m.at !== `s${a.seat}`) {
        m.trip = { kind: 'back', stops: 0 };
        startWalk(m, m.at, `s${a.seat}`);
      } else {
        // 席に着く
        m.mode = 'seated';
        m.trip = null;
        m.carry = false;
        m.nextAt = t + rand(10, 25);
        if (m.prop) {
          m.prop.parent?.remove(m.prop);
          m.prop = null;
        }
        root.position.set(seat.x, 0, seat.z);
        root.rotation.y = seat.rot;
      }
    },
  };
}
