// WinForms版の DataGridViewEx([[_Common/DataGridViewEx.cs]]) が持っていた Ctrl+Z(元に戻す)/Ctrl+Y(やり直す) を、
// WPF の DataGrid(ItemsSource = 行オブジェクトの ObservableCollection)向けに作り直したもの。
// DataGridViewEx は WinForms 専用のため、WPF版では「グリッド」ではなく「行データ」の変更を追跡する。
//
// ■対応範囲(DataGridViewExと同じ)
// セルの値の変更を追跡する。手入力での編集も、コードからの代入(貼り付け・Deleteキーでのクリア・置換等)も対象。
// 行の追加/削除そのものの取り消しには対応していない。行が削除(または並べ替え)されたら、
// 古い履歴が意味を失うので安全のため履歴をクリアする(DataGridViewExのRowsRemovedと同じ)。
//
// ■複数セルをまとめて1回のUndoで戻したい場合
// BeginUndoBatch()/EndUndoBatch()で挟む。挟まなければ、1回の値変更ごとに別々のUndo単位になる。
//
// ■DataGridViewExとの違い
// DataGridViewExは(行番号,列番号)で履歴を持っていたため、行の挿入で位置がずれることがあったが、
// こちらは行オブジェクトそのものを覚えるので挿入があってもずれない。
// また、行側の同期処理(例: Detail列を書き換えたら非表示のX/Y/Keyも書き換わる)で連動して変わった値は、
// 元の変更と同じ1つのUndo単位にまとめて戻す(IEditableGridRow.CellChangedが一度にまとめて通知する)。
//
// (EventRecorder専用の知識は持っていないので、_Common/Wpf への切り出し候補)
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace EventRecorderForWpf
{
    // 1つのセルの値の変更(列名・変更前・変更後)
    internal sealed class GridCellChange
    {
        public String ColumnName { get; }
        public String OldValue { get; }
        public String NewValue { get; }

        public GridCellChange(String columnName, String oldValue, String newValue)
        {
            ColumnName = columnName;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }

    internal sealed class GridCellChangedEventArgs : EventArgs
    {
        // 1回の変更で連動して変わったセルすべて(先頭が操作の起点になったセル)
        public IList<GridCellChange> Changes { get; }

        public GridCellChangedEventArgs(IList<GridCellChange> changes)
        {
            Changes = changes;
        }

        public Boolean Contains(String columnName)
        {
            foreach (GridCellChange change in Changes)
            {
                if (change.ColumnName == columnName)
                {
                    return true;
                }
            }
            return false;
        }
    }

    // Undo/Redo・Deleteキーでのクリア・貼り付け・検索/置換など、列名(文字列)で値を読み書きしたい処理のための行インターフェース
    internal interface IEditableGridRow
    {
        // 値が実際に変わった時だけ通知する(同じ値の再代入では通知しない)
        event EventHandler<GridCellChangedEventArgs> CellChanged;

        String GetCell(String name);

        // 通常の書き込み(行側の同期処理も走る)
        void SetCell(String name, String value);

        // Undo/Redo用。同期処理は走らせず、その列の値だけを書き戻す(通知はする)
        void SetCellRaw(String name, String value);
    }

    internal sealed class GridUndoRedo<TRow> where TRow : class, IEditableGridRow
    {
        private sealed class Entry
        {
            public TRow Row;
            public IList<GridCellChange> Changes;
        }

        private readonly ObservableCollection<TRow> rows;
        private readonly HashSet<TRow> subscribedRows = new HashSet<TRow>();

        private readonly Stack<List<Entry>> undoStack = new Stack<List<Entry>>();
        private readonly Stack<List<Entry>> redoStack = new Stack<List<Entry>>();

        private List<Entry> pendingBatch;
        // Undo/Redoの適用中かどうか(この間に行側から来る変更通知は履歴に積まない)
        private Boolean isApplyingHistory;

        // Ctrl+Z/Ctrl+Yによる元に戻す/やり直すを有効にするかどうか。既定は有効
        public Boolean EnableUndoRedo { get; set; } = true;

        // 管理下の行のどれかでセルの値が変わった(Undo/Redoによる書き戻しも含む)
        public event Action<TRow, GridCellChangedEventArgs> RowCellChanged;

        public GridUndoRedo(ObservableCollection<TRow> rows)
        {
            this.rows = rows;
            foreach (TRow row in rows)
            {
                Subscribe(row);
            }
            rows.CollectionChanged += Rows_CollectionChanged;
        }

        public Boolean CanUndo
        {
            get { return undoStack.Count > 0; }
        }

        public Boolean CanRedo
        {
            get { return redoStack.Count > 0; }
        }

        public void BeginUndoBatch()
        {
            pendingBatch = new List<Entry>();
        }

        public void EndUndoBatch()
        {
            if (pendingBatch == null)
            {
                return;
            }

            List<Entry> batch = pendingBatch;
            pendingBatch = null;

            if (batch.Count > 0)
            {
                PushUndo(batch);
            }
        }

        // ファイルの読込直後など、それ以前の編集を誤って元に戻せてしまわないようにしたい場面で呼ぶ
        public void ClearUndoHistory()
        {
            undoStack.Clear();
            redoStack.Clear();
        }

        public void Undo()
        {
            if (undoStack.Count == 0)
            {
                return;
            }

            List<Entry> entries = undoStack.Pop();
            Apply(entries, useOldValue: true);
            redoStack.Push(entries);
        }

        public void Redo()
        {
            if (redoStack.Count == 0)
            {
                return;
            }

            List<Entry> entries = redoStack.Pop();
            Apply(entries, useOldValue: false);
            undoStack.Push(entries);
        }

        private void Apply(List<Entry> entries, Boolean useOldValue)
        {
            isApplyingHistory = true;
            try
            {
                if (useOldValue)
                {
                    // 変更した順と逆順に戻す(同じセルを複数回変えていても最初の値に戻るように)
                    for (int i = entries.Count - 1; i >= 0; i--)
                    {
                        Entry entry = entries[i];
                        for (int c = entry.Changes.Count - 1; c >= 0; c--)
                        {
                            entry.Row.SetCellRaw(entry.Changes[c].ColumnName, entry.Changes[c].OldValue);
                        }
                    }
                }
                else
                {
                    foreach (Entry entry in entries)
                    {
                        foreach (GridCellChange change in entry.Changes)
                        {
                            entry.Row.SetCellRaw(change.ColumnName, change.NewValue);
                        }
                    }
                }
            }
            finally
            {
                isApplyingHistory = false;
            }
        }

        private void PushUndo(List<Entry> entries)
        {
            undoStack.Push(entries);
            // 新しい変更が入ったら、それより後のRedo履歴は辻褄が合わなくなるので破棄する
            redoStack.Clear();
        }

        private void Subscribe(TRow row)
        {
            if (row != null && subscribedRows.Add(row))
            {
                row.CellChanged += Row_CellChanged;
            }
        }

        private void Unsubscribe(TRow row)
        {
            if (row != null && subscribedRows.Remove(row))
            {
                row.CellChanged -= Row_CellChanged;
            }
        }

        private void Rows_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            Boolean anyRemoved = false;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    foreach (TRow row in e.NewItems)
                    {
                        Subscribe(row);
                    }
                    break;

                case NotifyCollectionChangedAction.Move:
                    // DataGridViewExでは行の並べ替え=削除+挿入だったので、同じく履歴をクリアする
                    anyRemoved = true;
                    break;

                default:
                    // Remove / Replace / Reset。今コレクションに無い行の購読を外す
                    HashSet<TRow> current = new HashSet<TRow>(rows);
                    foreach (TRow row in new List<TRow>(subscribedRows))
                    {
                        if (!current.Contains(row))
                        {
                            Unsubscribe(row);
                            anyRemoved = true;
                        }
                    }
                    foreach (TRow row in rows)
                    {
                        Subscribe(row);
                    }
                    break;
            }

            if (anyRemoved)
            {
                ClearUndoHistory();
            }
        }

        private void Row_CellChanged(object sender, GridCellChangedEventArgs e)
        {
            TRow row = (TRow)sender;

            List<Entry> unit = null;
            if (EnableUndoRedo && !isApplyingHistory)
            {
                Entry entry = new Entry { Row = row, Changes = new List<GridCellChange>(e.Changes) };
                if (dispatchingUnit != null)
                {
                    // RowCellChangedの受け手(例: ファイルを選んだらループ回数を自動セット)が連動して変えた値は、
                    // きっかけになった変更と同じ1つのUndo単位にまとめる
                    dispatchingUnit.Add(entry);
                }
                else if (pendingBatch != null)
                {
                    pendingBatch.Add(entry);
                    unit = pendingBatch;
                }
                else
                {
                    unit = new List<Entry> { entry };
                    PushUndo(unit);
                }
            }

            List<Entry> previous = dispatchingUnit;
            if (unit != null)
            {
                dispatchingUnit = unit;
            }
            try
            {
                RowCellChanged?.Invoke(row, e);
            }
            finally
            {
                dispatchingUnit = previous;
            }
        }

        // RowCellChangedを通知している最中のUndo単位(この間の連動変更は同じ単位に追加する)
        private List<Entry> dispatchingUnit;
    }
}
