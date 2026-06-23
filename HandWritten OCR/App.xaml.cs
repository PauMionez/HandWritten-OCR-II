using HandWritten_OCR.Services;
using System.Windows;

namespace HandWritten_OCR;

public partial class App : Application
{
    public static KrakenServerManager KrakenServerManager { get; } = new KrakenServerManager();
    public static PaddleServerManager PaddleServerManager { get; } = new PaddleServerManager();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _ = KrakenServerManager.StartAsync();
        // PaddleOCR starts on-demand when user selects it (see MainViewModel)
    }

    protected override void OnExit(ExitEventArgs e)
    {
        KrakenServerManager.Stop();
        PaddleServerManager.Stop();
        base.OnExit(e);
    }
}
