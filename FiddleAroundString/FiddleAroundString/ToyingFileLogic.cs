using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FiddleAroundString
{
    /// <summary>
    /// 「ファイル加工」タブ(旧ToyingFile)のロジック。CS_Wpf\ToyingFile\Logic.cs を中身ごと移植し、クラス名だけToyingFileLogicに変えた(4本分のLogicを1つのプロジェクトに入れるため)。
    ///
    /// ファイル内容から指定文字列を含む行を処理するロジック。DeleteStringFromContentは
    /// WinForms版(CS_Form\ToyingFile\Logic.cs)をそのまま流用している。
    ///
    /// ファイルの読み書き(fio.LoadFile/SaveFile)は画面側に残し、ここには文字列だけを渡す・返す形にした。
    ///
    /// caseSensitive(大文字小文字を区別するか)は、以前は「削除対象の行かどうかを判定する IndexOf」
    /// にしか効いておらず、実際に削除する String.Replace は常に大文字小文字を区別していた
    /// (.NET Framework の String.Replace に大文字小文字を無視するオーバーロードが無いため)。
    /// 区別しない場合は Regex.Replace を使うことで、判定と削除の挙動を揃えている。
    /// </summary>
    internal static class ToyingFileLogic
    {
        /// <summary>
        /// 対象ファイルを列挙する(WinForms版ではForm1.GetTargetFilesにあったもの)。
        /// filePatternが空なら全ファイル。includeSubDirectories=trueならサブフォルダも再帰的に探す。
        /// </summary>
        public static string[] GetTargetFiles(string directory, string filePattern, bool includeSubDirectories)
        {
            string searchPattern = (filePattern != string.Empty) ? filePattern : "*";
            SearchOption searchOption = includeSubDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            return Directory.GetFiles(directory, searchPattern, searchOption);
        }

        /// <summary>
        /// 対象ファイルのディレクトリとして使えるかを調べ、使えなければ画面に出すメッセージを返す(使えればnull)。
        /// 以前は存在しないディレクトリを指定すると Directory.GetFiles の例外で例外ダイアログが出ていた。
        /// </summary>
        public static string ValidateDirectory(string directory)
        {
            if (directory == string.Empty)
            {
                return "対象ファイルのディレクトリが設定されていません";
            }
            // Directory.Exists はパスに使えない文字が入っていても例外にせず false を返す
            if (!Directory.Exists(directory))
            {
                return "対象ファイルのディレクトリが見つかりません" + Environment.NewLine + directory;
            }
            return null;
        }

        /// <summary>削除文字列の入力欄(1行に1つ)を配列にする。空行は無視する。</summary>
        public static string[] SplitDeleteStrings(string text)
        {
            return text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// ファイル内容(改行区切り)から、deleteStrings のいずれかを含む行を処理する。
        /// deleteWholeLine=true なら行ごと空行にする。false なら該当文字列だけ削除する。
        /// </summary>
        public static string DeleteStringFromContent(string fileData, string[] deleteStrings, bool caseSensitive, bool deleteWholeLine)
        {
            StringComparison comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            // 大文字小文字を区別しない削除用の正規表現は、行ごとに作り直さず最初に1回だけ作る
            Regex[] ignoreCaseRegexes = (caseSensitive || deleteWholeLine)
                ? null
                : deleteStrings.Select(s => new Regex(Regex.Escape(s), RegexOptions.IgnoreCase)).ToArray();

            // 以前はEnvironment.NewLine(CRLF)だけで分けていたため、LFだけのファイルは全体が1行扱いになり、
            // 行ごと削除で「どこか1か所でも一致したらファイルの中身が丸ごと消える」状態だった。
            // 改行コード(CRLF/LF/CR、混在も可)を問わず行に分け、区切りの改行コードは捕捉して元のまま連結し直す
            // (Regex.Splitの捕捉グループで、偶数番目が行・奇数番目が改行コードになる)
            string[] lines = Regex.Split(fileData, "(\r\n|\n|\r)");

            for (int j = 0; j < lines.Length; j += 2)
            {
                for (int k = 0; k < deleteStrings.Length && lines[j] != string.Empty; k++)
                {
                    //削除対象の行か判別
                    if (lines[j].IndexOf(deleteStrings[k], comparison) == -1)
                    {
                        continue;
                    }

                    if (deleteWholeLine)
                    {
                        // 一行削除&空行追加
                        lines[j] = "";
                    }
                    else if (caseSensitive)
                    {
                        // 文字だけ削除ならReplace
                        lines[j] = lines[j].Replace(deleteStrings[k], "");
                    }
                    else
                    {
                        lines[j] = ignoreCaseRegexes[k].Replace(lines[j], "");
                    }
                }
            }

            return string.Concat(lines);
        }
    }
}
