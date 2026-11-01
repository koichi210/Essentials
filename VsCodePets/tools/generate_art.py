# オリジナルのペット画像(GIF)・アイコンを生成する。背景は tools/cut_seasons.py(四季の絵を切り出す)。
# 上流の画像は権利の都合ですべて削除し、ここで手続き的に描いたものに置き換えている。
# 使い方: py -3 tools/generate_art.py <media フォルダ>
import math
import os
import sys

from PIL import Image

G = 32          # ペットの論理グリッド(32x32)
SCALE = 2       # 出力は 64x64(正方形扱い)
GROUND = 29     # 足元の行
FRAMES = 4

# ---------------------------------------------------------------- 色

BASE = {
    'brown': ('#8a5a34', '#d9b48a'), 'black': ('#3b3b42', '#8c8c94'), 'white': ('#f2efe8', '#d2ccc0'),
    'red': ('#c4572e', '#f4d6b0'), 'akita': ('#d98c3c', '#fbecd2'), 'gray': ('#8d9098', '#d3d5da'),
    'green': ('#4f9a4a', '#bfe39a'), 'orange': ('#e0802c', '#ffd9a0'), 'yellow': ('#f2c53a', '#fff0b0'),
    'purple': ('#8a5cc4', '#d8c4f2'), 'blue': ('#4a7fd0', '#c4dcff'), 'pink': ('#e886b0', '#ffd8e8'),
    'beige': ('#d8bf95', '#f6ead2'), 'magical': ('#c9b6f2', '#fff6ff'), 'warrior': ('#7d8796', '#c9ced8'),
    'gray_jimothy': ('#6f7480', '#c2c5cc'),
}


def palette_for(color):
    """色名から (本体, 腹・明部, 模様) を返す。socks_/paint_ は本体色＋白の足先/まだら"""
    pattern = None
    name = color
    for prefix in ('socks_', 'paint_'):
        if color.startswith(prefix):
            pattern = prefix[:-1]
            name = color[len(prefix):]
    main, light = BASE.get(name, BASE['brown'])
    return {'main': main, 'light': light, 'pattern': pattern, 'name': color}


def shade(hex_color, k):
    h = hex_color.lstrip('#')
    r, g, b = (int(h[i:i + 2], 16) for i in (0, 2, 4))
    if k < 0:
        r, g, b = (int(c * (1 + k)) for c in (r, g, b))
    else:
        r, g, b = (int(c + (255 - c) * k) for c in (r, g, b))
    return '#%02x%02x%02x' % (r, g, b)


OUTLINE = '#2a2230'
EYE = '#1d1a22'

# ---------------------------------------------------------------- 描画の道具


class Canvas:
    def __init__(self):
        self.px = {}

    def set(self, x, y, c):
        x, y = int(round(x)), int(round(y))
        if 0 <= x < G and 0 <= y < G:
            self.px[(x, y)] = c

    def rect(self, x0, y0, x1, y1, c):
        for y in range(int(round(y0)), int(round(y1)) + 1):
            for x in range(int(round(x0)), int(round(x1)) + 1):
                self.set(x, y, c)

    def ellipse(self, cx, cy, rx, ry, c):
        for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
            for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
                if rx > 0 and ry > 0 and ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1.0:
                    self.set(x, y, c)

    def line(self, x0, y0, x1, y1, c):
        n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
        for i in range(n + 1):
            t = i / max(1, n)
            self.set(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, c)

    def outlined(self):
        """塗った形の外側1マスに輪郭線を付ける(ドット絵らしく見せるため)"""
        out = dict(self.px)
        for (x, y) in self.px:
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                p = (x + dx, y + dy)
                if p not in self.px and 0 <= p[0] < G and 0 <= p[1] < G:
                    out[p] = OUTLINE
        # 目などの細部は輪郭の上に描きたいので、あとで重ねる
        return out


# ---------------------------------------------------------------- 動きの共通計算


