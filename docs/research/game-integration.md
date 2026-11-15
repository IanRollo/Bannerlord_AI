# Bannerlord game-integration research (LLM-as-general mod)

Target: Bannerlord 1.4.x, C# / .NET Framework 4.7.2. The game is not installed, so everything below comes from open-source code and docs.

## Sources and conventions

**Confidence tags:** `[CONFIRMED]` means I read it in source. `[INFERRED]` means a deduction from source I read. `[UNKNOWN]` means not found.

**Primary source for engine signatures: the decompiled TaleWorlds archive, pinned to the v1.4.8 commit.**
- Repo: https://github.com/Sartiye/Bannerlord-Taleworlds-Assembly-Decompiled-Archive
- Pinned commit: `a948fdf07e4cebdff2a5e4c51136fe78365e4123` (commit message "v1.4.8").
- File URL pattern: `https://github.com/Sartiye/Bannerlord-Taleworlds-Assembly-Decompiled-Archive/blob/a948fdf07e4cebdff2a5e4c51136fe78365e4123/TaleWorlds.MountAndBlade/TaleWorlds.MountAndBlade/<Class>.cs`
- Other namespaces: `TaleWorlds.Engine/TaleWorlds.Engine/<Class>.cs`, `TaleWorlds.Library/TaleWorlds.Library/<Class>.cs`, `TaleWorlds.Core/TaleWorlds.Core/<Class>.cs`.
- The repo has commits for v1.2.10-12, 1.3.x, 1.4.5/7/8 and 1.5.7. The decompiled source is real game code, not a mod.
- Line numbers below are v1.4.8 unless noted. Class-level signatures of the tactic and behavior classes match between v1.4.8 and the latest (v1.5.7) archive as far as I compared (TacticComponent member list; line counts of BehaviorComponent/FormationAI/TeamAIComponent/TeamAIGeneral are identical).
- This is a third-party archive of decompiled code. Verify against your own ILSpy/dnSpy on the real DLLs once the game is installed.

**Mod sources:**
- **RBM** (Realistic Battle Mod): https://github.com/Fellow93/RealisticBattleProject. HEAD targets **1.5.7** (its SubModule.xml says `DependentVersion="v1.5.7"`). It is the best example of custom tactics and behaviors. The API shapes it uses are identical to 1.4.8.
- **RTSCamera**: https://github.com/lzh-mb-mod/RTSCamera. `LiZhenhuan1019/RTSCamera` does not exist; the repo is `lzh-mb-mod/RTSCamera`, default branch `master`. Its current SubModule.xml targets 1.4.8.
- The Norbivar and gediorios forks exist (2025 and 2022) but are stale. I did not use them.
- **MCM**: https://github.com/Aragas/Bannerlord.MBOptionScreen (default branch `dev`). `BUTR/Bannerlord.MCM` returned 404.
- **API docs**: apidoc.bannerlord.com/v/1.2.12 is reachable but incomplete for this work. Its TacticComponent page omits `protected internal` and `public virtual` members such as `OnApply`, `TickOccasionally` and `GetTacticWeight`. I found no pages for `TeamAIComponent` or `FormationAI` at the URLs I tried (404). Use the decompiled source instead.

---

## 0. Cross-cutting facts that shape the design

1. **Team AI ticks on a non-main thread.** `Mission.TickAgentsAndTeamsImp` (Mission.cs:3624-3641) runs `TWParallel.For` over agents, then `team.Tick(dt)` for each team, then sets `tickCompleted = true`. A `WaitTickCompletion` spin-wait (Mission.cs:3608) shows the main thread synchronises with this. `Team.Tick` leads to `TeamAI.Tick`, then `TacticComponent.TickOccasionally`, and `FormationAI.Tick` leads to `BehaviorComponent.TickOccasionally` and `CalculateCurrentOrder`. RBM documents this in its class comments: "TickOccasionally and GetTacticWeight run on the async AI thread ... no engine call, no MBRandom" (RBMTacticHedgehog.cs lines 21-22). `[CONFIRMED]` that tactic/behavior code runs inside the async team tick (Mission.cs + RBM comment). `[INFERRED]` consequence: do not do HTTP or blocking LLM calls there; enqueue work and apply it from a MissionBehavior's `OnMissionTick`. RBM also warns that native calls such as `GetNavMeshZ`, `GetGroundVec3` and navmesh lookups on a `WorldPosition` can crash or stall off the main thread (RBM `Utilities.Formations.cs` lines 39-42).
2. **Team AI only ticks if the team has bots.** `Team.Tick` calls `TeamAI.Tick(dt)` only if `TeamAI != null && HasBots` (Team.cs:591). `HasBots` is any non-mount active agent that is not player-controlled (Team.cs:130-143). `TeamAIComponent.TickOccasionally` additionally requires `Mission.Current.AllowAiTicking` (TeamAIComponent.cs:340). `[CONFIRMED]`
3. **`FormationAI.Tick` runs only for AI-controlled formations.** `Formation.Tick` (Formation.cs:2299) is `if (Team.HasTeamAi && (IsAIControlled || Team.IsPlayerSergeant)) AI.Tick(); else IsAITickedAfterSplit = true;`. So a non-AI-controlled formation never has behaviors applied, and direct orders persist. `[CONFIRMED]`
4. **AI tick cadence.** `FormationAI` ticks every 0.5s via its own timer (`FormationAI.Tick()` at line 232). `TeamAIComponent` re-evaluates tactics every 5s (`_nextTacticChooseTime = MissionTime.SecondsFromNow(5f)`) and calls `tactic.TickOccasionally()` every `applyTimerTime` (default 1s for `TeamAIGeneral`). `[CONFIRMED]` (TeamAIComponent.cs:196-213).

---

## 1. Replacing the team's tactic with a custom TacticComponent

### Answer
- Tactics are `TacticComponent` subclasses held in `TeamAIComponent._availableTactics`.
- The public API is `team.AddTacticOption(tactic)`, `team.ClearTacticOptions()`, `team.RemoveTacticOption(Type)` and `team.ResetTactic()` (Team.cs:415-445). These forward to `TeamAI` only if `HasTeamAi`. `TeamAIComponent` has the same-named public methods (TeamAIComponent.cs:129-143).
- **Hook and ordering.** Vanilla `MissionCombatantsLogic.EarlyStart` creates `TeamAIGeneral` for every team (including the player team) via `team.AddTeamAI(new TeamAIGeneral(Mission, team))` for `FieldBattle`, then adds `TacticCharge`, `TacticFullScaleAttack` and so on, gated by tactics skill. `[CONFIRMED]` (MissionCombatantsLogic.cs, `EarlyStart`).
- `Mission.AfterStart` (Mission.cs:3823-3850) runs in this order:
  1. `OnBeforeMissionBehaviorInitialize` for each submodule.
  2. `OnBehaviorInitialize` for all behaviors. `MissionCombatantsLogic` creates the teams here.
  3. `MBSubModuleBase.OnMissionBehaviorInitialize(mission)` for each submodule. This is where a mod adds its behaviors.
  4. `EarlyStart` for every behavior in list order. Mod behaviors were appended, so they come after `MissionCombatantsLogic`.
  5. Deployment-plan init, then `AfterStart` for every behavior.
