// dataGrid_Playlist(プレイリスト)の1行分(実行チェック/設定ファイル名/その行のループ回数)。
// WinForms版はDataGridViewのセルに直接値を入れていたが、WPFのDataGridはItemsSourceのオブジェクトを表示する方式なので、
// 行データのクラスを用意した。見た目の状態(実行中ハイライト・ファイル不在のピンク表示・フィルタでの非表示)も持つ。
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace EventRecorder
{
    internal sealed class PlaylistRow : INotifyPropertyChanged, IEditableGridRow, INumberedGridRow
    {
        public const String ColEnabled = "Enabled";
        public const String ColFileName = "FileName";
        public const String ColLoopCount = "LoopCount";

        private Boolean _enabled;
        private String _fileName = "";
        private String _loopCount = "";

        private int _rowNumber;
        private Boolean _isHighlighted;
        private Boolean _isVisible = true;
        private String _missingFileText;

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<GridCellChangedEventArgs> CellChanged;

        public Boolean Enabled
        {
            get { return _enabled; }
            set { SetCell(ColEnabled, value.ToString()); }
        }

        public String FileName { get { return _fileName; } set { SetCell(ColFileName, value); } }
        public String LoopCount { get { return _loopCount; } set { SetCell(ColLoopCount, value); } }

        public int RowNumber
        {
            get { return _rowNumber; }
            set { if (_rowNumber != value) { _rowNumber = value; Notify("RowNumber"); } }
        }

        // プレイリスト実行中に、今どのファイル(行)を再生しているかのハイライト
        public Boolean IsHighlighted
        {
            get { return _isHighlighted; }
            set { if (_isHighlighted != value) { _isHighlighted = value; Notify("IsHighlighted"); } }
        }

        // 「チェックONのみ表示」フィルタでの表示/非表示
        public Boolean IsVisible
        {
            get { return _isVisible; }
            set { if (_isVisible != value) { _isVisible = value; Notify("IsVisible"); } }
        }

        // 設定ファイルが見つからない時のメッセージ(nullなら正常)。設定ファイル列をピンク表示+ツールチップにする
        public String MissingFileText
        {
            get { return _missingFileText; }
            set
            {
                if (_missingFileText != value)
                {
                    _missingFileText = value;
                    Notify("MissingFileText");
                    Notify("IsFileMissing");
                }
            }
        }

        public Boolean IsFileMissing
        {
            get { return _missingFileText != null; }
        }

        public static PlaylistRow FromData(Boolean enabled, String fileName, String loopCount)
        {
            PlaylistRow row = new PlaylistRow();
            row._enabled = enabled;
            row._fileName = fileName ?? "";
            row._loopCount = loopCount ?? "";
            return row;
        }

        public String GetCell(String name)
        {
            switch (name)
            {
                case ColEnabled: return _enabled.ToString();
                case ColFileName: return _fileName;
                case ColLoopCount: return _loopCount;
                default: throw new ArgumentException("不明な列名: " + name);
            }
        }

        public void SetCell(String name, String value)
        {
            SetCellRaw(name, value);
        }

        public void SetCellRaw(String name, String value)
        {
            String newValue;
            if (name == ColEnabled)
            {
                // WinForms版のチェックボックスセルと同じく、null(Deleteキーでクリア)や読めない値はOFF扱い
                Boolean parsed;
                newValue = (Boolean.TryParse(value, out parsed) && parsed).ToString();
            }
            else
            {
                newValue = value ?? "";
            }

            String oldValue = GetCell(name);
            if (oldValue == newValue)
            {
                return;
            }

            switch (name)
            {
                case ColEnabled: _enabled = Boolean.Parse(newValue); break;
                case ColFileName: _fileName = newValue; break;
                case ColLoopCount: _loopCount = newValue; break;
                default: throw new ArgumentException("不明な列名: " + name);
            }

            Notify(name);
            CellChanged?.Invoke(this, new GridCellChangedEventArgs(new List<GridCellChange> { new GridCellChange(name, oldValue, newValue) }));
        }

        private void Notify(String propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
