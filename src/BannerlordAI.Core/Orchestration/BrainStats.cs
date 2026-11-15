using System;
using BannerlordAI.Core.Llm;
using BannerlordAI.Core.Model;

namespace BannerlordAI.Core.Orchestration;

public sealed class TierStats
{
    public int Calls { get; private set; }
    public int Failures { get; private set; }
    public long InputTokens { get; private set; }
    public long OutputTokens { get; private set; }
    public long CacheReadTokens { get; private set; }
    public long CacheWriteTokens { get; private set; }
    public double TotalLatency { get; private set; }
    public double MaxLatency { get; private set; }

    public long TotalTokens => InputTokens + OutputTokens + CacheReadTokens + CacheWriteTokens;

    public double AverageLatency => Calls == 0 ? 0 : TotalLatency / Calls;

    internal void Record(LlmResult r, bool parsedOk)
    {
        Calls++;
        if (!r.Success || !parsedOk) Failures++;
        InputTokens += r.InputTokens;
        OutputTokens += r.OutputTokens;
        CacheReadTokens += r.CacheReadTokens;
        CacheWriteTokens += r.CacheWriteTokens;
        TotalLatency += r.LatencySeconds;
        MaxLatency = Math.Max(MaxLatency, r.LatencySeconds);
    }

    public override string ToString() =>
        $"{Calls} calls ({Failures} failed), {InputTokens} in / {OutputTokens} out / {CacheReadTokens} cache-read tokens, " +
        $"avg {Wire.F(AverageLatency, "0.0")}s max {Wire.F(MaxLatency, "0.0")}s";
}

public sealed class BrainStats
{
    public TierStats Commander { get; } = new();
    public TierStats Lieutenant { get; } = new();
    public int ContingenciesFired { get; internal set; }
    public int PlansInstalled { get; internal set; }

    public long TotalTokens => Commander.TotalTokens + Lieutenant.TotalTokens;

    public override string ToString() =>
        $"Commander: {Commander}. Lieutenant: {Lieutenant}. Plans: {PlansInstalled}, contingencies fired: {ContingenciesFired}.";
}
