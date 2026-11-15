using System;
using System.Collections.Generic;
using System.Linq;
using BannerlordAI.Module.Settings;
using TaleWorlds.MountAndBlade;

namespace BannerlordAI.Module.Battle;

/// <summary>
/// Mission behavior added to every mission; activates only in field battles. Hooks our tactic into each commanded
/// team's AI in EarlyStart (after the game's MissionCombatantsLogic has created the team AIs) and drives the brains
/// from the main-thread mission tick.
/// </summary>
internal sealed class AiCommanderLogic : MissionLogic
{
    private const float BrainTickInterval = 1f;

    private readonly ModSettings _settings;
    private readonly List<TeamController> _controllers = new();
    private float _sinceTick;
    private bool _disposed;

    public AiCommanderLogic(ModSettings settings)
    {
        _settings = settings;
    }

    public override void EarlyStart()
    {
        base.EarlyStart();
        try
        {
            if (!Mission.IsFieldBattle) return;

            foreach (var team in Mission.Teams.Where(t => t.HasTeamAi).ToList())
            {
                if (!ShouldCommand(team)) continue;

                var commander = _settings.CreateClient(_settings.CommanderProvider, out var problem);
                if (commander == null)
                {
                    OrderLog.Show("AI Commander disabled: " + problem, OrderLog.Warning);
                    return;
                }
                var lieutenant = _settings.Brain.Lieutenant.Enabled ? _settings.CreateClient(_settings.LieutenantProvider, out problem) : null;
                if (_settings.Brain.Lieutenant.Enabled && lieutenant == null)
                    OrderLog.Show("AI Commander: lieutenant disabled: " + problem, OrderLog.Warning);

                var controller = new TeamController(Mission, team, _settings, commander, lieutenant, LabelFor(team));
                team.AddTacticOption(new LlmTactic(team, controller));
                _controllers.Add(controller);
            }
        }
        catch (Exception ex)
        {
            OrderLog.Show("AI Commander failed to start: " + ex.Message, OrderLog.Warning);
            DisposeControllers();
        }
    }

    public override void OnMissionTick(float dt)
    {
        base.OnMissionTick(dt);
        if (_controllers.Count == 0 || _disposed) return;
        _sinceTick += dt;
        if (_sinceTick < BrainTickInterval) return;
        _sinceTick = 0f;

        foreach (var c in _controllers.ToList())
        {
            try
            {
                c.Tick();
            }
            catch (Exception ex)
            {
                OrderLog.Show($"AI Commander error ({c.Label}), handing back to the game's AI: {ex.Message}", OrderLog.Warning);
                c.Dispose();
                _controllers.Remove(c);
            }
        }
    }

    protected override void OnEndMission()
    {
        DisposeControllers();
        base.OnEndMission();
    }

    public override void OnRemoveBehavior()
    {
        DisposeControllers();
        base.OnRemoveBehavior();
    }

    private bool ShouldCommand(Team team)
    {
        var player = Mission.PlayerTeam;
        if (player == null) return _settings.ControlEnemies || _settings.ControlAllies;
        if (team == player) return _settings.ControlPlayerTeam;
        return team.IsEnemyOf(player) ? _settings.ControlEnemies : _settings.ControlAllies;
    }

    private string LabelFor(Team team)
    {
        var player = Mission.PlayerTeam;
        if (team == player) return "Your commander";
        if (player != null && team.IsEnemyOf(player)) return "Enemy commander";
        return "Allied commander";
    }

    private void DisposeControllers()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var c in _controllers)
        {
            try { c.Dispose(); }
            catch { /* ignore during teardown */ }
        }
        _controllers.Clear();
    }
}
