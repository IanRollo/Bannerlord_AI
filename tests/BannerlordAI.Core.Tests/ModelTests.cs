using BannerlordAI.Core.Model;
using static BannerlordAI.Core.Tests.TestData;

namespace BannerlordAI.Core.Tests;

public class Vec2Tests
{
    [Fact]
    public void Arithmetic()
    {
        var a = new Vec2(3, 4);
        var b = new Vec2(1, -2);
        Assert.Equal(new Vec2(4, 2), a + b);
        Assert.Equal(new Vec2(2, 6), a - b);
        Assert.Equal(new Vec2(-3, -4), -a);
        Assert.Equal(new Vec2(6, 8), a * 2f);
        Assert.Equal(new Vec2(6, 8), 2f * a);
        Assert.Equal(new Vec2(1.5f, 2), a / 2f);
    }

    [Fact]
    public void DotProduct() => Assert.Equal(-5f, new Vec2(3, 4).Dot(new Vec2(1, -2)));

    [Fact]
    public void LengthAndDistance()
    {
        Assert.Equal(5f, new Vec2(3, 4).Length, 4);
        Assert.Equal(25f, new Vec2(3, 4).LengthSquared);
        Assert.Equal(5f, Vec2.Distance(new Vec2(1, 1), new Vec2(4, 5)), 4);
        Assert.Equal(0f, Vec2.Zero.Length);
    }

    [Fact]
    public void NormalizedHasUnitLength()
    {
        var n = new Vec2(10, 0).Normalized();
        Assert.Equal(1f, n.Length, 4);
        Assert.Equal(new Vec2(1, 0), n);
    }

    [Fact]
    public void NormalizedZeroOrTinyIsZero()
    {
        Assert.Equal(Vec2.Zero, Vec2.Zero.Normalized());
        Assert.Equal(Vec2.Zero, new Vec2(1e-7f, 0).Normalized());
    }

    [Fact]
    public void EqualityAndHash()
    {
        Assert.True(new Vec2(1, 2) == new Vec2(1, 2));
        Assert.True(new Vec2(1, 2) != new Vec2(2, 1));
        Assert.True(new Vec2(1, 2).Equals((object)new Vec2(1, 2)));
        Assert.False(new Vec2(1, 2).Equals("x"));
        Assert.Equal(new Vec2(1, 2).GetHashCode(), new Vec2(1, 2).GetHashCode());
    }

    [Fact]
    public void ToStringRoundsToWholeMetresInvariant() =>
        Assert.Equal("(-80,13)", new Vec2(-80.2f, 13.2f).ToString());
}

public class BattleFrameTests
{
    private static void AssertVec(Vec2 expected, Vec2 actual, int precision = 3)
    {
        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
    }

    [Fact]
    public void IdentityFrame()
    {
        var f = BattleFrame.Identity;
        Assert.Equal(new Vec2(0, 1), f.Forward);
        Assert.Equal(new Vec2(1, 0), f.Right);
        AssertVec(new Vec2(5, 7), f.ToLocal(new Vec2(5, 7)));
        AssertVec(new Vec2(5, 7), f.ToWorld(new Vec2(5, 7)));
    }

    [Fact]
    public void RightIsClockwiseFromForward()
    {
        var east = new BattleFrame(Vec2.Zero, new Vec2(1, 0));
        AssertVec(new Vec2(0, -1), east.Right);
        var south = new BattleFrame(Vec2.Zero, new Vec2(0, -1));
        AssertVec(new Vec2(-1, 0), south.Right);
    }

