using System;

namespace DigitalClock
{
    /// <summary>
    /// WinForms版DigitalClockのLogic.csをそのまま移したもの(時刻を "HH:mm:ss" 形式に整形する)。
    /// 起動時の位置復元で画面外に出ないかの判定も、テストできるようにここに置いた。
    /// </summary>
    internal static class Logic
    {
        public static String FormatTime(DateTime time)
        {
            return String.Format("{0:00}:{1:00}:{2:00}", time.Hour, time.Minute, time.Second);
        }

        // 保存されていた位置(left, top)が、全モニタを合わせた仮想スクリーンの中に収まっているか。
        // モニタを外した後などに見えない場所へ復元されて掴めなくなるのを防ぐため、
        // 左上の点が仮想スクリーン内にあることだけを確認する
        public static Boolean IsOnScreen(double left, double top,
            double screenLeft, double screenTop, double screenWidth, double screenHeight)
        {
            if (Double.IsNaN(left) || Double.IsNaN(top))
            {
                return false;
            }
            return left >= screenLeft && left < screenLeft + screenWidth
                && top >= screenTop && top < screenTop + screenHeight;
        }
    }
}
