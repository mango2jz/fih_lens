using System.Runtime.InteropServices;

namespace FihLens.Windows.Startup;

public static class StartupDiagnostic
{
    public static void ShowError(string message, string caption) => MessageBoxW(0, message, caption, 0x00000010);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint window, string text, string caption, uint type);
}
