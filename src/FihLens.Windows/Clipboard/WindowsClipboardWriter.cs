using FihLens.Application.Clipboard;
using Windows.ApplicationModel.DataTransfer;

namespace FihLens.Windows.Clipboard;

public sealed class WindowsClipboardWriter : IClipboardWriter
{
    // The composition root calls this on its UI thread; no dispatcher hop can outlive the request.
    public Task WriteTextAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        var content = new DataPackage();
        content.SetText(text);
        cancellationToken.ThrowIfCancellationRequested();
        global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(content);
        global::Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
        return Task.CompletedTask;
    }
}
