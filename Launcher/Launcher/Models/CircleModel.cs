using System.Collections.ObjectModel;

namespace Launcher.Models
{
    /// <summary>
    /// 魔法陣(MagicLauncher の circles)。セクション(タグ)をまとめる一番外の入れ物。
    /// 画面は設定ファイルを読むたびに作り直すので、変更通知は持たない。
    /// </summary>
    public class CircleModel
    {
        /// <summary>魔法陣の名前</summary>
        public string Name { get; init; }

        /// <summary>「最初の魔法陣」(置き場所が空欄のタグ・タグなしの項目が入る)か</summary>
        public bool IsFirst { get; init; }

        /// <summary>この魔法陣に置いたセクション(タグ)。設定ファイルの tags のキーの順</summary>
        public ObservableCollection<SectionModel> Sections { get; } = new ObservableCollection<SectionModel>();
    }
}
