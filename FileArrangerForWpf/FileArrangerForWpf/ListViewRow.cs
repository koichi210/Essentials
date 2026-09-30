using System;
using System.ComponentModel;

namespace FileArrangerForWpf
{
    /// <summary>
    /// WinForms版ListViewItem(SubItems[idx].Text)の代わりに、WPFのListView(GridView)へ入れる1行分のデータ。
    /// 列の値はインデクサ(row[idx])で読み書きし、XAML側は DisplayMemberBinding="{Binding [idx]}" で表示する。
    /// 値を書き換えると画面にも反映される(WinFormsのSubItems[idx].Text = ...と同じ使い勝手)。
    /// </summary>
    internal class ListViewRow : INotifyPropertyChanged
    {
        private readonly String[] columns;

        public event PropertyChangedEventHandler PropertyChanged;

        public ListViewRow(params String[] Columns)
        {
            columns = (String[])Columns.Clone();
        }

        public int ColumnCount
        {
            get { return columns.Length; }
        }

        public String this[int index]
        {
            get { return columns[index]; }
            set
            {
                if (columns[index] == value)
                {
                    return;
                }
                columns[index] = value;
                PropertyChangedEventHandler handler = PropertyChanged;
                if (handler != null)
                {
                    handler(this, new PropertyChangedEventArgs("Item[]"));
                }
            }
        }

        // WinForms版ListViewItemのTextは先頭列の値だった(ソートや既定の表示に使われる)
        public override String ToString()
        {
            return columns.Length > 0 ? columns[0] : "";
        }
    }
}