- **Recommended hook:** your own `MissionLogic.EarlyStart()` or `AfterStart()`. At that point every team has TeamAI and the vanilla options, so call `ClearTacticOptions()`, `AddTacticOption(new LlmTactic(team))`, then `team.ResetTactic()`.
  - `[INFERRED]` from the ordering above. RBM instead Harmony-postfixes `MissionCombatantsLogic.EarlyStart` and does the same thing (`team.ClearTacticOptions(); team.AddTacticOption(...)`), which `[CONFIRMED]` the semantics (RBM `RealisticBattleAiModule/AiModule/Tactics/Lifecycle.cs` lines 56-103).
  - `Team.ResetTactic()` calls `TeamAI.ResetTactic(keepCurrentTactic: true)`, which runs `MakeDecision()` and `TickOccasionally()` (TeamAIComponent.cs:184-194). Because `CurrentTactic.ResetTacticalPositions()` returns false by default, `MakeDecision` immediately switches to the best-weighted new tactic. `[CONFIRMED]`
- **Single-tactic caveat:** `MakeDecision` picks `MaxBy(GetTacticWeight() * (isCurrent ? 1.5 : 1))`. With one option it is always chosen, as long as the team has any non-empty formation (otherwise `MakeDecision` returns early). `[CONFIRMED]` (TeamAIComponent.cs:254-337)
- Field-battle gating in RBM: `if (Mission.Current.MissionTeamAIType == Mission.MissionTeamAITypeEnum.FieldBattle)` and `Mission.Current.Teams.Where(t => t.HasTeamAi)`.

### TacticComponent signatures (v1.4.8, TacticComponent.cs)
```csharp
public abstract class TacticComponent {
  public Team Team { get; protected set; }                              // :40
  protected MBList<Formation> FormationsIncludingEmpty => Team.FormationsIncludingEmpty;   // :44 (8 regular formations)
  protected MBList<Formation> FormationsIncludingSpecialAndEmpty => ...;                   // :42 (10: + General, Bodyguard)
  protected bool AreFormationsCreated { get; }                          // :46
  protected int _AIControlledFormationCount; protected bool IsTacticReapplyNeeded;         // :20,22
  protected Formation _mainInfantry, _archers, _leftCavalry, _rightCavalry, _rangedCavalry; // :26-34
  protected TacticComponent(Team team)                                  // :66  (protected, not public)
  protected internal virtual void OnCancel()                            // :78
  protected internal virtual void OnApply()                             // :82  sets IsTacticReapplyNeeded = true
  public virtual void TickOccasionally()                                // :87  base must be called (first-tactic bookkeeping, mission-ending cleanup)
  protected internal virtual float GetTacticWeight()                    // :589 default 0f
  protected virtual bool CheckAndSetAvailableFormationsChanged()        // :463
  protected virtual void ManageFormationCounts()                        // :516
  protected void ManageFormationCounts(int infantry, int ranged, int cavalry, int rangedCavalry) // :520
  protected internal virtual bool ResetTacticalPositions()              // :615 default false
  protected internal virtual void DebugTick(float dt)                   // :168
  public void ResetTactic()                                             // :468
  public static void SetDefaultBehaviorWeights(Formation f)             // :581 Charge, PullBack, Stop, Reserve = 1
  protected static List<Formation> ChooseAndSortByPriority(IEnumerable<Formation>, Func<Formation,bool> isEligible, Func<Formation,bool> isPrioritized, Func<Formation,float> score) // :508
  protected void SoundTacticalHorn(int soundCode)                       // :571
}
```
Nothing is `abstract` except the class itself. A minimal subclass needs only the constructor (`: base(team)`), `TickOccasionally` and `GetTacticWeight`.

### Overriding from a mod assembly `[CONFIRMED]` (RBM RealisticBattleAiModule/AiModule/Tactics/Custom/RBMTacticHedgehog.cs)
`protected internal` members are overridden with plain `protected override` from another assembly. `TickOccasionally` is `public override`.
```csharp
public class RBMTacticHedgehog : TacticComponent {                 // :24
    public RBMTacticHedgehog(Team team) : base(team) { }           // :50
    protected override void OnApply() { base.OnApply(); /*reset state*/ }          // :61
    protected override void ManageFormationCounts() { ManageFormationCounts(1,1,2,1); ... }  // :76
    protected override bool CheckAndSetAvailableFormationsChanged() { ... }         // :217
    public override void TickOccasionally() {                                       // :248
        if (!base.AreFormationsCreated) return;
        if (CheckAndSetAvailableFormationsChanged()) { ManageFormationCounts(); ApplyPhase(); }
        ...
        if (IsTacticReapplyNeeded) ApplyPhase();     // ApplyPhase ends with IsTacticReapplyNeeded = false
        base.TickOccasionally();                      // :318
    }
    protected override float GetTacticWeight() { ... return MathF.Max(0.01f, weight); }   // :477
}
```
Vanilla reference, the simplest tactic (TacticCharge.cs, v1.4.8): `TickOccasionally` loops `FormationsIncludingEmpty`, and for each formation with `CountOfUnits > 0` calls `AI.ResetBehaviorWeights()`, `TacticComponent.SetDefaultBehaviorWeights(f)`, then `AI.SetBehaviorWeight<BehaviorCharge>(10000f)`. It then calls `base.TickOccasionally()`.

### Sketch for our mod `[INFERRED]`
```csharp
public class LlmArmyLogic : MissionLogic {      // added in OnMissionBehaviorInitialize
    public override void EarlyStart() {          // runs after MissionCombatantsLogic.EarlyStart
        if (!Mission.IsFieldBattle) return;      // MissionTeamAIType is set in MissionCombatantsLogic.EarlyStart, so check here, not in OnBehaviorInitialize
        foreach (var team in Mission.Teams.Where(t => t.HasTeamAi)) {
            team.ClearTacticOptions();
            team.AddTacticOption(new LlmTactic(team));
            team.ResetTactic();
        }
    }
}
```
Alternative with no tactic: leave vanilla tactics and issue direct orders to non-AI formations (see Q4/Q5). That avoids TeamAI entirely.

---

## 2. Behavior weights: how tactics drive formations

### Answer
- `formation.AI` is a `FormationAI`, created in each `Formation`'s `Reset()` (Formation.cs:2671), and `Team` builds all 10 `Formation` objects in its constructor (Team.cs:267-277). So every formation of every team always has an AI object.
- `ResetBehaviorWeights()` calls `behavior.ResetBehavior()` on every registered behavior. By default that sets `WeightFactor = 0` (FormationAI.cs:358, BehaviorComponent `ResetBehavior`).
- `SetBehaviorWeight<T>(float w)` finds the first registered behavior `is T`, sets its `WeightFactor`, and returns it (FormationAI.cs:120-131). `[CONFIRMED]`
- `FindBestBehavior` (FormationAI.cs:169) scores `behavior.GetAIWeight() * WeightFactor` for every behavior whose `WeightFactor > 1e-7`. The active behavior gets a stickiness multiplier of Lerp(1.2 to 2.0) over `PreserveExpireTime`, which is set to now+10s on activation. The best one becomes `ActiveBehavior`.
- The active behavior's `TickOccasionally()` is invoked **only if `_formation.IsAIControlled`** (FormationAI.TickOccasionally). Beyond that, `Formation.Tick` doesn't even call `AI.Tick()` for non-AI formations (see 0.3). So:
  - **A formation must be `IsAIControlled` for tactic/behavior weights to have any effect.** `[CONFIRMED]`
  - The `SetBehaviorWeight` call itself still works on non-AI formations; it just has no consequence.
