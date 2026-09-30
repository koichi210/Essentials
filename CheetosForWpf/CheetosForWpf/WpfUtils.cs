// StcUtils([[_Common/StandardTemplateClass.cs]])のうち、WinFormsのコントロールを引数に取るため
// WPFから使えないものの代わり(プロジェクト内ローカル版)。
// _Common/Wpf に同等品ができたら置き換える候補。
using System;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CheetosForWpf
{
    internal static class WpfUtils
    {
        // StcUtils.GetStrArrayFromListBox の代わり。
        // WPFのSelectedItemsは「選択した順」に並ぶため、WinForms版と同じ「リストの並び順」に揃えて返す
        public static String[] GetSelectedStrArray(ListBox ListCtrl)
        {
            return ListCtrl.Items.Cast<Object>()
                .Where(item => ListCtrl.SelectedItems.Contains(item))
                .Select(item => item.ToString())
                .ToArray();
        }

        // WinForms版の「ListBox.Sorted=true のリストへ Items.Add」の代わり。並べ替えてから一括で入れる
        public static void SetSortedItems(ListBox ListCtrl, String[] Items)
        {
            String[] sorted = (String[])Items.Clone();
            Array.Sort(sorted, StringComparer.CurrentCulture);

            ListCtrl.Items.Clear();
            foreach (String item in sorted)
            {
                ListCtrl.Items.Add(item);
            }
        }

        // GDI+のBitmapをWPFで表示できるBitmapSourceへ変換する(96dpi・アルファ付き)。
        // 元のBitmapは呼び出し側で破棄してよい(ピクセルはコピー済み)
        public static BitmapSource ToBitmapSource(System.Drawing.Bitmap Bmp)
        {
            System.Drawing.Rectangle rect = new System.Drawing.Rectangle(0, 0, Bmp.Width, Bmp.Height);
            BitmapData data = Bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                BitmapSource source = BitmapSource.Create(Bmp.Width, Bmp.Height, 96, 96, PixelFormats.Bgra32, null,
                    data.Scan0, data.Stride * Bmp.Height, data.Stride);
                source.Freeze();
                return source;
            }
            finally
            {
                Bmp.UnlockBits(data);
            }
        }

        // ファイルをロックしたままにしないよう、一度メモリへ読み込んでから変換する
        public static BitmapSource LoadBitmapSource(String FilePath)
        {
            using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(FilePath))
            {
                return ToBitmapSource(bmp);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        // CaptWindow.TargetWindow(WinFormsのControl)に渡すための「目印」コントロールを作る。
        // CaptWindowはScreen.FromControl(TargetWindow)で「今このウィンドウがあるモニタ」を判定するが、
        // WPFのWindowはWinFormsのControlではないため、非表示の小さなWinFormsコントロールを作って
        // WPFウィンドウの子ウィンドウにしておく(子ウィンドウはいつも親と同じモニタ上にいる)。
        // WindowのSourceInitialized以降(HWNDができてから)に呼ぶこと
        public static System.Windows.Forms.Control CreateScreenAnchor(System.Windows.Window Owner)
        {
            IntPtr ownerHandle = new WindowInteropHelper(Owner).Handle;
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
