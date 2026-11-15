using System;
using System.Threading;
using System.Threading.Tasks;

namespace BannerlordAI.Core.Llm;

public sealed class LlmRequest
{
    public string Model { get; init; } = "";
    /// <summary>Static instructions; cached by providers that support prompt caching. Keep it byte-stable.</summary>
    public string SystemPrompt { get; init; } = "";
    /// <summary>Per-call content (battle report, plan, events).</summary>
    public string UserContent { get; init; } = "";
    public string SchemaName { get; init; } = "";
    public string SchemaJson { get; init; } = "";
    /// <summary>"low" | "medium" | "high" | "xhigh" | "max", or null for the provider default.</summary>
    public string? Effort { get; init; }
    public int MaxTokens { get; init; } = 16000;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}

public sealed class LlmResult
{
    public bool Success { get; init; }
    public string? Text { get; init; }
    public string? Error { get; init; }
    public string? StopReason { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long CacheReadTokens { get; init; }
    public long CacheWriteTokens { get; init; }
    public double LatencySeconds { get; init; }

    public static LlmResult Fail(string error, double latency = 0, string? stopReason = null) =>
        new() { Success = false, Error = error, LatencySeconds = latency, StopReason = stopReason };
}

/// <summary>A provider that returns one JSON object for one request. Implementations must not throw.</summary>
public interface ILlmClient
{
    string ProviderName { get; }

    Task<LlmResult> CompleteJsonAsync(LlmRequest request, CancellationToken cancellationToken);
}
