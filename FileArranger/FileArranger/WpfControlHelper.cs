// FileArranger専用のWPFコントロール用ヘルパー(ListViewRowを扱うもの・Logicに依存するもの)。
// どのアプリでも使える汎用の処理(選択項目の取得・クリップボード・Ctrl+A・コンボボックスの履歴など)は
// _Common/Wpf/WpfControlUtils.cs([[_Common/Wpf/WpfControlUtils.cs]])へ移した。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using StandardTemplate.Wpf;

namespace FileArranger
{
    internal static class WpfControlHelper
    {
        // 選択中の行(ListViewRow)を画面の並び順で返す
        public static List<ListViewRow> GetSelectedRows(ListView listCtrl)
        {
            return WpfControlUtils.GetSelectedItemsInIndexOrder(listCtrl).Cast<ListViewRow>().ToList();
        }

        // 指定した行の指定列にまとめて同じ文字列を入れる
        public static void SetColumnText(IEnumerable<ListViewRow> rows, String text, params int[] columnIdxes)
        {
            foreach (ListViewRow row in rows)
            {
                foreach (int columnIdx in columnIdxes)
                {
                    row[columnIdx] = text;
                }
            }
        }

        // コンボボックスの中から目的の文字列を探す(StcUtils.FindStringFromComboBox相当)
        public static String FindStringFromComboBox(ComboBox comboCtrl, String srcName, String trimName = "", Boolean isReverse = false)
        {
            // trimNameが設定されていたら、特定の文字列で区切る
            String searchName = Logic.TrimAtSeparator(srcName, trimName, isReverse);
            if (searchName.Length == 0)
            {
                return "";
            }
            return WpfControlUtils.GetItems(comboCtrl).FirstOrDefault(item => item.IndexOf(searchName) != -1) ?? "";
        }
    }
}
