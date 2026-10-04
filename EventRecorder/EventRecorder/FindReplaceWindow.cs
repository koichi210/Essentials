using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace EventRecorder
{
    // Ctrl+F(検索)/Ctrl+H(置換)共通のダイアログ(WinForms版FindReplaceFormのWPF版)。
    // 検索モードは、レコード表(dataGrid_Events)と同じ列構成(行番号+可視列)でヒットした行を一覧表示し、
    // ヒットしたセル自体は薄い黄色でハイライトする。置換モードは、次を検索/置換/すべて置換の従来型ダイアログとして動く。
    // 検索・置換のロジックそのものは[[FindReplaceEngine]]にある。
    //
    // レイアウトはWinForms版と同じく上部(topPanel)/中央(_resultsGrid)/下部(bottomPanel)の3つの帯で組む
    internal class FindReplaceWindow : DialogWindowBase
    {
        private readonly DataGrid _targetGrid;
        private readonly FindReplaceEngine _engine;

        private readonly RadioButton _radioFind;
        private readonly RadioButton _radioReplace;
        private readonly TextBlock _lblReplace;
        private readonly TextBox _txtFind;
        private readonly TextBox _txtReplace;
        private readonly CheckBox _chkMatchCase;
        private readonly Button _btnSearch;
        private readonly Button _btnFindNext;
        private readonly Button _btnReplace;
        private readonly Button _btnReplaceAll;
        private readonly DataGrid _resultsGrid;
        private readonly TextBlock _lblStatus;

        // 検索モード・置換モードとも幅は共通。高さだけ、検索モードは結果一覧の分だけ余計に確保する(クライアント領域のサイズ)
        private const double _panelWidth = 460;
        private const double _findModeHeight = 420;
        private const double _replaceModeHeight = 170;

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
            _targetGrid = grid;
            _engine = new FindReplaceEngine(
                () => rows.Cast<IEditableGridRow>().ToList(),
                () => WpfGridHelper.GetVisibleColumnsInDisplayOrder(_targetGrid).Select(WpfGridHelper.GetColumnName).ToList(),
                undo.BeginUndoBatch,
                undo.EndUndoBatch);

            MinWidth = 360;
            MinHeight = 200;

            // ***** 上段: ラジオボタン+検索/置換の入力欄+モード別ボタン *****
            Canvas topPanel = new Canvas { Height = 120 };
            DockPanel.SetDock(topPanel, Dock.Top);

            _radioFind = new RadioButton { Content = "検索" };
            _radioReplace = new RadioButton { Content = "置換" };
            TextBlock lblFind = new TextBlock { Text = "検索文字列:" };
            _txtFind = new TextBox { Width = 220, Text = initialSearchText ?? "" };
            _lblReplace = new TextBlock { Text = "置換後の文字列:" };
            _txtReplace = new TextBox { Width = 200 };
            _chkMatchCase = new CheckBox { Content = "大文字/小文字を区別する" };
            _btnSearch = CreateButton("検索(_S)");
            _btnFindNext = CreateButton("次を検索(_N)");
            _btnReplace = CreateButton("置換(_R)");
            _btnReplaceAll = CreateButton("すべて置換(_A)");

            Place(topPanel, _radioFind, 10, 8);
            Place(topPanel, _radioReplace, 80, 8);
            Place(topPanel, lblFind, 10, 38);
            Place(topPanel, _txtFind, 90, 34);
            Place(topPanel, _lblReplace, 10, 66);
            Place(topPanel, _txtReplace, 110, 62);
            Place(topPanel, _chkMatchCase, 10, 94);
            // 検索モード用/置換モード用のボタンは、それぞれの行の右端(X=320)に置き、ApplyModeで表示を切り替える
            Place(topPanel, _btnSearch, 320, 32);
            Place(topPanel, _btnFindNext, 320, 32);
            Place(topPanel, _btnReplace, 320, 60);
            Place(topPanel, _btnReplaceAll, 320, 88);

            // ***** 下段: ステータス表示。閉じるボタンは置かない(Escキーで閉じられる) *****
            Canvas bottomPanel = new Canvas { Height = 40 };
            DockPanel.SetDock(bottomPanel, Dock.Bottom);
            _lblStatus = new TextBlock();
            Place(bottomPanel, _lblStatus, 10, 10);

            // ***** 中段: 検索結果一覧(検索モードのみ表示、残り全体を使う) *****
            _resultsGrid = new DataGrid
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
            _resultsGrid.MouseDoubleClick += ResultsGrid_MouseDoubleClick;

            DockPanel root = new DockPanel { LastChildFill = true };
            root.Children.Add(topPanel);
            root.Children.Add(bottomPanel);
            root.Children.Add(_resultsGrid);
            Content = root;

            _radioFind.Checked += (s, e) => ApplyMode();
            _radioReplace.Checked += (s, e) => ApplyMode();
            _btnSearch.Click += (s, e) => DoSearch();
            _btnFindNext.Click += (s, e) => FindNext();
            _btnReplace.Click += (s, e) => ReplaceCurrent();
            _btnReplaceAll.Click += (s, e) => ReplaceAll();
            _chkMatchCase.Checked += (s, e) => _engine.MatchCase = true;
            _chkMatchCase.Unchecked += (s, e) => _engine.MatchCase = false;

            _txtFind.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter)
                {
                    return;
                }

                e.Handled = true;
                if (_radioFind.IsChecked == true)
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
                _txtFind.Focus();
                _txtFind.SelectAll();
            };

            SetMode(initialMode);

            if (!String.IsNullOrEmpty(initialSearchText) && initialMode == FindReplaceMode.Find)
            {
                DoSearch();
            }
        }

        private static Button CreateButton(String content)
        {
            return new Button { Content = content, Width = 90, Height = 23 };
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
            _radioFind.IsChecked = mode == FindReplaceMode.Find;
            _radioReplace.IsChecked = mode == FindReplaceMode.Replace;
            ApplyMode();
        }

        private void ApplyMode()
        {
            Boolean isFind = _radioFind.IsChecked == true;

            Title = isFind ? "検索" : "置換";

            // WinForms版のClientSize指定と同じく、クライアント領域の大きさに枠の分を足してウィンドウの大きさにする
            Thickness border = SystemParameters.WindowResizeBorderThickness;
            Width = _panelWidth + border.Left + border.Right;
            Height = (isFind ? _findModeHeight : _replaceModeHeight) + SystemParameters.WindowCaptionHeight + border.Top + border.Bottom;

            Visibility findOnly = isFind ? Visibility.Visible : Visibility.Collapsed;
            Visibility replaceOnly = isFind ? Visibility.Collapsed : Visibility.Visible;
            _lblReplace.Visibility = replaceOnly;
            _txtReplace.Visibility = replaceOnly;
            _btnFindNext.Visibility = replaceOnly;
            _btnReplace.Visibility = replaceOnly;
            _btnReplaceAll.Visibility = replaceOnly;
            _btnSearch.Visibility = findOnly;
            _resultsGrid.Visibility = findOnly;

            // 検索ボタン/次を検索ボタンのどちらをEnter(既定ボタン)の対象にするかは、_txtFindのEnter処理で振り分けている
            _lblStatus.Text = "";
        }

        // レコード表と同じ列構成(行番号+可視列)で検索結果を一覧表示する。
        // ヒットしたセルはLightYellowでハイライトし、どこがヒットしたか一目で分かるようにする
        private void DoSearch()
        {
            _resultsGrid.ItemsSource = null;
            _resultsGrid.Columns.Clear();

            String keyword = _txtFind.Text;
            if (String.IsNullOrEmpty(keyword))
            {
                _lblStatus.Text = "";
                return;
            }

            List<DataGridColumn> visibleColumns = WpfGridHelper.GetVisibleColumnsInDisplayOrder(_targetGrid);

            _resultsGrid.Columns.Add(new DataGridTextColumn { Header = "行", Binding = new Binding("RowNo"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            for (int vi = 0; vi < visibleColumns.Count; vi++)
            {
                Style cellStyle = new Style(typeof(DataGridCell));
                DataTrigger hitTrigger = new DataTrigger { Binding = new Binding("Hits[" + vi + "]"), Value = true };
                hitTrigger.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.LightYellow));
                cellStyle.Triggers.Add(hitTrigger);

                _resultsGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = visibleColumns[vi].Header,
                    Binding = new Binding("Values[" + vi + "]"),
                    CellStyle = cellStyle,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                });
            }

            List<ResultItem> items = new List<ResultItem>();
            foreach (FindHit hit in _engine.Search(keyword))
            {
                Boolean[] hits = new Boolean[hit.Values.Length];
                foreach (int vi in hit.HitVisibleColumnIndexes)
                {
                    hits[vi] = true;
                }

                // 行番号はレコード表の行ヘッダーの見た目(0始まり)に合わせる。+1すると実際の行と1つずれてしまうので注意
                items.Add(new ResultItem { RowNo = hit.TargetRowIndex, Values = hit.Values, Hits = hits, Hit = hit });
            }

            _resultsGrid.ItemsSource = items;
            _lblStatus.Text = items.Count + " 件ヒット";
        }

        // 検索結果一覧をダブルクリックしたら、本体グリッドの該当セル(最初にヒットした列)へジャンプする
        private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (WpfGridHelper.GetRowIndexFromSource(_resultsGrid, e.OriginalSource) < 0)
            {
                return;
            }

            ResultItem item = _resultsGrid.SelectedItem as ResultItem;
            if (item == null)
            {
                return;
            }

            List<DataGridColumn> visibleColumns = WpfGridHelper.GetVisibleColumnsInDisplayOrder(_targetGrid);
            int vi = item.Hit.HitVisibleColumnIndexes[0];
            if (vi < visibleColumns.Count)
            {
                WpfGridHelper.JumpToCell(_targetGrid, item.Hit.TargetRowIndex, visibleColumns[vi]);
            }
        }

        private Boolean FindNext()
        {
            String status;
            Boolean found = _engine.FindNext(_txtFind.Text, out status);
            if (found)
            {
                List<DataGridColumn> visibleColumns = WpfGridHelper.GetVisibleColumnsInDisplayOrder(_targetGrid);
                if (_engine.LastFoundColumnIndex < visibleColumns.Count)
                {
                    WpfGridHelper.JumpToCell(_targetGrid, _engine.LastFoundRowIndex, visibleColumns[_engine.LastFoundColumnIndex]);
                }
            }
            if (status != null)
            {
                _lblStatus.Text = status;
            }
            return found;
        }

        // 直前の「次を検索」で選択したセルが検索文字列にマッチしていれば置換して、続けて次を検索する
        private void ReplaceCurrent()
        {
            if (String.IsNullOrEmpty(_txtFind.Text))
            {
                return;
            }

            _engine.ReplaceCurrent(_txtFind.Text, _txtReplace.Text);
            FindNext();
        }

        private void ReplaceAll()
        {
            if (String.IsNullOrEmpty(_txtFind.Text))
            {
                _lblStatus.Text = "検索文字列を入力してね";
                return;
            }

            int replacedCount = _engine.ReplaceAll(_txtFind.Text, _txtReplace.Text);
            _lblStatus.Text = replacedCount + " 件置換したよ";
        }
    }
}
