using System.Windows;

namespace Cheetos
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
