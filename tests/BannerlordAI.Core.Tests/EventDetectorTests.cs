using BannerlordAI.Core.Compaction;
using BannerlordAI.Core.Model;
using static BannerlordAI.Core.Tests.TestData;

namespace BannerlordAI.Core.Tests;

public class EventDetectorTests
{
    private static BattleSnapshot S(IReadOnlyList<FormationState>? own = null, IReadOnlyList<FormationState>? enemies = null, double t = 0)
        => Snap(own, enemies, time: t);

    private static List<FormationState> Own(Func<FormationState, FormationState>? edit = null)
        => OwnArmy().Select(f => edit == null ? f : edit(f)).ToList();

    private static List<FormationState> Enemy(Func<FormationState, FormationState>? edit = null, float y = 300)
        => EnemyArmy(y).Select(f => edit == null ? f : edit(f)).ToList();

    [Fact]
    public void FirstUpdateIsQuiet()
    {
        var d = new EventDetector();
        Assert.Empty(d.Update(S(Own(), Enemy())));
    }

    [Fact]
    public void UnchangedSnapshotsStayQuiet()
    {
        var d = new EventDetector();
        d.Update(S(Own(), Enemy()));
        Assert.Empty(d.Update(S(Own(), Enemy(), 1)));
    }

    [Fact]
    public void NewFormationAfterFirstUpdateIsReported()
    {
        var d = new EventDetector();
        d.Update(S(Own(), Enemy()));
        var enemies = Enemy();
        enemies.Add(Form("E5", FormationRole.Cavalry, 20, 50, 400));
        var ev = d.Update(S(Own(), enemies, 1));
        var e = Assert.Single(ev);
        Assert.Equal(EventSeverity.Minor, e.Severity);
        Assert.Contains("E5", e.Text);
        Assert.Contains("appeared", e.Text);
    }

    [Fact]
    public void FirstContactIsMajorExactlyOnce()
    {
        var d = new EventDetector();
        d.Update(S(Own(), Enemy()));
        Assert.False(d.FirstContactHappened);

        var engaged = Own(f => f.Id == "F1" ? f.With(engaged: true) : f);
        var ev = d.Update(S(engaged, Enemy(), 1));
        var contact = Assert.Single(ev, e => e.Text.Contains("FIRST CONTACT"));
        Assert.Equal(EventSeverity.Major, contact.Severity);
        Assert.True(d.FirstContactHappened);
        Assert.Contains(ev, e => e.Text.Contains("F1") && e.Text.Contains("entered melee"));

        Assert.DoesNotContain(d.Update(S(engaged, Enemy(), 2)), e => e.Text.Contains("FIRST CONTACT"));
        // Disengaging and re-engaging is not a second first contact.
        d.Update(S(Own(), Enemy(), 3));
        Assert.DoesNotContain(d.Update(S(engaged, Enemy(), 4)), e => e.Text.Contains("FIRST CONTACT"));
    }

    [Fact]
    public void EnemyEngagementAloneIsNotFirstContact()
    {
        var d = new EventDetector();
        d.Update(S(Own(), Enemy()));
        var ev = d.Update(S(Own(), Enemy(f => f.Id == "E1" ? f.With(engaged: true) : f), 1));
        Assert.DoesNotContain(ev, e => e.Text.Contains("FIRST CONTACT"));
        Assert.False(d.FirstContactHappened);
    }

    [Fact]
    public void OwnFormationCrossingHalfStrengthIsMajor()
    {
        var d = new EventDetector();
        d.Update(S(Own(), Enemy()));

        var ev60 = d.Update(S(Own(f => f.Id == "F1" ? f.With(count: 60) : f), Enemy(), 1));
        var minor = Assert.Single(ev60);
        Assert.Equal(EventSeverity.Minor, minor.Severity);
        Assert.Contains("down to 60% (60/100)", minor.Text);

        var ev45 = d.Update(S(Own(f => f.Id == "F1" ? f.With(count: 45) : f), Enemy(), 2));
        var major = Assert.Single(ev45);
        Assert.Equal(EventSeverity.Major, major.Severity);
        Assert.Contains("F1", major.Text);
        Assert.Contains("down to 45%", major.Text);

        // Further losses below 25% are reported but no longer a fresh "crossing".
        var ev20 = d.Update(S(Own(f => f.Id == "F1" ? f.With(count: 20) : f), Enemy(), 3));
        Assert.Equal(EventSeverity.Minor, Assert.Single(ev20).Severity);

        // No repeat while strength is unchanged.
        Assert.Empty(d.Update(S(Own(f => f.Id == "F1" ? f.With(count: 20) : f), Enemy(), 4)));
    }

