using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BannerlordAI.Core.Compaction;
using BannerlordAI.Core.Llm;
using BannerlordAI.Core.Model;
using BannerlordAI.Core.Planning;

namespace BannerlordAI.Core.Orchestration;

public enum BrainMode
{
    /// <summary>No plan yet (or not in battle yet): leave the game's AI in control.</summary>
    Vanilla,
    /// <summary>A plan is installed: the adapter should drive formations from the commands.</summary>
    Llm,
    /// <summary>Gave up for this battle (failures or budget): restore the game's AI permanently.</summary>
    FallenBack,
}

public sealed class BrainTickResult
{
    public BrainMode Mode { get; init; }
    /// <summary>Commands that are new or changed since the last tick; send these to the game.</summary>
    public IReadOnlyList<FormationCommand> Changed { get; init; } = Array.Empty<FormationCommand>();
    /// <summary>
    /// The full current command set, keyed by formation id (for re-applying after the game resets orders). Instances
    /// are reused across ticks until they change meaningfully, so reference equality means "unchanged".
    /// </summary>
    public IReadOnlyDictionary<string, FormationCommand> Current { get; init; } = new Dictionary<string, FormationCommand>();
    /// <summary>Player-facing order-log lines.</summary>
    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Runs the Commander / Lieutenant / rules loop for one team. Call <see cref="Tick"/> from a single thread (the game
/// thread) about once a second with a fresh snapshot. Model calls run on background tasks; their results are picked
/// up on a later tick, so Tick never blocks.
/// </summary>
public sealed class BattleBrain : IDisposable
{
    private sealed class PendingCall
    {
        public Tier Tier;
        public BattleSnapshot Basis = null!;
        public int PlanVersion;
        public double StartedAt;
        public LlmRequest Request = null!;
        public Task<LlmResult> Task = null!;
    }

    private readonly BrainConfig _config;
    private readonly ILlmClient _commanderClient;
    private readonly ILlmClient? _lieutenantClient;
    private readonly IBrainLog _log;
    private readonly CancellationTokenSource _cts = new();
    private readonly EventDetector _events = new();
    private readonly List<BattleEvent> _eventsForCommander = new();
    private readonly List<BattleEvent> _eventsForLieutenant = new();
    private readonly List<string> _wakeReasons = new();
    private readonly Dictionary<string, FormationCommand> _emitted = new(StringComparer.OrdinalIgnoreCase);

    private Plan? _plan;
    private int _planVersion;
    private PendingCall? _commanderCall;
    private PendingCall? _lieutenantCall;
    private double _lastCommanderCallAt = double.NegativeInfinity;
    private double _lastLieutenantCallAt = double.NegativeInfinity;
    private string? _lieutenantFingerprint;
    private int _commanderFailures;
    private int _lieutenantFailures;
    private bool _lieutenantDisabled;
    private bool _battleStarted;
    private BrainMode _mode = BrainMode.Vanilla;
    private string? _fallbackReason;
    private BattleSnapshot? _latest;

    public BattleBrain(BrainConfig config, ILlmClient commanderClient, ILlmClient? lieutenantClient = null, IBrainLog? log = null)
    {
        _config = config;
        _commanderClient = commanderClient;
        _lieutenantClient = lieutenantClient;
        _log = log ?? NullBrainLog.Instance;
        _lieutenantDisabled = lieutenantClient == null || !config.Lieutenant.Enabled;
    }

    public BrainStats Stats { get; } = new();

    public BrainMode Mode => _mode;

    public Plan? CurrentPlan => _plan;

    public string? FallbackReason => _fallbackReason;

    public bool CommanderBusy => _commanderCall != null;

    public bool LieutenantBusy => _lieutenantCall != null;

    public BrainTickResult Tick(BattleSnapshot snap)
    {
        _latest = snap;
        var messages = new List<string>();

        if (_mode == BrainMode.FallenBack || snap.Phase == BattlePhase.Ended)
            return Result(snap, messages, computeCommands: false);

        if (snap.Phase == BattlePhase.Deployment)
            return Result(snap, messages, computeCommands: false);

        if (!_battleStarted)
        {
            _battleStarted = true;
            _wakeReasons.Add("battle start");
        }

        CollectCompletedCalls(snap, messages);
        if (_mode == BrainMode.FallenBack) return Result(snap, messages, computeCommands: false);

        foreach (var e in _events.Update(snap))
        {
            AddCapped(_eventsForCommander, e);
            AddCapped(_eventsForLieutenant, e);
            if (e.Severity == EventSeverity.Major) _wakeReasons.Add(e.Text);
        }

        if (_plan != null) FireDueContingencies(snap, messages);

        if (_config.MaxTokensPerBattle > 0 && Stats.TotalTokens >= _config.MaxTokensPerBattle)
        {
            FallBack("token budget for this battle used up", messages);
            return Result(snap, messages, computeCommands: false);
        }

        MaybeStartCommander(snap);
        MaybeStartLieutenant(snap);

        return Result(snap, messages, computeCommands: _plan != null);
    }

    // ---------------------------------------------------------------- results

    private void CollectCompletedCalls(BattleSnapshot snap, List<string> messages)
    {
        if (_commanderCall != null && _commanderCall.Task.IsCompleted)
        {
            var call = _commanderCall;
            _commanderCall = null;
            HandleCommanderResult(call, GetResult(call), snap, messages);
        }
        if (_lieutenantCall != null && _lieutenantCall.Task.IsCompleted)
        {
            var call = _lieutenantCall;
            _lieutenantCall = null;
            HandleLieutenantResult(call, GetResult(call), snap, messages);
        }
    }

    private static LlmResult GetResult(PendingCall call)
    {
        if (call.Task.IsFaulted) return LlmResult.Fail("client threw: " + call.Task.Exception?.GetBaseException().Message);
        if (call.Task.IsCanceled) return LlmResult.Fail("cancelled");
        return call.Task.Result;
    }

    private void HandleCommanderResult(PendingCall call, LlmResult result, BattleSnapshot now, List<string> messages)
    {
        _log.Transcript(Tier.Commander, call.Request, result);
        ParseOutcome<Plan>? parsed = null;
        if (result.Success)
            parsed = PlanParser.ParseCommander(result.Text, call.Basis, _planVersion + 1, now.Time);

        var ok = parsed != null && parsed.Ok;
        Stats.Commander.Record(result, ok);

        if (!ok)
        {
            _commanderFailures++;
            var why = !result.Success ? result.Error : string.Join("; ", parsed!.Errors);
            _log.Warn($"Commander call failed ({_commanderFailures}/{_config.MaxConsecutiveFailures}): {why}");
            if (_commanderFailures == 1) messages.Add("Commander unavailable: " + why);
            if (_commanderFailures >= _config.MaxConsecutiveFailures)
                FallBack("the commander model failed " + _commanderFailures + " times in a row (" + why + ")", messages);
            return;
        }

        _commanderFailures = 0;
        foreach (var w in parsed!.Warnings) _log.Warn("Commander plan: " + w);
        InstallPlan(parsed.Value!, now, messages);
    }

    private void InstallPlan(Plan plan, BattleSnapshot now, List<string> messages)
    {
        _planVersion = plan.Version;

        // Formations the commander left out keep their previous orders.
        if (_plan != null)
        {
            foreach (var kv in _plan.Directives)
                if (!plan.Directives.ContainsKey(kv.Key) && now.Find(kv.Key)?.IsAlive == true)
                    plan.Directives[kv.Key] = kv.Value;
        }

        foreach (var id in plan.Directives.Keys.ToList())
            plan.Directives[id] = PinStationary(plan.Directives[id], now);

        _plan = plan;
        _mode = BrainMode.Llm;
        Stats.PlansInstalled++;
        _eventsForLieutenant.Clear();
        _lieutenantFingerprint = null;

        var line = plan.OrderLog.Length > 0 ? plan.OrderLog : plan.Intent;
        if (line.Length > 0) messages.Add("Commander: " + line);
        _log.Info($"Plan v{plan.Version} installed: {plan.Intent} ({plan.Directives.Count} directives, {plan.Contingencies.Count} contingencies)");
    }

    private void HandleLieutenantResult(PendingCall call, LlmResult result, BattleSnapshot now, List<string> messages)
    {
        _log.Transcript(Tier.Lieutenant, call.Request, result);
        if (_plan == null || _plan.Version != call.PlanVersion)
        {
            Stats.Lieutenant.Record(result, true);
            _log.Info("Lieutenant reply discarded: the plan changed while it was thinking");
            return;
        }

        var parsed = result.Success ? PlanParser.ParseLieutenant(result.Text, call.Basis, _plan) : null;
        var ok = parsed != null && parsed.Ok;
        Stats.Lieutenant.Record(result, ok);
        if (!ok)
        {
            _lieutenantFailures++;
            var why = !result.Success ? result.Error : string.Join("; ", parsed!.Errors);
            _log.Warn($"Lieutenant call failed ({_lieutenantFailures}): {why}");
            if (_lieutenantFailures >= _config.MaxConsecutiveFailures)
            {
                _lieutenantDisabled = true;
                messages.Add("Lieutenant unavailable; the commander will manage alone.");
            }
            return;
        }

        _lieutenantFailures = 0;
        foreach (var w in parsed!.Warnings) _log.Warn("Lieutenant: " + w);
        var orders = parsed.Value!;

        foreach (var id in orders.FireContingencies)
        {
            var c = _plan.Contingencies.FirstOrDefault(x => x.Id == id && x.IsArmed);
            if (c != null) FireContingency(c, now, messages, "lieutenant");
        }

        foreach (var patch in orders.Adjustments) ApplyPatch(patch, now);

        if (orders.WakeCommander)
            _wakeReasons.Add("lieutenant: " + (orders.Reason.Length > 0 ? orders.Reason : "plan no longer fits"));

        if (orders.OrderLog != null) messages.Add("Lieutenant: " + orders.OrderLog);
        if (!orders.IsNoOp) _log.Info($"Lieutenant acted: fired [{string.Join(",", orders.FireContingencies)}], {orders.Adjustments.Count} adjustments, wake={orders.WakeCommander} {orders.Reason}");
    }

    private void ApplyPatch(DirectivePatch patch, BattleSnapshot now)
    {
        if (_plan == null || !_plan.Directives.TryGetValue(patch.FormationId, out var current)) return;
        var action = patch.Action;
        if (action != current.Action && action != DirectiveAction.Hold && action != DirectiveAction.Retreat)
        {
            _log.Warn($"Lieutenant tried to change {patch.FormationId} from {Wire.ToWire(current.Action)} to {Wire.ToWire(action)}; kept {Wire.ToWire(current.Action)}");
            action = current.Action;
        }
        var actionChanged = action != current.Action;
        var updated = current.With(
            action: action,
            target: patch.Target,
            side: patch.Side,
            anchor: patch.Anchor,
            // A new action invalidates an old anchor unless the patch gives one.
            clearAnchor: actionChanged && patch.Anchor == null,
            arrangement: patch.Arrangement,
            fire: patch.Fire,
            source: "lieutenant");
        _plan.Directives[patch.FormationId] = PinStationary(updated, now);
    }

    // ------------------------------------------------------------ contingencies

    private void FireDueContingencies(BattleSnapshot snap, List<string> messages)
    {
        foreach (var c in _plan!.Contingencies)
            if (c.IsArmed && ConditionEvaluator.IsMet(c, snap, _plan.IssuedAt))
                FireContingency(c, snap, messages, "conditions met");
    }

    private void FireContingency(Contingency c, BattleSnapshot snap, List<string> messages, string why)
    {
        c.FiredAt = snap.Time;
        foreach (var d in c.Directives)
            _plan!.Directives[d.FormationId] = PinStationary(d, snap);
        Stats.ContingenciesFired++;
        messages.Add($"Contingency {c.Id}: {(c.Description.Length > 0 ? c.Description : "triggered")}");
        _log.Info($"Contingency {c.Id} fired ({why})");
    }

    /// <summary>Hold / reserve without an anchor means "stay where you are now": pin the current position.</summary>
    private static Directive PinStationary(Directive d, BattleSnapshot snap)
    {
        if (d.Anchor != null || (d.Action != DirectiveAction.Hold && d.Action != DirectiveAction.Reserve)) return d;
        var self = snap.Find(d.FormationId);
        return self == null || !self.IsAlive ? d : d.With(anchor: Anchor.AbsoluteWorld(self.Position));
    }

    // --------------------------------------------------------------- scheduling

    private void MaybeStartCommander(BattleSnapshot snap)
    {
        if (_commanderCall != null) return;
        if (Stats.Commander.Calls >= _config.Commander.MaxCallsPerBattle)
        {
            if (_plan == null) FallBack("commander call limit reached without a plan", new List<string>());
            return;
        }

        var sinceLast = snap.Time - _lastCommanderCallAt;
        var cad = _config.Cadence;
        string? reason = null;

        if (_plan == null)
        {
            // No plan yet: keep trying, but not in a tight loop after failures.
            if (sinceLast >= Math.Min(cad.CommanderCooldown, 5)) reason = _wakeReasons.Count > 0 ? string.Join("; ", _wakeReasons) : "no plan yet";
        }
        else if (_wakeReasons.Count > 0 && sinceLast >= cad.CommanderCooldown)
        {
            reason = string.Join("; ", _wakeReasons.Distinct());
        }
        else if (sinceLast >= cad.CommanderInterval)
        {
            reason = "scheduled review";
        }

        if (reason == null) return;

        var request = new LlmRequest
        {
            Model = _config.Commander.Model,
            SystemPrompt = Prompts.Commander,
            UserContent = Prompts.CommanderUserContent(reason, snap, _plan, Recent(_eventsForCommander), _config.IncludeTerrain),
            SchemaName = PlanSchemas.CommanderName,
            SchemaJson = PlanSchemas.Commander,
            Effort = _config.Commander.Effort,
            MaxTokens = _config.Commander.MaxTokens,
            Timeout = TimeSpan.FromSeconds(_config.Commander.TimeoutSeconds),
        };

        _commanderCall = Start(Tier.Commander, _commanderClient, request, snap);
        _lastCommanderCallAt = snap.Time;
        _wakeReasons.Clear();
        _eventsForCommander.Clear();
        _log.Info($"Commander called at {Wire.Clock(snap.Time)}: {reason}");
    }

    private void MaybeStartLieutenant(BattleSnapshot snap)
    {
        if (_lieutenantDisabled || _lieutenantClient == null || _plan == null || _lieutenantCall != null) return;
        if (Stats.Lieutenant.Calls >= _config.Lieutenant.MaxCallsPerBattle) return;

        var cad = _config.Cadence;
        var interval = Cadence.LieutenantInterval(snap.MinEnemyDistance(), cad);
        if (interval == null) return;
        if (snap.Time - _lastLieutenantCallAt < interval.Value) return;
        if (snap.Time - _plan.IssuedAt < cad.LieutenantGapAfterPlan) return;

        var fingerprint = Fingerprint(snap);
        if (fingerprint == _lieutenantFingerprint) return; // nothing meaningful changed since the last check

        var request = new LlmRequest
        {
            Model = _config.Lieutenant.Model,
            SystemPrompt = Prompts.Lieutenant,
            UserContent = Prompts.LieutenantUserContent(snap, _plan, Recent(_eventsForLieutenant)),
            SchemaName = PlanSchemas.LieutenantName,
            SchemaJson = PlanSchemas.Lieutenant,
            Effort = _config.Lieutenant.Effort,
            MaxTokens = _config.Lieutenant.MaxTokens,
            Timeout = TimeSpan.FromSeconds(_config.Lieutenant.TimeoutSeconds),
        };

        _lieutenantCall = Start(Tier.Lieutenant, _lieutenantClient, request, snap);
        _lastLieutenantCallAt = snap.Time;
        _lieutenantFingerprint = fingerprint;
        _eventsForLieutenant.Clear();
    }

    private PendingCall Start(Tier tier, ILlmClient client, LlmRequest request, BattleSnapshot snap)
    {
        var token = _cts.Token;
        return new PendingCall
        {
            Tier = tier,
            Basis = snap,
            PlanVersion = _plan?.Version ?? 0,
            StartedAt = snap.Time,
            Request = request,
            Task = Task.Run(() => client.CompleteJsonAsync(request, token), token),
        };
    }

    private void FallBack(string reason, List<string> messages)
    {
        if (_mode == BrainMode.FallenBack) return;
        _mode = BrainMode.FallenBack;
        _fallbackReason = reason;
        messages.Add("AI commander stood down (" + reason + "); the game's AI takes over.");
        _log.Warn("Fallback to game AI: " + reason);
        _cts.Cancel();
    }

    // ----------------------------------------------------------------- helpers

    private BrainTickResult Result(BattleSnapshot snap, List<string> messages, bool computeCommands)
    {
        if (!computeCommands || _plan == null)
            return new BrainTickResult { Mode = _mode, Messages = messages };

        var changed = new List<FormationCommand>();
        var current = new Dictionary<string, FormationCommand>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in snap.Own)
        {
            if (!f.IsAlive || !_plan.Directives.TryGetValue(f.Id, out var d)) continue;
            var cmd = DirectiveResolver.Resolve(d, snap);
            if (cmd == null) continue;
            if (!_emitted.TryGetValue(f.Id, out var prev) || prev.DiffersMeaningfully(cmd, _config.CommandPositionTolerance))
            {
                _emitted[f.Id] = cmd;
                changed.Add(cmd);
            }
            // Reuse the emitted instance until it changes meaningfully, so consumers can compare by reference.
            current[f.Id] = _emitted[f.Id];
        }
        return new BrainTickResult { Mode = _mode, Changed = changed, Current = current, Messages = messages };
    }

    private IReadOnlyList<BattleEvent> Recent(List<BattleEvent> list) =>
        list.Count <= _config.MaxEventsInPrompt ? list.ToList() : list.Skip(list.Count - _config.MaxEventsInPrompt).ToList();

    private void AddCapped(List<BattleEvent> list, BattleEvent e)
    {
        list.Add(e);
        var cap = Math.Max(_config.MaxEventsInPrompt * 2, 10);
        if (list.Count > cap) list.RemoveRange(0, list.Count - cap);
    }

    /// <summary>Coarse state hash: 25 m position cells, 10% strength steps, melee/rout flags.</summary>
    internal static string Fingerprint(BattleSnapshot s)
    {
        var sb = new StringBuilder();
        foreach (var f in s.All)
        {
            sb.Append(f.Id).Append(':');
            if (!f.IsAlive) { sb.Append("x;"); continue; }
            sb.Append((int)Math.Floor(f.Position.X / 25f)).Append(',').Append((int)Math.Floor(f.Position.Y / 25f))
              .Append(',').Append((int)(f.Strength * 10))
              .Append(f.IsEngaged ? 'E' : '-').Append(f.IsRouting ? 'R' : '-').Append(';');
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
