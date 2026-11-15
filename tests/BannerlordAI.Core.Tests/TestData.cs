using System.Text.Json;
using BannerlordAI.Core.Model;

namespace BannerlordAI.Core.Tests;

/// <summary>Builders and canned model replies shared by all test files.</summary>
public static class TestData
{
    public static FormationState Form(
        string id,
        FormationRole role,
        int count,
        float x,
        float y,
        Allegiance? allegiance = null,
        int? initial = null,
        bool engaged = false,
        bool routing = false,
        Vec2? facing = null,
        float? morale = null,
        float? ammo = null,
        string? activity = null)
    {
        var side = allegiance ?? (id.StartsWith("E") ? Allegiance.Enemy : id.StartsWith("A") ? Allegiance.Ally : Allegiance.Own);
        return new FormationState
        {
            Id = id,
            Allegiance = side,
            Role = role,
            Label = role.ToString(),
            Count = count,
            InitialCount = initial ?? count,
            Position = new Vec2(x, y),
            Facing = facing ?? new Vec2(0, 1),
            Morale = morale,
            Ammo = ammo,
            IsEngaged = engaged,
            IsRouting = routing,
            Activity = activity,
        };
    }

    /// <summary>Copy of a formation with some fields changed.</summary>
    public static FormationState With(this FormationState f, int? count = null, float? x = null, float? y = null,
        bool? engaged = null, bool? routing = null, int? initial = null)
        => new()
        {
            Id = f.Id, Allegiance = f.Allegiance, Role = f.Role, Label = f.Label,
            Count = count ?? f.Count, InitialCount = initial ?? f.InitialCount,
            Position = new Vec2(x ?? f.Position.X, y ?? f.Position.Y), Facing = f.Facing,
            AverageHealth = f.AverageHealth, Morale = f.Morale, Ammo = f.Ammo,
            IsEngaged = engaged ?? f.IsEngaged, IsRouting = routing ?? f.IsRouting, Activity = f.Activity,
        };

    /// <summary>Own army: F1 infantry 100 (0,0), F2 ranged 50 (0,-30), F3 cavalry 40 (-80,-10).</summary>
    public static List<FormationState> OwnArmy() => new()
    {
        Form("F1", FormationRole.Infantry, 100, 0, 0),
        Form("F2", FormationRole.Ranged, 50, 0, -30),
        Form("F3", FormationRole.Cavalry, 40, -80, -10),
    };

    /// <summary>Enemy army at distance <paramref name="y"/> ahead: E1 infantry, E2 ranged, E3 cavalry.</summary>
    public static List<FormationState> EnemyArmy(float y = 300) => new()
    {
        Form("E1", FormationRole.Infantry, 100, 0, y),
        Form("E2", FormationRole.Ranged, 50, 0, y + 40),
        Form("E3", FormationRole.Cavalry, 40, 120, y),
    };

    /// <summary>Snapshot with an explicit (default identity) frame; no terrain, no bounds.</summary>
    public static BattleSnapshot Snap(
        IReadOnlyList<FormationState>? own = null,
        IReadOnlyList<FormationState>? enemies = null,
        double time = 60,
        long seq = 1,
        BattleFrame? frame = null,
        IReadOnlyList<FormationState>? allies = null,
        BattlePhase phase = BattlePhase.Battle,
        MapBounds? bounds = null)
        => new()
        {
            Sequence = seq,
            Time = time,
            Phase = phase,
            Own = own ?? OwnArmy(),
            Enemies = enemies ?? EnemyArmy(),
            Allies = allies ?? Array.Empty<FormationState>(),
            Frame = frame ?? BattleFrame.Identity,
            Bounds = bounds,
        };

    // ----------------------------------------------------------------- canned JSON

    /// <summary>Valid commander plan for the OwnArmy layout.</summary>
    public const string CommanderJson = """
    {
      "intent": "Hold the line, shoot, then hit with cavalry.",
      "stance": "defensive",
      "directives": [
        {"formation":"F1","action":"hold","target":null,"side":null,"anchor":{"mode":"absolute","x":0,"y":40,"relative_to":null},"arrangement":"shieldwall","fire":null},
        {"formation":"F2","action":"skirmish","target":"enemy:infantry","side":null,"anchor":{"mode":"relative","x":0,"y":-25,"relative_to":"F1"},"arrangement":null,"fire":"at_will"},
        {"formation":"F3","action":"reserve","target":null,"side":null,"anchor":{"mode":"absolute","x":-120,"y":0,"relative_to":null},"arrangement":null,"fire":null}
      ],
      "contingencies": [
        {"id":"C1","description":"Cavalry strikes the archers","match":"all",
         "conditions":[{"type":"engaged","subject":"F1","other":null,"value":null}],
         "directives":[{"formation":"F3","action":"charge","target":"enemy:ranged","side":null,"anchor":null,"arrangement":null,"fire":null}]}
      ],
      "wake_commander_if": ["F1 below 50%"],
      "order_log": "Hold the line!"
    }
    """;

    public static string Commander(string? orderLog = "Hold the line!", string? directivesJson = null, string contingenciesJson = "")
    {
        if (directivesJson == null)
            directivesJson = """{"formation":"F1","action":"hold"},{"formation":"F2","action":"skirmish"},{"formation":"F3","action":"reserve"}""";
        return "{\"intent\":\"test intent\",\"stance\":\"balanced\",\"directives\":[" + directivesJson + "],\"contingencies\":["
               + contingenciesJson + "],\"wake_commander_if\":[],\"order_log\":" + JsonSerializer.Serialize(orderLog) + "}";
    }

    public const string LieutenantNoOp = """{"fire_contingencies":[],"adjustments":[],"wake_commander":false,"reason":"","order_log":null}""";

    public static string Lieutenant(string fire = "", string adjustments = "", bool wake = false, string reason = "", string? orderLog = null)
        => "{\"fire_contingencies\":[" + fire + "],\"adjustments\":[" + adjustments + "],\"wake_commander\":" + (wake ? "true" : "false")
           + ",\"reason\":" + JsonSerializer.Serialize(reason) + ",\"order_log\":" + JsonSerializer.Serialize(orderLog) + "}";
}
