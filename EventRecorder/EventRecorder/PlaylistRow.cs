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

        private Boolean enabled;
        private String fileName = "";
        private String loopCount = "";

        private int rowNumber;
        private Boolean isHighlighted;
        private Boolean isVisible = true;
        private String missingFileText;

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<GridCellChangedEventArgs> CellChanged;

        public Boolean Enabled
        {
            get { return enabled; }
            set { SetCell(ColEnabled, value.ToString()); }
        }

        public String FileName { get { return fileName; } set { SetCell(ColFileName, value); } }
        public String LoopCount { get { return loopCount; } set { SetCell(ColLoopCount, value); } }

        public int RowNumber
        {
            get { return rowNumber; }
            set { if (rowNumber != value) { rowNumber = value; Notify("RowNumber"); } }
        }

        // プレイリスト実行中に、今どのファイル(行)を再生しているかのハイライト
        public Boolean IsHighlighted
        {
            get { return isHighlighted; }
            set { if (isHighlighted != value) { isHighlighted = value; Notify("IsHighlighted"); } }
        }

        // 「チェックONのみ表示」フィルタでの表示/非表示
        public Boolean IsVisible
        {
            get { return isVisible; }
            set { if (isVisible != value) { isVisible = value; Notify("IsVisible"); } }
        }

        // 設定ファイルが見つからない時のメッセージ(nullなら正常)。設定ファイル列をピンク表示+ツールチップにする
        public String MissingFileText
        {
            get { return missingFileText; }
            set
            {
                if (missingFileText != value)
                {
                    missingFileText = value;
                    Notify("MissingFileText");
                    Notify("IsFileMissing");
                }
            }
        }

        public Boolean IsFileMissing
        {
            get { return missingFileText != null; }
        }

        public static PlaylistRow FromData(Boolean enabled, String fileName, String loopCount)
        {
            PlaylistRow row = new PlaylistRow();
            row.enabled = enabled;
            row.fileName = fileName ?? "";
            row.loopCount = loopCount ?? "";
            return row;
        }

        public String GetCell(String name)
        {
            switch (name)
            {
                case ColEnabled: return enabled.ToString();
                case ColFileName: return fileName;
                case ColLoopCount: return loopCount;
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
                case ColEnabled: enabled = Boolean.Parse(newValue); break;
                case ColFileName: fileName = newValue; break;
                case ColLoopCount: loopCount = newValue; break;
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
