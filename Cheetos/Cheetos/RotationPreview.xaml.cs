using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Drawing = System.Drawing;

namespace Cheetos
{
    // Rotationタブの「プレビュー」で開く、回転結果の確認画面(WinForms版のRotationPreviewフォーム)。
    // 画像処理はWinForms版と同じくGDI+(System.Drawing)で行い、表示するときだけBitmapSourceへ変換する
    public partial class RotationPreview : Window
    {
        // OKで閉じたときの入力値
        public String OriginX { get; private set; } = "";
        public String OriginY { get; private set; } = "";
        public String Angle { get; private set; } = "";

        // 画面の組み立て中(InitializeComponent中や初期値の代入中)はTextChangedで描画しない
        private Boolean _isConstructed = false;

        // 描画元画像のキャッシュ。数値を1文字変えるたびに画像ファイルをデコードし直さないよう、
        // 同じパスの間は使い回す(「load」ボタンで読み直す)。
        // ファイルをロックしないよう、ファイルの中身はメモリへ読み込んでからデコードする
        private String _sourcePath;
        private MemoryStream _sourceStream;
        private Drawing.Bitmap _sourceBitmap;

        public RotationPreview()
        {
            InitializeComponent();
            Closed += (s, e) => ReleaseSourceBitmap();
            _isConstructed = true;
        }

        // 回転後のキャンバスの横幅の初期値は、元画像の幅のこの倍率
        private const double _defaultCanvasWidthRate = 1.5;
        private const int _maxCanvasWidth = 20000;

        public RotationPreview(String originX, String originY, String angle) : this(originX, originY, angle, "")
        {
        }

        // filePath: 最初に読み込む画像(Rotationタブの一覧の先頭のファイル)。あれば開いた時点で読み込んで表示する
        public RotationPreview(String originX, String originY, String angle, String filePath) : this()
        {
            _isConstructed = false;
            textBox_OriginX.Text = originX;
            textBox_OriginY.Text = originY;
            textBox_angle.Text = angle;
            textBox_LoadFilePath.Text = filePath ?? "";
            _isConstructed = true;

            if (File.Exists(textBox_LoadFilePath.Text))
            {
                Loaded += (s, e) => LoadSourceImage(false);
            }
        }

        private void button_ClickDraw(object sender, RoutedEventArgs e)
        {
            LoadSourceImage(true);
        }

        // 画像ファイルを読み直して元画像を表示し、横幅を元画像の1.5倍にしてから回転後を描く。
        // 以前はファイルが無いと例外になっていたので、先に有るか確かめる
        private void LoadSourceImage(Boolean showMessage)
        {
            String filePath = textBox_LoadFilePath.Text;
            if (!File.Exists(filePath))
            {
                if (showMessage)
                {
                    MessageBox.Show("ファイルが見つかりません。\n" + filePath, "RotationPreview");
                }
                return;
            }

            ReleaseSourceBitmap();
            pictureBox_Source.Source = null;
            Drawing.Bitmap img;
            try
            {
                img = GetSourceBitmap(filePath);
            }
            catch (Exception ex)
            {
                if (showMessage)
                {
                    MessageBox.Show("画像を読み込めませんでした。\n" + filePath + "\n" + ex.Message, "RotationPreview");
                }
                return;
            }
            pictureBox_Source.Source = WpfUtils.ToBitmapSource(img);

            _isConstructed = false;
            textBox_CanvasWidth.Text = ((int)Math.Round(img.Width * _defaultCanvasWidthRate)).ToString();
            _isConstructed = true;
            Draw();
        }

