using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace othelloForWpf.Tests
{
    /// <summary>
    /// GameMaster（オセロの盤面状態とルールを管理するクラス）のテスト。
    /// C++版 COthelloBase から移植したロジック(石を置く・ひっくり返す・
    /// 手番交代・パス・終局判定)を検証する。
    /// </summary>
    [TestClass]
    public class GameMasterTests
    {
        [TestMethod]
        public void Initializeで初期4石と黒番がセットされる()
        {
            var gm = new GameMaster();
            gm.Initialize();

            Assert.AreEqual(StoneColor.White, gm.Table[3, 3]);
            Assert.AreEqual(StoneColor.Black, gm.Table[3, 4]);
            Assert.AreEqual(StoneColor.Black, gm.Table[4, 3]);
            Assert.AreEqual(StoneColor.White, gm.Table[4, 4]);
            Assert.AreEqual(StoneColor.Black, gm.CurrentTurn);
            Assert.IsFalse(gm.IsGameEnd);
        }

        [TestMethod]
        public void 有効な手を打つと石が置かれ挟んだ石がひっくり返る()
        {
            var gm = new GameMaster();
            gm.Initialize();

            // 黒番。(2,3)[0-indexed]に置くと(3,3)の白を挟んで黒にひっくり返る
            bool result = gm.TryPut(2, 3);

            Assert.IsTrue(result);
            Assert.AreEqual(StoneColor.Black, gm.Table[3, 2]);
            Assert.AreEqual(StoneColor.Black, gm.Table[3, 3]); // ひっくり返った石
            Assert.AreEqual(StoneColor.White, gm.CurrentTurn); // 手番交代
        }

        [TestMethod]
        public void 石が置けないマスに打つとfalseで盤面も変わらない()
        {
            var gm = new GameMaster();
            gm.Initialize();

            // 何も挟めない場所
            bool result = gm.TryPut(0, 0);

            Assert.IsFalse(result);
            Assert.AreEqual(StoneColor.Unknown, gm.Table[0, 0]);
            Assert.AreEqual(StoneColor.Black, gm.CurrentTurn); // 手番は変わらない
        }

        [TestMethod]
        public void 既に石があるマスには打てない()
        {
            var gm = new GameMaster();
            gm.Initialize();

            bool result = gm.TryPut(3, 3);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void CountStoneは初期状態で黒2白2を返す()
        {
            var gm = new GameMaster();
            gm.Initialize();

            gm.CountStone(out int black, out int white);

            Assert.AreEqual(2, black);
            Assert.AreEqual(2, white);
        }

        [TestMethod]
        public void 通常進行では黒と白の手番が交互に切り替わる()
        {
            var gm = new GameMaster();
            gm.Initialize();

            gm.TryPut(2, 3); // 黒の有効手 → 白番になるはず
            Assert.AreEqual(StoneColor.White, gm.CurrentTurn);

            gm.TryPut(2, 2); // 白の有効手 → 黒番になるはず
            Assert.AreEqual(StoneColor.Black, gm.CurrentTurn);
        }

        [TestMethod]
        public void GetValidMovesは初期状態で黒の4箇所を返す()
        {
            var gm = new GameMaster();
            gm.Initialize();

            bool[,] validMoves = gm.GetValidMoves(StoneColor.Black);

            // オセロの初期配置で黒が置ける定番の4箇所(0-indexed)
            Assert.IsTrue(validMoves[3, 2]);
            Assert.IsTrue(validMoves[2, 3]);
            Assert.IsTrue(validMoves[5, 4]);
            Assert.IsTrue(validMoves[4, 5]);

            int count = 0;
            foreach (bool v in validMoves)
            {
                if (v)
                {
                    count++;
                }
            }
            Assert.AreEqual(4, count);
        }

        [TestMethod]
        public void Undoで直前の手が取り消され盤面と手番が1手前に戻る()
        {
            var gm = new GameMaster();
            gm.Initialize();
            gm.TryPut(2, 3); // 黒 -> 白番、(3,3)がひっくり返る

            bool result = gm.Undo();

            Assert.IsTrue(result);
            Assert.AreEqual(StoneColor.Unknown, gm.Table[3, 2]); // 打った石が消える
            Assert.AreEqual(StoneColor.White, gm.Table[3, 3]); // ひっくり返しも元に戻る
            Assert.AreEqual(StoneColor.Black, gm.CurrentTurn); // 手番も戻る
            Assert.AreEqual(0, gm.TurnCount);
            Assert.IsFalse(gm.CanUndo);
            Assert.IsTrue(gm.CanRedo);
        }

        [TestMethod]
        public void RedoでUndoした手をやり直せる()
        {
            var gm = new GameMaster();
            gm.Initialize();
            gm.TryPut(2, 3);
            gm.Undo();

            bool result = gm.Redo();

            Assert.IsTrue(result);
            Assert.AreEqual(StoneColor.Black, gm.Table[3, 2]);
            Assert.AreEqual(StoneColor.Black, gm.Table[3, 3]);
            Assert.AreEqual(StoneColor.White, gm.CurrentTurn);
            Assert.IsFalse(gm.CanRedo);
        }

        [TestMethod]
        public void 手がない状態でのUndoRedoはfalseを返す()
        {
            var gm = new GameMaster();
            gm.Initialize();

            Assert.IsFalse(gm.Undo());
            Assert.IsFalse(gm.Redo());
        }

        [TestMethod]
        public void Undo後に新しい手を打つとRedoの分岐が破棄される()
        {
            var gm = new GameMaster();
            gm.Initialize();
            gm.TryPut(2, 3); // 黒
            gm.Undo();

            gm.TryPut(3, 2); // 黒(別の手)。Redoできたはずの分岐は消えるはず

            Assert.IsFalse(gm.CanRedo);
            Assert.AreEqual(1, gm.History.Count);
            Assert.AreEqual(3, gm.History[0].X);
            Assert.AreEqual(2, gm.History[0].Y);
        }
    }
}
