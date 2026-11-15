using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BannerlordAI.Core.Llm;
using BannerlordAI.Core.Planning;

namespace BannerlordAI.Core.Tests;

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

    public FakeHandler(HttpStatusCode status, string body)
        : this((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") })) { }

    public HttpRequestMessage? Request { get; private set; }
    public string? Body { get; private set; }
    public int Calls { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Request = request;
        Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return await _respond(request, cancellationToken);
    }

    public JsonNode Json => JsonNode.Parse(Body!)!;
}

public class OpenAiCompatLlmClientTests
{
    private const string OkBody = """
    {"choices":[{"index":0,"message":{"role":"assistant","content":"{\"ok\":true}"},"finish_reason":"stop"}],
     "usage":{"prompt_tokens":120,"completion_tokens":30,"prompt_tokens_details":{"cached_tokens":100}}}
    """;

    private static LlmRequest Req(int timeoutMs = 5000) => new()
    {
        Model = "gpt-test",
        SystemPrompt = "SYS",
        UserContent = "USER",
        SchemaName = PlanSchemas.CommanderName,
        SchemaJson = PlanSchemas.Commander,
        MaxTokens = 1234,
        Timeout = TimeSpan.FromMilliseconds(timeoutMs),
    };

    private static (OpenAiCompatLlmClient client, FakeHandler handler) Make(
        JsonOutputMode mode = JsonOutputMode.Schema, string field = "max_tokens", string? key = "sk-test", string baseUrl = "https://example.test/v1/",
        HttpStatusCode status = HttpStatusCode.OK, string body = OkBody)
    {
        var h = new FakeHandler(status, body);
        return (new OpenAiCompatLlmClient(baseUrl, key, mode, field, h), h);
    }

    [Fact]
    public async Task PostsToChatCompletionsWithBearerAuth()
    {
        var (c, h) = Make();
        var r = await c.CompleteJsonAsync(Req(), CancellationToken.None);
        Assert.True(r.Success);
        Assert.Equal(HttpMethod.Post, h.Request!.Method);
        Assert.Equal("https://example.test/v1/chat/completions", h.Request.RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", h.Request.Headers.Authorization!.ToString());
        Assert.Equal("openai-compatible", c.ProviderName);
    }

    [Fact]
    public async Task NoApiKeyMeansNoAuthHeader()
    {
        var (c, h) = Make(key: null, baseUrl: "http://localhost:11434/v1");
        await c.CompleteJsonAsync(Req(), CancellationToken.None);
        Assert.Null(h.Request!.Headers.Authorization);
        Assert.Equal("http://localhost:11434/v1/chat/completions", h.Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task SchemaModeBody()
    {
        var (c, h) = Make();
        await c.CompleteJsonAsync(Req(), CancellationToken.None);
        var b = h.Json;
        Assert.Equal("gpt-test", (string?)b["model"]);
        Assert.Equal(1234, (int?)b["max_tokens"]);
        var msgs = b["messages"]!.AsArray();
        Assert.Equal(2, msgs.Count);
        Assert.Equal("system", (string?)msgs[0]!["role"]);
        Assert.Equal("SYS", (string?)msgs[0]!["content"]);
        Assert.Equal("user", (string?)msgs[1]!["role"]);
        Assert.Equal("USER", (string?)msgs[1]!["content"]);

        var rf = b["response_format"]!;
        Assert.Equal("json_schema", (string?)rf["type"]);
        Assert.Equal("battle_plan", (string?)rf["json_schema"]!["name"]);
        Assert.True((bool?)rf["json_schema"]!["strict"]);
        Assert.False((bool?)rf["json_schema"]!["schema"]!["additionalProperties"]);
        Assert.NotNull(rf["json_schema"]!["schema"]!["$defs"]);
    }

    [Fact]
    public async Task CustomMaxTokensFieldName()
    {
        var (c, h) = Make(field: "max_completion_tokens");
        await c.CompleteJsonAsync(Req(), CancellationToken.None);
        Assert.Equal(1234, (int?)h.Json["max_completion_tokens"]);
        Assert.Null(h.Json["max_tokens"]);
    }

    [Fact]
    public async Task JsonObjectModeAppendsSchemaToSystemPrompt()
    {
        var (c, h) = Make(JsonOutputMode.JsonObject);
        await c.CompleteJsonAsync(Req(), CancellationToken.None);
        var b = h.Json;
        Assert.Equal("json_object", (string?)b["response_format"]!["type"]);
        Assert.Null(b["response_format"]!["json_schema"]);
        var system = (string)b["messages"]![0]!["content"]!;
        Assert.StartsWith("SYS", system);
        Assert.Contains("JSON schema", system);
        Assert.Contains("wake_commander_if", system);
        Assert.Equal("USER", (string?)b["messages"]![1]!["content"]);
    }

    [Fact]
    public async Task PromptOnlyModeSendsNoResponseFormat()
    {
        var (c, h) = Make(JsonOutputMode.PromptOnly);
        await c.CompleteJsonAsync(Req(), CancellationToken.None);
        Assert.Null(h.Json["response_format"]);
        Assert.Contains("wake_commander_if", (string)h.Json["messages"]![0]!["content"]!);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "429")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    [InlineData(HttpStatusCode.Unauthorized, "401")]
    public async Task NonSuccessStatusFails(HttpStatusCode status, string code)
    {
        var (c, _) = Make(status: status, body: "{\"error\":\"nope\"}");
        var r = await c.CompleteJsonAsync(Req(), CancellationToken.None);
        Assert.False(r.Success);
        Assert.Contains(code, r.Error);
        Assert.Contains("nope", r.Error);
    }

    [Fact]
    public async Task LongErrorBodiesAreTruncated()
    {
        var (c, _) = Make(status: HttpStatusCode.BadGateway, body: new string('x', 5000));
        var r = await c.CompleteJsonAsync(Req(), CancellationToken.None);
        Assert.True(r.Error!.Length < 400);
    }

    [Fact]
    public async Task NonJsonSuccessBodyFailsWithoutThrowing()
    {
        var (c, _) = Make(body: "<html>oops</html>");
        var r = await c.CompleteJsonAsync(Req(), CancellationToken.None);
        Assert.False(r.Success);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public async Task NetworkExceptionBecomesFailure()
    {
        var h = new FakeHandler((_, _) => throw new HttpRequestException("boom"));
        var r = await new OpenAiCompatLlmClient("https://x.test", "k", handler: h).CompleteJsonAsync(Req(), CancellationToken.None);
        Assert.False(r.Success);
        Assert.Contains("boom", r.Error);
    }

    [Fact]
    public async Task CallerCancellationReportsCancelled()
    {
        var h = new FakeHandler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); });
        using var cts = new CancellationTokenSource();
        var task = new OpenAiCompatLlmClient("https://x.test", "k", handler: h).CompleteJsonAsync(Req(), cts.Token);
        cts.Cancel();
        var r = await task;
        Assert.False(r.Success);
        Assert.Equal("cancelled", r.Error);
    }

    [Fact]
    public async Task RequestTimeoutReportsTimedOut()
    {
        var h = new FakeHandler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); });
        var r = await new OpenAiCompatLlmClient("https://x.test", "k", handler: h).CompleteJsonAsync(Req(timeoutMs: 50), CancellationToken.None);
        Assert.False(r.Success);
        Assert.Equal("timed out", r.Error);
    }

    [Fact]
    public async Task SuccessfulResultCarriesTextAndUsage()
    {
        var (c, _) = Make();
        var r = await c.CompleteJsonAsync(Req(), CancellationToken.None);
        Assert.True(r.Success);
        Assert.Equal("{\"ok\":true}", r.Text);
        Assert.Equal(120, r.InputTokens);
        Assert.Equal(30, r.OutputTokens);
        Assert.Equal(100, r.CacheReadTokens);
        Assert.Equal("stop", r.StopReason);
        Assert.True(r.LatencySeconds >= 0);
    }

    // ------------------------------------------------------------ ParseResponse

    [Fact]
    public void ParseResponseContentAndUsage()
    {
        var r = OpenAiCompatLlmClient.ParseResponse(OkBody, 1.5);
        Assert.True(r.Success);
        Assert.Null(r.Error);
        Assert.Equal(1.5, r.LatencySeconds);
        Assert.Equal(120, r.InputTokens);
        Assert.Equal(100, r.CacheReadTokens);
    }

    [Fact]
    public void ParseResponseWithoutUsageHasZeroTokens()
    {
        var r = OpenAiCompatLlmClient.ParseResponse("""{"choices":[{"message":{"content":"{}"},"finish_reason":"stop"}]}""", 0);
        Assert.True(r.Success);
        Assert.Equal(0, r.InputTokens);
        Assert.Equal(0, r.OutputTokens);
        Assert.Equal(0, r.CacheReadTokens);
    }

    [Fact]
    public void FinishReasonLengthIsFailure()
    {
        var r = OpenAiCompatLlmClient.ParseResponse("""{"choices":[{"message":{"content":"{\"a\":"},"finish_reason":"length"}],"usage":{"prompt_tokens":5,"completion_tokens":6}}""", 0);
        Assert.False(r.Success);
        Assert.Equal("hit max tokens", r.Error);
        Assert.Equal("length", r.StopReason);
        Assert.Equal(6, r.OutputTokens);
    }

    [Fact]
    public void RefusalIsFailure()
    {
        var r = OpenAiCompatLlmClient.ParseResponse("""{"choices":[{"message":{"content":null,"refusal":"I can't help with that"},"finish_reason":"stop"}]}""", 0);
        Assert.False(r.Success);
        Assert.Contains("refused", r.Error);
        Assert.Contains("I can't help with that", r.Error);
        Assert.Equal("refusal", r.StopReason);
    }

    [Theory]
    [InlineData("""{"choices":[]}""", "no choices in response")]
    [InlineData("""{}""", "no choices in response")]
    [InlineData("""{"choices":[{"message":{"content":null},"finish_reason":"stop"}]}""", "empty response")]
    [InlineData("""{"choices":[{"message":{"content":"   "},"finish_reason":"stop"}]}""", "empty response")]
    [InlineData("""{"choices":[{"finish_reason":"stop"}]}""", "empty response")]
    public void BadShapesAreFailures(string body, string error)
    {
        var r = OpenAiCompatLlmClient.ParseResponse(body, 0);
        Assert.False(r.Success);
        Assert.Equal(error, r.Error);
    }
}

