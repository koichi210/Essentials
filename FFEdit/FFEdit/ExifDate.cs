using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FFEdit
{
    // JPEGのEXIFにある日時(撮影日時など)の読み書き。
    // System.DrawingのSetPropertyItem→Saveだと画像を再圧縮して画質が落ちるため使わない。
    // 代わりにEXIF内の日時文字列("yyyy:MM:dd HH:mm:ss"の19文字)の位置を探し、その19バイトだけを
    // ファイル上で上書きする。画像データ・他のタグ・ファイルサイズは一切変わらない。
    // そのため、EXIFに日時の欄がもともと無いJPEG(欄の追加が必要なもの)には書き込めない。
    // WinForms版(CS_Form/FFEdit)とWPF版(Essentials/FFEdit)で同じ内容にしてある
    static class ExifDate
    {
        public const int TagDateTime = 0x0132;            // IFD0: 変更日時(ファイル更新日時とは別物)
        public const int TagDateTimeOriginal = 0x9003;    // Exif IFD: 撮影日時(エクスプローラーの「撮影日時」)
        public const int TagDateTimeDigitized = 0x9004;   // Exif IFD: デジタル化日時

        private const int _tagExifIfdPointer = 0x8769;
        private const int _typeAscii = 2;
        private const int _dateLength = 19;
        private const String _dateFormat = "yyyy:MM:dd HH:mm:ss";

        // APP1(EXIF)は1セグメント最大64KBで、ファイル先頭付近にある。先頭だけ読めば足りる
        private const int _headerReadLimit = 256 * 1024;

        // 撮影日時を読む。撮影日時が無ければデジタル化日時、それも無ければ変更日時を使う
        public static Boolean TryRead(String path, out DateTime dateTaken)
        {
            return TryRead(ReadHeader(path), out dateTaken);
        }

        internal static Boolean TryRead(byte[] data, out DateTime dateTaken)
        {
            Dictionary<int, int> offsets = FindDateOffsets(data);
            foreach (int tag in new[] { TagDateTimeOriginal, TagDateTimeDigitized, TagDateTime })
            {
                int offset;
                if (offsets.TryGetValue(tag, out offset)
                    && DateTime.TryParseExact(Encoding.ASCII.GetString(data, offset, _dateLength), _dateFormat,
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTaken))
                {
                    return true;
                }
            }

            dateTaken = DateTime.MinValue;
            return false;
        }

        // EXIFにある日時の欄(撮影日時・デジタル化日時・変更日時のうち存在するもの)をすべてdateTakenにする。
        // 戻り値は書き換えた欄の数。0ならJPEGでない・EXIFが無い・日時の欄が無いのいずれか(ファイルは触らない)
        public static int Write(String path, DateTime dateTaken)
        {
            Dictionary<int, int> offsets = FindDateOffsets(ReadHeader(path));
            if (offsets.Count == 0)
            {
                return 0;
            }

            byte[] text = FormatDate(dateTaken);
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read))
            {
                foreach (int offset in offsets.Values)
                {
                    stream.Seek(offset, SeekOrigin.Begin);
                    stream.Write(text, 0, text.Length);
                }
            }
            return offsets.Count;
        }

        // Writeのメモリ上版(テスト用)。data自体を書き換える
        internal static int Write(byte[] data, DateTime dateTaken)
        {
            Dictionary<int, int> offsets = FindDateOffsets(data);
            byte[] text = FormatDate(dateTaken);
            foreach (int offset in offsets.Values)
            {
                Array.Copy(text, 0, data, offset, text.Length);
            }
            return offsets.Count;
        }

        private static byte[] FormatDate(DateTime dateTaken)
        {
            return Encoding.ASCII.GetBytes(dateTaken.ToString(_dateFormat, CultureInfo.InvariantCulture));
        }

        private static byte[] ReadHeader(String path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int length = (int)Math.Min(stream.Length, _headerReadLimit);
                byte[] buffer = new byte[length];
                int read = 0;
                while (read < length)
                {
                    int count = stream.Read(buffer, read, length - read);
                    if (count <= 0)
                    {
                        break;
                    }
                    read += count;
                }
                if (read < length)
                {
                    Array.Resize(ref buffer, read);
                }
                return buffer;
            }
        }

        // JPEGの先頭部分から、EXIFの日時文字列がある位置(dataの先頭からのバイト位置)をタグ番号ごとに返す。
        // 壊れたデータでも例外にせず、見つかった分だけ返す
        internal static Dictionary<int, int> FindDateOffsets(byte[] data)
        {
            Dictionary<int, int> result = new Dictionary<int, int>();
            if (data == null || data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            {
                return result;
            }

            int pos = 2;
            while (pos + 4 <= data.Length)
            {
                if (data[pos] != 0xFF)
                {
                    break;
                }

                byte marker = data[pos + 1];
                if (marker == 0xFF)
                {
                    // 詰め物の0xFF
                    pos++;
                    continue;
                }
                if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD8))
                {
                    // 長さを持たないマーカー
                    pos += 2;
                    continue;
                }
                if (marker == 0xDA || marker == 0xD9)
                {
                    // SOS(ここから画像データ)/EOI。EXIFはこれより前にしか無い
                    break;
                }

                int length = (data[pos + 2] << 8) | data[pos + 3];
                if (length < 2)
                {
                    break;
                }
                int segmentStart = pos + 4;
                int segmentEnd = pos + 2 + length;
                if (segmentEnd > data.Length)
                {
                    segmentEnd = data.Length;
                }

                if (marker == 0xE1 && IsExifHeader(data, segmentStart, segmentEnd))
                {
                    new TiffReader(data, segmentStart + 6, segmentEnd).CollectDateOffsets(result);
                    return result;
                }
                pos = pos + 2 + length;
            }
            return result;
        }

        private static Boolean IsExifHeader(byte[] data, int start, int end)
        {
            byte[] header = { (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0 };
            if (start + header.Length > end)
            {
                return false;
            }
            for (int i = 0; i < header.Length; i++)
            {
                if (data[start + i] != header[i])
                {
                    return false;
                }
            }
            return true;
        }

        // EXIF(APP1)の中身のTIFF構造を読む。位置はすべてTIFFヘッダの先頭(_tiffStart)からの相対値
        private class TiffReader
        {
            private readonly byte[] _data;
            private readonly int _tiffStart;
            private readonly int _limit;
            private Boolean _isLittleEndian;

            public TiffReader(byte[] data, int tiffStart, int limit)
            {
                _data = data;
                _tiffStart = tiffStart;
                _limit = limit;
            }

            public void CollectDateOffsets(Dictionary<int, int> result)
            {
                if (_tiffStart + 8 > _limit)
                {
                    return;
                }
                if (_data[_tiffStart] == 'I' && _data[_tiffStart + 1] == 'I')
                {
                    _isLittleEndian = true;
                }
                else if (_data[_tiffStart] == 'M' && _data[_tiffStart + 1] == 'M')
                {
                    _isLittleEndian = false;
                }
                else
                {
                    return;
                }
                if (ReadUInt16(_tiffStart + 2) != 42)
                {
                    return;
                }

                long exifIfdOffset = ReadIfd(ReadUInt32(_tiffStart + 4), TagDateTime, result);
                if (exifIfdOffset > 0)
                {
                    ReadIfd(exifIfdOffset, -1, result);
                }
            }

            // IFD(タグの一覧)を読み、日時タグの文字列の位置をresultに入れる。
            // dateTagがIFD0用(TagDateTime)のときはExif IFDへのポインタも探して返す(無ければ0)
            private long ReadIfd(long ifdOffset, int dateTag, Dictionary<int, int> result)
            {
                long exifIfdOffset = 0;
                long ifdPos = _tiffStart + ifdOffset;
                if (ifdOffset <= 0 || ifdPos + 2 > _limit)
                {
                    return 0;
                }

                int entryCount = ReadUInt16((int)ifdPos);
                for (int i = 0; i < entryCount; i++)
                {
                    long entryPos = ifdPos + 2 + 12L * i;
                    if (entryPos + 12 > _limit)
                    {
                        break;
                    }
                    int entry = (int)entryPos;
                    int tag = ReadUInt16(entry);
                    int type = ReadUInt16(entry + 2);
                    long count = ReadUInt32(entry + 4);

                    if (tag == _tagExifIfdPointer && dateTag == TagDateTime)
                    {
                        exifIfdOffset = ReadUInt32(entry + 8);
                        continue;
                    }

                    Boolean isDateTag = (dateTag == TagDateTime)
                        ? tag == TagDateTime
                        : (tag == TagDateTimeOriginal || tag == TagDateTimeDigitized);
                    if (!isDateTag || type != _typeAscii || count < _dateLength || result.ContainsKey(tag))
                    {
                        continue;
                    }

                    // 4バイトを超える値は、値の欄に値の置き場所(オフセット)が入っている
                    long valuePos = _tiffStart + ReadUInt32(entry + 8);
                    if (valuePos + _dateLength <= _limit)
                    {
                        result[tag] = (int)valuePos;
                    }
                }
                return exifIfdOffset;
            }

            private int ReadUInt16(int pos)
            {
                return _isLittleEndian
                    ? _data[pos] | (_data[pos + 1] << 8)
                    : (_data[pos] << 8) | _data[pos + 1];
            }

            private long ReadUInt32(int pos)
            {
                return _isLittleEndian
                    ? (long)_data[pos] | ((long)_data[pos + 1] << 8) | ((long)_data[pos + 2] << 16) | ((long)_data[pos + 3] << 24)
                    : ((long)_data[pos] << 24) | ((long)_data[pos + 1] << 16) | ((long)_data[pos + 2] << 8) | (long)_data[pos + 3];
            }
        }
    }
}
