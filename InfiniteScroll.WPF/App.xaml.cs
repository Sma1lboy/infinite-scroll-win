using System.Windows;

namespace InfiniteScroll;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Force save on exit
        var mainWindow = MainWindow as Views.MainWindow;
        mainWindow?.ViewModel.Save();
        base.OnExit(e);
    }
}
