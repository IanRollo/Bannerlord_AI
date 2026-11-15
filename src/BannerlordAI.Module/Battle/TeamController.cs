using System;
using BannerlordAI.Core.Llm;
using BannerlordAI.Core.Orchestration;
using BannerlordAI.Module.Logging;
using BannerlordAI.Module.Settings;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace BannerlordAI.Module.Battle;

/// <summary>
/// One AI-commanded team: owns its brain, formation ids and snapshot builder, and publishes commands for its tactic.
/// Everything here runs on the main thread except reads of <see cref="Published"/> (and ReportTacticError).
/// </summary>
internal sealed class TeamController : IDisposable
{
    private readonly Mission _mission;
    private readonly ModSettings _settings;
    private readonly FormationRegistry _registry;
    private readonly SnapshotBuilder _builder;
    private readonly BattleBrain _brain;
    private readonly FileBrainLog _log;
    private readonly Color _color;
    private readonly bool _showMessages;
    private volatile CommandSet _published = CommandSet.Empty;
    private volatile string? _tacticError;
    private BrainMode _lastMode = BrainMode.Vanilla;
    private int _version;
    private bool _delegated;

    public TeamController(Mission mission, Team team, ModSettings settings, ILlmClient commander, ILlmClient? lieutenant, string label)
    {
        _mission = mission;
        Team = team;
        _settings = settings;
        Label = label;
        _registry = new FormationRegistry(team);
        _builder = new SnapshotBuilder(team, _registry);
        _log = new FileBrainLog(label, settings.DebugLog);
        _brain = new BattleBrain(settings.Brain, commander, lieutenant, _log);
        var enemyOfPlayer = mission.PlayerTeam != null && team.IsEnemyOf(mission.PlayerTeam);
        _color = enemyOfPlayer ? OrderLog.Enemy : OrderLog.Friendly;
        _showMessages = settings.ShowOrderLog && (!enemyOfPlayer || settings.ShowEnemyOrders);
        _log.Line($"Controller created for {label}: commander {settings.Brain.Commander.Model} via {commander.ProviderName}, " +
                  $"lieutenant {(lieutenant == null ? "off" : settings.Brain.Lieutenant.Model + " via " + lieutenant.ProviderName)}");
    }

    public Team Team { get; }

    public string Label { get; }

    /// <summary>Latest commands for <see cref="LlmTactic"/>. Safe to read from any thread.</summary>
    public CommandSet Published => _published;

    public bool IsPlayerTeam => Team == _mission.PlayerTeam;

    public void Tick()
    {
        if (_tacticError != null)
        {
            _log.Warn("Tactic error: " + _tacticError);
            _tacticError = null;
        }

        var snap = _builder.Build(_mission, _settings.Brain.IncludeTerrain);

        // The player's own formations start player-controlled when the player is the general: hand them to the AI
        // once the battle proper starts. The player can take any formation back by giving it an order.
        if (!_delegated && IsPlayerTeam && snap.Phase == Core.Model.BattlePhase.Battle)
        {
            _delegated = true;
            Team.DelegateCommandToAI();
            snap = _builder.Build(_mission, _settings.Brain.IncludeTerrain);
            Show("AI commander has taken command. Give any formation an order to take it back.", OrderLog.Warning);
        }

        var result = _brain.Tick(snap);
        foreach (var m in result.Messages) Show(m, _color);

        var active = result.Mode == BrainMode.Llm;
        if (result.Changed.Count > 0 || active != _published.Active)
        {
            _version++;
            _published = new CommandSet(_version, active, result.Current, _registry.CopyById());
        }

        if (result.Mode != _lastMode)
        {
            _log.Line($"Mode {_lastMode} -> {result.Mode}" + (_brain.FallbackReason != null ? " (" + _brain.FallbackReason + ")" : ""));
            _lastMode = result.Mode;
        }
    }

    /// <summary>Called from the tactic's tick (possibly off the main thread); logged on the next main-thread tick.</summary>
    public void ReportTacticError(Exception ex) => _tacticError = ex.GetType().Name + ": " + ex.Message;

    private void Show(string message, Color color)
    {
        if (_showMessages) OrderLog.Show(Label + " - " + message, color);
    }

    public void Dispose()
    {
        _published = CommandSet.Empty;
        _log.Line("Battle over. " + _brain.Stats);
        _brain.Dispose();
    }
}
