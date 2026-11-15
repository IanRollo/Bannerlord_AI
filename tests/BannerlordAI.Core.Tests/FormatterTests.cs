using BannerlordAI.Core.Compaction;
using BannerlordAI.Core.Model;
using BannerlordAI.Core.Orchestration;
using BannerlordAI.Core.Planning;
using static BannerlordAI.Core.Tests.TestData;

namespace BannerlordAI.Core.Tests;

public class SnapshotFormatterTests
{
    [Fact]
    public void ContainsHeaderFrameIdsAndRoles()
    {
        var text = SnapshotFormatter.Format(Snap(time: 125));
        Assert.Contains("BATTLE t=02:05", text);
        Assert.Contains("type=field", text);
        Assert.Contains("you=defender", text);
        Assert.Contains("yours 190 vs enemy 190", text);
        Assert.Contains("Frame: metres; (0,0) = your army centre now; +y = toward enemy centre; +x = your right.", text);
        Assert.Contains("YOUR FORMATIONS", text);
        Assert.Contains("ENEMY FORMATIONS", text);
        foreach (var id in new[] { "F1", "F2", "F3", "E1", "E2", "E3" }) Assert.Contains(id, text);
        foreach (var role in new[] { "infantry", "ranged", "cavalry" }) Assert.Contains(role, text);
        Assert.Contains("(-80,-10)", text); // F3 in local metres
        Assert.Contains("100/100", text);
        Assert.DoesNotContain("ALLIED", text);
    }

    [Fact]
    public void ReportsPositionsInLocalFrame()
    {
        var frame = new BattleFrame(new Vec2(100, 100), new Vec2(1, 0)); // east
        var own = new[] { Form("F1", FormationRole.Infantry, 10, 150, 100) }; // 50 m ahead
        var text = SnapshotFormatter.Format(Snap(own, frame: frame));
        Assert.Contains("(0,50)", text);
    }

    [Fact]
    public void ShowsStateFlagsAndNearestFoe()
    {
        var own = new[] { Form("F1", FormationRole.Infantry, 50, 0, 0, engaged: true, routing: true, activity: "Charging", morale: 0.6f) };
        var text = SnapshotFormatter.Format(Snap(own));
        Assert.Contains("ROUTING", text);
        Assert.Contains("in melee", text);
        Assert.Contains("Charging", text);
        Assert.Contains("60%", text);
        Assert.Contains("E1 300m", text);
    }

    [Fact]
    public void DestroyedFormationsAreMarked()
    {
        var own = new[] { Form("F1", FormationRole.Infantry, 0, 0, 0, initial: 80) };
        var text = SnapshotFormatter.Format(Snap(own));
        Assert.Contains("destroyed", text);
        Assert.Contains("0/80", text);
    }

    [Fact]
    public void AlliesAndEmptySidesAreShown()
    {
        var text = SnapshotFormatter.Format(Snap(enemies: new List<FormationState>(), allies: new[] { Form("A1", FormationRole.Infantry, 60, 200, 0) }));
        Assert.Contains("ALLIED FORMATIONS", text);
        Assert.Contains("A1", text);
        Assert.Contains("+ allies 60", text);
        Assert.Contains("(none)", text);
    }

    [Fact]
    public void TerrainBlockOnlyWhenPresentAndRequested()
    {
        var own = new[] { Form("F1", FormationRole.Infantry, 100, 0, 0) };
        var enemy = new[] { Form("E1", FormationRole.Infantry, 100, 0, 300) };
        var withTerrain = SnapshotFactory.Create(1, 0, BattlePhase.Battle, BattleType.Field, false, own, Array.Empty<FormationState>(), enemy,
            p => p.Y * 0.1f, new MapBounds(new Vec2(-1000, -1000), new Vec2(1000, 200)),
            new TerrainOptions { Columns = 3, Rows = 3, HalfWidth = 100, DepthMargin = 50 });

        var text = SnapshotFormatter.Format(withTerrain);
        Assert.Contains("TERRAIN", text);
        Assert.Contains("+15", text); // row y=150
        Assert.Contains("..", text);  // row y=350 is off the map
        Assert.Contains("-5", text);  // row y=-50

        Assert.DoesNotContain("TERRAIN", SnapshotFormatter.Format(withTerrain, includeTerrain: false));
        Assert.DoesNotContain("TERRAIN", SnapshotFormatter.Format(Snap()));
    }

    [Theory]
    [InlineData(0f, 1f, "ahead")]
    [InlineData(1f, 1f, "ahead-right")]
    [InlineData(1f, 0f, "right")]
    [InlineData(1f, -1f, "back-right")]
    [InlineData(0f, -1f, "back")]
    [InlineData(-1f, -1f, "back-left")]
    [InlineData(-1f, 0f, "left")]
    [InlineData(-1f, 1f, "ahead-left")]
    [InlineData(0f, 0f, "-")]
    public void DirectionLabel(float x, float y, string expected) =>
        Assert.Equal(expected, SnapshotFormatter.DirectionLabel(new Vec2(x, y)));
}

public class PlanFormatterTests
{
    private static Plan MakePlan(BattleSnapshot? basis = null) => PlanParser.ParseCommander(CommanderJson, basis ?? Snap(), 3, 75).Value!;

    [Fact]
    public void NullPlan() => Assert.Contains("none yet", PlanFormatter.Format(null, Snap()));

