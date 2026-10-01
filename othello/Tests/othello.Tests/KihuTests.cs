using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace othello.Tests
{
    /// <summary>
    /// Kihu(棋譜のテキスト変換・読込)のテスト。C++版 CManager::GetKihu/GetKihuFile 相当。
    /// </summary>
    [TestClass]
    public class KihuTests
    {
        [TestMethod]
        public void ToTextは打った手を1手ずつ列と行と色で出力する()
        {
            var gm = new GameMaster();
            gm.Initialize();
            gm.TryPut(2, 3); // 黒
            gm.TryPut(2, 2); // 白

            string text = Kihu.ToText(gm.History);

            StringAssert.Contains(text, "C.4 黒"); // (x=2,y=3) -> 列C(0=A,1=B,2=C), 行4(0-indexed3+1)
            StringAssert.Contains(text, "C.3 白"); // (x=2,y=2) -> 列C, 行3
        }

        [TestMethod]
        public void TryReplayは保存したテキストを再生して同じ盤面に戻せる()
        {
            var original = new GameMaster();
            original.Initialize();
            original.TryPut(2, 3);
            original.TryPut(2, 2);
            original.TryPut(4, 2);
            string text = Kihu.ToText(original.History);

            var replayed = new GameMaster();
            bool ok = Kihu.TryReplay(text, replayed, out string errorMessage);

            Assert.IsTrue(ok, errorMessage);
            Assert.AreEqual(original.CurrentTurn, replayed.CurrentTurn);
            Assert.AreEqual(original.History.Count, replayed.History.Count);
            for (int y = 0; y < GameMaster.BoardSize; y++)
            {
                for (int x = 0; x < GameMaster.BoardSize; x++)
                {
                    Assert.AreEqual(original.Table[y, x], replayed.Table[y, x], $"({x},{y})の石が一致しない");
                }
            }
        }

        [TestMethod]
        public void TryReplayは置けない手が出てきたらそこで打ち切りエラーを返す()
        {
            var gm = new GameMaster();
            string text = "***** 棋譜 *****\n 1 : A.1 黒\n";

            bool ok = Kihu.TryReplay(text, gm, out string errorMessage);

            Assert.IsFalse(ok);
            Assert.IsNotNull(errorMessage);
        }

        [TestMethod]
        public void TryReplayは無効な行を読み飛ばして有効な行だけ反映する()
        {
            var gm = new GameMaster();
            string text = "***** 棋譜 *****\nこれは無効な行\n 1 : C.4 黒\n";

            bool ok = Kihu.TryReplay(text, gm, out string errorMessage);

            Assert.IsTrue(ok, errorMessage);
            Assert.AreEqual(1, gm.History.Count);
        }
    }
}