def motion(state, f):
    """状態とコマ番号から、体の上下・脚の位相・その他のパラメータを返す"""
    m = {'bob': 0, 'leg': 0.0, 'stride': 0, 'blink': False, 'tail': 0, 'lie': False, 'swipe': 0, 'ball': False, 'stretch': 0}
    if state in ('idle', 'stand'):
        m['bob'] = 1 if f in (1, 2) else 0
        m['blink'] = f == 3
        m['tail'] = (-1, 0, 1, 0)[f]
    elif state in ('walk', 'walk_fast', 'run'):
        m['leg'] = f / FRAMES * 2 * math.pi
        m['stride'] = {'walk': 2, 'walk_fast': 3, 'run': 4}[state]
        m['bob'] = 1 if f % 2 == 0 else 0
        m['tail'] = (1, 0, -1, 0)[f]
        m['stretch'] = 1 if state == 'run' else 0
    elif state == 'swipe':
        m['swipe'] = (0, 2, 4, 2)[f]
        m['tail'] = (0, 1, 0, -1)[f]
    elif state == 'with_ball':
        m['ball'] = True
        m['bob'] = 1 if f in (1, 3) else 0
        m['tail'] = (-1, 1, -1, 1)[f]
    elif state == 'lie':
        m['lie'] = True
        m['bob'] = 1 if f in (2, 3) else 0
        m['blink'] = True
    return m


def legs4(c, xs, top, m, color, length):
    for i, x in enumerate(xs):
        phase = m['leg'] + (0 if i in (0, 3) else math.pi)
        dx = round(math.sin(phase) * m['stride'] / 2) if m['stride'] else 0
        lift = 1 if m['stride'] and math.cos(phase) > 0.3 else 0
        if i == 3 and m['swipe']:
            # 前脚を振り上げる
            c.line(x, top, x + m['swipe'], top - m['swipe'] + 1, color)
            c.set(x + m['swipe'] + 1, top - m['swipe'] + 1, color)
            continue
        c.rect(x + dx, top, x + dx + 1, top + length - lift, color)


def ball(c, x, y):
    c.ellipse(x, y, 2, 2, '#e24a4a')
    c.set(x - 1, y - 1, '#ffb0b0')


# ---------------------------------------------------------------- 種類ごとの絵