    [Fact]
    public void ForwardIsNormalized()
    {
        var f = new BattleFrame(Vec2.Zero, new Vec2(0, 250));
        Assert.Equal(1f, f.Forward.Length, 4);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(1e-4f, 0f)]
    public void DegenerateForwardFallsBackToNorth(float fx, float fy)
    {
        var f = new BattleFrame(new Vec2(3, 4), new Vec2(fx, fy));
        Assert.Equal(new Vec2(0, 1), f.Forward);
        Assert.Equal(new Vec2(1, 0), f.Right);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 0f)]
    [InlineData(-1f, 0f)]
    [InlineData(3f, 4f)]
    [InlineData(-2f, -7f)]
    public void LocalWorldRoundTrip(float fx, float fy)
    {
        var frame = new BattleFrame(new Vec2(10, -20), new Vec2(fx, fy), 12f);
        foreach (var p in new[] { new Vec2(0, 0), new Vec2(100, -50), new Vec2(-33.5f, 77.25f) })
        {
            AssertVec(p, frame.ToWorld(frame.ToLocal(p)), 2);
            AssertVec(p, frame.ToLocal(frame.ToWorld(p)), 2);
        }
        Assert.Equal(12f, frame.OriginHeight);
    }

    [Fact]
    public void RotatedFrameEastward()
    {
        var frame = new BattleFrame(new Vec2(10, 20), new Vec2(1, 0)); // facing east
        // 30 m ahead is east; 5 m to our right is south.
        AssertVec(new Vec2(40, 20), frame.ToWorld(new Vec2(0, 30)));
        AssertVec(new Vec2(10, 15), frame.ToWorld(new Vec2(5, 0)));
        AssertVec(new Vec2(5, 30), frame.ToLocal(new Vec2(40, 15)));
    }

    [Fact]
    public void OffsetAndDirectionIgnoreOrigin()
    {
        var frame = new BattleFrame(new Vec2(1000, 1000), new Vec2(1, 0));
        var off = frame.OffsetToWorld(new Vec2(2, 3)); // 2 right (south), 3 ahead (east)
        AssertVec(new Vec2(3, -2), off);
        AssertVec(new Vec2(2, 3), frame.DirToLocal(off));
    }

    [Fact]
    public void ZeroOffsetIsZero() => Assert.Equal(Vec2.Zero, new BattleFrame(new Vec2(5, 5), new Vec2(2, 2)).OffsetToWorld(Vec2.Zero));
}

public class MapBoundsTests
{
    private static readonly MapBounds B = new(new Vec2(0, 0), new Vec2(100, 200));

    [Fact]
    public void Contains()
    {
        Assert.True(B.Contains(new Vec2(50, 50)));
        Assert.True(B.Contains(new Vec2(0, 200)));
        Assert.False(B.Contains(new Vec2(-1, 50)));
        Assert.False(B.Contains(new Vec2(50, 201)));
    }

    [Fact]
    public void ClampPullsInsideByMargin()
    {
        Assert.Equal(new Vec2(5, 50), B.Clamp(new Vec2(-10, 50)));
        Assert.Equal(new Vec2(95, 195), B.Clamp(new Vec2(500, 500)));
        Assert.Equal(new Vec2(50, 5), B.Clamp(new Vec2(50, -3)));
    }

    [Fact]
    public void ClampLeavesInteriorPointsAlone() => Assert.Equal(new Vec2(50, 60), B.Clamp(new Vec2(50, 60)));

    [Fact]
    public void ClampCustomMargin()
    {
        Assert.Equal(new Vec2(0, 0), B.Clamp(new Vec2(-9, -9), 0f));
        Assert.Equal(new Vec2(20, 20), B.Clamp(new Vec2(1, 1), 20f));
    }
}

public class FormationAndSnapshotTests
{
    [Fact]
    public void StrengthIsCountOverInitial()
    {
        Assert.Equal(0.25f, Form("F1", FormationRole.Infantry, 25, 0, 0, initial: 100).Strength);
        Assert.Equal(1f, Form("F1", FormationRole.Infantry, 100, 0, 0).Strength);
        Assert.Equal(0f, Form("F1", FormationRole.Infantry, 0, 0, 0, initial: 100).Strength);
    }

    [Fact]
    public void StrengthWithZeroInitialIsZero() =>
        Assert.Equal(0f, Form("F1", FormationRole.Infantry, 10, 0, 0, initial: 0).Strength);

    [Fact]
    public void IsAliveFollowsCount()
    {
        Assert.True(Form("F1", FormationRole.Infantry, 1, 0, 0).IsAlive);
        Assert.False(Form("F1", FormationRole.Infantry, 0, 0, 0, initial: 5).IsAlive);
    }

    [Fact]
    public void RoleNames()
    {
        Assert.Equal("horse_archer", Form("F1", FormationRole.HorseArcher, 1, 0, 0).RoleName);
        Assert.Equal("skirmisher", Roles.ToWire(FormationRole.Skirmisher));
        Assert.True(Roles.IsMounted(FormationRole.Cavalry));
        Assert.False(Roles.IsMounted(FormationRole.Ranged));
        Assert.True(Roles.IsMissile(FormationRole.HorseArcher));
        Assert.False(Roles.IsMissile(FormationRole.Cavalry));
    }

