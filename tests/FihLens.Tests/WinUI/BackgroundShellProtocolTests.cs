using FihLens.Windows.Shell;

namespace FihLens.Tests.WinUI;

public sealed class BackgroundShellProtocolTests
{
    [Theory]
    [InlineData(BackgroundShellProtocol.Select)]
    [InlineData(BackgroundShellProtocol.KeySelect)]
    public void DecodesVersion4MouseAndKeyboardActivation(uint eventCode)
    {
        nuint coordinates = PackCoordinates(123, 456);
        nint callbackData = PackCallback(eventCode, BackgroundShellProtocol.IconId);

        Assert.True(BackgroundShellProtocol.TryDecodeVersion4(coordinates, callbackData, out var callback));
        Assert.Equal(eventCode, callback.EventCode);
        Assert.Equal(123, callback.X);
        Assert.Equal(456, callback.Y);
    }

    [Fact]
    public void DecodesContextMenuWithNegativeScreenCoordinates()
    {
        Assert.True(BackgroundShellProtocol.TryDecodeVersion4(
            PackCoordinates(-24, -360),
            PackCallback(BackgroundShellProtocol.ContextMenu, BackgroundShellProtocol.IconId),
            out var callback));

        Assert.Equal(BackgroundShellProtocol.ContextMenu, callback.EventCode);
        Assert.Equal(-24, callback.X);
        Assert.Equal(-360, callback.Y);
    }

    [Theory]
    [InlineData(2, BackgroundShellProtocol.Select)]
    [InlineData(1, 0x0555)]
    public void RejectsUnknownIconIdsAndEvents(uint iconId, uint eventCode)
    {
        Assert.False(BackgroundShellProtocol.TryDecodeVersion4(
            PackCoordinates(10, 20),
            PackCallback(eventCode, iconId),
            out _));
    }

    private static nuint PackCoordinates(int x, int y) => unchecked((nuint)(
        (uint)(ushort)(short)x | ((uint)(ushort)(short)y << 16)));

    private static nint PackCallback(uint eventCode, uint iconId) => unchecked((nint)(
        ((iconId & 0xFFFF) << 16) | (eventCode & 0xFFFF)));
}
