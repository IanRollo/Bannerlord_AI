using System;
using System.Net;
using BannerlordAI.Module.Battle;
using BannerlordAI.Module.Settings;
using TaleWorlds.MountAndBlade;

namespace BannerlordAI.Module;

public sealed class SubModule : MBSubModuleBase
{
    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        DependencyResolver.Install();
        try
        {
            // .NET Framework may default to older TLS on some systems; API endpoints require TLS 1.2+.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
        catch (NotSupportedException)
        {
        }
    }

    public override void OnMissionBehaviorInitialize(Mission mission)
    {
        base.OnMissionBehaviorInitialize(mission);
        var settings = ModSettings.Read();
        if (!settings.Enabled) return;
        if (!settings.ControlPlayerTeam && !settings.ControlAllies && !settings.ControlEnemies) return;
        // Battle type isn't known yet here (it is set in MissionCombatantsLogic.EarlyStart); the logic checks later.
        mission.AddMissionBehavior(new AiCommanderLogic(settings));
    }
}
