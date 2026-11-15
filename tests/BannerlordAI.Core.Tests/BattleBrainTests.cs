using BannerlordAI.Core.Llm;
using BannerlordAI.Core.Model;
using BannerlordAI.Core.Orchestration;
using BannerlordAI.Core.Planning;
using static BannerlordAI.Core.Tests.BrainRig;
using static BannerlordAI.Core.Tests.TestData;

namespace BannerlordAI.Core.Tests;

public class BattleBrainBasicsTests
{
    private static FormationState Engage(FormationState f) => f.Id == "F1" ? f.With(engaged: true) : f;

    [Fact]
    public void NoCallsDuringDeploymentOrAfterEnd()
    {
        using var rig = new BrainRig(lieutenant: true);
        foreach (var t in new[] { 0.0, 5, 50, 500 })
        {
            var r = rig.Tick(S(t, phase: BattlePhase.Deployment));
            Assert.Equal(BrainMode.Vanilla, r.Mode);
            Assert.Empty(r.Changed);
            Assert.Empty(r.Current);
        }
        Assert.Empty(rig.Cmd.Requests);
        Assert.Empty(rig.Lt.Requests);
        Assert.False(rig.Brain.CommanderBusy);
        Assert.Null(rig.Brain.CurrentPlan);

        rig.Tick(S(600, phase: BattlePhase.Ended));
        Assert.Empty(rig.Cmd.Requests);
    }

    [Fact]
    public void FirstBattleTickStartsCommanderCallWithBattleStartReason()
    {
        using var rig = new BrainRig();
        var r = rig.Tick(S(1), settle: false);
        Assert.True(rig.Brain.CommanderBusy);
        Assert.Equal(BrainMode.Vanilla, r.Mode);
        Assert.Empty(r.Changed);
        rig.Settle();

        var req = Assert.Single(rig.Cmd.Requests);
        Assert.Equal(PlanSchemas.CommanderName, req.SchemaName);
        Assert.Equal(PlanSchemas.Commander, req.SchemaJson);
        Assert.Equal(Prompts.Commander, req.SystemPrompt);
        Assert.Equal(rig.Config.Commander.Model, req.Model);
        Assert.Contains("REASON FOR THIS REPORT: battle start", req.UserContent);
        Assert.Contains("F1", req.UserContent);
        Assert.Contains("E1", req.UserContent);
    }

    [Fact]
    public void NoSecondCallWhileOneIsInFlight()
    {
        using var rig = new BrainRig();
        rig.Tick(S(1), settle: false);
        rig.Tick(S(2), settle: false);
        rig.Tick(S(3), settle: false);
        rig.Settle();
        Assert.Single(rig.Cmd.Requests);
    }

    [Fact]
    public void CompletedCallInstallsPlanOnNextTick()
    {
        using var rig = new BrainRig();
        rig.Tick(S(1));
        Assert.Equal(BrainMode.Vanilla, rig.Brain.Mode);
        Assert.Null(rig.Brain.CurrentPlan);

        var r = rig.Tick(S(2));
        Assert.Equal(BrainMode.Llm, r.Mode);
        Assert.Contains("Commander: Hold the line!", r.Messages);
        Assert.Equal(new[] { "F1", "F2", "F3" }, r.Changed.Select(c => c.FormationId).OrderBy(x => x).ToArray());
        Assert.Equal(1, rig.Brain.Stats.PlansInstalled);
        Assert.Equal(1, rig.Brain.Stats.Commander.Calls);
        Assert.Equal(0, rig.Brain.Stats.Commander.Failures);
        Assert.True(rig.Brain.Stats.TotalTokens > 0);
        Assert.Equal(1, rig.Brain.CurrentPlan!.Version);

        var f1 = r.Changed.Single(c => c.FormationId == "F1");
        Assert.Equal(DirectiveAction.Hold, f1.Action);
        Assert.Equal(Arrangement.ShieldWall, f1.Arrangement);
        Assert.Equal(new Vec2(0, 40), f1.Position);
        var f2 = r.Changed.Single(c => c.FormationId == "F2");
        Assert.Equal(DirectiveAction.Skirmish, f2.Action);
        Assert.Equal("E1", f2.TargetFormationId);
        Assert.Equal(new Vec2(0, -25), f2.Position);
        var f3 = r.Changed.Single(c => c.FormationId == "F3");
        Assert.Equal(DirectiveAction.Reserve, f3.Action);
        Assert.Equal(new Vec2(-120, 0), f3.Position);
        Assert.Equal(3, r.Current.Count);
    }

