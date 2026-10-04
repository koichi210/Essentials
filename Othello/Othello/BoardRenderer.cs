using System;
using System.Drawing;

namespace Othello
{
    /// <summary>
    /// オセロ盤面をSystem.Drawing.Bitmap(Canvas)に描画するクラス。
    /// WinForms版はPictureBoxに直接描いていたが、WPF版ではPictureBoxが無いため
    /// 「描画先のサイズ(ピクセル)」だけを受け取り、自前のBitmapに描く形にした。
    /// 描画内容(色・線幅・マス目の計算式)はWinForms版と同じ。
    /// 画面への表示はMainWindowがCanvasをBitmapSourceに変換して行う(BoardImage.ToBitmapSource)。
    /// </summary>
    class BoardRenderer
    {
        private const int _cellMax = GameMaster.BoardSize;
        private const int _edgeOffset = 0;
        private const int _lineWidth = 2;
        private static readonly Color _lineColor = Color.Black;

        /// <summary>
        /// 現在描画中のBitmap。WinForms版のpictureBox.Image相当。
        /// CreateCanvasが無効サイズでスキップされた場合やDeleteCanvas後はnull。
        /// 描画メソッドはこれを直接書き換える(毎回盤面全体をコピーするコストを避けるため)。
        /// </summary>
        public Bitmap Canvas { get; private set; }

        // 描画先のサイズ(ピクセル)。WinForms版のpb.Width/pb.Height相当。
        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>
        /// 描画先のサイズ(ピクセル)を設定し、そのサイズのCanvasを作り直す。
        /// </summary>
        public void SetDrawArea(int width, int height)
        {
            Width = width;
            Height = height;
            CreateCanvas();
        }

        public void CreateCanvas()
        {
            // リサイズ中の一瞬(最小化直後など)は幅・高さが0以下になることがある。
            // その場合はBitmapが作れないので、既存のCanvasを残したまま何もしない。
            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            Bitmap oldCanvas = Canvas;
            Canvas = new Bitmap(Width, Height);
            oldCanvas?.Dispose();
        }

        public void DeleteCanvas()
        {
            if (Canvas != null)
            {
                Canvas.Dispose();
                Canvas = null;
            }
        }

        /// <summary>
        /// 初期化
        /// </summary>
        public void InitField()
        {
            // Canvasが無い(CreateCanvasが無効サイズでスキップされた等)場合は描画できないので何もしない
            if (Canvas == null)
            {
                return;
            }

            // 基盤作成
            FillBackground(Brushes.Green);

            // 線描画
            // 注意: 「1マスの幅 = 全体 / 8」を先に整数で求めてから位置を計算すると、
            // 割り切れない端数が最後のマスだけに溜め込まれ、細長い9マス目のような
            // 見た目になってしまう。「i番目の線の位置 = 全体 * i / 8」の形で
            // 掛け算を先に行うことで、端数を各マスに均等に配分し、最後の線が
            // 必ず盤面のちょうど端に来るようにする。
            int width = Width - (_edgeOffset * 2);
            int height = Height - (_edgeOffset * 2);

            // 縦線
            for (int i = 0; i <= _cellMax; i++)
            {
                int x = _edgeOffset + (width * i / _cellMax);
                Point startPoint = new Point(x, _edgeOffset);
                Point endPoint = new Point(x, Height - _edgeOffset);
                DrawLine(startPoint, endPoint, _lineColor, _lineWidth);
            }

            // 横線
            for (int i = 0; i <= _cellMax; i++)
            {
                int y = _edgeOffset + (height * i / _cellMax);
                Point startPoint = new Point(_edgeOffset, y);
                Point endPoint = new Point(Width - _edgeOffset, y);
                DrawLine(startPoint, endPoint, _lineColor, _lineWidth);
            }
        }

        /// <summary>
        /// マス目座標cellのX方向の範囲(左端・右端のピクセル座標)を返す。
        /// 罫線の描画(InitField)と同じ「掛け算してから割る」計算式を使うことで、
        /// 罫線と石の位置がどのウィンドウサイズでもぴったり一致するようにしている。
        /// </summary>
        private void GetCellRangeX(int cellX, out int left, out int right)
        {
            int width = Width - (_edgeOffset * 2);
            left = _edgeOffset + (width * cellX / _cellMax);
            right = _edgeOffset + (width * (cellX + 1) / _cellMax);
        }

