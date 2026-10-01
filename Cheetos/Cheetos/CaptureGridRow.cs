// CaptureWindowタブのDataGrid(cw_dataGridView)の1行分。
// WinForms版はDataGridViewのセルに直接文字列を入れていたが、WPFのDataGridはItemsSourceの
// オブジェクトを表示する方式なので、5列ぶんの文字列を持つだけの行クラスを用意した。
// 列の並びはMainWindowのdataGridColumns(Sleep/MouseX/MouseY/MouseAction/Capture)と同じ。
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Cheetos
{
    internal class CaptureGridRow : INotifyPropertyChanged
    {
        public const int ColumnCount = 5;

        private readonly String[] cells = { "", "", "", "", "" };

        public event PropertyChangedEventHandler PropertyChanged;

        public String Sleep { get { return this[0]; } set { this[0] = value; } }
        public String MouseX { get { return this[1]; } set { this[1] = value; } }
        public String MouseY { get { return this[2]; } set { this[2] = value; } }
        public String MouseAction { get { return this[3]; } set { this[3] = value; } }
        public String Capture { get { return this[4]; } set { this[4] = value; } }

        private static readonly String[] PropertyNames = { "Sleep", "MouseX", "MouseY", "MouseAction", "Capture" };

        // 列番号でアクセスする(WinForms版の util.GetDataGridCell / SetDataGridCell の代わり)
        public String this[int columnIdx]
        {
            get { return cells[columnIdx] ?? ""; }
            set
            {
                cells[columnIdx] = value ?? "";
                PropertyChangedEventHandler handler = PropertyChanged;
                if (handler != null)
                {
                    handler(this, new PropertyChangedEventArgs(PropertyNames[columnIdx]));
                }
            }
        }

        public List<String> ToList()
        {
            return new List<String>(cells);
        }

        // 保存ファイルの1行分から作る。列が足りない分は空文字、多い分は捨てる
        // (WinForms版のApplyGenericProfileと同じく、ColumnCountを超える列は無視する)
        public static CaptureGridRow FromList(List<String> values)
        {
            CaptureGridRow row = new CaptureGridRow();
            for (int c = 0; values != null && c < values.Count && c < ColumnCount; c++)
            {
                row.cells[c] = values[c] ?? "";
            }
            return row;
        }
    }
}
