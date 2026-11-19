// PictMergeタブの「切り取る高さ」の指定方法。
// 以前は「開始,終了」を1行ずつ自由に書く方式だけだったが、同じ高さの帯をたくさん切る時に書くのが大変なので、
//   ・開始位置だけを並べて、高さは共通の1つにする方式(単体のPictMergeツールにある書き方)
//   ・開始位置・ずらす幅(間隔)・高さ・回数で、等間隔に並べる方式
// を足した。どの方式も最後は今までと同じ「開始,終了」の行に直してから結合する(確認・結合の処理は共通)。
// WinForms版(CS_Form\Cheetos)とWPF版(stable\Essentials\Cheetos)で同じファイルを使う。
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cheetos
{
    internal static class TrimHeightPattern
    {
        private static readonly char[] _startSeparators = { '\r', '\n', ',' };

        // 回数を省略したとき(画像の下端まで)に作る行数の上限(間隔の入れ間違いで延々と作らないため)
        private const int _maxGeneratedLines = 10000;

        /// <summary>
        /// 開始位置の一覧(改行かカンマで区切る。空の項目は無視)と共通の高さから「開始,終了」の行を作る。
        /// 数値でない項目があればFormatException(メッセージにその項目を入れる)を投げる。
        /// </summary>
        public static String[] BuildFromStarts(String startsText, int height)
        {
            List<String> lines = new List<String>();
            foreach (String token in (startsText ?? "").Split(_startSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                String trimmed = token.Trim();
                if (trimmed == String.Empty)
                {
                    continue;
                }
                int start;
                if (!Int32.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out start))
                {
                    throw new FormatException("開始位置に数値でない値があります: [" + trimmed + "]");
                }
                lines.Add(ToRangeLine(start, height));
            }
            return lines.ToArray();
        }

        /// <summary>
        /// 開始位置startからpitchずつずらしながら、高さheightの帯をcount個作る(「開始,終了」の行)。
        /// countが0以下なら、帯の下端がimageHeightを超えない所まで作る(imageHeightが0以下なら何も作らない)。
        /// </summary>
        public static String[] BuildFromPitch(int start, int pitch, int height, int count, int imageHeight)
        {
            List<String> lines = new List<String>();
            if (pitch <= 0 || height <= 0)
            {
                return lines.ToArray();
            }

            for (int i = 0; ; i++)
            {
                if (count > 0)
                {
                    if (i >= count)
                    {
                        break;
                    }
                }
                else if (imageHeight <= 0 || i >= _maxGeneratedLines || (long)start + (long)i * pitch + height > imageHeight)
                {
                    break;
                }
                lines.Add(ToRangeLine(start + i * pitch, height));
            }
            return lines.ToArray();
        }

        private static String ToRangeLine(int start, int height)
        {
            return start + "," + (start + height);
        }
    }
}
