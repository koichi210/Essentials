using System;

namespace EventRecorder
{
    // 記録データ配列([Type, X, Y, Key, Wait, Delta]の固定順)と、レコード表の行(EventRow)の間の変換だけを担う。
    //
    // ⚠️WinForms版での過去の不具合: この変換をCells[0]/Cells[1].../Cells[4]のような列の「物理的な並び順」の
    // 決め打ちで実装していたことがあり、Detail列を追加した際に列の並び順が変わったのに追従し忘れ、
    // 記録した値が別の列(Key列にX、Wait列にY等)にずれて入る不具合になっていた(2026-09-12発覚)。
    // WPF版は列の並び順ではなく行クラスのプロパティ名で読み書きするので同じ事故は起きないが、
    // 配列の順序([Type, X, Y, Key, Wait, Delta])との対応はここ1か所に閉じ込め、単体テストで保証する
    internal static class EventRowMapper
    {
        // values([Type, X, Y, Key, Wait, Delta]の順)の値を、rowの各列へ書き込む。
        // Delta(ホイールの回転量)は後から足した項目なので、5要素の配列なら空として扱う
        internal static void ApplyToRow(EventRow row, String[] values)
        {
            row.Type = values[0];
            row.X = values[1];
            row.Y = values[2];
            row.Key = values[3];
            row.Wait = values[4];
            row.Delta = values.Length > 5 ? values[5] : "";
        }

        // rowの各列の値を[Type, X, Y, Key, Wait, Delta]の順の配列に読み出す(ApplyToRowの逆)
        internal static String[] ReadFromRow(EventRow row)
        {
            return new String[]
            {
                row.Type,
                row.X,
                row.Y,
                row.Key,
                row.Wait,
                row.Delta,
            };
        }
    }
}