    [Fact]
    public void DescribesIntentDirectivesContingenciesAndWakeConditions()
    {
        var snap = Snap();
        var text = PlanFormatter.Format(MakePlan(snap), snap);
        Assert.Contains("CURRENT PLAN v3 (issued 01:15, stance defensive)", text);
        Assert.Contains("Intent: Hold the line, shoot, then hit with cavalry.", text);
        Assert.Contains("F1: hold at (0,40) [shieldwall]", text);
        Assert.Contains("F2: skirmish target enemy:infantry at (0,-25) from F1 fire at_will", text);
        Assert.Contains("F3: reserve at (-120,0)", text);
        Assert.Contains("C1 [armed] Cavalry strikes the archers", text);
        Assert.Contains("when all: F1 engaged", text);
        Assert.Contains("F3: charge target enemy:ranged (from contingency C1)", text);
        Assert.Contains("F1 below 50%", text);
    }

    [Fact]
    public void FiredContingencyAndMissingDirective()
    {
        var snap = Snap();
        var plan = MakePlan(snap);
        plan.Contingencies[0].FiredAt = 90;
        plan.Directives.Remove("F3");
        var text = PlanFormatter.Format(plan, snap);
        Assert.Contains("C1 [fired 01:30]", text);
        Assert.Contains("F3: (no directive)", text);
    }

    [Fact]
    public void AbsoluteAnchorsAreShownInTheCurrentFrame()
    {
        var basis = Snap();
        var plan = MakePlan(basis);
        var rotated = Snap(frame: new BattleFrame(new Vec2(0, 0), new Vec2(1, 0)));
        // World (0,40) seen from a frame facing east is 40 m to the left.
        Assert.Contains("F1: hold at (-40,0)", PlanFormatter.Format(plan, rotated));
    }

    [Fact]
    public void DescribeConditionCoversEveryType()
    {
        foreach (var t in Enum.GetValues<ConditionType>())
        {
            var s = PlanFormatter.DescribeCondition(new Condition { Type = t, Subject = "F1", Other = "enemy:cavalry", Value = 60 });
            Assert.False(string.IsNullOrWhiteSpace(s));
        }
        Assert.Equal("enemy:cavalry within 60m of F2", PlanFormatter.DescribeCondition(new Condition { Type = ConditionType.EnemyWithin, Subject = "F2", Other = "enemy:cavalry", Value = 60 }));
        Assert.Equal("enemy:any within 60m of F2", PlanFormatter.DescribeCondition(new Condition { Type = ConditionType.EnemyWithin, Subject = "F2", Value = 60 }));
        Assert.Equal("our/enemy men below 0.7", PlanFormatter.DescribeCondition(new Condition { Type = ConditionType.StrengthRatioBelow, Value = 0.7 }));
    }
}

public class PromptTests
{
    [Fact]
    public void SystemPromptsMentionEveryActionAndCondition()
    {
        Assert.False(string.IsNullOrWhiteSpace(Prompts.Commander));
        Assert.False(string.IsNullOrWhiteSpace(Prompts.Lieutenant));
        foreach (var prompt in new[] { Prompts.Commander, Prompts.Lieutenant })
        {
            foreach (var a in Enum.GetValues<DirectiveAction>()) Assert.Contains(Wire.ToWire(a), prompt);
            foreach (var c in Enum.GetValues<ConditionType>()) Assert.Contains(Wire.ToWire(c), prompt);
            foreach (var r in Enum.GetValues<FormationRole>()) Assert.Contains(Roles.ToWire(r), prompt);
            foreach (var a in Enum.GetValues<Arrangement>()) Assert.Contains(Wire.ToWire(a), prompt);
        }
    }

    [Fact]
    public void CommanderUserContentWithoutPlan()
    {
        var s = Prompts.CommanderUserContent("battle start", Snap(), null, Array.Empty<BattleEvent>(), true);
        Assert.Contains("REASON FOR THIS REPORT: battle start", s);
        Assert.Contains("EVENTS SO FAR", s);
        Assert.Contains("(nothing notable)", s);
        Assert.Contains("none yet", s);
        Assert.Contains("Issue your battle plan.", s);
    }

    [Fact]
    public void CommanderUserContentWithPlanAndEvents()
    {
        var snap = Snap();
        var plan = PlanParser.ParseCommander(CommanderJson, snap, 1, 0).Value!;
        var events = new[] { new BattleEvent { Time = 20, Text = "later" }, new BattleEvent { Time = 10, Text = "earlier" } };
        var s = Prompts.CommanderUserContent("scheduled review", snap, plan, events, true);
        Assert.Contains("EVENTS SINCE YOUR LAST ORDERS", s);
        Assert.True(s.IndexOf("earlier", StringComparison.Ordinal) < s.IndexOf("later", StringComparison.Ordinal));
        Assert.Contains("CURRENT PLAN v1", s);
        Assert.Contains("Review the situation", s);
    }

    [Fact]
    public void LieutenantUserContentOmitsTerrain()
    {
        var own = new[] { Form("F1", FormationRole.Infantry, 100, 0, 0) };
        var enemy = new[] { Form("E1", FormationRole.Infantry, 100, 0, 300) };
        var snap = SnapshotFactory.Create(1, 0, BattlePhase.Battle, BattleType.Field, false, own, Array.Empty<FormationState>(), enemy, _ => 0f);
        var plan = PlanParser.ParseCommander(Commander(directivesJson: """{"formation":"F1","action":"hold"}"""), snap, 1, 0).Value!;
        Assert.Contains("TERRAIN", Prompts.CommanderUserContent("r", snap, plan, Array.Empty<BattleEvent>(), true));
        var lt = Prompts.LieutenantUserContent(snap, plan, Array.Empty<BattleEvent>());
        Assert.DoesNotContain("TERRAIN", lt);
        Assert.Contains("EVENTS SINCE YOUR LAST CHECK", lt);
        Assert.Contains("Decide whether anything needs to change", lt);
    }
}
