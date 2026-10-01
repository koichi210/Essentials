using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LauncherForWpf.Models
{
    /// <summary>
    /// 項目をグルーピングするセクション（開閉可能）
    /// </summary>
    public class SectionModel : INotifyPropertyChanged
    {
        private string _name;
        private bool _isExpanded = true;

        /// <summary>セクション名</summary>
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        /// <summary>開閉状態（折りたたみ表示用）</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); }
        }

        /// <summary>このセクションに属する項目一覧</summary>
        public ObservableCollection<ItemModel> Items { get; set; } = new ObservableCollection<ItemModel>();

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
