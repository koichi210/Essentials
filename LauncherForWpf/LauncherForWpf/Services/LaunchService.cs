using System.Diagnostics;

namespace LauncherForWpf.Services
{
    /// <summary>
    /// 登録されたリンクを起動する。
    /// exeのフルパス・URL・その他関連付けされたパスのいずれも
    /// UseShellExecute=trueでOS標準の方法で開く。
    /// </summary>
    public static class LaunchService
    {
        public static void Launch(string path)
        {
            var info = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            };
            Process.Start(info);
        }
    }
}