    [Fact]
    public void ExactlyHalfIsNotYetMajorButJustUnderIs()
    {
        var d = new EventDetector();
        d.Update(S(Own(), Enemy()));
        Assert.Equal(EventSeverity.Minor, Assert.Single(d.Update(S(Own(f => f.Id == "F1" ? f.With(count: 50) : f), Enemy(), 1))).Severity);
        Assert.Equal(EventSeverity.Major, Assert.Single(d.Update(S(Own(f => f.Id == "F1" ? f.With(count: 49) : f), Enemy(), 2))).Severity);
    }

    [Fact]
    public void DirectDropFromFullToUnderHalfIsMajor()
    {
        var d = new EventDetector();
        d.Update(S(Own(), Enemy()));
        var ev = d.Update(S(Own(f => f.Id == "F1" ? f.With(count: 40) : f), Enemy(), 1));
        Assert.Equal(EventSeverity.Major, Assert.Single(ev).Severity);
    }

    [Fact]
    public void EnemyCrossingHalfStrengthIsOnlyMinor()
    {
        var d = new EventDetector();
        d.Update(S(Own(), Enemy()));
        var ev = d.Update(S(Own(), Enemy(f => f.Id == "E1" ? f.With(count: 45) : f), 1));
        var e = Assert.Single(ev);
        Assert.Equal(EventSeverity.Minor, e.Severity);
        Assert.Contains("E1", e.Text);
    }

    [Fact]
    public void RoutingAndRallying()
    {
        var d = new EventDetector();
        d.Update(S(Own(), Enemy()));

        var ownRout = d.Update(S(Own(f => f.Id == "F2" ? f.With(routing: true) : f), Enemy(), 1));
        var e = Assert.Single(ownRout);
        Assert.Equal(EventSeverity.Major, e.Severity);
        Assert.Contains("ROUTING", e.Text);

        Assert.Empty(d.Update(S(Own(f => f.Id == "F2" ? f.With(routing: true) : f), Enemy(), 2)));

        var rally = d.Update(S(Own(), Enemy(), 3));
        var r = Assert.Single(rally);
        Assert.Equal(EventSeverity.Minor, r.Severity);
        Assert.Contains("rallied", r.Text);

        var enemyRout = d.Update(S(Own(), Enemy(f => f.Id == "E3" ? f.With(routing: true) : f), 4));
        Assert.Equal(EventSeverity.Minor, Assert.Single(enemyRout).Severity);
    }

    [Fact]
    public void DestroyedOwnIsMajorEnemyIsMinorAndReportedOnce()
    {
        var d = new EventDetector();
        d.Update(S(Own(), Enemy()));
        var ev = d.Update(S(Own(f => f.Id == "F3" ? f.With(count: 0) : f), Enemy(f => f.Id == "E3" ? f.With(count: 0) : f), 1));
        var own = Assert.Single(ev, e => e.Text.Contains("F3"));
        var enemy = Assert.Single(ev, e => e.Text.Contains("E3"));
        Assert.Equal(EventSeverity.Major, own.Severity);
        Assert.Equal(EventSeverity.Minor, enemy.Severity);
        Assert.Contains("destroyed", own.Text);

        Assert.Empty(d.Update(S(Own(f => f.Id == "F3" ? f.With(count: 0) : f), Enemy(f => f.Id == "E3" ? f.With(count: 0) : f), 2)));
    }

    [Fact]
    public void EnemyMovementReportedOnceThenRearmedFromNewPosition()
    {
        var d = new EventDetector();
        d.Update(S(Own(), Enemy(y: 300)));

        Assert.DoesNotContain(d.Update(S(Own(), Enemy(y: 380), 1)), e => e.Text.Contains("moved")); // 80 m

        var ev = d.Update(S(Own(), Enemy(y: 410), 2)); // 110 m from the original report point
        var moved = Assert.Single(ev, e => e.Text.Contains("E1") && e.Text.Contains("moved"));
        Assert.Equal(EventSeverity.Minor, moved.Severity);
        Assert.Contains("moved 110m", moved.Text);
        Assert.Contains("toward the enemy side", moved.Text);

        // Only 10 m more: nothing, because the reference point is now y=410.
        Assert.DoesNotContain(d.Update(S(Own(), Enemy(y: 420), 3)), e => e.Text.Contains("moved"));

        // Another 100 m from the new reference: reported again.
        Assert.Contains(d.Update(S(Own(), Enemy(y: 510), 4)), e => e.Text.Contains("E1") && e.Text.Contains("moved"));
    }

