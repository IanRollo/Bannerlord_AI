using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using MCM.Common;

namespace BannerlordAI.Module.Settings;

/// <summary>In-game settings (MCM). Read once at the start of each battle via <see cref="ModSettings.Read"/>.</summary>
internal sealed class AiCommanderSettings : AttributeGlobalSettings<AiCommanderSettings>
{
    private const string KeyWarning = " Stored in plain text in Documents\\Mount and Blade II Bannerlord\\Configs\\ModSettings. Leave empty to use the environment variable instead.";

    public override string Id => "BannerlordAICommander_v1";
    public override string DisplayName => "AI Commander";
    public override string FolderName => "BannerlordAICommander";
    public override string FormatType => "json2";

    // ------------------------------------------------------------------ general

    [SettingPropertyBool("Enabled", Order = 0, RequireRestart = false, HintText = "Let an AI model command armies in field battles.")]
    [SettingPropertyGroup("1. General", GroupOrder = 0)]
    public bool Enabled { get; set; } = true;

    [SettingPropertyBool("Command my army", Order = 1, RequireRestart = false, HintText = "The AI commands your own army; you fight as a soldier. Issue any order yourself to take a formation back.")]
    [SettingPropertyGroup("1. General")]
    public bool ControlPlayerTeam { get; set; } = true;

    [SettingPropertyBool("Command allied armies", Order = 2, RequireRestart = false)]
    [SettingPropertyGroup("1. General")]
    public bool ControlAllies { get; set; } = false;

    [SettingPropertyBool("Command enemy armies", Order = 3, RequireRestart = false, HintText = "A smarter opponent. Each commanded army makes its own model calls.")]
    [SettingPropertyGroup("1. General")]
    public bool ControlEnemies { get; set; } = false;

    [SettingPropertyBool("Show order log", Order = 4, RequireRestart = false, HintText = "Show the AI's orders as on-screen messages.")]
    [SettingPropertyGroup("1. General")]
    public bool ShowOrderLog { get; set; } = true;

    [SettingPropertyBool("Show enemy orders", Order = 5, RequireRestart = false, HintText = "Also show orders of AI-commanded enemy armies (spoilers!).")]
    [SettingPropertyGroup("1. General")]
    public bool ShowEnemyOrders { get; set; } = false;

    // ------------------------------------------------------------------ commander

    [SettingPropertyDropdown("Provider", Order = 0, RequireRestart = false, HintText = "anthropic = Claude API; openai = any OpenAI-compatible endpoint (OpenAI, OpenRouter, Ollama, LM Studio).")]
    [SettingPropertyGroup("2. Commander (large model)", GroupOrder = 1)]
    public Dropdown<string> CommanderProvider { get; set; } = new(new[] { "anthropic", "openai" }, 0);

    [SettingPropertyText("Model", Order = 1, RequireRestart = false, HintText = "Model id, e.g. claude-opus-5-5 or claude-sonnet-5-5.")]
    [SettingPropertyGroup("2. Commander (large model)")]
    public string CommanderModel { get; set; } = "claude-opus-5-5";

    [SettingPropertyDropdown("Effort", Order = 2, RequireRestart = false, HintText = "Thinking effort. Higher plans better but answers slower.")]
    [SettingPropertyGroup("2. Commander (large model)")]
    public Dropdown<string> CommanderEffort { get; set; } = new(new[] { "default", "low", "medium", "high" }, 1);

    [SettingPropertyFloatingInteger("Review interval (s)", 10f, 120f, "0", Order = 3, RequireRestart = false, HintText = "How often the commander re-plans (it is also woken early by big events).")]
    [SettingPropertyGroup("2. Commander (large model)")]
    public float CommanderInterval { get; set; } = 30f;

    [SettingPropertyInteger("Max calls per battle", 1, 200, Order = 4, RequireRestart = false)]
    [SettingPropertyGroup("2. Commander (large model)")]
    public int CommanderMaxCalls { get; set; } = 40;

    // ------------------------------------------------------------------ lieutenant

    [SettingPropertyBool("Enabled", Order = 0, RequireRestart = false, HintText = "A small, fast model that keeps the plan working between commander reviews when armies are close.")]
    [SettingPropertyGroup("3. Lieutenant (small model)", GroupOrder = 2)]
    public bool LieutenantEnabled { get; set; } = true;

    [SettingPropertyDropdown("Provider", Order = 1, RequireRestart = false)]
    [SettingPropertyGroup("3. Lieutenant (small model)")]
    public Dropdown<string> LieutenantProvider { get; set; } = new(new[] { "anthropic", "openai" }, 0);

