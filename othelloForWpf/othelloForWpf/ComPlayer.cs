using System;
using System.Collections.Generic;
using System.Drawing;

namespace othelloForWpf
{
    /// <summary>
    /// COM(コンピュータ)の思考ルーチン。C++版 CComProc(com.h/com.cpp)を移植したもの。
    ///
    /// レベル1〜3で「候補手をどんな基準で並べるか」が変わり(中心優先/ひっくり返す数/開放度)、
    /// 最終的にDetermineLocationで角・星・次星などのマス目の性質を見て1手に絞り込む。
    /// なお、C++版では「相手を全滅させられるか」の判定(AllSteal)がALLSTEAL_USE=0で
    /// 無効化された状態で使われていたため、このC#版でもその判定は行わない(常にfalse相当)。
    /// </summary>
    static class ComPlayer
    {
        private static readonly Random random = new Random();

        /// <summary>
        /// colorの次の一手を決める。置ける場所が無ければfalseを返す。
        /// </summary>
        public static bool TryGetMove(GameMaster gm, StoneColor color, int level, out int x, out int y)
        {
            x = -1;
            y = -1;

            List<Point> candidates = GetCandidates(gm, color);
            if (candidates.Count == 0)
            {
                return false;
            }

            // 序盤2手はランダム(C++版準拠: turn_cnt <= 2)
            if (gm.TurnCount <= 2)
            {
                Point picked = candidates[random.Next(candidates.Count)];
                x = picked.X;
                y = picked.Y;
                return true;
            }

            List<Point> sorted;
            switch (level)
            {
                case 1:
                    sorted = SortByCenter(candidates);
                    break;
                case 2:
                    sorted = SortByPhase(gm, color, candidates);
                    break;
                case 3:
                default:
                    sorted = SortByOpenness(gm, color, candidates);
                    break;
            }

            Point determined = DetermineLocation(gm, color, sorted);
            x = determined.X;
            y = determined.Y;
            return true;
        }

