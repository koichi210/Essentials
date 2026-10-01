using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf;
using StandardTemplate.Wpf.Tests;

namespace FileArrangerForWpf.Tests
{
    /// <summary>
    /// WpfControlHelper(StcUtilsのWinFormsコントロール向けメソッドをWPF用にしたもの)のテスト。
    /// WinForms版と同じ結果になることを確認する。
    /// </summary>
    [TestClass]
    public class WpfControlHelperTests
    {
        private static object[] ItemsOf(ItemsControl ctrl)
        {
            return ctrl.Items.Cast<object>().ToArray();
        }

        [TestMethod]
        public void SetComboBoxFromArray_基準フォルダを取り除いて重複なく並べる()
        {
            StaRunner.Run(() =>
            {
                var combo = new ComboBoxEx();
                WpfControlHelper.SetComboBoxFromArray(combo, new[] { @"D:\ref\a", @"D:\ref\b", @"D:\ref\a", "" }, @"D:\ref");

                CollectionAssert.AreEqual(new object[] { "a", "b" }, ItemsOf(combo));
            });
        }

        [TestMethod]
        public void SetComboBoxFromArray_配列がnullなら空にする()
        {
            StaRunner.Run(() =>
            {
                var combo = new ComboBoxEx();
                combo.Items.Add("old");
                WpfControlHelper.SetComboBoxFromArray(combo, null, @"D:\ref");

                Assert.AreEqual(0, combo.Items.Count);
            });
        }

        [TestMethod]
        public void SetComboBoxFromArraySubString_入力中の文字で絞り込み入力中の文字は残す()
        {
            StaRunner.Run(() =>
            {
                var combo = new ComboBoxEx();
                combo.Text = "PH";
                WpfControlHelper.SetComboBoxFromArraySubString(combo,
                    new[] { @"D:\r\photo_01", @"D:\r\music_02", @"D:\r\graph_03" }, 5, "_", combo.Text, true);

                CollectionAssert.AreEqual(new object[] { "photo", "graph" }, ItemsOf(combo), "大文字小文字を区別せず部分一致");
                Assert.AreEqual("PH", combo.Text);
            });
        }

        [TestMethod]
        public void FindStringFromComboBox_区切り文字より前が部分一致する最初の項目を返す()
        {
            StaRunner.Run(() =>
            {
                var combo = new ComboBoxEx();
                combo.Items.Add("apple_01");
                combo.Items.Add("banana_05");

                Assert.AreEqual("banana_05", WpfControlHelper.FindStringFromComboBox(combo, "banana_new.jpg", "_", true));
                Assert.AreEqual("", WpfControlHelper.FindStringFromComboBox(combo, "cherry_1.jpg", "_", true));
            });
        }

        [TestMethod]
        public void ModifyComboBoxList_入力値を履歴に追加し重複は除く()
        {
            StaRunner.Run(() =>
            {
                var combo = new ComboBoxEx();
                combo.Items.Add("a");
                combo.Text = "b";
                WpfControlHelper.ModifyComboBoxList(combo);
                combo.Text = "a";
                WpfControlHelper.ModifyComboBoxList(combo);

                CollectionAssert.AreEqual(new object[] { "a", "b" }, ItemsOf(combo));
                Assert.AreEqual("a", combo.Text);
            });
        }

        [TestMethod]
        public void ModifyComboBoxList_入力値が空なら何もしない()
        {
            StaRunner.Run(() =>
            {
                var combo = new ComboBoxEx();
                combo.Items.Add("a");
                WpfControlHelper.ModifyComboBoxList(combo);

                CollectionAssert.AreEqual(new object[] { "a" }, ItemsOf(combo));
            });
        }

        [TestMethod]
        public void 昇順指定のコンボボックスは追加した項目が並び替わる()
        {
            // WinForms版のSorted=trueの代わりにSortedの目印を付けている
            StaRunner.Run(() =>
            {
                var combo = new ComboBoxEx();
                WpfControlHelper.SetSorted(combo, true);
                WpfControlHelper.SetItemsKeepText(combo, new[] { "c", "a", "b" });

                CollectionAssert.AreEqual(new object[] { "a", "b", "c" }, ItemsOf(combo));
            });
        }

        [TestMethod]
        public void GetSelectListName_選択項目を並び順で改行区切りにする()
        {
            StaRunner.Run(() =>
            {
                var lv = new ListView { SelectionMode = SelectionMode.Extended };
                lv.Items.Add(new ListViewRow("x", "1"));
                lv.Items.Add(new ListViewRow("y", "2"));
                lv.Items.Add(new ListViewRow("z", "3"));
                lv.SelectedItems.Add(lv.Items[2]);
                lv.SelectedItems.Add(lv.Items[0]);

                string text = WpfControlHelper.GetSelectListName(lv, item => ((ListViewRow)item)[0]);

                Assert.AreEqual("x" + Environment.NewLine + "z" + Environment.NewLine, text);
            });
        }

        [TestMethod]
        public void ListViewRowは列を書き換えると変更通知を出す()
        {
            var row = new ListViewRow("a", "");
            string changed = null;
            row.PropertyChanged += (s, e) => changed = e.PropertyName;

            row[1] = "b";

            Assert.AreEqual("Item[]", changed);
            Assert.AreEqual("b", row[1]);
            Assert.AreEqual("a", row.ToString());
        }
    }
}
