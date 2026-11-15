using BannerlordAI.Core.Model;

namespace BannerlordAI.Core.Planning;

/// <summary>Turns a directive (with selectors and anchors) into a concrete <see cref="FormationCommand"/> for this tick.</summary>
public static class DirectiveResolver
{
    public static FormationCommand? Resolve(Directive d, BattleSnapshot snap)
    {
        var self = snap.Find(d.FormationId);
        if (self == null || !self.IsAlive || self.Allegiance != Allegiance.Own) return null;

        var action = d.Action;
        string? targetId = null;

        if (action == DirectiveAction.Advance && d.Anchor != null && string.IsNullOrEmpty(d.Target))
        {
            // Plain move to a point: no target.
        }
        else if (IsAttackAction(action))
        {
            var enemy = PickEnemy(d.Target, snap, self);
            targetId = enemy?.Id;
            if (enemy == null && action != DirectiveAction.Advance)
            {
                // Nothing left to attack: stand where we are.
                action = DirectiveAction.Hold;
            }
        }
        else if (IsFriendlyTargetAction(action))
        {
            var friend = PickFriend(d.Target, snap, self);
            targetId = friend?.Id;
            if (friend == null) action = DirectiveAction.Hold;
        }

        var side = action == DirectiveAction.Flank || action == DirectiveAction.ProtectFlank ? d.Side ?? FlankSide.Left : (FlankSide?)null;
        var position = ResolveAnchor(d.Anchor, snap);
        var target = targetId == null ? null : snap.Find(targetId);
        var fwd = snap.Frame.Forward;

        if (!position.HasValue)
        {
            switch (action)
            {
                case DirectiveAction.Hold:
                case DirectiveAction.Reserve:
                    position = self.Position; // stay put
                    break;
                case DirectiveAction.Retreat:
                    // Never hand the game a bare retreat order: it sends troops off the map.
                    position = self.Position - fwd * RetreatDistance;
                    break;
                case DirectiveAction.Follow:
                    if (target != null) position = target.Position - fwd * FollowDistance;
                    break;
                case DirectiveAction.Screen:
                    if (target != null) position = target.Position + fwd * ScreenDistance;
                    break;
                case DirectiveAction.Flank:
                    if (target != null) position = FlankWaypoint(target.Position, side ?? FlankSide.Left, snap.Frame);
                    break;
            }
        }
        if (position.HasValue && snap.Bounds.HasValue) position = snap.Bounds.Value.Clamp(position.Value);

        return new FormationCommand
        {
            FormationId = self.Id,
            Action = action,
            TargetFormationId = targetId,
            Position = position,
            Side = side,
            Arrangement = d.Arrangement,
            Fire = d.Fire,
            Source = d.Source,
        };
    }

    /// <summary>How far an anchor-less retreat falls back, metres.</summary>
    public const float RetreatDistance = 150f;
    /// <summary>How far behind its target an anchor-less follow stays, metres.</summary>
    public const float FollowDistance = 30f;
    /// <summary>How far in front of its target an anchor-less screen stands, metres.</summary>
    public const float ScreenDistance = 40f;
    /// <summary>Lateral and rear offsets of a flanking waypoint from the target, metres.</summary>
    public const float FlankLateral = 90f;
    public const float FlankBeyond = 30f;

    /// <summary>
    /// Point beside and slightly beyond the target on the given side (our left/right, in our frame). The adapter moves
    /// there, then charges the target once close.
    /// </summary>
    public static Vec2 FlankWaypoint(Vec2 target, FlankSide side, BattleFrame frame)
    {
        var lateral = side == FlankSide.Left ? -FlankLateral : FlankLateral;
        return target + frame.Right * lateral + frame.Forward * FlankBeyond;
    }

    public static Vec2? ResolveAnchor(Anchor? anchor, BattleSnapshot snap)
    {
        if (anchor == null) return null;
        if (!anchor.IsRelative) return anchor.Value;
        var reference = snap.Find(anchor.RelativeTo);
        if (reference == null || !reference.IsAlive) return null;
        return reference.Position + snap.Frame.OffsetToWorld(anchor.Value);
    }

    public static bool IsAttackAction(DirectiveAction a) =>
        a == DirectiveAction.Charge || a == DirectiveAction.Skirmish || a == DirectiveAction.Flank || a == DirectiveAction.Advance;

    public static bool IsFriendlyTargetAction(DirectiveAction a) =>
        a == DirectiveAction.ProtectFlank || a == DirectiveAction.Screen || a == DirectiveAction.Follow;

    private static FormationState? PickEnemy(string? selector, BattleSnapshot snap, FormationState self)
    {
        if (!string.IsNullOrEmpty(selector))
        {
            var side = Selectors.SideOf(selector, snap);
            if (side == Allegiance.Enemy)
            {
                var hit = Selectors.SelectOne(selector, snap, self.Position);
                if (hit != null) return hit;
            }
        }
        // Default (or the named target is gone): nearest living enemy.
        return Selectors.SelectOne("enemy:nearest", snap, self.Position);
    }

    private static FormationState? PickFriend(string? selector, BattleSnapshot snap, FormationState self)
    {
        if (!string.IsNullOrEmpty(selector))
        {
            var side = Selectors.SideOf(selector, snap);
            if (side == Allegiance.Own || side == Allegiance.Ally)
            {
                var hit = Selectors.SelectOne(selector, snap, self.Position, exclude: self.Id);
                if (hit != null) return hit;
            }
        }
        // Default: our largest infantry formation, else our largest formation.
        return Selectors.SelectOne("own:infantry", snap, self.Position, exclude: self.Id)
               ?? Selectors.SelectOne("own:largest", snap, self.Position, exclude: self.Id);
    }
}
