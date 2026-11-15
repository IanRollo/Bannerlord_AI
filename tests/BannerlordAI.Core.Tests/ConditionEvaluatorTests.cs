using BannerlordAI.Core.Model;
using BannerlordAI.Core.Planning;
using static BannerlordAI.Core.Tests.TestData;

namespace BannerlordAI.Core.Tests;

public class ConditionEvaluatorTests
{
    private static Condition C(ConditionType t, string? subject = null, string? other = null, double value = 0)
        => new() { Type = t, Subject = subject, Other = other, Value = value };

    private static bool Met(Condition c, BattleSnapshot s, double issued = 0) => ConditionEvaluator.IsMet(c, s, issued);

    [Fact]
    public void EnemyWithinUsesDistanceInclusive()
    {
        var s = Snap(); // F1 (0,0) vs E1 (0,300)
        Assert.True(Met(C(ConditionType.EnemyWithin, "F1", null, 300), s));
        Assert.True(Met(C(ConditionType.EnemyWithin, "F1", null, 310), s));
        Assert.False(Met(C(ConditionType.EnemyWithin, "F1", null, 299), s));
    }

    [Fact]
    public void EnemyWithinHonoursOtherSelector()
    {
        var s = Snap(); // F1 to E3 (120,300) = 323.1
        Assert.False(Met(C(ConditionType.EnemyWithin, "F1", "enemy:cavalry", 320), s));
        Assert.True(Met(C(ConditionType.EnemyWithin, "F1", "enemy:cavalry", 330), s));
    }

    [Fact]
    public void EnemyWithinOnlyCountsEnemies()
    {
        var s = Snap(); // F2 is 30 m from F1, but it is not an enemy
        Assert.False(Met(C(ConditionType.EnemyWithin, "F1", "F2", 100), s));
        Assert.False(Met(C(ConditionType.EnemyWithin, "F1", "own:any", 100), s));
    }

    [Fact]
    public void EnemyWithinSubjectSelectorAndDeadFormations()
    {
        var own = new[] { Form("F1", FormationRole.Infantry, 50, 0, 0), Form("F2", FormationRole.Ranged, 50, 0, 250) };
        var enemies = new[] { Form("E1", FormationRole.Cavalry, 10, 0, 300), Form("E2", FormationRole.Infantry, 0, 0, 255, initial: 5) };
        var s = Snap(own, enemies);
        Assert.True(Met(C(ConditionType.EnemyWithin, "own:ranged", "enemy:cavalry", 60), s));
        Assert.False(Met(C(ConditionType.EnemyWithin, "own:infantry", "enemy:cavalry", 60), s));
        Assert.False(Met(C(ConditionType.EnemyWithin, "F2", "E2", 60), s)); // dead enemy ignored
    }

    [Fact]
    public void StrengthBelow()
    {
        var own = new[] { Form("F1", FormationRole.Infantry, 40, 0, 0, initial: 100) };
        var s = Snap(own);
        Assert.True(Met(C(ConditionType.StrengthBelow, "F1", null, 50), s));
        Assert.False(Met(C(ConditionType.StrengthBelow, "F1", null, 40), s)); // strictly below
        Assert.False(Met(C(ConditionType.StrengthBelow, "F1", null, 30), s));
    }

    [Fact]
    public void StrengthBelowAggregatesAndCountsDeadStartingMen()
    {
        var own = new[]
        {
            Form("F1", FormationRole.Infantry, 40, 0, 0, initial: 100),
            Form("F4", FormationRole.Infantry, 100, 0, 0, initial: 100),
            Form("F5", FormationRole.Infantry, 0, 0, 0, initial: 100),
        };
        var s = Snap(own);
        // (40 + 100 + 0) / 300 = 46.7%
        Assert.True(Met(C(ConditionType.StrengthBelow, "own:infantry", null, 50), s));
        Assert.False(Met(C(ConditionType.StrengthBelow, "own:infantry", null, 46), s));
    }

    [Fact]
    public void StrengthBelowOfEverythingGoneIsZero()
    {
        var own = new[] { Form("F1", FormationRole.Infantry, 0, 0, 0, initial: 100) };
        Assert.True(Met(C(ConditionType.StrengthBelow, "F1", null, 1), Snap(own)));
    }

    [Fact]
    public void StrengthBelowWithNoMatchingFormationsCountsAsZero()
    {
        var s = Snap(); // no allies, no skirmishers
        Assert.True(Met(C(ConditionType.StrengthBelow, "ally:any", null, 10), s));
        Assert.True(Met(C(ConditionType.StrengthBelow, "own:skirmisher", null, 1), s));
        Assert.False(Met(C(ConditionType.StrengthBelow, "ally:any", null, 0), s));
    }

