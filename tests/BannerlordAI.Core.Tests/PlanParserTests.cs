using BannerlordAI.Core.Model;
using BannerlordAI.Core.Planning;
using static BannerlordAI.Core.Tests.TestData;

namespace BannerlordAI.Core.Tests;

public class PlanParserCommanderTests
{
    private static ParseOutcome<Plan> Parse(string? raw, BattleSnapshot? basis = null, int version = 1, double issuedAt = 10)
        => PlanParser.ParseCommander(raw, basis ?? Snap(seq: 42), version, issuedAt);

    private static string Plan(string directives, string contingencies = "") => Commander(directivesJson: directives, contingenciesJson: contingencies);

    [Fact]
    public void ValidPlanParsesEverything()
    {
        var o = Parse(CommanderJson);
        Assert.True(o.Ok);
        var plan = o.Value!;
        Assert.Equal(1, plan.Version);
        Assert.Equal(10, plan.IssuedAt);
        Assert.Equal(42, plan.BasisSequence);
        Assert.Equal("Hold the line, shoot, then hit with cavalry.", plan.Intent);
        Assert.Equal(Stance.Defensive, plan.Stance);
        Assert.Equal("Hold the line!", plan.OrderLog);
        Assert.Equal(new[] { "F1 below 50%" }, plan.WakeCommanderIf);
        Assert.Equal(3, plan.Directives.Count);

        var f1 = plan.Directives["F1"];
        Assert.Equal(DirectiveAction.Hold, f1.Action);
        Assert.Equal(Arrangement.ShieldWall, f1.Arrangement);
        Assert.False(f1.Anchor!.IsRelative);
        Assert.Equal(new Vec2(0, 40), f1.Anchor.Value);
        Assert.Equal("plan", f1.Source);

        var f2 = plan.Directives["F2"];
        Assert.Equal(DirectiveAction.Skirmish, f2.Action);
        Assert.Equal("enemy:infantry", f2.Target);
        Assert.Equal(FireOrder.AtWill, f2.Fire);
        Assert.True(f2.Anchor!.IsRelative);
        Assert.Equal("F1", f2.Anchor.RelativeTo);
        Assert.Equal(new Vec2(0, -25), f2.Anchor.Value);

        Assert.Single(plan.Contingencies);
        var c = plan.Contingencies[0];
        Assert.Equal("C1", c.Id);
        Assert.Equal("Cavalry strikes the archers", c.Description);
        Assert.True(c.MatchAll);
        Assert.True(c.IsArmed);
        Assert.Equal(ConditionType.Engaged, c.Conditions[0].Type);
        Assert.Equal("F1", c.Conditions[0].Subject);
        Assert.Equal("F3", c.Directives[0].FormationId);
        Assert.Equal(DirectiveAction.Charge, c.Directives[0].Action);
        Assert.Equal("contingency C1", c.Directives[0].Source);
        Assert.Empty(o.Warnings);
    }

    [Fact]
    public void ReplyWrappedInProseAndFencesIsExtracted()
    {
        var wrapped = "Here is my plan:\n```json\n" + CommanderJson + "\n```\nGood luck, commander!";
        Assert.True(Parse(wrapped).Ok);
        Assert.True(Parse("```\n" + CommanderJson + "\n```").Ok);
    }