        // WinForms版はKeyPressのたびに再描画していた。WPFでは入力後の値で描画できるTextChangedを使う
        private void textBox_Param_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isConstructed)
            {
                Draw();
            }
        }

        // ↑/↓キーで数値を増減する(TextBoxが↑/↓をキャレット移動に使ってしまう前に拾うためPreviewKeyDown)
        private void textBox_OriginX_KeyDown(object sender, KeyEventArgs e)
        {
            StepTextBoxValueByArrowKey(textBox_OriginX, e);
        }

        private void textBox_OriginY_KeyDown(object sender, KeyEventArgs e)
        {
            StepTextBoxValueByArrowKey(textBox_OriginY, e);
        }

        private void textBox_Angle_KeyDown(object sender, KeyEventArgs e)
        {
            StepTextBoxValueByArrowKey(textBox_angle, e);
        }

        private void StepTextBoxValueByArrowKey(TextBox ctrl, KeyEventArgs e)
        {
            if (e.Key != Key.Up && e.Key != Key.Down)
            {
                return;
            }

            // 値が変わればTextChangedで再描画される
            ctrl.Text = Logic.StepValueByArrowKey(ctrl.Text, e.Key);
            ctrl.CaretIndex = ctrl.Text.Length;
            e.Handled = true;
        }

        private void buttonOk_Click(object sender, RoutedEventArgs e)
        {
            OriginX = textBox_OriginX.Text;
            OriginY = textBox_OriginY.Text;
            Angle = textBox_angle.Text;

            this.DialogResult = true;
            this.Close();
        }

        private void buttonCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private Boolean AdjustParam()
        {
            if (!File.Exists(textBox_LoadFilePath.Text))
            {
                return false;
            }

            // 入力のたびに呼ばれるので、「-」は負の数の入力途中として残す(描画では0として扱う)
            WpfUtils.ClearIfNotInteger(textBox_OriginX, true);
            WpfUtils.ClearIfNotInteger(textBox_OriginY, true);
            WpfUtils.ClearIfNotInteger(textBox_angle, true);
            return true;
        }

        // 描画元画像を返す。前回と同じパスならキャッシュを返す
        private Drawing.Bitmap GetSourceBitmap(String filePath)
        {
            if (_sourceBitmap != null && _sourcePath == filePath)
            {
                return _sourceBitmap;
            }

            ReleaseSourceBitmap();
            MemoryStream stream = new MemoryStream(File.ReadAllBytes(filePath));
            try
            {
                _sourceBitmap = new Drawing.Bitmap(stream);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
            _sourceStream = stream;
            _sourcePath = filePath;
            return _sourceBitmap;
        }

        private void ReleaseSourceBitmap()
        {
            if (_sourceBitmap != null)
            {
                _sourceBitmap.Dispose();
                _sourceBitmap = null;
            }
            if (_sourceStream != null)
            {
                _sourceStream.Dispose();
                _sourceStream = null;
            }
            _sourcePath = null;
        }

        private void Draw()
        {
            if (!AdjustParam())
            {
                return;
            }

            Drawing.Bitmap img;
            try
            {
                img = GetSourceBitmap(textBox_LoadFilePath.Text);
            }
            catch (Exception)
            {
                // 画像として読めないファイル(loadボタンの方で知らせる)
                return;
            }

            // 横幅は入力値(空欄・数値以外は元画像の幅の1.5倍)、高さは回転実行と同じく元画像の対角線の長さ
            int canvasWidth;
            if (!Int32.TryParse(textBox_CanvasWidth.Text, out canvasWidth) || canvasWidth <= 0)
            {
                canvasWidth = (int)Math.Round(img.Width * _defaultCanvasWidthRate);
            }
            int canvasHeight = (int)Math.Sqrt((double)img.Width * img.Width + (double)img.Height * img.Height);

            // 桁の打ち間違いで巨大なキャンバスを作ろうとして落ちないよう、上限を決めておく
            canvasWidth = Math.Min(canvasWidth, _maxCanvasWidth);
            using (Drawing.Bitmap canvas = new Drawing.Bitmap(Math.Max(canvasWidth, 1), Math.Max(canvasHeight, 1)))
            {
                // (WinForms版はfloat.Parseで、空欄だと例外になっていた。空欄は0として扱う)
                int angle;
                Int32.TryParse(textBox_angle.Text, out angle);
                float x;
                float y;
                float.TryParse(textBox_OriginX.Text, out x);
                float.TryParse(textBox_OriginY.Text, out y);

                Rotation.DrawRotated(canvas, img, x, y, angle);

                //pictureBoxに表示
                pictureBox_Dest.Source = WpfUtils.ToBitmapSource(canvas);
            }
        }
    }
}