    [Fact]
    public void MessageFallsBackToIntentWhenOrderLogEmpty()
    {
        using var rig = new BrainRig();
        rig.CommanderReply = _ => Commander(orderLog: "");
        rig.Tick(S(1));
        var r = rig.Tick(S(2));
        Assert.Contains("Commander: test intent", r.Messages);
    }

    [Fact]
    public void UnchangedStateYieldsNoChangedCommandsAndStableInstances()
    {
        using var rig = new BrainRig();
        rig.Tick(S(1));
        var first = rig.Tick(S(2));
        var second = rig.Tick(S(3));
        Assert.Empty(second.Changed);
        Assert.Equal(3, second.Current.Count);
        foreach (var id in new[] { "F1", "F2", "F3" })
            Assert.Same(first.Current[id], second.Current[id]);
    }

    [Fact]
    public void CurrentKeepsEmittedInstanceUntilMovementExceedsTolerance()
    {
        using var rig = new BrainRig();
        rig.Tick(S(1));
        var installed = rig.Tick(S(2));
        var f2 = installed.Current["F2"]; // relative to F1, 25 m behind

        // F1 moves 5 m: F2's desired position shifts by 5 m, under the 8 m tolerance.
        var small = rig.Tick(S(3, ownEdit: f => f.Id == "F1" ? f.With(y: 5) : f));
        Assert.Empty(small.Changed);
        Assert.Same(f2, small.Current["F2"]);
        Assert.Equal(new Vec2(0, -25), small.Current["F2"].Position); // lags the true -20

        // Another 15 m: now 20 m from what the game was last told.
        var big = rig.Tick(S(4, ownEdit: f => f.Id == "F1" ? f.With(y: 20) : f));
        var changed = Assert.Single(big.Changed);
        Assert.Equal("F2", changed.FormationId);
        Assert.NotSame(f2, big.Current["F2"]);
        Assert.Same(changed, big.Current["F2"]);
        Assert.Equal(-5f, changed.Position!.Value.Y, 2);
    }

    [Fact]
    public void ToleranceIsConfigurable()
    {
        using var rig = new BrainRig(c => c.CommandPositionTolerance = 1f);
        rig.Tick(S(1));
        rig.Tick(S(2));
        var r = rig.Tick(S(3, ownEdit: f => f.Id == "F1" ? f.With(y: 5) : f));
        Assert.Single(r.Changed, c => c.FormationId == "F2");
    }

    [Fact]
    public void HoldWithoutAnchorIsPinnedToPositionAtInstallTime()
    {
        using var rig = new BrainRig();
        rig.CommanderReply = _ => Commander(); // no anchors anywhere
        rig.Tick(S(1));
        var installed = rig.Tick(S(2, ownEdit: f => f.Id == "F1" ? f.With(y: 5) : f));
        Assert.Equal(new Vec2(0, 5), installed.Current["F1"].Position);
        var anchor = rig.Brain.CurrentPlan!.Directives["F1"].Anchor!;
        Assert.False(anchor.IsRelative);
        Assert.Equal(new Vec2(0, 5), anchor.Value);
        Assert.Equal(new Vec2(-80, -10), installed.Current["F3"].Position); // reserve pinned too
        Assert.Null(rig.Brain.CurrentPlan.Directives["F2"].Anchor); // skirmish is not pinned

        // The formation drifts; the command stays where it was pinned.
        var later = rig.Tick(S(3, ownEdit: f => f.Id == "F1" ? f.With(y: 60) : f));
        Assert.Equal(new Vec2(0, 5), later.Current["F1"].Position);
        Assert.DoesNotContain(later.Changed, c => c.FormationId == "F1");
    }