        private static List<Point> GetCandidates(GameMaster gm, StoneColor color)
        {
            bool[,] validMoves = gm.GetValidMoves(color);
            List<Point> list = new List<Point>();

            for (int cy = 0; cy < GameMaster.BoardSize; cy++)
            {
                for (int cx = 0; cx < GameMaster.BoardSize; cx++)
                {
                    if (validMoves[cy, cx])
                    {
                        list.Add(new Point(cx, cy));
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// レベル1: 盤の中心に近い順に並べる(C++版 Center)
        /// </summary>
        private static List<Point> SortByCenter(List<Point> candidates)
        {
            List<Point> sorted = new List<Point>(candidates);
            double centerX = (GameMaster.BoardSize - 1) / 2.0;
            double centerY = (GameMaster.BoardSize - 1) / 2.0;

            sorted.Sort((a, b) =>
            {
                double da = Math.Abs(a.X - centerX) + Math.Abs(a.Y - centerY);
                double db = Math.Abs(b.X - centerX) + Math.Abs(b.Y - centerY);
                return da.CompareTo(db);
            });

            return sorted;
        }

        /// <summary>
        /// レベル2: 序盤は中心優先、中盤はひっくり返す数が少ない順、終盤は多い順(C++版 GetMin/GetMax)
        /// </summary>
        private static List<Point> SortByPhase(GameMaster gm, StoneColor color, List<Point> candidates)
        {
            const int total = GameMaster.BoardSize * GameMaster.BoardSize; // 64
            int turnCount = gm.TurnCount;

            if (turnCount < total / 3)
            {
                return SortByCenter(candidates);
            }

            List<Point> sorted = new List<Point>(candidates);

            if (turnCount < total / 3 * 2)
            {
                // 中盤: なるべく少なくひっくり返す(石を序盤に固めすぎない)
                sorted.Sort((a, b) => gm.CountFlips(a.X, a.Y, color).CompareTo(gm.CountFlips(b.X, b.Y, color)));
            }
            else
            {
                // 終盤: なるべく多くひっくり返す
                sorted.Sort((a, b) => gm.CountFlips(b.X, b.Y, color).CompareTo(gm.CountFlips(a.X, a.Y, color)));
            }

            return sorted;
        }

        /// <summary>
        /// レベル3: 開放度(手を打った結果できる空きマスの少なさ)が低い順(C++版 GetKaiho)
        /// </summary>
        private static List<Point> SortByOpenness(GameMaster gm, StoneColor color, List<Point> candidates)
        {
            List<Point> sorted = new List<Point>(candidates);
            sorted.Sort((a, b) => CalcOpenness(gm, color, a).CompareTo(CalcOpenness(gm, color, b)));
            return sorted;
        }

        /// <summary>
        /// 開放度: (x,y)に置いた場合にひっくり返る石すべて(置いた石自身を含む)について、
        /// 周囲8マスの空きマス数を合計する。値が小さいほど相手に囲まれにくい「閉じた」手とされる。
        /// (C++版 Kaiho/KaihoSub)
        /// </summary>
        private static int CalcOpenness(GameMaster gm, StoneColor color, Point p)
        {
            int total = CountEmptyNeighbors(gm, p.X, p.Y);

            foreach (Point flipped in gm.GetFlippedPositions(p.X, p.Y, color))
            {
                total += CountEmptyNeighbors(gm, flipped.X, flipped.Y);
            }

            return total;
        }

        private static int CountEmptyNeighbors(GameMaster gm, int x, int y)
        {
            int count = 0;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx < 0 || nx >= GameMaster.BoardSize || ny < 0 || ny >= GameMaster.BoardSize)
                    {
                        continue;
                    }

                    if (gm.Table[ny, nx] == StoneColor.Unknown)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// 候補手(優先順にソート済み)から、角・星・次星などのマス目の性質を見て最終的に1手選ぶ。
        /// C++版 DetermineLocation を移植したもの:
        /// 「特に問題ない安全な手」を最優先、無ければ角、無ければ星、無ければ次星、
        /// 最後に「置くと敵に角を取られてしまう手」という優先順位。
        /// </summary>
        private static Point DetermineLocation(GameMaster gm, StoneColor color, List<Point> sortedCandidates)
        {
            Point? safe = null;
            Point? corner = null;
            Point? star = null;
            Point? nextStar = null;
            Point? stealCorner = null;

            foreach (Point p in sortedCandidates)
            {
                if (IsCorner(p))
                {
                    if (corner == null)
                    {
                        corner = p;
                    }
                }
                else if (WouldLetEnemyTakeCorner(gm, color, p))
                {
                    if (stealCorner == null)
                    {
                        stealCorner = p;
                    }
                }
                else if (IsStar(p))
                {
                    if (star == null)
                    {
                        star = p;
                    }
                }
                else if (IsStarNear(p))
                {
                    if (nextStar == null)
                    {
                        nextStar = p;
                    }
                }
                else
                {
                    if (safe == null)
                    {
                        safe = p;
                    }
                }
            }

            if (safe != null)
            {
                return safe.Value;
            }
            if (corner != null)
            {
                return corner.Value;
            }
            if (star != null)
            {
                return star.Value;
            }
            if (nextStar != null)
            {
                return nextStar.Value;
            }
            if (stealCorner != null)
            {
                return stealCorner.Value;
            }

            // 候補が1つ以上あれば必ずどれかに分類されるので、通常ここには来ない
            return sortedCandidates[0];
        }

        private static bool IsCorner(Point p)
        {
            int max = GameMaster.BoardSize - 1;
            return (p.X == 0 || p.X == max) && (p.Y == 0 || p.Y == max);
        }

        private static bool IsStar(Point p)
        {
            int max = GameMaster.BoardSize - 1;
            return (p.X == 1 || p.X == max - 1) && (p.Y == 1 || p.Y == max - 1);
        }

        private static bool IsStarNear(Point p)
        {
            int max = GameMaster.BoardSize - 1;
            bool edgeXcornerY = (p.X == 0 || p.X == max) && (p.Y == 1 || p.Y == max - 1);
            bool cornerXedgeY = (p.X == 1 || p.X == max - 1) && (p.Y == 0 || p.Y == max);
            return edgeXcornerY || cornerXedgeY;
        }

        /// <summary>
        /// ここに置いた場合、直後に敵が角を取れる状態になってしまうかどうか(C++版 StealCorner)。
        /// 実際の盤面には影響しないよう、複製した盤面で試す。
        /// </summary>
        private static bool WouldLetEnemyTakeCorner(GameMaster gm, StoneColor color, Point p)
        {
            GameMaster sim = gm.Clone();
            if (!sim.TryPut(p.X, p.Y))
            {
                return false;
            }

            StoneColor enemy = color == StoneColor.Black ? StoneColor.White : StoneColor.Black;
            bool[,] enemyMoves = sim.GetValidMoves(enemy);
            int max = GameMaster.BoardSize - 1;

            return enemyMoves[0, 0] || enemyMoves[0, max] || enemyMoves[max, 0] || enemyMoves[max, max];
        }
    }
}
