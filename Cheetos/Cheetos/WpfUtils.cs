// Cheetos専用のWPF用ヘルパー(画像の変換・キャプチャ用の目印など)。
// どのアプリでも使える汎用の処理(選択項目を並び順で取る等)は
// _Common/Wpf/WpfControlUtils.cs([[_Common/Wpf/WpfControlUtils.cs]])へ移した。
using System;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Cheetos
{
    internal static class WpfUtils
    {
        // WinForms版の「ListBox.Sorted=true のリストへ Items.Add」の代わり。並べ替えてから一括で入れる
        public static void SetSortedItems(ListBox listCtrl, String[] items)
        {
            String[] sorted = (String[])items.Clone();
            Array.Sort(sorted, StringComparer.CurrentCulture);

            listCtrl.Items.Clear();
            foreach (String item in sorted)
            {
                listCtrl.Items.Add(item);
            }
        }

        // GDI+のBitmapをWPFで表示できるBitmapSourceへ変換する(96dpi・アルファ付き)。
        // 元のBitmapは呼び出し側で破棄してよい(ピクセルはコピー済み)
        public static BitmapSource ToBitmapSource(System.Drawing.Bitmap bmp)
        {
            System.Drawing.Rectangle rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                BitmapSource source = BitmapSource.Create(bmp.Width, bmp.Height, 96, 96, PixelFormats.Bgra32, null,
                    data.Scan0, data.Stride * bmp.Height, data.Stride);
                source.Freeze();
                return source;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        // 整数として読めないテキストボックスを空欄にする
        public static void ClearIfNotInteger(TextBox ctrl)
        {
            int val;
            if (!Int32.TryParse(ctrl.Text, out val))
            {
                ctrl.Text = "";
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        // CaptWindow.TargetWindow(WinFormsのControl)に渡すための「目印」コントロールを作る。
        // CaptWindowはScreen.FromControl(TargetWindow)で「今このウィンドウがあるモニタ」を判定するが、
        // WPFのWindowはWinFormsのControlではないため、非表示の小さなWinFormsコントロールを作って
        // WPFウィンドウの子ウィンドウにしておく(子ウィンドウはいつも親と同じモニタ上にいる)。
        // WindowのSourceInitialized以降(HWNDができてから)に呼ぶこと
        public static System.Windows.Forms.Control CreateScreenAnchor(System.Windows.Window owner)
        {
            IntPtr ownerHandle = new WindowInteropHelper(owner).Handle;
            if (ownerHandle == IntPtr.Zero)
            {
                return null;
            }

            System.Windows.Forms.Control anchor = new System.Windows.Forms.Control();
            anchor.Visible = false;
            anchor.Size = new System.Drawing.Size(1, 1);
            IntPtr anchorHandle = anchor.Handle;
            SetParent(anchorHandle, ownerHandle);
            return anchor;
        }
    }
}
