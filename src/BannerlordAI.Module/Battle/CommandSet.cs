using System.Collections.Generic;
using BannerlordAI.Core.Planning;
using TaleWorlds.MountAndBlade;

namespace BannerlordAI.Module.Battle;

/// <summary>
/// Immutable bundle the main thread publishes for the tactic, which runs inside the game's (possibly parallel) team
/// tick. Swapped atomically via a volatile reference; never mutated after publication.
/// </summary>
internal sealed class CommandSet
{
    public static readonly CommandSet Empty = new(0, false, new Dictionary<string, FormationCommand>(), new Dictionary<string, Formation>());

    public CommandSet(int version, bool active, IReadOnlyDictionary<string, FormationCommand> commands, IReadOnlyDictionary<string, Formation> formations)
    {
        Version = version;
        Active = active;
        Commands = commands;
        Formations = formations;
    }

    public int Version { get; }

    /// <summary>True while an LLM plan is in force; the tactic only wins the team AI's choice when this is set.</summary>
    public bool Active { get; }

    /// <summary>Own formation id -> command.</summary>
    public IReadOnlyDictionary<string, FormationCommand> Commands { get; }

    /// <summary>Formation id (any side) -> game formation, for resolving targets.</summary>
    public IReadOnlyDictionary<string, Formation> Formations { get; }

    public Formation? Resolve(string? id) => id != null && Formations.TryGetValue(id, out var f) ? f : null;
}
