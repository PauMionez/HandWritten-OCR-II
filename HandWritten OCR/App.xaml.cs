using HandWritten_OCR.Services;
using HandWritten_OCR.ViewModels;
using System.IO;
using System.Windows;

namespace HandWritten_OCR;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var modelFolder = Path.Combine(AppContext.BaseDirectory, "models");
        var ocrService = new TrOcrService();
        var fileDialogService = new FileDialogService();
        var viewModel = new MainViewModel(ocrService, fileDialogService, modelFolder);

        new MainWindow { DataContext = viewModel }.Show();
    }
}
