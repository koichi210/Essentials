using System;
using System.Windows;
using System.Windows.Controls;
using StandardTemplate;

namespace FiddleAroundString
{
    /// <summary>
    /// 「データ加工」タブ: 貼り付けた文字列の重複行を削除する/全角英数字・スペースを半角にする。
    /// 旧ToyingData(CS_Wpf\ToyingData)のMainWindowを、タブに載せるためUserControlにしたもの(処理は変えていない)。
    /// </summary>
    public partial class ToyingDataView : UserControl
    {
        private readonly StcUtils _util = new StcUtils();

        // 結果をクリップボードへ入れる処理。テストで本物のクリップボードを書き換えないよう差し替えられるようにしてある
        // (既定はStcUtils.SetClipboardText。他アプリが掴んでいても数回リトライして落ちない)
        internal Func<String, Boolean> ClipboardWriter { get; set; }

        public ToyingDataView()
        {
            InitializeComponent();
            ClipboardWriter = _util.SetClipboardText;

            // WinForms版ではCtrl+Aの全選択をKeyDownで自前処理していたが、WPFのTextBoxは標準でCtrl+Aが全選択なので不要
        }

        // WPFのTextBoxは自前でDragOverを処理してファイルのドロップを拒否してしまうため、Previewで横取りする。
        // ファイル以外(文字列のドラッグ等)はTextBox標準の処理に任せる
        private void textBox_Source_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void textBox_Source_PreviewDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return;
            }
            e.Handled = true;
            SetDroppedFiles(e.Data.GetData(DataFormats.FileDrop) as String[]);
        }

        // ドロップされた全ファイルのパスを改行区切りで入力欄に入れる(中身は読まない)。
        // TextBoxEx(先頭の1件だけ・フォルダに変換)ではなく素のTextBoxにしているのは、全件をそのまま並べたいため
        internal void SetDroppedFiles(String[] paths)
        {
            if (paths == null || paths.Length == 0)
            {
                return;
            }
            textBox_Source.Text = _util.ChangeStrArray2Linear(paths, Environment.NewLine);
        }

        private void button_Execute_Click(object sender, RoutedEventArgs e)
        {
            Execute();
        }

        internal void Execute()
        {
            // WPFのTextBoxもEnterで入る改行はEnvironment.NewLine(CRLF)なので、WinForms版と同じ区切りで分けられる
            String[] sourceLines = _util.ChangeStrLinear2Array(textBox_Source.Text, Environment.NewLine);
            String[] destLines = null;
            if (radioButton_DeleteDuplicate.IsChecked == true)
            {
                destLines = _util.TrimDuplication(sourceLines);
            }
            else if (radioButton_ChangeWide2Narrow.IsChecked == true)
            {
                destLines = ChangeWide2Narrow(sourceLines);
            }
            String destText = _util.ChangeStrArray2Linear(destLines, Environment.NewLine);
            textBox_Dest.Text = destText;

            if (destText != String.Empty)
            {
                ClipboardWriter(destText);
            }
        }

        private String[] ChangeWide2Narrow(String[] lines)
        {
            String regexPattern;
            if (!ToyingDataLogic.TryGetRegexPattern(
                checkBox_Wide2Narrow_Number.IsChecked == true,
                checkBox_Wide2Narrow_Alpha_Large.IsChecked == true,
                checkBox_Wide2Narrow_Alpha_Small.IsChecked == true,
                checkBox_Wide2Narrow_Space.IsChecked == true,
                out regexPattern))
            {
                MessageBox.Show("変換対象が選ばれませんでした");
                return null;
            }

            return ToyingDataLogic.ApplyWide2Narrow(lines, regexPattern);
        }
    }
}