    [Fact]
    public void ScheduledReplanAfterCommanderInterval()
    {
        using var rig = new BrainRig();
        rig.Tick(S(1));
        rig.Tick(S(2));
        rig.Tick(S(30));
        Assert.Single(rig.Cmd.Requests); // 29 s since the last call

        rig.Tick(S(31), settle: false);
        Assert.True(rig.Brain.CommanderBusy);
        rig.Settle();
        Assert.Equal(2, rig.Cmd.Requests.Count);
        Assert.Contains("scheduled review", rig.Cmd.Requests[1].UserContent);
        Assert.Contains("CURRENT PLAN v1", rig.Cmd.Requests[1].UserContent);

        var r = rig.Tick(S(32));
        Assert.Equal(2, rig.Brain.CurrentPlan!.Version);
        Assert.Equal(2, rig.Brain.Stats.PlansInstalled);
        Assert.Contains("Commander: Hold the line!", r.Messages);
    }

    [Fact]
    public void MajorEventWakesCommanderEarlyButRespectsCooldown()
    {
        using var rig = new BrainRig();
        rig.CommanderReply = _ => Commander();
        rig.Tick(S(1));
        rig.Tick(S(2));

        rig.Tick(S(5, ownEdit: Engage)); // first contact (Major) but only 4 s since the last call
        Assert.Single(rig.Cmd.Requests);
        rig.Tick(S(10.5, ownEdit: Engage));
        Assert.Single(rig.Cmd.Requests);

        rig.Tick(S(11, ownEdit: Engage), settle: false);
        Assert.True(rig.Brain.CommanderBusy);
        rig.Settle();
        Assert.Equal(2, rig.Cmd.Requests.Count);
        Assert.Contains("FIRST CONTACT", rig.Cmd.Requests[1].UserContent);
        Assert.DoesNotContain("scheduled review", rig.Cmd.Requests[1].UserContent.Split('\n')[0]);
    }

    [Fact]
    public void MinorEventsDoNotWakeCommander()
    {
        using var rig = new BrainRig();
        rig.CommanderReply = _ => Commander();
        rig.Tick(S(1));
        rig.Tick(S(2));
        // An enemy formation takes losses and moves: only Minor events.
        var s = Snap(OwnArmy(), EnemyArmy().Select(f => f.Id == "E1" ? f.With(count: 40, y: 450) : f).ToList(), time: 20);
        rig.Tick(s);
        Assert.Single(rig.Cmd.Requests);
    }

    [Fact]
    public void ContingencyFiresWhenConditionsMetReplacesDirectiveAndFiresOnce()
    {
        using var rig = new BrainRig();
        rig.Tick(S(1));
        rig.Tick(S(2));

        var r = rig.Tick(S(3, ownEdit: Engage));
        Assert.Contains("Contingency C1: Cavalry strikes the archers", r.Messages);
        var f3 = Assert.Single(r.Changed, c => c.FormationId == "F3");
        Assert.Equal(DirectiveAction.Charge, f3.Action);
        Assert.Equal("E2", f3.TargetFormationId);
        Assert.Equal("contingency C1", f3.Source);
        Assert.Equal(1, rig.Brain.Stats.ContingenciesFired);
        Assert.Equal(3.0, rig.Brain.CurrentPlan!.Contingencies[0].FiredAt);
        Assert.False(rig.Brain.CurrentPlan.Contingencies[0].IsArmed);

        var again = rig.Tick(S(4, ownEdit: Engage));
        Assert.DoesNotContain(again.Messages, m => m.StartsWith("Contingency"));
        Assert.Empty(again.Changed);
        Assert.Equal(1, rig.Brain.Stats.ContingenciesFired);
    }

    [Fact]
    public void ContingencyDoesNotFireBeforeConditionsAreMet()
    {
        using var rig = new BrainRig();
        rig.Tick(S(1));
        var r = rig.Tick(S(2));
        Assert.DoesNotContain(r.Messages, m => m.StartsWith("Contingency"));
        Assert.Equal(0, rig.Brain.Stats.ContingenciesFired);
        Assert.True(rig.Brain.CurrentPlan!.Contingencies[0].IsArmed);
    }

    [Fact]
    public void TimeElapsedContingencyIsRelativeToPlanInstallTime()
    {
        using var rig = new BrainRig();
        rig.CommanderReply = _ => Commander(
            directivesJson: """{"formation":"F1","action":"hold"},{"formation":"F3","action":"reserve"}""",
            contingenciesJson: """{"id":"T","description":"Time is up","match":"all","conditions":[{"type":"time_elapsed","value":20}],"directives":[{"formation":"F3","action":"retreat"}]}""");
        rig.Tick(S(1));
        rig.Tick(S(2)); // installed at t=2
        Assert.DoesNotContain(rig.Tick(S(21)).Messages, m => m.StartsWith("Contingency"));
        var r = rig.Tick(S(22));
        Assert.Contains("Contingency T: Time is up", r.Messages);
        Assert.Equal(DirectiveAction.Retreat, Assert.Single(r.Changed, c => c.FormationId == "F3").Action);
    }

