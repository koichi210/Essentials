using System.Collections.Generic;

namespace Launcher.Models
{
    /// <summary>
    /// ランチャーに登録する1項目。設定ファイルの items の1つ(画面は読むたびに作り直すので、変更通知は持たない)。
    /// </summary>
    public class ItemModel
    {
        /// <summary>項目の id(書き込むときに設定ファイルの中から探す目印)</summary>
        public string Id { get; init; }

        /// <summary>設定ファイルに書かれている名前(空のこともある)</summary>
        public string Name { get; init; }

        /// <summary>
        /// 起動対象。以下のいずれかを想定(%USERPROFILE% などの環境変数も使える)。
        /// ・exeのフルパス（例: C:\Windows\notepad.exe）
        /// ・httpから始まるURL（例: https://example.com）や steam:// などのスキーム付きURL
        /// ・その他Windowsが関連付けで開けるパス（フォルダ、ドキュメント等）
        /// </summary>
        public string Path { get; init; }

        public IReadOnlyList<string> Tags { get; init; }

        public string Emoji { get; init; }

        public string Note { get; init; }

        /// <summary>画面に表示するタイトル(名前が空なら、URL はホスト名、パスは末尾の名前)</summary>
        public string Title { get; init; }

        /// <summary>ボタンに出す文字(絵文字＋タイトル)</summary>
        public string DisplayText => string.IsNullOrEmpty(Emoji) ? Title : Emoji + " " + Title;

        /// <summary>マウスを載せたときに出す文字(リンク先＋メモ)</summary>
        public string ToolTipText => string.IsNullOrWhiteSpace(Note) ? Path : Path + "\n\n" + Note;
    }
}
