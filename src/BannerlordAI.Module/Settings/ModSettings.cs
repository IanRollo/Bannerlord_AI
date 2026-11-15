using System;
using BannerlordAI.Core.Llm;
using BannerlordAI.Core.Orchestration;
using MCM.Common;

namespace BannerlordAI.Module.Settings;

/// <summary>Plain snapshot of the settings for one battle, decoupled from MCM (defaults if MCM isn't available).</summary>
internal sealed class ModSettings
{
    public bool Enabled { get; private set; } = true;
    public bool ControlPlayerTeam { get; private set; } = true;
    public bool ControlAllies { get; private set; }
    public bool ControlEnemies { get; private set; }
    public bool ShowOrderLog { get; private set; } = true;
    public bool ShowEnemyOrders { get; private set; }
    public bool DebugLog { get; private set; }

    public string CommanderProvider { get; private set; } = "anthropic";
    public string LieutenantProvider { get; private set; } = "anthropic";
    public string AnthropicApiKey { get; private set; } = "";
    public string OpenAiBaseUrl { get; private set; } = "https://api.openai.com/v1";
    public string OpenAiApiKey { get; private set; } = "";
    public JsonOutputMode OpenAiJsonMode { get; private set; } = JsonOutputMode.Schema;
    public string OpenAiMaxTokensField { get; private set; } = "max_tokens";

    public BrainConfig Brain { get; private set; } = new();

    public static ModSettings Read()
    {
        var result = new ModSettings();
        AiCommanderSettings? s = null;
        try { s = AiCommanderSettings.Instance; }
        catch { /* MCM not loaded: defaults */ }
        if (s == null) return result.WithEnvironmentKeys();

        result.Enabled = s.Enabled;
        result.ControlPlayerTeam = s.ControlPlayerTeam;
        result.ControlAllies = s.ControlAllies;
        result.ControlEnemies = s.ControlEnemies;
        result.ShowOrderLog = s.ShowOrderLog;
        result.ShowEnemyOrders = s.ShowEnemyOrders;
        result.DebugLog = s.DebugLog;

        result.CommanderProvider = Selected(s.CommanderProvider, "anthropic");
        result.LieutenantProvider = Selected(s.LieutenantProvider, "anthropic");
        result.AnthropicApiKey = (s.AnthropicApiKey ?? "").Trim();
        result.OpenAiBaseUrl = (s.OpenAiBaseUrl ?? "").Trim();
        result.OpenAiApiKey = (s.OpenAiApiKey ?? "").Trim();
        result.OpenAiJsonMode = Selected(s.OpenAiJsonMode, "schema") switch
        {
            "json_object" => JsonOutputMode.JsonObject,
            "prompt" => JsonOutputMode.PromptOnly,
            _ => JsonOutputMode.Schema,
        };
        result.OpenAiMaxTokensField = string.IsNullOrWhiteSpace(s.OpenAiMaxTokensField) ? "max_tokens" : s.OpenAiMaxTokensField.Trim();

        var brain = new BrainConfig();
        brain.Commander.Model = NonEmpty(s.CommanderModel, brain.Commander.Model);
        brain.Commander.Effort = Effort(s.CommanderEffort);
        brain.Commander.MaxCallsPerBattle = s.CommanderMaxCalls;
        brain.Lieutenant.Enabled = s.LieutenantEnabled && s.LieutenantMaxCalls > 0;
        brain.Lieutenant.Model = NonEmpty(s.LieutenantModel, brain.Lieutenant.Model);
        brain.Lieutenant.Effort = Effort(s.LieutenantEffort);
        brain.Lieutenant.MaxCallsPerBattle = s.LieutenantMaxCalls;
        brain.Cadence.CommanderInterval = s.CommanderInterval;
        brain.Cadence.NearDistance = s.NearDistance;
        brain.Cadence.FarDistance = Math.Max(s.FarDistance, s.NearDistance + 10f);
        brain.Cadence.LieutenantNearInterval = s.NearInterval;
        brain.Cadence.LieutenantFarInterval = Math.Max(s.FarInterval, s.NearInterval);
        brain.MaxTokensPerBattle = (long)s.TokenBudgetThousands * 1000;
        brain.IncludeTerrain = s.IncludeTerrain;
        result.Brain = brain;

        return result.WithEnvironmentKeys();
    }

    private ModSettings WithEnvironmentKeys()
    {
        if (AnthropicApiKey.Length == 0) AnthropicApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")?.Trim() ?? "";
        if (OpenAiApiKey.Length == 0) OpenAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim() ?? "";
        return this;
    }

    /// <summary>Creates the client for a provider, or returns null with a reason if it can't be configured.</summary>
    public ILlmClient? CreateClient(string provider, out string? problem)
    {
        problem = null;
        if (provider == "openai")
        {
            if (string.IsNullOrWhiteSpace(OpenAiBaseUrl))
            {
                problem = "no OpenAI-compatible base URL set";
                return null;
            }
            return new OpenAiCompatLlmClient(OpenAiBaseUrl, OpenAiApiKey, OpenAiJsonMode, OpenAiMaxTokensField);
        }
        if (AnthropicApiKey.Length == 0)
        {
            problem = "no Anthropic API key (set it in Mod Options > AI Commander, or the ANTHROPIC_API_KEY environment variable)";
            return null;
        }
        return new AnthropicLlmClient(AnthropicApiKey);
    }

    private static string Selected(Dropdown<string>? d, string fallback)
    {
        try { return d?.SelectedValue ?? fallback; }
        catch { return fallback; }
    }

    private static string? Effort(Dropdown<string>? d)
    {
        var v = Selected(d, "default");
        return v == "default" ? null : v;
    }

    private static string NonEmpty(string? s, string fallback) => string.IsNullOrWhiteSpace(s) ? fallback : s!.Trim();
}
