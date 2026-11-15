using BannerlordAI.Core.Llm;
using BannerlordAI.Core.Model;

namespace BannerlordAI.Core.Orchestration;

/// <summary>Developer log (file/console). Player-facing lines go through BrainTickResult.Messages instead.</summary>
public interface IBrainLog
{
    void Info(string message);
    void Warn(string message);
    void Transcript(Tier tier, LlmRequest request, LlmResult result);
}

public sealed class NullBrainLog : IBrainLog
{
    public static readonly NullBrainLog Instance = new();
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Transcript(Tier tier, LlmRequest request, LlmResult result) { }
}
