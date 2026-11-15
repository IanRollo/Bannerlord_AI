using System;
using System.Collections.Generic;
using System.Linq;

namespace BannerlordAI.Core.Model;

public sealed class TerrainOptions
{
    public bool Enabled { get; init; } = true;
    public int Columns { get; init; } = 7;
    public int Rows { get; init; } = 8;
    /// <summary>Half-width of the sampled area across the front, metres.</summary>
    public float HalfWidth { get; init; } = 300f;
    /// <summary>Extra depth sampled behind our centre and beyond the enemy centre, metres.</summary>
    public float DepthMargin { get; init; } = 200f;
}

/// <summary>
/// Builds a <see cref="BattleSnapshot"/> from raw formation states: computes the army-relative frame and samples a
/// coarse terrain grid. Must run on the thread that owns the height sampler (the game thread in the mod).
/// </summary>
public static class SnapshotFactory
{
    public static BattleSnapshot Create(
        long sequence,
        double time,
        BattlePhase phase,
        BattleType type,
        bool isAttacker,
        IReadOnlyList<FormationState> own,
        IReadOnlyList<FormationState> allies,
        IReadOnlyList<FormationState> enemies,
        Func<Vec2, float?>? heightAt = null,
        MapBounds? bounds = null,
        TerrainOptions? terrain = null)
    {
        var ownCentre = WeightedCentre(own);
        var enemyCentre = WeightedCentre(enemies);

        var origin = ownCentre ?? Vec2.Zero;
        var forward = enemyCentre.HasValue && ownCentre.HasValue ? enemyCentre.Value - ownCentre.Value : new Vec2(0f, 1f);
        var originHeight = heightAt?.Invoke(origin) ?? 0f;
        var frame = new BattleFrame(origin, forward, originHeight);

        TerrainGrid? grid = null;
        terrain ??= new TerrainOptions();
        if (heightAt != null && terrain.Enabled && terrain.Columns > 1 && terrain.Rows > 1)
        {
            var depth = enemyCentre.HasValue ? Vec2.Distance(origin, enemyCentre.Value) : 300f;
            grid = SampleGrid(frame, depth, heightAt, bounds, terrain);
        }

        return new BattleSnapshot
        {
            Sequence = sequence,
            Time = time,
            Phase = phase,
            Type = type,
            IsAttacker = isAttacker,
            Own = own,
            Allies = allies,
            Enemies = enemies,
            Frame = frame,
            Terrain = grid,
            Bounds = bounds,
        };
    }

    public static Vec2? WeightedCentre(IEnumerable<FormationState> formations)
    {
        float sx = 0f, sy = 0f;
        var n = 0;
        foreach (var f in formations)
        {
            if (!f.IsAlive) continue;
            sx += f.Position.X * f.Count;
            sy += f.Position.Y * f.Count;
            n += f.Count;
        }
        return n == 0 ? null : new Vec2(sx / n, sy / n);
    }

    private static TerrainGrid SampleGrid(BattleFrame frame, float depth, Func<Vec2, float?> heightAt, MapBounds? bounds, TerrainOptions o)
    {
        var xs = new float[o.Columns];
        for (var c = 0; c < o.Columns; c++)
            xs[c] = -o.HalfWidth + 2f * o.HalfWidth * c / (o.Columns - 1);

        var nearY = -o.DepthMargin;
        var farY = Math.Max(depth, 100f) + o.DepthMargin;
        var ys = new float[o.Rows];
        for (var r = 0; r < o.Rows; r++)
            ys[r] = nearY + (farY - nearY) * r / (o.Rows - 1);

        var heights = new float?[o.Rows * o.Columns];
        for (var r = 0; r < o.Rows; r++)
        {
            for (var c = 0; c < o.Columns; c++)
            {
                var world = frame.ToWorld(new Vec2(xs[c], ys[r]));
                float? h = null;
                if (bounds == null || bounds.Value.Contains(world))
                {
                    var raw = heightAt(world);
                    if (raw.HasValue) h = raw.Value - frame.OriginHeight;
                }
                heights[r * o.Columns + c] = h;
            }
        }
        return new TerrainGrid(xs, ys, heights);
    }

    /// <summary>Convenience for tests and the simulator: a snapshot with an explicit frame and no terrain.</summary>
    public static BattleSnapshot Simple(
        IReadOnlyList<FormationState> own,
        IReadOnlyList<FormationState> enemies,
        double time = 60,
        long sequence = 1,
        IReadOnlyList<FormationState>? allies = null) =>
        Create(sequence, time, BattlePhase.Battle, BattleType.Field, false, own, allies ?? Array.Empty<FormationState>(), enemies);

    internal static IEnumerable<FormationState> Alive(this IEnumerable<FormationState> fs) => fs.Where(f => f.IsAlive);
}
