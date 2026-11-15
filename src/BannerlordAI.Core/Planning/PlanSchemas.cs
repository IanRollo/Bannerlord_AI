namespace BannerlordAI.Core.Planning;

/// <summary>
/// JSON schemas for structured output. Written to the subset both Anthropic structured outputs and OpenAI strict mode
/// accept: every object has additionalProperties:false and lists all properties as required (optional values are
/// nullable via anyOf), no numeric/string constraints, no recursion.
/// </summary>
public static class PlanSchemas
{
    public const string CommanderName = "battle_plan";
    public const string LieutenantName = "lieutenant_orders";

    private const string Defs = """
      "$defs": {
        "anchor": {
          "type": "object",
          "additionalProperties": false,
          "required": ["mode", "x", "y", "relative_to"],
          "properties": {
            "mode": { "type": "string", "enum": ["absolute", "relative"] },
            "x": { "type": "number" },
            "y": { "type": "number" },
            "relative_to": { "anyOf": [ { "type": "string" }, { "type": "null" } ] }
          }
        },
        "directive": {
          "type": "object",
          "additionalProperties": false,
          "required": ["formation", "action", "target", "side", "anchor", "arrangement", "fire"],
          "properties": {
            "formation": { "type": "string" },
            "action": { "type": "string", "enum": ["hold", "advance", "charge", "skirmish", "flank", "protect_flank", "screen", "reserve", "retreat", "follow"] },
            "target": { "anyOf": [ { "type": "string" }, { "type": "null" } ] },
            "side": { "anyOf": [ { "type": "string", "enum": ["left", "right"] }, { "type": "null" } ] },
            "anchor": { "anyOf": [ { "$ref": "#/$defs/anchor" }, { "type": "null" } ] },
            "arrangement": { "anyOf": [ { "type": "string", "enum": ["line", "shieldwall", "loose", "square", "circle", "column", "scatter"] }, { "type": "null" } ] },
            "fire": { "anyOf": [ { "type": "string", "enum": ["at_will", "hold"] }, { "type": "null" } ] }
          }
        },
        "condition": {
          "type": "object",
          "additionalProperties": false,
          "required": ["type", "subject", "other", "value"],
          "properties": {
            "type": { "type": "string", "enum": ["enemy_within", "strength_below", "engaged", "routing", "time_elapsed", "strength_ratio_below"] },
            "subject": { "anyOf": [ { "type": "string" }, { "type": "null" } ] },
            "other": { "anyOf": [ { "type": "string" }, { "type": "null" } ] },
            "value": { "anyOf": [ { "type": "number" }, { "type": "null" } ] }
          }
        },
        "contingency": {
          "type": "object",
          "additionalProperties": false,
          "required": ["id", "description", "match", "conditions", "directives"],
          "properties": {
            "id": { "type": "string" },
            "description": { "type": "string" },
            "match": { "type": "string", "enum": ["all", "any"] },
            "conditions": { "type": "array", "items": { "$ref": "#/$defs/condition" } },
            "directives": { "type": "array", "items": { "$ref": "#/$defs/directive" } }
          }
        }
      }
""";

    public const string Commander = """
{
  "type": "object",
  "additionalProperties": false,
  "required": ["intent", "stance", "directives", "contingencies", "wake_commander_if", "order_log"],
  "properties": {
    "intent": { "type": "string" },
    "stance": { "type": "string", "enum": ["aggressive", "balanced", "defensive", "delaying", "withdraw"] },
    "directives": { "type": "array", "items": { "$ref": "#/$defs/directive" } },
    "contingencies": { "type": "array", "items": { "$ref": "#/$defs/contingency" } },
    "wake_commander_if": { "type": "array", "items": { "type": "string" } },
    "order_log": { "type": "string" }
  },
""" + Defs + """
}
""";

    public const string Lieutenant = """
{
  "type": "object",
  "additionalProperties": false,
  "required": ["fire_contingencies", "adjustments", "wake_commander", "reason", "order_log"],
  "properties": {
    "fire_contingencies": { "type": "array", "items": { "type": "string" } },
    "adjustments": { "type": "array", "items": { "$ref": "#/$defs/directive" } },
    "wake_commander": { "type": "boolean" },
    "reason": { "type": "string" },
    "order_log": { "anyOf": [ { "type": "string" }, { "type": "null" } ] }
  },
""" + Defs + """
}
""";
}
