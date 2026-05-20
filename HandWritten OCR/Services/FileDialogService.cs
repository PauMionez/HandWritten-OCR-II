using Microsoft.Win32;

namespace HandWritten_OCR.Services;

public sealed class FileDialogService : IFileDialogService
{
    public string? OpenImageFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Image",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.gif|All files (*.*)|*.*"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
