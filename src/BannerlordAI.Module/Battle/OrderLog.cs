using TaleWorlds.Library;

namespace BannerlordAI.Module.Battle;

/// <summary>On-screen messages. Main thread only.</summary>
internal static class OrderLog
{
    public static readonly Color Friendly = Color.FromUint(0xFF8FD3FFu);
    public static readonly Color Enemy = Color.FromUint(0xFFFF9C8Fu);
    public static readonly Color Warning = Color.FromUint(0xFFFFD27Fu);

    public static void Show(string text, Color color)
    {
        try
        {
            InformationManager.DisplayMessage(new InformationMessage(text, color));
        }
        catch
        {
            // UI may not be available (e.g. during mission teardown).
        }
    }
}