    [Fact]
    public void FormationsLeftOutOfAReplanKeepTheirPreviousDirectives()
    {
        using var rig = new BrainRig();
        var n = 0;
        rig.CommanderReply = _ => n++ == 0
            ? Commander()
            : Commander(directivesJson: """{"formation":"F1","action":"charge"}""");
        rig.Tick(S(1));
        var first = rig.Tick(S(2));
        rig.Tick(S(31));
        var r = rig.Tick(S(32));

        var plan = rig.Brain.CurrentPlan!;
        Assert.Equal(2, plan.Version);
        Assert.Equal(DirectiveAction.Charge, plan.Directives["F1"].Action);
        Assert.Equal(DirectiveAction.Skirmish, plan.Directives["F2"].Action);
        Assert.Equal(DirectiveAction.Reserve, plan.Directives["F3"].Action);
        Assert.Equal("F1", Assert.Single(r.Changed).FormationId);
        Assert.Same(first.Current["F2"], r.Current["F2"]);
        Assert.Equal(3, r.Current.Count);
    }

    [Fact]
    public void DeadFormationsAreNotCarriedOverOnReplan()
    {
        using var rig = new BrainRig();
        var n = 0;
        rig.CommanderReply = _ => n++ == 0
            ? Commander()
            : Commander(directivesJson: """{"formation":"F1","action":"charge"}""");
        rig.Tick(S(1));
        rig.Tick(S(2));
        Func<FormationState, FormationState> killF3 = f => f.Id == "F3" ? f.With(count: 0) : f;
        rig.Tick(S(31, ownEdit: killF3));
        var r = rig.Tick(S(32, ownEdit: killF3));
        Assert.False(rig.Brain.CurrentPlan!.Directives.ContainsKey("F3"));
        Assert.DoesNotContain("F3", r.Current.Keys);
    }

    [Fact]
    public void PlanWithNoUsableDirectivesCountsAsAFailure()
    {
        using var rig = new BrainRig();
        rig.CommanderReply = _ => Commander(directivesJson: """{"formation":"F9","action":"hold"}""");
        rig.Tick(S(1));
        var r = rig.Tick(S(2));
        Assert.Equal(BrainMode.Vanilla, r.Mode);
        Assert.Equal(1, rig.Brain.Stats.Commander.Failures);
    }

    [Fact]
    public void FingerprintIsCoarse()
    {
        string Fp(params FormationState[] own) => BattleBrain.Fingerprint(Snap(own, new List<FormationState>()));
        var a = Form("F1", FormationRole.Infantry, 100, 0, 0);
        Assert.Equal(Fp(a), Fp(a.With(x: 24.9f)));
        Assert.NotEqual(Fp(a), Fp(a.With(x: 25f)));
        Assert.NotEqual(Fp(a), Fp(a.With(y: -0.1f)));
        Assert.NotEqual(Fp(a), Fp(a.With(count: 99)));
        Assert.Equal(Fp(a.With(count: 95)), Fp(a.With(count: 91)));
        Assert.NotEqual(Fp(a), Fp(a.With(engaged: true)));
        Assert.NotEqual(Fp(a), Fp(a.With(routing: true)));
        Assert.NotEqual(Fp(a), Fp(a.With(count: 0)));
        Assert.Contains("F1:x;", Fp(a.With(count: 0)));
    }
}

public class BattleBrainLieutenantTests
{
    private static BrainRig Rig(Action<BrainConfig>? cfg = null)
    {
        var rig = new BrainRig(cfg, lieutenant: true);
        rig.CommanderReply = _ => Commander();
        return rig;
    }

    /// <summary>Installs a plan with armies 80 m apart (Lieutenant interval 5 s). Plan issued at t=2.</summary>
    private static void Install(BrainRig rig, string? commanderJson = null)
    {
        if (commanderJson != null) rig.CommanderReply = _ => commanderJson;
        rig.Tick(S(1, 80));
        rig.Tick(S(2, 80));
        Assert.Equal(BrainMode.Llm, rig.Brain.Mode);
    }

