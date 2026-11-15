using System;
using BannerlordAI.Core.Model;

namespace BannerlordAI.Core.Planning;

/// <summary>
/// A fully resolved order for one own formation: concrete target formation id and world position. This is the only
/// thing the game adapter has to understand.
/// </summary>
public sealed class FormationCommand
{
    public string FormationId { get; init; } = "";
    public DirectiveAction Action { get; init; }
    /// <summary>Concrete formation id (enemy for attack-type actions, friendly for protect/screen/follow).</summary>
    public string? TargetFormationId { get; init; }
    /// <summary>
    /// World position: where to stand for hold/reserve/retreat/follow/screen and advance-to-point; the approach
    /// waypoint for flank. Null when the action needs none (charge, skirmish, protect_flank, advance-on-target).
    /// </summary>
    public Vec2? Position { get; init; }
    public FlankSide? Side { get; init; }
    public Arrangement? Arrangement { get; init; }
    public FireOrder? Fire { get; init; }
    public string Source { get; init; } = "plan";

    /// <summary>True if <paramref name="next"/> should be re-sent to the game instead of keeping this one.</summary>
    public bool DiffersMeaningfully(FormationCommand next, float positionTolerance = 8f)
    {
        if (Action != next.Action) return true;
        if (!string.Equals(TargetFormationId, next.TargetFormationId, StringComparison.OrdinalIgnoreCase)) return true;
        if (Side != next.Side || Arrangement != next.Arrangement || Fire != next.Fire) return true;
        if (Position.HasValue != next.Position.HasValue) return true;
        if (Position.HasValue && next.Position.HasValue && Vec2.Distance(Position.Value, next.Position.Value) > positionTolerance) return true;
        return false;
    }

    public override string ToString()
    {
        var s = FormationId + ": " + Wire.ToWire(Action);
        if (TargetFormationId != null) s += " -> " + TargetFormationId;
        if (Side.HasValue) s += " (" + Wire.ToWire(Side.Value) + ")";
        if (Position.HasValue) s += " @" + Position.Value;
        if (Arrangement.HasValue) s += " [" + Wire.ToWire(Arrangement.Value) + "]";
        if (Fire.HasValue) s += " fire:" + Wire.ToWire(Fire.Value);
        return s;
    }
}
