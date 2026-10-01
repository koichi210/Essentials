using System;
using System.Collections.Generic;
using System.Drawing;

namespace othelloForWpf
{
    /// <summary>
    /// 石の色(盤面の状態)
    /// </summary>
    public enum StoneColor
    {
        Unknown = 0,
        Black = 1,
        White = 2,
    }

    /// <summary>
    /// 棋譜の1手分。C++版 StrKihuTable(KihuTable配列の要素)相当。
    /// </summary>
    public struct KihuMove
    {
        public int X;
        public int Y;
        public StoneColor Color;
    }

    /// <summary>
    /// オセロの盤面状態とルール(石を置く・ひっくり返す・手番交代・終局判定)を管理するクラス。
    /// C++版 COthelloBase(othellobase.h/.cpp)のロジックを移植したもの。
    /// </summary>
    class GameMaster
    {
        public const int BoardSize = 8;

        // 盤面(0-indexed) [y, x]
        public StoneColor[,] Table { get; private set; }

        // 現在の手番
        public StoneColor CurrentTurn { get; private set; }

        // どちらも置けなくなり終局したかどうか
        public bool IsGameEnd { get; private set; }

        // これまでに置かれた石の総数(初期4石は含まない)。C++版 turn_cnt 相当。
        // COMの思考(序盤/中盤/終盤の判定、最初の2手はランダムにする等)に使う。
        public int TurnCount { get; private set; }

        // これまでに打たれた手の履歴(棋譜)。C++版 KihuTable 相当。
        public List<KihuMove> History { get; private set; }

        // 8方向(左, 右, 上, 下, 左上, 左下, 右上, 右下)
        private static readonly int[] DirX = { -1, 1, 0, 0, -1, -1, 1, 1 };
        private static readonly int[] DirY = { 0, 0, -1, 1, -1, 1, -1, 1 };

        public void Initialize()
        {
            ResetBoardOnly();
            History = new List<KihuMove>();
        }

        /// <summary>
        /// 盤面・手番・手数だけを初期状態に戻す(棋譜Historyはそのまま残す)。
        /// Undo/Redoで「最初から履歴をN手だけ再生し直す」ために使う。
        /// </summary>
        private void ResetBoardOnly()
        {
            Table = new StoneColor[BoardSize, BoardSize];
            Table[3, 3] = StoneColor.White;
            Table[3, 4] = StoneColor.Black;
            Table[4, 3] = StoneColor.Black;
            Table[4, 4] = StoneColor.White;

            CurrentTurn = StoneColor.Black;
            IsGameEnd = false;
            TurnCount = 0;
        }

        /// <summary>
        /// 現在の盤面状態をコピーした別のGameMasterを作る。
        /// COMの思考で「実際には置かず、仮に置いた場合どうなるか」を試すのに使う。
        /// </summary>
        public GameMaster Clone()
        {
            return new GameMaster
            {
                Table = (StoneColor[,])Table.Clone(),
                CurrentTurn = CurrentTurn,
                IsGameEnd = IsGameEnd,
                TurnCount = TurnCount,
                History = new List<KihuMove>(History),
            };
        }

        internal static StoneColor GetEnemyColor(StoneColor color)
        {
            return color == StoneColor.Black ? StoneColor.White : StoneColor.Black;
        }

        private static bool IsInBoard(int x, int y)
        {
            return x >= 0 && x < BoardSize && y >= 0 && y < BoardSize;
        }

        /// <summary>
        /// (x, y)にcolorを置けるか判定する。置ける場合、方向ごとにひっくり返せる石数をflipCountsに入れて返す。
        /// </summary>
        private bool CanPut(int x, int y, StoneColor color, int[] flipCounts)
        {
            if (!IsInBoard(x, y) || Table[y, x] != StoneColor.Unknown)
            {
                return false;
            }

            StoneColor enemy = GetEnemyColor(color);
            bool canPut = false;

            for (int dir = 0; dir < DirX.Length; dir++)
            {
                int cx = x + DirX[dir];
                int cy = y + DirY[dir];
                int count = 0;

                while (IsInBoard(cx, cy) && Table[cy, cx] == enemy)
                {
                    count++;
                    cx += DirX[dir];
                    cy += DirY[dir];
                }

                if (count > 0 && IsInBoard(cx, cy) && Table[cy, cx] == color)
                {
                    flipCounts[dir] = count;
                    canPut = true;
                }
            }

            return canPut;
        }

