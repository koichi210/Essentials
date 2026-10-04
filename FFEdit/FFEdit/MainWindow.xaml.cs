using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StandardTemplate;
using StandardTemplate.Wpf;

namespace FFEdit
{
    public partial class MainWindow : Window
    {
        // データ保存先はWinForms版FFEditと同じ(%LOCALAPPDATA%\FFEdit\)。保存キーも同じにしてあるので、
        // WinForms版で保存した設定ファイルをそのまま読める。FFEditは設定ファイルが1つだけ(プロファイル選択は無い)
        private const String _appName = "FFEdit";
        private const String _settingFileName = "FFEdit.json";

        private static readonly String[] _incrCycleArray = { "無し", "秒", "分", "時間", "日" };
        private static readonly String[] _digitArray = { "自動", "1桁", "2桁", "3桁", "4桁", "5桁", "6桁" };
        private const int _tabIdxChangeName = 0;
        private const int _tabIdxTimeStamp = 1;
        private const int _tabIdxFunction = 2;

        private readonly String _userDataFolder = UserDataLocation.GetUserDataFolder(_appName);
        private readonly StcUtils _util = new StcUtils();
        internal WpfSaveRestore SaveRestore { get; } = new WpfSaveRestore();

        private readonly Rename _rename = new Rename();
        private readonly Function _function = new Function();

        // 時刻入力欄(textBox_Time)で最後に正しく入力された時刻。不正な入力はこの値に戻す
        private TimeSpan _currentTime;

        // InitializeComponent中(XAMLのIsChecked="True"等)にイベントが走っても何もしないようにする。
        // WinForms版もデザイナーでの初期値設定ではイベントが呼ばれていなかった
        private readonly Boolean _isUiReady;

        public MainWindow()
        {
            InitializeComponent();
            _util.SetCurrentDirectory();

            RegisterSettingItems();
            LoadProc(Path.Combine(_userDataFolder, _settingFileName));

            // 桁の選択肢を生成
            foreach (String digit in _digitArray)
            {
                comboBox_ChangeNumber_Digit.Items.Add(digit);
            }
            comboBox_ChangeNumber_Digit.SelectedIndex = 0;

            // 加算間隔の選択肢を生成
            foreach (String cycle in _incrCycleArray)
            {
                comboBox_TimeSpan.Items.Add(cycle);
            }
            comboBox_TimeSpan.SelectedIndex = 1;

            // WinForms版のDateTimePickerの初期値(起動した日時)に合わせる
            DateTime now = DateTime.Now;
            dateTimePicker_Days.SelectedDate = now.Date;
            SetTime(new TimeSpan(now.Hour, now.Minute, now.Second));

            WpfDataFolderMenu.Attach(this, () => DataFolderMenu.ChangeDataFolder(_appName, _userDataFolder, MoveSettingFile));

            _isUiReady = true;
        }

        // *******************************************************************************
        // 設定の保存/読み込み(WinForms版のSaveRestore.cs)

        internal void RegisterSettingItems()
        {
            // キーはWinForms版(SaveRestore.RegisterItem)と同じにすること
            SaveRestore.RegisterCtrlList("comboBox_TargetDir", "Value_", comboBox_TargetDir);
            SaveRestore.RegisterCtrlList("comboBox_String1", "Value_", comboBox_String1);
            SaveRestore.RegisterCtrlList("comboBox_String2", "Value_", comboBox_String2);
            SaveRestore.RegisterCtrl("textBox_Target_Extension", "Value", textBox_Target_Extension, "*");
        }

        // WinForms版のSaveRestore.LoadProcと同じ。ファイル名が空なら何もせず失敗を返す。
        // 読む前にフィルターを既定値("*")にしておく。ファイルが無い・壊れている場合は既定値のままにしてfalseを返す
        internal Boolean LoadProc(String loadFileName)
        {
            if (loadFileName == String.Empty)
            {
                return false;
            }

            // Default設定
            textBox_Target_Extension.Text = "*";

            GenericProfile profile = JsonFileStorage.Load<GenericProfile>(loadFileName);
            SaveRestore.ApplyGenericProfile(profile);
            return profile != null;
        }

