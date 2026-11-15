using System;
using System.Globalization;
using System.Text;

namespace BannerlordAI.Core.Model;

/// <summary>Conversions between enum values and the snake_case strings used in prompts and JSON.</summary>
public static class Wire
{
    /// <summary>Lower-case, trims, and turns spaces/hyphens into underscores. "Shield Wall" -> "shield_wall".</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sb = new StringBuilder(text!.Length);
        foreach (var c in text.Trim())
        {
            if (c == ' ' || c == '-') sb.Append('_');
            else sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public static string F(double value, string format = "0") => value.ToString(format, CultureInfo.InvariantCulture);

    public static string Clock(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var total = (int)Math.Round(seconds);
        return (total / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    public static string ToWire(DirectiveAction a) => a switch
    {
        DirectiveAction.Hold => "hold",
        DirectiveAction.Advance => "advance",
        DirectiveAction.Charge => "charge",
        DirectiveAction.Skirmish => "skirmish",
        DirectiveAction.Flank => "flank",
        DirectiveAction.ProtectFlank => "protect_flank",
        DirectiveAction.Screen => "screen",
        DirectiveAction.Reserve => "reserve",
        DirectiveAction.Retreat => "retreat",
        DirectiveAction.Follow => "follow",
        _ => "hold",
    };

    public static bool TryParseAction(string? s, out DirectiveAction a)
    {
        switch (Normalize(s))
        {
            case "hold": case "defend": case "hold_position": a = DirectiveAction.Hold; return true;
            case "advance": case "move": a = DirectiveAction.Advance; return true;
            case "charge": case "attack": a = DirectiveAction.Charge; return true;
            case "skirmish": a = DirectiveAction.Skirmish; return true;
            case "flank": a = DirectiveAction.Flank; return true;
            case "protect_flank": a = DirectiveAction.ProtectFlank; return true;
            case "screen": a = DirectiveAction.Screen; return true;
            case "reserve": a = DirectiveAction.Reserve; return true;
            case "retreat": case "fall_back": a = DirectiveAction.Retreat; return true;
            case "follow": a = DirectiveAction.Follow; return true;
            default: a = DirectiveAction.Hold; return false;
        }
    }

    public static string ToWire(Arrangement a) => a switch
    {
        Arrangement.Line => "line",
        Arrangement.ShieldWall => "shieldwall",
        Arrangement.Loose => "loose",
        Arrangement.Square => "square",
        Arrangement.Circle => "circle",
        Arrangement.Column => "column",
        Arrangement.Scatter => "scatter",
        _ => "line",
    };

    public static bool TryParseArrangement(string? s, out Arrangement a)
    {
        switch (Normalize(s))
        {
            case "line": a = Arrangement.Line; return true;
            case "shieldwall": case "shield_wall": a = Arrangement.ShieldWall; return true;
            case "loose": a = Arrangement.Loose; return true;
            case "square": a = Arrangement.Square; return true;
            case "circle": a = Arrangement.Circle; return true;
            case "column": a = Arrangement.Column; return true;
            case "scatter": a = Arrangement.Scatter; return true;
            default: a = Arrangement.Line; return false;
        }
    }

    public static string ToWire(FireOrder f) => f == FireOrder.Hold ? "hold" : "at_will";

    public static bool TryParseFire(string? s, out FireOrder f)
    {
        switch (Normalize(s))
        {
            case "at_will": case "fire_at_will": case "fire": f = FireOrder.AtWill; return true;
            case "hold": case "hold_fire": f = FireOrder.Hold; return true;
            default: f = FireOrder.AtWill; return false;
        }
    }

    public static string ToWire(FlankSide s) => s == FlankSide.Left ? "left" : "right";

    public static bool TryParseSide(string? s, out FlankSide side)
    {
        switch (Normalize(s))
        {
            case "left": side = FlankSide.Left; return true;
            case "right": side = FlankSide.Right; return true;
            default: side = FlankSide.Left; return false;
        }
    }

    public static string ToWire(Stance s) => s switch
    {
        Stance.Aggressive => "aggressive",
        Stance.Balanced => "balanced",
        Stance.Defensive => "defensive",
        Stance.Delaying => "delaying",
        Stance.Withdraw => "withdraw",
        _ => "balanced",
    };

    public static bool TryParseStance(string? s, out Stance stance)
    {
        switch (Normalize(s))
        {
            case "aggressive": stance = Stance.Aggressive; return true;
            case "balanced": stance = Stance.Balanced; return true;
            case "defensive": stance = Stance.Defensive; return true;
            case "delaying": stance = Stance.Delaying; return true;
            case "withdraw": stance = Stance.Withdraw; return true;
            default: stance = Stance.Balanced; return false;
        }
    }

    public static string ToWire(ConditionType c) => c switch
    {
        ConditionType.EnemyWithin => "enemy_within",
        ConditionType.StrengthBelow => "strength_below",
        ConditionType.Engaged => "engaged",
        ConditionType.Routing => "routing",
        ConditionType.TimeElapsed => "time_elapsed",
        ConditionType.StrengthRatioBelow => "strength_ratio_below",
        _ => "time_elapsed",
    };

    public static bool TryParseCondition(string? s, out ConditionType c)
    {
        switch (Normalize(s))
        {
            case "enemy_within": c = ConditionType.EnemyWithin; return true;
            case "strength_below": c = ConditionType.StrengthBelow; return true;
            case "engaged": c = ConditionType.Engaged; return true;
            case "routing": c = ConditionType.Routing; return true;
            case "time_elapsed": c = ConditionType.TimeElapsed; return true;
            case "strength_ratio_below": c = ConditionType.StrengthRatioBelow; return true;
            default: c = ConditionType.TimeElapsed; return false;
        }
    }
}
