using System;
using System.Drawing;

namespace othelloForWpf
{
    /// <summary>
    /// オセロ盤面をSystem.Drawing.Bitmap(Canvas)に描画するクラス。
    /// WinForms版はPictureBoxに直接描いていたが、WPF版ではPictureBoxが無いため
    /// 「描画先のサイズ(ピクセル)」だけを受け取り、自前のBitmapに描く形にした。
    /// 描画内容(色・線幅・マス目の計算式)はWinForms版と同じ。
    /// 画面への表示はMainWindowがCanvasをBitmapSourceに変換して行う(BoardImage.ToBitmapSource)。
    /// </summary>
    class Draw
    {
        private readonly int CellMax = 8;
        //private readonly int EdgeOffset = 3;
        private readonly int EdgeOffset = 0;
        private readonly Color LineColor = Color.Black;
        private readonly int LineWidth = 2;

        // 描画先のサイズ(ピクセル)。WinForms版のpb.Width/pb.Height相当。
        private int areaWidth;
        private int areaHeight;

        /// <summary>
        /// 現在描画中のBitmap。WinForms版のpictureBox.Image相当。
        /// CreateCanvasが無効サイズでスキップされた場合やDeleteCanvas後はnull。
        /// </summary>
        public Bitmap Canvas { get; private set; }

        public int Width => areaWidth;
        public int Height => areaHeight;

        public Draw()
        {
        }

        public Draw(int width, int height)
        {
            SetDrawArea(width, height);
        }

        /// <summary>
        /// 描画先のサイズ(ピクセル)を設定し、そのサイズのCanvasを作り直す。
        /// </summary>
        public void SetDrawArea(int width, int height)
        {
            areaWidth = width;
            areaHeight = height;
            CreateCanvas();
        }

        public void CreateCanvas()
        {
            // リサイズ中の一瞬(最小化直後など)は幅・高さが0以下になることがある。
            // その場合はBitmapが作れないので、既存のCanvasを残したまま何もしない。
            if (areaWidth <= 0 || areaHeight <= 0)
            {
                return;
            }

            Bitmap oldCanvas = Canvas;
            Canvas = new Bitmap(areaWidth, areaHeight);
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
        /// 現在描画中のBitmapそのもの(コピーではなく参照)を返す。
        /// 描画メソッドはこれを直接書き換えることで、毎回盤面全体をコピーする
        /// 無駄なコストを避けている。
        /// </summary>
        private Bitmap GetCanvas()
        {
            return Canvas;
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
            int width = areaWidth - (EdgeOffset * 2);
            int height = areaHeight - (EdgeOffset * 2);

            // 縦線
            for (int i = 0; i <= CellMax; i++)
            {
                int x = EdgeOffset + (width * i / CellMax);
                Point MovePt = new Point(x, EdgeOffset);
                Point LinePt = new Point(x, areaHeight - EdgeOffset);
                WriteLine(MovePt, LinePt, LineColor, LineWidth);
            }

            // 横線
            for (int i = 0; i <= CellMax; i++)
            {
                int y = EdgeOffset + (height * i / CellMax);
                Point MovePt = new Point(EdgeOffset, y);
                Point LinePt = new Point(areaWidth - EdgeOffset, y);
                WriteLine(MovePt, LinePt, LineColor, LineWidth);
            }
        }

        /// <summary>
        /// マス目座標cellのX方向の範囲(左端・右端のピクセル座標)を返す。
        /// 罫線の描画(InitField)と同じ「掛け算してから割る」計算式を使うことで、
        /// 罫線と石の位置がどのウィンドウサイズでもぴったり一致するようにしている。
        /// </summary>
        private void GetCellRangeX(int cellX, out int left, out int right)
        {
            int width = areaWidth - (EdgeOffset * 2);
            left = EdgeOffset + (width * cellX / CellMax);
            right = EdgeOffset + (width * (cellX + 1) / CellMax);
        }

        private void GetCellRangeY(int cellY, out int top, out int bottom)
        {
            int height = areaHeight - (EdgeOffset * 2);
            top = EdgeOffset + (height * cellY / CellMax);
            bottom = EdgeOffset + (height * (cellY + 1) / CellMax);
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

            for (int y = 0; y < CellMax; y++)
            {
                for (int x = 0; x < CellMax; x++)
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

            using (Graphics g = Graphics.FromImage(GetCanvas()))
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
            int margin = Math.Max(2, LineWidth);

            Rectangle rect = new Rectangle(
                left + margin,
                top + margin,
                (right - left) - margin * 2,
                (bottom - top) - margin * 2);

            Brush brush = color == StoneColor.Black ? Brushes.Black : Brushes.White;

            using (Graphics g = Graphics.FromImage(GetCanvas()))
            {
                g.FillEllipse(brush, rect);
            }
        }

        public void FillBackground(Brush color)
        {
            Rectangle rect = new Rectangle(0, 0, areaWidth, areaHeight);

            using (Graphics g = Graphics.FromImage(GetCanvas()))
            {
                g.FillRectangle(color, rect);
            }
        }

        public void WriteLine(Point MovePt, Point LinePt, Color clr, int LineWidth)
        {
            using (Graphics g = Graphics.FromImage(GetCanvas()))
            using (Pen pen = new Pen(clr, LineWidth))
            {
                g.DrawLine(pen, MovePt, LinePt);
            }
        }
    }
}
