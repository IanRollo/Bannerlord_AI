namespace BannerlordAI.Core.Model;

/// <summary>What a formation actually is, judged by its units (not just the game's formation slot).</summary>
public enum FormationRole
{
    Infantry,
    Ranged,
    Cavalry,
    HorseArcher,
    Skirmisher,
}

public enum Allegiance
{
    Own,
    Ally,
    Enemy,
}

public enum BattlePhase
{
    Deployment,
    Battle,
    Ended,
}

public enum BattleType
{
    Field,
    Siege,
    SallyOut,
    Naval,
    Other,
}

/// <summary>High-level action for a formation; the adapter maps these to game behaviors.</summary>
public enum DirectiveAction
{
    Hold,
    Advance,
    Charge,
    Skirmish,
    Flank,
    ProtectFlank,
    Screen,
    Reserve,
    Retreat,
    Follow,
}

public enum Arrangement
{
    Line,
    ShieldWall,
    Loose,
    Square,
    Circle,
    Column,
    Scatter,
}

public enum FireOrder
{
    AtWill,
    Hold,
}

public enum FlankSide
{
    Left,
    Right,
}

public enum Stance
{
    Aggressive,
    Balanced,
    Defensive,
    Delaying,
    Withdraw,
}

public enum ConditionType
{
    /// <summary>An enemy matching Other is within Value metres of a formation matching Subject.</summary>
    EnemyWithin,
    /// <summary>Formations matching Subject together hold fewer than Value percent of their starting men.</summary>
    StrengthBelow,
    /// <summary>A formation matching Subject is in melee.</summary>
    Engaged,
    /// <summary>A formation matching Subject is routing.</summary>
    Routing,
    /// <summary>Value seconds have passed since the plan was issued.</summary>
    TimeElapsed,
    /// <summary>Own men divided by enemy men is below Value.</summary>
    StrengthRatioBelow,
}

public enum Tier
{
    Commander,
    Lieutenant,
}