    [Theory]
    [InlineData("infantry", FormationRole.Infantry)]
    [InlineData("Archers", FormationRole.Ranged)]
    [InlineData("ranged", FormationRole.Ranged)]
    [InlineData("Horse-Archer", FormationRole.HorseArcher)]
    [InlineData("horse archers", FormationRole.HorseArcher)]
    [InlineData("CAVALRY", FormationRole.Cavalry)]
    [InlineData("skirmishers", FormationRole.Skirmisher)]
    public void RoleTryParse(string text, FormationRole expected)
    {
        Assert.True(Roles.TryParse(text, out var role));
        Assert.Equal(expected, role);
    }

    [Theory]
    [InlineData("wizard")]
    [InlineData("")]
    [InlineData(null)]
    public void RoleTryParseFailure(string? text) => Assert.False(Roles.TryParse(text, out _));

    [Fact]
    public void FindIsCaseInsensitiveAcrossSides()
    {
        var s = Snap(allies: new[] { Form("A1", FormationRole.Infantry, 10, 200, 0) });
        Assert.Equal("F1", s.Find("f1")!.Id);
        Assert.Equal("E2", s.Find("e2")!.Id);
        Assert.Equal("A1", s.Find("a1")!.Id);
        Assert.Null(s.Find("F99"));
        Assert.Null(s.Find(""));
        Assert.Null(s.Find(null));
    }

    [Fact]
    public void ManCounts()
    {
        var s = Snap(allies: new[] { Form("A1", FormationRole.Infantry, 10, 200, 0) });
        Assert.Equal(190, s.OwnMen);
        Assert.Equal(10, s.AllyMen);
        Assert.Equal(190, s.EnemyMen);
        Assert.Equal(1, s.All.Count(f => f.Id == "A1"));
    }

    [Fact]
    public void MinEnemyDistance()
    {
        var s = Snap(new[] { Form("F1", FormationRole.Infantry, 10, 0, 0) },
                     new[] { Form("E1", FormationRole.Infantry, 10, 30, 40), Form("E2", FormationRole.Infantry, 10, 0, 500) });
        Assert.Equal(50f, s.MinEnemyDistance(), 3);
    }

    [Fact]
    public void MinEnemyDistanceIgnoresDeadFormations()
    {
        var s = Snap(new[] { Form("F1", FormationRole.Infantry, 10, 0, 0), Form("F2", FormationRole.Infantry, 0, 0, 290, initial: 10) },
                     new[] { Form("E1", FormationRole.Infantry, 0, 0, 10, initial: 10), Form("E2", FormationRole.Infantry, 10, 0, 300) });
        Assert.Equal(300f, s.MinEnemyDistance(), 3);
    }

    [Fact]
    public void MinEnemyDistanceInfiniteWhenNoPairs()
    {
        Assert.True(float.IsPositiveInfinity(Snap(new List<FormationState>(), EnemyArmy()).MinEnemyDistance()));
        Assert.True(float.IsPositiveInfinity(Snap(OwnArmy(), new List<FormationState>()).MinEnemyDistance()));
        var allDead = new[] { Form("E1", FormationRole.Infantry, 0, 0, 10, initial: 10) };
        Assert.True(float.IsPositiveInfinity(Snap(OwnArmy(), allDead).MinEnemyDistance()));
    }
}

public class SnapshotFactoryTests
{
    private static BattleSnapshot Create(
        IReadOnlyList<FormationState> own, IReadOnlyList<FormationState> enemies,
        Func<Vec2, float?>? height = null, MapBounds? bounds = null, TerrainOptions? terrain = null)
        => SnapshotFactory.Create(7, 12.5, BattlePhase.Battle, BattleType.Field, true, own, Array.Empty<FormationState>(), enemies, height, bounds, terrain);

    [Fact]
    public void CopiesMetadata()
    {
        var s = Create(OwnArmy(), EnemyArmy());
        Assert.Equal(7, s.Sequence);
        Assert.Equal(12.5, s.Time);
        Assert.Equal(BattlePhase.Battle, s.Phase);
        Assert.Equal(BattleType.Field, s.Type);
        Assert.True(s.IsAttacker);
        Assert.Null(s.Terrain);
    }

