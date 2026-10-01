using System;

namespace EventRecorderForWpf
{
    // アプリ本体の設定値をまとめて1つのファイル(EventRecorder.json、userDataFolder配下)に保存するためのクラス。
    // WinForms版EventRecorder(AppSettings.cs)と同じファイル名・同じプロパティ名にしてあるので、
    // WinForms版とWPF版で同じ設定ファイル(ウィンドウサイズ・境界線位置・ホットキー)を共有できる。
    // ホットキーの型もWinForms版と同じSystem.Windows.Forms.Keys(JSON上は数値)のまま
    // (GlobalHook([[_Common/GlobalHook.cs]])が渡してくるキーもKeysなので、比較にそのまま使える)
    public class AppSettings
    {
        // ウィンドウサイズ(外枠込み、ピクセル)+左右分割の境界線位置(左側パネルの幅、ピクセル)
        public int Width { get; set; }
        public int Height { get; set; }
        public int SplitterDistance { get; set; }

        // 記録/再生の切り替えホットキー([[HotkeyDefaults.cs]]参照)
        public System.Windows.Forms.Keys RecordHotkey { get; set; }
        public System.Windows.Forms.Keys PlayHotkey { get; set; }
    }
}
