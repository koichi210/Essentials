using System.Diagnostics;

namespace Launcher.Services
{
    /// <summary>
    /// 登録されたリンクを起動する。
    /// exeのフルパス・URL・その他関連付けされたパスのいずれも
    /// UseShellExecute=trueでOS標準の方法で開く。
    /// MagicLauncher と同じく、前後の "..." を外し %VAR% を展開してから開く。
    /// </summary>
    public static class LaunchService
    {
        public static void Launch(string path)
        {
            var info = new ProcessStartInfo
            {
                FileName = ConfigService.ExpandEnv(ConfigService.StripQuotes(path)),
                UseShellExecute = true,
            };
            Process.Start(info);
        }
    }
}
