using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace BannerlordAI.Core.Llm;

public enum JsonOutputMode
{
    /// <summary>response_format json_schema with strict:true (OpenAI, OpenRouter, recent LM Studio / Ollama).</summary>
    Schema,
    /// <summary>response_format json_object; the schema is described in the system prompt.</summary>
    JsonObject,
    /// <summary>No response_format; the schema is described in the system prompt and JSON is extracted from the text.</summary>
    PromptOnly,
}

/// <summary>Any OpenAI-compatible /chat/completions endpoint: OpenAI, OpenRouter, Ollama, LM Studio, vLLM, etc.</summary>
public sealed class OpenAiCompatLlmClient : ILlmClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly JsonOutputMode _mode;
    private readonly string _maxTokensField;

    public OpenAiCompatLlmClient(string baseUrl, string? apiKey, JsonOutputMode mode = JsonOutputMode.Schema, string maxTokensField = "max_tokens", HttpMessageHandler? handler = null)
    {
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = System.Threading.Timeout.InfiniteTimeSpan; // per-request timeout via cancellation
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
        _endpoint = baseUrl.TrimEnd('/') + "/chat/completions";
        _mode = mode;
        _maxTokensField = string.IsNullOrWhiteSpace(maxTokensField) ? "max_tokens" : maxTokensField;
    }

    public string ProviderName => "openai-compatible";

    public async Task<LlmResult> CompleteJsonAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(request.Timeout);

            var system = request.SystemPrompt;
            if (_mode != JsonOutputMode.Schema)
                system += "\n\nRespond with only one JSON object (no prose, no code fences) that matches this JSON schema:\n" + request.SchemaJson;

            var body = new JsonObject
            {
                ["model"] = request.Model,
                [_maxTokensField] = request.MaxTokens,
                ["messages"] = new JsonArray
                {
                    new JsonObject { ["role"] = "system", ["content"] = system },
                    new JsonObject { ["role"] = "user", ["content"] = request.UserContent },
                },
            };
            if (_mode == JsonOutputMode.Schema)
            {
                body["response_format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject
                    {
                        ["name"] = request.SchemaName,
                        ["strict"] = true,
                        ["schema"] = JsonNode.Parse(request.SchemaJson),
                    },
                };
            }
            else if (_mode == JsonOutputMode.JsonObject)
            {
                body["response_format"] = new JsonObject { ["type"] = "json_object" };
            }

            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(_endpoint, content, timeout.Token).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return LlmResult.Fail($"HTTP {(int)response.StatusCode}: {Truncate(responseText, 300)}", sw.Elapsed.TotalSeconds);

            return ParseResponse(responseText, sw.Elapsed.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            return LlmResult.Fail(cancellationToken.IsCancellationRequested ? "cancelled" : "timed out", sw.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            return LlmResult.Fail(ex.GetType().Name + ": " + ex.Message, sw.Elapsed.TotalSeconds);
        }
    }

    internal static LlmResult ParseResponse(string responseText, double latency)
    {
        using var doc = JsonDocument.Parse(responseText);
        var root = doc.RootElement;
        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return LlmResult.Fail("no choices in response", latency);

        var choice = choices[0];
        var finish = choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String ? fr.GetString() : null;
        string? text = null;
        if (choice.TryGetProperty("message", out var message))
        {
            if (message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind == JsonValueKind.String)
                return LlmResult.Fail("model refused: " + refusal.GetString(), latency, "refusal");
            if (message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                text = c.GetString();
        }

        long inTok = 0, outTok = 0, cached = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var p) && p.ValueKind == JsonValueKind.Number) inTok = p.GetInt64();
            if (usage.TryGetProperty("completion_tokens", out var o) && o.ValueKind == JsonValueKind.Number) outTok = o.GetInt64();
            if (usage.TryGetProperty("prompt_tokens_details", out var d) && d.ValueKind == JsonValueKind.Object &&
                d.TryGetProperty("cached_tokens", out var ct) && ct.ValueKind == JsonValueKind.Number) cached = ct.GetInt64();
        }

        var ok = !string.IsNullOrWhiteSpace(text) && finish != "length";
        return new LlmResult
        {
            Success = ok,
            Text = text,
            StopReason = finish,
            Error = ok ? null : finish == "length" ? "hit max tokens" : "empty response",
            InputTokens = inTok,
            OutputTokens = outTok,
            CacheReadTokens = cached,
            LatencySeconds = latency,
        };
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "...";

    public void Dispose() => _http.Dispose();
}
