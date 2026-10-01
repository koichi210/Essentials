using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;

[assembly: AssemblyTitle("Cheetos")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("Cheetos")]
[assembly: AssemblyCopyright("Copyright ©  2017")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]

// テストプロジェクトから internal クラス（Logic / PicEdit / MainWindowのコントロール等）を直接参照できるようにする
[assembly: InternalsVisibleTo("Cheetos.Tests")]

[assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

[assembly: Guid("a9b79190-e1da-4c75-8c87-9e31f56daade")]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

// WinForms版Cheetos(DPI非対応)と同じ座標系で動かすため、WPFの既定のDPI対応を切る。
// DataGridのMouseX/MouseY(SetCursorPosへ渡す座標)やCursor.Positionの表示が、表示倍率100%以外の
// モニタでもWinForms版で保存したプロファイルと同じ値になる
[assembly: System.Windows.Media.DisableDpiAwareness]
