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
    /// IncrementSerialNumber）をそのまま踏襲している。Restore() は1回呼ぶと、
    /// 直前に CommitBatch した分（複数フォルダにまたがることもある）をまとめて元に戻す。
    /// </summary>
    internal class FileSorter
    {
        private readonly StcProcessMemory pm = new StcProcessMemory();

        /// <summary>指定フォルダの中のファイルを連番にリネームする。</summary>
        public void SortFolder(String folderPath)
        {
            String[] files = Directory.GetFiles(folderPath);
            for (int i = 0; i < files.Length; i++)
            {
                String ext = Path.GetExtension(files[i]);
                String destName = folderPath + @"\" + String.Format("{0:D3}", i) + ext;
                File.Move(files[i], destName);
                pm.AddRestoreItem(files[i], destName);
            }
        }

        /// <summary>SortFolder を1回以上呼んだあと、まとめて1回の「実行」として記録を確定する。</summary>
        public void CommitBatch()
        {
            pm.IncrementSerialNumber();
        }

        /// <summary>
        /// 直前に確定した分のリネームをまとめて元に戻す。
        /// 戻す対象が無ければ false を返す（呼び出し側は「これ以上復元できません」を表示する）。
        /// </summary>
        public Boolean Restore()
        {
            if (!pm.DecrementSerialNumber())
            {
                return false;
            }

            while (pm.HasRestoreItem())
            {
                String srcName = "";
                String destName = "";
                pm.PopRestoreItem(ref srcName, ref destName);
                File.Move(destName, srcName);
            }

            return true;
        }
    }
}
