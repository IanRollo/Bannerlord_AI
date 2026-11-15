using System.Linq;
using System.Text;
using BannerlordAI.Core.Model;
using BannerlordAI.Core.Planning;

namespace BannerlordAI.Core.Compaction;

/// <summary>Describes the current plan in the frame of a given snapshot, so models can see what is in force.</summary>
public static class PlanFormatter
{
    public static string Format(Plan? plan, BattleSnapshot s)
    {
        var sb = new StringBuilder();
        if (plan == null)
        {
            sb.AppendLine("CURRENT PLAN: none yet (the game's default AI is in control).");
            return sb.ToString();
        }

        sb.Append("CURRENT PLAN v").Append(plan.Version).Append(" (issued ").Append(Wire.Clock(plan.IssuedAt))
          .Append(", stance ").Append(Wire.ToWire(plan.Stance)).AppendLine(")");
        if (plan.Intent.Length > 0) sb.Append("Intent: ").AppendLine(plan.Intent);

        sb.AppendLine("Standing directives (as currently in force):");
        foreach (var f in s.Own)
        {
            if (!plan.Directives.TryGetValue(f.Id, out var d))
            {
                sb.Append("  ").Append(f.Id).AppendLine(": (no directive)");
                continue;
            }
            sb.Append("  ").AppendLine(Describe(d, s));
        }

        if (plan.Contingencies.Count > 0)
        {
            sb.AppendLine("Contingencies:");
            foreach (var c in plan.Contingencies)
            {
                sb.Append("  ").Append(c.Id).Append(c.IsArmed ? " [armed] " : " [fired " + Wire.Clock(c.FiredAt!.Value) + "] ")
                  .Append(c.Description);
                sb.Append(" | when ").Append(c.MatchAll ? "all" : "any").Append(": ")
                  .Append(string.Join("; ", c.Conditions.Select(DescribeCondition)));
                sb.Append(" | then: ").Append(string.Join("; ", c.Directives.Select(d => Describe(d, s))));
                sb.AppendLine();
            }
        }

        if (plan.WakeCommanderIf.Count > 0)
        {
            sb.AppendLine("Commander wants to be woken if:");
            foreach (var w in plan.WakeCommanderIf) sb.Append("  - ").AppendLine(w);
        }
        return sb.ToString();
    }

    public static string Describe(Directive d, BattleSnapshot s)
    {
        var sb = new StringBuilder();
        sb.Append(d.FormationId).Append(": ").Append(Wire.ToWire(d.Action));
        if (d.Target != null) sb.Append(" target ").Append(d.Target);
        if (d.Side.HasValue) sb.Append(" side ").Append(Wire.ToWire(d.Side.Value));
        if (d.Anchor != null)
        {
            if (d.Anchor.IsRelative)
                sb.Append(" at ").Append(d.Anchor.Value).Append(" from ").Append(d.Anchor.RelativeTo);
            else
                sb.Append(" at ").Append(s.Frame.ToLocal(d.Anchor.Value));
        }
        if (d.Arrangement.HasValue) sb.Append(" [").Append(Wire.ToWire(d.Arrangement.Value)).Append(']');
        if (d.Fire.HasValue) sb.Append(" fire ").Append(Wire.ToWire(d.Fire.Value));
        if (d.Source != "plan") sb.Append(" (from ").Append(d.Source).Append(')');
        return sb.ToString();
    }

    public static string DescribeCondition(Condition c) => c.Type switch
    {
        ConditionType.EnemyWithin => $"{c.Other ?? "enemy:any"} within {Wire.F(c.Value)}m of {c.Subject}",
        ConditionType.StrengthBelow => $"{c.Subject} below {Wire.F(c.Value)}% strength",
        ConditionType.Engaged => $"{c.Subject} engaged",
        ConditionType.Routing => $"{c.Subject} routing",
        ConditionType.TimeElapsed => $"{Wire.F(c.Value)}s after plan",
        ConditionType.StrengthRatioBelow => $"our/enemy men below {Wire.F(c.Value, "0.##")}",
        _ => Wire.ToWire(c.Type),
    };
}