        // WinForms版のSaveRestore.SaveSettingと同じ。保存前に、入力中の文字列を各コンボボックスの履歴に追加する
        internal Boolean SaveSetting(String saveFileName)
        {
            if (saveFileName == String.Empty)
            {
                return false;
            }

            // コンボボックスの更新
            WpfControlUtils.AddComboBoxTextToItems(comboBox_TargetDir);
            WpfControlUtils.AddComboBoxTextToItems(comboBox_String1);
            WpfControlUtils.AddComboBoxTextToItems(comboBox_String2);

            return SaveRestore.Save(saveFileName);
        }

        private void button_SaveSetting_Click(object sender, RoutedEventArgs e)
        {
            String saveFilePath = Path.Combine(_userDataFolder, _settingFileName);
            if (!SaveSetting(saveFilePath))
            {
                MessageBox.Show("設定の保存に失敗しました" + Environment.NewLine + saveFilePath,
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show("設定値を保存しました♪" + Environment.NewLine + saveFilePath);
        }

        // Ctrl+Sで「設定保存」ボタンと同じ動作にする(テキストボックス等にフォーカスがあっても拾える)
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                button_SaveSetting_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        // *******************************************************************************
        // データ保存先フォルダの変更(システムメニューから呼び出す)
        // FFEditは設定ファイルが1つだけなので、他プロジェクトのような全プロファイルの引っ越しではなく
        // この1ファイルだけを移す
        private void MoveSettingFile(String oldFolder, String newFolder)
        {
            String oldSettingPath = Path.Combine(oldFolder, _settingFileName);
            String newSettingPath = Path.Combine(newFolder, _settingFileName);

            if (!File.Exists(oldSettingPath) || File.Exists(newSettingPath))
            {
                return;
            }

            MessageBoxResult moveResult = MessageBox.Show(
                "既存の設定ファイルを新しい保存先に移動しますか？" + Environment.NewLine + Environment.NewLine
                    + "移動元: " + oldSettingPath + Environment.NewLine
                    + "移動先: " + newSettingPath,
                _appName + " - 設定ファイルの引っ越し",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (moveResult != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                File.Move(oldSettingPath, newSettingPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "移動に失敗したよ: " + ex.Message,
                    _appName + " - 設定ファイルの引っ越し",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        // *******************************************************************************
        // 対象フォルダ・一覧

        private void comboBox_TargetDir_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _util.ExecutePath(comboBox_TargetDir.Text);
            }
        }

        private void listBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Boolean isControl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            Boolean isShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            if (e.Key == Key.A && isControl)
            {
                listBox.SelectAll();
                e.Handled = true;
            }
            else if (e.Key == Key.C && isControl && isShift)
            {
                WpfControlUtils.CopyToClipboard(listBox, comboBox_TargetDir.Text);
                e.Handled = true;
            }
            else if (e.Key == Key.C && isControl)
            {
                WpfControlUtils.CopyToClipboard(listBox);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                e.Handled = true;
                MessageBoxResult dlgResult = MessageBox.Show(
                    "削除しますか？",
                    "情報",
                    MessageBoxButton.YesNo);
                if (dlgResult == MessageBoxResult.No)
                {
                    return;
                }

                String targetNames = WpfControlUtils.GetSelectName(listBox, comboBox_TargetDir.Text);

                StcFileInputOutput fio = new StcFileInputOutput();
                String[] targetArray = targetNames.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < targetArray.Length; i++)
                {
                    fio.DeleteDirectoryAndFile(targetArray[i]);
                }
                UpdateListBox();
            }
        }

        private void listBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateStatusBar();
        }

        private void UpdateStatusBar()
        {
            textBox_StatusBar.Text = String.Format("総数={0} 選択数={1}",
                listBox.Items.Count, listBox.SelectedItems.Count);
        }

        private void button_Listup_Click(object sender, RoutedEventArgs e)
        {
            UpdateListBox();
        }

        private void textBox_Target_Extension_KeyUp(object sender, KeyEventArgs e)
        {
            // このタイミングでリストを更新すると、対象ファイルが多いときに重くなってしまう
            // 入力途中でもリストアップされるのは非効率なので、Enterキーをトリガに動作するようにしておく
            if (e.Key == Key.Enter)
            {
                UpdateListBox();
            }
        }

        private void UpdateListBox(object sender, RoutedEventArgs e)
        {
            if (!_isUiReady)
            {
                return;
            }
            UpdateListBox();
        }

        internal void UpdateListBox()
        {
            if (comboBox_TargetDir.Text == String.Empty)
            {
                return;
            }

            int trimLength; // ファイルリストを生成するときに、基準となるディレクトリパスは削除する
            if (comboBox_TargetDir.Text.Length > 3)
            {
                // C:\ よりも長い場合は、終端の\を削除。ドライブレターの次の\は残す
                comboBox_TargetDir.Text = comboBox_TargetDir.Text.TrimEnd('\\');

                // 「+1」はフォルダ区切り文字
                trimLength = comboBox_TargetDir.Text.Length + 1;
            }
            else
            {
                if (comboBox_TargetDir.Text.Length == 1)
                {
                    comboBox_TargetDir.Text += @":\";
                }
                else if (comboBox_TargetDir.Text.Length == 2)
                {
                    comboBox_TargetDir.Text += @"\";
                }
                trimLength = comboBox_TargetDir.Text.Length;
            }

            if (!Directory.Exists(comboBox_TargetDir.Text))
            {
                MessageBox.Show("フォルダパスが不正です。" + comboBox_TargetDir.Text);
                return;
            }

            SearchOption opt = checkBox_Target_SubDirectory.IsChecked == true
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;
            String searchPattern = textBox_Target_Extension.Text != String.Empty ? textBox_Target_Extension.Text : "*";

            String[] elements;
            if (radioButton_Target_File.IsChecked == true)
            {
                elements = Directory.GetFiles(comboBox_TargetDir.Text, searchPattern, opt);
            }
            else
            {
                elements = Directory.GetDirectories(comboBox_TargetDir.Text, searchPattern, opt);
            }

            // 標準のstring比較だと"HOGE_2"より"HOGE_10"が先に来てしまうため、
            // 数字部分を数値として比較する自然順ソート(NaturalStringComparer)で並べ替える
            Array.Sort(elements, NaturalStringComparer.Instance);

            listBox.Items.Clear();
            for (int i = 0; i < elements.Length; i++)
            {
                listBox.Items.Add(elements[i].Substring(trimLength));
            }
            UpdateStatusBar();
        }

        // 処理対象の一覧(「選択項目のみ」なら選択中の項目を、画面の並び順で)
        internal List<String> GetFileList()
        {
            if (checkBox_Target_SelectFile.IsChecked == true)
            {
                return WpfControlUtils.GetSelectedItemsInOrder(listBox);
            }

            return listBox.Items.Cast<Object>().Select(item => item.ToString()).ToList();
        }

        // *******************************************************************************
        // 実行・復元

        private void button_Execute_Click(object sender, RoutedEventArgs e)
        {
            String errorList = "";
            switch (tabControl.SelectedIndex)
            {
                case _tabIdxChangeName:
                    errorList = ChangeName();
                    break;

                case _tabIdxTimeStamp:
                    ChangeTimeStamp();
                    break;

                case _tabIdxFunction:
                    errorList = ChangeOtherFunction();
                    break;
            }

            if (errorList != String.Empty)
            {
                ErrorMsg dlg = new ErrorMsg(errorList) { Owner = this };
                dlg.ShowDialog();
            }
            UpdateListBox();
        }

        private void button_Restore_Click(object sender, RoutedEventArgs e)
        {
            Boolean isSuccess = true;
            switch (tabControl.SelectedIndex)
            {
                case _tabIdxChangeName:
                    isSuccess = _rename.Restore();
                    break;
                case _tabIdxFunction:
                    isSuccess = _function.Restore();
                    break;
            }

            if (!isSuccess)
            {
                MessageBox.Show("これ以上復元できません");
                return;
            }
            UpdateListBox();
        }

        private String ChangeName()
        {
            _rename.BaseDir = comboBox_TargetDir.Text.TrimEnd('\\');
            _rename.FileList = GetFileList();
            _rename.Type = GetChangedNameType();

            _rename.Param1 = comboBox_String1.Text;
            _rename.Param2 = comboBox_String2.Text;

            // 連番モード以外ではtextBox_ChangeNumber_FirstValは空欄のままなので、
            // 他のモード用ガード(isChangeNumber等)と同じ考え方でモード判定してからParseする
            _rename.FirstNumber = radioButton_ChangeNumber.IsChecked == true
                ? int.Parse(textBox_ChangeNumber_FirstVal.Text)
                : 0;
            _rename.KeepOriginalName = checkBox_ChangeNumber_OrgName.IsChecked == true;
            _rename.PaddingDigits = GetPaddingDigits();

            return _rename.Execute();
        }

        private void ChangeTimeStamp()
        {
            var ts = new TimeStamp();

            // 時刻欄が編集途中(不正な値)なら、最後に正しく入力された時刻を使う
            CommitTimeText();
            DateTime day = dateTimePicker_Days.SelectedDate ?? DateTime.Today;
            DateTime dt = new DateTime(day.Year, day.Month, day.Day,
                _currentTime.Hours, _currentTime.Minutes, _currentTime.Seconds, 0);
            ts.BaseTicks = dt.Ticks;
            ts.IntervalTicks = GetTickTime(comboBox_TimeSpan.SelectedIndex);

            ts.BaseDir = comboBox_TargetDir.Text.TrimEnd('\\');
            ts.FileList = GetFileList();

            ts.UpdateCreationTime = checkBox_CreationTime.IsChecked == true;
            ts.UpdateLastWriteTime = checkBox_LastWriteTime.IsChecked == true;
            ts.UpdateLastAccessTime = checkBox_LastAccessTime.IsChecked == true;

            ts.Execute();
        }

        // 連番の0埋め桁数
        private int GetPaddingDigits()
        {
            if (comboBox_ChangeNumber_Digit.SelectedIndex > 0)
            {
                // 自動桁数じゃない場合
                return comboBox_ChangeNumber_Digit.SelectedIndex;
            }

            // [自動桁数]の場合
            if (checkBox_Target_SelectFile.IsChecked == true)
            {
                return listBox.SelectedItems.Count.ToString().Length;
            }
            return listBox.Items.Count.ToString().Length;
        }

        internal static long GetTickTime(int timeSpanIndex)
        {
            // 加算時間
            switch (timeSpanIndex)
            {
                case 1:     // [秒]
                    return TimeSpan.TicksPerSecond;
                case 2:     // [分]
                    return TimeSpan.TicksPerMinute;
                case 3:     // [時間]
                    return TimeSpan.TicksPerHour;
                case 4:     // [日]
                    return TimeSpan.TicksPerDay;
                default:    // [無し]
                    return 0;
            }
        }

        private Rename.ChangeType GetChangedNameType()
        {
            if (radioButton_ChangeNumber.IsChecked == true)
            {
                return Rename.ChangeType.Number;
            }
            else if (radioButton_ChangeDelNum.IsChecked == true)
            {
                return Rename.ChangeType.DelNum;
            }
            else if (radioButton_ChangeAdd.IsChecked == true)
            {
                return Rename.ChangeType.Add;
            }
            else if (radioButton_ChangeDelete.IsChecked == true)
            {
                return Rename.ChangeType.Delete;
            }
            else if (radioButton_ChangeReplace.IsChecked == true)
            {
                return Rename.ChangeType.Replace;
            }
            else if (radioButton_ChangeExt.IsChecked == true)
            {
                return Rename.ChangeType.OnlyExt;
            }

            // radioButton_ChangeAddDirName
            return Rename.ChangeType.AddDirName;
        }

        private void SetNameChangeControlLabel()
        {
            String labelText1 = "";
            String labelText2 = "";

            // 状態取得
            if (radioButton_ChangeNumber.IsChecked == true)
            {
            }
            else if (radioButton_ChangeDelNum.IsChecked == true)
            {
                labelText1 = "先頭から";
                labelText2 = "後方から";
            }
            else if (radioButton_ChangeAdd.IsChecked == true)
            {
                labelText1 = "先頭に追加";
                labelText2 = "後方に追加";
            }
            else if (radioButton_ChangeDelete.IsChecked == true)
            {
                labelText1 = "削除文字";
            }
            else if (radioButton_ChangeReplace.IsChecked == true)
            {
                labelText1 = "置換前";
                labelText2 = "置換後";
            }
            else if (radioButton_ChangeExt.IsChecked == true)
            {
                labelText1 = "拡張子";
            }
            else if (radioButton_ChangeAddDirName.IsChecked == true)
            {
                // 何も無し
            }

            label_String1.Content = labelText1;
            label_String2.Content = labelText2;
        }

        private void UpdateNameChangeControl(object sender, RoutedEventArgs e)
        {
            if (!_isUiReady)
            {
                return;
            }

            bool isChangeNumber = radioButton_ChangeNumber.IsChecked == true;
            SetNameChangeControlLabel();

            // TextBoxの表示
            comboBox_String1.IsEnabled = (String)label_String1.Content != String.Empty;
            comboBox_String2.IsEnabled = (String)label_String2.Content != String.Empty;

            // ChangeNumber用の設定
            label_ChangeNumber_FirstVal.IsEnabled = isChangeNumber;
            textBox_ChangeNumber_FirstVal.IsEnabled = isChangeNumber;

            comboBox_ChangeNumber_Digit.IsEnabled = isChangeNumber;
            checkBox_ChangeNumber_OrgName.IsEnabled = isChangeNumber;
        }

        // *******************************************************************************
        // 機能タブ

        private void UpdateFunctionControl(object sender, RoutedEventArgs e)
        {
            if (!_isUiReady)
            {
                return;
            }

            Boolean isDestDirSelectable = radioButton_Delete_BlankDir.IsChecked != true;
            checkBox_Operation_AnyDir.IsEnabled = isDestDirSelectable;

            textBox_Function_Any_Directory.IsEnabled = isDestDirSelectable && checkBox_Operation_AnyDir.IsChecked == true;
        }

        private void textBox_Function_Any_Directory_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            textBox_Function_Any_Directory.SelectAll();
        }

