using System;
using System.Collections.Generic;
using BannerlordAI.Core.Model;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using CVec2 = BannerlordAI.Core.Model.Vec2;
using TVec2 = TaleWorlds.Library.Vec2;

namespace BannerlordAI.Module.Battle;

/// <summary>Reads the mission into a core <see cref="BattleSnapshot"/>. Main thread only (agent data, scene queries).</summary>
internal sealed class SnapshotBuilder
{
    /// <summary>A formation counts as in melee if its centre is within (half depths + this) of an enemy formation's centre.</summary>
    private const float MeleeGap = 12f;
    /// <summary>A formation is routing if at least this share of its men are running away.</summary>
    private const float RoutShare = 0.5f;

    private readonly Team _ownTeam;
    private readonly FormationRegistry _registry;
    private long _sequence;

    public SnapshotBuilder(Team ownTeam, FormationRegistry registry)
    {
        _ownTeam = ownTeam;
        _registry = registry;
    }

    private sealed class Raw
    {
        public Formation Formation = null!;
        public string Id = "";
        public Allegiance Allegiance;
        public bool PlayerControlled;
        public int Count;
        public int Initial;
        public CVec2 Position;
        public CVec2 Facing;
        public float Depth;
        public float Health = 1f;
        public float? Morale;
        public float? Ammo;
        public bool Routing;
        public FormationRole Role;
        public string? Activity;
        public bool Engaged;
    }

    public BattleSnapshot Build(Mission mission, bool includeTerrain)
    {
        var raws = new List<Raw>();
        foreach (var team in mission.Teams)
        {
            if (team.Side == BattleSideEnum.None) continue;
            var allegiance = team == _ownTeam ? Allegiance.Own : team.IsEnemyOf(_ownTeam) ? Allegiance.Enemy : Allegiance.Ally;
            foreach (var f in team.FormationsIncludingEmpty)
            {
                if (f.CountOfUnits == 0 && !_registry.HasSeen(f)) continue;
                raws.Add(Read(f, allegiance));
            }
        }

        MarkEngagement(raws);

        var own = new List<FormationState>();
        var allies = new List<FormationState>();
        var enemies = new List<FormationState>();
        foreach (var r in raws)
        {
            var state = ToState(r);
            if (r.Allegiance == Allegiance.Enemy) enemies.Add(state);
            else if (r.Allegiance == Allegiance.Own && !r.PlayerControlled) own.Add(state);
            else allies.Add(state);
        }

        Func<CVec2, float?>? height = null;
        if (includeTerrain && mission.Scene != null)
        {
            var scene = mission.Scene;
            height = p => scene.GetTerrainHeight(p.ToGame());
        }

        return SnapshotFactory.Create(
            ++_sequence,
            mission.CurrentTime,
            PhaseOf(mission),
            BattleType.Field,
            _ownTeam.Side == BattleSideEnum.Attacker,
            own,
            allies,
            enemies,
            height,
            BoundsOf(mission));
    }

    private Raw Read(Formation f, Allegiance allegiance)
    {
        var count = f.CountOfUnits;
        var r = new Raw
        {
            Formation = f,
            Id = _registry.IdFor(f),
            Allegiance = allegiance,
            PlayerControlled = allegiance == Allegiance.Own && !f.IsAIControlled,
            Count = count,
            Initial = _registry.UpdateInitialCount(f, count),
        };
        if (count == 0) return r;

        r.Position = f.CachedAveragePosition.ToCore();
        r.Facing = f.Direction.ToCore();
        r.Depth = f.Depth;
        r.Role = RoleOf(f);

        int n = 0, running = 0, moraleN = 0;
        float health = 0f, morale = 0f;
        int ammo = 0, ammoMax = 0;
        f.ApplyActionOnEachUnit(agent =>
        {
            n++;
            health += agent.HealthLimit > 0 ? agent.Health / agent.HealthLimit : 1f;
            var m = agent.GetMorale();
            if (m >= 0f)
            {
                morale += m;
                moraleN++;
            }
            if (agent.IsRunningAway) running++;
            CountAmmo(agent, ref ammo, ref ammoMax);
        });

        if (n > 0)
        {
            r.Health = health / n;
            r.Routing = running >= n * RoutShare;
        }
        if (moraleN > 0) r.Morale = Math.Min(1f, morale / moraleN / 100f); // game morale is 0..100
        if (ammoMax > 0 && Roles.IsMissile(r.Role)) r.Ammo = (float)ammo / ammoMax;

        if (allegiance == Allegiance.Own)
        {
            r.Activity = r.PlayerControlled
                ? "under the player's direct command"
                : f.AI.ActiveBehavior?.GetBehaviorString()?.ToString();
        }
        return r;
    }

    private static void CountAmmo(Agent agent, ref int ammo, ref int max)
    {
        for (var i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
        {
            var w = agent.Equipment[i];
            if (w.IsEmpty || !w.IsAnyConsumable()) continue;
            ammo += w.Amount;
            max += w.ModifiedMaxAmount;
        }
    }

    private static FormationRole RoleOf(Formation f)
    {
        var q = f.QuerySystem;
        if (q.IsRangedCavalryFormation) return FormationRole.HorseArcher;
        if (q.IsCavalryFormation) return FormationRole.Cavalry;
        if (q.IsRangedFormation) return FormationRole.Ranged;
        if (f.FormationIndex == FormationClass.Skirmisher) return FormationRole.Skirmisher;
        return FormationRole.Infantry;
    }

    private static void MarkEngagement(List<Raw> raws)
    {
        foreach (var a in raws)
        {
            if (a.Count == 0) continue;
            foreach (var b in raws)
            {
                if (b.Count == 0 || ReferenceEquals(a, b)) continue;
                if (!a.Formation.Team.IsEnemyOf(b.Formation.Team)) continue;
                var reach = (a.Depth + b.Depth) * 0.5f + MeleeGap;
                if (CVec2.Distance(a.Position, b.Position) <= reach)
                {
                    a.Engaged = true;
                    break;
                }
            }
        }
    }

    private static FormationState ToState(Raw r) => new()
    {
        Id = r.Id,
        Allegiance = r.Allegiance == Allegiance.Own && r.PlayerControlled ? Allegiance.Ally : r.Allegiance,
        Role = r.Role,
        Label = r.Formation.FormationIndex.ToString(),
        Count = r.Count,
        InitialCount = r.Initial,
        Position = r.Position,
        Facing = r.Facing,
        AverageHealth = r.Health,
        Morale = r.Morale,
        Ammo = r.Ammo,
        IsEngaged = r.Engaged,
        IsRouting = r.Routing,
        Activity = r.Activity,
    };

    private static BattlePhase PhaseOf(Mission mission)
    {
        if (mission.MissionEnded) return BattlePhase.Ended;
        if (mission.Mode == MissionMode.Deployment || !mission.IsDeploymentFinished) return BattlePhase.Deployment;
        return BattlePhase.Battle;
    }

    private static MapBounds? BoundsOf(Mission mission)
    {
        try
        {
            if (mission.Boundaries == null || mission.Boundaries.Count == 0) return null;
            mission.Boundaries.GetOrientedBoundariesBox(out TVec2 min, out TVec2 max);
            return new MapBounds(min.ToCore(), max.ToCore());
        }
        catch
        {
            return null;
        }
    }
}
