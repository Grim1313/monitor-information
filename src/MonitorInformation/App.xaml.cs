using System.Windows;

namespace MonitorInformation;

public partial class App
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        window.Show();
    }
}
