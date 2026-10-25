using System.Reflection;
using System.Runtime.InteropServices;
using FihLens.Windows.Capture;

// Linked native capture sources are Windows-only; the suite runs on Windows CI.
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

namespace FihLens.Tests.Windows;

public sealed class UiAutomationInteropTests
{
    // IIDs from the Windows SDK UIAutomationClient.h; a wrong IID makes QueryInterface fail at runtime.
    [Theory]
    [InlineData("IUIAutomation2", "34723aff-0c9d-49d0-9896-7ab52df8cd8a")]
    [InlineData("IUIAutomationElement", "d22108aa-8ac5-49a5-837b-37bbb3d7591e")]
    [InlineData("IUIAutomationTreeWalker", "4042c624-389c-4afc-a630-9df854a541fc")]
    [InlineData("IUIAutomationTextPattern", "32eba289-3583-42c9-9c59-3b6d9a1e9b6a")]
    [InlineData("IUIAutomationTextRangeArray", "ce4ae76a-e717-4c98-81ea-47371d028eb6")]
    [InlineData("IUIAutomationTextRange", "a543cc6a-f4ae-494b-8239-c814481187a8")]
    public void ComInterfaceIdMatchesSdk(string name, string iid)
    {
        var type = typeof(SelectedTextSource).Assembly
            .GetType($"FihLens.Windows.Capture.UiAutomationInterop+{name}", throwOnError: true)!;

        Assert.Equal(Guid.Parse(iid), type.GetCustomAttribute<GuidAttribute>() is { } guid ? Guid.Parse(guid.Value) : Guid.Empty);
    }
}
