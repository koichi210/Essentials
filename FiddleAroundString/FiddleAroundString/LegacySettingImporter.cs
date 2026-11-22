using System;
using System.IO;

namespace FiddleAroundString
{
    /// <summary>
    /// 旧TrimFileData・旧TrimHtmlDataで保存していた設定(JSONプロファイル)を、FiddleAroundStringの各タブの設定フォルダへ
    /// 初回起動のときだけコピーする。
    ///
    /// ・コピー元は旧アプリの既定の保存先(%LOCALAPPDATA%\TrimFileData\ と %LOCALAPPDATA%\TrimHtmlData\)だけを見る。
    ///   旧アプリで「データ保存先を変更」して別の場所にしていた場合は見つけられないので、手でコピーしてもらう
    ///   (旧アプリのexeの横の DataFolder.txt の1行目が保存先)。
    /// ・保存キーは旧アプリと同じなので、コピーしたファイルはそのまま読める。
    /// ・コピーするのはタブの設定フォルダがまだ無いとき(=そのタブを初めて使うとき)だけ。2回目以降は何もしない。
    /// ・コピー(移動ではない)なので、旧アプリ側のファイルはそのまま残り、旧アプリも今まで通り使える。
    /// ・旧XML(*.xml)はWPF版では読めないのでコピーしない(JSONへの移行はWinForms版の起動時に済んでいる前提)。
    /// </summary>
    internal static class LegacySettingImporter
    {
        // 旧アプリの既定の保存先(%LOCALAPPDATA%\<旧アプリ名>)
        public static String GetLegacyDefaultFolder(String legacyAppName)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), legacyAppName);
        }

        // newFolderがまだ無く、legacyFolderがあるときだけ、legacyFolder配下(サブフォルダも)の*.jsonを
        // 同じフォルダ構成のままnewFolderへコピーする。コピーした件数を返す(何もしなかったら0)。
        // コピーに失敗したファイルは飛ばす(起動を止めない)
        public static int ImportIfFirstRun(String legacyFolder, String newFolder)
        {
            try
            {
                if (Directory.Exists(newFolder) || !Directory.Exists(legacyFolder))
                {
                    return 0;
                }

                String[] sourceFiles = Directory.GetFiles(legacyFolder, "*.json", SearchOption.AllDirectories);
                Directory.CreateDirectory(newFolder);

                int prefixLength = Path.GetFullPath(legacyFolder).TrimEnd('\\').Length + 1;
                int copied = 0;
                foreach (String sourcePath in sourceFiles)
                {
                    // 検索パターン"*.json"は".jsonx"のような長い拡張子にも一致することがあるので、拡張子は自分で確かめる
                    if (!Path.GetExtension(sourcePath).Equals(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    String relativePath = Path.GetFullPath(sourcePath).Substring(prefixLength);
                    String destPath = Path.Combine(newFolder, relativePath);
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                        File.Copy(sourcePath, destPath, false);
                        copied++;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        // 1件コピーできなくても残りは続ける
                    }
                }
                return copied;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                return 0;
            }
        }
    }
}
