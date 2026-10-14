namespace DigitalClock
{
    /// <summary>
    /// 終了時のウィンドウ位置。WinForms版はProperties.Settings(user.config)に保存していたが、
    /// WPF版は%LOCALAPPDATA%\DigitalClock\DigitalClock.json にJSONで保存する。
    /// 大きさは文字に合わせて自動で決まり、ユーザーが変えられないので保存しない
    /// </summary>
    internal class WindowPlacement
    {
        public double Left { get; set; }
        public double Top { get; set; }
    }
}
