using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate;

namespace FFEditForWpf.Tests
{
    /// <summary>
    /// ProcessMemory（元に戻す操作のためのスタック管理）のテスト。
    ///
    /// ファイル I/O を一切含まない純粋なロジックなので、実ファイルを使わずに検証できる。
    /// Rename / Function はどちらもこのクラスを継承し、実行のたびに
    /// IncrementSerialNumber、取り消し(Restore)のたびに DecrementSerialNumber を呼ぶ。
    /// "何回目の実行分の記録か" を SerialNumber で管理し、直前の実行分だけを取り消せるようにする
    /// 仕組みになっている。
    /// </summary>
    [TestClass]
    public class ProcessMemoryTests
    {
        private class TestMemory : StcProcessMemory
        {
        }

        [TestMethod]
        public void 何も登録していなければ取り消しリストは無い()
        {
            var pm = new TestMemory();

            Assert.IsFalse(pm.HasRestoreItem());
        }

        [TestMethod]
        public void 一度も実行していない状態でDecrementすると失敗する()
        {
            var pm = new TestMemory();

            Assert.IsFalse(pm.DecrementSerialNumber(), "CurrentIdx が 0 のときは false");
        }

        [TestMethod]
        public void 登録した直後は取り消しリストが存在する()
        {
            var pm = new TestMemory();

            pm.AddRestoreItem("a.txt", "b.txt");

            Assert.IsTrue(pm.HasRestoreItem());
        }

        [TestMethod]
        public void GetRestoreListは後から登録した順に取り出される()
        {
            var pm = new TestMemory();
            pm.AddRestoreItem("1_src", "1_dst");
            pm.AddRestoreItem("2_src", "2_dst");

            string src = "", dst = "";
            pm.PopRestoreItem(ref src, ref dst);
            Assert.AreEqual("2_src", src, "後から登録したものが先に返る(スタック)");
            Assert.AreEqual("2_dst", dst);

            pm.PopRestoreItem(ref src, ref dst);
            Assert.AreEqual("1_src", src);
            Assert.AreEqual("1_dst", dst);
        }

        [TestMethod]
        public void GetRestoreListは取り出すたびにリストから消える()
        {
            var pm = new TestMemory();
            pm.AddRestoreItem("a", "b");

            string src = "", dst = "";
            pm.PopRestoreItem(ref src, ref dst);

            Assert.IsFalse(pm.HasRestoreItem(), "取り出した分は無くなっているはず");
        }

        [TestMethod]
        public void 実行回ごとにIncrementしてから登録すると別の回として区別される()
        {
            // Rename.Execute / Function.Execute は実行の最後に IncrementSerialNumber を呼ぶ。
            // Restore はその前に DecrementSerialNumber してから、直前の回の分だけを戻す。
            var pm = new TestMemory();

            // 1回目の実行: 2件変更して Increment
            pm.AddRestoreItem("1a_src", "1a_dst");
            pm.AddRestoreItem("1b_src", "1b_dst");
            pm.IncrementSerialNumber();

            // 2回目の実行: 1件変更して Increment
            pm.AddRestoreItem("2a_src", "2a_dst");
            pm.IncrementSerialNumber();

            // Restore と同じ手順: Decrement してから、その回の分だけ取り出す
            Assert.IsTrue(pm.DecrementSerialNumber());
            string src = "", dst = "";
            Assert.IsTrue(pm.HasRestoreItem(), "2回目の実行分がまだ残っているはず");
            pm.PopRestoreItem(ref src, ref dst);
            Assert.AreEqual("2a_src", src, "2回目の実行分だけが取り出される");

            Assert.IsFalse(pm.HasRestoreItem(), "2回目の分は1件だけなのでもう無い");

            // さらに Restore すると1回目の実行分が取り出せる
            Assert.IsTrue(pm.DecrementSerialNumber());
            Assert.IsTrue(pm.HasRestoreItem());
            pm.PopRestoreItem(ref src, ref dst);
            Assert.AreEqual("1b_src", src, "1回目の実行分は後から登録した順(1b→1a)で戻る");
        }
    }
}
