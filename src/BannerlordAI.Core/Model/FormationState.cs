namespace BannerlordAI.Core.Model;

/// <summary>Immutable per-tick state of one formation. Positions are world coordinates (metres).</summary>
public sealed class FormationState
{
    /// <summary>Stable id: F# own, A# ally, E# enemy. Assigned by the game adapter.</summary>
    public string Id { get; init; } = "";

    public Allegiance Allegiance { get; init; }

    public FormationRole Role { get; init; }

    /// <summary>The game's own name for the formation slot (e.g. "Infantry", "HeavyCavalry").</summary>
    public string Label { get; init; } = "";

    public int Count { get; init; }

    public int InitialCount { get; init; }

    public Vec2 Position { get; init; }

    /// <summary>Unit facing direction in world space.</summary>
    public Vec2 Facing { get; init; } = new Vec2(0f, 1f);

    /// <summary>Average health fraction 0..1.</summary>
    public float AverageHealth { get; init; } = 1f;

    /// <summary>Average morale 0..1, or null if unknown.</summary>
    public float? Morale { get; init; }

    /// <summary>Remaining missile ammo fraction 0..1 for ranged formations, null otherwise.</summary>
    public float? Ammo { get; init; }

    public bool IsEngaged { get; init; }

    public bool IsRouting { get; init; }

    /// <summary>Short description of what the game says the formation is doing (behavior / order), if known.</summary>
    public string? Activity { get; init; }

    public bool IsAlive => Count > 0;

    /// <summary>Fraction of starting men still present, 0..1.</summary>
    public float Strength => InitialCount > 0 ? (float)Count / InitialCount : 0f;

    public string RoleName => Roles.ToWire(Role);
}

public static class Roles
{
    public static string ToWire(FormationRole role) => role switch
    {
        FormationRole.Infantry => "infantry",
        FormationRole.Ranged => "ranged",
        FormationRole.Cavalry => "cavalry",
        FormationRole.HorseArcher => "horse_archer",
        FormationRole.Skirmisher => "skirmisher",
        _ => "infantry",
    };

    public static bool TryParse(string? text, out FormationRole role)
    {
        switch (Wire.Normalize(text))
        {
            case "infantry": role = FormationRole.Infantry; return true;
            case "ranged":
            case "archer":
            case "archers": role = FormationRole.Ranged; return true;
            case "cavalry": role = FormationRole.Cavalry; return true;
            case "horse_archer":
            case "horse_archers": role = FormationRole.HorseArcher; return true;
            case "skirmisher":
            case "skirmishers": role = FormationRole.Skirmisher; return true;
            default: role = FormationRole.Infantry; return false;
        }
    }

    public static bool IsMounted(FormationRole role) => role == FormationRole.Cavalry || role == FormationRole.HorseArcher;

    public static bool IsMissile(FormationRole role) =>
        role == FormationRole.Ranged || role == FormationRole.HorseArcher || role == FormationRole.Skirmisher;
}
