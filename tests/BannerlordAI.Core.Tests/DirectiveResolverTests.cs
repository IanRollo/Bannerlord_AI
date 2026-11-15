using BannerlordAI.Core.Model;
using BannerlordAI.Core.Planning;
using static BannerlordAI.Core.Tests.TestData;

namespace BannerlordAI.Core.Tests;

public class DirectiveResolverTests
{
    private static Directive D(string formation, DirectiveAction action, string? target = null, Anchor? anchor = null,
        FlankSide? side = null, Arrangement? arr = null, FireOrder? fire = null, string source = "plan")
        => new() { FormationId = formation, Action = action, Target = target, Anchor = anchor, Side = side, Arrangement = arr, Fire = fire, Source = source };

    private static void AssertPos(Vec2 expected, Vec2? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.X, actual!.Value.X, 2);
        Assert.Equal(expected.Y, actual.Value.Y, 2);
    }

    [Fact]
    public void HoldWithoutAnchorStaysPut()
    {
        var c = DirectiveResolver.Resolve(D("F1", DirectiveAction.Hold), Snap())!;
        Assert.Equal(DirectiveAction.Hold, c.Action);
        Assert.Null(c.TargetFormationId);
        AssertPos(new Vec2(0, 0), c.Position);
        Assert.Null(c.Side);
    }

    [Fact]
    public void ReserveWithoutAnchorStaysPut()
    {
        var c = DirectiveResolver.Resolve(D("F3", DirectiveAction.Reserve), Snap())!;
        AssertPos(new Vec2(-80, -10), c.Position);
    }

    [Fact]
    public void HoldWithAbsoluteAnchorUsesIt()
    {
        var c = DirectiveResolver.Resolve(D("F1", DirectiveAction.Hold, anchor: Anchor.AbsoluteWorld(new Vec2(12, 34))), Snap())!;
        AssertPos(new Vec2(12, 34), c.Position);
    }

    [Fact]
    public void ModifiersAndSourcePassThrough()
    {
        var c = DirectiveResolver.Resolve(D("F1", DirectiveAction.Hold, arr: Arrangement.ShieldWall, fire: FireOrder.Hold, source: "contingency C1"), Snap())!;
        Assert.Equal(Arrangement.ShieldWall, c.Arrangement);
        Assert.Equal(FireOrder.Hold, c.Fire);
        Assert.Equal("contingency C1", c.Source);
        Assert.Equal("F1", c.FormationId);
    }

    [Fact]
    public void ChargeWithNullTargetPicksNearestEnemy()
    {
        var c = DirectiveResolver.Resolve(D("F3", DirectiveAction.Charge), Snap())!;
        Assert.Equal(DirectiveAction.Charge, c.Action);
        Assert.Equal("E1", c.TargetFormationId);
        Assert.Null(c.Position);
    }

    [Fact]
    public void ChargeNamedTargets()
    {
        Assert.Equal("E3", DirectiveResolver.Resolve(D("F1", DirectiveAction.Charge, "enemy:cavalry"), Snap())!.TargetFormationId);
        Assert.Equal("E2", DirectiveResolver.Resolve(D("F1", DirectiveAction.Charge, "E2"), Snap())!.TargetFormationId);
    }

    [Fact]
    public void ChargeWithFriendlyOrDeadTargetFallsBackToNearestEnemy()
    {
        Assert.Equal("E1", DirectiveResolver.Resolve(D("F1", DirectiveAction.Charge, "own:infantry"), Snap())!.TargetFormationId);
        var enemies = new[] { Form("E1", FormationRole.Infantry, 0, 0, 300, initial: 10), Form("E2", FormationRole.Ranged, 5, 0, 340) };
        Assert.Equal("E2", DirectiveResolver.Resolve(D("F1", DirectiveAction.Charge, "E1"), Snap(enemies: enemies))!.TargetFormationId);
    }

    [Theory]
    [InlineData(DirectiveAction.Charge)]
    [InlineData(DirectiveAction.Skirmish)]
    [InlineData(DirectiveAction.Flank)]
    public void AttackActionsWithNoEnemiesLeftBecomeHold(DirectiveAction action)
    {
        var s = Snap(enemies: new List<FormationState>());
        var c = DirectiveResolver.Resolve(D("F3", action), s)!;
        Assert.Equal(DirectiveAction.Hold, c.Action);
        Assert.Null(c.TargetFormationId);
        AssertPos(new Vec2(-80, -10), c.Position);
    }

    [Fact]
    public void AdvanceWithAnchorAndNoTargetMovesToPointWithoutTarget()
    {
        var c = DirectiveResolver.Resolve(D("F1", DirectiveAction.Advance, anchor: Anchor.AbsoluteWorld(new Vec2(0, 100))), Snap())!;
        Assert.Equal(DirectiveAction.Advance, c.Action);
        Assert.Null(c.TargetFormationId);
        AssertPos(new Vec2(0, 100), c.Position);
    }

    [Fact]
    public void AdvanceWithAnchorAndTargetKeepsBoth()
    {
        var c = DirectiveResolver.Resolve(D("F1", DirectiveAction.Advance, "E2", Anchor.AbsoluteWorld(new Vec2(0, 100))), Snap())!;
        Assert.Equal("E2", c.TargetFormationId);
        AssertPos(new Vec2(0, 100), c.Position);
    }

    [Fact]
    public void AdvanceWithoutAnchorTargetsNearestEnemy()
    {
        var c = DirectiveResolver.Resolve(D("F1", DirectiveAction.Advance), Snap())!;
        Assert.Equal("E1", c.TargetFormationId);
        Assert.Null(c.Position);
    }

    [Fact]
    public void AdvanceWithNothingToAdvanceOnStaysAdvanceWithoutTarget()
    {
        var c = DirectiveResolver.Resolve(D("F1", DirectiveAction.Advance), Snap(enemies: new List<FormationState>()))!;
        Assert.Equal(DirectiveAction.Advance, c.Action);
        Assert.Null(c.TargetFormationId);
        Assert.Null(c.Position);
    }

    [Fact]
    public void SkirmishDefaultsToNearestEnemy()
    {
        var c = DirectiveResolver.Resolve(D("F2", DirectiveAction.Skirmish), Snap())!;
        Assert.Equal("E1", c.TargetFormationId);
        Assert.Null(c.Position);
    }

    [Fact]
    public void RetreatWithoutAnchorFalls150mBackAlongMinusForward()
    {
        var c = DirectiveResolver.Resolve(D("F1", DirectiveAction.Retreat), Snap())!;
        AssertPos(new Vec2(0, -150), c.Position);
        Assert.Equal(150f, DirectiveResolver.RetreatDistance);
    }

    [Fact]
    public void RetreatFollowsRotatedFrame()
    {
        var frame = new BattleFrame(Vec2.Zero, new Vec2(1, 0)); // enemy to the east
        var own = new[] { Form("F1", FormationRole.Infantry, 100, 20, 30) };
        var c = DirectiveResolver.Resolve(D("F1", DirectiveAction.Retreat), Snap(own, frame: frame))!;
        AssertPos(new Vec2(-130, 30), c.Position);
    }

    [Fact]
    public void RetreatWithAnchorUsesAnchor()
    {
        var c = DirectiveResolver.Resolve(D("F1", DirectiveAction.Retreat, anchor: Anchor.AbsoluteWorld(new Vec2(40, -60))), Snap())!;
        AssertPos(new Vec2(40, -60), c.Position);
    }

    [Fact]
    public void FollowDefaultsToOwnInfantryExcludingSelf()
    {
        var c = DirectiveResolver.Resolve(D("F2", DirectiveAction.Follow), Snap())!;
        Assert.Equal("F1", c.TargetFormationId);
        AssertPos(new Vec2(0, -30), c.Position); // 30 m behind F1
    }

    [Fact]
    public void ScreenDefaultsToOwnInfantryAndStands40mAhead()
    {
        var c = DirectiveResolver.Resolve(D("F3", DirectiveAction.Screen), Snap())!;
        Assert.Equal("F1", c.TargetFormationId);
        AssertPos(new Vec2(0, 40), c.Position);
    }

    [Fact]
    public void ProtectFlankDefaultsToInfantryAndHasNoPosition()
    {
        var c = DirectiveResolver.Resolve(D("F3", DirectiveAction.ProtectFlank), Snap())!;
        Assert.Equal("F1", c.TargetFormationId);
        Assert.Equal(FlankSide.Left, c.Side);
        Assert.Null(c.Position);

        var right = DirectiveResolver.Resolve(D("F3", DirectiveAction.ProtectFlank, side: FlankSide.Right), Snap())!;
        Assert.Equal(FlankSide.Right, right.Side);
    }

    [Fact]
    public void FriendlyDefaultExcludesSelfAndFallsBackToLargest()
    {
        // F1 is the only infantry; as F1 itself the next-largest own formation (F2) is chosen.
        var c = DirectiveResolver.Resolve(D("F1", DirectiveAction.ProtectFlank), Snap())!;
        Assert.Equal("F2", c.TargetFormationId);
    }

    [Fact]
    public void FriendlyActionsCanTargetAllies()
    {
        var s = Snap(allies: new[] { Form("A1", FormationRole.Infantry, 60, 200, 0) });
        var c = DirectiveResolver.Resolve(D("F3", DirectiveAction.Follow, "ally:infantry"), s)!;
        Assert.Equal("A1", c.TargetFormationId);
        AssertPos(new Vec2(200, -30), c.Position);
    }

    [Fact]
    public void FriendlyActionWithEnemyTargetFallsBackToDefault()
    {
        var c = DirectiveResolver.Resolve(D("F3", DirectiveAction.Screen, "E1"), Snap())!;
        Assert.Equal("F1", c.TargetFormationId);
    }

    [Fact]
    public void FriendlyActionWithNobodyToTargetBecomesHold()
    {
        var s = Snap(new[] { Form("F1", FormationRole.Infantry, 10, 5, 6) });
        var c = DirectiveResolver.Resolve(D("F1", DirectiveAction.Follow), s)!;
        Assert.Equal(DirectiveAction.Hold, c.Action);
        AssertPos(new Vec2(5, 6), c.Position);
    }

    [Fact]
    public void FlankWaypointLeftAndRight()
    {
        var left = DirectiveResolver.Resolve(D("F3", DirectiveAction.Flank, "E1", side: FlankSide.Left), Snap())!;
        Assert.Equal(FlankSide.Left, left.Side);
        Assert.Equal("E1", left.TargetFormationId);
        AssertPos(new Vec2(-90, 330), left.Position);

        var right = DirectiveResolver.Resolve(D("F3", DirectiveAction.Flank, "E1", side: FlankSide.Right), Snap())!;
        AssertPos(new Vec2(90, 330), right.Position);

        var dflt = DirectiveResolver.Resolve(D("F3", DirectiveAction.Flank, "E1"), Snap())!;
        Assert.Equal(FlankSide.Left, dflt.Side);
    }

    [Fact]
    public void FlankWaypointUsesFrameAxes()
    {
        var frame = new BattleFrame(Vec2.Zero, new Vec2(1, 0)); // facing east: our left is north
        var wp = DirectiveResolver.FlankWaypoint(new Vec2(300, 0), FlankSide.Left, frame);
        AssertPos(new Vec2(330, 90), wp);
    }

    [Fact]
    public void FlankWithExplicitAnchorUsesAnchorNotWaypoint()
    {
        var c = DirectiveResolver.Resolve(D("F3", DirectiveAction.Flank, "E1", Anchor.AbsoluteWorld(new Vec2(-200, 200))), Snap())!;
        AssertPos(new Vec2(-200, 200), c.Position);
    }

    [Fact]
    public void SideOnlyAppliesToFlankActions()
    {
        var c = DirectiveResolver.Resolve(D("F1", DirectiveAction.Charge, side: FlankSide.Right), Snap())!;
        Assert.Null(c.Side);
    }

    [Fact]
    public void RelativeAnchorFollowsReferenceFormation()
    {
        var anchor = Anchor.Relative("F1", new Vec2(0, -25));
        var d = D("F2", DirectiveAction.Skirmish, anchor: anchor);

        AssertPos(new Vec2(0, -25), DirectiveResolver.Resolve(d, Snap())!.Position);

        var moved = OwnArmy().Select(f => f.Id == "F1" ? f.With(x: 10, y: 50) : f).ToList();
        AssertPos(new Vec2(10, 25), DirectiveResolver.Resolve(d, Snap(moved))!.Position);
    }

    [Fact]
    public void RelativeAnchorResolvesWithCurrentFrame()
    {
        var frame = new BattleFrame(Vec2.Zero, new Vec2(1, 0)); // east; right = south
        var own = new[] { Form("F1", FormationRole.Infantry, 100, 100, 0), Form("F2", FormationRole.Ranged, 50, 0, 0) };
        var anchor = Anchor.Relative("F1", new Vec2(20, -30)); // 20 to the right (south), 30 behind (west)
        var c = DirectiveResolver.Resolve(D("F2", DirectiveAction.Hold, anchor: anchor), Snap(own, frame: frame))!;
        AssertPos(new Vec2(70, -20), c.Position);
    }

    [Fact]
    public void DeadOrMissingRelativeReferenceGivesNullPosition()
    {
        var own = new[] { Form("F1", FormationRole.Infantry, 0, 0, 0, initial: 10), Form("F2", FormationRole.Ranged, 50, 0, -30) };
        var s = Snap(own);
        Assert.Null(DirectiveResolver.ResolveAnchor(Anchor.Relative("F1", new Vec2(0, -25)), s));
        Assert.Null(DirectiveResolver.ResolveAnchor(Anchor.Relative("F9", new Vec2(0, -25)), s));
        Assert.Null(DirectiveResolver.ResolveAnchor(null, s));

        var skirmish = DirectiveResolver.Resolve(D("F2", DirectiveAction.Skirmish, anchor: Anchor.Relative("F1", new Vec2(0, -25))), s)!;
        Assert.Null(skirmish.Position);
        // Hold falls back to staying put.
        var hold = DirectiveResolver.Resolve(D("F2", DirectiveAction.Hold, anchor: Anchor.Relative("F1", new Vec2(0, -25))), s)!;
        AssertPos(new Vec2(0, -30), hold.Position);
    }

    [Fact]
    public void PositionsAreClampedToBounds()
    {
        var bounds = new MapBounds(new Vec2(-100, -100), new Vec2(100, 100));
        var s = Snap(bounds: bounds);
        var c = DirectiveResolver.Resolve(D("F1", DirectiveAction.Advance, anchor: Anchor.AbsoluteWorld(new Vec2(500, 500))), s)!;
        AssertPos(new Vec2(95, 95), c.Position);
        var retreat = DirectiveResolver.Resolve(D("F1", DirectiveAction.Retreat), s)!; // (0,-150) -> (0,-95)
        AssertPos(new Vec2(0, -95), retreat.Position);
    }

    [Fact]
    public void DeadForeignOrUnknownFormationGivesNull()
    {
        var own = new[] { Form("F1", FormationRole.Infantry, 0, 0, 0, initial: 10), Form("F2", FormationRole.Ranged, 50, 0, -30) };
        var s = Snap(own, allies: new[] { Form("A1", FormationRole.Infantry, 10, 200, 0) });
        Assert.Null(DirectiveResolver.Resolve(D("F1", DirectiveAction.Hold), s));
        Assert.Null(DirectiveResolver.Resolve(D("E1", DirectiveAction.Hold), s));
        Assert.Null(DirectiveResolver.Resolve(D("A1", DirectiveAction.Hold), s));
        Assert.Null(DirectiveResolver.Resolve(D("F99", DirectiveAction.Hold), s));
        Assert.NotNull(DirectiveResolver.Resolve(D("f2", DirectiveAction.Hold), s));
    }

    [Theory]
    [MemberData(nameof(AllActions))]
    public void EveryActionResolvesAndKeepsItsKind(DirectiveAction action)
    {
        var c = DirectiveResolver.Resolve(D("F3", action), Snap());
        Assert.NotNull(c);
        Assert.Equal(action, c!.Action);
        Assert.Equal("F3", c.FormationId);
    }

    public static IEnumerable<object[]> AllActions() => Enum.GetValues<DirectiveAction>().Select(a => new object[] { a });

    [Fact]
    public void ActionClassification()
    {
        Assert.True(DirectiveResolver.IsAttackAction(DirectiveAction.Charge));
        Assert.True(DirectiveResolver.IsAttackAction(DirectiveAction.Advance));
        Assert.False(DirectiveResolver.IsAttackAction(DirectiveAction.Hold));
        Assert.True(DirectiveResolver.IsFriendlyTargetAction(DirectiveAction.Follow));
        Assert.False(DirectiveResolver.IsFriendlyTargetAction(DirectiveAction.Charge));
    }
}

