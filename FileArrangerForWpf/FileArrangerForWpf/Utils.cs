using System;
using System.IO;
using System.Windows.Controls;
using StandardTemplate;

namespace FileArrangerForWpf
{
    // WinForms版FileArrangerのUtils.csと同じ。FindSelectedRowIndex(WinForms版GetStringFromListViewInSelect)だけWPFのListView(項目はListViewRow)を受け取る
    class Utils : StcUtils
    {
        // フォルダ名の重複回避(WinForms版CreateFolderNameOverLapShirk)。
        // フォルダが既に存在すれば、targetPathの末尾に連番と日時を付けた名前に書き換える
        public void AvoidFolderNameOverlap(ref String targetPath, int loopIdx)
        {
            // フォルダが存在しなければ何もしない
            if (!Directory.Exists(targetPath))
            {
                return;
            }
            targetPath = targetPath + CreateOverlapSuffix(loopIdx);
        }

        // ファイル名の重複回避(WinForms版CreateFileNameOverLapShirk)。
        // 同名のファイル/フォルダが無ければtrue。あればtargetPathの末尾に連番と日時を付けてfalseを返す
        public Boolean AvoidFileNameOverlap(ref String targetPath, int loopIdx)
        {
            if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
            {
                return true;
            }
            targetPath = targetPath + CreateOverlapSuffix(loopIdx);
            return false;
        }

        private static String CreateOverlapSuffix(int loopIdx)
        {
            return "_Cnt" + loopIdx.ToString() + "_" + DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss");
        }

        public String CreateNewFolderName(String srcName, String trimName = "", Boolean isReverse = false)
        {
            // trimNameが設定されていたら、特定の文字列で区切る
            return Logic.CutBeforeDelimiter(srcName, trimName, isReverse);
        }

        // 選択されているリストビューの中から目的の文字列を探す(戻り値はItems上のインデックス。無ければ-1)
        public int FindSelectedRowIndex(ListView lvCtrl, int srcSubItemIdx, String srcName, String srcTrimName = "", Boolean isReverse = false)
        {
            // srcTrimNameが設定されていたら、特定の文字列で区切る
            String searchName = Logic.CutBeforeDelimiter(srcName, srcTrimName, isReverse);

            foreach (int idx in WpfControlHelper.GetSelectedIndices(lvCtrl))
            {
                String lvString = ((ListViewRow)lvCtrl.Items[idx])[srcSubItemIdx];

                if (lvString.IndexOf(searchName) != -1)
                {
                    return idx;
                }
            }

            return -1;
        }
    }
}
