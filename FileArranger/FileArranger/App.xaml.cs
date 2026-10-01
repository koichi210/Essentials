using System.Windows;

namespace FileArranger
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            StcWpfExceptionGuard.Install(this);
            base.OnStartup(e);
        }
    }
}
