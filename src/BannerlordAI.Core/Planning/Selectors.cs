using System;
using System.Collections.Generic;
using System.Linq;
using BannerlordAI.Core.Model;

namespace BannerlordAI.Core.Planning;

/// <summary>
/// Formation references used by models: an exact id ("F1", "E3", "A2") or "side:kind" where side is own/ally/enemy
/// and kind is a role (infantry, ranged, cavalry, horse_archer, skirmisher) or any / nearest / largest.
/// </summary>
public static class Selectors
{
    private enum Pick { Any, Nearest, Largest }

    private readonly struct Parsed
    {
        public Parsed(string? exactId, Allegiance side, FormationRole? role, Pick pick)
        {
            ExactId = exactId;
            Side = side;
            Role = role;
            Pick = pick;
        }

        public string? ExactId { get; }
        public Allegiance Side { get; }
        public FormationRole? Role { get; }
        public Pick Pick { get; }
    }

    private static bool TryParse(string? selector, out Parsed parsed)
    {
        parsed = default;
        var s = Wire.Normalize(selector);
        if (s.Length == 0) return false;

        var colon = s.IndexOf(':');
        if (colon < 0)
        {
            // Exact id; "enemy"/"own" alone mean "any".
            switch (s)
            {
                case "enemy": parsed = new Parsed(null, Allegiance.Enemy, null, Pick.Any); return true;
                case "own": case "ours": case "friendly": parsed = new Parsed(null, Allegiance.Own, null, Pick.Any); return true;
                case "ally": parsed = new Parsed(null, Allegiance.Ally, null, Pick.Any); return true;
            }
            parsed = new Parsed(s.ToUpperInvariant(), Allegiance.Own, null, Pick.Any);
            return true;
        }

        var sideText = s.Substring(0, colon);
        var kind = s.Substring(colon + 1);
        Allegiance side;
        switch (sideText)
        {
            case "enemy": case "enemies": side = Allegiance.Enemy; break;
            case "own": case "ours": case "friendly": side = Allegiance.Own; break;
            case "ally": case "allies": side = Allegiance.Ally; break;
            default: return false;
        }

        switch (kind)
        {
            case "any": case "all": case "": parsed = new Parsed(null, side, null, Pick.Any); return true;
            case "nearest": parsed = new Parsed(null, side, null, Pick.Nearest); return true;
            case "largest": case "main": parsed = new Parsed(null, side, null, Pick.Largest); return true;
        }

        if (!Roles.TryParse(kind, out var role)) return false;
        parsed = new Parsed(null, side, role, Pick.Nearest);
        return true;
    }

    /// <summary>True if the selector is well-formed and, for exact ids, names a formation in the snapshot.</summary>
    public static bool IsValid(string? selector, BattleSnapshot snap)
    {
        if (!TryParse(selector, out var p)) return false;
        return p.ExactId == null || snap.Find(p.ExactId) != null;
    }

    /// <summary>Which side a selector refers to, if it can be determined.</summary>
    public static Allegiance? SideOf(string? selector, BattleSnapshot snap)
    {
        if (!TryParse(selector, out var p)) return null;
        if (p.ExactId == null) return p.Side;
        return snap.Find(p.ExactId)?.Allegiance;
    }

    /// <summary>All formations the selector matches. Dead formations are included only if requested.</summary>
    public static List<FormationState> SelectAll(string? selector, BattleSnapshot snap, bool includeDead = false)
    {
        var result = new List<FormationState>();
        if (!TryParse(selector, out var p)) return result;

        if (p.ExactId != null)
        {
            var f = snap.Find(p.ExactId);
            if (f != null && (includeDead || f.IsAlive)) result.Add(f);
            return result;
        }

        foreach (var f in PoolFor(p.Side, snap))
        {
            if (!includeDead && !f.IsAlive) continue;
            if (p.Role.HasValue && f.Role != p.Role.Value) continue;
            result.Add(f);
        }
        return result;
    }

    /// <summary>
    /// One living formation for a selector, chosen relative to <paramref name="from"/>: the exact id, the largest, or
    /// the nearest match. <paramref name="exclude"/> skips a formation (e.g. the one being ordered).
    /// </summary>
    public static FormationState? SelectOne(string? selector, BattleSnapshot snap, Vec2 from, string? exclude = null)
    {
        if (!TryParse(selector, out var p)) return null;
        var candidates = SelectAll(selector, snap)
            .Where(f => exclude == null || !string.Equals(f.Id, exclude, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0) return null;
        if (p.ExactId != null) return candidates[0];
        if (p.Pick == Pick.Largest) return candidates.OrderByDescending(f => f.Count).First();
        return candidates.OrderBy(f => Vec2.Distance(f.Position, from)).First();
    }

    private static IEnumerable<FormationState> PoolFor(Allegiance side, BattleSnapshot snap) => side switch
    {
        Allegiance.Own => snap.Own,
        Allegiance.Ally => snap.Allies,
        _ => snap.Enemies,
    };
}
