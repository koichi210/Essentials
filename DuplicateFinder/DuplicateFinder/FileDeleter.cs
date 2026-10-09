using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DuplicateFinder
{
    /// <summary>ファイルの削除(ごみ箱へ移動 or 完全に削除)</summary>
    internal static class FileDeleter
    {
        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOERRORUI = 0x0400;
        // ごみ箱に入らない場所(ネットワークドライブ等)で完全に削除されそうなときは、Windowsが確認を出す
        private const ushort FOF_WANTNUKEWARNING = 0x4000;

        /// <summary>
        /// 削除する。消せなかった・取りやめになった場合は理由を返す(成功はnull)。
        /// ごみ箱に送る場合はシェルの確認ダイアログが出ることがあるので、UIスレッドから呼ぶこと
        /// </summary>
        public static string Delete(string path, bool toRecycleBin, IntPtr ownerWindow)
        {
            try
            {
                if (!toRecycleBin)
                {
                    File.Delete(path);
                    return null;
                }

                var op = new SHFILEOPSTRUCT
                {
                    hwnd = ownerWindow,
                    wFunc = FO_DELETE,
                    pFrom = path + "\0\0",
                    fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT | FOF_WANTNUKEWARNING,
                };
                int code = SHFileOperation(ref op);
                if (op.fAnyOperationsAborted)
                {
                    return "取りやめました";
                }
                if (code != 0)
                {
                    return "ごみ箱へ移動できませんでした (エラーコード 0x" + code.ToString("X") + ")";
                }
                return File.Exists(path) ? "ごみ箱へ移動できませんでした" : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return ex.Message;
            }
        }

        // x64専用(このアプリはx64ビルド)。x86ではPack=1にしないとずれる
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
    }
}
