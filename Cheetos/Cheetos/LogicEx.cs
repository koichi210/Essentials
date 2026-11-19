// Logic(Logic.cs)の続き。2026-11に足した機能(バックアップ先の名前・キャプチャ画像の名前・
// 次の操作の予定時刻・表への貼り付け)の純粋なロジックをまとめたもの。
// WinForms版(CS_Form\Cheetos)とWPF版(stable\Essentials\Cheetos)で同じファイルを使う。
using System;
using System.Collections.Generic;
using System.IO;

namespace Cheetos
{
    internal static partial class Logic
    {
        // バックアップ先は「元フォルダ\Cheetos\BK\yyyyMMdd_種類」
        private const String _backupRootFolderName = "Cheetos";
        private const String _backupFolderName = "BK";

        /// <summary>
        /// バックアップ先にする新しいフォルダのパスを返す(フォルダはまだ作らない)。
        /// 「元フォルダ\Cheetos\BK\yyyyMMdd_種類」。同じ名前のフォルダ(かファイル)が既にあれば
        /// 「_1」「_2」…と連番を付けて、前のバックアップには上書きしない。
        /// </summary>
        public static String GetNewBackupDirectoryPath(String sourceFolderPath, String kind, DateTime date)
        {
            String basePath = Path.Combine(sourceFolderPath, _backupRootFolderName, _backupFolderName, date.ToString("yyyyMMdd") + "_" + kind);
            String candidate = basePath;
            for (int number = 1; Directory.Exists(candidate) || File.Exists(candidate); number++)
            {
                candidate = basePath + "_" + number;
            }
            return candidate;
        }

        /// <summary>
        /// GetNewBackupDirectoryPathのフォルダを作ってそのパスを返す(途中のCheetos\BKも作る。確認はしない)。
        /// 作れなければ例外(IOException・UnauthorizedAccessExceptionなど)をそのまま投げる。
        /// </summary>
        public static String CreateBackupDirectory(String sourceFolderPath, String kind)
        {
            String path = GetNewBackupDirectoryPath(sourceFolderPath, kind, DateTime.Now);
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// CaptureWindowタブで保存されるキャプチャ画像のパスを、撮る順にすべて返す。
        /// 名前の付け方は共通クラスのCaptWindow.CaptureProcと同じ「接頭辞+繰り返し番号4桁_撮影番号.png」
        /// (撮影番号は繰り返しごとに1から、キャプチャする行だけ数える)。
        /// 開始前に「同じ名前のファイルがあれば上書きしてよいか」を確かめるのに使う。
        /// </summary>
        public static List<String> BuildCaptureFilePaths(String filePathPrefix, int loopCount, int captureRowCount)
        {
            List<String> paths = new List<String>();
            for (int loop = 1; loop <= loopCount; loop++)
            {
                for (int idx = 1; idx <= captureRowCount; idx++)
                {
                    paths.Add(filePathPrefix + loop.ToString("D4") + "_" + idx + ".png");
                }
            }
            return paths;
        }

        /// <summary>
        /// 共通クラスのCaptWindow.ExecuteSleepで待ち終わる時刻を返す。
        /// ExecuteSleepはsleepCycle(既定1秒)単位で待つので、sleepTimeをsleepCycleの倍数に切り上げた時間だけ待つ。
        /// </summary>
        public static DateTime CalcSleepEndTime(DateTime start, TimeSpan sleepTime, TimeSpan sleepCycle)
        {
            if (sleepTime <= TimeSpan.Zero || sleepCycle <= TimeSpan.Zero)
            {
                return start;
            }
            long cycles = (sleepTime.Ticks + sleepCycle.Ticks - 1) / sleepCycle.Ticks;
            return start + TimeSpan.FromTicks(cycles * sleepCycle.Ticks);
        }

        /// <summary>
        /// 予定時刻scheduledをgrace以上過ぎていればtrue(次の操作が始まっていないことを赤字で知らせる判定)。
        /// </summary>
        public static bool IsOverdue(DateTime now, DateTime scheduled, TimeSpan grace)
        {
            return now > scheduled + grace;
        }

        /// <summary>
        /// Excelなどからコピーした表の文字列(列はタブ、行は改行で区切る)を行ごとのセルの配列にする。
        /// 最後の改行の後ろの空行(Excelのコピーには付く)は入れない。
        /// (セルの中の改行・タブを「"」で囲む形式には対応しない)
        /// </summary>
        public static List<String[]> ParseTableText(String text)
        {
            List<String[]> rows = new List<String[]>();
            if (String.IsNullOrEmpty(text))
            {
                return rows;
            }

            String[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int lineCount = lines.Length;
            if (lineCount > 0 && lines[lineCount - 1] == String.Empty)
            {
                lineCount--;
            }
            for (int i = 0; i < lineCount; i++)
            {
                rows.Add(lines[i].Split('\t'));
            }
            return rows;
        }
    }
}