- A tactic usually calls these inside `TickOccasionally`/`ApplyPhase`, whenever `IsTacticReapplyNeeded` or the set of formations changed.

### Example, RBM hedgehog tactic (RBMTacticHedgehog.cs:110-160) `[CONFIRMED]`
```csharp
_mainInfantry.AI.ResetBehaviorWeights();
_mainInfantry.AI.SetBehaviorWeight<BehaviorHoldHighGround>(1f);
_mainInfantry.AI.SetBehaviorWeight<BehaviorRegroup>(1.75f);
RBMBehaviorHedgehog.TryAssign(_mainInfantry, this, ...);  // custom: formation.AI.SetBehaviorWeight<RBMBehaviorHedgehog>(1f)
...
cavalry.AI.ResetBehaviorWeights();
TacticComponent.SetDefaultBehaviorWeights(cavalry);
cavalry.AI.SetBehaviorWeight<BehaviorProtectFlank>(1f).FlankSide = FormationAI.BehaviorSide.Left;   // the returned behavior can be configured
```
Formation selection: `ManageFormationCounts(1,1,2,1)` splits and merges AI-controlled formations to 1 infantry, 1 ranged, 2 cavalry and 1 horse archer. Then `ChooseAndSortByPriority(nonEmpty, f => f.QuerySystem.IsInfantryFormation, f => f.IsAIControlled, f => f.QuerySystem.FormationPower).FirstOrDefault()` picks the formation for each role (:76-108). It also sets `f.AI.IsMainFormation = true` and `f.AI.Side = FormationAI.BehaviorSide.Left/Middle/Right`.

Vanilla behavior types registered by `TeamAIGeneral.OnUnitAddedToFormationForTheFirstTime` (v1.4.8, TeamAIGeneral.cs, non-server branch): BehaviorCharge, PullBack, Regroup, Reserve, Retreat, Stop, TacticalCharge, Advance, CautiousAdvance, CavalryScreen, Defend, DefensiveRing, FireFromInfantryCover, Flank, HoldHighGround, HorseArcherSkirmish, MountedSkirmish, ProtectFlank, ScreenedSkirmish, Skirmish, SkirmishBehindFormation, SkirmishLine, Vanguard and ShootFromCliff. Plus BehaviorGeneral for the General formation and BehaviorProtectGeneral for the Bodyguard.

---

## 3. Custom BehaviorComponent

### Signatures (v1.4.8, BehaviorComponent.cs) `[CONFIRMED]`
```csharp
public abstract class BehaviorComponent {
  protected FormationAI.BehaviorSide _behaviorSide;
  protected FacingOrder CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;    // protected FIELD
  public bool IsCurrentOrderChanged;                                     // public field
  public Formation Formation { get; private set; }
  public MovementOrder CurrentOrder { get; protected set; }              // setter sets IsCurrentOrderChanged = true
  public float PreserveExpireTime { get; set; }
  public float WeightFactor { get; set; }
  public float BehaviorCoherence { get; set; }
  public virtual float NavmeshlessTargetPositionPenalty { get; set; }    // :31 default 1; BehaviorCharge overrides the getter => 1f
  protected BehaviorComponent(Formation formation)                       // :76
  protected virtual void OnBehaviorActivatedAux()                        // :99 called on activation only if Formation.IsAIControlled
  public virtual void OnBehaviorCanceled(); public virtual void OnLostAIControl(); public virtual void OnAgentRemoved(Agent);
  public virtual void TickOccasionally()                                 // :145
  protected abstract float GetAiWeight();                                // :154  the only abstract member
  public float GetAIWeight() => GetAiWeight() * NavmeshlessTargetPositionPenalty;   // public non-virtual wrapper
  public virtual void ResetBehavior() { WeightFactor = 0f; }
  public virtual TextObject GetBehaviorString()                          // default looks up a game string by type name; override to avoid a missing-string lookup
  protected virtual void CalculateCurrentOrder()                         // :172
  public void PrecalculateMovementOrder() { CalculateCurrentOrder(); CurrentOrder.GetPosition(Formation); }  // called by FormationAI.FindBestBehavior on the candidate
  public virtual void OnDeploymentFinished(); public virtual void OnValidBehaviorSideChanged();
  public override bool Equals(object o) => GetType() == o.GetType();     // one behavior per type per formation
}
```
Vanilla template (BehaviorCharge, v1.4.8):
```csharp
public override float NavmeshlessTargetPositionPenalty => 1f;
protected override void CalculateCurrentOrder() {
  base.CurrentOrder = Formation.CachedClosestEnemyFormation == null
     ? MovementOrder.MovementOrderCharge
     : MovementOrder.MovementOrderChargeToTarget(Formation.CachedClosestEnemyFormation.Formation); }
public override void TickOccasionally() { CalculateCurrentOrder(); Formation.SetMovementOrder(CurrentOrder); ... }
protected override void OnBehaviorActivatedAux() { CalculateCurrentOrder(); Formation.SetMovementOrder(CurrentOrder); ... }
protected override float GetAiWeight() { ... }
```

### RBM custom behavior `[CONFIRMED]` (RealisticBattleAiModule/AiModule/Behaviors/Custom/RBMBehaviorHedgehog.cs, `internal class ... : BehaviorComponent`)
```csharp
public RBMBehaviorHedgehog(Formation formation) : base(formation) {          // :140
    _behaviorSide = formation.AI.Side;
    base.CurrentOrder = MovementOrder.MovementOrderStop;
    CurrentFacingOrder = FacingOrder.FacingOrderLookAtEnemy;
}
public override void TickOccasionally() {                                      // :250
    CalculateCurrentOrder();
    base.Formation.SetMovementOrder(base.CurrentOrder);
    base.Formation.SetFacingOrder(CurrentFacingOrder);
    ApplyShape(); CheckEngage(); }
protected override void OnBehaviorActivatedAux() { ...; Formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill); ... }   // :259
protected override void CalculateCurrentOrder() { ... base.CurrentOrder = MovementOrder.MovementOrderMove(world); }   // :271
public override TextObject GetBehaviorString() => new TextObject("{=RBMHedgehog01}forming a hedgehog");   // :867
protected override float GetAiWeight() => (formation empty) ? 0f : LiveWeight /*20f*/;   // :872
```
The standard loop: the behavior writes `CurrentOrder` and `CurrentFacingOrder` in `CalculateCurrentOrder`, then `TickOccasionally` pushes both via `Formation.SetMovementOrder` and `SetFacingOrder`. Other RBM behaviors (Embolon, ArcherSkirmish, HoldChoke, HoldPosition, ...) are in the same `Behaviors/Custom/` folder.

