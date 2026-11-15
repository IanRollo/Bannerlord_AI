using System.Collections.Generic;
using TaleWorlds.MountAndBlade;

namespace BannerlordAI.Module.Battle;

/// <summary>
/// Stable short ids for formations, from one team's point of view. Own formations use the game's slot number (F1 is
/// the in-game "formation 1"), so order-log lines match what the player sees. Other formations get A#/E# in order of
/// first sighting. Also tracks the largest head-count seen per formation, used as its starting strength.
/// Main thread only, except <see cref="OwnId"/> which is pure.
/// </summary>
internal sealed class FormationRegistry
{
    private readonly Team _ownTeam;
    private readonly Dictionary<Formation, string> _ids = new();
    private readonly Dictionary<string, Formation> _byId = new();
    private readonly Dictionary<Formation, int> _maxCount = new();
    private int _nextEnemy = 1;
    private int _nextAlly = 1;

    public FormationRegistry(Team ownTeam)
    {
        _ownTeam = ownTeam;
    }

    public static string OwnId(Formation f) => "F" + ((int)f.FormationIndex + 1);

    public bool HasSeen(Formation f) => _ids.ContainsKey(f);

    public string IdFor(Formation f)
    {
        if (_ids.TryGetValue(f, out var id)) return id;
        if (f.Team == _ownTeam) id = OwnId(f);
        else if (f.Team.IsEnemyOf(_ownTeam)) id = "E" + _nextEnemy++;
        else id = "A" + _nextAlly++;
        _ids[f] = id;
        _byId[id] = f;
        return id;
    }

    /// <summary>Records the current head-count and returns the largest seen (the formation's starting strength).</summary>
    public int UpdateInitialCount(Formation f, int count)
    {
        if (!_maxCount.TryGetValue(f, out var max) || count > max)
        {
            _maxCount[f] = count;
            return count;
        }
        return max;
    }

    public IReadOnlyDictionary<string, Formation> CopyById() => new Dictionary<string, Formation>(_byId);
}