    [Fact]
    public void EnemyMovementDirectionPhrases()
    {
        var d = new EventDetector();
        d.Update(S(Own(), Enemy(y: 300)));
        var toUs = d.Update(S(Own(), Enemy(y: 150), 1));
        Assert.Contains(toUs, e => e.Text.Contains("toward your side"));

        var d2 = new EventDetector();
        d2.Update(S(Own(), Enemy()));
        var sideways = d2.Update(S(Own(), Enemy(f => f.Id == "E1" ? f.With(x: 150) : f), 1));
        Assert.Contains(sideways, e => e.Text.Contains("toward your right"));
    }

    [Fact]
    public void OwnMovementIsNotReported()
    {
        var d = new EventDetector();
        d.Update(S(Own(), Enemy()));
        Assert.Empty(d.Update(S(Own(f => f.With(y: f.Position.Y + 200)), Enemy(), 1)));
    }

    [Fact]
    public void CavalryNearOwnArchersFiresOnceAndRearmsAfterMovingAway()
    {
        // F2 (ranged) is at (0,-30). Enemy cavalry E3 starts far away.
        List<FormationState> E(float y) => Enemy(f => f.Id == "E3" ? f.With(x: 0, y: y) : f);
        static bool Threat(BattleEvent e) => e.Text.Contains("within 120m");

        var d = new EventDetector();
        d.Update(S(Own(), E(300)));

        var near = d.Update(S(Own(), E(50), 1)); // 80 m from F2
        var threat = Assert.Single(near, Threat);
        Assert.Equal(EventSeverity.Minor, threat.Severity);
        Assert.Contains("E3", threat.Text);
        Assert.Contains("F2", threat.Text);

        Assert.DoesNotContain(d.Update(S(Own(), E(40), 2)), Threat); // still close: no repeat

        // 150 m away (<= 1.5 * 120 = 180): not re-armed yet.
        d.Update(S(Own(), E(120), 3));
        Assert.DoesNotContain(d.Update(S(Own(), E(60), 4)), Threat);

        // Well beyond 180 m: re-armed; coming back fires again.
        d.Update(S(Own(), E(300), 5));
        Assert.Contains(d.Update(S(Own(), E(50), 6)), Threat);
    }

    [Fact]
    public void CavalryThreatRequiresOwnRangedAndMountedLivingEnemy()
    {
        static bool Threat(BattleEvent e) => e.Text.Contains("within 120m");
        var d = new EventDetector();
        d.Update(S(Own(), Enemy()));

        // Enemy infantry next to our archers: no threat event.
        Assert.DoesNotContain(d.Update(S(Own(), Enemy(f => f.Id == "E1" ? f.With(x: 0, y: -20) : f), 1)), Threat);
        // Dead enemy cavalry next to our archers: no.
        Assert.DoesNotContain(d.Update(S(Own(), Enemy(f => f.Id == "E3" ? f.With(x: 0, y: -20, count: 0) : f), 2)), Threat);

        // Horse archers count as mounted.
        var d2 = new EventDetector();
        var enemies = new List<FormationState> { Form("E1", FormationRole.HorseArcher, 20, 0, 400) };
        d2.Update(S(Own(), enemies));
        var close = new List<FormationState> { Form("E1", FormationRole.HorseArcher, 20, 0, 40) };
        Assert.Contains(d2.Update(S(Own(), close, 1)), Threat);

        // Cavalry near our own cavalry/infantry (not ranged) is not a threat event.
        var d3 = new EventDetector();
        var ownNoArchers = new List<FormationState> { Form("F1", FormationRole.Infantry, 50, 0, 0) };
        d3.Update(S(ownNoArchers, enemies));
        Assert.DoesNotContain(d3.Update(S(ownNoArchers, close, 1)), Threat);
    }

    [Fact]
    public void EventToStringAndCustomOptions()
    {
        var e = new BattleEvent { Time = 65, Text = "hello" };
        Assert.Equal("01:05 hello", e.ToString());

        var d = new EventDetector(new EventDetectorOptions { MoveReportDistance = 20, CavalryThreatDistance = 50 });
        d.Update(S(Own(), Enemy()));
        Assert.Contains(d.Update(S(Own(), Enemy(y: 330), 1)), x => x.Text.Contains("moved 30m"));
    }
}
