using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BannerlordAI.Core.Planning;

// Wire shapes of model output. Every field is a plain string/number so a bad value becomes a validation warning,
// not a deserialization exception. Must stay in sync with PlanSchemas (a unit test checks the property names).

public sealed class AnchorDto
{
    [JsonPropertyName("mode")] public string? Mode { get; set; }
    [JsonPropertyName("x")] public double? X { get; set; }
    [JsonPropertyName("y")] public double? Y { get; set; }
    [JsonPropertyName("relative_to")] public string? RelativeTo { get; set; }
}

public sealed class DirectiveDto
{
    [JsonPropertyName("formation")] public string? Formation { get; set; }
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("target")] public string? Target { get; set; }
    [JsonPropertyName("side")] public string? Side { get; set; }
    [JsonPropertyName("anchor")] public AnchorDto? Anchor { get; set; }
    [JsonPropertyName("arrangement")] public string? Arrangement { get; set; }
    [JsonPropertyName("fire")] public string? Fire { get; set; }
}

public sealed class ConditionDto
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("subject")] public string? Subject { get; set; }
    [JsonPropertyName("other")] public string? Other { get; set; }
    [JsonPropertyName("value")] public double? Value { get; set; }
}

public sealed class ContingencyDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("match")] public string? Match { get; set; }
    [JsonPropertyName("conditions")] public List<ConditionDto>? Conditions { get; set; }
    [JsonPropertyName("directives")] public List<DirectiveDto>? Directives { get; set; }
}

public sealed class CommanderPlanDto
{
    [JsonPropertyName("intent")] public string? Intent { get; set; }
    [JsonPropertyName("stance")] public string? Stance { get; set; }
    [JsonPropertyName("directives")] public List<DirectiveDto>? Directives { get; set; }
    [JsonPropertyName("contingencies")] public List<ContingencyDto>? Contingencies { get; set; }
    [JsonPropertyName("wake_commander_if")] public List<string>? WakeCommanderIf { get; set; }
    [JsonPropertyName("order_log")] public string? OrderLog { get; set; }
}

public sealed class LieutenantOrdersDto
{
    [JsonPropertyName("fire_contingencies")] public List<string>? FireContingencies { get; set; }
    [JsonPropertyName("adjustments")] public List<DirectiveDto>? Adjustments { get; set; }
    [JsonPropertyName("wake_commander")] public bool? WakeCommander { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonPropertyName("order_log")] public string? OrderLog { get; set; }
}