### Registration is mandatory `[CONFIRMED]`
- `FormationAI.AddAiBehavior(BehaviorComponent)` (FormationAI.cs:133) just appends to `_behaviors`.
- **`SetBehaviorWeight<T>` throws `MBException("Behavior weight could not be set.")` if no registered behavior `is T`** (FormationAI.cs:130). There is no auto-create.
- Vanilla `TeamAIGeneral.OnUnitAddedToFormationForTheFirstTime` registers the vanilla behaviors, guarded by `if (formation.AI.GetBehavior<BehaviorCharge>() == null)`, when a formation goes from 0 to more than 0 units (Formation.cs:2166).
- RBM's approach (Lifecycle.cs:19-50): Harmony postfix on `TeamAIGeneral.OnUnitAddedToFormationForTheFirstTime(Formation)` that calls `formation.AI.AddAiBehavior(new RBMBehaviorX(formation))` for each custom behavior. It guards against duplicates with `GetBehavior<T>() != null` because the hook fires on every 0 to more than 0 transition.
- **Harmony-free alternative `[INFERRED]`:** all 10 `Formation` objects (each with its `AI`) exist from team construction (Team.cs:267-277). So in your MissionLogic's `EarlyStart`, loop `team.FormationsIncludingSpecialAndEmpty` and call `f.AI.AddAiBehavior(new MyBehavior(f))` directly. The `Formation` instance persists for the mission.
- **We may not need custom behaviors at all.** The cheapest design is vanilla behaviors plus direct orders (Q4).

---

## 4. Direct orders

### API names (v1.4.8) `[CONFIRMED]`
```csharp
// Formation.cs
public void SetMovementOrder(MovementOrder input)                 // :701
public void SetFacingOrder(FacingOrder order)                     // :733
public void SetArrangementOrder(ArrangementOrder order)           // :738
public void SetFormOrder(FormOrder order, bool updateDesiredFileCount = true)   // :768
public void SetFiringOrder(FiringOrder order)                     // :792
public void SetRidingOrder(RidingOrder order)
public void SetControlledByAI(bool isControlledByAI, bool enforceNotSplittableByAI = false)   // :804
public void SetPositioning(WorldPosition? position = null, Vec2? direction = null, int? unitSpacing = null)  // :851
public ref readonly MovementOrder GetReadonlyMovementOrderReference()   // :948
public WorldPosition CreateNewOrderWorldPosition(WorldPosition.WorldPositionEnforcedCache c)  // :683
public ArrangementOrder ArrangementOrder { get; }  public FacingOrder FacingOrder { get; }  public FiringOrder FiringOrder { get; }  public FormOrder FormOrder { get; }  // read-only (private setters)
```
There is no `formation.ArrangementOrder = ...` setter. Use `SetArrangementOrder`. `[CONFIRMED]`

**Movement orders (MovementOrder.cs).** `MovementOrderEnum`: Invalid, AttackEntity, Charge, ChargeToTarget, Follow, FollowEntity, Move, Retreat, Stop, Advance, FallBack.
- Static instances: `MovementOrder.MovementOrderCharge`, `MovementOrderRetreat`, `MovementOrderStop`, `MovementOrderAdvance`, `MovementOrderFallBack`, `MovementOrderNull`.
- Factories: `MovementOrder.MovementOrderMove(WorldPosition)` (:412), `MovementOrderChargeToTarget(Formation)` (:397), `MovementOrderFollow(Agent)`, `MovementOrderFollowEntity(GameEntity)`, `MovementOrderAttackEntity(GameEntity, bool surround)`.

**Arrangement orders (ArrangementOrder.cs).** `ArrangementOrderEnum` is Circle, Column, Line, Loose, Scatter, ShieldWall, Skein, Square. Static instances exist for each: `ArrangementOrder.ArrangementOrderCircle/Column/Line/Loose/Scatter/ShieldWall/Skein/Square`.

**Other orders.**
- `FiringOrder.FiringOrderFireAtWill` and `FiringOrder.FiringOrderHoldYourFire`.
- `FacingOrder.FacingOrderLookAtEnemy` and `FacingOrder.FacingOrderLookAtDirection(Vec2)`.
- `FormOrder.FormOrderDeep`, `FormOrderWide`, `FormOrderWider` and `FormOrder.FormOrderCustom(float width)`.
- `RidingOrder.RidingOrderMount` and `RidingOrder.RidingOrderDismount`.

**Player-style orders.** `team.PlayerOrderController.SelectAllFormations()` / `SelectFormation(f)`, then `SetOrder(OrderType)`, `SetOrderWithPosition(OrderType, WorldPosition)`, `SetOrderWithFormation(OrderType, Formation)`, `SetOrderWithTwoPositions(...)` (OrderController.cs:232, 772, 816, 997). `SetOrder` ends by calling `SetControlledByAI(false)` on selected formations that were AI-controlled and have a `PlayerOwner` (OrderController.cs:737-745). `OrderType.AIControlOn/Off` map to `SetControlledByAI(true/false)` (OrderController.cs:339-350). `[CONFIRMED]` For an LLM "general", calling `formation.SetXOrder` directly is simpler. Use the OrderController only if you want vanilla side effects (formation re-layout, order events, UI).

### RTSCamera usage `[CONFIRMED]` (source/RTSCamera.CommandSystem/src/Logic/CommandQueueLogic.cs, ~lines 905-1069)
```csharp
formation.SetMovementOrder(MovementOrder.MovementOrderMove(position));
formation.SetMovementOrder(MovementOrder.MovementOrderCharge);
formation.SetMovementOrder(MovementOrder.MovementOrderChargeToTarget(order.TargetFormation));
formation.SetMovementOrder(MovementOrder.MovementOrderRetreat);
formation.SetArrangementOrder(Utilities.Utility.GetArrangementOrder(...));     // :1351
formation.SetFiringOrder(FiringOrder.FiringOrderFireAtWill);                   // source/RTSCamera/src/Utilities/Utility.cs:119
// re-create a "current" position:
WorldPosition p = formation.CreateNewOrderWorldPosition(WorldPosition.WorldPositionEnforcedCache.None);   // :1197
```

### Constructing a WorldPosition (TaleWorlds.Engine/WorldPosition.cs v1.4.8) `[CONFIRMED]`
```csharp
public WorldPosition(Scene scene, Vec3 position)                                  // :77  -> (scene.Pointer, UIntPtr.Zero, position, hasValidZ:false)
public WorldPosition(Scene scene, UIntPtr navMesh, Vec3 position, bool hasValidZ)   // :82
public void SetVec2(Vec2 v); public void SetVec2MT(Vec2 v);    // move XY, Z marked invalid and re-resolved on use
public Vec2 AsVec2; public bool IsValid; public Vec3 GetGroundVec3(); public float GetNavMeshZ();
public static WorldPosition Invalid
```
- So `new WorldPosition(Mission.Current.Scene, new Vec3(x, y, 0f))` works (the two-argument constructor passes `hasValidZ:false`). The 4-argument form with `UIntPtr.Zero, false` is equivalent.
- A safer pattern, used by RBM (`RealisticBattleAiModule/Utilities/Utilities.Formations.cs:43-52`): copy `formation.CachedMedianPosition` and call `SetVec2(center)` so the navmesh face carries over.
- `[INFERRED]` Because the engine resolves Z and navmesh lazily, build these positions on the main thread.