    [Fact]
    public void EngagedAndRouting()
    {
        var own = new[]
        {
            Form("F1", FormationRole.Infantry, 50, 0, 0, engaged: true),
            Form("F2", FormationRole.Ranged, 50, 0, -30, routing: true),
            Form("F3", FormationRole.Cavalry, 0, 0, 0, initial: 10, engaged: true, routing: true),
        };
        var s = Snap(own);
        Assert.True(Met(C(ConditionType.Engaged, "F1"), s));
        Assert.False(Met(C(ConditionType.Engaged, "F2"), s));
        Assert.True(Met(C(ConditionType.Engaged, "own:any"), s));
        Assert.True(Met(C(ConditionType.Routing, "F2"), s));
        Assert.False(Met(C(ConditionType.Routing, "F1"), s));
        // dead formations never count
        Assert.False(Met(C(ConditionType.Engaged, "own:cavalry"), s));
        Assert.False(Met(C(ConditionType.Routing, "own:cavalry"), s));
    }

    [Fact]
    public void EngagedCanWatchEnemyFormations()
    {
        var enemies = new[] { Form("E1", FormationRole.Infantry, 10, 0, 300, engaged: true) };
        Assert.True(Met(C(ConditionType.Engaged, "enemy:any"), Snap(OwnArmy(), enemies)));
    }

    [Fact]
    public void TimeElapsedIsRelativeToPlanIssueTime()
    {
        var s = Snap(time: 100);
        Assert.True(Met(C(ConditionType.TimeElapsed, value: 30), s, issued: 70));
        Assert.True(Met(C(ConditionType.TimeElapsed, value: 30), s, issued: 60));
        Assert.False(Met(C(ConditionType.TimeElapsed, value: 31), s, issued: 70));
        Assert.True(Met(C(ConditionType.TimeElapsed, value: 100), s, issued: 0));
    }

    [Fact]
    public void StrengthRatioIncludesAlliesAndIgnoresZeroEnemies()
    {
        var own = new[] { Form("F1", FormationRole.Infantry, 100, 0, 0) };
        var enemies = new[] { Form("E1", FormationRole.Infantry, 200, 0, 300) };
        var s = Snap(own, enemies); // 0.5
        Assert.True(Met(C(ConditionType.StrengthRatioBelow, value: 0.6), s));
        Assert.False(Met(C(ConditionType.StrengthRatioBelow, value: 0.5), s));

        var withAllies = Snap(own, enemies, allies: new[] { Form("A1", FormationRole.Infantry, 100, 100, 0) }); // 1.0
        Assert.False(Met(C(ConditionType.StrengthRatioBelow, value: 0.6), withAllies));
        Assert.True(Met(C(ConditionType.StrengthRatioBelow, value: 1.1), withAllies));

        var noEnemies = Snap(own, new List<FormationState>());
        Assert.False(Met(C(ConditionType.StrengthRatioBelow, value: 100), noEnemies));
    }

    [Fact]
    public void ContingencyMatchAllVersusAny()
    {
        var s = Snap(OwnArmy().Select(f => f.Id == "F1" ? f.With(engaged: true) : f).ToList());
        var yes = C(ConditionType.Engaged, "F1");
        var no = C(ConditionType.Routing, "F1");

        var all = new Contingency { Id = "C", MatchAll = true, Conditions = new[] { yes, no } };
        var any = new Contingency { Id = "C", MatchAll = false, Conditions = new[] { yes, no } };
        Assert.False(ConditionEvaluator.IsMet(all, s, 0));
        Assert.True(ConditionEvaluator.IsMet(any, s, 0));

        var allYes = new Contingency { Id = "C", MatchAll = true, Conditions = new[] { yes, yes } };
        Assert.True(ConditionEvaluator.IsMet(allYes, s, 0));
        var anyNo = new Contingency { Id = "C", MatchAll = false, Conditions = new[] { no, no } };
        Assert.False(ConditionEvaluator.IsMet(anyNo, s, 0));
    }

    [Fact]
    public void ContingencyWithoutConditionsNeverFires()
    {
        Assert.False(ConditionEvaluator.IsMet(new Contingency { Id = "C", MatchAll = true }, Snap(), 0));
        Assert.False(ConditionEvaluator.IsMet(new Contingency { Id = "C", MatchAll = false }, Snap(), 0));
    }
}
