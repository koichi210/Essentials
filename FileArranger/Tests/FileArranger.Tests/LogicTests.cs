using System;
using System.Windows.Controls;
using StandardTemplate.Wpf.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FileArranger.Tests
{
    /// <summary>
    /// Logic（Form1.cs から切り出した純粋ロジック）のテスト。
    /// 抽出前と挙動が変わっていないことを、抽出後のコードに対して確認する。
    /// </summary>
    [TestClass]
    public class LogicTests
    {
        // ------------------------------------------------------------------
        // GetPaddingDigits / ToPaddedNumberString
        // ------------------------------------------------------------------

        [TestMethod]
        public void GetPaddingDigits_1桁でも2桁ゼロ埋めにする()
        {
            Assert.AreEqual(2, Logic.GetPaddingDigits(5));
        }

        [TestMethod]
        public void GetPaddingDigits_2桁ちょうどでも2桁のまま()
        {
            Assert.AreEqual(2, Logic.GetPaddingDigits(42));
        }

        [TestMethod]
        public void GetPaddingDigits_3桁以上ならパディング不要()
        {
            Assert.AreEqual(0, Logic.GetPaddingDigits(100));
        }

        [TestMethod]
        public void GetPaddingDigits_isZeroDigitForZeroがtrueで値が0ならパディング0()
        {
            Assert.AreEqual(0, Logic.GetPaddingDigits(0, true));
        }

        [TestMethod]
        public void GetPaddingDigits_isZeroDigitForZeroがfalseなら0でも2桁扱い()
        {
            Assert.AreEqual(2, Logic.GetPaddingDigits(0, false));
        }

        [TestMethod]
        public void ToPaddedNumberString_加算数を足してゼロ埋めした文字列になる()
        {
            Assert.AreEqual("07", Logic.ToPaddedNumberString(5, 2));
        }

        [TestMethod]
        public void ToPaddedNumberString_加算数省略時は0扱い()
        {
            Assert.AreEqual("05", Logic.ToPaddedNumberString(5));
        }

        [TestMethod]
        public void ToPaddedNumberString_3桁以上になったらゼロ埋めしない()
        {
            Assert.AreEqual("150", Logic.ToPaddedNumberString(100, 50));
        }

        // ------------------------------------------------------------------
        // ChangeWide2Narrow
        // ------------------------------------------------------------------

        [TestMethod]
        public void ChangeWide2Narrow_全角数字を半角にする()
        {
            Assert.AreEqual("123", Logic.ChangeWide2Narrow("１２３"));
        }

        [TestMethod]
        public void ChangeWide2Narrow_全角英字を半角にする()
        {
            Assert.AreEqual("ABCabc", Logic.ChangeWide2Narrow("ＡＢＣａｂｃ"));
        }

        [TestMethod]
        public void ChangeWide2Narrow_全角スペースを半角にする()
        {
            Assert.AreEqual("a b", Logic.ChangeWide2Narrow("a　b"));
        }

        [TestMethod]
        public void ChangeWide2Narrow_日本語や記号は変換しない()
        {
            Assert.AreEqual("あいう123", Logic.ChangeWide2Narrow("あいう１２３"));
        }

        [TestMethod]
        public void ChangeWide2Narrow_変換対象が無ければそのまま()
        {
            Assert.AreEqual("abc123", Logic.ChangeWide2Narrow("abc123"));
        }

        // ------------------------------------------------------------------
        // GetAddCount
        // ------------------------------------------------------------------

        // WPFのListView(項目はListViewRow)を作る。WPFのコントロールはSTAスレッドでしか作れないので、
        // テスト本体はStaRunner.Runの中で実行する
        private static ListView NewListViewWithItems(params string[] texts)
        {
            var lv = new ListView { SelectionMode = SelectionMode.Extended };
            foreach (string t in texts)
            {
                lv.Items.Add(new ListViewRow(t));
            }
            return lv;
        }

        private static void Select(ListView lv, int index)
        {
            lv.SelectedItems.Add(lv.Items[index]);
        }

        [TestMethod]
        public void GetAddCount_一致する選択項目の数を返す()
        {
            StaRunner.Run(() =>
            {
                ListView lv = NewListViewWithItems("photo_1.jpg", "photo_2.jpg", "other.jpg");
                Select(lv, 0);
                Select(lv, 1);
                Select(lv, 2);

                int count = Logic.GetAddCount(lv, "photo_3.jpg", "_");

                Assert.AreEqual(2, count, "\"photo\" で始まる2件が一致するはず");
            });
        }

        [TestMethod]
        public void GetAddCount_一致が無ければ新規追加の初期値1になる()
        {
            StaRunner.Run(() =>
            {
                ListView lv = NewListViewWithItems("other.jpg");
                Select(lv, 0);

                int count = Logic.GetAddCount(lv, "photo_1.jpg", "_");

                Assert.AreEqual(1, count);
            });
        }

        [TestMethod]
        public void GetAddCount_選択していない項目は数えない()
        {
            StaRunner.Run(() =>
            {
                ListView lv = NewListViewWithItems("photo_1.jpg", "photo_2.jpg");
                Select(lv, 0);
                // photo_2.jpg は選択していない

                int count = Logic.GetAddCount(lv, "photo_3.jpg", "_");

                Assert.AreEqual(1, count);
            });
        }

        [TestMethod]
        public void GetAddCount_Reverse指定で末尾の区切りから探す()
        {
            StaRunner.Run(() =>
            {
                ListView lv = NewListViewWithItems("a_b_photo", "x_y_photo");
                Select(lv, 0);
                Select(lv, 1);

                int count = Logic.GetAddCount(lv, "a_b_photo_new", "_", true);

                Assert.AreEqual(1, count, "末尾の区切りより前(a_b)が一致するのは1件目だけ");
            });
        }

        // ------------------------------------------------------------------
        // DeleteDuplicate
        // ------------------------------------------------------------------

        [TestMethod]
        public void DeleteDuplicate_既存の一覧に含まれるものを取り除く()
        {
            string[] legacy = { "apple_folder", "banana_folder" };
            string[] added = { "apple", "cherry" };

            Logic.DeleteDuplicate(legacy, ref added, "_");

            CollectionAssert.AreEqual(new[] { "cherry" }, added, "apple は既存に含まれるので除外される");
        }

        [TestMethod]
        public void DeleteDuplicate_重複が無ければそのまま()
        {
            string[] legacy = { "apple_folder" };
            string[] added = { "cherry", "durian" };

            Logic.DeleteDuplicate(legacy, ref added, "_");

            CollectionAssert.AreEqual(new[] { "cherry", "durian" }, added);
        }

        [TestMethod]
        public void DeleteDuplicate_Delimiter引数は結果に影響しない()
        {
            // 元の実装で Delimiter 引数が使われていない挙動をそのまま引き継いでいる。
            string[] legacy = { "apple_folder" };
            string[] added1 = { "apple", "cherry" };
            string[] added2 = { "apple", "cherry" };

            Logic.DeleteDuplicate(legacy, ref added1, "_");
            Logic.DeleteDuplicate(legacy, ref added2, "###まったく違う区切り###");

            CollectionAssert.AreEqual(added1, added2);
        }

        [TestMethod]
        public void DeleteDuplicate_全部重複していれば空配列になる()
        {
            string[] legacy = { "apple_folder", "cherry_folder" };
            string[] added = { "apple", "cherry" };

            Logic.DeleteDuplicate(legacy, ref added, "_");

            Assert.AreEqual(0, added.Length);
        }

        [TestMethod]
        public void DeleteDuplicate_追加リストが空なら空配列のまま()
        {
            string[] legacy = { "apple_folder" };
            string[] added = new string[0];

            Logic.DeleteDuplicate(legacy, ref added, "_");

            Assert.AreEqual(0, added.Length);
        }

        [TestMethod]
        public void DeleteDuplicate_既存リストが空なら何も除外されない()
        {
            string[] legacy = new string[0];
            string[] added = { "apple", "cherry" };

            Logic.DeleteDuplicate(legacy, ref added, "_");

            CollectionAssert.AreEqual(new[] { "apple", "cherry" }, added);
        }

        [TestMethod]
        public void GetAddCount_名前の一覧を渡す版もListView版と同じ数え方をする()
        {
            string[] names = { "photo_1.jpg", "photo_2.jpg", "other.jpg" };

            Assert.AreEqual(2, Logic.GetAddCount(names, "photo_3.jpg", "_"));
            Assert.AreEqual(1, Logic.GetAddCount(names, "movie_1.mp4", "_"), "一致が無ければ初期値1");
            Assert.AreEqual(1, Logic.GetAddCount(new[] { "a_b_photo", "x_y_photo" }, "a_b_photo_new", "_", true));
        }
    }
}
