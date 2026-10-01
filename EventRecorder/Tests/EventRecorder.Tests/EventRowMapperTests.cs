using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventRecorder.Tests
{
    /// <summary>
    /// EventRowMapperのテスト(WinForms版EventRowMapperTestsの移植)。
    ///
    /// ⚠️WinForms版で2026-09-12に実際に踏んだ不具合の再発防止テスト: 記録処理側が列の物理的な並び順を
    /// 決め打ちで[Type, X, Y, Key, Wait]と対応させていたため、記録した値が別の列(Key列にX、Wait列にY等)に
    /// ずれて入り、再生前チェックで全行がエラー(ピンク表示)になった。
    /// WPF版は行クラス(EventRow)のプロパティで読み書きするが、配列の順序との対応が正しいことを同じ観点で確認する。
    /// </summary>
    [TestClass]
    public class EventRowMapperTests
    {
        [TestMethod]
        public void ApplyToRowは配列の順序どおり正しい列に値が入る()
        {
            EventRow row = new EventRow();

            // r = [Type, X, Y, Key, Wait]
            String[] r = { "LEFT_DOWN", "638", "503", "", "0" };
            EventRowMapper.ApplyToRow(row, r);

            Assert.AreEqual("LEFT_DOWN", row.Type, "Type列に値が入っていない");
            Assert.AreEqual("638", row.X, "X列にX座標が入っていない(Key列やWait列にずれていないか)");
            Assert.AreEqual("503", row.Y, "Y列にY座標が入っていない");
            Assert.AreEqual("", row.Key, "Key列が空であるべき(マウスイベントなので)");
            Assert.AreEqual("0", row.Wait, "Wait列に0が入っていない");
            Assert.AreEqual("X:638 Y:503", row.Detail, "Detail列の表示が実データから作られていない");
            Assert.IsFalse(row.HasDetailProblem, "正しく入っていればピンク表示にはならない");
        }

        [TestMethod]
        public void ApplyToRowはWAIT行でもWait列にだけ値が入る()
        {
            EventRow row = new EventRow();

            String[] r = { "WAIT", "", "", "", "500" };
            EventRowMapper.ApplyToRow(row, r);

            Assert.AreEqual("WAIT", row.Type);
            Assert.AreEqual("", row.X);
            Assert.AreEqual("", row.Y);
            Assert.AreEqual("", row.Key);
            Assert.AreEqual("500", row.Wait, "WAIT行の待機時間がWait列に入っていない(過去の不具合ではここが空になっていた)");
        }

        [TestMethod]
        public void ReadFromRowはApplyToRowで書いた内容を同じ順で読み戻せる()
        {
            EventRow row = new EventRow();

            String[] original = { "KEY_DOWN", "", "", "A", "120" };
            EventRowMapper.ApplyToRow(row, original);

            String[] roundTripped = EventRowMapper.ReadFromRow(row);

            CollectionAssert.AreEqual(original, roundTripped);
        }
    }
}