### Do behavior components override direct orders? `[CONFIRMED]`
- **Yes, if the formation is AI-controlled.** `FormationAI.TickOccasionally` (every 0.5s) calls `ActiveBehavior.TickOccasionally()`, which in all vanilla and RBM behaviors calls `Formation.SetMovementOrder(CurrentOrder)`. It also calls `SetMovementOrder(behavior.CurrentOrder)` whenever the active behavior changes or `IsCurrentOrderChanged` is set. So a direct order on an AI-controlled formation is overwritten within about 0.5s.
- **No, if the formation is not AI-controlled.** `Formation.Tick` skips `AI.Tick()` entirely (0.3), so direct orders persist. `SetMovementOrder` itself has no `IsAIControlled` check; it applies to any formation.
- Mixing: for each formation, either `SetControlledByAI(true)` + weights, or `SetControlledByAI(false)` + direct orders. `SetControlledByAI(true)` has a side effect (Formation.cs:811-823): it forces an immediate AI tick and applies the active behavior's order.
- **Team-level overrides:**
  - `Team.Tick` forces `MovementOrderRetreat` on all formations of `Mission.RetreatSide`.
  - `TeamAIComponent.Tick` resets the Bodyguard formation to `BehaviorCharge` when the general's formation is empty.
  - `OrderController.AddOrderOverride(Func<Formation, MovementOrder, MovementOrder>)` exists (OrderController.cs:1696).

---

## 5. Player team under AI control

### Facts `[CONFIRMED]` from decompiled v1.4.8
- Every team, including the player's, gets `TeamAIGeneral` in `MissionCombatantsLogic.EarlyStart`.
- `Team.AddTeamAI(teamAI, bool forceNotAIControlled = false)` (Team.cs:447) does `item.SetControlledByAI(!forceNotAIControlled && (this != Mission.PlayerTeam || !IsPlayerGeneral))` for every formation. So **the player team's formations start NOT AI-controlled when the player is the general**, and AI-controlled when the player is only a sergeant or non-commander.
- `Team.SetPlayerRole(bool isPlayerGeneral, bool isPlayerSergeant)` (Team.cs:707) sets the flags and runs `item.SetControlledByAI(this != Mission.PlayerTeam || !IsPlayerGeneral)` on all formations. A `Team` starts with `IsPlayerGeneral = true`, `IsPlayerSergeant = false` (Team.cs:201-202).
- **`Team.DelegateCommandToAI()`** (Team.cs:466) is a public method that sets `SetControlledByAI(true)` on every `FormationsIncludingEmpty` formation. This is the cleanest "hand control to the AI" call.
- To hand back: `formation.SetControlledByAI(false)` per formation. `OrderController.SetOrder*` also does it automatically for AI-controlled formations that have a `PlayerOwner`.
- `Mission.Current.PlayerTeam` is a get/set property (`Teams.Player`). `Mission.MainAgent` is the player agent. `Team.PlayerOrderController` is `_orderControllers[1]`, with `.Owner`, `.SelectedFormations` and `.SelectAllFormations()`.
- `Team.HasTeamAi => TeamAI != null`; `Team.AddTeamAI` is public.
- The team's TeamAI ticks only if `HasBots`. With a normal army this is true. `[CONFIRMED]`
- Player-agent factors: `Formation.PlayerOwner` is the agent given control. `Team.GeneralAgent` is settable. `Agent.Controller` is `AgentControllerType.AI / Player / None`, settable.

### RTSCamera `[CONFIRMED]`
- It does **not** turn the player's team into AI control by default. It keeps the player as general with player-controlled formations, and detaches the camera from the agent:
  - `Mission.MainAgent.Controller = AgentControllerType.None / AI / Player` (`source/RTSCamera/src/Logic/SubLogic/ControlTroopLogic.cs` lines ~50-53; config default `PlayerControllerInFreeCamera = AgentControllerType.AI`).
  - Reflection hack in `SwitchFreeCameraLogic.cs:225-245` re-sets `Mission._initialPlayerAgent`, `Utility.SetPlayerAsCommander(true)`, `Mission.MainAgent.SetCanLeadFormationsRemotely(true)` and `Mission.PlayerTeam.GeneralAgent = Mission.MainAgent`, then `team.PlayerOrderController?.SelectAllFormations()`.
- Its per-formation AI toggle is the `OrderType.AIControlOn/Off` path (CommandQueueLogic.cs:1107-1116): `formation.SetControlledByAI(true)` and `(false)`.
- Team swap (cheat mode): `Mission.PlayerTeam = Mission.PlayerEnemyTeam; PlayerOrderController.Owner = targetAgent; formation.PlayerOwner = Mission.MainAgent; formation.SetControlledByAI(isAI, isSplittable)` (`SwitchTeamLogic.cs:45-75`). The code comments reference e1.4.3 and e1.4.7.

### Practical recipe `[INFERRED]`
- To let the LLM be the player team's "general" with vanilla micro AI, do one of:
  1. Keep the player as general. Every few seconds, issue direct orders to the formations (non-AI path, Q4).
  2. After deployment, call `Mission.PlayerTeam.DelegateCommandToAI()`. Our `LlmTactic` then drives it. Or call `PlayerTeam.SetPlayerRole(false, false)` so the player counts as a normal soldier. That also changes UI (order bar) behavior, and RBM's "player-controlled infantry never charges late" comment shows mods already special-case this.
  3. In both cases the player agent can stay alive as a troop; `Team.HasBots` stays true because the rest of the army are bots.
- Hand-back: `foreach (var f in team.FormationsIncludingEmpty) f.SetControlledByAI(false);`.
- `[UNKNOWN]` whether the order UI or UI-side logic misbehaves while formations are AI-controlled but `IsPlayerGeneral` is true. Needs in-game testing.

---

## 6. Reading state for a snapshot

### Iteration `[CONFIRMED]`
```csharp
foreach (Team team in Mission.Current.Teams)                    // Mission.Teams
    foreach (Formation f in team.FormationsIncludingEmpty)      // 8 regular formations; FormationsIncludingSpecialAndEmpty = 10
        if (f.CountOfUnits > 0) { ... }
Formation inf = team.GetFormation(FormationClass.Infantry);     // Team.GetFormation(FormationClass), Team.cs:613
```
`FormationClass`: Infantry=0, Ranged=1, Cavalry=2, HorseArcher=3, Skirmisher=4, HeavyInfantry=5, LightCavalry=6, HeavyCavalry=7, General=8, Bodyguard=9.

**Team flags and queries:** `team.Side` (`BattleSideEnum`), `team.IsPlayerTeam`, `team.IsPlayerAlly`, `team.IsDefender` / `IsAttacker`, `team.IsEnemyOf(Team)`, `team.TeamSide` (`TeamSideEnum`), `team.ActiveAgents` / `TeamAgents`, `team.GeneralAgent` / `Leader`, `team.QuerySystem` (`TeamQuerySystem`).