public class FormationCommandTests
{
    private static FormationCommand Cmd(DirectiveAction a = DirectiveAction.Hold, string? target = null, Vec2? pos = null,
        FlankSide? side = null, Arrangement? arr = null, FireOrder? fire = null)
        => new() { FormationId = "F1", Action = a, TargetFormationId = target, Position = pos, Side = side, Arrangement = arr, Fire = fire };

    [Fact]
    public void IdenticalCommandsDoNotDiffer() =>
        Assert.False(Cmd(pos: new Vec2(1, 1)).DiffersMeaningfully(Cmd(pos: new Vec2(1, 1))));

    [Fact]
    public void FieldChangesDiffer()
    {
        var a = Cmd();
        Assert.True(a.DiffersMeaningfully(Cmd(DirectiveAction.Charge)));
        Assert.True(a.DiffersMeaningfully(Cmd(target: "E1")));
        Assert.True(a.DiffersMeaningfully(Cmd(side: FlankSide.Left)));
        Assert.True(a.DiffersMeaningfully(Cmd(arr: Arrangement.Line)));
        Assert.True(a.DiffersMeaningfully(Cmd(fire: FireOrder.Hold)));
        Assert.True(a.DiffersMeaningfully(Cmd(pos: new Vec2(0, 0))));
        Assert.True(Cmd(pos: new Vec2(0, 0)).DiffersMeaningfully(a));
    }

