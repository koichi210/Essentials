using System;
using System.Collections.Generic;

namespace EventRecorderForWpf
{
    // WinForms版EventRecorder(Profile.cs)からそのまま移したもの(名前空間だけ変更)。
    // プロパティ名がそのままJSONのキー名になるので、リネームするとWinForms版で保存した
    // JSONファイル(%LOCALAPPDATA%\EventRecorder\*.json)が読めなくなる点に注意。
    //
    // マクロ(記録データ+プレイリスト)をJSON([[_Common/JsonFileStorage.cs]])で保存する際のデータ構造
    public class EventRecorderProfile
    {
        public String LoopCount { get; set; } = "1";
        public List<MacroEventData> Events { get; set; } = new List<MacroEventData>();
        public List<PlaylistEntryData> Playlist { get; set; } = new List<PlaylistEntryData>();

        // モード切替ラジオボタン(true=レコード、false=プレイバック)・最小化チェックボックスの
        // 状態。プレイリスト再生時の各行のファイル読込(LoadProfileForPlayback)では、
        // 途中でモードが切り替わってしまわないようこの2つは適用しない
        public Boolean IsRecordMode { get; set; } = true;
        public Boolean MinimizeOnPlay { get; set; }
    }

    // dataGrid_Eventsの1行分(Type/X/Y/Key/Wait/備考)
    public class MacroEventData
    {
        public String Type { get; set; }
        public String X { get; set; }
        public String Y { get; set; }
        public String Key { get; set; }
        public String Wait { get; set; }

        // ユーザーが自由に書けるコメント欄。記録・再生には一切使わない
        public String Remarks { get; set; }
    }

    // dataGrid_Playlistの1行分(実行チェック/設定ファイル名/その行のループ回数)
    public class PlaylistEntryData
    {
        public Boolean Enabled { get; set; }
        public String FileName { get; set; }
        public String LoopCount { get; set; }
    }
}
