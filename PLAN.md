# Bannerlord AI Commander — Implementation Plan

An LLM (via the player's own API key) acts as the general for one or more teams in a battle. It makes the big calls
(where formations go, what they do, when to commit), and Bannerlord's own formation and soldier AI does the micro.

## Decisions so far (2026-11-15)

| Topic | Decision |
|---|---|
| Who the AI commands | Configurable per team: player's team, enemy, allies, any mix (AI-vs-AI is useful for testing) |
| Battle types | Milestone 1: field battles. Milestone 2: sieges, the headline feature (the plan format supports sieges from day one). Naval later. |
| Providers | Anthropic (official C# SDK) + generic OpenAI-compatible endpoint (OpenAI, OpenRouter, Ollama, LM Studio) |
| Model tiers | Commander (large) + Lieutenant (small) + deterministic rules |
| Settings | MCM in-game menu (Harmony, ButterLib, UIExtenderEx, MCM dependencies) |
| Game version | Latest stable, v1.4.8 (`Bannerlord.ReferenceAssemblies` 1.4.8.x) |
| Player-facing extras (v1) | On-screen order log only |
| Dev constraint | The dev machine cannot run Bannerlord. Build against reference assemblies; test in-game later. |

---

## 1. How it fits into the game AI

Bannerlord battle AI already has three layers. We replace only the top one.

```
Team AI (TeamAIGeneral)      picks a TacticComponent every few seconds     <-- WE REPLACE THIS
  TacticComponent            sets per-formation behavior weights           <-- our TacticLlmDirected
    FormationAI              runs the highest-weighted BehaviorComponent   <-- vanilla + a few custom
      Agent AI               individual fighting, pathing, shields         <-- untouched (the micro)
```

- For each team under LLM control, we call `TeamAIComponent.ClearTacticOptions()` and then
  `AddTacticOption(new TacticLlmDirected(team, brain))`, so the team AI always picks our tactic.
- `TacticLlmDirected` reads the current plan and turns each formation's directive into
  `formation.AI.ResetBehaviorWeights()` + `SetBehaviorWeight<T>(w)`. Where needed it also sets
  `ArrangementOrder`, `FiringOrder`, `FormOrder` and `SetMovementOrder(...)`.
- When a directive needs an explicit position or target that no vanilla behavior accepts, we use small custom
  `BehaviorComponent` subclasses (`BehaviorLlmHoldAt`, `BehaviorLlmAttackFormation`, `BehaviorLlmMoveTo`).
- If the LLM fails or is turned off, we restore the vanilla tactic options, so the game falls back to stock AI.
- Reference implementations to study before writing the adapter:
  - **Realistic Battle Mod**, AI module (github.com/Fellow93/RealisticBattleProject): how it patches tactics and behaviors.
  - **RTS Camera / Command System**: how it issues orders from code, and how it gives AI control of the player's team.

### Directive vocabulary to game behavior (initial mapping, to verify in-game)

| Directive | Vanilla behavior(s) | Custom fallback |
|---|---|---|
| `hold` (at anchor) | `BehaviorDefend`, `BehaviorHoldHighGround` | `BehaviorLlmHoldAt` |
| `advance` | `BehaviorAdvance` | `BehaviorLlmMoveTo` |
| `charge` (target) | `BehaviorCharge`, `BehaviorTacticalCharge` | `BehaviorLlmAttackFormation` |
| `skirmish` | `BehaviorSkirmish`, `BehaviorScreenedSkirmish`, `BehaviorHorseArcherSkirmish` | — |
| `flank` (side, target) | `BehaviorFlank` | `BehaviorLlmMoveTo` + charge |
| `protect_flank` | `BehaviorProtectFlank` | — |
| `screen` | `BehaviorCavalryScreen` | — |
| `reserve` | `BehaviorReserve` | `BehaviorLlmHoldAt` |
| `retreat` | `BehaviorRetreat`, `BehaviorPullBack` | — |

Modifiers on any directive: `arrangement` (line / shieldwall / loose / square / circle / column / scatter) and
`fire` (at_will / hold).

---

## 2. Solution layout

```
Bannerlord_AI/
  src/
    BannerlordAI.Core/        netstandard2.0, no game references; all logic that can be tested here
      Model/                  BattleSnapshot, FormationState, TerrainSummary, Plan, Directive, Contingency, Condition
      Compaction/             SnapshotFormatter (compact text table), EventLog (deltas since last plan)
      Llm/                    ILlmClient, AnthropicLlmClient (official SDK), OpenAiCompatLlmClient (HTTP), schemas
      Orchestration/          BattleBrain, CadenceScheduler, Commander, Lieutenant, ConditionEvaluator, Budget
      Planning/               PlanValidator, DirectiveResolver (plan + snapshot -> FormationCommand[])
    BannerlordAI.Module/      net472; refs Bannerlord.ReferenceAssemblies 1.4.8, Harmony, MCM
      SubModule.cs, LlmBattleMissionBehavior, TacticLlmDirected, Behaviors/*,
      SnapshotBuilder (Mission -> BattleSnapshot), CommandApplier (FormationCommand -> game calls),
      McmSettings, OrderLog (InformationManager.DisplayMessage)
    Module/                   SubModule.xml, packaging layout for Modules/BannerlordAICommander/
  tests/
    BannerlordAI.Core.Tests/  net10.0 unit tests
    BannerlordAI.Sim/         toy 2D battle simulator implementing the same snapshot/command interfaces
```

The Core sees only plain data. The Module is a thin translator, which keeps most of the code testable without the game.

---

## 3. What the models see (compaction is done in code)

Built each tick on the main thread as an immutable snapshot, then rendered to a compact text block.

- **Frame:** our army's center is the origin, +y points toward the enemy center, units are metres. Bearings are given
  as left / center / right and ahead / behind, so the model never handles world coordinates.
- **Formations, both sides:** id, class, count/initial, average health, morale, average position, facing, speed,
  current behavior/order, engaged-with, ranged ammo %.
- **Terrain:** a coarse height grid (for example 7x7 over the battle area), with high ground and slopes flagged, plus
  obstacles where they can be sampled. Needs in-game verification of which scene queries are cheap.
- **Event log:** deterministic deltas since the last Commander call, e.g. "E3 cavalry moved 120 m toward our left
  flank", "F2 archers lost 30%", "first contact at 00:47".
- **Siege-ready fields (filled in milestone 2):** lanes (gate, ladders, towers), wall sectors, engine states, breaches.

---

## 4. The three tiers

### Commander: large model (default `claude-opus-5-5`, configurable)
- **Called** at deployment, on its interval (default 30 s), when the Lieutenant escalates, and on major events
  (first contact, a formation routs or drops below 50%, the enemy changes overall posture). There is a minimum
  cooldown between calls (default 10 s).
- **Outputs** a `Plan` as JSON (structured outputs, `output_config.format`):
  ```json
  {
    "intent": "Hold the ridge; archers bleed their infantry; cavalry waits, then hits their archers once lines meet.",
    "stance": "defensive",
    "directives": [
      {"formation": "F1", "do": "hold", "anchor": {"x": 0, "y": 40}, "arrangement": "shieldwall"},
      {"formation": "F2", "do": "skirmish", "anchor": {"behind": "F1", "dist": 25}, "fire": "at_will"},
      {"formation": "F3", "do": "reserve", "anchor": {"x": -120, "y": 0}}
    ],
    "contingencies": [
      {"id": "C1",
       "when": {"all": [{"engaged": "F1"}, {"enemy_class_strength_below": {"class": "cavalry", "pct": 60}}]},
       "then": [{"formation": "F3", "do": "charge", "target": "enemy:ranged"}]},
      {"id": "C2",
       "when": {"enemy_within": {"class": "cavalry", "of": "F2", "dist": 60}},
       "then": [{"formation": "F2", "do": "retreat", "anchor": {"behind": "F1", "dist": 40}}]}
    ],
    "wake_me_if": ["F1 below 50%", "enemy flanks beyond our left"]
  }
  ```
- Conditions use a small fixed vocabulary (distance, strength, morale, engaged, elapsed time, class strength) so
  code can evaluate them for free. Anything fuzzy goes in `wake_me_if`, which the Lieutenant judges.

### Lieutenant: small model (default `claude-haiku-5-5`, configurable)
- **Called** on the proximity cadence (below). It is skipped if the Commander replied within the last few seconds or
  if the snapshot hasn't changed meaningfully.
- **Can:** fire a contingency, adjust a directive's target or anchor within limits, change arrangement or fire
  orders, and escalate (`wake_commander: true` + reason).
- **Cannot:** rewrite the intent or invent new directives for formations. It works within the plan.

### Rules: code, every ~1 s, free
- Evaluate contingency conditions, detect events, compute the cadence, enforce the budget, and validate and clamp
  every model output (unknown formation ids, anchors off the map) before it reaches the game.

### Proximity cadence (applies to the Lieutenant; the Commander keeps its own interval)
`d` = minimum distance between any of our formations and any enemy formation.
- `d >= 300 m`: interval 30 s (the Lieutenant is effectively idle; the Commander's 30 s covers it)
- `d <= 80 m`: interval 5 s
- in between: linear interpolation. All thresholds are configurable in MCM.

---

## 5. Runtime and threading

- All game objects are touched only on the main thread (inside `MissionBehavior.OnMissionTick`).
- The snapshot (plain data) goes to a background `Task` for the model call. Results come back through a
  `ConcurrentQueue` that the next tick drains.
- At most one call is in flight per tier per team. Stale results are discarded: each response carries the snapshot
  sequence number it was based on.
- **Latency is expected:** the game keeps running during a call, so the plan's contingencies are what keep the army
  reactive between calls.
- **Failures:** timeouts and retries come from the SDK, and the last good plan stays active. After N consecutive
  failures, or once the per-battle budget is spent, the team reverts to vanilla tactics with an on-screen notice.
- **Budget:** per-battle caps on calls and tokens, set in MCM. Running totals are shown in the order log at
  battle end.

---

## 6. LLM layer

- `ILlmClient.CompleteJsonAsync(systemPrompt, userContent, jsonSchema, model, effort, ct)`
- **Anthropic:** the official `Anthropic` NuGet package (v13.8.0, which ships a netstandard2.0 build).
  - Structured outputs via `OutputConfig.Format`. Forced `tool_choice` returns a 400 on Opus 5.5, so it isn't an option.
  - Thinking can't be disabled on Opus 5.5, so `OutputConfig.Effort` (low or medium) controls latency.
  - Prompt caching on the static system prompt (rules, directive vocabulary, unit glossary). The volatile battle
    state goes after the cache breakpoint.
  - Server-side refusal fallback enabled per the SDK docs.
- **OpenAI-compatible:** plain `HttpClient` POST to `{baseUrl}/chat/completions` with `response_format` set to a
  JSON schema, falling back to JSON mode for local models that lack schema support. TLS 1.2 is forced on net472.
- **Rough cost** (estimate only, to measure later): a 10-minute battle with ~20 Commander calls (Opus 5.5, ~4k in /
  ~1k out) and ~60 Lieutenant calls (Haiku 5.5) comes to about **$0.75 per battle**, almost all of it Commander.
  Using Sonnet 5.5 as Commander roughly halves that.

---

## 7. Settings (MCM)

- Per-team control: player team / allies / enemies (on/off each).
- Commander: provider, model, effort, interval, cooldown. Lieutenant: provider, model, enable, near/far distances
  and intervals.
- API keys: entered in MCM, or read from an environment variable instead. **MCM stores settings in plain text
  under Documents\Mount and Blade II Bannerlord\Configs; the menu will warn about this.**
- Budget caps, failure threshold, order-log verbosity (off / orders / orders + reasoning).

---

## 8. Testing without the game

1. **Unit tests (Core):** cadence maths, condition evaluation, plan validation and clamping, snapshot formatting,
   JSON schema round-trips, failure and fallback state machine.
2. **Simulator (`BannerlordAI.Sim`):** a crude 2D battle (formations move, shoot, melee, rout) driving the real
   `BattleBrain`. It checks that the loop, threading, cadence and contingencies behave sensibly end to end.
3. **Recorded responses:** canned Commander/Lieutenant JSON (including malformed output) for deterministic runs.
4. **Live prompt tuning (optional, costs API credits; needs the user's go-ahead):** the sim against real models,
   to tune prompts and measure latency and tokens.
5. **Compile-time check of the adapter** against the v1.4.8 reference assemblies. Behavior is confirmed only in-game.

---

## 9. Milestones

- **M0, scaffold:** solution, projects, NuGet refs, SubModule.xml, packaging script.
- **M1, Core:** models, formatter, conditions, cadence, validator, LLM clients, BattleBrain; unit tests.
- **M2, Sim harness:** toy battle and recorded-response runs; optional live tuning.
- **M3, Field-battle adapter:** MissionBehavior, TacticLlmDirected, custom behaviors, SnapshotBuilder,
  CommandApplier, MCM settings, order log. Compiles against 1.4.8.
- **M4, first in-game test (on the gaming machine):** checklist in section 10.
- **M5, sieges:** siege snapshot fields; lane, sector and reserve directives; adapter for `TeamAISiegeAttacker` /
  `TeamAISiegeDefender` / sally-out.
- **Later:** naval (War Sails), talking to the AI general, optional slow motion while the Commander thinks,
  debug overlay.

## 10. Must be confirmed in-game (cannot be verified from reference assemblies)

- [ ] The Anthropic SDK's dependencies (System.Text.Json, etc.) load in Bannerlord's process without clashing
      with other mods. If they clash: merge them into our DLL (ILRepack), or fall back to a raw-HTTP Anthropic
      client behind the same `ILlmClient` interface.
- [ ] Replacing tactic options on `TeamAIGeneral` sticks, and vanilla re-evaluation doesn't fight it.
- [ ] Player team: when the player is general, the team can be put under AI control
      (`SetControlledByAI`, plus whatever RTS Camera does), and handed back.
- [ ] Behavior weights vs explicit `SetMovementOrder`: which one wins, and whether custom behaviors are needed.
- [ ] Cost of terrain height sampling on the main thread.
- [ ] Order-log readability during a real battle.
