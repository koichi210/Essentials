using System.ComponentModel;

namespace Launcher.Models
{
    /// <summary>
    /// ランチャーに登録する1項目（タイトル＋リンク先）
    /// </summary>
    public class ItemModel : INotifyPropertyChanged
    {
        private string _title;
        private string _path;

        /// <summary>画面に表示するタイトル</summary>
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(nameof(Title)); }
        }

        /// <summary>
        /// 起動対象。以下のいずれかを想定。
        /// ・exeのフルパス（例: C:\Windows\notepad.exe）
        /// ・httpから始まるURL（例: https://example.com）
        /// ・その他Windowsが関連付けで開けるパス（フォルダ、ドキュメント等）
        /// </summary>
        public string Path
        {
            get => _path;
            set { _path = value; OnPropertyChanged(nameof(Path)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
