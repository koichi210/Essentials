// StcUtils([[_Common/StandardTemplateClass.cs]])のうち、WinFormsのコントロールを引数に取るため
// WPFから呼べないもの(ModifyCombBoxList / GetSelectName / CopyToClipboard)のWPF版。
// 挙動はWinForms版と同じにしてある。他のアプリでも使うようなら_Common/Wpfへ移す候補。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace FFEditForWpf
{
    internal static class WpfControlUtils
    {
        // ComboBoxのTextをプルダウン(履歴)に追加し、重複を取り除く。Textが空なら何もしない。
        // (StcUtils.ModifyCombBoxListと同じ。Items.Clearで入力中の文字列が消えないよう、Textは退避して戻す)
        public static void ModifyCombBoxList(ComboBox ComboCtrl)
        {
            String text = ComboCtrl.Text;
            if (text == String.Empty)
            {
                return;
            }

            List<String> items = ComboCtrl.Items.Cast<Object>().Select(item => item.ToString()).ToList();
            items.Add(text);

            // 順序を保ったまま重複を除去する
            HashSet<String> seen = new HashSet<String>();
            List<String> distinct = items.Where(item => seen.Add(item)).ToList();

            ComboCtrl.Items.Clear();
            foreach (String item in distinct)
            {
                ComboCtrl.Items.Add(item);
            }
            ComboCtrl.Text = text;
        }

        // 選択中の項目を、画面の並び順(インデックス順)で返す。
        // WPFのListBox.SelectedItemsはクリックした順になるため、WinForms版(常にインデックス順)に合わせて並べ直す
        public static List<String> GetSelectedItemsInOrder(ListBox ListBoxCtrl)
        {
            List<int> indices = new List<int>();
            foreach (Object item in ListBoxCtrl.SelectedItems)
            {
                indices.Add(ListBoxCtrl.Items.IndexOf(item));
            }
            indices.Sort();
            return indices.Select(i => ListBoxCtrl.Items[i].ToString()).ToList();
        }

        // 選択項目を「RootPath\項目名」の形で改行区切りに連結する(StcUtils.GetSelectNameと同じ)
        public static String GetSelectName(ListBox ListBoxCtrl, String RootPath = "")
        {
            StringBuilder TargetName = new StringBuilder();
            foreach (String item in GetSelectedItemsInOrder(ListBoxCtrl))
            {
                if (RootPath != String.Empty)
                {
                    TargetName.Append(RootPath).Append(@"\");
                }
                TargetName.Append(item).Append(Environment.NewLine);
            }
            return TargetName.ToString();
        }

        // リストボックスの選択項目をクリップボードへコピーする(StcUtils.CopyToClipboardと同じ)
        public static Boolean CopyToClipboard(ListBox ListBoxCtrl, String RootPath = "")
        {
            return SetClipboardText(GetSelectName(ListBoxCtrl, RootPath));
        }

        // クリップボードは他のアプリが掴んでいる間は開けず例外になるため、少し待って数回やり直す
        // (StcUtils.SetClipboardTextと同じ考え方)。空文字のときはクリアする
        public static Boolean SetClipboardText(String Text)
        {
            const int RetryCount = 5;
            for (int i = 0; i < RetryCount; i++)
            {
                try
                {
                    if (String.IsNullOrEmpty(Text))
                    {
                        Clipboard.Clear();
                    }
                    else
                    {
                        Clipboard.SetText(Text);
                    }
                    return true;
                }
                catch (COMException)
                {
                    Thread.Sleep(100);
                }
            }
            return false;
        }
    }
}