    [Fact]
    public void NotCalledWhenArmiesAreBeyondFarDistance()
    {
        using var rig = Rig();
        rig.Tick(S(1, 500));
        rig.Tick(S(2, 500));
        foreach (var t in new[] { 5.0, 10, 20, 25 }) rig.Tick(S(t, 500));
        Assert.Empty(rig.Lt.Requests);
    }

    [Fact]
    public void CalledWhenCloseAtNearIntervalAndNotAgainWithoutChange()
    {
        using var rig = Rig();
        Install(rig);

        rig.Tick(S(4, 80));
        Assert.Empty(rig.Lt.Requests); // within LieutenantGapAfterPlan of the new plan

        rig.Tick(S(5, 80), settle: false);
        Assert.True(rig.Brain.LieutenantBusy);
        rig.Settle();
        var req = Assert.Single(rig.Lt.Requests);
        Assert.Equal(PlanSchemas.LieutenantName, req.SchemaName);
        Assert.Equal(Prompts.Lieutenant, req.SystemPrompt);
        Assert.Equal(rig.Config.Lieutenant.Model, req.Model);
        Assert.Contains("EVENTS SINCE YOUR LAST CHECK", req.UserContent);

        rig.Tick(S(6, 80));
        Assert.False(rig.Brain.LieutenantBusy);
        rig.Tick(S(11, 80)); // interval elapsed, fingerprint unchanged
        rig.Tick(S(20, 80));
        Assert.Single(rig.Lt.Requests);
        Assert.Equal(1, rig.Brain.Stats.Lieutenant.Calls);
    }

    [Fact]
    public void CalledAgainAfterMeaningfulChange()
    {
        using var rig = Rig();
        Install(rig);
        rig.Tick(S(5, 80));
        rig.Tick(S(6, 80));
        rig.Tick(S(12, 80, f => f.Id == "F1" ? f.With(x: 30) : f));
        Assert.Equal(2, rig.Lt.Requests.Count);
    }

    [Fact]
    public void IntervalInterpolatesWithDistance()
    {
        using var rig = Rig();
        Install(rig);
        // Min distance 190 m -> interval 17.5 s.
        rig.Tick(S(5, 190));
        Assert.Single(rig.Lt.Requests);
        rig.Tick(S(6, 190));
        rig.Tick(S(20, 190, f => f.Id == "F1" ? f.With(x: 0, y: 0) : f.With(y: f.Position.Y - 60))); // fingerprint changed, only 15 s
        Assert.Single(rig.Lt.Requests);
        rig.Tick(S(23, 190, f => f.Id == "F1" ? f.With(x: 0, y: 0) : f.With(y: f.Position.Y - 60))); // 18 s
        Assert.Equal(2, rig.Lt.Requests.Count);
    }

    [Fact]
    public void DisabledLieutenantIsNeverCalled()
    {
        using var rig = new BrainRig(c => c.Lieutenant.Enabled = false, lieutenant: true);
        Install(rig);
        rig.Tick(S(5, 80));
        Assert.Empty(rig.Lt.Requests);
    }

    [Fact]
    public void AdjustmentCannotChangeActionExceptToHoldOrRetreat()
    {
        using var rig = Rig();
        rig.LieutenantReply = _ => Lieutenant(adjustments: """{"formation":"F2","action":"charge","arrangement":"loose","fire":"hold"}""");
        Install(rig);
        rig.Tick(S(5, 80));
        var r = rig.Tick(S(6, 80));

        var d = rig.Brain.CurrentPlan!.Directives["F2"];
        Assert.Equal(DirectiveAction.Skirmish, d.Action); // charge refused, original kept
        Assert.Equal(Arrangement.Loose, d.Arrangement);   // other fields applied
        Assert.Equal(FireOrder.Hold, d.Fire);
        Assert.Equal("lieutenant", d.Source);
        var cmd = Assert.Single(r.Changed);
        Assert.Equal("F2", cmd.FormationId);
        Assert.Equal(Arrangement.Loose, cmd.Arrangement);
        Assert.Contains(rig.Log.Warns, w => w.Contains("Lieutenant tried to change F2"));
    }

