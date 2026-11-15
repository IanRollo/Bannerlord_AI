using CVec2 = BannerlordAI.Core.Model.Vec2;
using TVec2 = TaleWorlds.Library.Vec2;

namespace BannerlordAI.Module.Battle;

internal static class Conv
{
    public static CVec2 ToCore(this TVec2 v) => new(v.x, v.y);

    public static TVec2 ToGame(this CVec2 v) => new(v.X, v.Y);
}
