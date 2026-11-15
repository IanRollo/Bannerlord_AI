using System;
using System.Collections.Generic;
using BannerlordAI.Core.Planning;
using TaleWorlds.MountAndBlade;

namespace BannerlordAI.Module.Battle;

/// <summary>
/// Added alongside the vanilla tactics. It wins the team AI's tactic choice only while an LLM plan is active
/// (huge weight), so falling back is automatic: drop the weight and the vanilla tactics take over again.
/// Runs inside the game's team tick, which may be off the main thread: it only reads the immutable
/// <see cref="CommandSet"/> published by the main thread and touches formation AI, like vanilla tactics do.
/// </summary>
internal sealed class LlmTactic : TacticComponent
{
    private static readonly FormationCommand DefaultMarker = new() { FormationId = "<default>" };

    private readonly TeamController _controller;
    private readonly Dictionary<Formation, FormationCommand> _applied = new();
    private int _appliedVersion = -1;

    public LlmTactic(Team team, TeamController controller) : base(team)
    {
        _controller = controller;
    }

    protected override float GetTacticWeight() => _controller.Published.Active ? 10000f : 0f;

    protected override void OnApply()
    {
        base.OnApply();
        _applied.Clear();
        _appliedVersion = -1;
    }

    // Don't split or merge the army's formations: the plan refers to them by id.
    protected override void ManageFormationCounts() { }

    public override void TickOccasionally()
    {
        try
        {
            if (AreFormationsCreated) ApplyCommands();
        }
        catch (Exception ex)
        {
            _controller.ReportTacticError(ex);
        }
        base.TickOccasionally();
    }

    private void ApplyCommands()
    {
        var set = _controller.Published;
        if (!set.Active) return;
        var reapplyAll = IsTacticReapplyNeeded;

        foreach (var f in FormationsIncludingEmpty)
        {
            if (f.CountOfUnits == 0 || !f.IsAIControlled) continue;
            set.Commands.TryGetValue(FormationRegistry.OwnId(f), out var cmd);
            _applied.TryGetValue(f, out var prev);

            if (cmd == null)
            {
                if (reapplyAll || !ReferenceEquals(prev, DefaultMarker))
                {
                    CommandApplier.ApplyDefault(f);
                    _applied[f] = DefaultMarker;
                }
                continue;
            }

            // Command objects are reused by the core until they change meaningfully, so reference equality is the
            // "has this formation's order changed" test.
            if (reapplyAll || !ReferenceEquals(prev, cmd))
            {
                CommandApplier.Apply(f, cmd, set);
                _applied[f] = cmd;
            }
            else
            {
                CommandApplier.EnforceStandingOrders(f, cmd);
            }
        }

        _appliedVersion = set.Version;
        IsTacticReapplyNeeded = false;
    }
}
