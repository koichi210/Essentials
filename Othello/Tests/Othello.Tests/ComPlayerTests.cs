using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Othello.Tests
{
    /// <summary>
    /// ComPlayer(COMの思考ルーチン、C++版 CComProc の移植)のテスト。
    /// </summary>
    [TestClass]
    public class ComPlayerTests
    {
        [TestMethod]
        public void TryGetMoveは常に実際に置ける手を返す()
        {
            var gm = new GameMaster();
            gm.Initialize();

            for (int level = 1; level <= 3; level++)
            {
                bool result = ComPlayer.TryGetMove(gm, gm.CurrentTurn, level, out int x, out int y);

                Assert.IsTrue(result);
                bool[,] validMoves = gm.GetValidMoves(gm.CurrentTurn);
                Assert.IsTrue(validMoves[y, x], $"level={level}が返した({x},{y})は置ける手のはず");
            }
        }

        [TestMethod]
        public void 置ける手が無い場合はfalseを返す()
        {
            var gm = new GameMaster();
            gm.Initialize();
            // 盤面を全て黒で埋めると、白はどこにも置けない
            for (int y = 0; y < GameMaster.BoardSize; y++)
            {
                for (int x = 0; x < GameMaster.BoardSize; x++)
                {
                    gm.Table[y, x] = StoneColor.Black;
                }
            }

            bool result = ComPlayer.TryGetMove(gm, StoneColor.White, 1, out _, out _);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void 角に置けるなら安全な手より角を選ぶ()
        {
            var gm = new GameMaster();
            gm.Initialize();
            // 序盤2手はランダムなので、先に3手進めておく(黒→白→黒で白の番になる)
            for (int i = 0; i < 3; i++)
            {
                bool[,] moves = gm.GetValidMoves(gm.CurrentTurn);
                bool put = false;
                for (int y = 0; y < GameMaster.BoardSize && !put; y++)
                {
                    for (int x = 0; x < GameMaster.BoardSize && !put; x++)
                    {
                        put = moves[y, x] && gm.TryPut(x, y);
                    }
                }
            }
            Assert.AreEqual(StoneColor.White, gm.CurrentTurn);

            // 白の置ける場所は角(0,0)と、盤の中ほどの安全な手(3,4)の2か所だけにする
            for (int y = 0; y < GameMaster.BoardSize; y++)
            {
                for (int x = 0; x < GameMaster.BoardSize; x++)
                {
                    gm.Table[y, x] = StoneColor.Unknown;
                }
            }
            gm.Table[0, 1] = StoneColor.Black;
            gm.Table[0, 2] = StoneColor.White;
            gm.Table[4, 4] = StoneColor.Black;
            gm.Table[4, 5] = StoneColor.White;

            for (int level = 1; level <= 3; level++)
            {
                Assert.IsTrue(ComPlayer.TryGetMove(gm, StoneColor.White, level, out int x, out int y));
                Assert.AreEqual(0, x, $"level={level}");
                Assert.AreEqual(0, y, $"level={level}");
            }
        }

        [TestMethod]
        public void COMvsCOMで最後まで打つと例外なく終局する()
        {
            var gm = new GameMaster();
            gm.Initialize();

            int safetyCount = 0;
            while (!gm.IsGameEnd && safetyCount < 200)
            {
                bool moved = ComPlayer.TryGetMove(gm, gm.CurrentTurn, 3, out int x, out int y);
                Assert.IsTrue(moved, "終局していないのに置ける手が無いのはおかしい");
                Assert.IsTrue(gm.TryPut(x, y), "ComPlayerが返した手はTryPutで必ず成功するはず");
                safetyCount++;
            }

            Assert.IsTrue(gm.IsGameEnd);
            // 終局は必ずしも盤面が埋まりきるとは限らない(両者とも置けなくなれば途中でも終局する)
            gm.CountStones(out int black, out int white);
            Assert.IsTrue(black + white >= 4 && black + white <= 64);
        }
    }
}
