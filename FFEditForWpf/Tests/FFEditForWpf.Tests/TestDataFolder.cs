using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FFEditForWpf.Tests
{
    /// <summary>
    /// MainWindowのコンストラクタはUserDataLocation.GetUserDataFolder("FFEdit")で設定フォルダを決め、
    /// そこの FFEdit.json を読む。案内板(DataFolder.txt)が無いと本物の %LOCALAPPDATA%\FFEdit を
    /// 使ってしまうため、テスト開始前にテスト出力フォルダの案内板を一時フォルダ向けに書き換えておく。
    /// </summary>
    [TestClass]
    public static class TestDataFolder
    {
        private static string dataFolder;

        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext context)
        {
            dataFolder = Path.Combine(Path.GetTempPath(), "FFEditForWpfTests_Data_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataFolder);

            // UserDataLocationはGetEntryAssembly(テスト時はnull)→実行中アセンブリ(=FFEditForWpf.exe)の隣を見る
            string exeDir = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location);
            File.WriteAllText(Path.Combine(exeDir, "DataFolder.txt"), dataFolder + Environment.NewLine);
        }

        [AssemblyCleanup]
        public static void AssemblyCleanup()
        {
            try
            {
                if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
            }
            catch (IOException)
            {
                // 後片付けの失敗はテストの成否に関係ないので黙って流す
            }
        }
    }
}
