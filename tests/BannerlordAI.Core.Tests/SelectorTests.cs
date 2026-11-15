using BannerlordAI.Core.Model;
using BannerlordAI.Core.Planning;
using static BannerlordAI.Core.Tests.TestData;

namespace BannerlordAI.Core.Tests;

public class SelectorTests
{
    private static BattleSnapshot World() => Snap(
        own: new[]
        {
            Form("F1", FormationRole.Infantry, 100, 0, 0),
            Form("F2", FormationRole.Ranged, 50, 0, -30),
            Form("F3", FormationRole.Cavalry, 40, -80, -10),
            Form("F4", FormationRole.Infantry, 0, 0, 0, initial: 30),
        },
        enemies: new[]
        {
            Form("E1", FormationRole.Infantry, 100, 0, 300),
            Form("E2", FormationRole.Ranged, 50, 0, 340),
            Form("E3", FormationRole.Cavalry, 40, 120, 300),
            Form("E4", FormationRole.Infantry, 0, 0, 100, initial: 50),
        },
        allies: new[] { Form("A1", FormationRole.Infantry, 60, 200, 0) });

    private static string[] Ids(IEnumerable<FormationState> fs) => fs.Select(f => f.Id).ToArray();

    [Fact]
    public void ExactIdsAreCaseInsensitive()
    {
        var w = World();
        Assert.Equal(new[] { "F1" }, Ids(Selectors.SelectAll("f1", w)));
        Assert.Equal(new[] { "E3" }, Ids(Selectors.SelectAll(" E3 ", w)));
        Assert.Equal(new[] { "A1" }, Ids(Selectors.SelectAll("a1", w)));
    }

    [Fact]
    public void DeadExactIdOnlyWithIncludeDead()
    {
        var w = World();
        Assert.Empty(Selectors.SelectAll("F4", w));
        Assert.Equal(new[] { "F4" }, Ids(Selectors.SelectAll("F4", w, includeDead: true)));
    }

