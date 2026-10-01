using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;

[assembly: AssemblyTitle("EventRecorderForWpf")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("EventRecorderForWpf")]
[assembly: AssemblyCopyright("Copyright ©  2026")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]

// テストプロジェクトから internal クラス(EventRow / MainWindowのコントロール等)を直接参照できるようにする
[assembly: InternalsVisibleTo("EventRecorderForWpf.Tests")]

[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

[assembly: Guid("e814aa73-6bcd-4e50-a960-fd4361a3df35")]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

// WinForms版EventRecorder(DPI非対応)と同じ座標系で動かすため、WPFの既定のDPI対応を切る(CheetosForWpfと同じ方針)。
// 記録する座標(低レベルマウスフックのX/Y)・再生時のSendInputの絶対座標換算(Screen.PrimaryScreen.Bounds)・
// 再生後に戻すCursor.Position・画面右上のマウス座標表示が、表示倍率100%以外のモニタでも
// WinForms版と同じ値になる(=WinForms版で記録したマクロをそのまま再生できる)
[assembly: System.Windows.Media.DisableDpiAwareness]