    [Fact]
    public void FrameCentreIsWeightedByCount()
    {
        var own = new[] { Form("F1", FormationRole.Infantry, 100, 0, 0), Form("F2", FormationRole.Infantry, 300, 40, 0) };
        var s = Create(own, new[] { Form("E1", FormationRole.Infantry, 50, 30, 300) });
        Assert.Equal(30f, s.Frame.Origin.X, 3);
        Assert.Equal(0f, s.Frame.Origin.Y, 3);
        Assert.Equal(0f, s.Frame.Forward.X, 3);
        Assert.Equal(1f, s.Frame.Forward.Y, 3);
    }

    [Fact]
    public void DeadFormationsDoNotMoveTheCentre()
    {
        var own = new[] { Form("F1", FormationRole.Infantry, 100, 0, 0), Form("F2", FormationRole.Infantry, 0, 900, 900, initial: 50) };
        var s = Create(own, new[] { Form("E1", FormationRole.Infantry, 50, 0, 100) });
        Assert.Equal(Vec2.Zero, s.Frame.Origin);
    }

    [Fact]
    public void WeightedCentreOfNothingIsNull()
    {
        Assert.Null(SnapshotFactory.WeightedCentre(Array.Empty<FormationState>()));
        Assert.Null(SnapshotFactory.WeightedCentre(new[] { Form("F1", FormationRole.Infantry, 0, 5, 5, initial: 9) }));
    }

    [Fact]
    public void NoEnemiesKeepsNorthFacing()
    {
        var s = Create(OwnArmy(), new List<FormationState>());
        Assert.Equal(new Vec2(0, 1), s.Frame.Forward);
    }

    [Fact]
    public void NoOwnLivingUsesWorldOrigin()
    {
        var s = Create(new List<FormationState>(), EnemyArmy());
        Assert.Equal(Vec2.Zero, s.Frame.Origin);
        Assert.Equal(new Vec2(0, 1), s.Frame.Forward);
    }

    [Fact]
    public void ForwardPointsAtEnemyCentre_Rotated()
    {
        var own = new[] { Form("F1", FormationRole.Infantry, 100, 0, 0) };
        var enemy = new[] { Form("E1", FormationRole.Infantry, 100, 300, 0) };
        var s = Create(own, enemy);
        Assert.Equal(1f, s.Frame.Forward.X, 3);
        Assert.Equal(0f, s.Frame.Forward.Y, 3);
        Assert.Equal(0f, s.Frame.Right.X, 3);
        Assert.Equal(-1f, s.Frame.Right.Y, 3);
        var local = s.Frame.ToLocal(new Vec2(300, 0));
        Assert.Equal(0f, local.X, 3);
        Assert.Equal(300f, local.Y, 3);
    }

    [Fact]
    public void SimpleBuildsExplicitSnapshot()
    {
        var s = SnapshotFactory.Simple(OwnArmy(), EnemyArmy(), time: 33, sequence: 4);
        Assert.Equal(33, s.Time);
        Assert.Equal(4, s.Sequence);
        Assert.Empty(s.Allies);
        Assert.Null(s.Terrain);
    }

    [Fact]
    public void TerrainGridIsRelativeToOriginHeightRowMajor()
    {
        var own = new[] { Form("F1", FormationRole.Infantry, 100, 0, 0) };
        var enemy = new[] { Form("E1", FormationRole.Infantry, 100, 0, 300) };
        // Absolute height: 5 at origin, rising 0.1 m per metre north and 0.5 m per metre east.
        Func<Vec2, float?> height = p => 5f + p.Y * 0.1f + p.X * 0.5f;
        var s = Create(own, enemy, height, terrain: new TerrainOptions { Columns = 3, Rows = 3, HalfWidth = 100, DepthMargin = 50 });
        var g = s.Terrain!;

        Assert.Equal(new[] { -100f, 0f, 100f }, g.Xs);
        Assert.Equal(new[] { -50f, 150f, 350f }, g.Ys);
        Assert.Equal(9, g.Heights.Length);
        Assert.Equal(5f, s.Frame.OriginHeight);

        // row = near->far (Y), col = left->right (X); heights relative to origin height.
        Assert.Equal(-5f - 50f, g.At(0, 0)!.Value, 2);
        Assert.Equal(-5f, g.At(0, 1)!.Value, 2);
        Assert.Equal(-5f + 50f, g.At(0, 2)!.Value, 2);
        Assert.Equal(15f, g.At(1, 1)!.Value, 2);
        Assert.Equal(35f + 50f, g.At(2, 2)!.Value, 2);
        Assert.Equal(g.Heights[1 * 3 + 2], g.At(1, 2));
    }