    [Fact]
    public void ExtractJsonObject()
    {
        Assert.Equal("{\"a\":1}", PlanParser.ExtractJsonObject("x {\"a\":1} y"));
        Assert.Null(PlanParser.ExtractJsonObject("no braces"));
        Assert.Null(PlanParser.ExtractJsonObject(""));
        Assert.Null(PlanParser.ExtractJsonObject(null));
        Assert.Null(PlanParser.ExtractJsonObject("} reversed {"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I refuse to answer in JSON.")]
    public void NoJsonIsAnError(string? raw)
    {
        var o = Parse(raw);
        Assert.False(o.Ok);
        Assert.Null(o.Value);
        Assert.Contains(o.Errors, e => e.Contains("no JSON"));
    }

    [Fact]
    public void InvalidJsonIsAnErrorNotAnException()
    {
        var o = Parse("{ \"intent\": \"x\", \"directives\": [ { }");
        Assert.False(o.Ok);
        Assert.Null(o.Value);
        Assert.Contains(o.Errors, e => e.StartsWith("invalid JSON"));
    }

    [Fact]
    public void WrongTypesAreErrorsNotExceptions()
    {
        Assert.False(Parse("{\"directives\": \"nope\"}").Ok);
    }

    [Fact]
    public void EmptyPlanIsAnError()
    {
        var o = Parse("{}");
        Assert.False(o.Ok);
        Assert.Contains(o.Errors, e => e.Contains("no valid directives"));
        Assert.Null(Parse("null").Value);
    }

    [Fact]
    public void LenientJson()
    {
        var o = Parse("{ // c\n \"Intent\": \"x\", \"DIRECTIVES\": [ {\"formation\":\"F1\",\"action\":\"hold\",}, ], }");
        Assert.True(o.Ok);
        Assert.Equal("x", o.Value!.Intent);
    }

    [Fact]
    public void UnknownAndEnemyFormationsAreDroppedWithWarnings()
    {
        var o = Parse(Plan("""{"formation":"F9","action":"hold"},{"formation":"E1","action":"hold"},{"formation":"F1","action":"hold"}"""));
        Assert.True(o.Ok);
        Assert.Single(o.Value!.Directives);
        Assert.Contains(o.Warnings, w => w.Contains("unknown formation 'F9'"));
        Assert.Contains(o.Warnings, w => w.Contains("E1") && w.Contains("not ours"));
    }

    [Fact]
    public void FormationIdIsCaseInsensitiveAndNormalised()
    {
        var o = Parse(Plan("""{"formation":" f1 ","action":"hold"}"""));
        Assert.True(o.Value!.Directives.ContainsKey("F1"));
        Assert.Equal("F1", o.Value.Directives["F1"].FormationId);
    }

    [Fact]
    public void UnknownEnumsGetWarningsAndDefaults()
    {
        var o = Parse(Plan("""{"formation":"F1","action":"dance","arrangement":"blob","fire":"maybe","side":"up","target":"bogus:thing"}"""));
        var d = o.Value!.Directives["F1"];
        Assert.Equal(DirectiveAction.Hold, d.Action);
        Assert.Null(d.Arrangement);
        Assert.Null(d.Fire);
        Assert.Null(d.Side);
        Assert.Null(d.Target);
        Assert.Contains(o.Warnings, w => w.Contains("unknown action 'dance'"));
        Assert.Contains(o.Warnings, w => w.Contains("unknown arrangement 'blob'"));
        Assert.Contains(o.Warnings, w => w.Contains("unknown fire order 'maybe'"));
        Assert.Contains(o.Warnings, w => w.Contains("unknown side 'up'"));
        Assert.Contains(o.Warnings, w => w.Contains("unknown target 'bogus:thing'"));
    }

    [Fact]
    public void UnknownTargetFormationIdIsDropped()
    {
        var o = Parse(Plan("""{"formation":"F1","action":"charge","target":"E9"}"""));
        Assert.Null(o.Value!.Directives["F1"].Target);
        Assert.Contains(o.Warnings, w => w.Contains("unknown target 'E9'"));
    }

    [Fact]
    public void ValidTargetsAndAliasesAreKept()
    {
        var o = Parse(Plan("""{"formation":"F1","action":"Attack","target":" Enemy:Cavalry ","arrangement":"Shield Wall","fire":"hold","side":"Right"}"""));
        var d = o.Value!.Directives["F1"];
        Assert.Equal(DirectiveAction.Charge, d.Action);
        Assert.Equal("Enemy:Cavalry", d.Target);
        Assert.Equal(Arrangement.ShieldWall, d.Arrangement);
        Assert.Equal(FireOrder.Hold, d.Fire);
        Assert.Equal(FlankSide.Right, d.Side);
    }

    [Fact]
    public void DuplicateDirectivesLastOneWins()
    {
        var o = Parse(Plan("""{"formation":"F1","action":"hold"},{"formation":"F1","action":"charge"}"""));
        Assert.Equal(DirectiveAction.Charge, o.Value!.Directives["F1"].Action);
        Assert.Contains(o.Warnings, w => w.Contains("duplicate directive for F1"));
    }

    [Fact]
    public void MissingLivingFormationGetsWarningButDeadDoesNot()
    {
        var own = new[] { Form("F1", FormationRole.Infantry, 10, 0, 0), Form("F2", FormationRole.Ranged, 10, 0, -30), Form("F3", FormationRole.Cavalry, 0, 0, 0, initial: 5) };
        var o = Parse(Plan("""{"formation":"F1","action":"hold"}"""), Snap(own));
        Assert.Contains(o.Warnings, w => w.Contains("no directive for F2"));
        Assert.DoesNotContain(o.Warnings, w => w.Contains("no directive for F3"));
        Assert.DoesNotContain(o.Warnings, w => w.Contains("no directive for F1"));
    }

    [Fact]
    public void UnknownStanceWarnsAndDefaultsToBalanced()
    {
        var o = Parse("{\"stance\":\"berserk\",\"directives\":[{\"formation\":\"F1\",\"action\":\"hold\"}]}");
        Assert.Equal(Stance.Balanced, o.Value!.Stance);
        Assert.Contains(o.Warnings, w => w.Contains("unknown stance 'berserk'"));
    }

    // ------------------------------------------------------------ contingencies

    private static string Cont(string id, string conditions, string directives = """[{"formation":"F3","action":"charge"}]""", string match = "all")
        => $$"""{"id":{{System.Text.Json.JsonSerializer.Serialize(id)}},"description":"d","match":"{{match}}","conditions":{{conditions}},"directives":{{directives}}}""";

    private const string GoodCond = """[{"type":"engaged","subject":"F1"}]""";

    [Fact]
    public void ContingencyIdsDefaultAndDeduplicate()
    {
        var o = Parse(Plan("""{"formation":"F1","action":"hold"}""",
            string.Join(",", Cont("", GoodCond), Cont("", GoodCond), Cont("X", GoodCond), Cont("x", GoodCond))));
        Assert.Equal(new[] { "C1", "C2", "X", "x_4" }, o.Value!.Contingencies.Select(c => c.Id).ToArray());
    }

    [Fact]
    public void InvalidConditionsAreDroppedAndContingencyKeptIfOthersRemain()
    {
        var conds = """[{"type":"moon_phase","value":1},{"type":"engaged","subject":"F1"},{"type":"engaged","subject":"F9"},{"type":"strength_below","subject":"F1"}]""";
        var o = Parse(Plan("""{"formation":"F1","action":"hold"}""", Cont("C1", conds)));
        var c = Assert.Single(o.Value!.Contingencies);
        Assert.Single(c.Conditions);
        Assert.Contains(o.Warnings, w => w.Contains("unknown condition 'moon_phase'"));
        Assert.Contains(o.Warnings, w => w.Contains("invalid subject 'F9'"));
        Assert.Contains(o.Warnings, w => w.Contains("has no value"));
    }

    [Fact]
    public void ContingencyWithNoUsableConditionsIsDropped()
    {
        var o = Parse(Plan("""{"formation":"F1","action":"hold"}""", Cont("C1", """[{"type":"bogus"}]""")));
        Assert.Empty(o.Value!.Contingencies);
        Assert.Contains(o.Warnings, w => w.Contains("contingency C1 has no usable"));
        Assert.True(o.Ok); // the directive still makes the plan valid
    }

    [Fact]
    public void ContingencyWithNoUsableDirectivesIsDropped()
    {
        var o = Parse(Plan("""{"formation":"F1","action":"hold"}""", Cont("C1", GoodCond, """[{"formation":"F9","action":"charge"}]""")));
        Assert.Empty(o.Value!.Contingencies);
    }

    [Fact]
    public void ContingencyOnlyPlanIsValid()
    {
        var o = Parse(Plan("", Cont("C1", GoodCond)));
        Assert.True(o.Ok);
        Assert.Empty(o.Value!.Directives);
        Assert.Single(o.Value.Contingencies);
    }

    [Fact]
    public void ConditionDetails()
    {
        var conds = """
            [{"type":"time_elapsed","subject":null,"other":null,"value":30},
             {"type":"strength_ratio_below","value":"0.7"},
             {"type":"enemy_within","subject":"F2","other":"enemy:cavalry","value":60},
             {"type":"enemy_within","subject":"F2","other":"nonsense","value":60}]
            """;
        var o = Parse(Plan("""{"formation":"F1","action":"hold"}""", Cont("C1", conds, match: "any")));
        var c = o.Value!.Contingencies[0];
        Assert.False(c.MatchAll);
        Assert.Equal(4, c.Conditions.Count);
        Assert.Equal(30, c.Conditions[0].Value);
        Assert.Null(c.Conditions[0].Subject);
        Assert.Equal(0.7, c.Conditions[1].Value, 6);
        Assert.Equal("enemy:cavalry", c.Conditions[2].Other);
        Assert.Null(c.Conditions[3].Other);
        Assert.Contains(o.Warnings, w => w.Contains("invalid other 'nonsense'"));
    }

    [Fact]
    public void MatchDefaultsToAll()
    {
        var o = Parse(Plan("""{"formation":"F1","action":"hold"}""", Cont("C1", GoodCond, match: "weird")));
        Assert.True(o.Value!.Contingencies[0].MatchAll);
    }

    // ------------------------------------------------------------------ anchors

    private static Anchor? ParseAnchor(string anchorJson, BattleSnapshot? basis = null)
    {
        var o = Parse(Plan("{\"formation\":\"F1\",\"action\":\"hold\",\"anchor\":" + anchorJson + "}"), basis);
        return o.Value!.Directives["F1"].Anchor;
    }

    [Fact]
    public void AbsoluteAnchorIsConvertedToWorldUsingBasisFrame()
    {
        var anchor = ParseAnchor("""{"mode":"absolute","x":10,"y":50,"relative_to":null}""");
        Assert.False(anchor!.IsRelative);
        Assert.Equal(new Vec2(10, 50), anchor.Value);
    }

    [Fact]
    public void AbsoluteAnchorUsesRotatedFrame()
    {
        // Facing east from (10,20): local (x=5 right, y=30 ahead) -> 30 east, 5 south.
        var basis = Snap(frame: new BattleFrame(new Vec2(10, 20), new Vec2(1, 0)));
        var anchor = ParseAnchor("""{"mode":"absolute","x":5,"y":30,"relative_to":null}""", basis)!;
        Assert.Equal(40f, anchor.Value.X, 3);
        Assert.Equal(15f, anchor.Value.Y, 3);
    }

    [Fact]
    public void RelativeAnchorKeepsLocalOffsetEvenInRotatedFrame()
    {
        var basis = Snap(frame: new BattleFrame(new Vec2(10, 20), new Vec2(1, 0)));
        var anchor = ParseAnchor("""{"mode":"relative","x":5,"y":-30,"relative_to":"f2"}""", basis)!;
        Assert.True(anchor.IsRelative);
        Assert.Equal("F2", anchor.RelativeTo);
        Assert.Equal(new Vec2(5, -30), anchor.Value);
    }

    [Fact]
    public void AnchorModeInference()
    {
        Assert.True(ParseAnchor("""{"x":1,"y":2,"relative_to":"F2"}""")!.IsRelative);
        Assert.False(ParseAnchor("""{"x":1,"y":2}""")!.IsRelative);
        Assert.False(ParseAnchor("""{"mode":"absolute","x":1,"y":2,"relative_to":"F2"}""")!.IsRelative);
    }

    [Fact]
    public void BadAnchorsAreIgnoredWithWarnings()
    {
        var o = Parse(Plan("""{"formation":"F1","action":"hold","anchor":{"mode":"absolute","x":1}}"""));
        Assert.Null(o.Value!.Directives["F1"].Anchor);
        Assert.Contains(o.Warnings, w => w.Contains("anchor without x/y"));

        var o2 = Parse(Plan("""{"formation":"F1","action":"hold","anchor":{"mode":"relative","x":1,"y":2,"relative_to":"F9"}}"""));
        Assert.Null(o2.Value!.Directives["F1"].Anchor);
        Assert.Contains(o2.Warnings, w => w.Contains("relative to unknown 'F9'"));

        Assert.Null(ParseAnchor("""{"mode":"relative","x":1,"y":2,"relative_to":null}"""));
    }

    [Fact]
    public void NumbersMayArriveAsStrings() =>
        Assert.Equal(new Vec2(12, 7), ParseAnchor("""{"mode":"absolute","x":"12","y":"7"}""")!.Value);
}

public class PlanParserLieutenantTests
{
    private static Plan MakePlan(BattleSnapshot? basis = null, string? json = null)
        => PlanParser.ParseCommander(json ?? CommanderJson, basis ?? Snap(), 1, 0).Value!;

    private static ParseOutcome<LieutenantOrders> Parse(string? raw, Plan plan, BattleSnapshot? basis = null)
        => PlanParser.ParseLieutenant(raw, basis ?? Snap(), plan);

    [Fact]
    public void NoOpReply()
    {
        var o = Parse(LieutenantNoOp, MakePlan());
        Assert.True(o.Ok);
        Assert.True(o.Value!.IsNoOp);
        Assert.False(o.Value.WakeCommander);
        Assert.Null(o.Value.OrderLog);
    }

    [Fact]
    public void FireContingencyMatchedCaseInsensitivelyToRealId()
    {
        var o = Parse(Lieutenant(fire: "\" c1 \""), MakePlan());
        Assert.Equal(new[] { "C1" }, o.Value!.FireContingencies);
        Assert.False(o.Value.IsNoOp);
    }

    [Fact]
    public void UnknownAndAlreadyFiredContingenciesAreIgnored()
    {
        var plan = MakePlan();
        var o = Parse(Lieutenant(fire: "\"C9\""), plan);
        Assert.Empty(o.Value!.FireContingencies);
        Assert.Contains(o.Warnings, w => w.Contains("unknown contingency 'C9'"));

        plan.Contingencies[0].FiredAt = 5;
        var o2 = Parse(Lieutenant(fire: "\"C1\""), plan);
        Assert.Empty(o2.Value!.FireContingencies);
        Assert.Contains(o2.Warnings, w => w.Contains("already fired"));
    }

    [Fact]
    public void AdjustmentsForFormationsNotInPlanAreIgnored()
    {
        var plan = MakePlan(json: Commander(directivesJson: """{"formation":"F1","action":"hold"}"""));
        var o = Parse(Lieutenant(adjustments: """{"formation":"F2","action":"hold"},{"formation":"F1","action":"hold","arrangement":"loose"},{"formation":"F9","action":"hold"}"""), plan);
        var patch = Assert.Single(o.Value!.Adjustments);
        Assert.Equal("F1", patch.FormationId);
        Assert.Equal(Arrangement.Loose, patch.Arrangement);
        Assert.Contains(o.Warnings, w => w.Contains("F2") && w.Contains("no directive in the plan"));
        Assert.Contains(o.Warnings, w => w.Contains("unknown formation 'F9'"));
    }

    [Fact]
    public void PatchCarriesAnchorAndTarget()
    {
        var o = Parse(Lieutenant(adjustments: """{"formation":"F1","action":"hold","target":"enemy:cavalry","anchor":{"mode":"absolute","x":5,"y":6}}"""), MakePlan());
        var p = o.Value!.Adjustments[0];
        Assert.Equal("enemy:cavalry", p.Target);
        Assert.Equal(new Vec2(5, 6), p.Anchor!.Value);
    }

    [Fact]
    public void WakeReasonAndOrderLog()
    {
        var o = Parse(Lieutenant(wake: true, reason: "  flank threatened ", orderLog: " Watch the left! "), MakePlan());
        Assert.True(o.Value!.WakeCommander);
        Assert.Equal("flank threatened", o.Value.Reason);
        Assert.Equal("Watch the left!", o.Value.OrderLog);
        Assert.False(o.Value.IsNoOp);
    }

    [Fact]
    public void BlankOrderLogBecomesNull() =>
        Assert.Null(Parse(Lieutenant(orderLog: "   "), MakePlan()).Value!.OrderLog);

    [Fact]
    public void BadReplies()
    {
        var plan = MakePlan();
        Assert.False(Parse("nope", plan).Ok);
        Assert.False(Parse("{ broken", plan).Ok);
        Assert.False(Parse(null, plan).Ok);
        Assert.True(Parse("{}", plan).Ok); // empty object = no-op
    }
}