def quadruped(p, state, f, kind):
    m = motion(state, f)
    c = Canvas()
    main, light = p['main'], p['light']
    dark = shade(main, -0.35)
    cfg = {
        'dog': dict(bw=8, bh=5, leg=5, head=5, snout=3, ear='flop', tail='up'),
        'fox': dict(bw=8, bh=4, leg=5, head=5, snout=4, ear='point', tail='bush'),
        'horse': dict(bw=9, bh=5, leg=8, head=4, snout=4, ear='point', tail='long'),
        'panda': dict(bw=9, bh=6, leg=4, head=6, snout=1, ear='round', tail='stub'),
        'raccoon': dict(bw=8, bh=5, leg=4, head=5, snout=2, ear='point', tail='ring'),
        'rat': dict(bw=6, bh=4, leg=2, head=4, snout=3, ear='round', tail='thin'),
        'monkey': dict(bw=6, bh=6, leg=6, head=5, snout=1, ear='side', tail='curl'),
    }[kind]
    bw, bh, leg = cfg['bw'], cfg['bh'], cfg['leg']
    if m['lie']:
        leg = 0
    by = GROUND - leg - bh + m['bob'] - (0 if m['lie'] else 0)
    bx = 14 - m['stretch']
    body = main
    if kind == 'panda':
        body = '#f4f2ee'
        main = '#2c2c32' if p['name'] != 'brown' else '#6b4a30'
    # 尻尾
    tx = bx - bw
    t = m['tail']
    if cfg['tail'] == 'up':
        c.line(tx, by, tx - 3, by - 4 + t, body)
        c.line(tx + 1, by, tx - 2, by - 4 + t, body)
    elif cfg['tail'] == 'bush':
        c.ellipse(tx - 3, by + 1 + t, 4, 2, body)
        c.ellipse(tx - 6, by + t, 2, 2, '#ffffff' if p['name'] != 'white' else '#e8e2d8')
    elif cfg['tail'] == 'long':
        mane = '#ffd6f4' if p['name'] == 'magical' else dark
        c.line(tx, by, tx - 3, by + 6 + t, mane)
        c.line(tx + 1, by, tx - 2, by + 7 + t, mane)
    elif cfg['tail'] == 'stub':
        c.ellipse(tx + 1, by + 1, 2, 2, body)
    elif cfg['tail'] == 'ring':
        for i in range(6):
            c.set(tx - i, by + 1 + (t if i > 3 else 0), dark if i % 2 == 0 else light)
            c.set(tx - i, by + 2 + (t if i > 3 else 0), dark if i % 2 == 0 else light)
    elif cfg['tail'] == 'thin':
        c.line(tx, by + 3, tx - 7, by + 1 + t, '#e8a0a8')
    elif cfg['tail'] == 'curl':
        c.line(tx, by + 2, tx - 3, by - 2, body)
        c.ellipse(tx - 3, by - 4 + t, 2, 2, body)
        c.set(tx - 3, by - 4 + t, None)
    # 脚
    leg_color = main if kind == 'panda' else (shade(body, -0.15))
    if not m['lie']:
        xs = [bx - bw + 2, bx - bw + 4, bx + bw - 4, bx + bw - 2]
        legs4(c, xs, by + bh - 1, m, leg_color, leg)
    # 体
    c.ellipse(bx, by + bh / 2, bw, bh, body)
    c.ellipse(bx + 1, by + bh * 0.8, bw - 3, bh / 2.5, light if kind != 'panda' else body)
    if kind == 'panda':
        c.ellipse(bx + bw - 3, by + 2, 3, 3, main)
    if p['pattern'] == 'paint':
        c.ellipse(bx - 3, by + 2, 3, 2, '#ffffff')
        c.ellipse(bx + 3, by + 4, 2, 1.5, '#ffffff')
    if p['pattern'] == 'socks' and not m['lie']:
        for x in [bx - bw + 2, bx - bw + 4, bx + bw - 4, bx + bw - 2]:
            c.rect(x, GROUND - 1, x + 1, GROUND, '#ffffff')
    if p['name'] == 'warrior' and kind == 'horse':
        c.rect(bx - 4, by - 1, bx + 4, by + 2, '#b03a3a')
        c.rect(bx - 4, by + 2, bx + 4, by + 2, '#e9c46a')
    # 頭
    hs = cfg['head']
    hx = bx + bw + 1
    hy = by - hs + 3 + (2 if m['lie'] else 0)
    if kind == 'horse':
        c.line(bx + bw - 2, by + 1, hx + 1, hy - 1, body)
        c.line(bx + bw - 1, by + 2, hx + 2, hy, body)
        c.line(bx + bw - 3, by + 1, hx, hy - 2, '#ffd6f4' if p['name'] == 'magical' else dark)
        hy -= 3
    c.ellipse(hx, hy, hs, hs - 1, body)
    if cfg['snout']:
        sl = cfg['snout']
        c.ellipse(hx + hs - 1, hy + 1, sl, 2, light if kind not in ('panda',) else body)
        c.set(hx + hs + sl - 1, hy, EYE)
    # 耳
    if cfg['ear'] == 'flop':
        c.ellipse(hx - 2, hy - 1, 1.5, 3, shade(body, -0.25))
    elif cfg['ear'] == 'point':
        for i in range(3):
            c.rect(hx - 2 + i * 0, hy - hs + 1 - i, hx - 1 + (i == 0), hy - hs + 1 - i, body)
        c.rect(hx + 1, hy - hs, hx + 2, hy - hs, body)
        c.set(hx + 1, hy - hs - 1, body)
    elif cfg['ear'] == 'round':
        ear = main if kind == 'panda' else ('#e8a0a8' if kind == 'rat' else body)
        c.ellipse(hx - 2, hy - hs + 1, 2, 2, ear)
    elif cfg['ear'] == 'side':
        c.ellipse(hx - hs + 1, hy, 2, 2, light)
    # 顔の模様
    if kind == 'raccoon':
        c.rect(hx - 1, hy - 1, hx + hs - 1, hy, '#2c2c32')
    if kind == 'monkey':
        c.ellipse(hx + 1, hy + 1, 3, 2.5, light)
    if kind == 'panda':
        c.ellipse(hx + 2, hy - 1, 1.5, 1.5, main)
    if kind == 'horse' and p['name'] == 'magical':
        c.line(hx + 1, hy - hs, hx + 3, hy - hs - 4, '#ffd34a')
    if kind == 'horse' and p['name'] == 'warrior':
        c.rect(hx - 2, hy - 3, hx + 3, hy - 2, '#9aa3b0')
    out = c.outlined()
    # 目(輪郭の上に描く)
    ex, ey = hx + 2, hy - 1
    if kind == 'panda':
        out[(ex, ey)] = '#ffffff' if not m['blink'] else main
    elif m['blink']:
        out[(ex, ey)] = OUTLINE
        out[(ex - 1, ey)] = OUTLINE
    else:
        out[(ex, ey)] = EYE
        out[(ex, ey - 1)] = EYE
    if kind == 'raccoon':
        out[(ex, ey)] = '#ffffff' if not m['blink'] else '#2c2c32'
    if m['ball']:
        cc = Canvas()
        ball(cc, hx + hs + cfg['snout'] + 1, hy + 3)
        out.update(cc.outlined() if False else cc.px)
    if m['lie']:
        out[(hx + hs + 3, hy - hs - 1 - m['bob'])] = '#9ab0d8'
    return out


