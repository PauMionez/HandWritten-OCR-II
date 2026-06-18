using HandWritten_OCR.Services;
using System.Windows;

namespace HandWritten_OCR;

public partial class App : Application
{
    public static KrakenServerManager KrakenServerManager { get; } = new KrakenServerManager();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _ = KrakenServerManager.StartAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        KrakenServerManager.Stop();
        base.OnExit(e);
    }
}