### Formation position and direction
- `formation.CachedAveragePosition` (Vec2) is preferred; RBM chooses it over the median, which is one soldier (Utilities.Formations.cs:21-35).
- `formation.CachedMedianPosition` (WorldPosition).
- `formation.CurrentPosition` (Vec2, accounts for column).
- `formation.GetAveragePositionOfUnits(bool excludeDetachedUnits, bool excludePlayer)`.
- `formation.OrderPosition` (Vec2) and `OrderPositionIsValid`.
- `formation.Direction` (Vec2, ordered) and `formation.CurrentDirection` (blend with `QuerySystem.EstimatedDirection`).
- `formation.Width` and `formation.CountOfUnits` (includes detached units; `CountOfUnitsWithoutDetachedOnes` excludes them).
- `formation.IsAIControlled` and `formation.IsSplittableByAI`.
- `formation.GetReadonlyMovementOrderReference().OrderEnum`, `formation.ArrangementOrder.OrderEnum`, `formation.FiringOrder`.
- `formation.GetMovementState()` returns `MovementOrder.MovementStateEnum` (Charge, Hold, Retreat, StandGround).

### QuerySystem: no split in 1.4.8 `[CONFIRMED]`
- There is a single class, `FormationQuerySystem` (`formation.QuerySystem`). Each cached value has a normal getter `X` (recomputes if stale) and a `XReadOnly` getter (`GetCachedValueUnlessTooOld()`). Example: `FormationPower` / `FormationPowerReadOnly`.
- I found no `CachedFormationQuerySystem`. `[UNKNOWN]` whether 1.5.x split it, because I did not diff it; RBM at 1.5.7 still uses `f.QuerySystem.IsInfantryFormation` etc.
- Useful properties:
  - Composition: `MainClass`, `IsInfantryFormation`, `IsRangedFormation`, `IsCavalryFormation`, `IsRangedCavalryFormation`, `IsMeleeFormation`.
  - Ratios: `InfantryUnitRatio`, `RangedUnitRatio`, `CavalryUnitRatio`, `HasShieldUnitRatio`.
  - Power: `FormationPower`, `FormationMeleeFightingPower`, `CasualtyRatio`.
  - Ranged state: `MissileRangeAdjusted`, `MaximumMissileRange`, `IsUnderRangedAttack`, `UnderRangedAttackRatio`, `MakingRangedAttackRatio`.
  - Surroundings: `MovementSpeedMaximum`, `ClosestEnemyAgent`, `LocalAllyPower`, `LocalEnemyPower`, `LocalPowerRatio`, `LocalEnemyUnits`, `AverageAllyPosition`, `WeightedAverageEnemyPosition`, `IsUnderCavalryChargeFromFront`.
  - Enemy formations: `ClosestSignificantlyLargeEnemyFormation`, `FastestSignificantlyLargeEnemyFormation`.
- `Formation.CachedClosestEnemyFormation` (a `FormationQuerySystem`) and `Formation.CachedClosestEnemyFormationDistanceSquared` are useful for "distance to the enemy".
- **There is no formation-level `IsInMeleeCombat` property** in `FormationQuerySystem`. Practical proxies: closest-enemy distance, `LocalEnemyPower`, or a per-agent check. RBM implements its own `FormationFightingInMelee(formation, 0.35f)` and `HasBattleBeenJoined(...)` in `Utilities.Formations.cs:218-243` (RBM-only helpers). `[CONFIRMED]` that vanilla lacks the property; the proxies are `[INFERRED]`.
- `TeamQuerySystem` (`team.QuerySystem`): `AveragePosition`, `AverageEnemyPosition`, `MemberCount`, `AllyUnitCount`, `EnemyUnitCount`, `TeamPower`, `RemainingPowerRatio`, `TotalPowerRatio`, `InfantryRatio`, `RangedRatio`, `CavalryRatio`, `LeftFlankEdgePosition`, `RightFlankEdgePosition`, `MedianPosition`, `DeathCount`.
- RBM calls `formation.QuerySystem.Expire()` to force a refresh (Lifecycle.cs:26).

### Agent-level data (morale, health, ammo)
- **Morale:** `AgentComponentExtensions.GetMorale(this Agent agent)` returns `agent.CommonAIComponent?.Morale ?? -1f`. It is an extension in `TaleWorlds.MountAndBlade`, so call `agent.GetMorale()`. `[CONFIRMED]` The scale is probably 0-100, since Agent.cs:5108 uses `GetMorale() * 0.0033f` as a probability. `[INFERRED]` Formation morale = average over `formation.ApplyActionOnEachUnit(a => ...)`.
- **Health:** `agent.Health` and `agent.HealthLimit` (Agent.cs:1477, :889). `[CONFIRMED]`
- **Fleeing:** `agent.IsRunningAway` (Agent.cs:784). `[CONFIRMED]`
- **Ammo:** `agent.IsRangedCached => Equipment.ContainsNonConsumableRangedWeaponWithAmmo()` (Agent.cs:891). `[CONFIRMED]` For counts, iterate `agent.Equipment[EquipmentIndex.Weapon0..3]` and read `MissionWeapon.Ammo` / `ModifiedMaxAmount`. `[INFERRED]`, not read from source.
- **Iterate units:** `formation.ApplyActionOnEachUnit(Action<Agent>)` (Formation.cs:1686), `formation.GetUnitsWithoutDetachedOnes()` (:1182) and `formation.GetCountOfUnitsWithCondition(Func<Agent,bool>)` (:928). `[CONFIRMED]`

---

## 7. Terrain

All `[CONFIRMED]` from TaleWorlds.Engine/Scene.cs and Mission.cs (v1.4.8):
```csharp
float Scene.GetTerrainHeight(Vec2 position, bool checkHoles = true)                         // Scene.cs:1089, cheapest: terrain only, ignores objects
float Scene.GetGroundHeightAtPosition(Vec3 position, BodyFlags excludeFlags = BodyFlags.CommonCollisionExcludeFlags)  // :1130, includes physics objects
void  Scene.GetTerrainHeightAndNormal(Vec2 position, out float height, out Vec3 normal)     // :485
Vec3  Scene.GetNormalAt(Vec2 position)                                                       // :993  (slope)
bool  Scene.GetHeightAtPoint(Vec2 point, BodyFlags excludeBodyFlags, ref float height)       // :988
void  Scene.GetBoundingBox(out Vec3 min, out Vec3 max)                                       // :1554 whole scene
Mission.MBBoundaryCollection Mission.Boundaries   // IDictionary<string, ICollection<Vec2>> of named polygons; .GetBoundaryRadius(name); .GetOrientedBoundariesBox(out Vec2 min, out Vec2 max, float rotRad = 0)  (Mission.cs:26-125, :1131)
bool  Mission.IsPositionInsideBoundaries(Vec2 p)          // :1680
bool  Mission.IsPositionInsideHardBoundaries(Vec2 p)      // :1685
Vec2  Mission.GetClosestBoundaryPosition(Vec2 p)          // :1930
```
- Usage: `Mission.Current.Scene.GetTerrainHeight(new Vec2(x, y))`.
- Mission code itself combines `Scene.GetBoundingBox` with `GetClosestBoundaryPosition` (Mission.cs:2721-2725) to find playable edges.
- `[INFERRED]` Use `Mission.Boundaries.GetOrientedBoundariesBox` for the playable area and `Scene.GetBoundingBox` as a fallback. The scene box can be much larger than the fightable area.
- Threading: RBM warns that native queries are unsafe on worker threads. Sample terrain on the main thread, and cache a coarse height or slope grid for the LLM snapshot. `[INFERRED]`
- RBM has its own terrain feature code (`RealisticBattleAiModule/AiModule/Tactics/TerrainSpots.cs`, `TerrainShape.cs`) as a model for high ground and choke points. I did not read it in depth.

