namespace BannerlordAI.Core.Orchestration;

public sealed class TierConfig
{
    public bool Enabled { get; set; } = true;
    public string Model { get; set; } = "";
    /// <summary>"low" | "medium" | "high" | "max" or null for the provider default.</summary>
    public string? Effort { get; set; }
    public int MaxTokens { get; set; } = 16000;
    public double TimeoutSeconds { get; set; } = 60;
    /// <summary>Hard cap on calls of this tier per battle.</summary>
    public int MaxCallsPerBattle { get; set; } = 100;
}

public sealed class CadenceConfig
{
    /// <summary>Scheduled Commander re-plan interval (seconds of mission time).</summary>
    public double CommanderInterval { get; set; } = 30;
    /// <summary>Minimum gap between Commander calls, even when woken early.</summary>
    public double CommanderCooldown { get; set; } = 10;
    /// <summary>At or below this distance between armies the Lieutenant runs at its fastest.</summary>
    public double NearDistance { get; set; } = 80;
    /// <summary>At or beyond this distance the Lieutenant is idle (the Commander's interval covers it).</summary>
    public double FarDistance { get; set; } = 300;
    public double LieutenantNearInterval { get; set; } = 5;
    public double LieutenantFarInterval { get; set; } = 30;
    /// <summary>Don't call the Lieutenant this soon after a new Commander plan arrived.</summary>
    public double LieutenantGapAfterPlan { get; set; } = 3;
}

public sealed class BrainConfig
{
    public TierConfig Commander { get; set; } = new()
    {
        Model = "claude-opus-5-5",
        Effort = "low",
        MaxTokens = 16000,
        TimeoutSeconds = 60,
        MaxCallsPerBattle = 40,
    };

    public TierConfig Lieutenant { get; set; } = new()
    {
        Model = "claude-haiku-5-5",
        Effort = "low",
        MaxTokens = 6000,
        TimeoutSeconds = 20,
        MaxCallsPerBattle = 200,
    };

    public CadenceConfig Cadence { get; set; } = new();

    /// <summary>After this many consecutive Commander failures the team reverts to the game's AI.</summary>
    public int MaxConsecutiveFailures { get; set; } = 3;

    /// <summary>Per-battle cap on input+output tokens across both tiers (cache reads included). 0 = no cap.</summary>
    public long MaxTokensPerBattle { get; set; } = 1_500_000;

    public bool IncludeTerrain { get; set; } = true;

    public int MaxEventsInPrompt { get; set; } = 25;

    /// <summary>Re-send a command to the game only if its position moved more than this (metres).</summary>
    public float CommandPositionTolerance { get; set; } = 8f;
}
