# In-game test plan (first run)

Everything up to this point was built against reference assemblies on a machine without the game. This is the
checklist for the first session on a machine that can run Bannerlord.

## 1. Setup

1. **Game version:** Bannerlord **v1.4.8** (any 1.4.x should load; other versions need a rebuild against the matching
   `Bannerlord.ReferenceAssemblies`).
2. **Required mods** (Nexus or Steam Workshop): Harmony, ButterLib, UIExtenderEx, Mod Configuration Menu v5 (MCM).
3. **Build:** `dotnet build src/BannerlordAI.Module -c Release`. The result is in
   `artifacts/Modules/BannerlordAICommander/`.
4. **Install:** copy that folder into `<Bannerlord>/Modules/`. Enable **AI Commander** in the launcher, below the
   four required mods.
5. **Configure:** in the game, open *Options > Mod Options > AI Commander*:
   - Set the Anthropic API key, or set the `ANTHROPIC_API_KEY` environment variable before launching.
   - Turn on **Write debug log**.
   - Leave the other settings at their defaults for the first runs.

Rough cost is about $0.75 per 10-minute battle with the default models (Opus 5.5 commander, Haiku 5.5 lieutenant).
Lower *Max calls per battle*, or switch the commander to `claude-sonnet-5-5`, to spend less while testing.

## 2. Test sequence

Use **Custom Battle** first: it's the fastest loop, with small armies (~100 a side) on an open field map.

| # | Test | Expected | If not |
|---|------|----------|--------|
| 1 | Launch to the main menu | No launcher error, no crash, AI Commander page appears in Mod Options | Crash at load usually means a DLL version clash: note the assembly name in the crash report or `rgl_log` |
| 2 | Start a battle, finish deployment | "AI commander has taken command..." message | Check the debug log for "Controller created" |
| 3 | Wait ~10-20 s | "Your commander - Commander: <order>" message; formations start moving to their positions | Debug log shows the Commander call; look for errors (key, model id, 400s) |
| 4 | Watch the approach | Infantry holds or advances, archers stay behind, cavalry waits or flanks as ordered; the game AI still handles fighting | Note which action looked wrong: the log shows each plan |
| 5 | Armies within ~300 m | Debug log shows Lieutenant calls getting more frequent as the armies close | |
| 6 | Contingencies | "Contingency C1: ..." messages when conditions trigger (e.g. at first contact) | |
| 7 | Take a formation back | Give any order to one formation in the normal UI; it obeys you; the commander stops ordering it | **Key unknown:** order UI behavior while the player is general but formations are AI-controlled |
| 8 | Fallback | Set an invalid API key and start a battle: "Commander unavailable", then after 3 failures "stood down"; vanilla AI keeps fighting normally | |
| 9 | Enemy control | Turn off *Command my army* and turn on *Command enemy armies*; fight against it | |
| 10 | Both sides | Turn on both; play as a soldier | |
| 11 | Performance | No hitch about once a second in larger battles (300+ a side) | The snapshot reads every agent once a second; can be throttled |
| 12 | Campaign | Field battle in the campaign works; **sieges and hideouts are untouched** (vanilla) | |

## 3. Things that could only be guessed without the game

These are the most likely things to need adjusting. Watch for them, and check the debug log.

- **Threading.** Our tactic runs inside the game's (possibly parallel) team tick; we only read an immutable command
  set there. Any crash with our behaviors in the stack trace points here.
- **Behavior override.** Our custom behaviors (hold-at-point, charge-target, flank) win by being the only weighted
  behavior. Check that formations really stick to them and don't flip-flop with vanilla behaviors.
- **Delay before the plan takes effect.** The team AI re-chooses tactics every 5 s, so up to ~5 s may pass between
  "Commander: ..." and movement.
- **Melee detection.** There is no game flag for "in melee". We use centre distance < (half depths + 12 m). Check
  that "entered melee" and "FIRST CONTACT" events in the log line up with reality.
- **Morale scale.** We assume the game reports morale on a 0-100 scale; the report shows it as a percentage. Check
  the numbers look sane in the log.
- **Ammo.** Read per agent from consumable weapon slots. The report should show ammo going down for archers.
- **Map bounds.** Taken from the mission's boundary box; anchors are clamped to it. Watch for formations walking to
  map edges.
- **Coordinates.** World X/Y are assumed to be the ground plane; if anchors land mirrored or rotated, the frame
  maths needs a sign fix.
- **DLL clashes.** We ship System.Text.Json, System.Memory etc. for the Anthropic SDK, with a fallback resolver.
  Clashes with other mods would show at load or on the first API call.

## 4. Bring back

- `Documents/Mount and Blade II Bannerlord/Logs/AICommander/*.log` (prompts, replies, timings, tokens, warnings).
- Any crash report or `rgl_log_*.txt` from the game's logs folder.
- Short notes, or screenshots or video, of anything that looked wrong, with the battle time.