    [Fact]
    public void AdjustmentMaySwitchToRetreatAndDropsOldAnchor()
    {
        using var rig = Rig();
        rig.LieutenantReply = _ => Lieutenant(adjustments: """{"formation":"F1","action":"retreat"}""");
        Install(rig);
        rig.Tick(S(5, 80));
        var r = rig.Tick(S(6, 80));
        var d = rig.Brain.CurrentPlan!.Directives["F1"];
        Assert.Equal(DirectiveAction.Retreat, d.Action);
        Assert.Null(d.Anchor); // the pinned hold position no longer applies
        var cmd = Assert.Single(r.Changed);
        Assert.Equal(DirectiveAction.Retreat, cmd.Action);
        Assert.Equal(-150f, cmd.Position!.Value.Y, 2);
    }

    [Fact]
    public void AdjustmentMaySwitchToHoldAndPinsCurrentPosition()
    {
        using var rig = Rig();
        rig.LieutenantReply = _ => Lieutenant(adjustments: """{"formation":"F2","action":"hold"}""");
        Install(rig);
        rig.Tick(S(5, 80));
        var r = rig.Tick(S(6, 80));
        var cmd = Assert.Single(r.Changed);
        Assert.Equal(DirectiveAction.Hold, cmd.Action);
        Assert.Equal(new Vec2(0, -30), cmd.Position);
    }

    [Fact]
    public void FireContingenciesWorksAndOnlyOnce()
    {
        using var rig = Rig();
        rig.LieutenantReply = _ => Lieutenant(fire: "\"C1\"", orderLog: "Cavalry, now!");
        Install(rig, CommanderJson);
        rig.Tick(S(5, 80));
        var r = rig.Tick(S(6, 80));
        Assert.Contains("Contingency C1: Cavalry strikes the archers", r.Messages);
        Assert.Contains("Lieutenant: Cavalry, now!", r.Messages);
        Assert.Equal(1, rig.Brain.Stats.ContingenciesFired);
        var f3 = Assert.Single(r.Changed);
        Assert.Equal(DirectiveAction.Charge, f3.Action);
        Assert.Equal("E2", f3.TargetFormationId);

        // Asking again is ignored: the contingency has already fired.
        rig.Tick(S(12, 80, f => f.Id == "F1" ? f.With(x: 30) : f));
        var r2 = rig.Tick(S(13, 80, f => f.Id == "F1" ? f.With(x: 30) : f));
        Assert.Equal(1, rig.Brain.Stats.ContingenciesFired);
        Assert.DoesNotContain(r2.Messages, m => m.StartsWith("Contingency"));
    }

    [Fact]
    public void WakeCommanderLeadsToCommanderCallRespectingCooldown()
    {
        using var rig = Rig();
        rig.LieutenantReply = _ => Lieutenant(wake: true, reason: "flank threatened");
        Install(rig);
        rig.Tick(S(5, 80));
        rig.Tick(S(6, 80)); // reply collected; only 5 s since the commander's call at t=1
        Assert.Single(rig.Cmd.Requests);

        rig.Tick(S(10.5, 80));
        Assert.Single(rig.Cmd.Requests);

        rig.Tick(S(11, 80), settle: false);
        rig.Settle();
        Assert.Equal(2, rig.Cmd.Requests.Count);
        Assert.Contains("lieutenant: flank threatened", rig.Cmd.Requests[1].UserContent);
    }

    [Fact]
    public void WakeWithoutReasonUsesDefaultText()
    {
        using var rig = Rig();
        rig.LieutenantReply = _ => Lieutenant(wake: true);
        Install(rig);
        rig.Tick(S(5, 80));
        rig.Tick(S(6, 80));
        rig.Tick(S(11, 80));
        Assert.Contains("plan no longer fits", rig.Cmd.Requests[1].UserContent);
    }

