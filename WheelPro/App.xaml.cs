using System.Windows;
using Velopack;

namespace WheelPro;

public sealed class App : Application
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();
        var app = new App();
        app.Run(new MainWindow());
    }
}