    [Fact]
    public void TerrainRowsUseMinimumDepthOfOneHundred()
    {
        var own = new[] { Form("F1", FormationRole.Infantry, 100, 0, 0) };
        var enemy = new[] { Form("E1", FormationRole.Infantry, 100, 0, 40) };
        var s = Create(own, enemy, _ => 0f, terrain: new TerrainOptions { Columns = 2, Rows = 2, HalfWidth = 10, DepthMargin = 20 });
        Assert.Equal(new[] { -20f, 120f }, s.Terrain!.Ys);
    }

    [Fact]
    public void TerrainIsNullOutsideBoundsOrWhenSamplerReturnsNull()
    {
        var own = new[] { Form("F1", FormationRole.Infantry, 100, 0, 0) };
        var enemy = new[] { Form("E1", FormationRole.Infantry, 100, 0, 300) };
        var bounds = new MapBounds(new Vec2(-50, -100), new Vec2(50, 200));
        var s = Create(own, enemy, _ => 10f, bounds, new TerrainOptions { Columns = 3, Rows = 3, HalfWidth = 100, DepthMargin = 50 });
        var g = s.Terrain!;
        Assert.Null(g.At(0, 0)); // x=-100 outside
        Assert.NotNull(g.At(0, 1)); // (0,-50) inside
        Assert.Null(g.At(0, 2));
        Assert.Null(g.At(1, 0));
        Assert.NotNull(g.At(1, 1)); // (0,150) inside
        for (var c = 0; c < 3; c++) Assert.Null(g.At(2, c)); // y=350 outside

        var s2 = Create(own, enemy, p => p.Y > 100 ? null : 3f, terrain: new TerrainOptions { Columns = 2, Rows = 3, HalfWidth = 10, DepthMargin = 50 });
        Assert.NotNull(s2.Terrain!.At(0, 0));
        Assert.Null(s2.Terrain.At(2, 1));
    }

    [Fact]
    public void TerrainSamplesAlongRotatedFrame()
    {
        // Enemy to the east: local +Y is world +X, local +X (right) is world -Y.
        var own = new[] { Form("F1", FormationRole.Infantry, 100, 0, 0) };
        var enemy = new[] { Form("E1", FormationRole.Infantry, 100, 300, 0) };
        Func<Vec2, float?> height = p => p.X * 0.01f - p.Y * 0.02f;
        var s = Create(own, enemy, height, terrain: new TerrainOptions { Columns = 3, Rows = 2, HalfWidth = 100, DepthMargin = 100 });
        var g = s.Terrain!;
        Assert.Equal(new[] { -100f, 400f }, g.Ys);
        // Row 0, col 2: local (100,-100) -> world (-100, -100) -> -1 + 2 = 1
        Assert.Equal(1f, g.At(0, 2)!.Value, 3);
        // Row 1, col 0: local (-100, 400) -> world (400, 100) -> 4 - 2 = 2
        Assert.Equal(2f, g.At(1, 0)!.Value, 3);
    }

    [Fact]
    public void TerrainDisabledOrNoSamplerOrTooSmallGridGivesNull()
    {
        var own = new[] { Form("F1", FormationRole.Infantry, 100, 0, 0) };
        var enemy = new[] { Form("E1", FormationRole.Infantry, 100, 0, 300) };
        Assert.Null(Create(own, enemy, _ => 0f, terrain: new TerrainOptions { Enabled = false }).Terrain);
        Assert.Null(Create(own, enemy, null).Terrain);
        Assert.Null(Create(own, enemy, _ => 0f, terrain: new TerrainOptions { Columns = 1 }).Terrain);
        Assert.NotNull(Create(own, enemy, _ => 0f).Terrain);
    }

    [Fact]
    public void TerrainGridValidatesLength() =>
        Assert.Throws<ArgumentException>(() => new TerrainGrid(new float[2], new float[2], new float?[3]));
}