def bird(p, state, f, kind):
    m = motion(state, f)
    c = Canvas()
    main, light = p['main'], p['light']
    if kind == 'rubber-duck':
        main, light = '#f6d23a', '#fff4a8'
    by = GROUND - 9 + m['bob'] + (3 if m['lie'] else 0)
    bx = 13
    if kind != 'rubber-duck' and not m['lie']:
        for i, x in enumerate((bx - 1, bx + 2)):
            phase = m['leg'] + i * math.pi
            dx = round(math.sin(phase) * m['stride'] / 2) if m['stride'] else 0
            c.rect(x + dx, by + 5, x + dx, GROUND, '#e8962c')
            c.rect(x + dx, GROUND, x + dx + 1, GROUND, '#e8962c')
    # 体と尾羽
    c.ellipse(bx, by + 2, 7, 5, main)
    tail = '#f6d23a' if kind == 'rubber-duck' else shade(main, -0.2)
    c.line(bx - 7, by + 1, bx - 10, by - 2 + m['tail'], tail)
    c.line(bx - 7, by + 2, bx - 10, by - 1 + m['tail'], tail)
    if kind == 'cockatiel':
        c.line(bx - 7, by + 3, bx - 13, by + 6 + m['tail'], shade(main, -0.3))
    # 羽
    flap = (0, -1, 0, 1)[f] if m['stride'] else 0
    if m['swipe']:
        flap = -m['swipe']
    c.ellipse(bx - 1, by + 2 + flap, 4, 2.5, light)
    # 頭
    hx, hy = bx + 5, by - 4
    head = main
    if kind == 'cockatiel':
        head = '#f6d77a' if p['name'] == 'gray' else '#f2e2b8'
    c.ellipse(hx, hy, 4, 4, head)
    beak = '#f39c2c' if kind != 'cockatiel' else '#c8b8a0'
    c.rect(hx + 4, hy, hx + 6, hy + 1, beak)
    if kind == 'rubber-duck':
        c.rect(hx + 4, hy, hx + 7, hy + 1, '#f39c2c')
    if kind == 'chicken':
        c.rect(hx - 1, hy - 5, hx + 1, hy - 4, '#d83a3a')
        c.set(hx, hy - 6, '#d83a3a')
        c.rect(hx + 4, hy + 2, hx + 4, hy + 3, '#d83a3a')
    if kind == 'cockatiel':
        c.line(hx - 1, hy - 4, hx - 3, hy - 8, '#f2c53a')
        c.line(hx, hy - 4, hx - 1, hy - 8, '#f2c53a')
    out = c.outlined()
    if kind == 'cockatiel':
        out[(hx + 1, hy + 2)] = '#f07a4a'
        out[(hx + 2, hy + 2)] = '#f07a4a'
    ex, ey = hx + 2, hy - 1
    out[(ex, ey)] = OUTLINE if m['blink'] else EYE
    if m['ball']:
        cc = Canvas()
        ball(cc, hx + 9, hy + 2)
        out.update(cc.px)
    return out


