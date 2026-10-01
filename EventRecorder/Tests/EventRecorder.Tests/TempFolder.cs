using System;
using System.IO;
using System.Text;

namespace EventRecorder.Tests
{
    // テスト専用の一時フォルダ(%TEMP%配下)。MainWindowのデータフォルダとして渡し、
    // 本物のユーザーデータフォルダ(%LOCALAPPDATA%\EventRecorder)には一切触れないようにする
    internal sealed class TempFolder : IDisposable
    {
        public String Path { get; }

        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EventRecorderTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public String Combine(String name)
        {
            return System.IO.Path.Combine(Path, name);
        }

        public String Write(String name, String content)
        {
            String path = Combine(name);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
            catch (IOException)
            {
                // 後片付けの失敗はテストの成否に関係ないので黙って流す
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