---

## 8. Mission lifecycle: field battles only

### Adding a behavior `[CONFIRMED]`
RBM `RBM/SubModule.cs:339-380`:
```csharp
public override void OnMissionBehaviorInitialize(Mission mission) {
    mission.AddMissionBehavior(new MyLogic());
}
```
`MBSubModuleBase.OnMissionBehaviorInitialize(Mission)` is `public virtual` and `Mission.AddMissionBehavior(MissionBehavior)` is public (Mission.cs:4647). Also available: `OnBeforeMissionBehaviorInitialize(Mission)`.

### Detecting battle type
- `Mission.MissionTeamAIType` (enum `NoTeamAI, FieldBattle, Siege, SallyOut, NavalBattle, NavalRaid`). Convenience properties: `Mission.IsFieldBattle`, `IsSiegeBattle`, `IsSallyOutBattle`, `IsNavalBattle`, `IsNavalRaidBattle` (Mission.cs:1388-1396).
- **Caveat `[CONFIRMED]`:** `MissionTeamAIType` is set by `MissionCombatantsLogic.EarlyStart` (`Mission.Current.MissionTeamAIType = TeamAIType`). It is not yet set inside `OnMissionBehaviorInitialize`, so check `IsFieldBattle` in your logic's `EarlyStart` or `AfterStart`, or later.
- RBM gates on `Mission.Current.IsFieldBattle || IsSiegeBattle || IsNavalBattle || SceneName.Contains("arena") || isHideout`, where `isHideout = Campaign.Current != null && MapEvent.PlayerMapEvent != null && MapEvent.PlayerMapEvent.IsHideoutBattle` (SubModule.cs:181-182).
- RTSCamera's battle check: `mission.Mode == MissionMode.Battle && mission.CombatType == Mission.MissionCombatType.Combat && !IsArenaCombat(mission)`, where arena is detected by the presence of `ArenaAgentStateDeciderLogic` among `mission.MissionLogics` (`source/RTSCamera/src/Utilities/Utility.cs:87-103`).
- `Mission.MissionCombatType` enum: `Combat, ArenaCombat, NoCombat`. `Mission.CombatType` is a property.
- `MissionMode` enum: `StartUp, Conversation, Battle, Duel, Stealth, Barter, Deployment, Tournament, Replay, CutScene, Benchmark`.
- `[UNKNOWN]` the exact `MissionTeamAIType` of hideout and custom-battle missions (hideout is probably NoTeamAI; custom battle probably FieldBattle via `MissionCombatantsLogic`).

### Deployment detection `[CONFIRMED]`
- `Mission.Mode == MissionMode.Deployment` during deployment.
- `Mission.IsDeploymentFinished` is a bool set in `Mission.OnDeploymentFinished()` (Mission.cs:6819-6833).
- `MissionBehavior` hooks (all `public virtual`): `OnDeploymentFinished()` (:200), `OnAfterDeploymentFinished()` (:204), `OnTeamDeployed(Team)` (:208), `OnBattleSideDeployed(BattleSideEnum)` (:212), `OnMissionModeChange(MissionMode old, bool atStart)` (:228), plus `EarlyStart`, `AfterStart`, `OnBehaviorInitialize`, `OnMissionTick(float dt)`.
- `Mission.OnDeploymentFinished` also calls `team.TeamAI.OnDeploymentFinished()`, and `TeamAIGeneral.OnDeploymentFinished` calls `formation.OnDeploymentFinished()` for each formation. That calls `AI.OnDeploymentFinished()` and `OrderController.TryCancelStopOrder(formation)`.
- There is also a C# event, `Mission.DeploymentFinishedEvent`.
- `[INFERRED]` Start LLM orders only after `OnDeploymentFinished`. Before that, formations are placed by the player or the deployment plan and the AI is idle.

---

## 9. Displaying messages `[CONFIRMED]`
```csharp
using TaleWorlds.Library;
InformationManager.DisplayMessage(new InformationMessage("text", Color.FromUint(0xFF00FF00u)));   // InformationManager.cs:61; Color ctor: new Color(r, g, b, a=1f)
```
- Namespace `TaleWorlds.Library` (`InformationManager`, `InformationMessage` and `Color` are all in `TaleWorlds.Library`), unchanged in 1.4.8.
- `InformationMessage` constructors: `(string)`, `(string, Color)`, `(string, Color, string category)`, `(string, string soundEventPath)`.
- RBM uses `InformationManager.DisplayMessage(new InformationMessage("Vroom = " + ..., Color.FromUint(4282569842u)))` (SubModule.cs:192).
- `[INFERRED]` Call it from the main thread only.

---

## 10. SubModule.xml and MCM v5

### Real current examples
**A. MCM's own v5 test module, with Harmony, ButterLib, UIExtenderEx and MCM dependencies.** https://github.com/Aragas/Bannerlord.MBOptionScreen/blob/dev/tests/MCMv5.Tests/_Module/SubModule.xml `[CONFIRMED]`. It uses `$token$` placeholders from the BUTR build tooling.
```xml
<Module xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
        xsi:noNamespaceSchemaLocation="https://raw.githubusercontent.com/BUTR/Bannerlord.XmlSchemas/master/SubModule.xsd">
  <Id value="$moduleid$" /><Name value="$modulename$" /><Version value="v$version$" />
  <DefaultModule value="false" /><ModuleCategory value="Singleplayer" /><ModuleType value="Community" />
  <DependedModules>
    <DependedModule Id="Bannerlord.Harmony" />
    <DependedModule Id="Bannerlord.ButterLib" />
    <DependedModule Id="Bannerlord.UIExtenderEx" />
    <DependedModule Id="Bannerlord.MBOptionScreen" />
    <DependedModule Id="Bannerlord.MBOptionScreen.UI" />
  </DependedModules>
  <DependedModuleMetadatas>
    <DependedModuleMetadata id="Bannerlord.Harmony"        order="LoadBeforeThis" version="v$harmonyversion$" />
    <DependedModuleMetadata id="Bannerlord.ButterLib"      order="LoadBeforeThis" version="v$butterlibversion$" />
    <DependedModuleMetadata id="Bannerlord.UIExtenderEx"   order="LoadBeforeThis" version="v$uiextenderversion$" />
    <DependedModuleMetadata id="Bannerlord.MBOptionScreen" order="LoadBeforeThis" version="v$version$" />
    <DependedModuleMetadata id="Bannerlord.MBOptionScreen.UI" order="LoadBeforeThis" version="v$version$" />
    <DependedModuleMetadata id="Native"      order="LoadAfterThis" version="v$gameversion$.*" />
    <DependedModuleMetadata id="SandBoxCore" order="LoadAfterThis" version="v$gameversion$.*" />
    <DependedModuleMetadata id="Sandbox"     order="LoadAfterThis" version="v$gameversion$.*" />
    <DependedModuleMetadata id="StoryMode"   order="LoadAfterThis" version="v$gameversion$.*" optional="true" />
    <DependedModuleMetadata id="CustomBattle" order="LoadAfterThis" version="v$gameversion$.*" optional="true" />
  </DependedModuleMetadatas>
  <SubModules>
    <SubModule>
      <Name value="MCMv5 Tests" /><DLLName value="MCMv5.Tests.dll" />
      <SubModuleClassType value="MCMv5.Tests.SubModule" /><Tags />
    </SubModule>
  </SubModules>
</Module>
```
Note the odd-looking `order="LoadAfterThis"` for the game modules. That is how this test module is written. Most mods use `LoadBeforeThis` for the game modules, as in example B.