        /// <summary>
        /// colorがどこかに置けるか判定する
        /// </summary>
        private bool CanPutAny(StoneColor color)
        {
            int[] flipCounts = new int[DirX.Length];

            for (int y = 0; y < BoardSize; y++)
            {
                for (int x = 0; x < BoardSize; x++)
                {
                    Array.Clear(flipCounts, 0, flipCounts.Length);
                    if (CanPut(x, y, color, flipCounts))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// colorが置ける全マスをTableと同じ8x8のbool配列で返す(true=置ける)。
        /// 「置ける場所のマーク表示」用。C++版 DrawNotice/GetPutNotice 相当。
        /// </summary>
        public bool[,] GetValidMoves(StoneColor color)
        {
            bool[,] result = new bool[BoardSize, BoardSize];
            int[] flipCounts = new int[DirX.Length];

            for (int y = 0; y < BoardSize; y++)
            {
                for (int x = 0; x < BoardSize; x++)
                {
                    Array.Clear(flipCounts, 0, flipCounts.Length);
                    result[y, x] = CanPut(x, y, color, flipCounts);
                }
            }

            return result;
        }

        /// <summary>
        /// (x, y)にcolorを置いた場合に実際にはひっくり返さず、ひっくり返る石の座標だけを返す。
        /// COMの思考(開放度計算など)で「置いたと仮定した場合」を調べるのに使う。
        /// </summary>
        public IEnumerable<Point> GetFlippedPositions(int x, int y, StoneColor color)
        {
            int[] flipCounts = new int[DirX.Length];
            if (!CanPut(x, y, color, flipCounts))
            {
                yield break;
            }

            for (int dir = 0; dir < DirX.Length; dir++)
            {
                int cx = x;
                int cy = y;
                for (int i = 0; i < flipCounts[dir]; i++)
                {
                    cx += DirX[dir];
                    cy += DirY[dir];
                    yield return new Point(cx, cy);
                }
            }
        }

        /// <summary>
        /// (x, y)にcolorを置いた場合にひっくり返る石の総数を返す(置けない場合は0)。
        /// </summary>
        public int CountFlips(int x, int y, StoneColor color)
        {
            int count = 0;
            foreach (Point p in GetFlippedPositions(x, y, color))
            {
                count++;
            }
            return count;
        }

        /// <summary>
        /// (x, y)にCurrentTurnの石を置き、ひっくり返し、次の手番(パス・終局判定込み)に進める。
        /// 置けない場所を指定した場合は何もせずfalseを返す。
        /// Undoで過去に戻っている状態から新しい手を打った場合、そこから先の(やり直せたはずの)
        /// 棋譜は破棄される(C++版 UpDateKihu の turning_point = turn_cnt 相当)。
        /// </summary>
        public bool TryPut(int x, int y)
        {
            if (!ApplyMove(x, y))
            {
                return false;
            }

            if (History.Count > TurnCount)
            {
                History.RemoveRange(TurnCount, History.Count - TurnCount);
            }

            History.Add(new KihuMove { X = x, Y = y, Color = Table[y, x] });
            TurnCount++;
            return true;
        }

        /// <summary>
        /// (x, y)にCurrentTurnの石を置き、ひっくり返し、次の手番に進める(Historyへの記録はしない)。
        /// Undo/Redoで盤面を再構築する際に使う内部処理。
        /// </summary>
        private bool ApplyMove(int x, int y)
        {
            if (IsGameEnd)
            {
                return false;
            }

            int[] flipCounts = new int[DirX.Length];
            if (!CanPut(x, y, CurrentTurn, flipCounts))
            {
                return false;
            }

            Table[y, x] = CurrentTurn;

            for (int dir = 0; dir < DirX.Length; dir++)
            {
                int cx = x;
                int cy = y;
                for (int i = 0; i < flipCounts[dir]; i++)
                {
                    cx += DirX[dir];
                    cy += DirY[dir];
                    Table[cy, cx] = CurrentTurn;
                }
            }

            AdvanceTurn();
            return true;
        }

        // 過去に打った手をUndoでき、Redoでき、を判定する
        public bool CanUndo => TurnCount > 0;
        public bool CanRedo => TurnCount < History.Count;

        /// <summary>
        /// 直前の一手を取り消す(C++版 ReVersProc相当)。
        /// 実装は「最初から履歴を1手少なく再生し直す」方式で、ひっくり返した石を
        /// 逆算する処理を個別に持たずに済むようにしている。
        /// </summary>
        public bool Undo()
        {
            if (!CanUndo)
            {
                return false;
            }

            ReplayHistory(TurnCount - 1);
            return true;
        }

        /// <summary>
        /// Undoで戻した手をやり直す(C++版 VersProc相当)。
        /// </summary>
        public bool Redo()
        {
            if (!CanRedo)
            {
                return false;
            }

            ReplayHistory(TurnCount + 1);
            return true;
        }

        /// <summary>
        /// 盤面を初期状態に戻し、Historyの先頭からtargetCount手だけ再生する。
        /// </summary>
        private void ReplayHistory(int targetCount)
        {
            List<KihuMove> savedHistory = History;

            ResetBoardOnly();

            for (int i = 0; i < targetCount; i++)
            {
                ApplyMove(savedHistory[i].X, savedHistory[i].Y);
            }

            History = savedHistory;
            TurnCount = targetCount;
        }

        /// <summary>
        /// 手番交代。相手が置けなければパスして自分の手番のまま、
        /// どちらも置けなければ終局(IsGameEndがtrueになる)。
        /// </summary>
        private void AdvanceTurn()
        {
            StoneColor enemy = GetEnemyColor(CurrentTurn);

            if (CanPutAny(enemy))
            {
                CurrentTurn = enemy;
            }
            else if (CanPutAny(CurrentTurn))
            {
                // 相手はパス、自分の手番が続く
            }
            else
            {
                // どちらも置けない = 終局(CurrentTurnは維持)
                IsGameEnd = true;
            }
        }

        public void CountStones(out int blackCount, out int whiteCount)
        {
            blackCount = 0;
            whiteCount = 0;

            foreach (StoneColor stone in Table)
            {
                if (stone == StoneColor.Black)
                {
                    blackCount++;
                }
                else if (stone == StoneColor.White)
                {
                    whiteCount++;
                }
            }
        }
    }
}
