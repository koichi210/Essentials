using System.Collections.ObjectModel;

namespace Launcher.Models
{
    /// <summary>
    /// 設定ファイル（JSON）のルート要素
    /// </summary>
    public class LauncherConfig
    {
        public ObservableCollection<SectionModel> Sections { get; set; } = new ObservableCollection<SectionModel>();
    }
}
