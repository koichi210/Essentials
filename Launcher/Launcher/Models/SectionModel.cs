using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;

namespace Launcher.Models
{
    /// <summary>
    /// 項目をグルーピングするセクション(開閉可能)。設定ファイルでは 1 つのタグ(MagicLauncher の Node)。
    /// 項目は先頭のタグのセクションに並ぶ(MagicLauncher で円がぶら下がる場所と同じ)。
    /// </summary>
    public class SectionModel : INotifyPropertyChanged
    {
        private bool _isExpanded = true;

        /// <summary>セクション名(=タグ名)。タグなしのまとめは「タグなし」</summary>
        public string Name { get; init; }

        /// <summary>開閉状態を覚えるときの名前(タグ名。タグなしは MagicLauncher と同じ仮の名前)</summary>
        public string Key { get; init; }

        /// <summary>タグの付いていない項目をまとめた仮のセクションか(名前変更・削除はできない)</summary>
        public bool IsUntagged { get; init; }

        public bool CanEdit => !IsUntagged;

        /// <summary>タグの色(MagicLauncher で分岐点や円に使う色)</summary>
        public Brush ColorBrush { get; init; }

        /// <summary>開閉状態(折りたたみ表示用)。設定ファイルには書かず、この PC の画面の状態として覚える</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                {
                    return;
                }
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }

        /// <summary>このセクションに並ぶ項目一覧</summary>
        public ObservableCollection<ItemModel> Items { get; } = new ObservableCollection<ItemModel>();

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
