using System.Diagnostics;
using System.Reflection;
using BannerlordAI.Core.Llm;
using BannerlordAI.Core.Model;
using BannerlordAI.Core.Orchestration;
using static BannerlordAI.Core.Tests.TestData;

namespace BannerlordAI.Core.Tests;

internal sealed class TestLog : IBrainLog
{
    public List<string> Infos { get; } = new();
    public List<string> Warns { get; } = new();
    public void Info(string message) { lock (Infos) Infos.Add(message); }
    public void Warn(string message) { lock (Warns) Warns.Add(message); }
    public void Transcript(Tier tier, LlmRequest request, LlmResult result) { }
}

/// <summary>
/// Drives a BattleBrain with scripted model replies. The brain starts calls on Task.Run and only collects results on a
/// later Tick, so <see cref="Tick"/> waits (real time, bounded) for the started call to finish before returning.
/// Note CommanderBusy stays true until the next Tick collects the finished task, so completion is observed on the
/// brain's pending task itself.
/// </summary>
internal sealed class BrainRig : IDisposable
{
    public Func<LlmRequest, string?> CommanderReply { get; set; } = _ => CommanderJson;
    public Func<LlmRequest, string?> LieutenantReply { get; set; } = _ => LieutenantNoOp;
    public ScriptedLlmClient Cmd { get; }
    public ScriptedLlmClient Lt { get; }
    public BattleBrain Brain { get; }
    public BrainConfig Config { get; }
    public TestLog Log { get; } = new();

    public BrainRig(Action<BrainConfig>? configure = null, bool lieutenant = false)
    {
        Config = new BrainConfig();
        configure?.Invoke(Config);
        Cmd = new ScriptedLlmClient(r => CommanderReply(r));
        Lt = new ScriptedLlmClient(r => LieutenantReply(r));
        Brain = new BattleBrain(Config, Cmd, lieutenant ? Lt : null, Log);
    }

    public BrainTickResult Tick(BattleSnapshot snap, bool settle = true, bool settleCommander = true, bool settleLieutenant = true)
    {
        var r = Brain.Tick(snap);
        if (settle) Settle(settleCommander, settleLieutenant);
        return r;
    }

    public void Settle(bool commander = true, bool lieutenant = true)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(2))
        {
            if ((!commander || Done("_commanderCall")) && (!lieutenant || Done("_lieutenantCall"))) return;
            Thread.Sleep(1);
        }
        throw new TimeoutException("model call did not finish within 2 s");
    }

    private bool Done(string field)
    {
        var call = typeof(BattleBrain).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Brain);
        if (call == null) return true;
        var task = (Task)call.GetType().GetField("Task")!.GetValue(call)!;
        return task.IsCompleted;
    }

    /// <summary>Standard snapshot: own army near (0,0), enemy centre <paramref name="enemyY"/> m ahead.</summary>
    public static BattleSnapshot S(double time, float enemyY = 300, Func<FormationState, FormationState>? ownEdit = null, BattlePhase phase = BattlePhase.Battle)
    {
        var own = OwnArmy();
        if (ownEdit != null) own = own.Select(ownEdit).ToList();
        return Snap(own, EnemyArmy(enemyY), time, phase: phase);
    }

    public void Dispose() => Brain.Dispose();
}
