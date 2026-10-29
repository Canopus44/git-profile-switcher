using Avalonia;
using Velopack;

namespace GitProfileSwitcher.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Debe ser lo primero en Main: vpk lo exige para los hooks de instalacion/desinstalacion.
        VelopackApp.Build().Run();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
