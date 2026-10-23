namespace HappyCheats.App;

/// <summary>Consume held keys even outside the game so focus changes cannot open a menu.</summary>
internal sealed class GameMenuShortcut
{
    internal const int VirtualKey = 0x2E; // VK_DELETE
    private bool deleteDown, f6Down;

    internal bool Poll(bool control, bool delete, bool f6, bool allowed, bool dawnwalker)
    {
        var pressed = control && delete && !deleteDown || dawnwalker && f6 && !f6Down;
        deleteDown = delete;
        f6Down = f6;
        return allowed && pressed;
    }
}
