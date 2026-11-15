using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using BannerlordAI.Core.Model;

namespace BannerlordAI.Core.Planning;

public sealed class ParseOutcome<T> where T : class
{
    public T? Value { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool Ok => Value != null && Errors.Count == 0;
}

public sealed class LieutenantOrders
{
    public List<string> FireContingencies { get; init; } = new();
    /// <summary>Patches for existing directives. Null fields mean "unchanged".</summary>
    public List<DirectivePatch> Adjustments { get; init; } = new();
    public bool WakeCommander { get; init; }
    public string Reason { get; init; } = "";
    public string? OrderLog { get; init; }

    public bool IsNoOp => FireContingencies.Count == 0 && Adjustments.Count == 0 && !WakeCommander;
}

public sealed class DirectivePatch
{
    public string FormationId { get; init; } = "";
    public DirectiveAction Action { get; init; }
    public string? Target { get; init; }
    public FlankSide? Side { get; init; }
    public Anchor? Anchor { get; init; }
    public Arrangement? Arrangement { get; init; }
    public FireOrder? Fire { get; init; }
}

/// <summary>Parses and validates model JSON into domain objects. Never throws on bad model output.</summary>
public static class PlanParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Pulls the JSON object out of a reply that may be wrapped in prose or ``` fences.</summary>
    public static string? ExtractJsonObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var start = text!.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return text.Substring(start, end - start + 1);
    }

    public static ParseOutcome<Plan> ParseCommander(string? raw, BattleSnapshot basis, int version, double issuedAt)
    {
        var outcome = new ParseOutcome<Plan>();
        var dto = Deserialize<CommanderPlanDto>(raw, outcome.Errors);
        if (dto == null) return outcome;

        if (!Wire.TryParseStance(dto.Stance, out var stance))
            outcome.Warnings.Add($"unknown stance '{dto.Stance}', using balanced");

        var plan = new Plan
        {
            Version = version,
            IssuedAt = issuedAt,
            BasisSequence = basis.Sequence,
            Intent = (dto.Intent ?? "").Trim(),
            Stance = stance,
            OrderLog = (dto.OrderLog ?? "").Trim(),
            WakeCommanderIf = (dto.WakeCommanderIf ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList(),
        };

        foreach (var d in dto.Directives ?? new List<DirectiveDto>())
        {
            var directive = ToDirective(d, basis, outcome.Warnings, "plan");
            if (directive == null) continue;
            if (plan.Directives.ContainsKey(directive.FormationId))
                outcome.Warnings.Add($"duplicate directive for {directive.FormationId}; last one wins");
            plan.Directives[directive.FormationId] = directive;
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var n = 0;
        foreach (var c in dto.Contingencies ?? new List<ContingencyDto>())
        {
            n++;
            var id = string.IsNullOrWhiteSpace(c.Id) ? "C" + n : c.Id!.Trim();
            if (!seenIds.Add(id))
            {
                id = id + "_" + n;
                seenIds.Add(id);
            }
            var contingency = ToContingency(c, id, basis, outcome.Warnings);
            if (contingency != null) plan.Contingencies.Add(contingency);
        }

        foreach (var own in basis.Own.Where(f => f.IsAlive))
            if (!plan.Directives.ContainsKey(own.Id))
                outcome.Warnings.Add($"no directive for {own.Id}; it keeps its previous orders");

        if (plan.Directives.Count == 0 && plan.Contingencies.Count == 0)
            outcome.Errors.Add("plan contains no valid directives or contingencies");

        outcome.Value = plan;
        return outcome;
    }

    public static ParseOutcome<LieutenantOrders> ParseLieutenant(string? raw, BattleSnapshot basis, Plan plan)
    {
        var outcome = new ParseOutcome<LieutenantOrders>();
        var dto = Deserialize<LieutenantOrdersDto>(raw, outcome.Errors);
        if (dto == null) return outcome;

        var fire = new List<string>();
        foreach (var id in dto.FireContingencies ?? new List<string>())
        {
            var match = plan.Contingencies.FirstOrDefault(c => string.Equals(c.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match == null) outcome.Warnings.Add($"unknown contingency '{id}'");
            else if (!match.IsArmed) outcome.Warnings.Add($"contingency {match.Id} already fired");
            else fire.Add(match.Id);
        }

        var patches = new List<DirectivePatch>();
        foreach (var d in dto.Adjustments ?? new List<DirectiveDto>())
        {
            var directive = ToDirective(d, basis, outcome.Warnings, "lieutenant");
            if (directive == null) continue;
            if (!plan.Directives.ContainsKey(directive.FormationId))
            {
                outcome.Warnings.Add($"lieutenant adjusted {directive.FormationId}, which has no directive in the plan; ignored");
                continue;
            }
            patches.Add(new DirectivePatch
            {
                FormationId = directive.FormationId,
                Action = directive.Action,
                Target = directive.Target,
                Side = directive.Side,
                Anchor = directive.Anchor,
                Arrangement = directive.Arrangement,
                Fire = directive.Fire,
            });
        }

        outcome.Value = new LieutenantOrders
        {
            FireContingencies = fire,
            Adjustments = patches,
            WakeCommander = dto.WakeCommander ?? false,
            Reason = (dto.Reason ?? "").Trim(),
            OrderLog = string.IsNullOrWhiteSpace(dto.OrderLog) ? null : dto.OrderLog!.Trim(),
        };
        return outcome;
    }

    private static T? Deserialize<T>(string? raw, List<string> errors) where T : class
    {
        var json = ExtractJsonObject(raw);
        if (json == null)
        {
            errors.Add("response contained no JSON object");
            return null;
        }
        try
        {
            var value = JsonSerializer.Deserialize<T>(json, Options);
            if (value == null) errors.Add("response JSON was null");
            return value;
        }
        catch (JsonException ex)
        {
            errors.Add("invalid JSON: " + ex.Message);
            return null;
        }
    }

    internal static Directive? ToDirective(DirectiveDto d, BattleSnapshot basis, List<string> warnings, string source)
    {
        var self = basis.Find(d.Formation?.Trim());
        if (self == null)
        {
            warnings.Add($"directive for unknown formation '{d.Formation}' dropped");
            return null;
        }
        if (self.Allegiance != Allegiance.Own)
        {
            warnings.Add($"directive for {self.Id}, which is not ours, dropped");
            return null;
        }

        if (!Wire.TryParseAction(d.Action, out var action))
        {
            warnings.Add($"{self.Id}: unknown action '{d.Action}', using hold");
            action = DirectiveAction.Hold;
        }

        string? target = string.IsNullOrWhiteSpace(d.Target) ? null : d.Target!.Trim();
        if (target != null && !Selectors.IsValid(target, basis))
        {
            warnings.Add($"{self.Id}: unknown target '{target}', using default");
            target = null;
        }

        FlankSide? side = null;
        if (!string.IsNullOrWhiteSpace(d.Side))
        {
            if (Wire.TryParseSide(d.Side, out var s)) side = s;
            else warnings.Add($"{self.Id}: unknown side '{d.Side}'");
        }

        Arrangement? arrangement = null;
        if (!string.IsNullOrWhiteSpace(d.Arrangement))
        {
            if (Wire.TryParseArrangement(d.Arrangement, out var a)) arrangement = a;
            else warnings.Add($"{self.Id}: unknown arrangement '{d.Arrangement}'");
        }

        FireOrder? fire = null;
        if (!string.IsNullOrWhiteSpace(d.Fire))
        {
            if (Wire.TryParseFire(d.Fire, out var f)) fire = f;
            else warnings.Add($"{self.Id}: unknown fire order '{d.Fire}'");
        }

        return new Directive
        {
            FormationId = self.Id,
            Action = action,
            Target = target,
            Side = side,
            Anchor = ToAnchor(d.Anchor, basis, self.Id, warnings),
            Arrangement = arrangement,
            Fire = fire,
            Source = source,
        };
    }

    internal static Anchor? ToAnchor(AnchorDto? a, BattleSnapshot basis, string formationId, List<string> warnings)
    {
        if (a == null) return null;
        if (a.X == null || a.Y == null)
        {
            warnings.Add($"{formationId}: anchor without x/y ignored");
            return null;
        }
        var local = new Vec2((float)a.X.Value, (float)a.Y.Value);
        var mode = Wire.Normalize(a.Mode);
        var relativeTo = string.IsNullOrWhiteSpace(a.RelativeTo) ? null : a.RelativeTo!.Trim();

        if (mode == "relative" || (mode.Length == 0 && relativeTo != null))
        {
            var reference = basis.Find(relativeTo);
            if (reference == null)
            {
                warnings.Add($"{formationId}: anchor relative to unknown '{relativeTo}' ignored");
                return null;
            }
            return Anchor.Relative(reference.Id, local);
        }

        return Anchor.AbsoluteWorld(basis.Frame.ToWorld(local));
    }

    internal static Contingency? ToContingency(ContingencyDto c, string id, BattleSnapshot basis, List<string> warnings)
    {
        var conditions = new List<Condition>();
        foreach (var cd in c.Conditions ?? new List<ConditionDto>())
        {
            if (!Wire.TryParseCondition(cd.Type, out var type))
            {
                warnings.Add($"{id}: unknown condition '{cd.Type}' dropped");
                continue;
            }
            var subject = string.IsNullOrWhiteSpace(cd.Subject) ? null : cd.Subject!.Trim();
            var other = string.IsNullOrWhiteSpace(cd.Other) ? null : cd.Other!.Trim();
            var needsSubject = type != ConditionType.TimeElapsed && type != ConditionType.StrengthRatioBelow;
            if (needsSubject && (subject == null || !Selectors.IsValid(subject, basis)))
            {
                warnings.Add($"{id}: condition {Wire.ToWire(type)} has invalid subject '{subject}', dropped");
                continue;
            }
            if (other != null && !Selectors.IsValid(other, basis))
            {
                warnings.Add($"{id}: condition {Wire.ToWire(type)} has invalid other '{other}', using enemy:any");
                other = null;
            }
            var needsValue = type != ConditionType.Engaged && type != ConditionType.Routing;
            if (needsValue && cd.Value == null)
            {
                warnings.Add($"{id}: condition {Wire.ToWire(type)} has no value, dropped");
                continue;
            }
            conditions.Add(new Condition { Type = type, Subject = subject, Other = other, Value = cd.Value ?? 0 });
        }

        var directives = new List<Directive>();
        foreach (var dd in c.Directives ?? new List<DirectiveDto>())
        {
            var d = ToDirective(dd, basis, warnings, "contingency " + id);
            if (d != null) directives.Add(d);
        }

        if (conditions.Count == 0 || directives.Count == 0)
        {
            warnings.Add($"contingency {id} has no usable conditions or directives; dropped");
            return null;
        }

        return new Contingency
        {
            Id = id,
            Description = (c.Description ?? "").Trim(),
            MatchAll = Wire.Normalize(c.Match) != "any",
            Conditions = conditions,
            Directives = directives,
        };
    }
}
