# 秘書さんスキン(SecretaryGadget の Skins\<色>\secretary.png)の胸から上を切り出し、
# 弾む・傾く・潰れる動きを付けて、ペット用の GIF とアイコンにする。1枚絵なので手足は動かない。
# 使い方: py -3 tools/generate_secretary.py <SecretaryGadget の Skins フォルダ> <media フォルダ>
import math
import os
import sys

from PIL import Image, ImageDraw

CANVAS = 96      # GIF の一辺
BODY = 80        # 胸像の一辺(キャンバス内)
SS = 3           # 縮小時のにじみを減らすための倍率
FRAMES = 8
STATES = ['idle', 'walk', 'walk_fast', 'run', 'swipe', 'with_ball']

# 切り出す正方形: (中心x, 上端y, 一辺) を元絵の幅・高さに対する割合で
CROPS = {
    'enji': (0.55, 0.00, 0.72),
    'blue': (0.50, 0.00, 0.66),
    'red': (0.50, 0.00, 0.68),
    'brown': (0.50, 0.00, 0.72),
    'green': (0.50, 0.00, 0.72),
}


def bust(path, crop):
    im = Image.open(path).convert('RGBA')
    cx, top, size = crop
    s = round(size * im.height)
    x0 = round(cx * im.width - s / 2)
    x0 = max(0, min(im.width - s, x0))
    y0 = round(top * im.height)
    sq = Image.new('RGBA', (s, s), (0, 0, 0, 0))
    sq.paste(im.crop((x0, y0, x0 + s, y0 + s)), (0, 0))
    return sq


def pose(sprite, dy, lean, sx, sy, ball=False):
    """胸像を (dy:持ち上げ px, lean:右への傾き 度, sx/sy:拡縮) で置いた CANVAS 四方の画像"""
    big = CANVAS * SS
    w, h = round(BODY * SS * sx), round(BODY * SS * sy)
    layer = Image.new('RGBA', (big, big), (0, 0, 0, 0))
    body = sprite.resize((w, h), Image.LANCZOS)
    foot = big - round(dy * SS)
    left = (big - w) // 2 - (CANVAS // 10) * SS if ball else (big - w) // 2
    layer.alpha_composite(body, (left, foot - h))
    layer = layer.rotate(-lean, resample=Image.BICUBIC, center=(left + w // 2, foot))
    if ball:
        d = ImageDraw.Draw(layer)
        r = 9 * SS
        bx, by = big - 20 * SS, big - r - SS
        d.ellipse((bx - r, by - r, bx + r, by + r), fill=(224, 72, 64, 255), outline=(60, 30, 40, 255), width=SS)
        d.arc((bx - r + 2 * SS, by - r + 2 * SS, bx + r - 2 * SS, by + r - 2 * SS), 200, 290, fill=(255, 220, 200, 255), width=SS)
    return layer.resize((CANVAS, CANVAS), Image.LANCZOS)


def motion(state, f):
    t = f / FRAMES
    s = math.sin(2 * math.pi * t)
    hop = abs(math.sin(2 * math.pi * t * 2))
    if state in ('idle', 'with_ball'):
        return dict(dy=0, lean=1.5 * s, sx=1 - 0.012 * s, sy=1 + 0.02 * s)
    if state == 'walk':
        return dict(dy=4 * hop, lean=4 * s, sx=1, sy=1)
    if state == 'walk_fast':
        return dict(dy=6 * hop, lean=6 * s, sx=1, sy=1)
    if state == 'run':
        return dict(dy=9 * hop, lean=8 + 3 * s, sx=1 + 0.04 * (1 - hop), sy=1 - 0.05 * (1 - hop))
    # swipe: 高く跳ねてくるっと傾く
    jump = math.sin(math.pi * t)
    return dict(dy=16 * jump, lean=18 * math.sin(4 * math.pi * t), sx=1, sy=1)


def save_gif(frames, path, duration):
    key = (1, 254, 3)
    pal = []
    for im in frames:
        a = im.split()[3].point(lambda v: 255 if v >= 150 else 0)
        bg = Image.new('RGB', im.size, key)
        bg.paste(im.convert('RGB'), mask=a)
        pal.append(bg.quantize(colors=255, method=Image.MEDIANCUT, dither=Image.NONE))
    # 全フレームで同じパレットにする(透明キー色を 0 番に)
    allim = Image.new('RGB', (CANVAS * len(frames), CANVAS), key)
    for i, im in enumerate(frames):
        a = im.split()[3].point(lambda v: 255 if v >= 150 else 0)
        allim.paste(im.convert('RGB'), (i * CANVAS, 0), a)
    q = allim.quantize(colors=255, method=Image.MEDIANCUT, dither=Image.NONE)
    palette = q.getpalette()[:768]
    kidx = q.getpixel((0, 0))
    out = []
    for i in range(len(frames)):
        p = q.crop((i * CANVAS, 0, (i + 1) * CANVAS, CANVAS))
        out.append(p)
    out[0].save(path, save_all=True, append_images=out[1:], duration=duration, loop=0,
                transparency=kidx, disposal=2, optimize=False)


def main(skins, media):
    folder = os.path.join(media, 'secretary')
    os.makedirs(folder, exist_ok=True)
    count = 0
    for color, crop in CROPS.items():
        sprite = bust(os.path.join(skins, color, 'secretary.png'), crop)
        for state in STATES:
            frames = [pose(sprite, ball=(state == 'with_ball'), **motion(state, f)) for f in range(FRAMES)]
            save_gif(frames, os.path.join(folder, f'{color}_{state}_8fps.gif'), 100 if state in ('run', 'walk_fast') else 125)
            count += 1
        icon = pose(sprite, 0, 0, 1, 1).resize((32, 32), Image.LANCZOS)
        icon.save(os.path.join(folder, f'icon_{color}.png'))
        if color == 'brown':
            icon.save(os.path.join(folder, 'icon.png'))
    print('secretary gifs', count)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