    [Fact]
    public void UnknownExactIdIsInvalid()
    {
        var w = World();
        Assert.False(Selectors.IsValid("E9", w));
        Assert.Empty(Selectors.SelectAll("E9", w));
        Assert.Null(Selectors.SideOf("E9", w));
        Assert.Null(Selectors.SelectOne("E9", w, Vec2.Zero));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bogus:infantry")]
    [InlineData("enemy:wizards")]
    [InlineData("enemy:")]
    public void InvalidSelectors(string? s)
    {
        var w = World();
        if (s == "enemy:")
        {
            // empty kind means "any" for the side
            Assert.True(Selectors.IsValid(s, w));
            return;
        }
        Assert.False(Selectors.IsValid(s, w));
        Assert.Empty(Selectors.SelectAll(s, w));
        Assert.Null(Selectors.SideOf(s, w));
        Assert.Null(Selectors.SelectOne(s, w, Vec2.Zero));
    }

    [Fact]
    public void SideKindSelectors()
    {
        var w = World();
        Assert.Equal(new[] { "E3" }, Ids(Selectors.SelectAll("enemy:cavalry", w)));
        Assert.Equal(new[] { "E1" }, Ids(Selectors.SelectAll("Enemy:Infantry", w)));
        Assert.Equal(new[] { "E1", "E2", "E3" }, Ids(Selectors.SelectAll("enemy:any", w)));
        Assert.Equal(new[] { "E1", "E2", "E3" }, Ids(Selectors.SelectAll("enemy", w)));
        Assert.Equal(new[] { "F1", "F2", "F3" }, Ids(Selectors.SelectAll("own:all", w)));
        Assert.Equal(new[] { "F2" }, Ids(Selectors.SelectAll("ours:archers", w)));
        Assert.Equal(new[] { "A1" }, Ids(Selectors.SelectAll("allies:infantry", w)));
        Assert.Equal(new[] { "A1" }, Ids(Selectors.SelectAll("ally", w)));
        Assert.Equal(new[] { "E1", "E2", "E3" }, Ids(Selectors.SelectAll("enemies:any", w)));
    }

    [Fact]
    public void SelectAllIncludeDead()
    {
        var w = World();
        Assert.Equal(new[] { "E1", "E2", "E3", "E4" }, Ids(Selectors.SelectAll("enemy:any", w, includeDead: true)));
        Assert.Equal(new[] { "E1", "E4" }, Ids(Selectors.SelectAll("enemy:infantry", w, includeDead: true)));
    }

    [Fact]
    public void SideOf()
    {
        var w = World();
        Assert.Equal(Allegiance.Own, Selectors.SideOf("F1", w));
        Assert.Equal(Allegiance.Enemy, Selectors.SideOf("e1", w));
        Assert.Equal(Allegiance.Ally, Selectors.SideOf("A1", w));
        Assert.Equal(Allegiance.Enemy, Selectors.SideOf("enemy:cavalry", w));
        Assert.Equal(Allegiance.Own, Selectors.SideOf("own:largest", w));
        Assert.Equal(Allegiance.Ally, Selectors.SideOf("ally:any", w));
    }

    [Fact]
    public void SelectOneNearest()
    {
        var w = World();
        Assert.Equal("E1", Selectors.SelectOne("enemy:nearest", w, new Vec2(0, 0))!.Id);
        Assert.Equal("E3", Selectors.SelectOne("enemy:nearest", w, new Vec2(130, 300))!.Id);
        // Dead E4 at (0,100) is closest to the origin but is ignored.
        Assert.Equal("E1", Selectors.SelectOne("enemy:infantry", w, new Vec2(0, 90))!.Id);
        // Role selector picks nearest of that role.
        Assert.Equal("E2", Selectors.SelectOne("enemy:ranged", w, new Vec2(0, 0))!.Id);
    }

    [Fact]
    public void SelectOneLargest()
    {
        var w = World();
        Assert.Equal("E1", Selectors.SelectOne("enemy:largest", w, new Vec2(500, 500))!.Id);
        Assert.Equal("F1", Selectors.SelectOne("own:main", w, Vec2.Zero)!.Id);
    }

    [Fact]
    public void SelectOneExclude()
    {
        var w = World();
        Assert.Equal("F2", Selectors.SelectOne("own:largest", w, Vec2.Zero, exclude: "F1")!.Id);
        Assert.Equal("F2", Selectors.SelectOne("own:largest", w, Vec2.Zero, exclude: "f1")!.Id);
        Assert.Null(Selectors.SelectOne("F1", w, Vec2.Zero, exclude: "F1"));
        Assert.Null(Selectors.SelectOne("own:infantry", w, Vec2.Zero, exclude: "F1")); // F4 is dead
    }

    [Fact]
    public void SelectOneExactIgnoresDistance() =>
        Assert.Equal("E2", Selectors.SelectOne("E2", World(), new Vec2(0, 0))!.Id);

    [Fact]
    public void SelectOneNoCandidates()
    {
        var w = Snap(OwnArmy(), new List<FormationState>());
        Assert.Null(Selectors.SelectOne("enemy:nearest", w, Vec2.Zero));
    }
}

public class CadenceTests
{
    private static readonly BannerlordAI.Core.Orchestration.CadenceConfig C = new();

    [Theory]
    [InlineData(0, 5)]
    [InlineData(50, 5)]
    [InlineData(80, 5)]
    [InlineData(190, 17.5)]
    [InlineData(299, 29.886)]
    public void Interpolates(double distance, double expected)
    {
        var v = BannerlordAI.Core.Orchestration.Cadence.LieutenantInterval(distance, C);
        Assert.NotNull(v);
        Assert.Equal(expected, v!.Value, 2);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(1000)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NaN)]
    public void IdleWhenFarOrUnknown(double distance) =>
        Assert.Null(BannerlordAI.Core.Orchestration.Cadence.LieutenantInterval(distance, C));

    [Fact]
    public void RespectsCustomConfig()
    {
        var c = new BannerlordAI.Core.Orchestration.CadenceConfig
        { NearDistance = 100, FarDistance = 200, LieutenantNearInterval = 2, LieutenantFarInterval = 12 };
        Assert.Equal(7.0, BannerlordAI.Core.Orchestration.Cadence.LieutenantInterval(150, c)!.Value, 6);
        Assert.Equal(2.0, BannerlordAI.Core.Orchestration.Cadence.LieutenantInterval(90, c)!.Value, 6);
        Assert.Null(BannerlordAI.Core.Orchestration.Cadence.LieutenantInterval(200, c));
    }
}