        private void GetCellRangeY(int cellY, out int top, out int bottom)
        {
            int height = Height - (_edgeOffset * 2);
            top = _edgeOffset + (height * cellY / _cellMax);
            bottom = _edgeOffset + (height * (cellY + 1) / _cellMax);
        }

        /// <summary>
        /// ピクセル座標(pixelX, pixelY)が盤面のどのマスに当たるかを返す。
        /// 罫線の描画と同じ考え方(「全体*i/8」)でマス目を求める。
        /// 盤面の外ならfalse。WinForms版Form1.pictureBoxField_MouseClickの計算と同じ。
        /// </summary>
        public static bool TryGetCell(int pixelX, int pixelY, int areaWidth, int areaHeight, out int cellX, out int cellY)
        {
            cellX = -1;
            cellY = -1;
            if (areaWidth <= 0 || areaHeight <= 0 || pixelX < 0 || pixelY < 0)
            {
                return false;
            }

            // 先に1マス分の幅を割ってしまうと、盤面のサイズによっては罫線とクリック判定の
            // マス目がわずかにズレることがあるため、掛け算してから割る。
            int x = pixelX * GameMaster.BoardSize / areaWidth;
            int y = pixelY * GameMaster.BoardSize / areaHeight;
            if (x < 0 || x >= GameMaster.BoardSize || y < 0 || y >= GameMaster.BoardSize)
            {
                return false;
            }

            cellX = x;
            cellY = y;
            return true;
        }

        /// <summary>
        /// 盤面(格子線)と全マスの石をまとめて描画する。
        /// validMovesを渡すと、石が無く置ける(true)マスに置ける場所のマークも描画する。
        /// C++版 DrawNotice 相当。
        /// </summary>
        public void DrawField(StoneColor[,] table, bool[,] validMoves = null)
        {
            InitField();

            for (int y = 0; y < _cellMax; y++)
            {
                for (int x = 0; x < _cellMax; x++)
                {
                    DrawStone(x, y, table[y, x]);

                    if (validMoves != null && validMoves[y, x])
                    {
                        DrawNotice(x, y);
                    }
                }
            }
        }

        /// <summary>
        /// マス目座標(cellX, cellY)に「置ける場所」の小さなマークを描画する。
        /// </summary>
        public void DrawNotice(int cellX, int cellY)
        {
            if (Canvas == null)
            {
                return;
            }

            GetCellRangeX(cellX, out int left, out int right);
            GetCellRangeY(cellY, out int top, out int bottom);

            int cellWidth = right - left;
            int cellHeight = bottom - top;
            int noticeWidth = Math.Max(2, cellWidth / 4);
            int noticeHeight = Math.Max(2, cellHeight / 4);

            Rectangle rect = new Rectangle(
                left + (cellWidth - noticeWidth) / 2,
                top + (cellHeight - noticeHeight) / 2,
                noticeWidth,
                noticeHeight);

            using (Graphics g = Graphics.FromImage(Canvas))
            using (Brush brush = new SolidBrush(Color.FromArgb(140, Color.DarkGray)))
            {
                g.FillEllipse(brush, rect);
            }
        }

        /// <summary>
        /// マス目座標(cellX, cellY)に石を描画する。colorがUnknownなら何もしない。
        /// </summary>
        public void DrawStone(int cellX, int cellY, StoneColor color)
        {
            if (color == StoneColor.Unknown || Canvas == null)
            {
                return;
            }

            GetCellRangeX(cellX, out int left, out int right);
            GetCellRangeY(cellY, out int top, out int bottom);
            int margin = Math.Max(2, _lineWidth);

            Rectangle rect = new Rectangle(
                left + margin,
                top + margin,
                (right - left) - margin * 2,
                (bottom - top) - margin * 2);

            Brush brush = color == StoneColor.Black ? Brushes.Black : Brushes.White;

            using (Graphics g = Graphics.FromImage(Canvas))
            {
                g.FillEllipse(brush, rect);
            }
        }

        public void FillBackground(Brush brush)
        {
            Rectangle rect = new Rectangle(0, 0, Width, Height);

            using (Graphics g = Graphics.FromImage(Canvas))
            {
                g.FillRectangle(brush, rect);
            }
        }

        public void DrawLine(Point startPoint, Point endPoint, Color color, int lineWidth)
        {
            using (Graphics g = Graphics.FromImage(Canvas))
            using (Pen pen = new Pen(color, lineWidth))
            {
                g.DrawLine(pen, startPoint, endPoint);
            }
        }
    }
}
