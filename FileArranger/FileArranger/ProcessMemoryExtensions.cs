using System;
using StandardTemplate;

namespace FileArranger
{
    // StcProcessMemory(リネーム/移動の履歴)から「元に戻す」処理。
    // フォルダ名変更(rdタブ)とファイル並べ替え(sfタブ)で同じ戻し方をしていたためまとめた
    internal static class ProcessMemoryExtensions
    {
        // 直前に確定した1回分の操作を、記録とは逆向き(移動後→移動前)にmoveで戻す。
        // 戻す対象が無ければfalse(呼び出し側は「これ以上復元できません」を表示する)
        public static Boolean RestoreLastBatch(this StcProcessMemory memory, Action<String, String> move)
        {
            if (!memory.DecrementSerialNumber())
            {
                return false;
            }

            while (memory.HasRestoreItem())
            {
                String srcName = "";
                String destName = "";
                memory.PopRestoreItem(ref srcName, ref destName);
                move(destName, srcName);
            }

            return true;
        }
    }
}
