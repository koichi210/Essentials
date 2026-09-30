using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StandardTemplate.Wpf.Tests;

namespace CheetosForWpf.Tests
{
    /// <summary>
    /// MainWindowの設定保存/読み込み(WpfSaveRestore)のテスト。
    ///
    /// WinForms版Cheetosの SaveRestoreTests を移植したもの。WinForms版はXML(SaveXmlFile/LoadXmlFile)と
    /// JSONの両方をテストしていたが、WPF版はJSONプロファイルだけに対応するので、XMLのテストはJSONで同じ内容を確かめる。
    /// 実物のMainWindowを使うのは、RegistLoadItem 内のタイプミスや属性名の重複を検出したいため。
    ///
    /// MainWindowはテスト用のコンストラクタ(データフォルダ指定)で作り、実際のユーザーデータフォルダには触れない。
    /// </summary>
    [TestClass]
    public class SaveRestoreTests
    {
        private string tempDirectory;
        private string dataDirectory;

        [TestInitialize]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "CheetosForWpfTests_" + Guid.NewGuid().ToString("N"));
            dataDirectory = Path.Combine(tempDirectory, "data");
            Directory.CreateDirectory(tempDirectory);
            Directory.CreateDirectory(dataDirectory);
        }

        [TestCleanup]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
            }
            catch (IOException)
            {
                // 後片付けの失敗はテストの成否に関係ないので黙って流す
            }
        }

        /// <summary>空の一時データフォルダを保存先にしたMainWindowを作る(起動時に読むプロファイルは無い)。</summary>
        private MainWindow NewWindow()
        {
            return new MainWindow(dataDirectory);
        }

        private string PathFor(string name)
        {
            return Path.Combine(tempDirectory, name + ".json");
        }

        [TestMethod]
        public void CaptureWindowタブの値が保存して読み直すと戻る()
        {
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.cw_TextBox_SavePath.Text = @"C:\capture\out";
                writer.cw_TextBox_SaveFilePrefix.Text = "shot_";
                writer.cw_checkBox_AddTimeStamp.IsChecked = true;
                writer.cw_Radio_CurrentWindow.IsChecked = true;
                writer.cw_TextBox_Sleep.Text = "1234";
                writer.cw_TextBox_Loop.Text = "9";

                string path = PathFor("capture");
                Assert.IsTrue(writer.sr.Save(path), "保存に成功するはず");

                MainWindow reader = NewWindow();
                Assert.IsTrue(reader.sr.Load(path), "読み込みに成功するはず");

                Assert.AreEqual(@"C:\capture\out", reader.cw_TextBox_SavePath.Text);
                Assert.AreEqual("shot_", reader.cw_TextBox_SaveFilePrefix.Text);
                Assert.IsTrue(reader.cw_checkBox_AddTimeStamp.IsChecked == true);
                Assert.IsTrue(reader.cw_Radio_CurrentWindow.IsChecked == true);
                Assert.IsFalse(reader.cw_Radio_FullScreen.IsChecked == true);
                Assert.AreEqual("1234", reader.cw_TextBox_Sleep.Text);
                Assert.AreEqual("9", reader.cw_TextBox_Loop.Text);
            });
        }

        [TestMethod]
        public void Rotationタブの値が保存して読み直すと戻る()
        {
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.pr_SourceFolderPath.Text = @"D:\photos";
                writer.pr_BaseX.Text = "100";
                writer.pr_BaseY.Text = "200";
                writer.pr_Angle.Text = "45";

                string path = PathFor("rotation");
                writer.sr.Save(path);

                MainWindow reader = NewWindow();
                reader.sr.Load(path);

                Assert.AreEqual(@"D:\photos", reader.pr_SourceFolderPath.Text);
                Assert.AreEqual("100", reader.pr_BaseX.Text);
                Assert.AreEqual("200", reader.pr_BaseY.Text);
                Assert.AreEqual("45", reader.pr_Angle.Text);
            });
        }

        [TestMethod]
        public void DistOrientタブの値が保存して読み直すと戻る()
        {
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.do_SourceFolderPath.Text = @"D:\src";
                writer.do_DestPortFolderPath.Text = @"D:\port";
                writer.do_DestLandFolderPath.Text = @"D:\land";
                writer.do_TargetFileName.Text = "*.jpg";
                writer.do_WhiteLength.Text = "5";
                writer.do_WhiteCoef.Text = "77";
                writer.do_SampleFilePath.Text = @"D:\sample.jpg";

                string path = PathFor("distorient");
                writer.sr.Save(path);

                MainWindow reader = NewWindow();
                reader.sr.Load(path);

                Assert.AreEqual(@"D:\src", reader.do_SourceFolderPath.Text);
                Assert.AreEqual(@"D:\port", reader.do_DestPortFolderPath.Text);
                Assert.AreEqual(@"D:\land", reader.do_DestLandFolderPath.Text);
                Assert.AreEqual("*.jpg", reader.do_TargetFileName.Text);
                Assert.AreEqual("5", reader.do_WhiteLength.Text);
                Assert.AreEqual("77", reader.do_WhiteCoef.Text);
                Assert.AreEqual(@"D:\sample.jpg", reader.do_SampleFilePath.Text);
            });
        }

        [TestMethod]
        public void PictMergeタブとFileCollectタブの値が保存して読み直すと戻る()
        {
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.pm_SourceFolderPath.Text = @"D:\merge";
                writer.pm_SourceFile1Prefix.Text = "left_";
                writer.pm_SourceFile2Prefix.Text = "right_";
                writer.pm_TrimingHeight.Text = "0,480\r\n-,-";

                writer.fc_SourceFolderPath.Text = @"D:\from";
                writer.fc_DestFolderPath.Text = @"D:\to";
                writer.fc_TargetFileName.Text = "*.png";

                string path = PathFor("merge");
                writer.sr.Save(path);

                MainWindow reader = NewWindow();
                reader.sr.Load(path);

                Assert.AreEqual(@"D:\merge", reader.pm_SourceFolderPath.Text);
                Assert.AreEqual("left_", reader.pm_SourceFile1Prefix.Text);
                Assert.AreEqual("right_", reader.pm_SourceFile2Prefix.Text);
                Assert.AreEqual("0,480\r\n-,-", reader.pm_TrimingHeight.Text);

                Assert.AreEqual(@"D:\from", reader.fc_SourceFolderPath.Text);
                Assert.AreEqual(@"D:\to", reader.fc_DestFolderPath.Text);
                Assert.AreEqual("*.png", reader.fc_TargetFileName.Text);
            });
        }

        [TestMethod]
        public void PictTrimタブのラジオボタンが保存して読み直すと戻る()
        {
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.pt_SourceFolderPath.Text = @"D:\trim";
                writer.pt_BaseX.Text = "10";
                writer.pt_BaseY.Text = "20";
                writer.pt_Radio_SelectSizeOfEnd.IsChecked = true;
                writer.pt_TargetX.Text = "30";
                writer.pt_TargetY.Text = "40";

                string path = PathFor("trim");
                writer.sr.Save(path);

                MainWindow reader = NewWindow();
                reader.sr.Load(path);

                Assert.AreEqual(@"D:\trim", reader.pt_SourceFolderPath.Text);
                Assert.AreEqual("10", reader.pt_BaseX.Text);
                Assert.AreEqual("20", reader.pt_BaseY.Text);
                Assert.IsTrue(reader.pt_Radio_SelectSizeOfEnd.IsChecked == true);
                Assert.IsFalse(reader.pt_Radio_SelectPointOfEnd.IsChecked == true);
                Assert.AreEqual("30", reader.pt_TargetX.Text);
                Assert.AreEqual("40", reader.pt_TargetY.Text);
            });
        }

        [TestMethod]
        public void CaptureWindowのDataGridの行数が保存して読み直すと戻る()
        {
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.cw_Rows.Add(new CaptureGridRow());
                writer.cw_Rows.Add(new CaptureGridRow());

                string path = PathFor("datagrid");
                writer.sr.Save(path);

                MainWindow reader = NewWindow();
                reader.sr.Load(path);

                Assert.AreEqual(3, reader.cw_Rows.Count);
            });
        }

        [TestMethod]
        public void JSON保存でCaptureWindowタブの値が保存して読み直すと戻る()
        {
            // WinForms版ではXML経路とJSON経路の両方をテストしていた。WPF版はJSONだけなので、
            // ここでは保存したファイルが正しいJSON(キー"AttrName|AttrValue")になっていることも確かめる
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.cw_TextBox_SavePath.Text = @"C:\capture\out";
                writer.cw_TextBox_SaveFilePrefix.Text = "shot_";
                writer.cw_checkBox_AddTimeStamp.IsChecked = true;
                writer.cw_Radio_CurrentWindow.IsChecked = true;
                writer.cw_TextBox_Sleep.Text = "1234";
                writer.cw_TextBox_Loop.Text = "9";

                string path = Path.Combine(tempDirectory, "capture.json");
                Assert.IsTrue(writer.sr.Save(path), "JSON保存に成功するはず");

                string json = File.ReadAllText(path, Encoding.UTF8);
                StringAssert.Contains(json, "\"CaptureWindow|cw_TextBox_SaveFilePrefix\": \"shot_\"");
                StringAssert.Contains(json, "\"CaptureWindow|cw_checkBox_AddTimeStamp\": \"True\"");
                StringAssert.Contains(json, "\"DataGrid|Cell\"");

                MainWindow reader = NewWindow();
                Assert.IsTrue(reader.sr.Load(path), "JSON読み込みに成功するはず");

                Assert.AreEqual(@"C:\capture\out", reader.cw_TextBox_SavePath.Text);
                Assert.AreEqual("shot_", reader.cw_TextBox_SaveFilePrefix.Text);
                Assert.IsTrue(reader.cw_checkBox_AddTimeStamp.IsChecked == true);
                Assert.IsTrue(reader.cw_Radio_CurrentWindow.IsChecked == true);
                Assert.AreEqual("1234", reader.cw_TextBox_Sleep.Text);
                Assert.AreEqual("9", reader.cw_TextBox_Loop.Text);
            });
        }

        [TestMethod]
        public void JSON保存でCaptureWindowのDataGridの内容が保存して読み直すと戻る()
        {
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.cw_Rows.Add(new CaptureGridRow());
                writer.cw_Rows.Add(new CaptureGridRow());
                writer.cw_Rows[1][0] = "500";
                writer.cw_Rows[1].MouseAction = "LeftDown";
                writer.cw_Rows[1].Capture = "〇";

                string path = Path.Combine(tempDirectory, "datagrid.json");
                Assert.IsTrue(writer.sr.Save(path));

                MainWindow reader = NewWindow();
                Assert.IsTrue(reader.sr.Load(path));

                Assert.AreEqual(3, reader.cw_Rows.Count);
                Assert.AreEqual("500", reader.cw_Rows[1].Sleep);
                Assert.AreEqual("LeftDown", reader.cw_Rows[1].MouseAction);
                Assert.AreEqual("〇", reader.cw_Rows[1].Capture);
            });
        }

        [TestMethod]
        public void JSONファイルが無ければ読み込みは失敗を返す()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = NewWindow();
                Assert.IsFalse(window.sr.Load(Path.Combine(tempDirectory, "nothing.json")));
            });
        }

        [TestMethod]
        public void 存在しないファイルを読んでも例外にならず失敗を返す()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = NewWindow();
                window.cw_TextBox_SavePath.Text = "そのまま";

                Assert.IsFalse(window.LoadProfile(PathFor("nothing")));
                Assert.AreEqual("そのまま", window.cw_TextBox_SavePath.Text, "読めなかったときは値を変えない");
            });
        }

        [TestMethod]
        public void LoadProcはファイル名が空なら何もせず失敗を返す()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = NewWindow();
                window.do_WhiteCoef.Text = "1";

                Assert.IsFalse(window.LoadProfile(""));
                Assert.AreEqual("1", window.do_WhiteCoef.Text, "ファイル名が空なら既定値の再設定もしない");
            });
        }

        [TestMethod]
        public void LoadProcは白コエフの既定値を先に30へ戻してから読み込む()
        {
            // WinForms版の LoadProc は読み込み前に do_WhiteCoef.Text = "30" を決め打ちしている。
            // WPF版では MainWindow.LoadProfile が同じ処理をする
            StaRunner.Run(() =>
            {
                MainWindow writer = NewWindow();
                writer.do_WhiteCoef.Text = "99";
                string path = PathFor("whitecoef");
                writer.sr.Save(path);

                MainWindow reader = NewWindow();
                reader.do_WhiteCoef.Text = "1";

                Assert.IsTrue(reader.LoadProfile(path));

                // ファイルに保存されている値（99）が最終的に反映される
                Assert.AreEqual("99", reader.do_WhiteCoef.Text);

                // ファイルに値が無い場合は既定値(30)になる
                string emptyPath = PathFor("empty");
                File.WriteAllText(emptyPath, "{ \"Values\": {} }", new UTF8Encoding(false));
                reader.do_WhiteCoef.Text = "1";
                Assert.IsTrue(reader.LoadProfile(emptyPath));
                Assert.AreEqual("30", reader.do_WhiteCoef.Text);
            });
        }

        // ------------------------------------------------------------------
        // WPF版で追加したテスト
        // ------------------------------------------------------------------

        [TestMethod]
        public void WinForms版で保存したJSONプロファイルを読める()
        {
            // WinForms版Cheetos(StcSaveRestore.BuildGenericProfile + JsonFileStorage)が書き出す形式。
            // 旧キー(typoのあるcw_TextBox_SaveFilePrifix / cw_checkBox_AddTimeStump)で保存されたものも読めること
            string path = Path.Combine(tempDirectory, "winforms.json");
            File.WriteAllText(path,
                "{\n" +
                "  \"Values\": {\n" +
                "    \"CaptureWindow|cw_TextBox_SavePath\": \"C:\\\\capture\",\n" +
                "    \"CaptureWindow|cw_TextBox_SaveFilePrifix\": \"shot\",\n" +
                "    \"CaptureWindow|cw_checkBox_AddTimeStump\": \"True\",\n" +
                "    \"CaptureWindow|cw_Radio_FullScreen\": \"False\",\n" +
                "    \"CaptureWindow|cw_Radio_CurrentScreen\": \"True\",\n" +
                "    \"CaptureWindow|cw_Radio_CurrentWindow\": \"False\",\n" +
                "    \"CaptureWindow|cw_TextBox_Sleep\": \"1500\",\n" +
                "    \"PictTrim|pt_BaseX\": \"12\",\n" +
                "    \"DistOrient|do_WhiteLength\": \"5\"\n" +
                "  },\n" +
                "  \"Lists\": {},\n" +
                "  \"CheckedStates\": {},\n" +
                "  \"Grids\": {\n" +
                "    \"DataGrid|Cell\": [\n" +
                "      [ \"1000\", \"640\", \"1660\", \"Move\", \"×\" ],\n" +
                "      [ \"500\", \"1640\", \"1100\", \"LeftDown\", \"〇\" ]\n" +
                "    ]\n" +
                "  }\n" +
                "}", new UTF8Encoding(false));

            StaRunner.Run(() =>
            {
                MainWindow window = NewWindow();
                window.fc_TargetFileName.Text = "消える値";
                window.do_WhiteCoef.Text = "1";
                Assert.IsTrue(window.LoadProfile(path));

                Assert.AreEqual(@"C:\capture", window.cw_TextBox_SavePath.Text);
                Assert.AreEqual("shot", window.cw_TextBox_SaveFilePrefix.Text);
                Assert.IsTrue(window.cw_checkBox_AddTimeStamp.IsChecked == true);
                Assert.IsTrue(window.cw_Radio_CurrentScreen.IsChecked == true);
                Assert.IsFalse(window.cw_Radio_FullScreen.IsChecked == true);
                Assert.AreEqual("1500", window.cw_TextBox_Sleep.Text);
                Assert.AreEqual("12", window.pt_BaseX.Text);
                Assert.AreEqual("5", window.do_WhiteLength.Text);

                // ファイルに無い項目は既定値に戻る(WinForms版と同じ)
                Assert.AreEqual("", window.fc_TargetFileName.Text);
                Assert.AreEqual("30", window.do_WhiteCoef.Text);
                Assert.AreEqual("2", window.cw_TextBox_Loop.Text);

                Assert.AreEqual(2, window.cw_Rows.Count);
                Assert.AreEqual("1000", window.cw_Rows[0].Sleep);
                Assert.AreEqual("640", window.cw_Rows[0].MouseX);
                Assert.AreEqual("1660", window.cw_Rows[0].MouseY);
                Assert.AreEqual("Move", window.cw_Rows[0].MouseAction);
                Assert.AreEqual("×", window.cw_Rows[0].Capture);
                Assert.AreEqual("LeftDown", window.cw_Rows[1].MouseAction);
                Assert.AreEqual("〇", window.cw_Rows[1].Capture);
            });
        }

        [TestMethod]
        public void LoadOrDefaultは存在しないファイルなら既定値に戻す()
        {
            StaRunner.Run(() =>
            {
                MainWindow window = NewWindow();
                window.cw_TextBox_Sleep.Text = "1";
                window.cw_TextBox_Loop.Text = "1";
                window.cw_Radio_CurrentWindow.IsChecked = true;
                window.cw_Rows.Add(new CaptureGridRow());

                window.sr.LoadOrDefault(Path.Combine(tempDirectory, "nothing.json"));

                Assert.AreEqual("2000", window.cw_TextBox_Sleep.Text);
                Assert.AreEqual("2", window.cw_TextBox_Loop.Text);
                Assert.AreEqual("30", window.do_WhiteCoef.Text);
                Assert.IsTrue(window.cw_Radio_FullScreen.IsChecked == true);
                Assert.AreEqual(1, window.cw_Rows.Count, "DataGridは空の1行に戻る");
            });
        }

        [TestMethod]
        public void 起動時にデータフォルダのプロファイルを読み込む()
        {
            // WinForms版と同じく、プロファイル一覧の先頭が選ばれてそのプロファイルが読み込まれる
            File.WriteAllText(Path.Combine(dataDirectory, "a.json"),
                "{ \"Values\": { \"Rotation|pr_Angle\": \"90\" } }", new UTF8Encoding(false));

            StaRunner.Run(() =>
            {
                MainWindow window = NewWindow();
                Assert.AreEqual("a.json", window.Profile.SelectedItem);
                Assert.AreEqual("90", window.pr_Angle.Text);
            });
        }
    }
}
