namespace FihLens.Application.Clipboard;

/// <summary>Writes the explicitly chosen output. Completion means the native write succeeded.</summary>
public interface IClipboardWriter
{
    Task WriteTextAsync(string text, CancellationToken cancellationToken);
}
