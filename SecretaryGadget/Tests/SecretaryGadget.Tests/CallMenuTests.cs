using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SecretaryGadget.Tests
{
    /// <summary>選択肢の設定画面で保存する前のチェック。</summary>
    [TestClass]
    public class CallMenuTests
    {
        private static CallMenuEntry Row(string label, string path) => new CallMenuEntry { Label = label, Path = path };

        [TestMethod]
        public void FindProblem_NullWhenAllRowsAreFine()
        {
            Assert.IsNull(CallMenuEntry.FindProblem(new List<CallMenuEntry> { Row("本日の予定", "@schedule"), Row("箱庭", @"C:\tools\open.ps1"), Row("タイマー", " @Timer ") }));
            Assert.IsNull(CallMenuEntry.FindProblem(new List<CallMenuEntry>()));
        }

        [TestMethod]
        public void FindProblem_EmptyLabel()
        {
            var problem = CallMenuEntry.FindProblem(new List<CallMenuEntry> { Row("A", "@timer"), Row("  ", "x.exe") });
            Assert.AreEqual(1, problem!.Value.Index);
            StringAssert.Contains(problem.Value.Message, "2行目の表示名");
        }

        [TestMethod]
        public void FindProblem_EmptyPath()
        {
            var problem = CallMenuEntry.FindProblem(new List<CallMenuEntry> { Row("A", "") });
            Assert.AreEqual(0, problem!.Value.Index);
            StringAssert.Contains(problem.Value.Message, "起動するもの");
        }

        [TestMethod]
        public void FindProblem_UnknownBuiltin()
        {
            var problem = CallMenuEntry.FindProblem(new List<CallMenuEntry> { Row("A", "@hakoniwa") });
            StringAssert.Contains(problem!.Value.Message, "@hakoniwa");
        }

        [TestMethod]
        public void Serialize_DoesNotWriteComputedProperties()
        {
            string json = System.Text.Json.JsonSerializer.Serialize(Row("A", "@timer"));
            Assert.IsFalse(json.Contains("IsBuiltin"));
            Assert.IsFalse(json.Contains("BuiltinName"));
        }
    }
}
