using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BannerlordAI.Core.Llm;

/// <summary>
/// Test/simulator client: answers from a delegate (or a queue of canned replies) with an optional simulated delay.
/// Records every request it receives.
/// </summary>
public sealed class ScriptedLlmClient : ILlmClient
{
    private readonly Func<LlmRequest, string?> _responder;
    private readonly TimeSpan _delay;
    private readonly object _lock = new();

    public ScriptedLlmClient(Func<LlmRequest, string?> responder, TimeSpan? delay = null)
    {
        _responder = responder;
        _delay = delay ?? TimeSpan.Zero;
    }

    public static ScriptedLlmClient FromQueue(IEnumerable<string?> replies, TimeSpan? delay = null)
    {
        var queue = new Queue<string?>(replies);
        var gate = new object();
        return new ScriptedLlmClient(_ =>
        {
            lock (gate) return queue.Count > 0 ? queue.Dequeue() : null;
        }, delay);
    }

    public string ProviderName => "scripted";

    public List<LlmRequest> Requests { get; } = new();

    public async Task<LlmResult> CompleteJsonAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        lock (_lock) Requests.Add(request);
        if (_delay > TimeSpan.Zero)
        {
            try { await Task.Delay(_delay, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return LlmResult.Fail("cancelled"); }
        }

        string? text;
        try { text = _responder(request); }
        catch (Exception ex) { return LlmResult.Fail("responder threw: " + ex.Message); }

        if (text == null) return LlmResult.Fail("no scripted reply");
        return new LlmResult
        {
            Success = true,
            Text = text,
            StopReason = "end_turn",
            InputTokens = (request.SystemPrompt.Length + request.UserContent.Length) / 4,
            OutputTokens = text.Length / 4,
            LatencySeconds = _delay.TotalSeconds,
        };
    }
}
