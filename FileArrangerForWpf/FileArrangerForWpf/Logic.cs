using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using Microsoft.VisualBasic;

namespace FileArrangerForWpf
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
        public static int GetPadding(long Number, Boolean ThroughNumberZero = false)
        {
            const int PaddingMinNum = 2;
            int PaddingDigit = 0;

            if (ThroughNumberZero && Number == 0)
            {
                // 数値が「0」のときは、桁数も「0」とする
            }
            else if (Number.ToString().Length <= PaddingMinNum)
            {
                PaddingDigit = PaddingMinNum;
            }
            return PaddingDigit;
        }

        /// <summary>連番に加算数を足し、必要な桁数までゼロ埋めした文字列にする。</summary>
        public static String GetNumber(long SrcNumber, int AddCount = 0)
        {
            long DestNumber = SrcNumber + AddCount;

            int PaddingDigit = GetPadding(DestNumber);
            return DestNumber.ToString().PadLeft(PaddingDigit, '0');
        }

        /// <summary>全角の数字・英字・スペースを半角に変換する。</summary>
        public static String ChangeWide2Narrow(String SrcString)
        {
            String RegesStr = "[０-９Ａ-Ｚａ-ｚ　]";
            Regex re = new Regex(RegesStr);
            return re.Replace(SrcString, myReplacer);
        }

        private static String myReplacer(Match m)
        {
            // Memo: 参照設定に「Microsoft.VisualBasic」が必要
            return Strings.StrConv(m.Value, VbStrConv.Narrow);
        }

        /// <summary>
        /// リストの選択項目の中から、区切り文字より前の部分が一致するものを数える。
        /// 一致が無ければ、新規追加時の初期値として 1 を返す。
        /// </summary>
        public static int GetAddCount(ListView lv, String FileName, String TrimName, Boolean IsReverse = false)
        {
            const int TargetSubItemIdx = 0;

            int Count = 0;
            String SearchName = "";

            int FileNameidx;
            if (!IsReverse)
            {
                FileNameidx = FileName.IndexOf(TrimName);
            }
            else
            {
                FileNameidx = FileName.LastIndexOf(TrimName);
            }

            if (0 <= FileNameidx)
            {
                SearchName = FileName.Substring(0, FileNameidx);
            }

            foreach (Object item in WpfControlHelper.GetSelectedItemsInIndexOrder(lv))
            {
                String SrcFileName = ((ListViewRow)item)[TargetSubItemIdx];

                if (SrcFileName.IndexOf(SearchName) != -1)
                {
                    Count++;
                }
            }

            if (Count == 0)
            {
                // 今回新規追加時の初期値
                Count = 1;
            }
            return Count;
        }

        /// <summary>
        /// 新しく追加された項目のうち、既存の一覧に既に含まれているものを取り除く。
        ///
        /// ⚠️ Delimiter 引数は元の実装から使われていなかった（呼び出し側は値を渡しているが
        /// 中では参照されていない）。挙動を変えないため、そのまま残してある。
        /// </summary>
        public static void DeleteDuplicate(String[] LegacyArray, ref String[] NewArray, String Delimiter)
        {
            var Result = new List<String>();
            foreach (String NewItem in NewArray)
            {
                Boolean IsDuplicate = false;
                foreach (String LegacyItem in LegacyArray)
                {
                    if (LegacyItem.IndexOf(NewItem) != -1)
                    {
                        IsDuplicate = true;
                        break;
                    }
                }

                if (!IsDuplicate)
                {
                    Result.Add(NewItem);
                }
            }

            NewArray = Result.ToArray();
        }
    }
}
