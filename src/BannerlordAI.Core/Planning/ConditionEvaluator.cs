using System.Linq;
using BannerlordAI.Core.Model;

namespace BannerlordAI.Core.Planning;

/// <summary>Evaluates contingency conditions against a snapshot. Pure and cheap; runs every brain tick.</summary>
public static class ConditionEvaluator
{
    public static bool IsMet(Contingency c, BattleSnapshot snap, double planIssuedAt)
    {
        if (c.Conditions.Count == 0) return false;
        return c.MatchAll
            ? c.Conditions.All(x => IsMet(x, snap, planIssuedAt))
            : c.Conditions.Any(x => IsMet(x, snap, planIssuedAt));
    }

    public static bool IsMet(Condition c, BattleSnapshot snap, double planIssuedAt)
    {
        switch (c.Type)
        {
            case ConditionType.EnemyWithin:
            {
                var subjects = Selectors.SelectAll(c.Subject, snap);
                var others = Selectors.SelectAll(string.IsNullOrEmpty(c.Other) ? "enemy:any" : c.Other, snap);
                foreach (var s in subjects)
                    foreach (var o in others)
                        if (o.Allegiance == Allegiance.Enemy && Vec2.Distance(s.Position, o.Position) <= c.Value)
                            return true;
                return false;
            }
            case ConditionType.StrengthBelow:
            {
                var matched = Selectors.SelectAll(c.Subject, snap, includeDead: true);
                var initial = matched.Sum(f => f.InitialCount);
                var now = matched.Sum(f => f.Count);
                // No such formations (or all gone) counts as zero strength.
                var pct = initial > 0 ? 100.0 * now / initial : 0.0;
                return pct < c.Value;
            }
            case ConditionType.Engaged:
                return Selectors.SelectAll(c.Subject, snap).Any(f => f.IsEngaged);
            case ConditionType.Routing:
                return Selectors.SelectAll(c.Subject, snap).Any(f => f.IsRouting);
            case ConditionType.TimeElapsed:
                return snap.Time - planIssuedAt >= c.Value;
            case ConditionType.StrengthRatioBelow:
            {
                var enemy = snap.EnemyMen;
                if (enemy == 0) return false;
                return (double)(snap.OwnMen + snap.AllyMen) / enemy < c.Value;
            }
            default:
                return false;
        }
    }
}