public class AnthropicClientTests
{
    [Theory]
    [InlineData("claude-opus-5-5", true)]
    [InlineData("claude-sonnet-5-5", true)]
    [InlineData("claude-fable-5-2", true)]
    [InlineData("CLAUDE-OPUS-5-5", true)]
    [InlineData("claude-haiku-5-5", false)]
    [InlineData("claude-sonnet-4-5", false)]
    [InlineData("claude-opus-4-1", false)]
    [InlineData("gpt-5", false)]
    [InlineData("", false)]
    public void SupportsServerFallback(string model, bool expected) =>
        Assert.Equal(expected, AnthropicLlmClient.SupportsServerFallback(model));

    [Fact]
    public void SupportsServerFallbackToleratesNull() =>
        Assert.False(AnthropicLlmClient.SupportsServerFallback(null!));
}

public class ScriptedLlmClientTests
{
    private static LlmRequest Req() => new() { Model = "m", SystemPrompt = "s", UserContent = "u", SchemaName = "n", SchemaJson = "{}" };

    [Fact]
    public async Task FromQueueRepliesInOrderThenFails()
    {
        var c = ScriptedLlmClient.FromQueue(new string?[] { "one", "two" });
        Assert.Equal("one", (await c.CompleteJsonAsync(Req(), default)).Text);
        Assert.Equal("two", (await c.CompleteJsonAsync(Req(), default)).Text);
        var r = await c.CompleteJsonAsync(Req(), default);
        Assert.False(r.Success);
        Assert.Equal("no scripted reply", r.Error);
        Assert.Equal(3, c.Requests.Count);
        Assert.Equal("scripted", c.ProviderName);
    }