        private void textBox_Function_Any_Directory_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            textBox_Function_Any_Directory.SelectAll();
        }

        private String GetDestDirOtherFunction()
        {
            String destDirName = comboBox_TargetDir.Text;
            if (radioButton_Delete_BlankDir.IsChecked != true &&
                checkBox_Operation_AnyDir.IsChecked == true)
            {
                destDirName = textBox_Function_Any_Directory.Text;
            }

            return destDirName;
        }

        private Function.FunctionType GetFunctionType()
        {
            if (radioButton_Delete_BlankDir.IsChecked == true)
            {
                return Function.FunctionType.DelEmptyDir;
            }
            else if (radioButton_Move_Target.IsChecked == true)
            {
                return Function.FunctionType.Move;
            }

            // radioButton_Copy_Target
            return Function.FunctionType.Copy;
        }

        private String ChangeOtherFunction()
        {
            _function.BaseDir = comboBox_TargetDir.Text.TrimEnd('\\');
            _function.DestDir = GetDestDirOtherFunction().TrimEnd('\\');

            _function.FileList = GetFileList();
            _function.Type = GetFunctionType();

            return _function.Execute();
        }

        // *******************************************************************************
        // 時刻入力欄(WinForms版のDateTimePicker Format=Time / ShowUpDown=true の代わり)

        private void SetTime(TimeSpan time)
        {
            _currentTime = time;
            textBox_Time.Text = TimeText.Format(time);
        }

        // 入力内容が正しければ採用し、不正なら最後に正しかった時刻に戻す
        private void CommitTimeText()
        {
            TimeSpan parsed;
            SetTime(TimeText.TryParse(textBox_Time.Text, out parsed) ? parsed : _currentTime);
        }

        private void StepTime(int delta)
        {
            int caret = textBox_Time.CaretIndex;
            CommitTimeText();
            int field = TimeText.GetFieldIndex(textBox_Time.Text, caret);
            SetTime(TimeText.Increment(_currentTime, field, delta));
            textBox_Time.CaretIndex = Math.Min(caret, textBox_Time.Text.Length);
        }

        private void textBox_Time_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up || e.Key == Key.Down)
            {
                StepTime(e.Key == Key.Up ? 1 : -1);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                CommitTimeText();
            }
        }

        private void textBox_Time_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            CommitTimeText();
        }

        private void button_TimeUp_Click(object sender, RoutedEventArgs e)
        {
            StepTime(1);
        }

        private void button_TimeDown_Click(object sender, RoutedEventArgs e)
        {
            StepTime(-1);
        }
    }
}
