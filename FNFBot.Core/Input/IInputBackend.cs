namespace FNFBot.Core.Input
{
    /// <summary>
    /// Sends synthetic arrow-key presses to whatever window currently has focus.
    /// Directions: 0 = Left, 1 = Down, 2 = Up, 3 = Right.
    /// </summary>
    public interface IInputBackend
    {
        void KeyDown(int direction);
        void KeyUp(int direction);
    }
}
