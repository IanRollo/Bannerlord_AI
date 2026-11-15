using System;
using System.Collections.Generic;
using System.Linq;

namespace BannerlordAI.Core.Model;

/// <summary>
/// The army-relative frame models reason in: origin at our army's centre, +Y toward the enemy centre, +X to our right.
/// Fixed per snapshot; anything a model says in local coordinates is converted with the frame of the snapshot it saw.
/// </summary>
public sealed class BattleFrame
{
    public BattleFrame(Vec2 origin, Vec2 forward, float originHeight = 0f)
    {
        Origin = origin;
        Forward = forward.LengthSquared < 1e-6f ? new Vec2(0f, 1f) : forward.Normalized();
        // Rotate forward 90 degrees clockwise (world X east, Y north): facing north, right is east.
        Right = new Vec2(Forward.Y, -Forward.X);
        OriginHeight = originHeight;
    }

    public Vec2 Origin { get; }
    public Vec2 Forward { get; }
    public Vec2 Right { get; }
    public float OriginHeight { get; }

    public Vec2 ToLocal(Vec2 world)
    {
        var d = world - Origin;
        return new Vec2(d.Dot(Right), d.Dot(Forward));
    }

    public Vec2 ToWorld(Vec2 local) => Origin + Right * local.X + Forward * local.Y;

    /// <summary>Converts a world-space direction or offset to local axes (no translation).</summary>
    public Vec2 DirToLocal(Vec2 worldDir) => new(worldDir.Dot(Right), worldDir.Dot(Forward));

    /// <summary>Converts a local-axis offset to a world-space offset (no translation).</summary>
    public Vec2 OffsetToWorld(Vec2 localOffset) => Right * localOffset.X + Forward * localOffset.Y;

    public static BattleFrame Identity { get; } = new(Vec2.Zero, new Vec2(0f, 1f));
}

/// <summary>Coarse height grid in the local frame. Heights are metres relative to the frame origin; null = outside the map.</summary>
public sealed class TerrainGrid
{
    public TerrainGrid(float[] xs, float[] ys, float?[] heights)
    {
        if (heights.Length != xs.Length * ys.Length) throw new ArgumentException("heights must be xs.Length * ys.Length");
        Xs = xs;
        Ys = ys;
        Heights = heights;
    }

    /// <summary>Local X of each column, left to right.</summary>
    public float[] Xs { get; }

    /// <summary>Local Y of each row, near (own side) to far (enemy side).</summary>
    public float[] Ys { get; }

    /// <summary>Row-major: index = row * Xs.Length + col.</summary>
    public float?[] Heights { get; }

    public float? At(int row, int col) => Heights[row * Xs.Length + col];
}

/// <summary>Axis-aligned world-space bounds of the playable area.</summary>
public readonly struct MapBounds
{
    public MapBounds(Vec2 min, Vec2 max)
    {
        Min = min;
        Max = max;
    }

    public Vec2 Min { get; }
    public Vec2 Max { get; }

    public bool Contains(Vec2 p) => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y;

    public Vec2 Clamp(Vec2 p, float margin = 5f) => new(
        Math.Max(Min.X + margin, Math.Min(Max.X - margin, p.X)),
        Math.Max(Min.Y + margin, Math.Min(Max.Y - margin, p.Y)));
}

/// <summary>Immutable picture of the battle from one team's perspective. Built on the game thread, read anywhere.</summary>
public sealed class BattleSnapshot
{
    public long Sequence { get; init; }

    /// <summary>Mission time in seconds (pauses with the game).</summary>
    public double Time { get; init; }

    public BattlePhase Phase { get; init; } = BattlePhase.Battle;

    public BattleType Type { get; init; } = BattleType.Field;

    /// <summary>True if this team is the attacking side (relevant for sieges, and for plain field battles).</summary>
    public bool IsAttacker { get; init; }

    public IReadOnlyList<FormationState> Own { get; init; } = Array.Empty<FormationState>();
    public IReadOnlyList<FormationState> Allies { get; init; } = Array.Empty<FormationState>();
    public IReadOnlyList<FormationState> Enemies { get; init; } = Array.Empty<FormationState>();

    public BattleFrame Frame { get; init; } = BattleFrame.Identity;

    public TerrainGrid? Terrain { get; init; }

    public MapBounds? Bounds { get; init; }

    public IEnumerable<FormationState> All => Own.Concat(Allies).Concat(Enemies);

    public FormationState? Find(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var f in All)
            if (string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase)) return f;
        return null;
    }

    public int OwnMen => Own.Sum(f => f.Count);
    public int EnemyMen => Enemies.Sum(f => f.Count);
    public int AllyMen => Allies.Sum(f => f.Count);

    /// <summary>Minimum distance between any living own formation and any living enemy formation; +inf if none.</summary>
    public float MinEnemyDistance()
    {
        var best = float.PositiveInfinity;
        foreach (var o in Own)
        {
            if (!o.IsAlive) continue;
            foreach (var e in Enemies)
            {
                if (!e.IsAlive) continue;
                var d = Vec2.Distance(o.Position, e.Position);
                if (d < best) best = d;
            }
        }
        return best;
    }
}
