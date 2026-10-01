using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace EventRecorderForWpf
{
    // Ctrl+F(検索)/Ctrl+H(置換)共通のダイアログ(WinForms版FindReplaceFormのWPF版)。
    // 検索モードは、レコード表(dataGrid_Events)と同じ列構成(行番号+可視列)でヒットした行を一覧表示し、
    // ヒットしたセル自体は薄い黄色でハイライトする。置換モードは、次を検索/置換/すべて置換の従来型ダイアログとして動く。
    // 検索・置換のロジックそのものは[[FindReplaceEngine]]にある。
    //
    // レイアウトはWinForms版と同じく上部(topPanel)/中央(resultsGrid)/下部(bottomPanel)の3つの帯で組む
    internal class FindReplaceWindow : DialogWindowBase
    {
        private readonly DataGrid targetGrid;
        private readonly FindReplaceEngine engine;

        private readonly RadioButton radioFind;
        private readonly RadioButton radioReplace;
        private readonly TextBlock lblReplace;
        private readonly TextBox txtFind;
        private readonly TextBox txtReplace;
        private readonly CheckBox chkMatchCase;
        private readonly Button btnSearch;
        private readonly Button btnFindNext;
        private readonly Button btnReplace;
        private readonly Button btnReplaceAll;
        private readonly DataGrid resultsGrid;
        private readonly TextBlock lblStatus;

        // 検索モード・置換モードとも幅は共通。高さだけ、検索モードは結果一覧の分だけ余計に確保する(クライアント領域のサイズ)
        private const double PanelWidth = 460;
        private const double FindModeHeight = 420;
        private const double ReplaceModeHeight = 170;

        // 検索結果一覧の1行(表示用)
        internal sealed class ResultItem
        {
            public int RowNo { get; set; }
            public String[] Values { get; set; }
            public Boolean[] Hits { get; set; }
            public FindHit Hit { get; set; }
        }

        public FindReplaceWindow(DataGrid grid, IList<EventRow> rows, GridUndoRedo<EventRow> undo, FindReplaceMode initialMode, String initialSearchText)
        {
            targetGrid = grid;
            engine = new FindReplaceEngine(
                () => rows.Cast<IEditableGridRow>().ToList(),
                () => WpfGridHelper.GetVisibleColumnsInDisplayOrder(targetGrid).Select(WpfGridHelper.GetColumnName).ToList(),
                undo.BeginUndoBatch,
                undo.EndUndoBatch);

            MinWidth = 360;
            MinHeight = 200;

            // ***** 上段: ラジオボタン+検索/置換の入力欄+モード別ボタン *****
            Canvas topPanel = new Canvas { Height = 120 };
            DockPanel.SetDock(topPanel, Dock.Top);

            radioFind = new RadioButton { Content = "検索" };
            radioReplace = new RadioButton { Content = "置換" };
            TextBlock lblFind = new TextBlock { Text = "検索文字列:" };
            txtFind = new TextBox { Width = 220, Text = initialSearchText ?? "" };
            lblReplace = new TextBlock { Text = "置換後の文字列:" };
            txtReplace = new TextBox { Width = 200 };
            chkMatchCase = new CheckBox { Content = "大文字/小文字を区別する" };
            btnSearch = new Button { Content = "検索(_S)", Width = 90, Height = 23 };
            btnFindNext = new Button { Content = "次を検索(_N)", Width = 90, Height = 23 };
            btnReplace = new Button { Content = "置換(_R)", Width = 90, Height = 23 };
            btnReplaceAll = new Button { Content = "すべて置換(_A)", Width = 90, Height = 23 };

            Place(topPanel, radioFind, 10, 8);
            Place(topPanel, radioReplace, 80, 8);
            Place(topPanel, lblFind, 10, 38);
            Place(topPanel, txtFind, 90, 34);
            Place(topPanel, lblReplace, 10, 66);
            Place(topPanel, txtReplace, 110, 62);
            Place(topPanel, chkMatchCase, 10, 94);
            // 検索モード用/置換モード用のボタンは、それぞれの行の右端(X=320)に置き、ApplyModeで表示を切り替える
            Place(topPanel, btnSearch, 320, 32);
            Place(topPanel, btnFindNext, 320, 32);
            Place(topPanel, btnReplace, 320, 60);
            Place(topPanel, btnReplaceAll, 320, 88);

            // ***** 下段: ステータス表示。閉じるボタンは置かない(Escキーで閉じられる) *****
            Canvas bottomPanel = new Canvas { Height = 40 };
            DockPanel.SetDock(bottomPanel, Dock.Bottom);
            lblStatus = new TextBlock();
            Place(bottomPanel, lblStatus, 10, 10);

            // ***** 中段: 検索結果一覧(検索モードのみ表示、残り全体を使う) *****
            resultsGrid = new DataGrid
            {
                IsReadOnly = true,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserResizeRows = false,
                CanUserSortColumns = false,
                CanUserReorderColumns = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
            };
            resultsGrid.MouseDoubleClick += ResultsGrid_MouseDoubleClick;

            DockPanel root = new DockPanel { LastChildFill = true };
            root.Children.Add(topPanel);
            root.Children.Add(bottomPanel);
            root.Children.Add(resultsGrid);
            Content = root;

            radioFind.Checked += (s, e) => ApplyMode();
            radioReplace.Checked += (s, e) => ApplyMode();
            btnSearch.Click += (s, e) => DoSearch();
            btnFindNext.Click += (s, e) => FindNext();
            btnReplace.Click += (s, e) => ReplaceCurrent();
            btnReplaceAll.Click += (s, e) => ReplaceAll();
            chkMatchCase.Checked += (s, e) => engine.MatchCase = true;
            chkMatchCase.Unchecked += (s, e) => engine.MatchCase = false;

            txtFind.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter)
                {
                    return;
                }

                e.Handled = true;
                if (radioFind.IsChecked == true)
                {
                    DoSearch();
                }
                else
                {
                    FindNext();
                }
            };

            Loaded += (s, e) =>
            {
                txtFind.Focus();
                txtFind.SelectAll();
            };

            SetMode(initialMode);

            if (!String.IsNullOrEmpty(initialSearchText) && initialMode == FindReplaceMode.Find)
            {
                DoSearch();
            }
        }

        private static void Place(Canvas canvas, UIElement element, double left, double top)
        {
            Canvas.SetLeft(element, left);
            Canvas.SetTop(element, top);
            canvas.Children.Add(element);
        }

        // Ctrl+F/Ctrl+Hが押されるたびに、開き直さずこのメソッドでモードだけ切り替える
        internal void SetMode(FindReplaceMode mode)
        {
            radioFind.IsChecked = mode == FindReplaceMode.Find;
            radioReplace.IsChecked = mode == FindReplaceMode.Replace;
            ApplyMode();
        }

        private void ApplyMode()
        {
            Boolean isFind = radioFind.IsChecked == true;

            Title = isFind ? "検索" : "置換";

            // WinForms版のClientSize指定と同じく、クライアント領域の大きさに枠の分を足してウィンドウの大きさにする
            Thickness border = SystemParameters.WindowResizeBorderThickness;
            Width = PanelWidth + border.Left + border.Right;
            Height = (isFind ? FindModeHeight : ReplaceModeHeight) + SystemParameters.WindowCaptionHeight + border.Top + border.Bottom;

            Visibility replaceOnly = isFind ? Visibility.Collapsed : Visibility.Visible;
            lblReplace.Visibility = replaceOnly;
            txtReplace.Visibility = replaceOnly;
            btnFindNext.Visibility = replaceOnly;
            btnReplace.Visibility = replaceOnly;
            btnReplaceAll.Visibility = replaceOnly;
            btnSearch.Visibility = isFind ? Visibility.Visible : Visibility.Collapsed;
            resultsGrid.Visibility = isFind ? Visibility.Visible : Visibility.Collapsed;

            // 検索ボタン/次を検索ボタンのどちらをEnter(既定ボタン)の対象にするかは、txtFindのEnter処理で振り分けている
            lblStatus.Text = "";
        }

        // レコード表と同じ列構成(行番号+可視列)で検索結果を一覧表示する。
        // ヒットしたセルはLightYellowでハイライトし、どこがヒットしたか一目で分かるようにする
        private void DoSearch()
        {
            resultsGrid.ItemsSource = null;
            resultsGrid.Columns.Clear();

            String keyword = txtFind.Text;
            if (String.IsNullOrEmpty(keyword))
            {
                lblStatus.Text = "";
                return;
            }

            List<DataGridColumn> visibleColumns = WpfGridHelper.GetVisibleColumnsInDisplayOrder(targetGrid);

            resultsGrid.Columns.Add(new DataGridTextColumn { Header = "行", Binding = new Binding("RowNo"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            for (int vi = 0; vi < visibleColumns.Count; vi++)
            {
                Style cellStyle = new Style(typeof(DataGridCell));
                DataTrigger hitTrigger = new DataTrigger { Binding = new Binding("Hits[" + vi + "]"), Value = true };
                hitTrigger.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.LightYellow));
                cellStyle.Triggers.Add(hitTrigger);

                resultsGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = visibleColumns[vi].Header,
                    Binding = new Binding("Values[" + vi + "]"),
                    CellStyle = cellStyle,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                });
            }

            List<ResultItem> items = new List<ResultItem>();
            foreach (FindHit hit in engine.Search(keyword))
            {
                Boolean[] hits = new Boolean[hit.Values.Length];
                foreach (int vi in hit.HitVisibleColumnIndexes)
                {
                    hits[vi] = true;
                }

                // 行番号はレコード表の行ヘッダーの見た目(0始まり)に合わせる。+1すると実際の行と1つずれてしまうので注意
                items.Add(new ResultItem { RowNo = hit.TargetRowIndex, Values = hit.Values, Hits = hits, Hit = hit });
            }

            resultsGrid.ItemsSource = items;
            lblStatus.Text = items.Count + " 件ヒット";
        }

        // 検索結果一覧をダブルクリックしたら、本体グリッドの該当セル(最初にヒットした列)へジャンプする
        private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (WpfGridHelper.GetRowIndexFromSource(resultsGrid, e.OriginalSource) < 0)
            {
                return;
            }

            ResultItem item = resultsGrid.SelectedItem as ResultItem;
            if (item == null)
            {
                return;
            }

            List<DataGridColumn> visibleColumns = WpfGridHelper.GetVisibleColumnsInDisplayOrder(targetGrid);
            int vi = item.Hit.HitVisibleColumnIndexes[0];
            if (vi < visibleColumns.Count)
            {
                WpfGridHelper.JumpToCell(targetGrid, item.Hit.TargetRowIndex, visibleColumns[vi]);
            }
        }

        private Boolean FindNext()
        {
            String status;
            Boolean found = engine.FindNext(txtFind.Text, out status);
            if (found)
            {
                List<DataGridColumn> visibleColumns = WpfGridHelper.GetVisibleColumnsInDisplayOrder(targetGrid);
                if (engine.LastFoundColumnIndex < visibleColumns.Count)
                {
                    WpfGridHelper.JumpToCell(targetGrid, engine.LastFoundRowIndex, visibleColumns[engine.LastFoundColumnIndex]);
                }
            }
            if (status != null)
            {
                lblStatus.Text = status;
            }
            return found;
        }

        // 直前の「次を検索」で選択したセルが検索文字列にマッチしていれば置換して、続けて次を検索する
        private void ReplaceCurrent()
        {
            if (String.IsNullOrEmpty(txtFind.Text))
            {
                return;
            }

            engine.ReplaceCurrent(txtFind.Text, txtReplace.Text);
            FindNext();
        }

        private void ReplaceAll()
        {
            if (String.IsNullOrEmpty(txtFind.Text))
            {
                lblStatus.Text = "検索文字列を入力してね";
                return;
            }

            int replacedCount = engine.ReplaceAll(txtFind.Text, txtReplace.Text);
            lblStatus.Text = replacedCount + " 件置換したよ";
        }
    }
}
