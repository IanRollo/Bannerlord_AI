using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace BannerlordAI.Module.Battle.Behaviors;

// Custom formation behaviors for orders vanilla behaviors can't express (a specific point, a specific target).
// They follow the vanilla pattern: CalculateCurrentOrder fills CurrentOrder/CurrentFacingOrder, TickOccasionally
// pushes them to the formation. They run inside the game's team tick, like vanilla behaviors.
// Parameters are written by LlmTactic (same tick context) before the weight is set.

/// <summary>Move to a point and hold it, facing the enemy. Used for hold, reserve, retreat, follow, advance-to-point.</summary>
internal sealed class LlmBehaviorHoldAt : BehaviorComponent
{
    public LlmBehaviorHoldAt(Formation formation) : base(formation)
    {
        _behaviorSide = formation.AI.Side;
        CurrentOrder = MovementOrder.MovementOrderStop;
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
    }

    public Vec2? Point { get; set; }

    public string Description { get; set; } = "holding position";

    protected override void CalculateCurrentOrder()
    {
        if (!Point.HasValue)
        {
            CurrentOrder = MovementOrder.MovementOrderStop;
            return;
        }
        var wp = Formation.CachedMedianPosition;
        wp.SetVec2(Point.Value);
        CurrentOrder = MovementOrder.MovementOrderMove(wp);
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
    }

    public override void TickOccasionally()
    {
        CalculateCurrentOrder();
        Formation.SetMovementOrder(CurrentOrder);
        Formation.SetFacingOrder(CurrentFacingOrder);
    }

    protected override void OnBehaviorActivatedAux()
    {
        CalculateCurrentOrder();
        Formation.SetMovementOrder(CurrentOrder);
        Formation.SetFacingOrder(CurrentFacingOrder);
    }

    protected override float GetAiWeight() => Formation.CountOfUnits > 0 ? 1f : 0f;

    public override TextObject GetBehaviorString() => new(Description);
}

/// <summary>Charge one specific enemy formation; falls back to a general charge if it's gone.</summary>
internal sealed class LlmBehaviorChargeTarget : BehaviorComponent
{
    public LlmBehaviorChargeTarget(Formation formation) : base(formation)
    {
        _behaviorSide = formation.AI.Side;
        CurrentOrder = MovementOrder.MovementOrderCharge;
    }

    public Formation? Target { get; set; }

    protected override void CalculateCurrentOrder()
    {
        var t = Target;
        CurrentOrder = t != null && t.CountOfUnits > 0
            ? MovementOrder.MovementOrderChargeToTarget(t)
            : MovementOrder.MovementOrderCharge;
    }

    public override void TickOccasionally()
    {
        CalculateCurrentOrder();
        Formation.SetMovementOrder(CurrentOrder);
    }

    protected override void OnBehaviorActivatedAux()
    {
        CalculateCurrentOrder();
        Formation.SetMovementOrder(CurrentOrder);
    }

    protected override float GetAiWeight() => Formation.CountOfUnits > 0 ? 1f : 0f;

    public override TextObject GetBehaviorString() => new("charging");
}

/// <summary>
/// Swing out to a waypoint beside the target, then charge it. Latches into the charge once close, until given a new
/// target. The waypoint is recomputed by the core every second as the target moves.
/// </summary>
internal sealed class LlmBehaviorFlankTarget : BehaviorComponent
{
    private const float WaypointReached = 30f;
    private const float CloseEnoughToCharge = 50f;

    private bool _charging;
    private Formation? _target;

    public LlmBehaviorFlankTarget(Formation formation) : base(formation)
    {
        _behaviorSide = formation.AI.Side;
        CurrentOrder = MovementOrder.MovementOrderStop;
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
    }

    public Vec2? Waypoint { get; set; }

    public void SetTarget(Formation? target)
    {
        if (!ReferenceEquals(target, _target)) _charging = false;
        _target = target;
    }

    protected override void CalculateCurrentOrder()
    {
        var t = _target;
        if (t == null || t.CountOfUnits == 0)
        {
            CurrentOrder = MovementOrder.MovementOrderCharge;
            return;
        }

        var here = Formation.CachedAveragePosition;
        if (!_charging)
        {
            var nearTarget = here.Distance(t.CachedAveragePosition) < CloseEnoughToCharge;
            var atWaypoint = !Waypoint.HasValue || here.Distance(Waypoint.Value) < WaypointReached;
            if (nearTarget || atWaypoint) _charging = true;
        }

        if (_charging)
        {
            CurrentOrder = MovementOrder.MovementOrderChargeToTarget(t);
            return;
        }

        var wp = Formation.CachedMedianPosition;
        wp.SetVec2(Waypoint!.Value);
        CurrentOrder = MovementOrder.MovementOrderMove(wp);
        CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
    }

    public override void TickOccasionally()
    {
        CalculateCurrentOrder();
        Formation.SetMovementOrder(CurrentOrder);
        if (!_charging) Formation.SetFacingOrder(CurrentFacingOrder);
    }

    protected override void OnBehaviorActivatedAux()
    {
        CalculateCurrentOrder();
        Formation.SetMovementOrder(CurrentOrder);
    }

    protected override float GetAiWeight() => Formation.CountOfUnits > 0 ? 1f : 0f;

    public override TextObject GetBehaviorString() => new(_charging ? "charging the flank" : "moving to flank");
}
