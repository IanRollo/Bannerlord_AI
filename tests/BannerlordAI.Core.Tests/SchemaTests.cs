using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using BannerlordAI.Core.Model;
using BannerlordAI.Core.Planning;

namespace BannerlordAI.Core.Tests;

public class SchemaTests
{
    public static IEnumerable<object[]> Schemas() => new[]
    {
        new object[] { "commander", PlanSchemas.Commander },
        new object[] { "lieutenant", PlanSchemas.Lieutenant },
    };

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static IEnumerable<(string path, JsonElement obj)> ObjectSchemas(JsonElement e, string path = "$")
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            if (e.TryGetProperty("properties", out _)) yield return (path, e);
            foreach (var p in e.EnumerateObject())
                foreach (var x in ObjectSchemas(p.Value, path + "." + p.Name)) yield return x;
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in e.EnumerateArray())
                foreach (var x in ObjectSchemas(item, path + "[" + i++ + "]")) yield return x;
        }
    }

    private static string[] PropNames(JsonElement schema) => schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray();

    private static string[] JsonNames(Type dto) => dto.GetProperties()
        .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name).ToArray();

    [Theory]
    [MemberData(nameof(Schemas))]
    public void SchemaIsValidJson(string name, string json)
    {
        var root = Parse(json);
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.True(name.Length > 0);
    }

    [Theory]
    [MemberData(nameof(Schemas))]
    public void EveryObjectIsClosedAndFullyRequired(string name, string json)
    {
        var objects = ObjectSchemas(Parse(json)).ToList();
        Assert.True(objects.Count >= 5, name + ": expected root plus $defs");
        foreach (var (path, obj) in objects)
        {
            Assert.True(obj.TryGetProperty("additionalProperties", out var ap) && ap.ValueKind == JsonValueKind.False,
                $"{name} {path}: additionalProperties must be false");
            var required = obj.GetProperty("required").EnumerateArray().Select(x => x.GetString()!).OrderBy(x => x).ToArray();
            Assert.Equal(PropNames(obj).OrderBy(x => x).ToArray(), required);
        }
    }

    [Theory]
    [MemberData(nameof(Schemas))]
    public void ReferencesResolve(string name, string json)
    {
        var root = Parse(json);
        var defs = root.GetProperty("$defs");
        foreach (var r in Refs(root))
        {
            Assert.StartsWith("#/$defs/", r);
            Assert.True(defs.TryGetProperty(r.Substring("#/$defs/".Length), out _), name + ": dangling " + r);
        }
    }

    private static IEnumerable<string> Refs(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                if (p.Name == "$ref") yield return p.Value.GetString()!;
                else foreach (var r in Refs(p.Value)) yield return r;
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
            foreach (var i in e.EnumerateArray()) foreach (var r in Refs(i)) yield return r;
    }

    [Fact]
    public void CommanderRootMatchesDto() =>
        Assert.Equal(JsonNames(typeof(CommanderPlanDto)).OrderBy(x => x), PropNames(Parse(PlanSchemas.Commander)).OrderBy(x => x));

    [Fact]
    public void LieutenantRootMatchesDto() =>
        Assert.Equal(JsonNames(typeof(LieutenantOrdersDto)).OrderBy(x => x), PropNames(Parse(PlanSchemas.Lieutenant)).OrderBy(x => x));

    [Theory]
    [MemberData(nameof(Schemas))]
    public void DefsMatchDtos(string name, string json)
    {
        var defs = Parse(json).GetProperty("$defs");
        var map = new Dictionary<string, Type>
        {
            ["directive"] = typeof(DirectiveDto),
            ["anchor"] = typeof(AnchorDto),
            ["condition"] = typeof(ConditionDto),
            ["contingency"] = typeof(ContingencyDto),
        };
        Assert.Equal(map.Keys.OrderBy(x => x), defs.EnumerateObject().Select(p => p.Name).OrderBy(x => x));
        foreach (var kv in map)
            Assert.Equal(JsonNames(kv.Value).OrderBy(x => x), PropNames(defs.GetProperty(kv.Key)).OrderBy(x => x));
        Assert.True(name.Length > 0);
    }

    [Fact]
    public void AllDtoPropertiesHaveJsonNames()
    {
        foreach (var t in new[] { typeof(CommanderPlanDto), typeof(LieutenantOrdersDto), typeof(DirectiveDto), typeof(AnchorDto), typeof(ConditionDto), typeof(ContingencyDto) })
            foreach (var p in t.GetProperties())
                Assert.NotNull(p.GetCustomAttribute<JsonPropertyNameAttribute>());
    }

    private static string[] EnumValues(JsonElement prop)
    {
        if (prop.TryGetProperty("enum", out var e)) return e.EnumerateArray().Select(x => x.GetString()!).ToArray();
        foreach (var alt in prop.GetProperty("anyOf").EnumerateArray())
            if (alt.TryGetProperty("enum", out var e2)) return e2.EnumerateArray().Select(x => x.GetString()!).ToArray();
        throw new InvalidOperationException("no enum in " + prop);
    }

    private delegate bool TryParse(string? s);

    [Fact]
    public void EnumValuesInSchemaAllParseAndCoverEveryMember()
    {
        var root = Parse(PlanSchemas.Commander);
        var defs = root.GetProperty("$defs");

        void Check(string[] values, TryParse parse, IEnumerable<string> expectedWire, string label)
        {
            foreach (var v in values) Assert.True(parse(v), label + ": '" + v + "' does not parse");
            Assert.Equal(expectedWire.OrderBy(x => x), values.OrderBy(x => x));
        }

        var dir = defs.GetProperty("directive").GetProperty("properties");
        Check(EnumValues(dir.GetProperty("action")), s => Wire.TryParseAction(s, out _),
            Enum.GetValues<DirectiveAction>().Select(x => Wire.ToWire(x)), "action");
        Check(EnumValues(dir.GetProperty("arrangement")), s => Wire.TryParseArrangement(s, out _),
            Enum.GetValues<Arrangement>().Select(x => Wire.ToWire(x)), "arrangement");
        Check(EnumValues(dir.GetProperty("fire")), s => Wire.TryParseFire(s, out _),
            Enum.GetValues<FireOrder>().Select(x => Wire.ToWire(x)), "fire");
        Check(EnumValues(dir.GetProperty("side")), s => Wire.TryParseSide(s, out _),
            Enum.GetValues<FlankSide>().Select(x => Wire.ToWire(x)), "side");
        Check(EnumValues(defs.GetProperty("condition").GetProperty("properties").GetProperty("type")), s => Wire.TryParseCondition(s, out _),
            Enum.GetValues<ConditionType>().Select(x => Wire.ToWire(x)), "condition");
        Check(EnumValues(root.GetProperty("properties").GetProperty("stance")), s => Wire.TryParseStance(s, out _),
            Enum.GetValues<Stance>().Select(x => Wire.ToWire(x)), "stance");

        Assert.Equal(new[] { "all", "any" }, EnumValues(defs.GetProperty("contingency").GetProperty("properties").GetProperty("match")).OrderBy(x => x));
        Assert.Equal(new[] { "absolute", "relative" }, EnumValues(defs.GetProperty("anchor").GetProperty("properties").GetProperty("mode")).OrderBy(x => x));
    }

    [Fact]
    public void SchemaNamesAreProviderSafe()
    {
        Assert.Matches("^[a-zA-Z0-9_-]{1,64}$", PlanSchemas.CommanderName);
        Assert.Matches("^[a-zA-Z0-9_-]{1,64}$", PlanSchemas.LieutenantName);
        Assert.NotEqual(PlanSchemas.CommanderName, PlanSchemas.LieutenantName);
    }

    [Fact]
    public void CannedRepliesDeserializeIntoDtos()
    {
        var opts = new JsonSerializerOptions();
        Assert.NotNull(JsonSerializer.Deserialize<CommanderPlanDto>(TestData.CommanderJson, opts));
        Assert.NotNull(JsonSerializer.Deserialize<LieutenantOrdersDto>(TestData.LieutenantNoOp, opts));
    }
}
