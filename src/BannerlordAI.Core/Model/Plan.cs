using System.Collections.Generic;

namespace BannerlordAI.Core.Model;

/// <summary>
/// A position for a directive. Absolute anchors are stored in world space (converted from the model's local frame at
/// receipt, so they stay put as armies move). Relative anchors are a local-axis offset from a formation, re-resolved
/// every tick with the current frame's axes.
/// </summary>
public sealed class Anchor
{
    private Anchor(bool isRelative, Vec2 value, string? relativeTo)
    {
        IsRelative = isRelative;
        Value = value;
        RelativeTo = relativeTo;
    }

    public bool IsRelative { get; }

    /// <summary>World point when absolute; local-axis offset when relative.</summary>
    public Vec2 Value { get; }

    public string? RelativeTo { get; }

    public static Anchor AbsoluteWorld(Vec2 world) => new(false, world, null);

    public static Anchor Relative(string formationId, Vec2 localOffset) => new(true, localOffset, formationId);
}

public sealed class Directive
{
    public string FormationId { get; init; } = "";
    public DirectiveAction Action { get; init; }
    /// <summary>Formation id or selector (e.g. "E2", "enemy:cavalry", "own:infantry"), or null for the default.</summary>
    public string? Target { get; init; }
    public FlankSide? Side { get; init; }
    public Anchor? Anchor { get; init; }
    public Arrangement? Arrangement { get; init; }
    public FireOrder? Fire { get; init; }
    /// <summary>Where this directive came from, for logs: "plan", "contingency C1", "lieutenant".</summary>
    public string Source { get; init; } = "plan";

    public Directive With(
        DirectiveAction? action = null,
        string? target = null,
        bool clearTarget = false,
        FlankSide? side = null,
        Anchor? anchor = null,
        bool clearAnchor = false,
        Arrangement? arrangement = null,
        FireOrder? fire = null,
        string? source = null) => new()
    {
        FormationId = FormationId,
        Action = action ?? Action,
        Target = clearTarget ? null : target ?? Target,
        Side = side ?? Side,
        Anchor = clearAnchor ? null : anchor ?? Anchor,
        Arrangement = arrangement ?? Arrangement,
        Fire = fire ?? Fire,
        Source = source ?? Source,
    };
}

public sealed class Condition
{
    public ConditionType Type { get; init; }
    public string? Subject { get; init; }
    public string? Other { get; init; }
    public double Value { get; init; }
}

public sealed class Contingency
{
    public string Id { get; init; } = "";
    public string Description { get; init; } = "";
    public bool MatchAll { get; init; } = true;
    public IReadOnlyList<Condition> Conditions { get; init; } = new List<Condition>();
    public IReadOnlyList<Directive> Directives { get; init; } = new List<Directive>();

    /// <summary>Mission time it fired, or null if still armed. Each contingency fires at most once.</summary>
    public double? FiredAt { get; set; }

    public bool IsArmed => FiredAt == null;
}

public sealed class Plan
{
    /// <summary>Increments every time a Commander plan is installed.</summary>
    public int Version { get; init; }

    public double IssuedAt { get; init; }

    public long BasisSequence { get; init; }

    public string Intent { get; init; } = "";

    public Stance Stance { get; init; } = Stance.Balanced;

    /// <summary>Current directive per own formation id (mutated by contingencies and Lieutenant adjustments).</summary>
    public Dictionary<string, Directive> Directives { get; init; } = new(System.StringComparer.OrdinalIgnoreCase);

    public List<Contingency> Contingencies { get; init; } = new();

    public List<string> WakeCommanderIf { get; init; } = new();

    public string OrderLog { get; init; } = "";
}
