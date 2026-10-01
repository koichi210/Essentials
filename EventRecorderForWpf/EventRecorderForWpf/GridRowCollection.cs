// DataGridのItemsSource用のObservableCollection。WinForms版で行ヘッダーに描いていた行番号
// (RowPostPaintでe.RowIndexを描画、0始まり)を、各行のRowNumberプロパティとして持たせて
// 行ヘッダーにバインドする。行の追加・挿入・削除・並べ替えのたびに、影響を受ける位置以降だけ振り直す。
// また、ファイル読込時のように大量の行を入れ替える時は、1行ずつ通知すると重いのでReplaceAllで1回にまとめる。
//
// (EventRecorder専用の知識は持っていないので、_Common/Wpf への切り出し候補)
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace EventRecorderForWpf
{
    internal interface INumberedGridRow
    {
        int RowNumber { get; set; }
    }

    internal sealed class GridRowCollection<T> : ObservableCollection<T> where T : INumberedGridRow
    {
        // 中身をまとめて入れ替える(通知はResetの1回だけ)
        public void ReplaceAll(IEnumerable<T> items)
        {
            CheckReentrancy();
            Items.Clear();
            foreach (T item in items)
            {
                Items.Add(item);
            }

            OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            // 行番号は通知より先に振り直しておく(通知を受けた側がすぐ正しい番号を読めるように)
            int from;
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    from = e.NewStartingIndex;
                    break;
                case NotifyCollectionChangedAction.Remove:
                    from = e.OldStartingIndex;
                    break;
                case NotifyCollectionChangedAction.Move:
                    from = Math.Min(e.OldStartingIndex, e.NewStartingIndex);
                    break;
                default:
                    from = 0;
                    break;
            }

            for (int i = Math.Max(0, from); i < Count; i++)
            {
                this[i].RowNumber = i;
            }

            base.OnCollectionChanged(e);
        }
    }
}
