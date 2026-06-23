using HandWritten_OCR.Services;
using System.Windows;

namespace HandWritten_OCR;

public partial class App : Application
{
    public static PaddleServerManager PaddleServerManager { get; } = new PaddleServerManager();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        PaddleServerManager.Stop();
        base.OnExit(e);
    }
}
