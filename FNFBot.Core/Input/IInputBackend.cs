namespace FNFBot.Core.Input
{
    /// <summary>
    /// Sends synthetic arrow-key presses to whatever window currently has focus.
    /// Directions: 0 = Left, 1 = Down, 2 = Up, 3 = Right.
    /// Implemented per-OS (Windows = SendInput, Linux = uinput, macOS = CGEvent).
    /// </summary>
    public interface IInputBackend
    {
        void KeyDown(int direction);
        void KeyUp(int direction);
    }
}
