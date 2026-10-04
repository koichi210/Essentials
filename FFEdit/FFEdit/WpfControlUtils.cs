// StcUtils([[_Common/StandardTemplateClass.cs]])のうち、WinFormsのコントロールを引数に取るため
// WPFから呼べないもの(AddComboBoxTextToItems / GetSelectName / CopyToClipboard)のWPF版。
// 挙動はWinForms版と同じにしてある。他のアプリでも使うようなら_Common/Wpfへ移す候補。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace FFEdit
{
    internal static class WpfControlUtils
    {
        // ComboBoxのTextをプルダウン(履歴)に追加し、重複を取り除く。Textが空なら何もしない。
        // (StcUtils.AddComboBoxTextToItemsと同じ。Items.Clearで入力中の文字列が消えないよう、Textは退避して戻す)
        public static void AddComboBoxTextToItems(ComboBox comboBox)
        {
            String text = comboBox.Text;
            if (text == String.Empty)
            {
                return;
            }

            List<String> items = comboBox.Items.Cast<Object>().Select(item => item.ToString()).ToList();
            items.Add(text);

            // 順序を保ったまま重複を除去する
            HashSet<String> seen = new HashSet<String>();
            List<String> distinct = items.Where(item => seen.Add(item)).ToList();

            comboBox.Items.Clear();
            foreach (String item in distinct)
            {
                comboBox.Items.Add(item);
            }
            comboBox.Text = text;
        }

        // 選択中の項目を、画面の並び順(インデックス順)で返す。
        // WPFのListBox.SelectedItemsはクリックした順になるため、WinForms版(常にインデックス順)に合わせて並べ直す
        public static List<String> GetSelectedItemsInOrder(ListBox listBox)
        {
            return listBox.SelectedItems.Cast<Object>()
                .Select(item => listBox.Items.IndexOf(item))
                .OrderBy(index => index)
                .Select(index => listBox.Items[index].ToString())
                .ToList();
        }

        // 選択項目を「rootPath\項目名」の形で改行区切りに連結する(StcUtils.GetSelectNameと同じ)
        public static String GetSelectName(ListBox listBox, String rootPath = "")
        {
            StringBuilder selectedNames = new StringBuilder();
            foreach (String item in GetSelectedItemsInOrder(listBox))
            {
                if (rootPath != String.Empty)
                {
                    selectedNames.Append(rootPath).Append(@"\");
                }
                selectedNames.Append(item).Append(Environment.NewLine);
            }
            return selectedNames.ToString();
        }

        // リストボックスの選択項目をクリップボードへコピーする(StcUtils.CopyToClipboardと同じ)
        public static Boolean CopyToClipboard(ListBox listBox, String rootPath = "")
        {
            return SetClipboardText(GetSelectName(listBox, rootPath));
        }

        // クリップボードは他のアプリが掴んでいる間は開けず例外になるため、少し待って数回やり直す
        // (StcUtils.SetClipboardTextと同じ考え方)。空文字のときはクリアする
        public static Boolean SetClipboardText(String text)
        {
            const int retryCount = 5;
            const int retryWaitMsec = 100;
            for (int i = 0; i < retryCount; i++)
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
                    Thread.Sleep(retryWaitMsec);
                }
            }
            return false;
        }
    }
}
