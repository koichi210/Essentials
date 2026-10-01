// StcUtils([[_Common/StandardTemplateClass.cs]])のうち、WinFormsのコントロールを引数に取るもの
// (SetComboBoxFromArray/SetComboBoxFromArraySubString/FindStringFromComboBox/AddComboBoxTextToItems/
//  CopyToClipboard/SelectAll)を、WPFのコントロールで使えるようにしたプロジェクト内ヘルパー。
// 中身はWinForms版と同じ処理にしてある(メソッド名もStcUtilsと同じにしてある)。
//
// ※他のWPF移植でも使えそうなので、_Common/Wpf へ移す候補
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FileArrangerForWpf
{
    internal static class WpfControlHelper
    {
        // ------------------------------------------------------------------
        // ListBox / ListView
        // ------------------------------------------------------------------

        // 選択項目を「画面の並び順(インデックス順)」で返す。
        // WPFのSelectedItemsは選択した順に並ぶが、WinFormsのSelectedItemsはインデックス順だったため、それに合わせる
        public static List<Object> GetSelectedItemsInIndexOrder(ListBox listCtrl)
        {
            return GetSelectedIndices(listCtrl).Select(index => listCtrl.Items[index]).ToList();
        }

        // 選択項目のインデックスを昇順で返す(WinFormsのSelectedItems[i].Index相当)
        public static List<int> GetSelectedIndices(ListBox listCtrl)
        {
            List<int> indices = new List<int>();
            foreach (Object item in listCtrl.SelectedItems)
            {
                int index = listCtrl.Items.IndexOf(item);
                if (index >= 0)
                {
                    indices.Add(index);
                }
            }
            indices.Sort();
            return indices;
        }

        // Ctrl+Aで全選択(WinForms版StcUtils.SelectAll(KeyEventArgs)はSendKeysで{HOME}+{END}を送っていた)
        public static void SelectAll(ListBox listCtrl, KeyEventArgs e)
        {
            if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                listCtrl.SelectAll();
                e.Handled = true;
            }
        }

        // 選択項目を「RootPath\項目名」の形で改行区切りに連結する(StcUtils.GetSelectListName相当)
        public static String GetSelectListName(ListBox listCtrl, Func<Object, String> getItemText, String rootPath = "")
        {
            StringBuilder targetName = new StringBuilder();
            foreach (Object item in GetSelectedItemsInIndexOrder(listCtrl))
            {
                if (rootPath != String.Empty)
                {
                    targetName.Append(rootPath).Append(@"\");
                }
                targetName.Append(getItemText(item)).Append(Environment.NewLine);
            }
            return targetName.ToString();
        }

        // Ctrl+Cで選択項目をコピー(StcUtils.CopyToClipboard(KeyEventArgs, ListView, RootPath, index)相当)
        public static Boolean CopyToClipboard(KeyEventArgs e, ListBox listCtrl, Func<Object, String> getItemText, String rootPath = "")
        {
            if (e.Key != Key.C || Keyboard.Modifiers != ModifierKeys.Control)
            {
                return false;
            }

            String targetName = GetSelectListName(listCtrl, getItemText, rootPath);
            if (targetName.Equals(String.Empty))
            {
                return false;
            }
            e.Handled = true;
            return SetClipboardText(targetName);
        }

        // クリップボードは他のアプリが掴んでいる間は開けず例外になるため、少し待って数回やり直す
        // (StcUtils.SetClipboardTextと同じ考え方。こちらはWPFのClipboardを使う)
        public static Boolean SetClipboardText(String text)
        {
            const int RetryCount = 5;
            const int RetryWaitMsec = 100;

            for (int i = 0; i < RetryCount; i++)
            {
                try
                {
                    if (String.IsNullOrEmpty(text))
                    {
                        Clipboard.Clear();
                    }
                    else
                    {
                        Clipboard.SetText(text);
                    }
                    return true;
                }
                catch (COMException)
                {
                    Thread.Sleep(RetryWaitMsec);
                }
            }
            return false;
        }

        // ------------------------------------------------------------------
        // ComboBox
        // ------------------------------------------------------------------

        // WinForms版ComboBoxのSorted=true相当の目印。WPFのItems.SortDescriptionsは、Items.Addした項目が
        // Refreshするまで並び替わらないため使わず、項目を入れ替えるときにこちらで並べ替える
        public static readonly DependencyProperty SortedProperty =
            DependencyProperty.RegisterAttached("Sorted", typeof(Boolean), typeof(WpfControlHelper), new PropertyMetadata(false));

        public static void SetSorted(ItemsControl ctrl, Boolean value)
        {
            ctrl.SetValue(SortedProperty, value);
        }

        public static Boolean GetSorted(ItemsControl ctrl)
        {
            return (Boolean)ctrl.GetValue(SortedProperty);
        }

        // 項目一覧を入れ替える。入力可能なComboBoxはItems.Clearで入力欄の文字が消えることがあるため、
        // 入力欄の文字(Text)は入れ替え前のまま残す(WinForms版ComboBox(DropDown)と同じ見え方にする)。
        // Sortedの目印が付いていれば昇順(WinForms版と同じくカルチャ依存の文字列比較)に並べる
        public static void SetItemsKeepText(ComboBox comboCtrl, IEnumerable<String> items)
        {
            String text = comboCtrl.Text;
            List<String> list = items.ToList();
            if (GetSorted(comboCtrl))
            {
                list.Sort(StringComparer.CurrentCulture);
            }
            comboCtrl.Items.Clear();
            foreach (String item in list)
            {
                comboCtrl.Items.Add(item);
            }
            if (comboCtrl.IsEditable && comboCtrl.Text != text)
            {
                comboCtrl.Text = text;
            }
        }

        public static List<String> GetItems(ComboBox comboCtrl)
        {
            return comboCtrl.Items.Cast<Object>().Select(item => item.ToString()).ToList();
        }

        // 文字配列をコンボボックスにセット(StcUtils.SetComboBoxFromArray相当)。
        // removeStringは各要素の先頭から取り除く部分(長さ+区切り1文字分だけ読み飛ばす)
        public static void SetComboBoxFromArray(ComboBox comboCtrl, String[] values, String removeString = "", String limitString = "")
        {
            int startIdx = 0;
            if (removeString != String.Empty)
            {
                startIdx = removeString.Length + 1;
            }
            SetComboBoxFromArraySubString(comboCtrl, values, startIdx, "", limitString);
        }

        // 文字配列(SubString)をコンボボックスにセット(StcUtils.SetComboBoxFromArraySubString相当)。
        // valuesがnull(プロファイル未読込・リストアップ前)なら項目を空にするだけ
        public static void SetComboBoxFromArraySubString(ComboBox comboCtrl, String[] values, int startIdx, String endDelimiter = "", String limitString = "", Boolean isReverse = false)
        {
            List<String> items = new List<String>();
            String currentText = comboCtrl.Text;
            foreach (String value in values ?? new String[0])
            {
                // カラ文字
                if (String.IsNullOrEmpty(value))
                {
                    continue;
                }

                // 文字列生成
                int endIdx = isReverse ? value.LastIndexOf(endDelimiter) : value.IndexOf(endDelimiter);

                String valueName;
                if (endIdx >= 0 && endIdx > startIdx)
                {
                    valueName = value.Substring(startIdx, endIdx - startIdx);
                }
                else
                {
                    valueName = value.Substring(startIdx);
                }

                // 文字の絞り込み(limitStringを含む項目だけ残す)
                if (limitString != String.Empty)
                {
                    // 大文字小文字を区別せずに部分一致で検索
                    if (valueName.IndexOf(limitString, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                }

                // 登録済みだったらスキップ
                if (items.Contains(valueName))
                {
                    continue;
                }

                items.Add(valueName);
            }

            SetItemsKeepText(comboCtrl, items);
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
            return GetItems(comboCtrl).FirstOrDefault(item => item.IndexOf(searchName) != -1) ?? "";
        }

        // ComboBoxのTextをプルダウンに追加する(重複は除く。StcUtils.AddComboBoxTextToItems相当)
        public static void AddComboBoxTextToItems(ComboBox comboCtrl)
        {
            if (comboCtrl.Text == String.Empty)
            {
                return;
            }

            List<String> items = GetItems(comboCtrl);
            items.Add(comboCtrl.Text);
            SetItemsKeepText(comboCtrl, items.Distinct().ToList());
        }
    }
}