    [Fact]
    public void ReplyIsDiscardedIfPlanVersionChangedMeanwhile()
    {
        using var gate = new ManualResetEventSlim(false);
        using var rig = Rig(c => c.Cadence.CommanderInterval = 30);
        rig.LieutenantReply = _ =>
        {
            gate.Wait(TimeSpan.FromSeconds(5));
            return Lieutenant(fire: "\"C1\"", adjustments: """{"formation":"F1","action":"retreat"}""");
        };
        Install(rig, CommanderJson);

        rig.Tick(S(5, 80), settle: false); // lieutenant call starts and blocks on the gate
        Assert.True(rig.Brain.LieutenantBusy);
        rig.Tick(S(31, 80), settleLieutenant: false); // scheduled commander re-plan; wait for it only
        rig.Tick(S(32, 80), settleLieutenant: false); // plan v2 installed while the lieutenant is still thinking
        Assert.Equal(2, rig.Brain.CurrentPlan!.Version);

        gate.Set();
        rig.Settle();
        rig.Tick(S(33, 80));

        Assert.Equal(1, rig.Brain.Stats.Lieutenant.Calls);
        Assert.Equal(0, rig.Brain.Stats.ContingenciesFired);
        Assert.True(rig.Brain.CurrentPlan.Contingencies[0].IsArmed);
        Assert.Equal(DirectiveAction.Hold, rig.Brain.CurrentPlan.Directives["F1"].Action);
        Assert.Contains(rig.Log.Infos, i => i.Contains("discarded"));
    }

    [Fact]
    public void LieutenantIsDisabledAfterRepeatedFailures()
    {
        using var rig = Rig(c => c.Cadence.CommanderInterval = 1e9);
        rig.LieutenantReply = _ => "garbage";
        Install(rig);

        rig.Tick(S(5, 80));
        rig.Tick(S(6, 80));
        rig.Tick(S(20, 80, f => f.Id == "F1" ? f.With(x: 30) : f));
        rig.Tick(S(21, 80, f => f.Id == "F1" ? f.With(x: 30) : f));
        rig.Tick(S(40, 80, f => f.Id == "F1" ? f.With(x: 60) : f));
        var r = rig.Tick(S(41, 80, f => f.Id == "F1" ? f.With(x: 60) : f));
        Assert.Contains(r.Messages, m => m.Contains("Lieutenant unavailable"));
        Assert.Equal(3, rig.Lt.Requests.Count);
        Assert.Equal(3, rig.Brain.Stats.Lieutenant.Failures);

        rig.Tick(S(60, 80, f => f.Id == "F1" ? f.With(x: 90) : f));
        Assert.Equal(3, rig.Lt.Requests.Count);
        Assert.Equal(BrainMode.Llm, rig.Brain.Mode); // the commander carries on alone
    }

    [Fact]
    public void LieutenantMaxCallsRespected()
    {
        using var rig = Rig(c => c.Lieutenant.MaxCallsPerBattle = 1);
        Install(rig);
        rig.Tick(S(5, 80));
        rig.Tick(S(6, 80));
        rig.Tick(S(12, 80, f => f.Id == "F1" ? f.With(x: 30) : f));
        Assert.Single(rig.Lt.Requests);
    }
}

public class BattleBrainFailureTests
{
    public static IEnumerable<object?[]> BadReplies() => new[]
    {
        new object?[] { "not json at all" },
        new object?[] { null },
        new object?[] { "{}" },
        new object?[] { """{"intent":"x","directives":[{"formation":"E1","action":"hold"}]}""" },
    };

    [Theory]
    [MemberData(nameof(BadReplies))]
    public void RepeatedCommanderFailuresFallBackAndStopCalling(string? reply)
    {
        using var rig = new BrainRig();
        rig.CommanderReply = _ => reply;

        rig.Tick(S(1));
        var r1 = rig.Tick(S(2));
        Assert.Contains(r1.Messages, m => m.StartsWith("Commander unavailable"));
        Assert.Equal(BrainMode.Vanilla, r1.Mode);
        Assert.Equal(1, rig.Brain.Stats.Commander.Failures);

        rig.Tick(S(3)); // retry is throttled to every 5 s
        Assert.Single(rig.Cmd.Requests);

        rig.Tick(S(6));
        var r2 = rig.Tick(S(7));
        Assert.Equal(BrainMode.Vanilla, r2.Mode);
        Assert.DoesNotContain(r2.Messages, m => m.StartsWith("Commander unavailable")); // only the first failure is announced

        rig.Tick(S(12));
        var r3 = rig.Tick(S(13));
        Assert.Equal(BrainMode.FallenBack, r3.Mode);
        Assert.Contains(r3.Messages, m => m.Contains("stood down"));
        Assert.Contains("3 times", rig.Brain.FallbackReason);
        Assert.Equal(3, rig.Cmd.Requests.Count);

        foreach (var t in new[] { 20.0, 50, 100, 1000 })
        {
            var r = rig.Tick(S(t));
            Assert.Equal(BrainMode.FallenBack, r.Mode);
            Assert.Empty(r.Changed);
            Assert.Empty(r.Current);
        }
        Assert.Equal(3, rig.Cmd.Requests.Count);
        Assert.False(rig.Brain.CommanderBusy);
    }