def crab(p, state, f):
    m = motion(state, f)
    c = Canvas()
    main = '#d8483a'
    light = '#f48a6a'
    by = GROUND - 6 + m['bob'] + (2 if m['lie'] else 0)
    bx = 16
    shift = round(math.sin(m['leg']) * m['stride'] / 2) if m['stride'] else 0
    if not m['lie']:
        for i in range(3):
            for s in (-1, 1):
                x = bx + s * (4 + i * 2)
                c.line(x, by + 2, x + s * 2 + (shift if i % 2 else -shift), GROUND, main)
    c.ellipse(bx, by, 7, 4, main)
    c.ellipse(bx, by - 1, 5, 2, light)
    raise_ = m['swipe']
    for s in (-1, 1):
        cx = bx + s * 9
        cy = by - 3 - (raise_ if s > 0 else 0) + (m['tail'] if s < 0 else 0)
        c.line(bx + s * 5, by, cx, cy + 2, main)
        c.ellipse(cx, cy, 2.5, 2, main)
        c.set(cx + s, cy - 2, main)
    for s in (-1, 1):
        c.rect(bx + s * 2, by - 6, bx + s * 2, by - 4, main)
    out = c.outlined()
    for s in (-1, 1):
        out[(bx + s * 2, by - 7)] = OUTLINE if m['blink'] else '#ffffff'
        out[(bx + s * 2, by - 6)] = EYE
    if m['ball']:
        cc = Canvas()
        ball(cc, bx + 9, by - 7)
        out.update(cc.px)
    return out


def snail(p, state, f):
    m = motion(state, f)
    c = Canvas()
    body = '#d8c8a8'
    stretch = (0, 1, 2, 1)[f] if m['stride'] else 0
    c.rect(6, GROUND - 2, 24 + stretch, GROUND, body)
    c.ellipse(24 + stretch, GROUND - 3, 2.5, 3, body)
    c.line(24 + stretch, GROUND - 6, 25 + stretch, GROUND - 9 + m['tail'] * 0, body)
    c.line(26 + stretch, GROUND - 5, 28 + stretch, GROUND - 8, body)
    shell = '#a0643a'
    c.ellipse(14, GROUND - 7 + m['bob'], 7, 6, shell)
    c.ellipse(14, GROUND - 7 + m['bob'], 4, 3.5, '#c88a52')
    c.ellipse(14, GROUND - 7 + m['bob'], 1.5, 1.5, shell)
    out = c.outlined()
    out[(25 + stretch, GROUND - 10)] = EYE
    out[(28 + stretch, GROUND - 9)] = EYE
    if m['ball']:
        cc = Canvas()
        ball(cc, 29, GROUND - 3)
        out.update(cc.px)
    return out


