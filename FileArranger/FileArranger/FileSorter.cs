using System;
using System.IO;
using StandardTemplate;

namespace FileArranger
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
        private readonly StcProcessMemory _pm = new StcProcessMemory();

        /// <summary>指定フォルダの中のファイルを連番にリネームする。</summary>
        public void SortFolder(String folderPath)
        {
            String[] files = Directory.GetFiles(folderPath);
            for (int i = 0; i < files.Length; i++)
            {
                String ext = Path.GetExtension(files[i]);
                String destName = folderPath + @"\" + i.ToString("D3") + ext;
                File.Move(files[i], destName);
                _pm.AddRestoreItem(files[i], destName);
            }
        }

        /// <summary>SortFolder を1回以上呼んだあと、まとめて1回の「実行」として記録を確定する。</summary>
        public void CommitBatch()
        {
            _pm.IncrementSerialNumber();
        }

        /// <summary>
        /// 直前に確定した分のリネームをまとめて元に戻す。
        /// 戻す対象が無ければ false を返す（呼び出し側は「これ以上復元できません」を表示する）。
        /// </summary>
        public Boolean Restore()
        {
            return _pm.RestoreLastBatch(File.Move);
        }
    }
}