    [Fact]
    public void FailureThresholdIsConfigurable()
    {
        using var rig = new BrainRig(c => c.MaxConsecutiveFailures = 1);
        rig.CommanderReply = _ => "nope";
        rig.Tick(S(1));
        var r = rig.Tick(S(2));
        Assert.Equal(BrainMode.FallenBack, r.Mode);
    }

    [Fact]
    public void SuccessResetsTheFailureCounter()
    {
        using var rig = new BrainRig();
        var replies = new Queue<string?>(new string?[] { "bad", "bad", CommanderJson });
        rig.CommanderReply = _ => replies.Count > 0 ? replies.Dequeue() : CommanderJson;
        foreach (var t in new[] { 1.0, 2, 6, 7, 12, 13 }) rig.Tick(S(t));
        Assert.Equal(BrainMode.Llm, rig.Brain.Mode);
        Assert.Equal(2, rig.Brain.Stats.Commander.Failures);
    }

    [Fact]
    public void ClientExceptionsAreFailuresNotCrashes()
    {
        using var rig = new BrainRig();
        rig.CommanderReply = _ => throw new InvalidOperationException("kaboom");
        rig.Tick(S(1));
        var r = rig.Tick(S(2));
        Assert.Contains(r.Messages, m => m.Contains("kaboom"));
    }

    [Fact]
    public void FailuresAfterAPlanKeepOldPlanUntilFallback()
    {
        using var rig = new BrainRig();
        var n = 0;
        rig.CommanderReply = _ => n++ == 0 ? CommanderJson : "garbage";
        rig.Tick(S(1));
        rig.Tick(S(2));
        rig.Tick(S(31));
        var r = rig.Tick(S(32));
        Assert.Equal(BrainMode.Llm, r.Mode);
        Assert.Equal(3, r.Current.Count);
        Assert.Equal(1, rig.Brain.CurrentPlan!.Version);
    }

    [Fact]
    public void TokenBudgetExceededFallsBack()
    {
        using var rig = new BrainRig(c => c.MaxTokensPerBattle = 10);
        rig.Tick(S(1));
        var r = rig.Tick(S(2));
        Assert.Equal(BrainMode.FallenBack, r.Mode);
        Assert.Contains(r.Messages, m => m.Contains("token budget"));
        Assert.Contains("token budget", rig.Brain.FallbackReason);
        Assert.Empty(r.Changed);

        rig.Tick(S(100));
        Assert.Single(rig.Cmd.Requests);
    }

    [Fact]
    public void ZeroTokenCapMeansNoCap()
    {
        using var rig = new BrainRig(c => c.MaxTokensPerBattle = 0);
        rig.Tick(S(1));
        Assert.Equal(BrainMode.Llm, rig.Tick(S(2)).Mode);
    }

    [Fact]
    public void CommanderMaxCallsPerBattleRespected()
    {
        using var rig = new BrainRig(c =>
        {
            c.Commander.MaxCallsPerBattle = 2;
            c.Cadence.CommanderInterval = 10;
            c.Cadence.CommanderCooldown = 1;
        });
        foreach (var t in new[] { 1.0, 2, 12, 13, 30, 60, 200, 1000 }) rig.Tick(S(t));
        Assert.Equal(2, rig.Cmd.Requests.Count);
        Assert.Equal(BrainMode.Llm, rig.Brain.Mode); // a plan exists, so no fallback
    }

    [Fact]
    public void CommanderCallLimitWithoutPlanFallsBack()
    {
        using var rig = new BrainRig(c => c.Commander.MaxCallsPerBattle = 0);
        var r = rig.Tick(S(1));
        Assert.Equal(BrainMode.FallenBack, r.Mode);
        Assert.Empty(rig.Cmd.Requests);
    }

    [Fact]
    public void DisposeCancelsOutstandingCalls()
    {
        var rig = new BrainRig();
        rig.Tick(S(1), settle: false);
        rig.Dispose(); // must not throw even with a call in flight
        Assert.True(rig.Cmd.Requests.Count <= 1);
    }
}
