using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using StandardTemplate;

namespace FileArranger
{
    // WinForms版FileArrangerのUtils.csと同じ。FindSelectedRowIndexだけWPFのListView(項目はListViewRow)を受け取る
    class Utils : StcUtils
    {
        // フォルダ名の重複回避。
        // 同名のフォルダ/ファイルが既に存在すれば、targetPathの末尾に連番と日時を付けた名前に書き換える
        // (以前はフォルダしか見ていなかったため、同名のファイルがあると重複回避されずに移動が失敗していた)
        public void AvoidFolderNameConflict(ref String targetPath, int loopIdx)
        {
            // フォルダもファイルも存在しなければ何もしない
            if (!Directory.Exists(targetPath) && !File.Exists(targetPath))
            {
                return;
            }
            targetPath = targetPath + CreateConflictSuffix(loopIdx);
        }

        // ファイル名の重複回避。
        // 同名のファイル/フォルダが無ければtrue。あればtargetPathの拡張子の前に連番と日時を付けてfalseを返す
        // (以前は拡張子の後ろに付けていたため、「a.jpg_Cnt0_…」となって拡張子として認識されなかった)
        public Boolean AvoidFileNameConflict(ref String targetPath, int loopIdx)
        {
            if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
            {
                return true;
            }
            String extension = Path.GetExtension(targetPath);
            targetPath = targetPath.Substring(0, targetPath.Length - extension.Length) + CreateConflictSuffix(loopIdx) + extension;
            return false;
        }

        private static String CreateConflictSuffix(int loopIdx)
        {
            return "_Cnt" + loopIdx.ToString() + "_" + DateTime.Now.ToString("yyyy_MM_dd_HH_mm_ss");
        }

        // フォルダのリネーム(大文字小文字だけ違う名前にも変えられる)。
        // Directory.Moveは大文字小文字だけ違うパスを「同じパス」として例外にするため、その場合は一旦別の名前を経由する
        public static void RenameDirectory(String sourcePath, String destPath)
        {
            if (!String.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(sourcePath, destPath);
                return;
            }

            String tempPath = sourcePath + "_" + Guid.NewGuid().ToString("N");
            Directory.Move(sourcePath, tempPath);
            Directory.Move(tempPath, destPath);
        }

        public String CreateNewFolderName(String srcName, String trimName = "", Boolean isReverse = false)
        {
            // trimNameが設定されていたら、特定の文字列で区切る
            return Logic.TrimAtSeparator(srcName, trimName, isReverse);
        }

        // 選択されているリストビューの中から目的の文字列を探す(戻り値はItems上のインデックス。無ければ-1)
        public int FindSelectedRowIndex(ListView listView, int subItemIdx, String srcName, String trimName = "", Boolean isReverse = false)
        {
            ListViewRow found = FindRow(WpfControlHelper.GetSelectedRows(listView), subItemIdx, srcName, trimName, isReverse);
            return found != null ? listView.Items.IndexOf(found) : -1;
        }

        // rows(選択行のスナップショット等)の中から目的の文字列を含む最初の行を探す(無ければnull)
        public ListViewRow FindRow(IEnumerable<ListViewRow> rows, int subItemIdx, String srcName, String trimName = "", Boolean isReverse = false)
        {
            // trimNameが設定されていたら、特定の文字列で区切る
            String searchName = Logic.TrimAtSeparator(srcName, trimName, isReverse);
            return rows.FirstOrDefault(row => row[subItemIdx].IndexOf(searchName) != -1);
        }
    }
}
