using System;
using System.IO;
using StandardTemplate;

namespace FileArrangerForWpf
{
    /// <summary>
    /// フォルダ内のファイルを "000.ext", "001.ext" ... と連番にリネームする機能
    /// （sf_ で始まるタブのロジック）。もともと Form1.cs の SortFile private メソッドと
    /// pmf フィールド(ProcessMemory)だったものを、そのまま1つのクラスにまとめた。
    ///
    /// 複数フォルダをまとめて Sort してから1回だけ CommitBatch する、という使い方は
    /// 元の Form1 の呼び出し順序（ループで SortFolder → ループの外で1回だけ
    /// IncrementRegistNumber）をそのまま踏襲している。Restore() は1回呼ぶと、
    /// 直前に CommitBatch した分（複数フォルダにまたがることもある）をまとめて元に戻す。
    /// </summary>
    internal class FileSorter
    {
        private readonly StcProcessMemory pm = new StcProcessMemory();

        /// <summary>指定フォルダの中のファイルを連番にリネームする。</summary>
        public void SortFolder(String FilePath)
        {
            String[] Files = Directory.GetFiles(FilePath);
            for (int i = 0; i < Files.Length; i++)
            {
                String Ext = Path.GetExtension(Files[i]);
                String DestName = FilePath + @"\" + String.Format("{0:D3}", i) + Ext;
                File.Move(Files[i], DestName);
                pm.SetRestoreList(Files[i], DestName);
            }
        }

        /// <summary>SortFolder を1回以上呼んだあと、まとめて1回の「実行」として記録を確定する。</summary>
        public void CommitBatch()
        {
            pm.IncrementRegistNumber();
        }

        /// <summary>
        /// 直前に確定した分のリネームをまとめて元に戻す。
        /// 戻す対象が無ければ false を返す（呼び出し側は「これ以上復元できません」を表示する）。
        /// </summary>
        public Boolean Restore()
        {
            if (!pm.DecrementRegistNumber())
            {
                return false;
            }

            while (pm.IsExistRestoreList())
            {
                String SrcName = "";
                String DestName = "";
                pm.GetRestoreList(ref SrcName, ref DestName);
                File.Move(DestName, SrcName);
            }

            return true;
        }
    }
}
