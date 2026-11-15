using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BannerlordAI.Core.Model;

namespace BannerlordAI.Core.Compaction;

/// <summary>Renders a snapshot as a compact, model-friendly text report in the army-relative frame.</summary>
public static class SnapshotFormatter
{
    public static string Format(BattleSnapshot s, bool includeTerrain = true)
    {
        var sb = new StringBuilder();
        sb.Append("BATTLE t=").Append(Wire.Clock(s.Time))
          .Append(" type=").Append(Wire.Normalize(s.Type.ToString()))
          .Append(" you=").Append(s.IsAttacker ? "attacker" : "defender")
          .Append(" men: yours ").Append(s.OwnMen);
        if (s.Allies.Count > 0) sb.Append(" + allies ").Append(s.AllyMen);
        sb.Append(" vs enemy ").Append(s.EnemyMen).AppendLine();
        sb.AppendLine("Frame: metres; (0,0) = your army centre now; +y = toward enemy centre; +x = your right.");

        AppendTable(sb, "YOUR FORMATIONS (you command these)", s.Own, s, s.Enemies);
        if (s.Allies.Count > 0) AppendTable(sb, "ALLIED FORMATIONS (not under your command)", s.Allies, s, s.Enemies);
        AppendTable(sb, "ENEMY FORMATIONS", s.Enemies, s, s.Own.Concat(s.Allies).ToList());

        if (includeTerrain && s.Terrain != null) AppendTerrain(sb, s.Terrain);
        return sb.ToString();
    }

    private static void AppendTable(StringBuilder sb, string title, IReadOnlyList<FormationState> list, BattleSnapshot s, IReadOnlyList<FormationState> opponents)
    {
        sb.AppendLine().AppendLine(title);
        if (list.Count == 0)
        {
            sb.AppendLine("  (none)");
            return;
        }
        sb.AppendLine("  id  role          men      hp   morale ammo  pos(x,y)     facing      nearest-foe   state");
        foreach (var f in list)
        {
            if (!f.IsAlive)
            {
                sb.Append("  ").Append(Pad(f.Id, 4)).Append(Pad(f.RoleName, 14)).Append("0/").Append(f.InitialCount).AppendLine("  destroyed");
                continue;
            }
            var local = s.Frame.ToLocal(f.Position);
            var nearest = Nearest(f, opponents);
            var state = new List<string>();
            if (f.IsRouting) state.Add("ROUTING");
            if (f.IsEngaged) state.Add("in melee");
            if (!string.IsNullOrEmpty(f.Activity)) state.Add(f.Activity!);

            sb.Append("  ").Append(Pad(f.Id, 4))
              .Append(Pad(f.RoleName, 14))
              .Append(Pad(f.Count + "/" + f.InitialCount, 9))
              .Append(Pad(Pct(f.AverageHealth), 5))
              .Append(Pad(f.Morale.HasValue ? Pct(f.Morale.Value) : "-", 7))
              .Append(Pad(f.Ammo.HasValue ? Pct(f.Ammo.Value) : "-", 6))
              .Append(Pad(local.ToString(), 13))
              .Append(Pad(DirectionLabel(s.Frame.DirToLocal(f.Facing)), 12))
              .Append(Pad(nearest == null ? "-" : nearest.Value.id + " " + Wire.F(nearest.Value.dist) + "m", 14))
              .Append(state.Count == 0 ? "-" : string.Join(", ", state))
              .AppendLine();
        }
    }

    private static void AppendTerrain(StringBuilder sb, TerrainGrid t)
    {
        sb.AppendLine().AppendLine("TERRAIN height (m, relative to your centre). Rows far (enemy side) to near; '..' = off map.");
        sb.Append("   y\\x ");
        foreach (var x in t.Xs) sb.Append(PadLeft(Wire.F(x), 6));
        sb.AppendLine();
        for (var r = t.Ys.Length - 1; r >= 0; r--)
        {
            sb.Append(PadLeft(Wire.F(t.Ys[r]), 6)).Append(' ');
            for (var c = 0; c < t.Xs.Length; c++)
            {
                var h = t.At(r, c);
                sb.Append(PadLeft(h.HasValue ? SignedInt(h.Value) : "..", 6));
            }
            sb.AppendLine();
        }
    }

    private static (string id, float dist)? Nearest(FormationState f, IReadOnlyList<FormationState> others)
    {
        (string id, float dist)? best = null;
        foreach (var o in others)
        {
            if (!o.IsAlive) continue;
            var d = Vec2.Distance(f.Position, o.Position);
            if (best == null || d < best.Value.dist) best = (o.Id, d);
        }
        return best;
    }

    /// <summary>Eight-way label for a local-frame direction: ahead = toward the enemy.</summary>
    public static string DirectionLabel(Vec2 localDir)
    {
        if (localDir.LengthSquared < 1e-6f) return "-";
        var deg = Math.Atan2(localDir.X, localDir.Y) * 180.0 / Math.PI; // 0 = +y (ahead), 90 = +x (right)
        if (deg < 0) deg += 360;
        var sector = (int)Math.Round(deg / 45.0) % 8;
        return sector switch
        {
            0 => "ahead",
            1 => "ahead-right",
            2 => "right",
            3 => "back-right",
            4 => "back",
            5 => "back-left",
            6 => "left",
            _ => "ahead-left",
        };
    }

    private static string Pct(float v) => Wire.F(Math.Max(0, Math.Min(1, v)) * 100) + "%";

    private static string SignedInt(float v)
    {
        var r = (int)Math.Round(v);
        return r > 0 ? "+" + r : r.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Pad(string s, int width) => s.Length >= width ? s + " " : s + new string(' ', width - s.Length);

    private static string PadLeft(string s, int width) => s.Length >= width ? " " + s : new string(' ', width - s.Length) + s;
}