def snake(p, state, f):
    m = motion(state, f)
    c = Canvas()
    main, light = '#4f9a4a', '#bfe39a'
    amp = 2 if m['stride'] or state == 'idle' else 1
    speed = m['leg'] if m['stride'] else f * math.pi / 2
    pts = []
    for i in range(22):
        x = 4 + i
        y = GROUND - 2 - round(math.sin(i * 0.5 - speed) * amp * (i / 22))
        pts.append((x, y))
    for (x, y) in pts:
        c.rect(x, y - 1, x, y + 1, main)
        c.set(x, y + 1, light)
    hx, hy = pts[-1][0] + 2, pts[-1][1] - 2 - (m['swipe'] // 2)
    c.ellipse(hx, hy, 3, 2.5, main)
    out = c.outlined()
    out[(hx + 1, hy - 1)] = OUTLINE if m['blink'] else EYE
    if f % 2 == 0 and not m['ball']:
        out[(hx + 4, hy + 1)] = '#d83a3a'
        out[(hx + 5, hy + 1)] = '#d83a3a'
    if m['ball']:
        cc = Canvas()
        ball(cc, hx + 5, hy + 1)
        out.update(cc.px)
    return out


def turtle(p, state, f):
    m = motion(state, f)
    c = Canvas()
    shell = '#4f9a4a' if p['name'] != 'orange' else '#d9782c'
    skin = '#9ccf6a' if p['name'] != 'orange' else '#f2b860'
    by = GROUND - 7 + m['bob'] + (2 if m['lie'] else 0)
    if not m['lie']:
        legs4(c, [9, 11, 18, 20], by + 4, m, skin, 3)
    hx = 25 + (0 if not m['lie'] else -3)
    c.ellipse(hx, by + 3 - m['swipe'] // 2, 3, 2.5, skin)
    c.ellipse(15, by + 1, 9, 6, shell)
    c.rect(6, by + 4, 24, by + 5, shade(shell, -0.25))
    for x, y in ((12, by - 1), (18, by - 1), (15, by + 2)):
        c.ellipse(x, y, 1.5, 1.2, shade(shell, 0.25))
    out = c.outlined()
    out[(hx + 1, by + 2 - m['swipe'] // 2)] = OUTLINE if m['blink'] else EYE
    if m['ball']:
        cc = Canvas()
        ball(cc, hx + 4, by + 3)
        out.update(cc.px)
    return out


def skeleton(p, state, f):
    m = motion(state, f)
    c = Canvas()
    bone = '#f2efe6'
    accent = BASE.get(p['name'], BASE['white'])[0]
    by = GROUND - 15 + m['bob'] + (6 if m['lie'] else 0)
    x = 15
    # 脚
    if not m['lie']:
        for i, s in enumerate((-1, 1)):
            phase = m['leg'] + i * math.pi
            dx = round(math.sin(phase) * m['stride'] / 2) if m['stride'] else 0
            c.line(x + s, by + 9, x + s * 2 + dx, GROUND, bone)
    # 背骨とあばら
    c.rect(x, by + 3, x, by + 9, bone)
    for i in range(3):
        c.rect(x - 3, by + 4 + i * 2, x + 3, by + 4 + i * 2, bone)
    c.rect(x - 2, by + 9, x + 2, by + 9, bone)
    # 腕
    sw = m['swipe']
    c.line(x - 3, by + 4, x - 5, by + 9 + (m['tail'] if m['stride'] else 0), bone)
    c.line(x + 3, by + 4, x + 5 + sw, by + 9 - sw * 2, bone)
    # 頭(色はスカーフで表す)
    c.ellipse(x + 1, by - 1, 4, 3.5, bone)
    c.rect(x - 1, by + 2, x + 3, by + 3, bone)
    c.rect(x - 3, by + 3, x + 3, by + 3, accent)
    if p['name'] == 'warrior':
        c.rect(x - 3, by - 5, x + 5, by - 3, '#9aa3b0')
        c.set(x + 1, by - 6, '#d83a3a')
    out = c.outlined()
    eye = OUTLINE
    out[(x + 2, by - 1)] = eye
    out[(x + 4, by - 1)] = eye if not m['blink'] else bone
    out[(x + 3, by + 2)] = eye
    if m['ball']:
        cc = Canvas()
        ball(cc, x + 7, by + 6)
        out.update(cc.px)
    return out


def draw(pet, color, state, f):
    p = palette_for(color)
    if pet in ('dog', 'fox', 'horse', 'panda', 'raccoon', 'rat', 'monkey'):
        return quadruped(p, state, f, pet)
    if pet in ('chicken', 'cockatiel', 'rubber-duck'):
        return bird(p, state, f, pet)
    return {'crab': crab, 'snail': snail, 'snake': snake, 'turtle': turtle, 'skeleton': skeleton}[pet](p, state, f)


# ---------------------------------------------------------------- 書き出し


def to_rgba(px, scale):
    im = Image.new('RGBA', (G * scale, G * scale), (0, 0, 0, 0))
    for (x, y), c in px.items():
        # 目やボールが枠の外にはみ出した分は描かない
        if c is None or not (0 <= x < G and 0 <= y < G):
            continue
        h = c.lstrip('#')
        rgb = tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))
        for dy in range(scale):
            for dx in range(scale):
                im.putpixel((x * scale + dx, y * scale + dy), rgb + (255,))
    return im


def save_gif(frames, path, duration=130):
    # 透明色を1つ決めてパレット化する(GIF は1色だけ透明にできる)
    key = (1, 254, 3)
    pal_frames = []
    for im in frames:
        bg = Image.new('RGB', im.size, key)
        bg.paste(im, mask=im.split()[3])
        pal_frames.append(bg)
    colors = set()
    for im in pal_frames:
        colors.update(c for _, c in im.getcolors(1 << 16))
    colors.discard(key)
    order = [key] + sorted(colors)
    palette = [v for c in order for v in c]
    palette += [0] * (768 - len(palette))
    out = []
    lut = {c: i for i, c in enumerate(order)}
    for im in pal_frames:
        p = Image.new('P', im.size)
        p.putpalette(palette)
        p.putdata([lut[c] for c in im.getdata()])
        out.append(p)
    out[0].save(path, save_all=True, append_images=out[1:], duration=duration, loop=0, transparency=0, disposal=2, optimize=False)


PETS = {
    'dog': ['akita', 'black', 'brown', 'red', 'white'],
    'fox': ['red', 'white'],
    'chicken': ['brown', 'white'],
    'cockatiel': ['brown', 'gray'],
    'crab': ['red'],
    'horse': ['black', 'brown', 'magical', 'paint_beige', 'paint_black', 'paint_brown', 'socks_beige', 'socks_black', 'socks_brown', 'warrior', 'white'],
    'monkey': ['gray'],
    'panda': ['black', 'brown'],
    'raccoon': ['gray', 'gray_jimothy'],
    'rat': ['brown', 'gray', 'white'],
    'rubber-duck': ['yellow'],
    'skeleton': ['blue', 'brown', 'green', 'orange', 'pink', 'purple', 'red', 'warrior', 'white', 'yellow'],
    'snail': ['brown'],
    'snake': ['green'],
    'turtle': ['green', 'orange'],
}
STATES = {
    'dog': ['idle', 'lie', 'run', 'swipe', 'walk', 'walk_fast', 'with_ball'],
    'fox': ['idle', 'lie', 'run', 'swipe', 'walk', 'walk_fast', 'with_ball'],
    'panda': ['idle', 'lie', 'run', 'swipe', 'walk', 'walk_fast', 'with_ball'],
    'raccoon': ['idle', 'lie', 'run', 'swipe', 'walk', 'walk_fast', 'with_ball'],
    'turtle': ['idle', 'lie', 'run', 'swipe', 'walk', 'walk_fast', 'with_ball'],
    'horse': ['idle', 'run', 'stand', 'swipe', 'walk', 'walk_fast', 'with_ball'],
    'skeleton': ['idle', 'run', 'stand', 'swipe', 'walk', 'with_ball'],
    'monkey': ['idle', 'run', 'swipe', 'walk', 'with_ball'],
}
DEFAULT_STATES = ['idle', 'run', 'swipe', 'walk', 'walk_fast', 'with_ball']


def generate_pets(media):
    count = 0
    for pet, colors in PETS.items():
        folder = os.path.join(media, pet)
        os.makedirs(folder, exist_ok=True)
        for color in colors:
            for state in STATES.get(pet, DEFAULT_STATES):
                frames = [to_rgba(draw(pet, color, state, f), SCALE) for f in range(FRAMES)]
                save_gif(frames, os.path.join(folder, f'{color}_{state}_8fps.gif'), 120 if state == 'run' else 140)
                count += 1
            icon = to_rgba(draw(pet, color, 'idle', 0), 1)
            icon.save(os.path.join(folder, f'icon_{color}.png'))
            if color == colors[0]:
                icon.save(os.path.join(folder, 'icon.png'))
        if pet == 'cockatiel':
            # 上流のコードがこの名前のアイコンも探す
            to_rgba(draw(pet, 'brown', 'idle', 0), 1).save(os.path.join(folder, 'brown_icon.png'))
    return count


def hexrgb(h, a=255):
    h = h.lstrip('#')
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def generate_extension_icons(root):
    # 拡張機能のアイコン(128x128)とペット一覧のサムネイル
    icon = to_rgba(draw('dog', 'brown', 'idle', 0), 4)
    canvas = Image.new('RGBA', (128, 128), hexrgb('#5c7aea'))
    canvas.alpha_composite(icon)
    canvas.save(os.path.join(root, 'icon.png'))


if __name__ == '__main__':
    media = sys.argv[1]
    print('pets', generate_pets(media))
    generate_extension_icons(os.path.dirname(os.path.abspath(media)))
