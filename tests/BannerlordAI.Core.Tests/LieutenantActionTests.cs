using BannerlordAI.Core.Model;
using BannerlordAI.Core.Planning;

namespace BannerlordAI.Core.Tests;

public class LieutenantActionTests
{
    private static BattleSnapshot Snap() => SnapshotFactory.Simple(
        new[]
        {
            new FormationState { Id = "F1", Allegiance = Allegiance.Own, Role = FormationRole.Infantry, Count = 100, InitialCount = 100, Position = new Vec2(0, 0) },
        },
        new[]
        {
            new FormationState { Id = "E1", Allegiance = Allegiance.Enemy, Role = FormationRole.Infantry, Count = 100, InitialCount = 100, Position = new Vec2(0, 300) },
        });

    private static Plan PlanWithCharge() => new()
    {
        Version = 1,
        Directives = { ["F1"] = new Directive { FormationId = "F1", Action = DirectiveAction.Charge, Target = "E1" } },
    };

    [Fact]
    public void Unknown_action_in_adjustment_keeps_current_action()
    {
        const string json = """
        {"fire_contingencies":[],"adjustments":[{"formation":"F1","action":"bananas","target":null,"side":null,"anchor":null,"arrangement":"shieldwall","fire":null}],
         "wake_commander":false,"reason":"","order_log":null}
        """;
        var outcome = PlanParser.ParseLieutenant(json, Snap(), PlanWithCharge());
        Assert.True(outcome.Ok);
        var patch = Assert.Single(outcome.Value!.Adjustments);
        Assert.Equal(DirectiveAction.Charge, patch.Action);
        Assert.Equal(Arrangement.ShieldWall, patch.Arrangement);
    }

    [Fact]
    public void Known_action_in_adjustment_is_passed_through()
    {
        const string json = """
        {"fire_contingencies":[],"adjustments":[{"formation":"F1","action":"hold","target":null,"side":null,"anchor":null,"arrangement":null,"fire":null}],
         "wake_commander":false,"reason":"","order_log":null}
        """;
        var outcome = PlanParser.ParseLieutenant(json, Snap(), PlanWithCharge());
        Assert.Equal(DirectiveAction.Hold, Assert.Single(outcome.Value!.Adjustments).Action);
    }
}