    [SettingPropertyText("Model", Order = 2, RequireRestart = false, HintText = "Model id, e.g. claude-haiku-5-5.")]
    [SettingPropertyGroup("3. Lieutenant (small model)")]
    public string LieutenantModel { get; set; } = "claude-haiku-5-5";

    [SettingPropertyDropdown("Effort", Order = 3, RequireRestart = false)]
    [SettingPropertyGroup("3. Lieutenant (small model)")]
    public Dropdown<string> LieutenantEffort { get; set; } = new(new[] { "default", "low", "medium", "high" }, 1);

    [SettingPropertyFloatingInteger("Near distance (m)", 20f, 300f, "0", Order = 4, RequireRestart = false, HintText = "At or below this distance between armies the lieutenant checks in at its fastest rate.")]
    [SettingPropertyGroup("3. Lieutenant (small model)")]
    public float NearDistance { get; set; } = 80f;

    [SettingPropertyFloatingInteger("Far distance (m)", 100f, 1000f, "0", Order = 5, RequireRestart = false, HintText = "Beyond this distance the lieutenant is idle and only the commander runs.")]
    [SettingPropertyGroup("3. Lieutenant (small model)")]
    public float FarDistance { get; set; } = 300f;

    [SettingPropertyFloatingInteger("Interval when near (s)", 2f, 30f, "0", Order = 6, RequireRestart = false)]
    [SettingPropertyGroup("3. Lieutenant (small model)")]
    public float NearInterval { get; set; } = 5f;

    [SettingPropertyFloatingInteger("Interval when far (s)", 5f, 120f, "0", Order = 7, RequireRestart = false)]
    [SettingPropertyGroup("3. Lieutenant (small model)")]
    public float FarInterval { get; set; } = 30f;

    [SettingPropertyInteger("Max calls per battle", 0, 1000, Order = 8, RequireRestart = false)]
    [SettingPropertyGroup("3. Lieutenant (small model)")]
    public int LieutenantMaxCalls { get; set; } = 200;

    // ------------------------------------------------------------------ connection

    [SettingPropertyText("Anthropic API key", Order = 0, RequireRestart = false, HintText = "Your Claude API key." + KeyWarning + " (ANTHROPIC_API_KEY)")]
    [SettingPropertyGroup("4. Connection", GroupOrder = 3)]
    public string AnthropicApiKey { get; set; } = "";

    [SettingPropertyText("OpenAI-compatible base URL", Order = 1, RequireRestart = false, HintText = "e.g. https://api.openai.com/v1, https://openrouter.ai/api/v1, http://localhost:11434/v1 (Ollama), http://localhost:1234/v1 (LM Studio).")]
    [SettingPropertyGroup("4. Connection")]
    public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";

    [SettingPropertyText("OpenAI-compatible API key", Order = 2, RequireRestart = false, HintText = "Not needed for local servers." + KeyWarning + " (OPENAI_API_KEY)")]
    [SettingPropertyGroup("4. Connection")]
    public string OpenAiApiKey { get; set; } = "";

    [SettingPropertyDropdown("OpenAI JSON mode", Order = 3, RequireRestart = false, HintText = "schema = strict JSON schema (best); json_object = JSON mode; prompt = no response_format (for local servers that support neither).")]
    [SettingPropertyGroup("4. Connection")]
    public Dropdown<string> OpenAiJsonMode { get; set; } = new(new[] { "schema", "json_object", "prompt" }, 0);

    [SettingPropertyText("OpenAI max-tokens field", Order = 4, RequireRestart = false, HintText = "max_tokens for most servers; max_completion_tokens for newer OpenAI reasoning models.")]
    [SettingPropertyGroup("4. Connection")]
    public string OpenAiMaxTokensField { get; set; } = "max_tokens";

    // ------------------------------------------------------------------ limits & debug

    [SettingPropertyInteger("Token budget per battle (thousands)", 0, 20000, Order = 0, RequireRestart = false, HintText = "Stop using the AI for the rest of a battle after this many tokens (0 = no limit).")]
    [SettingPropertyGroup("5. Limits and debugging", GroupOrder = 4)]
    public int TokenBudgetThousands { get; set; } = 1500;

    [SettingPropertyBool("Include terrain", Order = 1, RequireRestart = false, HintText = "Send a coarse height map to the commander.")]
    [SettingPropertyGroup("5. Limits and debugging")]
    public bool IncludeTerrain { get; set; } = true;

    [SettingPropertyBool("Write debug log", Order = 2, RequireRestart = false, HintText = "Write prompts, replies, timings and token counts to Documents\\Mount and Blade II Bannerlord\\Logs\\AICommander.")]
    [SettingPropertyGroup("5. Limits and debugging")]
    public bool DebugLog { get; set; } = false;
}
