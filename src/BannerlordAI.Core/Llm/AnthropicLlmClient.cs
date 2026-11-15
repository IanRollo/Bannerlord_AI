using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using BetaMessages = Anthropic.Models.Beta.Messages;

namespace BannerlordAI.Core.Llm;

/// <summary>Claude via the official Anthropic C# SDK, using structured outputs and prompt caching.</summary>
public sealed class AnthropicLlmClient : ILlmClient
{
    private readonly AnthropicClient _client;

    public AnthropicLlmClient(string apiKey, string? baseUrl = null)
    {
        _client = string.IsNullOrWhiteSpace(baseUrl)
            ? new AnthropicClient { ApiKey = apiKey }
            : new AnthropicClient { ApiKey = apiKey, BaseUrl = baseUrl! };
    }

    public string ProviderName => "anthropic";

    public async Task<LlmResult> CompleteJsonAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(request.Timeout);

            var schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.SchemaJson)
                         ?? throw new InvalidOperationException("schema is empty");

            if (UseRefusalFallback && SupportsServerFallback(request.Model))
                return await CreateWithServerFallback(request, schema, sw, timeout.Token).ConfigureAwait(false);

            var outputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = schema } };
            var effort = MapEffort(request.Effort);
            if (effort.HasValue) outputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = schema }, Effort = effort.Value };

            var parameters = new MessageCreateParams
            {
                Model = request.Model,
                MaxTokens = request.MaxTokens,
                System = new List<TextBlockParam>
                {
                    new() { Text = request.SystemPrompt, CacheControl = new CacheControlEphemeral() },
                },
                Messages = [new UserMessageParam { Content = request.UserContent }],
                OutputConfig = outputConfig,
            };

            var response = await _client.Messages.Create(parameters, timeout.Token).ConfigureAwait(false);

            var text = new StringBuilder();
            foreach (var block in response.Content.Select(b => b.Value).OfType<TextBlock>())
                text.Append(block.Text);

            var refused = response.StopReason == "refusal";
            var truncated = response.StopReason == "max_tokens";
            var usage = response.Usage;
            var result = new LlmResult
            {
                Success = !refused && !truncated && text.Length > 0,
                Text = text.ToString(),
                StopReason = refused ? "refusal" : truncated ? "max_tokens" : response.StopReason?.ToString(),
                Error = refused ? "model refused" : truncated ? "hit max_tokens" : text.Length == 0 ? "empty response" : null,
                InputTokens = usage.InputTokens,
                OutputTokens = usage.OutputTokens,
                CacheReadTokens = usage.CacheReadInputTokens ?? 0,
                CacheWriteTokens = usage.CacheCreationInputTokens ?? 0,
                LatencySeconds = sw.Elapsed.TotalSeconds,
            };
            return result;
        }
        catch (OperationCanceledException)
        {
            return LlmResult.Fail(cancellationToken.IsCancellationRequested ? "cancelled" : "timed out", sw.Elapsed.TotalSeconds);
        }
        catch (AnthropicUnauthorizedException)
        {
            return LlmResult.Fail("invalid Anthropic API key (401)", sw.Elapsed.TotalSeconds);
        }
        catch (AnthropicRateLimitException)
        {
            return LlmResult.Fail("rate limited (429)", sw.Elapsed.TotalSeconds);
        }
        catch (AnthropicBadRequestException ex)
        {
            return LlmResult.Fail("bad request (400): " + ex.Message, sw.Elapsed.TotalSeconds);
        }
        catch (Anthropic5xxException ex)
        {
            return LlmResult.Fail("server error: " + ex.Message, sw.Elapsed.TotalSeconds);
        }
        catch (AnthropicApiException ex)
        {
            return LlmResult.Fail("API error: " + ex.Message, sw.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            return LlmResult.Fail(ex.GetType().Name + ": " + ex.Message, sw.Elapsed.TotalSeconds);
        }
    }

    /// <summary>
    /// Server-side refusal fallback (beta): if a safety classifier declines, the API re-serves the request on a
    /// fallback model inside the same call. On by default for models that support it.
    /// </summary>
    public bool UseRefusalFallback { get; set; } = true;

    internal static bool SupportsServerFallback(string model)
    {
        var m = (model ?? "").ToLowerInvariant();
        return m.StartsWith("claude-opus-5", StringComparison.Ordinal)
               || m.StartsWith("claude-sonnet-5-5", StringComparison.Ordinal)
               || m.StartsWith("claude-fable-5", StringComparison.Ordinal);
    }

    private async Task<LlmResult> CreateWithServerFallback(LlmRequest request, Dictionary<string, JsonElement> schema, Stopwatch sw, CancellationToken ct)
    {
        var effort = MapBetaEffort(request.Effort);
        var outputConfig = effort.HasValue
            ? new BetaMessages.BetaOutputConfig { Format = new BetaMessages.BetaJsonOutputFormat { Schema = schema }, Effort = effort.Value }
            : new BetaMessages.BetaOutputConfig { Format = new BetaMessages.BetaJsonOutputFormat { Schema = schema } };

        var parameters = new BetaMessages.MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = request.MaxTokens,
            Betas = [Anthropic.Models.Beta.AnthropicBeta.ServerSideFallback2026_07_01],
            Fallbacks = new BetaMessages.Default(),
            System = new List<BetaMessages.BetaTextBlockParam>
            {
                new() { Text = request.SystemPrompt, CacheControl = new BetaMessages.BetaCacheControlEphemeral() },
            },
            Messages = [new BetaMessages.BetaUserMessageParam { Content = request.UserContent }],
            OutputConfig = outputConfig,
        };

        var response = await _client.Beta.Messages.Create(parameters, ct).ConfigureAwait(false);

        var text = new StringBuilder();
        foreach (var block in response.Content)
            if (block.TryPickText(out var t)) text.Append(t.Text);

        var refused = response.StopReason == "refusal";
        var truncated = response.StopReason == "max_tokens";
        var usage = response.Usage;
        return new LlmResult
        {
            Success = !refused && !truncated && text.Length > 0,
            Text = text.ToString(),
            StopReason = refused ? "refusal" : truncated ? "max_tokens" : response.StopReason?.ToString(),
            Error = refused ? "model refused" : truncated ? "hit max_tokens" : text.Length == 0 ? "empty response" : null,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            CacheReadTokens = usage.CacheReadInputTokens ?? 0,
            CacheWriteTokens = usage.CacheCreationInputTokens ?? 0,
            LatencySeconds = sw.Elapsed.TotalSeconds,
        };
    }

    private static BetaMessages.Effort? MapBetaEffort(string? effort) => (effort ?? "").Trim().ToLowerInvariant() switch
    {
        "low" => BetaMessages.Effort.Low,
        "medium" => BetaMessages.Effort.Medium,
        "high" => BetaMessages.Effort.High,
        "max" => BetaMessages.Effort.Max,
        _ => null,
    };

    private static Effort? MapEffort(string? effort) => (effort ?? "").Trim().ToLowerInvariant() switch
    {
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "high" => Effort.High,
        "max" => Effort.Max,
        _ => null,
    };
}