**B. RTSCamera v5.4.19 for game v1.4.8** https://github.com/lzh-mb-mod/RTSCamera/blob/master/source/RTSCamera/Modules/RTSCamera/SubModule.xml `[CONFIRMED]`
```xml
<Module>
  <Name value="RTS Camera"/><Id value="RTSCamera"/><Version value="v5.4.19"/>
  <ModuleCategory value="Singleplayer"/><ModuleType value="Community"/>
  <DependedModules>
    <DependedModule Id="Native" DependentVersion="v1.4.8"/> <DependedModule Id="SandBoxCore" DependentVersion="v1.4.8"/>
    <DependedModule Id="Sandbox" DependentVersion="v1.4.8"/> <DependedModule Id="CustomBattle" DependentVersion="v1.4.8"/>
    <DependedModule Id="StoryMode" DependentVersion="v1.4.8"/>
  </DependedModules>
  <DependedModuleMetadatas>
    <DependedModuleMetadata id="Bannerlord.Harmony" order="LoadBeforeThis" optional="true"/>
    <DependedModuleMetadata id="Native" order="LoadBeforeThis" version="v1.4.*"/>
    <!-- SandBoxCore, Sandbox, StoryMode, CustomBattle likewise, version="v1.4.*" -->
  </DependedModuleMetadatas>
  <SubModules><SubModule>
    <Name value="RTSCameraSubModule"/><DLLName value="RTSCamera.dll"/>
    <SubModuleClassType value="RTSCamera.RTSCameraSubModule"/>
    <Tags><Tag key="DedicatedServerType" value="none"/><Tag key="IsNoRenderModeElement" value="false"/></Tags>
  </SubModule></SubModules>
</Module>
```
**Recommended for us `[INFERRED]`:** combine A's dependency block (with `Bannerlord.MBOptionScreen` only if you want MCM) with B's game-module version pins (`v1.4.*`, game modules ordered `LoadBeforeThis`). Do not hard-require ButterLib or UIExtenderEx unless you use them.

### MCM v5 settings class `[CONFIRMED]` (from tests/MCMv5.Tests/TestSettingsV2.cs and BaseTestGlobalSettings.cs in Aragas/Bannerlord.MBOptionScreen, branch dev)
```csharp
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

internal sealed class LlmSettings : AttributeGlobalSettings<LlmSettings>
{
    public override string Id => "LlmGeneral_v1";                 // unique across mods
    public override string DisplayName => "LLM General";
    public override string FolderName => "LlmGeneral";            // Configs/ModSettings/<FolderName>
    public override string FormatType => "json2";

    [SettingPropertyBool("Enabled", RequireRestart = false, HintText = "Let the LLM command the army.")]
    [SettingPropertyGroup("General")]
    public bool Enabled { get; set; } = true;

    [SettingPropertyFloatingInteger("Decision interval (s)", 2f, 60f, "0.0", RequireRestart = false)]
    [SettingPropertyGroup("General")]
    public float DecisionIntervalSeconds { get; set; } = 10f;

    [SettingPropertyInteger("Max orders per decision", 1, 20, RequireRestart = false)]
    [SettingPropertyGroup("General/Advanced")]                    // '/' nests groups
    public int MaxOrders { get; set; } = 8;

    [SettingPropertyText("Endpoint URL", RequireRestart = false)]
    [SettingPropertyGroup("LLM")]
    public string Endpoint { get; set; } = "http://localhost:8080";
}
// Read: LlmSettings.Instance.Enabled   (see caveat below)
```
- `AttributeGlobalSettings<T> : GlobalSettings<T>` (sealed `DiscoveryType => "attributes"`) is in `MCM.Abstractions.Base.Global`. In the tests, `FolderName` and `FormatType` ("json2") are set in a shared base class, and `Id` / `DisplayName` in the concrete class.
- Attribute signatures seen in the tests and docs: `SettingPropertyBool(string name)`, `SettingPropertyInteger(string, int min, int max, [string format])`, `SettingPropertyFloatingInteger(string, float min, float max, [string format])`, `SettingPropertyText(string)`, `SettingPropertyDropdown(string)` with `Dropdown<string>`, and `SettingPropertyGroup("A/B")`. Named parameters: `RequireRestart`, `HintText`, `Order`.
- `Instance` access: the docs' soft-dependency page uses `CustomSettings.Instance is not null`, so the singleton is named `Instance` and can be null. I did not read the `GlobalSettings<T>` source. `[INFERRED]`
- MCM v5 also supports declaring settings in SubModule.xml (`<Settings>` block, no code reference), documented in `docs/articles/MCMv5/mcmv5-submodule-xml-settings.md`. This is useful if you want zero MCM dependency.
- Soft dependency: ship `MCMv5.dll`, or ILRepack it (docs/articles/MCMv5/mcmv5-soft-dependency.md).

---

## Unanswered or low-confidence items
1. **apidoc.bannerlord.com** pages for `TeamAIComponent` and `FormationAI` returned 404 at the URL pattern I tried. Everything about them comes from the decompiled archive instead.
2. **Formation-level "is in melee"** has no vanilla property (`[CONFIRMED]` absent from `FormationQuerySystem`); proxies are suggested but not tested.
3. **Per-agent ammo count API** (`MissionWeapon.Ammo`, `MissionEquipment`) was not read from source. The `Equipment.ContainsNonConsumableRangedWeaponWithAmmo()` check was.
4. **Hideout and custom-battle `MissionTeamAIType` values** are not verified.
5. **Order-UI behavior** when the player is general and formations are AI-controlled (Q5) is untested.
6. **`GlobalSettings<T>.Instance`** name and nullability: not read from source.
7. **1.4.x vs 1.5.7 differences** beyond the files I compared (Tactic/Behavior/FormationAI/Team/TeamAI) are unchecked. RBM HEAD targets 1.5.7, newer than the 1.4.x target, but the RBM API calls I cite all appear in the v1.4.8 decompile.
8. **Whether the async tick thread is truly separate** from the main mission-behavior thread: the RBM comments and `Mission.TickAgentsAndTeamsImp` structure say so, but I did not read the caller that dispatches it.
