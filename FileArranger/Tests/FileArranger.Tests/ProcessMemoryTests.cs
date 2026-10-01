using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate;

namespace FileArranger.Tests
{
    /// <summary>
    /// ProcessMemory（元に戻す操作のためのスタック管理）のテスト。
    /// FFEdit / EventRecorder の同名クラスと同じ形。ファイル I/O を含まない
    /// 純粋なロジックなので、実ファイルを使わずに検証できる。
    /// </summary>
    [TestClass]
    public class ProcessMemoryTests
    {
        [TestMethod]
        public void 何も登録していなければ取り消しリストは無い()
        {
            var pm = new StcProcessMemory();

            Assert.IsFalse(pm.HasRestoreItem());
        }

        [TestMethod]
        public void 一度も実行していない状態でDecrementすると失敗する()
        {
            var pm = new StcProcessMemory();

            Assert.IsFalse(pm.DecrementSerialNumber());
        }

        [TestMethod]
        public void GetRestoreListは後から登録した順に取り出される()
        {
            var pm = new StcProcessMemory();
            pm.AddRestoreItem("1_src", "1_dst");
            pm.AddRestoreItem("2_src", "2_dst");

            string src = "", dst = "";
            pm.PopRestoreItem(ref src, ref dst);
            Assert.AreEqual("2_src", src);

            pm.PopRestoreItem(ref src, ref dst);
            Assert.AreEqual("1_src", src);
        }

        [TestMethod]
        public void 実行回ごとにIncrementしてから登録すると別の回として区別される()
        {
            var pm = new StcProcessMemory();

            pm.AddRestoreItem("1a_src", "1a_dst");
            pm.IncrementSerialNumber();

            pm.AddRestoreItem("2a_src", "2a_dst");
            pm.IncrementSerialNumber();

            Assert.IsTrue(pm.DecrementSerialNumber());
            string src = "", dst = "";
            pm.PopRestoreItem(ref src, ref dst);
            Assert.AreEqual("2a_src", src, "直近の実行分だけが取り出される");
            Assert.IsFalse(pm.HasRestoreItem(), "2回目の分は1件だけ");
        }
    }
}
