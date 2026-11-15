using BannerlordAI.Core.Model;

namespace BannerlordAI.Core.Tests;

public class WireTests
{
    [Theory]
    [InlineData("Shield Wall", "shield_wall")]
    [InlineData("  Horse-Archer ", "horse_archer")]
    [InlineData("FALL_BACK", "fall_back")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Normalize(string? input, string expected) => Assert.Equal(expected, Wire.Normalize(input));

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(5, "00:05")]
    [InlineData(65, "01:05")]
    [InlineData(59.6, "01:00")]
    [InlineData(-10, "00:00")]
    [InlineData(3600, "60:00")]
    [InlineData(754.4, "12:34")]
    public void Clock(double seconds, string expected) => Assert.Equal(expected, Wire.Clock(seconds));

    [Fact]
    public void FormatsInvariant()
    {
        Assert.Equal("1.5", Wire.F(1.5, "0.0"));
        Assert.Equal("3", Wire.F(2.6));
    }

    [Theory]
    [InlineData("hold", DirectiveAction.Hold)]
    [InlineData("defend", DirectiveAction.Hold)]
    [InlineData("Hold Position", DirectiveAction.Hold)]
    [InlineData("advance", DirectiveAction.Advance)]
    [InlineData("move", DirectiveAction.Advance)]
    [InlineData("charge", DirectiveAction.Charge)]
    [InlineData("attack", DirectiveAction.Charge)]
    [InlineData("skirmish", DirectiveAction.Skirmish)]
    [InlineData("flank", DirectiveAction.Flank)]
    [InlineData("protect_flank", DirectiveAction.ProtectFlank)]
    [InlineData("Protect-Flank", DirectiveAction.ProtectFlank)]
    [InlineData("screen", DirectiveAction.Screen)]
    [InlineData("reserve", DirectiveAction.Reserve)]
    [InlineData("retreat", DirectiveAction.Retreat)]
    [InlineData("fall_back", DirectiveAction.Retreat)]
    [InlineData("Fall Back", DirectiveAction.Retreat)]
    [InlineData("follow", DirectiveAction.Follow)]
    public void ParseAction(string text, DirectiveAction expected)
    {
        Assert.True(Wire.TryParseAction(text, out var a));
        Assert.Equal(expected, a);
    }

    [Theory]
    [InlineData("dance")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseActionFailure(string? text)
    {
        Assert.False(Wire.TryParseAction(text, out var a));
        Assert.Equal(DirectiveAction.Hold, a);
    }

    [Fact]
    public void ActionsRoundTrip()
    {
        foreach (var a in Enum.GetValues<DirectiveAction>())
        {
            Assert.True(Wire.TryParseAction(Wire.ToWire(a), out var back));
            Assert.Equal(a, back);
        }
    }

    [Theory]
    [InlineData("line", Arrangement.Line)]
    [InlineData("shieldwall", Arrangement.ShieldWall)]
    [InlineData("shield wall", Arrangement.ShieldWall)]
    [InlineData("Shield-Wall", Arrangement.ShieldWall)]
    [InlineData("loose", Arrangement.Loose)]
    [InlineData("square", Arrangement.Square)]
    [InlineData("circle", Arrangement.Circle)]
    [InlineData("column", Arrangement.Column)]
    [InlineData("scatter", Arrangement.Scatter)]
    public void ParseArrangement(string text, Arrangement expected)
    {
        Assert.True(Wire.TryParseArrangement(text, out var a));
        Assert.Equal(expected, a);
    }

    [Fact]
    public void ArrangementFailureAndRoundTrip()
    {
        Assert.False(Wire.TryParseArrangement("blob", out var a));
        Assert.Equal(Arrangement.Line, a);
        Assert.False(Wire.TryParseArrangement(null, out _));
        foreach (var x in Enum.GetValues<Arrangement>())
        {
            Assert.True(Wire.TryParseArrangement(Wire.ToWire(x), out var back));
            Assert.Equal(x, back);
        }
    }

    [Theory]
    [InlineData("at_will", FireOrder.AtWill)]
    [InlineData("fire at will", FireOrder.AtWill)]
    [InlineData("fire", FireOrder.AtWill)]
    [InlineData("hold", FireOrder.Hold)]
    [InlineData("Hold-Fire", FireOrder.Hold)]
    public void ParseFire(string text, FireOrder expected)
    {
        Assert.True(Wire.TryParseFire(text, out var f));
        Assert.Equal(expected, f);
    }

    [Fact]
    public void FireFailureAndRoundTrip()
    {
        Assert.False(Wire.TryParseFire("maybe", out var f));
        Assert.Equal(FireOrder.AtWill, f);
        foreach (var x in Enum.GetValues<FireOrder>())
        {
            Assert.True(Wire.TryParseFire(Wire.ToWire(x), out var back));
            Assert.Equal(x, back);
        }
    }

    [Theory]
    [InlineData("left", FlankSide.Left)]
    [InlineData(" RIGHT ", FlankSide.Right)]
    public void ParseSide(string text, FlankSide expected)
    {
        Assert.True(Wire.TryParseSide(text, out var s));
        Assert.Equal(expected, s);
        Assert.Equal(expected, Enum.Parse<FlankSide>(Wire.ToWire(expected), true));
    }

    [Fact]
    public void SideFailure()
    {
        Assert.False(Wire.TryParseSide("up", out var s));
        Assert.Equal(FlankSide.Left, s);
        Assert.False(Wire.TryParseSide(null, out _));
    }

    [Theory]
    [InlineData("aggressive", Stance.Aggressive)]
    [InlineData("Balanced", Stance.Balanced)]
    [InlineData("defensive", Stance.Defensive)]
    [InlineData("delaying", Stance.Delaying)]
    [InlineData("withdraw", Stance.Withdraw)]
    public void ParseStance(string text, Stance expected)
    {
        Assert.True(Wire.TryParseStance(text, out var s));
        Assert.Equal(expected, s);
        Assert.Equal(text.ToLowerInvariant(), Wire.ToWire(expected));
    }

    [Fact]
    public void StanceFailure()
    {
        Assert.False(Wire.TryParseStance("berserk", out var s));
        Assert.Equal(Stance.Balanced, s);
    }

    [Fact]
    public void ConditionParsingAndRoundTrip()
    {
        foreach (var c in Enum.GetValues<ConditionType>())
        {
            Assert.True(Wire.TryParseCondition(Wire.ToWire(c), out var back));
            Assert.Equal(c, back);
        }
        Assert.True(Wire.TryParseCondition("Enemy Within", out var e));
        Assert.Equal(ConditionType.EnemyWithin, e);
        Assert.False(Wire.TryParseCondition("moon_phase", out var bad));
        Assert.Equal(ConditionType.TimeElapsed, bad);
        Assert.False(Wire.TryParseCondition(null, out _));
    }
}
