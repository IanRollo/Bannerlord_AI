using System;
using BannerlordAI.Core.Model;
using BannerlordAI.Core.Planning;
using BannerlordAI.Module.Battle.Behaviors;
using TaleWorlds.MountAndBlade;

namespace BannerlordAI.Module.Battle;

/// <summary>
/// Maps a resolved <see cref="FormationCommand"/> onto a formation's AI: behavior weights (vanilla where they fit,
/// our own where a point or a specific target is needed) plus arrangement and fire orders. Runs in the tactic tick.
/// </summary>
internal static class CommandApplier
{
    public static void Apply(Formation f, FormationCommand cmd, CommandSet set)
    {
        f.AI.ResetBehaviorWeights();
        var target = set.Resolve(cmd.TargetFormationId);
        var mounted = f.QuerySystem.IsCavalryFormation || f.QuerySystem.IsRangedCavalryFormation;

        switch (cmd.Action)
        {
            case DirectiveAction.Hold:
                HoldAt(f, cmd, "holding position");
                break;
            case DirectiveAction.Reserve:
                HoldAt(f, cmd, "in reserve");
                break;
            case DirectiveAction.Retreat:
                HoldAt(f, cmd, "falling back");
                break;
            case DirectiveAction.Follow:
                HoldAt(f, cmd, "following");
                break;
            case DirectiveAction.Advance:
                if (cmd.Position.HasValue) HoldAt(f, cmd, "advancing");
                else if (!TrySetWeight<BehaviorAdvance>(f)) Charge(f, target);
                break;
            case DirectiveAction.Charge:
                Charge(f, target);
                break;
            case DirectiveAction.Flank:
                if (target == null)
                {
                    Charge(f, null);
                    break;
                }
                var flank = Ensure(f, x => new LlmBehaviorFlankTarget(x));
                flank.SetTarget(target);
                flank.Waypoint = cmd.Position?.ToGame();
                f.AI.SetBehaviorWeight<LlmBehaviorFlankTarget>(1f);
                break;
            case DirectiveAction.Skirmish:
                Skirmish(f, mounted, target);
                break;
            case DirectiveAction.ProtectFlank:
                if (f.AI.GetBehavior<BehaviorProtectFlank>() != null)
                {
                    var pf = f.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f);
                    pf.FlankSide = cmd.Side == FlankSide.Right ? FormationAI.BehaviorSide.Right : FormationAI.BehaviorSide.Left;
                }
                else HoldAt(f, cmd, "guarding the flank");
                break;
            case DirectiveAction.Screen:
                if (!(mounted && TrySetWeight<BehaviorCavalryScreen>(f))) HoldAt(f, cmd, "screening");
                break;
            default:
                HoldAt(f, cmd, "holding position");
                break;
        }

        EnforceStandingOrders(f, cmd);
    }

    /// <summary>Re-assert arrangement / fire if a behavior changed them. Cheap; called every tactic tick.</summary>
    public static void EnforceStandingOrders(Formation f, FormationCommand cmd)
    {
        if (cmd.Arrangement.HasValue)
        {
            var want = ToGame(cmd.Arrangement.Value);
            if (f.ArrangementOrder.OrderEnum != want.OrderEnum) f.SetArrangementOrder(want);
        }
        if (cmd.Fire.HasValue)
        {
            var want = cmd.Fire.Value == FireOrder.Hold ? FiringOrder.FiringOrderHoldYourFire : FiringOrder.FiringOrderFireAtWill;
            if (f.FiringOrder.OrderEnum != want.OrderEnum) f.SetFiringOrder(want);
        }
    }

    /// <summary>What the formation does when the plan has nothing for it: the game's default behavior mix.</summary>
    public static void ApplyDefault(Formation f)
    {
        f.AI.ResetBehaviorWeights();
        TacticComponent.SetDefaultBehaviorWeights(f);
    }

    private static void HoldAt(Formation f, FormationCommand cmd, string description)
    {
        var hold = Ensure(f, x => new LlmBehaviorHoldAt(x));
        hold.Point = (cmd.Position ?? f.CachedAveragePosition.ToCore()).ToGame();
        hold.Description = description;
        f.AI.SetBehaviorWeight<LlmBehaviorHoldAt>(1f);
    }

    private static void Charge(Formation f, Formation? target)
    {
        var charge = Ensure(f, x => new LlmBehaviorChargeTarget(x));
        charge.Target = target;
        f.AI.SetBehaviorWeight<LlmBehaviorChargeTarget>(1f);
    }

    private static void Skirmish(Formation f, bool mounted, Formation? target)
    {
        if (f.QuerySystem.IsRangedCavalryFormation)
        {
            if (TrySetWeight<BehaviorHorseArcherSkirmish>(f)) return;
        }
        else if (mounted)
        {
            if (TrySetWeight<BehaviorMountedSkirmish>(f)) return;
        }
        else
        {
            // Let the game pick between shooting from behind the line and screened skirmishing.
            var any = TrySetWeight<BehaviorSkirmishBehindFormation>(f);
            any |= TrySetWeight<BehaviorScreenedSkirmish>(f);
            if (!any) any = TrySetWeight<BehaviorSkirmish>(f);
            if (any) return;
        }
        Charge(f, target);
    }

    /// <summary>Sets a vanilla behavior's weight if the game registered it on this formation (it throws otherwise).</summary>
    private static bool TrySetWeight<T>(Formation f, float weight = 1f) where T : BehaviorComponent
    {
        if (f.AI.GetBehavior<T>() == null) return false;
        f.AI.SetBehaviorWeight<T>(weight);
        return true;
    }

    /// <summary>Returns our behavior on this formation, registering it on first use.</summary>
    private static T Ensure<T>(Formation f, Func<Formation, T> create) where T : BehaviorComponent
    {
        var b = f.AI.GetBehavior<T>();
        if (b != null) return b;
        b = create(f);
        f.AI.AddAiBehavior(b);
        return b;
    }

    private static ArrangementOrder ToGame(Arrangement a) => a switch
    {
        Arrangement.ShieldWall => ArrangementOrder.ArrangementOrderShieldWall,
        Arrangement.Loose => ArrangementOrder.ArrangementOrderLoose,
        Arrangement.Square => ArrangementOrder.ArrangementOrderSquare,
        Arrangement.Circle => ArrangementOrder.ArrangementOrderCircle,
        Arrangement.Column => ArrangementOrder.ArrangementOrderColumn,
        Arrangement.Scatter => ArrangementOrder.ArrangementOrderScatter,
        _ => ArrangementOrder.ArrangementOrderLine,
    };
}