    [Fact]
    public void TargetComparisonIgnoresCase() =>
        Assert.False(Cmd(target: "e1").DiffersMeaningfully(Cmd(target: "E1")));

    [Fact]
    public void PositionToleranceIsInclusiveAtEightMetres()
    {
        var a = Cmd(pos: new Vec2(0, 0));
        Assert.False(a.DiffersMeaningfully(Cmd(pos: new Vec2(5, 0))));
        Assert.False(a.DiffersMeaningfully(Cmd(pos: new Vec2(8, 0))));
        Assert.True(a.DiffersMeaningfully(Cmd(pos: new Vec2(8.5f, 0))));
        Assert.True(a.DiffersMeaningfully(Cmd(pos: new Vec2(6, 6)))); // 8.49
    }

    [Fact]
    public void CustomTolerance()
    {
        var a = Cmd(pos: new Vec2(0, 0));
        Assert.True(a.DiffersMeaningfully(Cmd(pos: new Vec2(5, 0)), 2f));
        Assert.False(a.DiffersMeaningfully(Cmd(pos: new Vec2(15, 0)), 20f));
    }

    [Fact]
    public void ToStringDescribesCommand()
    {
        var s = Cmd(DirectiveAction.Flank, "E2", new Vec2(10, 20), FlankSide.Right, Arrangement.Loose, FireOrder.Hold).ToString();
        Assert.Contains("F1: flank", s);
        Assert.Contains("-> E2", s);
        Assert.Contains("(right)", s);
        Assert.Contains("@(10,20)", s);
        Assert.Contains("[loose]", s);
        Assert.Contains("fire:hold", s);
    }
}
