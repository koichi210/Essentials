# 四季の1枚絵(2x2)を春夏秋冬の背景4テーマに切り出す。
# 使い方: py -3 tools/cut_seasons.py <四季.png> <media フォルダ>
# 季節の境界(タイル間の白い線)で切り、絵は切り落とさない。ペットの背景は
# パネルの高さに合わせて全体を縮小表示するので、ここでは高さ800pxの1枚だけ作る。
import os
import sys

from PIL import Image

THEMES = ['spring', 'summer', 'autumn', 'winter']   # 左上, 右上, 左下, 右下
HEIGHT = 800
# 元絵のタイル境界(白い区切り線を除いた範囲。端の1pxは線とのにじみなので外す)
XS = [(0, 764), (771, 1536)]
YS = [(0, 508), (515, 1024)]


def main(src, media):
    im = Image.open(src).convert('RGB')
    assert im.size == (1536, 1024), im.size
    for i, theme in enumerate(THEMES):
        x0, x1 = XS[i % 2]
        y0, y1 = YS[i // 2]
        tile = im.crop((x0, y0, x1, y1))
        width = round(tile.width * HEIGHT / tile.height)
        folder = os.path.join(media, 'backgrounds', theme)
        os.makedirs(folder, exist_ok=True)
        tile.resize((width, HEIGHT), Image.LANCZOS).save(
            os.path.join(folder, 'background.jpg'), quality=88, optimize=True)
        print(theme, tile.size, '->', (width, HEIGHT))


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
