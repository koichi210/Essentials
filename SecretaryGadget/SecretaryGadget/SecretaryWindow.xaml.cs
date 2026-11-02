using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SecretaryGadget
{
    /// <summary>
    /// 秘書さん(キャラ＋吹き出し)。作業領域の右下からせり上がって吹き出しでお知らせし、一定時間で引っ込む。
    /// 拡張スタイル WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE で、フォーカスを奪わずタスクバーにも出ない。
    /// 画面キャプチャからは除外(Teams の画面共有やスクショに映らない)。
    /// </summary>
    public partial class SecretaryWindow : Window
    {
        private const int _riseMs = 450;
        private const int _bubbleFadeInMs = 250;
        private const int _bubbleFadeInDelayMs = 300;
        private const int _bubbleFadeOutMs = 150;
        private const double _lineHeight = 19;
        private static readonly TimeSpan _peekTimeout = TimeSpan.FromSeconds(8);

        private readonly DispatcherTimer _autoHideTimer;
        private readonly DispatcherTimer _peekTimer;
        private SkinInfo _skin = SkinCatalog.BuiltIn;
        private SkinImage? _image;
        private double _scale = 1.0;
        private int _displayHeight = 420;
        private bool _captureExcluded = true;
        private bool _mouseOverBubble;
        private bool _retracting;
        // アニメーション完了時に「その後に別の表示が始まっていないか」を見分ける番号
        private int _generation;

        public SecretaryWindow()
        {
            InitializeComponent();
            _autoHideTimer = new DispatcherTimer();
            _autoHideTimer.Tick += (_, __) =>
            {
                _autoHideTimer.Stop();
                Retract();
            };
            _peekTimer = new DispatcherTimer { Interval = _peekTimeout };
            _peekTimer.Tick += (_, __) =>
            {
                _peekTimer.Stop();
                if (IsPeeking)
                {
                    // 8秒以内に中身が来なければ黙って引っ込む
                    Retract();
                }
            };
        }

        /// <summary>吹き出しクリックで引っ込み終わったあと(Notice.Open の実行は呼び出し側)</summary>
        internal event Action<Notice>? BubbleOpened;

        /// <summary>引っ込み終わった(次のお知らせへ)</summary>
        internal event Action? Retracted;

        /// <summary>窓が出ている(Peek・引っ込み途中を含む)</summary>
        internal bool IsShowing { get; private set; }

        /// <summary>「秘書さんを呼ぶ」直後の、中身を待っている状態</summary>
        internal bool IsPeeking { get; private set; }

        internal Notice? CurrentNotice { get; private set; }

        /// <summary>隠れるまでの秒数</summary>
        internal int DisplaySeconds { get; set; } = 20;

        /// <summary>false なら「自動的に隠さない」</summary>
        internal bool AutoHide { get; private set; } = true;

        internal void SetAutoHide(bool autoHide)
        {
            AutoHide = autoHide;
            RestartAutoHide();
        }

        /// <summary>キャプチャ除外の切り替え。「自動的に隠さない」中だけ WDA_NONE にする。</summary>
        internal void SetCaptureExcluded(bool exclude)
        {
            _captureExcluded = exclude;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowDisplayAffinity(hwnd, exclude ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE);
            }
        }

        internal void ApplySkin(SkinInfo skin, SkinImage image, double scale, int displayHeight)
        {
            _skin = skin;
            _image = image;
            _scale = scale;
            _displayHeight = displayHeight;
            CharacterImage.Source = image.Bitmap;
            if (IsShowing)
            {
                Relayout();
            }
        }

        /// <summary>お知らせを出す。表示中なら差し替える。</summary>
        internal void ShowNotice(Notice notice, string? footer)
        {
            _peekTimer.Stop();
            IsPeeking = false;
            CurrentNotice = notice;
            SetTexts(notice, footer);
            BeginShow(showBubble: true);
        }

        /// <summary>中身を読む前に絵だけ先にせり上げる(吹き出しはクリック不可)。</summary>
        internal void Peek()
        {
            CurrentNotice = null;
            SetTexts(null, null);
            IsPeeking = true;
            BeginShow(showBubble: false);
            _peekTimer.Stop();
            _peekTimer.Start();
        }

        /// <summary>引っ込む: 吹き出し150msで消す → 絵を450msで下げる → Hide → Retracted。</summary>
        internal void Retract(Action? afterHidden = null)
        {
            if (!IsShowing || _retracting)
            {
                return;
            }
            _retracting = true;
            _autoHideTimer.Stop();
            _peekTimer.Stop();
            int generation = ++_generation;
            BubbleLayer.IsHitTestVisible = false;
            AnimateBubble(0, _bubbleFadeOutMs, TimeSpan.Zero, () =>
            {
                if (generation != _generation)
                {
                    return;
                }
                AnimateCharacter(Height, new CubicEase { EasingMode = EasingMode.EaseIn }, () =>
                {
                    if (generation != _generation)
                    {
                        return;
                    }
                    FinishHide();
                    afterHidden?.Invoke();
                    Retracted?.Invoke();
                });
            });
        }

        /// <summary>アニメーションなしで即座に隠す(ロック・プレゼン中の抑止用)。表示していたお知らせを返す。</summary>
        internal Notice? HideNow()
        {
            Notice? notice = CurrentNotice;
            _generation++;
            _autoHideTimer.Stop();
            _peekTimer.Stop();
            CharacterShift.BeginAnimation(TranslateTransform.YProperty, null);
            BubbleLayer.BeginAnimation(OpacityProperty, null);
            FinishHide();
            return notice;
        }

        /// <summary>
        /// --render-preview 用。窓を画面外に置き、吹き出し付きの見た目を背景 #3A6EA5 で PNG にする。
        /// </summary>
        internal void RenderPreview(Notice notice, string? footer, string path)
        {
            CurrentNotice = notice;
            SetTexts(notice, footer);
            Relayout();
            Left = -32000;
            Top = -32000;
            Show();
            CharacterShift.BeginAnimation(TranslateTransform.YProperty, null);
            BubbleLayer.BeginAnimation(OpacityProperty, null);
            CharacterShift.Y = 0;
            BubbleLayer.Opacity = 1;
            UpdateLayout();

            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            int pixelWidth = (int)Math.Ceiling(Width * dpi.DpiScaleX);
            int pixelHeight = (int)Math.Ceiling(Height * dpi.DpiScaleY);
            var content = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            content.Render(RootCanvas);
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                var rect = new Rect(0, 0, Width, Height);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x3A, 0x6E, 0xA5)), null, rect);
                dc.DrawImage(content, rect);
            }
            var output = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            output.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(output));
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            using FileStream fs = File.Create(path);
            encoder.Save(fs);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetExStyle(hwnd, NativeMethods.GetExStyle(hwnd) | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
            // 起動時は必ずキャプチャから除外する
            SetCaptureExcluded(_captureExcluded);
        }

        private void BeginShow(bool showBubble)
        {
            // 引っ込み途中のアニメーション完了処理を無効にする
            _generation++;
            _retracting = false;
            Relayout();
            if (!IsVisible)
            {
                CharacterShift.BeginAnimation(TranslateTransform.YProperty, null);
                BubbleLayer.BeginAnimation(OpacityProperty, null);
                CharacterShift.Y = Height;
                BubbleLayer.Opacity = 0;
                Show();
            }
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            IsShowing = true;

            // 絵が半分より下にあるときは、せり上がりを待って吹き出しを300ms遅らせる
            bool lowerHalf = CharacterShift.Y > Height / 2;
            AnimateCharacter(0, new CubicEase { EasingMode = EasingMode.EaseOut }, null);
            BubbleLayer.IsHitTestVisible = showBubble;
            if (showBubble)
            {
                AnimateBubble(1, _bubbleFadeInMs, TimeSpan.FromMilliseconds(lowerHalf ? _bubbleFadeInDelayMs : 0), null);
            }
            else
            {
                AnimateBubble(0, 0, TimeSpan.Zero, null);
            }
            RestartAutoHide();
        }

        private void FinishHide()
        {
            Hide();
            IsShowing = false;
            IsPeeking = false;
            _retracting = false;
            _mouseOverBubble = false;
            CurrentNotice = null;
        }

        /// <summary>自動で隠れるタイマーを数え直す。マウスが吹き出し上にある間・「自動的に隠さない」中・Peek 中は止める。</summary>
        private void RestartAutoHide()
        {
            _autoHideTimer.Stop();
            if (!IsShowing || IsPeeking || _retracting || !AutoHide || _mouseOverBubble || CurrentNotice?.IsSticky == true)
            {
                return;
            }
            _autoHideTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, DisplaySeconds));
            _autoHideTimer.Start();
        }

        private void SetTexts(Notice? notice, string? footer)
        {
            HeadlineText.Text = notice?.Headline ?? string.Empty;
            SetLine(Line1Text, notice?.Line1, notice != null && !notice.IsUnlimited ? 2 : 0);
            SetLine(Line2Text, notice?.Line2, notice != null && !notice.IsUnlimited ? 3 : 0);
            SetLine(Line3Text, notice?.Line3, 0);
            SetLine(FooterText, footer, 0);
            CloseMark.Visibility = notice == null ? Visibility.Hidden : Visibility.Visible;
        }

        /// <summary>maxLines=0 なら高さ制限なし。</summary>
        private static void SetLine(TextBlock block, string? text, int maxLines)
        {
            block.Text = text ?? string.Empty;
            block.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            block.MaxHeight = maxLines > 0 ? maxLines * _lineHeight : double.PositiveInfinity;
        }

        /// <summary>配置計算(出すたび・スキン/大きさ変更時)。右下の角を作業領域の右下(右端−12)に固定する。</summary>
        private void Relayout()
        {
            // 文面差し替え後は TextBlock から吹き出しまでの親を全部 InvalidateMeasure してから測る(前回の高さの使い回し対策)
            foreach (UIElement e in new UIElement[] { HeadlineText, Line1Text, Line2Text, Line3Text, FooterText, CloseMark })
            {
                InvalidateUp(e);
            }
            Bubble.Measure(new Size(BubbleLayout.BubbleWidth, double.PositiveInfinity));
            double bubbleHeight = Bubble.DesiredSize.Height;

            SkinImage image = _image ?? SkinCatalog.LoadImage(_skin);
            _image = image;
            BubbleLayoutResult r = BubbleLayout.Compute(_displayHeight, _scale, bubbleHeight, image.PixelWidth, image.PixelHeight, image.LeftmostOpaque, _skin.BubbleOverlap);

            Width = r.WindowWidth;
            Height = r.WindowHeight;
            RootCanvas.Width = r.WindowWidth;
            RootCanvas.Height = r.WindowHeight;
            CharacterImage.Width = r.ImageWidth;
            CharacterImage.Height = r.ImageHeight;
            Canvas.SetLeft(CharacterImage, r.ImageLeft);
            Canvas.SetTop(CharacterImage, r.ImageTop);
            Canvas.SetLeft(Bubble, BubbleLayout.BubbleLeft);
            Canvas.SetTop(Bubble, r.BubbleTop);
            Canvas.SetLeft(Tail, BubbleLayout.BubbleLeft + BubbleLayout.BubbleWidth - BubbleLayout.TailOverlap);
            Canvas.SetTop(Tail, r.TailTip - BubbleLayout.TailHalfHeight);

            Rect workArea = SystemParameters.WorkArea;
            Left = workArea.Right - BubbleLayout.RightMargin - r.WindowWidth;
            Top = workArea.Bottom - r.WindowHeight;
        }

        private void InvalidateUp(UIElement element)
        {
            DependencyObject? current = element;
            while (current is UIElement ui)
            {
                ui.InvalidateMeasure();
                if (ReferenceEquals(current, Bubble))
                {
                    break;
                }
                current = VisualTreeHelper.GetParent(current);
            }
        }

        private void AnimateCharacter(double to, IEasingFunction ease, Action? completed)
        {
            var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(_riseMs)) { EasingFunction = ease };
            if (completed != null)
            {
                animation.Completed += (_, __) => completed();
            }
            CharacterShift.BeginAnimation(TranslateTransform.YProperty, animation);
        }

        private void AnimateBubble(double to, int ms, TimeSpan delay, Action? completed)
        {
            var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { BeginTime = delay };
            if (completed != null)
            {
                animation.Completed += (_, __) => completed();
            }
            BubbleLayer.BeginAnimation(OpacityProperty, animation);
        }

        /// <summary>絵クリック: 引っ込む。</summary>
        private void OnCharacterClick(object sender, MouseButtonEventArgs e)
        {
            Retract();
        }

        /// <summary>
        /// 吹き出しクリック: 引っ込めてから Notice.Open。Open が無いお知らせ(あいさつ・まとめ等)は引っ込むだけ
        /// (仕様書では「何もしない」だったが、クリックで閉じられるよう変更)。
        /// </summary>
        private void OnBubbleClick(object sender, MouseButtonEventArgs e)
        {
            // × は吹き出しの中にあり、MouseLeftButtonUp は要素ごとに発生するため × で Handled にしてもここへ届く。
            // × からのクリック(と、× で既に吹き出しを消した後のクリック)は無視して、秘書さんを残す
            if (ReferenceEquals(e.OriginalSource, CloseMark) || !BubbleLayer.IsHitTestVisible)
            {
                return;
            }
            Notice? notice = CurrentNotice;
            if (notice == null || IsPeeking)
            {
                return;
            }
            if (notice.Open == null)
            {
                Retract();
                return;
            }
            Retract(() => BubbleOpened?.Invoke(notice));
        }

        /// <summary>×クリック: 吹き出しだけ消し(クリック不可に)、秘書さんは残す。そこから隠れるまでの時間を数え直す。</summary>
        private void OnCloseClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            BubbleLayer.IsHitTestVisible = false;
            _mouseOverBubble = false;
            AnimateBubble(0, _bubbleFadeOutMs, TimeSpan.Zero, null);
            RestartAutoHide();
        }

        private void OnBubbleMouseEnter(object sender, MouseEventArgs e)
        {
            _mouseOverBubble = true;
            _autoHideTimer.Stop();
        }

        private void OnBubbleMouseLeave(object sender, MouseEventArgs e)
        {
            // 離れたら数え直し
            _mouseOverBubble = false;
            RestartAutoHide();
        }
    }

    /// <summary>配置計算の結果(単位 DIP、窓の左上が原点)。</summary>
    internal readonly record struct BubbleLayoutResult(
        double WindowWidth,
        double WindowHeight,
        double ImageLeft,
        double ImageTop,
        double ImageWidth,
        double ImageHeight,
        double BubbleTop,
        double TailTip,
        double Overlap);

    /// <summary>
    /// 配置計算。
    /// h = DisplayHeight×scale、吹き出しを幅300で Measure → bh、wh = max(h, bh+8)、imageTop = wh−h、faceY = imageTop + 0.23h、
    /// bubbleTop = clamp(faceY−36, 4, wh−bh−4)、tailTip = clamp(faceY, bubbleTop+22, bubbleTop+bh−22)、
    /// 重なり率 r = skin.BubbleOverlap ?? min(0.5, L(吹き出しの行＋影) − (しっぽの張り出し14 + すき間6)/絵幅)、
    /// BubbleOverlap が無いときは、しっぽが faceY を指せる範囲(faceY−bh+22 〜 faceY−22)で bubbleTop をずらし、r が最大になる高さを使う。
    /// 吹き出しが絵の上端より上にはみ出す高さを選んだら、窓をその分(＋4)上へ伸ばす(窓は下端固定なのでキャラは動かない)。
    /// L(行範囲) = その行範囲で最も左にある不透明画素(α&gt;32)の x / 画像幅、窓の高さ = wh、窓の幅 = 306 − 絵幅×r + 絵幅。
    /// 仕様書の式は吹き出しをキャラに少し潜り込ませるものだったが、吹き出しとイラストは重ねない方針のため、
    /// 吹き出しの高さにあたる行では、キャラの左端がしっぽの先よりさらに右(すき間6)に来るよう r を決める。r は負にもなる。
    /// </summary>
    internal static class BubbleLayout
    {
        public const double BubbleLeft = 6;
        public const double BubbleWidth = 300;
        public const double TailWidth = 16;
        public const double TailHalfHeight = 10;
        public const double TailOverlap = 2;
        public const double RightMargin = 12;
        public const double FaceRatio = 0.23;

        /// <summary>しっぽの先とキャラの間にあけるすき間</summary>
        public const double NoOverlapGap = 6;

        /// <summary>吹き出しの影(DropShadow)が下へはみ出す分。この行までキャラと重ねない</summary>
        public const double ShadowBelow = 8;

        public static BubbleLayoutResult Compute(double displayHeight, double scale, double bubbleHeight, int pixelWidth, int pixelHeight, IReadOnlyList<int> leftmostOpaque, double? bubbleOverlap)
        {
            double h = displayHeight * scale;
            double imageWidth = pixelHeight > 0 ? h * pixelWidth / pixelHeight : h;
            double bh = bubbleHeight;
            double wh = Math.Max(h, bh + 8);
            double imageTop = wh - h;
            double faceY = imageTop + FaceRatio * h;
            double bubbleTop = Clamp(faceY - 36, 4, wh - bh - 4);

            double r;
            if (bubbleOverlap != null)
            {
                r = bubbleOverlap.Value;
            }
            else
            {
                // 吹き出し(枠・影を含む高さ)の行で最も左にある不透明画素が、しっぽの先＋すき間より右に来る位置。
                // 肩や広がった髪が吹き出しの行に入ると絵が右へ押し出されるので、しっぽが顔を指せる範囲で
                // 吹き出しを上下にずらし、いちばん顔に近づける高さを選ぶ(同じなら既定の高さに近い方)
                double gap = (TailWidth - TailOverlap + NoOverlapGap) / imageWidth;
                double bubbleRows = Math.Min(0.5 + gap, LeftmostRatio(bubbleTop, bubbleTop + bh + ShadowBelow, imageTop, h, pixelWidth, pixelHeight, leftmostOpaque));
                // 以下は絵の上端を0とした座標。上へのはみ出し(負)も候補にする
                double faceInImage = FaceRatio * h;
                double defaultTop = bubbleTop - imageTop;
                double bestTop = defaultTop;
                double highest = Math.Min(h - bh - 4, faceInImage - 22);
                for (double top = Math.Ceiling(faceInImage - bh + 22); top <= highest; top++)
                {
                    double rows = Math.Min(0.5 + gap, LeftmostRatio(top, top + bh + ShadowBelow, 0, h, pixelWidth, pixelHeight, leftmostOpaque));
                    if (rows > bubbleRows || (rows == bubbleRows && Math.Abs(top - defaultTop) < Math.Abs(bestTop - defaultTop)))
                    {
                        bubbleRows = rows;
                        bestTop = top;
                    }
                }
                if (bestTop != defaultTop)
                {
                    wh = h + Math.Max(0, 4 - bestTop);
                    imageTop = wh - h;
                    faceY = imageTop + faceInImage;
                    bubbleTop = imageTop + bestTop;
                }
                r = bubbleRows - gap;
            }
            double tailTip = Clamp(faceY, bubbleTop + 22, bubbleTop + bh - 22);

            double bubbleRight = BubbleLeft + BubbleWidth;
            double imageLeft = bubbleRight - imageWidth * r;
            double windowWidth = imageLeft + imageWidth;
            return new BubbleLayoutResult(windowWidth, wh, imageLeft, imageTop, imageWidth, h, bubbleTop, tailTip, r);
        }

        /// <summary>窓座標 y0〜y1 の行範囲で、最も左にある不透明画素の x / 画像幅。範囲に不透明画素が無ければ 1。</summary>
        private static double LeftmostRatio(double y0, double y1, double imageTop, double imageHeight, int pixelWidth, int pixelHeight, IReadOnlyList<int> leftmostOpaque)
        {
            if (pixelWidth <= 0 || pixelHeight <= 0 || leftmostOpaque.Count == 0)
            {
                return 1.0;
            }
            double toPixel = pixelHeight / imageHeight;
            int from = (int)Math.Floor((y0 - imageTop) * toPixel);
            int to = (int)Math.Ceiling((y1 - imageTop) * toPixel);
            from = Math.Max(0, from);
            to = Math.Min(Math.Min(pixelHeight, leftmostOpaque.Count) - 1, to);
            int min = pixelWidth;
            for (int y = from; y <= to; y++)
            {
                min = Math.Min(min, leftmostOpaque[y]);
            }
            return (double)min / pixelWidth;
        }

        /// <summary>上下限が逆転したら(吹き出しが小さすぎる等)中間を取る。</summary>
        private static double Clamp(double value, double min, double max)
        {
            if (max < min)
            {
                return (min + max) / 2;
            }
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
