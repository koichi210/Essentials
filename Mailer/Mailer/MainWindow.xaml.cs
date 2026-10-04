using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StandardTemplate;
using StandardTemplate.Wpf;

namespace Mailer
{
    public partial class MainWindow : Window
    {
        // データ保存先はWinForms版Mailerと同じ(%LOCALAPPDATA%\Mailer\)。保存キーも同じにしてあるので、
        // WinForms版で保存したプロファイルをそのまま読める
        private const String _appName = "Mailer";
        private const String _settingFileName = "Mailer.json";
        private static readonly String[] _profileExtensions = { "*.json" };

        private const String _mailUrl = @"https://mail.google.com/mail/?view=cm&fs=1";

        private readonly String _userDataFolder = UserDataLocation.GetUserDataFolder(_appName);
        private readonly StcUtils _util = new StcUtils();
        private readonly StcFileInputOutput _fio = new StcFileInputOutput();
        internal WpfSaveRestore SaveRestore { get; } = new WpfSaveRestore();

        private readonly ExecParam _param = new ExecParam();

        private class ExecParam
        {
            public int CreateNum = 0;
            public int IntervalMsec = 0;
            public DateTime UserDate;
        }

        public MainWindow()
        {
            InitializeComponent();
            _util.SetCurrentDirectory();
            dateTimePicker_Calendar.SelectedDate = DateTime.Today;

            RegisterSettingItems();
            SaveRestore.LoadOrDefault(Path.Combine(_userDataFolder, _settingFileName));
            WpfProfile.UpdateProfileList(comboBox_LoadSetting, _profileExtensions, "", _userDataFolder);

            WpfDataFolderMenu.Attach(this, () => DataFolderMenu.ChangeDataFolder(_appName, _userDataFolder,
                (oldFolder, newFolder) => DataFolderMenu.MoveProfiles(oldFolder, newFolder, _appName)));
        }

        internal void RegisterSettingItems()
        {
            // キーはWinForms版(SaveRestore.RegisterLoadItem)と同じにすること
            SaveRestore.RegisterCtrl("Common", "textBox_BrowserPath", textBox_BrowserPath);
            SaveRestore.RegisterCtrl("Common", "textBox_MailTo", textBox_MailTo);
            SaveRestore.RegisterCtrl("Common", "textBox_MailCc", textBox_MailCc);
            SaveRestore.RegisterCtrl("Common", "textBox_MailBcc", textBox_MailBcc);
            SaveRestore.RegisterCtrl("Common", "textBox_MailSubject", textBox_MailSubject);
            SaveRestore.RegisterCtrl("Common", "textBox_MailBody", textBox_MailBody);
        }

        private void comboBox_LoadSetting_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // SelectionChangedの時点ではTextがまだ古いので、SelectedItemから読む
            if (comboBox_LoadSetting.SelectedItem == null)
            {
                return;
            }
            SaveRestore.Load(Path.Combine(_userDataFolder, comboBox_LoadSetting.SelectedItem.ToString()));
        }

        private void button_SaveSetting_Click(object sender, RoutedEventArgs e)
        {
            WpfProfile.SaveProfileWithDialog(_fio, comboBox_LoadSetting, _profileExtensions, SaveRestore.Save, _userDataFolder, _settingFileName);
        }

        private void button_OpenBrowse_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetUiParam())
            {
                return;
            }

            OpenBrowser();
        }

        private void button_OpenBrowse_OneWeek_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetUiParam())
            {
                return;
            }
            var offsets = Logic.GetDayOffsetList(_param.CreateNum, checkBox_Reverse.IsChecked == true);
            foreach (var offset in offsets)
            {
                OpenBrowser(offset);
                System.Threading.Thread.Sleep(_param.IntervalMsec);
            }
        }

        private void OpenBrowser(int daysOffset = 0)
        {
            String browseUrl = _mailUrl;
            if (textBox_MailTo.Text != String.Empty)
            {
                browseUrl += "&to=" + textBox_MailTo.Text;
            }
            if (textBox_MailCc.Text != String.Empty)
            {
                browseUrl += "&cc=" + textBox_MailCc.Text;
            }
            if (textBox_MailBcc.Text != String.Empty)
            {
                browseUrl += "&bcc=" + textBox_MailBcc.Text;
            }

            if (textBox_MailSubject.Text != String.Empty)
            {
                DateTime userDate = _param.UserDate.AddDays(daysOffset);
                String chromeFormatText = textBox_MailSubject.Text.Replace(" ", "+");
                browseUrl += "&su=" + Logic.ReplaceDatePlaceholders(chromeFormatText, userDate);
            }

            if (textBox_MailBody.Text != String.Empty)
            {
                browseUrl += "&body=" + textBox_MailBody.Text.Replace("\r\n", "%0D%0A").Replace(" ", "+");
            }

            _util.ExecuteProcess(textBox_BrowserPath.Text, browseUrl);
        }

        private Boolean TryGetUiParam()
        {
            if (!_util.IsExistFileNameInEnvironment(textBox_BrowserPath.Text))
            {
                MessageBox.Show("ファイルが存在しません" + Environment.NewLine + textBox_BrowserPath.Text);
                return false;
            }

            int.TryParse(textBox_CreateNum.Text, out _param.CreateNum);
            int.TryParse(textBox_IntervalMsec.Text, out _param.IntervalMsec);

            // WinForms版のDateTimePickerは起動時刻の時分秒を持っていたので、それに合わせて今の時刻を付ける
            DateTime date = dateTimePicker_Calendar.SelectedDate ?? DateTime.Today;
            DateTime now = DateTime.Now;
            _param.UserDate = new DateTime(date.Year, date.Month, date.Day, now.Hour, now.Minute, now.Second, 0);
            return true;
        }

        private void textBox_BrowserPath_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _util.ExecutePath(textBox_BrowserPath.Text);
            }
        }

        private void button_Help_Click(object sender, RoutedEventArgs e)
        {
            var message = "USAGE:" + Environment.NewLine +
                "  %%today%% ・・・・        1/1" + Environment.NewLine +
                "  %%TODAY%% ・・・・   2024/1/1" + Environment.NewLine +
                "  %%tomorrow%% ・・・       1/2" + Environment.NewLine +
                "  %%TOMORROW%% ・・・  2024/1/2" + Environment.NewLine +
                "  %%weekend%% ・・・  (金曜日の日付）" + Environment.NewLine +
                "  %%WEEKEND%% ・・・  (金曜日の日付）" + Environment.NewLine +
                "  %%usersday%% ・・・       2/3 (select day)" + Environment.NewLine +
                "  %%USERSDAY%% ・・・  2024/2/3 (select day)" + Environment.NewLine +
                "  %%dayofweek%% ・・・ 月       (is selected 2024/1/1)" + Environment.NewLine +
                "  %%DAYOFWEEK%% ・・・ 月曜日   (is selected 2024/1/1)";
            MessageBox.Show(message);
        }
    }
}
