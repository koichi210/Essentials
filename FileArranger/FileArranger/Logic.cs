using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using Microsoft.VisualBasic;

namespace FileArranger
{
    /// <summary>
    /// もともと Form1.cs のイベントハンドラの隣に private メソッドとして埋め込まれていた
    /// 純粋なロジックを、テストできる形に切り出したもの(WinForms版FileArrangerのLogic.csと同じ)。
    /// WPF版での変更点は、GetAddCount が WPF の ListView(項目は ListViewRow)を受け取るようにしたことだけ。
    /// </summary>
    internal static class Logic
    {
        /// <summary>
        /// ファイル名の連番部分にゼロ埋めが必要な桁数を返す。
        /// 例えば連番が1桁・2桁のときは2桁（"01","02"..."09"）にそろえる。
        /// </summary>
        public static int GetPaddingDigits(long number, Boolean isZeroDigitForZero = false)
        {
            const int PaddingMinDigits = 2;
            int paddingDigits = 0;

            if (isZeroDigitForZero && number == 0)
            {
                // 数値が「0」のときは、桁数も「0」とする
            }
            else if (number.ToString().Length <= PaddingMinDigits)
            {
                paddingDigits = PaddingMinDigits;
            }
            return paddingDigits;
        }

        /// <summary>連番に加算数を足し、必要な桁数までゼロ埋めした文字列にする。</summary>
        public static String ToPaddedNumberString(long srcNumber, int addCount = 0)
        {
            long destNumber = srcNumber + addCount;

            int paddingDigits = GetPaddingDigits(destNumber);
            return destNumber.ToString().PadLeft(paddingDigits, '0');
        }

        /// <summary>全角の数字・英字・スペースを半角に変換する。</summary>
        public static String ChangeWide2Narrow(String srcString)
        {
            const String WidePattern = "[０-９Ａ-Ｚａ-ｚ　]";
            Regex re = new Regex(WidePattern);
            return re.Replace(srcString, ToNarrow);
        }

        private static String ToNarrow(Match m)
        {
            // Memo: 参照設定に「Microsoft.VisualBasic」が必要
            return Strings.StrConv(m.Value, VbStrConv.Narrow);
        }

        /// <summary>
        /// 区切り文字(trimName)が設定されていれば、その位置より前の部分を返す
        /// (isReverse=true なら最後に現れた区切り文字で区切る)。
        /// 区切り文字が空、または見つからなければ srcName をそのまま返す。
        /// </summary>
        public static String TrimAtSeparator(String srcName, String trimName, Boolean isReverse)
        {
            if (trimName == String.Empty)
            {
                return srcName;
            }

            int delimiterIdx = isReverse ? srcName.LastIndexOf(trimName) : srcName.IndexOf(trimName);
            if (0 <= delimiterIdx)
            {
                return srcName.Substring(0, delimiterIdx);
            }
            return srcName;
        }

        /// <summary>
        /// リストの選択項目の中から、区切り文字より前の部分が一致するものを数える。
        /// 一致が無ければ、新規追加時の初期値として 1 を返す。
        /// </summary>
        public static int GetAddCount(ListView lv, String fileName, String trimName, Boolean isReverse = false)
        {
            const int TargetSubItemIdx = 0;

            int count = 0;
            String searchName = "";

            // TrimAtSeparatorとは違い、区切り文字が見つからなければ空文字で探す(=全件一致)
            int delimiterIdx = isReverse ? fileName.LastIndexOf(trimName) : fileName.IndexOf(trimName);
            if (0 <= delimiterIdx)
            {
                searchName = fileName.Substring(0, delimiterIdx);
            }

            foreach (Object item in WpfControlHelper.GetSelectedItemsInIndexOrder(lv))
            {
                String srcFileName = ((ListViewRow)item)[TargetSubItemIdx];

                if (srcFileName.IndexOf(searchName) != -1)
                {
                    count++;
                }
            }

            if (count == 0)
            {
                // 今回新規追加時の初期値
                count = 1;
            }
            return count;
        }

        /// <summary>
        /// 新しく追加された項目のうち、既存の一覧に既に含まれているものを取り除く。
        ///
        /// ⚠️ Delimiter 引数は元の実装から使われていなかった（呼び出し側は値を渡しているが
        /// 中では参照されていない）。挙動を変えないため、そのまま残してある。
        /// </summary>
        public static void DeleteDuplicate(String[] existingArray, ref String[] newArray, String delimiter)
        {
            var result = new List<String>();
            foreach (String newItem in newArray)
            {
                Boolean isDuplicate = false;
                foreach (String existingItem in existingArray)
                {
                    if (existingItem.IndexOf(newItem) != -1)
                    {
                        isDuplicate = true;
                        break;
                    }
                }

                if (!isDuplicate)
                {
                    result.Add(newItem);
                }
            }

            newArray = result.ToArray();
        }
    }
}
