using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Drawing = System.Drawing;

namespace CheetosForWpf
{
    // Rotationタブの「プレビュー」で開く、回転結果の確認画面(WinForms版のRotationPreviewフォーム)。
    // 画像処理はWinForms版と同じくGDI+(System.Drawing)で行い、表示するときだけBitmapSourceへ変換する
    public partial class RotationPreview : Window
    {
        public String OriginX = "";
        public String OriginY = "";
        public String Angle = "";

        // 画面の組み立て中(InitializeComponent中や初期値の代入中)はTextChangedで描画しない
        private Boolean IsInitialized_ = false;

        public RotationPreview()
        {
            InitializeComponent();
            IsInitialized_ = true;
        }

        public RotationPreview(String OriginX, String OriginY, String Angle)
        {
            InitializeComponent();

            textBox_OriginX.Text = OriginX;
            textBox_OriginY.Text = OriginY;
            textBox_angle.Text = Angle;
            IsInitialized_ = true;
        }

        private void button_ClickDraw(object sender, RoutedEventArgs e)
        {
            pictureBox_Source.Source = null;
            pictureBox_Source.Source = WpfUtils.LoadBitmapSource(textBox_loadfiepath.Text);
            Draw();
        }

        // WinForms版はKeyPressのたびに再描画していた。WPFでは入力後の値で描画できるTextChangedを使う
        private void textBox_Param_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (IsInitialized_)
            {
                Draw();
            }
        }

        // ↑/↓キーで数値を増減する(TextBoxが↑/↓をキャレット移動に使ってしまう前に拾うためPreviewKeyDown)
        private void textBox_OriginX_KeyDown(object sender, KeyEventArgs e)
        {
            UpdateValue(textBox_OriginX, e);
        }

        private void textBox_OriginY_KeyDown(object sender, KeyEventArgs e)
        {
            UpdateValue(textBox_OriginY, e);
        }

        private void textBox_Angle_KeyDown(object sender, KeyEventArgs e)
        {
            UpdateValue(textBox_angle, e);
        }

        private void UpdateValue(TextBox Ctrl, KeyEventArgs e)
        {
            if (e.Key != Key.Up && e.Key != Key.Down)
            {
                return;
            }

            // 値が変わればTextChangedで再描画される
            Ctrl.Text = Logic.UpdateValue(Ctrl.Text, e.Key);
            Ctrl.CaretIndex = Ctrl.Text.Length;
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
            if (!File.Exists(textBox_loadfiepath.Text))
            {
                return false;
            }

            int val;
            if (!Int32.TryParse(textBox_OriginX.Text.ToString(), out val))
            {
                textBox_OriginX.Text = "";
            }
            if (!Int32.TryParse(textBox_OriginY.Text.ToString(), out val))
            {
                textBox_OriginY.Text = "";
            }
            if (!Int32.TryParse(textBox_angle.Text.ToString(), out val))
            {
                textBox_angle.Text = "";
            }

            return true;
        }

        private void Draw()
        {
            if (!AdjustParam())
            {
                return;
            }

            using (Drawing.Bitmap img = new Drawing.Bitmap(textBox_loadfiepath.Text))
            {
                int max = img.Width;
                if (img.Width < img.Height)
                {
                    max = img.Height;
                }

                using (Drawing.Bitmap canvas = new Drawing.Bitmap(max * 2, max * 2))
                {
                    //ラジアン単位に変換
                    int angle = 0;
                    Int32.TryParse(textBox_angle.Text.ToString(), out angle);
                    double d = angle / (180 / Math.PI);

                    //新しい座標位置を計算する
                    // (WinForms版はfloat.Parseで、空欄だと例外になっていた。空欄は0として扱う)
                    float x;
                    float y;
                    float.TryParse(textBox_OriginX.Text.ToString(), out x);
                    float.TryParse(textBox_OriginY.Text.ToString(), out y);

                    float x1 = x + img.Width * (float)Math.Cos(d);
                    float y1 = y + img.Width * (float)Math.Sin(d);
                    float x2 = x - img.Height * (float)Math.Sin(d);
                    float y2 = y + img.Height * (float)Math.Cos(d);

                    //PointF配列を作成
                    Drawing.PointF[] destinationPoints =
                    {
                        new Drawing.PointF(x, y),
                        new Drawing.PointF(x1, y1),
                        new Drawing.PointF(x2, y2)
                    };

                    using (Drawing.Graphics g = Drawing.Graphics.FromImage(canvas))
                    {
                        //画像を表示
                        g.DrawImage(img, destinationPoints);
                    }

                    //pictureBoxに表示
                    pictureBox_Dest.Source = WpfUtils.ToBitmapSource(canvas);
                }
            }
        }
    }
}
