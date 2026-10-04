// dataGrid_Events(レコード表)の1行分。WinForms版はDataGridViewのセルに直接値を入れていたが、
// WPFのDataGridはItemsSourceのオブジェクトを表示する方式なので、行データのクラスを用意した。
//
// 列の構成はWinForms版と同じ: 表示するのはEvent(Type)/Detail/備考(Remarks)の3列で、
// 実データのX/Y/Key/Wait(Delay)は非表示(WPF版では列自体を作らず、この行クラスだけが持つ)。
// Detail列と非表示の実データの相互同期(WinForms版のdataGridView_Events_CellValueChangedでやっていた処理)は、
// この行クラスの中で行う:
//   - Type / X / Y / Key / Wait を書き換えたら、Detail列の表示を作り直す(SyncDetailFromHiddenColumns)
//   - Detail列を書き換えたら、X / Y / Key(WAIT_MS行ならWait)へ書き戻す(SyncHiddenColumnsFromDetail)
// 連動して変わったセルは、CellChangedでまとめて1回通知する(Undo/Redoでも1回でまとめて戻せるように)。
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace EventRecorder
{
    internal sealed class EventRow : INotifyPropertyChanged, IEditableGridRow, INumberedGridRow
    {
        public const String ColType = "Type";
        public const String ColDetail = "Detail";
        public const String ColWait = "Wait";
        public const String ColX = "X";
        public const String ColY = "Y";
        public const String ColKey = "Key";
        public const String ColRemarks = "Remarks";

        private String _type = "";
        private String _detail = "";
        private String _wait = "";
        private String _x = "";
        private String _y = "";
        private String _key = "";
        private String _remarks = "";

        private int _rowNumber;
        private Boolean _isHighlighted;

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<GridCellChangedEventArgs> CellChanged;

        public String Type { get { return _type; } set { SetCell(ColType, value); } }
        public String Detail { get { return _detail; } set { SetCell(ColDetail, value); } }
        public String Wait { get { return _wait; } set { SetCell(ColWait, value); } }
        public String X { get { return _x; } set { SetCell(ColX, value); } }
        public String Y { get { return _y; } set { SetCell(ColY, value); } }
        public String Key { get { return _key; } set { SetCell(ColKey, value); } }
        public String Remarks { get { return _remarks; } set { SetCell(ColRemarks, value); } }

        // 行ヘッダーに表示する行番号(0始まり、WinForms版のRowPostPaintと同じ)
        public int RowNumber
        {
            get { return _rowNumber; }
            set
            {
                if (_rowNumber != value)
                {
                    _rowNumber = value;
                    Notify("RowNumber");
                }
            }
        }

        // 記録中の最新行・再生中の実行中行のハイライト(薄い黄色)
        public Boolean IsHighlighted
        {
            get { return _isHighlighted; }
            set
            {
                if (_isHighlighted != value)
                {
                    _isHighlighted = value;
                    Notify("IsHighlighted");
                }
            }
        }

        // Detail列の警告/エラー表示(WinForms版のCellFormattingでのMistyRose+ErrorText相当)。
        // 再生時に読めない値(エラー)を優先し、無ければ「マウス行なのにKeyがある」警告を出す
        public String DetailProblemText
        {
            get
            {
                String errorMessage;
                if (EventRules.IsRowInvalidForPlayback(_type, _x, _y, _key, _wait, out errorMessage))
                {
                    return errorMessage;
                }

                String warningMessage;
                if (EventRules.IsKeyIgnoredOnMouseRow(_type, _key, out warningMessage))
                {
                    return warningMessage;
                }

                return null;
            }
        }

        public Boolean HasDetailProblem
        {
            get { return DetailProblemText != null; }
        }

        public Boolean IsInvalidForPlayback(out String message)
        {
            return EventRules.IsRowInvalidForPlayback(_type, _x, _y, _key, _wait, out message);
        }

        // 保存ファイル(MacroEventData)の値からまとめて作る。Detailは実データから作り直す
        public static EventRow FromData(String type, String x, String y, String key, String wait, String remarks)
        {
            EventRow row = new EventRow();
            row._type = type ?? "";
            row._x = x ?? "";
            row._y = y ?? "";
            row._key = key ?? "";
            row._wait = wait ?? "";
            row._remarks = remarks ?? "";
            row._detail = row.ComputeDetail();
            return row;
        }

        public String GetCell(String name)
        {
            switch (name)
            {
                case ColType: return _type;
                case ColDetail: return _detail;
                case ColWait: return _wait;
                case ColX: return _x;
                case ColY: return _y;
                case ColKey: return _key;
                case ColRemarks: return _remarks;
                default: throw new ArgumentException("不明な列名: " + name);
            }
        }

        public void SetCell(String name, String value)
        {
            List<GridCellChange> changes = new List<GridCellChange>();
            Store(name, value, changes);
            if (changes.Count == 0)
            {
                return;
            }

            switch (name)
            {
                case ColType:
                case ColX:
                case ColY:
                case ColKey:
                case ColWait:
                    // Event列(WAIT_MS行への/からの切り替え等)や実データが変わったら、Detail列の表示を追従させる
                    Store(ColDetail, ComputeDetail(), changes);
                    break;

                case ColDetail:
                    // ユーザーがDetail列を直接編集した時は、逆に実データへ書き戻す
                    if (EventRules.IsWaitEventType(_type))
                    {
                        String parsedWait;
                        EventRules.TryParseWaitDetail(_detail, out parsedWait);
                        Store(ColWait, parsedWait, changes);
                    }
                    else
                    {
                        String parsedX, parsedY, parsedKey;
                        EventRules.ParseDetail(_detail, out parsedX, out parsedY, out parsedKey);
                        Store(ColX, parsedX, changes);
                        Store(ColY, parsedY, changes);
                        Store(ColKey, parsedKey, changes);
                    }
                    break;
            }

            RaiseCellChanged(changes);
        }

        public void SetCellRaw(String name, String value)
        {
            List<GridCellChange> changes = new List<GridCellChange>();
            Store(name, value, changes);
            if (changes.Count > 0)
            {
                RaiseCellChanged(changes);
            }
        }

        private String ComputeDetail()
        {
            if (EventRules.IsWaitEventType(_type))
            {
                return EventRules.FormatWaitDetail(_wait);
            }
            return EventRules.FormatDetail(_x, _y, _key);
        }

        // 値を1つ書き換える(変わらなければ何もしない)。WinForms版のセルのnullは空文字として扱う
        private void Store(String name, String value, List<GridCellChange> changes)
        {
            String newValue = value ?? "";
            String oldValue = GetCell(name);
            if (oldValue == newValue)
            {
                return;
            }

            switch (name)
            {
                case ColType: _type = newValue; break;
                case ColDetail: _detail = newValue; break;
                case ColWait: _wait = newValue; break;
                case ColX: _x = newValue; break;
                case ColY: _y = newValue; break;
                case ColKey: _key = newValue; break;
                case ColRemarks: _remarks = newValue; break;
            }

            changes.Add(new GridCellChange(name, oldValue, newValue));
            Notify(name);
        }

        private void RaiseCellChanged(List<GridCellChange> changes)
        {
            // Detail列の警告表示は、どの列が変わっても判定し直す(WinForms版のInvalidateCell相当)
            Notify("DetailProblemText");
            Notify("HasDetailProblem");
            CellChanged?.Invoke(this, new GridCellChangedEventArgs(changes));
        }

        private void Notify(String propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