    [Fact]
    public async Task NullEntryInQueueIsAFailureAndConsumesTheSlot()
    {
        var c = ScriptedLlmClient.FromQueue(new string?[] { null, "after" });
        Assert.False((await c.CompleteJsonAsync(Req(), default)).Success);
        Assert.Equal("after", (await c.CompleteJsonAsync(Req(), default)).Text);
    }

    [Fact]
    public async Task ResponderSeesRequestAndTokenCountsAreEstimated()
    {
        var c = new ScriptedLlmClient(r => r.SchemaName + ":" + r.UserContent);
        var r = await c.CompleteJsonAsync(Req(), default);
        Assert.True(r.Success);
        Assert.Equal("n:u", r.Text);
        Assert.Equal("end_turn", r.StopReason);
        Assert.Equal(0, r.InputTokens); // (1+1)/4
        Assert.Equal(0, r.OutputTokens);

        var big = new ScriptedLlmClient(_ => new string('x', 400));
        var rb = await big.CompleteJsonAsync(new LlmRequest { SystemPrompt = new string('s', 400), UserContent = new string('u', 400) }, default);
        Assert.Equal(200, rb.InputTokens);
        Assert.Equal(100, rb.OutputTokens);
    }

    [Fact]
    public async Task ResponderExceptionBecomesFailure()
    {
        var c = new ScriptedLlmClient(_ => throw new InvalidOperationException("bad"));
        var r = await c.CompleteJsonAsync(Req(), default);
        Assert.False(r.Success);
        Assert.Contains("bad", r.Error);
    }

    [Fact]
    public async Task DelayIsCancellable()
    {
        var c = new ScriptedLlmClient(_ => "x", TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(50);
        var r = await c.CompleteJsonAsync(Req(), cts.Token);
        Assert.False(r.Success);
        Assert.Equal("cancelled", r.Error);
    }

    [Fact]
    public void LlmResultFailHelper()
    {
        var r = LlmResult.Fail("x", 2.5, "stop");
        Assert.False(r.Success);
        Assert.Equal("x", r.Error);
        Assert.Equal(2.5, r.LatencySeconds);
        Assert.Equal("stop", r.StopReason);
    }
}
