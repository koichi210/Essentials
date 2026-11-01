// 席のメンバーが、仕事中でも席を立って歩き回る。机の間の通路だけを通る経路探索と、行動の状態機械。
// 通路は全スキン共通(席の並びが同じ)。立ち寄り先(ctx.spot)はスキンごとに spots.js が用意する。

const WALK_SPEED = 2.3;

// 通路のグラフ: 縦通路は机の列の間、横通路は席の行の間。席は右隣の縦通路につながる
function buildGraph(seats, spot) {
  const cols = [...new Set(seats.map((s) => s.x))].sort((a, b) => a - b);
  const rows = [...new Set(seats.map((s) => s.z))].sort((a, b) => a - b);
  const vx = [cols[0] - 1.5, ...cols.map((x) => x + 1.5)];
  const corr = rows.map((z) => z + 1.25);
  const hz = [...rows, ...corr].sort((a, b) => a - b);
  const nodes = new Map();
  const node = (id, x, z) => {
    nodes.set(id, { id, x, z, n: [] });
    return nodes.get(id);
  };
  const link = (a, b) => {
    nodes.get(a).n.push(b);
    nodes.get(b).n.push(a);
  };
  const nid = (i, j) => `g${i}_${j}`;
  vx.forEach((x, i) => hz.forEach((z, j) => node(nid(i, j), x, z)));
  vx.forEach((_, i) => {
    hz.forEach((_z, j) => {
      if (j > 0) {
        link(nid(i, j - 1), nid(i, j));
      }
      if (i > 0 && corr.includes(hz[j])) {
        link(nid(i - 1, j), nid(i, j));
      }
    });
  });
  seats.forEach((s, k) => {
    node(`s${k}`, s.x, s.z);
    link(`s${k}`, nid(cols.indexOf(s.x) + 1, hz.indexOf(s.z)));
  });
  if (spot) {
    // 立ち寄り先は右端の縦通路の、いちばん近い横通路につなぐ
    const j = hz.reduce((best, z, k) => (Math.abs(z - spot.z) < Math.abs(hz[best] - spot.z) ? k : best), 0);
    node('spot', spot.x, spot.z);
    link('spot', nid(vx.length - 1, j));
  }
  return { nodes, ids: [...nodes.keys()].filter((id) => id.startsWith('g')) };
}

function route(graph, from, to) {
  const dist = new Map([[from, 0]]);
  const prev = new Map();
  const todo = new Set([from]);
  while (todo.size) {
    let cur = null;
    for (const id of todo) {
      if (cur === null || dist.get(id) < dist.get(cur)) {
        cur = id;
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
  return path.slice(1);
}

const rand = (a, b) => a + Math.random() * (b - a);

export function createWander(ctx) {
  const graph = buildGraph(ctx.seats, ctx.spot);
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
      const seat = ctx.seats[a.seat];
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
            startWalk(m, seatId, graph.ids[Math.floor(Math.random() * graph.ids.length)]);
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
        // 通路で立ち止まって、また別の通路へ
        m.trip.stops--;
        startWalk(m, m.at, graph.ids[Math.floor(Math.random() * graph.ids.length)]);
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
