namespace FihLens.Windows.Shell;

internal static class BackgroundShellProtocol
{
    internal const uint IconId = 1;
    internal const uint ContextMenu = 0x007B;
    internal const uint Select = 0x0400;
    internal const uint KeySelect = 0x0401;

    internal readonly record struct Callback(uint EventCode, int X, int Y);

    internal static bool TryDecodeVersion4(nuint coordinates, nint callbackData, out Callback callback)
    {
        uint data = unchecked((uint)callbackData);
        uint iconId = data >> 16;
        uint eventCode = data & 0xFFFF;
        uint packedCoordinates = unchecked((uint)coordinates);

        callback = new Callback(
            eventCode,
            unchecked((short)packedCoordinates),
            unchecked((short)(packedCoordinates >> 16)));

        return iconId == IconId && eventCode is ContextMenu or Select or KeySelect;
    }
}
