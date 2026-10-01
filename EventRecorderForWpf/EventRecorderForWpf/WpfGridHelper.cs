// WPFのDataGridで、WinForms版DataGridViewの操作(HitTest・SelectedCells・FirstDisplayedScrollingRowIndex・
// CurrentCell・IsCurrentCellInEditMode等)に相当することをするための小さなヘルパー集。
// 列と行データの対応は「列のSortMemberPath = 行クラスの列名(IEditableGridRow.GetCell/SetCellの名前)」という約束にしてある。
//
// (EventRecorder専用の知識は持っていないので、_Common/Wpf への切り出し候補)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EventRecorderForWpf
{
    internal static class WpfGridHelper
    {
        public static T FindAncestor<T>(DependencyObject start) where T : DependencyObject
        {
            DependencyObject current = start;
            while (current != null)
            {
                T found = current as T;
                if (found != null)
                {
                    return found;
                }

                // Run等のContentElementはビジュアルツリーに居ないので論理ツリーで辿る
                current = (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }
            return null;
        }

        public static T FindDescendant<T>(DependencyObject start) where T : DependencyObject
        {
            if (start == null)
            {
                return null;
            }

            int count = VisualTreeHelper.GetChildrenCount(start);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(start, i);
                T found = (child as T) ?? FindDescendant<T>(child);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        // マウスイベント等の発生元(OriginalSource)が、gridのどの行(ItemsSource上のインデックス)の上かを返す。行の上でなければ-1
        public static int GetRowIndexFromSource(DataGrid grid, object originalSource)
        {
            DependencyObject source = originalSource as DependencyObject;
            if (source == null)
            {
                return -1;
            }

            DataGridRow row = FindAncestor<DataGridRow>(source);
            if (row == null || FindAncestor<DataGrid>(row) != grid)
            {
                return -1;
            }

            return grid.ItemContainerGenerator.IndexFromContainer(row);
        }

        // 発生元が載っているセル(無ければnull)
        public static DataGridCell GetCellFromSource(object originalSource)
        {
            DependencyObject source = originalSource as DependencyObject;
            return (source == null) ? null : FindAncestor<DataGridCell>(source);
        }

        // セルの中で編集中(テキストボックス等に入力中)かどうか。WinForms版のIsCurrentCellInEditMode相当
        public static Boolean IsEditing(DataGrid grid)
        {
            DependencyObject focused = Keyboard.FocusedElement as DependencyObject;
            if (focused == null)
            {
                return false;
            }

            DataGridCell cell = FindAncestor<DataGridCell>(focused);
            return cell != null && cell.IsEditing && FindAncestor<DataGrid>(cell) == grid;
        }

        public static String GetColumnName(DataGridColumn column)
        {
            return column.SortMemberPath;
        }

        // gridの表示列(Visible)だけを、DisplayIndex順に並べて返す(WinForms版のGetVisibleColumnsInDisplayOrder)
        public static List<DataGridColumn> GetVisibleColumnsInDisplayOrder(DataGrid grid)
        {
            return grid.Columns
                .Where(c => c.Visibility == Visibility.Visible)
                .OrderBy(c => c.DisplayIndex)
                .ToList();
        }

        // 選択中のセルが載っている行のインデックス(重複なし・昇順)
        public static List<int> GetSelectedRowIndexes(DataGrid grid)
        {
            HashSet<int> indexes = new HashSet<int>();
            foreach (DataGridCellInfo info in grid.SelectedCells)
            {
                int index = grid.Items.IndexOf(info.Item);
                if (index >= 0)
                {
                    indexes.Add(index);
                }
            }
            return indexes.OrderBy(x => x).ToList();
        }

        public static Boolean IsCellSelected(DataGrid grid, object item, DataGridColumn column)
        {
            foreach (DataGridCellInfo info in grid.SelectedCells)
            {
                if (info.Item == item && (column == null || info.Column == column))
                {
                    return true;
                }
            }
            return false;
        }

        // 行全体を単独選択にする(WinForms版の ClearSelection() + Rows[i].Selected = true)
        public static void SelectWholeRow(DataGrid grid, object item)
        {
            // 一度も表示されていない(列の表示位置が確定していない)グリッドでは選択セルを正しく扱えないので何もしない
            if (!grid.IsLoaded)
            {
                return;
            }

            grid.SelectedCells.Clear();
            foreach (DataGridColumn column in grid.Columns)
            {
                grid.SelectedCells.Add(new DataGridCellInfo(item, column));
            }
        }

        // 指定セルを選択してカレントセルにし、その行が見えるようにする(WinForms版FindReplaceForm.JumpToCell)
        public static void JumpToCell(DataGrid grid, int rowIndex, DataGridColumn column)
        {
            if (rowIndex < 0 || rowIndex >= grid.Items.Count || column == null || !grid.IsLoaded)
            {
                return;
            }

            object item = grid.Items[rowIndex];
            DataGridCellInfo info = new DataGridCellInfo(item, column);
            grid.SelectedCells.Clear();
            grid.CurrentCell = info;
            grid.SelectedCells.Add(info);
            ScrollRowToTop(grid, rowIndex);
        }

        // idx行が一番上に来るようにスクロールする(WinForms版のFirstDisplayedScrollingRowIndex = idx)。
        // DataGridは行単位スクロール(CanContentScroll)なので、縦のスクロール位置=先頭に表示する行番号になる
        public static void ScrollRowToTop(DataGrid grid, int idx)
        {
            if (idx < 0 || idx >= grid.Items.Count)
            {
                return;
            }

            ScrollViewer viewer = FindDescendant<ScrollViewer>(grid);
            if (viewer != null && viewer.CanContentScroll)
            {
                viewer.ScrollToVerticalOffset(idx);
            }
            else
            {
                grid.ScrollIntoView(grid.Items[idx]);
            }
        }
    }
}
