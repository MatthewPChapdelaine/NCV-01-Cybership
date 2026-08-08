# 02 - Prefab Configuration

Field-by-field configuration for each station and system.

---

## Base Station (all five stations)

Common `StationController` fields:

| Field | Meaning |
|---|---|
| `stationName` | Display name (set by subclass `SetupStation()`) |
| `stationId` | Unique ID for debugging |
| `requiredRank` | Minimum rank to operate (0 = anyone) |
| `departmentId` | Owning department (1 Command, 2 Operations, 3 Tactical, 4 Engineering, 5 Science) |
| `shipState` | SHIP_STATE reference |
| `progression` | PLAYER_MANAGER reference |
| `vrStation` | The seat's `VRCStation` |
| `stationUI` | GameObject enabled while operating |
| `lockedUI` | GameObject shown when rank-blocked |
| `stationUIController` | StationUI controller for score/timer/status |

---

## Tactical Station — `TacticalStation`

| Field | Meaning |
|---|---|
| `targetPrefabs` | `GameObject[]` of target prefabs (each with a `TacticalTarget` component + trigger `Collider`) |
| `spawnPoints` | `Transform[]` where targets spawn |
| `crosshair` | Transform that rotates to face the aim hit normal |
| `gameDuration` | Round length in seconds (default 60) |
| `spawnInterval` | Seconds between spawns (default 2) |
| `rayRange` | Aim ray length (default 100) |
| `pointsPerHit` | Points for destroying a target (default 100) |
| `pointsPerMiss` | Penalty for letting a target time out / friendly hit (default -25) |
| `bonusStreak` | Multiplier after a streak of hits (default 5) |

Behavior: `InputUse` fires the weapon at whatever `TacticalTarget` the ray hits. `TacticalTarget` fields: `station` (reference back to the station — set on the prefab or at spawn), `lifetime` (auto-destroy seconds). End of round submits score.

**Prefab note:** target prefabs must contain a `TacticalTarget` script and a trigger `Collider`. Instantiation uses `VRCInstantiate`, so the prefab must be a **scene-referenced prefab** (assigned in the Inspector).

---

## Navigation Station — `NavigationStation`

| Field | Meaning |
|---|---|
| `waypointNodes` | `Transform[]` of selectable path nodes |
| `pathRenderer` | LineRenderer showing the chosen path |
| `activePathMaterial` / `errorPathMaterial` / `defaultPathMaterial` | Path visual states |
| `pathLength` | Nodes required per route (default 5) |
| `timeLimit` | Seconds per route (default 45) |
| `timePenaltyPerError` | Seconds deducted on wrong node (default 5) |
| `rayRange` | Aim ray length (default 10) |

Behavior: aim with the right hand, `InputUse` to "lock" a node selection at a `waypointNodes[i]`. Wrong node → error state + time penalty. Route complete → score.

`SelectNode(int index)` and `SelectNode0..5` are public for button bindings (UdonSharp has no direct `UnityEvent` binding to int params).

---

## Engineering Station — `EngineeringStation`

| Field | Meaning |
|---|---|
| `powerSliderUI` | UI `Slider` driving reactor power (0–100) |
| `coolantSliderUI` | UI `Slider` driving coolant (0–100) |
| `powerSliderTransform` / `coolantSliderTransform` | Optional transform-based sliders (read by `localPosition.x`, travel = `sliderTravel`) |
| `sliderTravel` | Half-travel of the transform sliders in meters |
| `temperatureGauge` / `outputGauge` | `Renderer`s tinted by reactor temp / output |
| `steamEffect` / `warningEffect` | `ParticleSystem`s triggered at temp > 100 / > 140 |
| `reactorGlow` | `Light` scaled with output |
| `targetOutput` | Ideal output % for the stability window (default 75) |
| `targetTemp` | Ideal temperature for the stability window (default 80) |
| `tolerance` | ± window for "in tolerance" (default 10) |

Behavior: effective output = `power * (1 - coolant / 200)`. The seated engineer writes the synced `_desiredOutput` (throttled); the host applies it to the reactor (`ShipStateManager.SetReactorOutput`). `SCRAMReactor()` zeroes output and raises Red alert. **No Inspector wiring needed for the relay** — `_desiredOutput` is private and its `FieldChangeCallback` is code-driven.

---

## Science Station — `ScienceStation`

| Field | Meaning |
|---|---|
| `sampleDisplays` | `Renderer[]` that show the current sample material |
| `sampleMaterials` | Materials index-aligned with `SAMPLE_TYPES` (optional; null-safe) |
| `samplesToAnalyze` | Number of samples per run (default 10) |
| `analysisTime` | Seconds per run (default 60) |
| `SAMPLE_TYPES` | Classification names (Organic, Mineral, Energy, Unknown, Hazardous) |

Behavior: a sample appears on the displays; crew classifies it with `ClassifySample(int)` (wrappers `ClassifyOrganic()..ClassifyHazardous()` for UI buttons). Score = accuracy × 1000 + remaining time × 5.

---

## Communications Station — `CommunicationsStation`

| Field | Meaning |
|---|---|
| `padRenderers` | Pad meshes used in the Simon replay |
| `signalMaterials` | Per-signal material index-aligned with signal id |
| `idlePadMaterial` | Pad material when idle (never null) |
| `padLights` | Optional per-pad lights |
| `signalCount` | Distinct signals (default 4) |
| `baseSequenceLength` / `maxSequenceLength` | Round scaling (3 → 8) |
| `signalHoldTime` / `signalGapTime` / `inputTimeLimit` | Timing |
| `relayDisplayText` / `scoreText` | `TextMesh` readouts |
| `relayChannels` | Quick message presets |

Behavior: `PressPad(int)` (wrappers `PressPad0..3` for UI) replays/inputs signals. The **Inter-Ship Relay** is `[UdonSynced]`; `SendRelayMessage(string)` or `SendRelayChannel(int)` (`SendChannel0..4` wrappers) broadcasts to all players. Non-owner callers briefly take ownership to post, then return it to the host, so **any** crew member can use the board without disrupting an active operator's station writes.

---

## Ship State Manager — `ShipStateManager`

Attach to the `SHIP_STATE` object (UdonBehaviour, `Synchronization = Manual`).

| Field | Meaning |
|---|---|
| `alertAudioSource` | AudioSource playing `alertSounds[level]` |
| `alertSounds` | Clips index-aligned with level: 0 green, 1 yellow, 2 red, 3 black |
| `emergencyLights` | `Light[]` enabled while alert >= Red (2) |
| `alertSurfaces` | `Renderer[]` emissive-tinted by alert color |
| `hudManager` | HUD reference (alert color + notifications) |
| `tempRiseRate` / `tempCoolRate` | Reactor temperature climb/cool per second (default 2 / 1) |
| `reactorCriticalTemp` | Temp that triggers Red alert + `OnReactorCritical` (default 150) |

Behavior: host simulates reactor temperature from synced output. Alert/relay internals are code-driven (`FieldChangeCallback` + named events) — no extra Inspector wiring.

---

## Captain's Chair — `CaptainsChair`

| Field | Meaning |
|---|---|
| `chairStation` | The chair's `VRCStation` |
| `requiredRank` | Rank gate for seating (default 6 = Commander; the instance master always passes) |
| `chairSpotlight` | `Light` enabled while a captain is seated |
| `commandAura` | `ParticleSystem` played while seated |
| `chairEmissive` | `Renderer` that swaps between `activeMaterial` / `inactiveMaterial` |
| `activeMaterial` / `inactiveMaterial` | Chair emissive materials |
| `commandUI` | Command console UI shown while seated |
| `lockedUI` | UI shown to a denied player before ejection |
| `shipState` | SHIP_STATE reference |
| `progression` | PLAYER_MANAGER reference |

Behavior: anyone below `requiredRank` is shown the locked UI and ejected (`chairStation.ExitStation` after 0.5s). While seated, the captain gets `SetAlertGreen/Yellow/Red/Black`, auto-promotion of an XO on leave, and `RelinquishCommand` to stand. Alert buttons call `shipState.SetAlertLevel(...)`, which relays to the host for levels 0–2; **Black is host/captain-only**.

---

## MAGI System — `MAGISystem`

| Field | Meaning |
|---|---|
| `coreRenderers` | `Renderer[]` for the three cores (0 Melchior, 1 Balthasar, 2 Caspar) |
| `coreMaterials` | Core materials: 0 standby, 1 processing, 2 aligned/YES, 3 dissent/NO |
| `coreParticles` | Optional per-core `ParticleSystem`s played when a core decides |
| `decisionText` | `TextMesh` showing the current deliberation / consensus |
| `voteStatusText` | `TextMesh` showing "MAGI: n/3 ALIGNED" |
| `magiVoice` / `deliberationSound` / `consensusSound` | Voice + SFX |
| `shipState` | SHIP_STATE reference |
| `DECISION_TEMPLATES` | Random decision prompts |

Behavior: master-only simulation. `InitiateDecision(string)` starts a 15s vote; cores decide by personality weight. Approved decisions execute (e.g., SCRAM → `SetReactorOutput(0)` on the host).

---

## Emergency Event Manager — `EmergencyEventManager`

| Field | Meaning |
|---|---|
| `minEventInterval` / `maxEventInterval` | Seconds between random events (default 120–300) |
| `eventResponseTime` | Seconds crew have to respond (default 60) |
| `eventNames` | Event display names (index-aligned) |
| `fireEffect` / `steamLeakEffect` / `sparkEffect` | Per-event `ParticleSystem` effects |
| `alarmAudio` / `alarmClip` | Alarm SFX while an event is active |
| `shipState` | SHIP_STATE reference |
| `magiSystem` | MAGI reference (events trigger a MAGI deliberation) |
| `hudManager` | HUD reference |

Behavior: master picks a random event → alert rises to Red (2). `RespondToEvent()` resolves it: the host writes directly, non-host callers relay via `SendCustomNetworkEvent(Master, "OnCrewRespondedRemote")`. Success → +10 reputation; timeout → -15 and alert drops only if nothing else raised it.

---

## Mission Manager — `MissionManager`

| Field | Meaning |
|---|---|
| `missionTypes` | Mission display names (Cargo Transport, etc.) |
| `difficultyLevels` | Difficulty tiers (Routine → Extreme) |
| `shipState` / `progression` / `hudManager` | Core references |
| `stations` | Array of all `StationController`s (used to sum active station score) |
| `baseMissionTime` | Mission length in seconds at Routine (default 300) |
| `missionScoreRate` | Avg station score per second for 100% progress (default 250) |

Behavior: master starts a mission → alert rises with difficulty (1 or 2), timer runs, mission success is judged by the sum of active station scores. Results are broadcast (`SendCustomNetworkEvent(All, "OnMissionSuccess"/"OnMissionFailed")`) so each client awards its own local XP via `PlayerData`.

---

## Watch Schedule Manager — `WatchScheduleManager`

Attach to the `WATCH_MANAGER` object (UdonBehaviour, `Synchronization = Manual`).

| Field | Meaning |
|---|---|
| `WATCH_NAMES` | Watch roster (ALPHA, BRAVO, CHARLIE, DELTA) |
| `watchDuration` | Shift length in seconds (default 3600 = 1h) — values <= 0 are clamped to 1s |
| `progression` | PLAYER_MANAGER reference (XP awards) |
| `watchBonusXP` | XP granted per shift when the local player is on duty (default 25) |

Behavior: watch index = `(serverTime - _watchStartTime) / watchDuration % watchCount`; assignment = `playerId % watchCount`. The host seeds `_watchStartTime` at start.

---

## Ship Designer — `ShipDesignerManager`

Full setup in `06-Ship-Designer.md`.

| Field | Meaning |
|---|---|
| `gridCols` / `gridRows` | Grid dimensions (default 4×4) |
| `maxModulesPlaced` | Cap on placed modules per design (sync-budget guard, default 24) |
| `MODULE_NAMES` | Palette; index-aligned with each cell's module visual children |
| `cellAnchors` | Optional per-cell highlight markers (length = cols×rows) |
| `cellVisualRoots` | Per-cell root GameObjects whose children are the module visuals |
| `designerUI` | Console panel shown while seated |
| `statusText` / `gridText` | `TextMesh` readouts (tool/module/cell/author + mini-map) |
| `rayRange` | Hand-ray paint distance (default 30) |
| `hudManager` | HUD for notifications |

Behavior: sit at the `VRCStation` on the same object → console opens. Pick a palette
module, point your ray at a `ModuleMount` cell, pull Use to paint. `ApplyDesign()`
syncs the layout to everyone (become-owner write, then ownership returns to host);
`SaveDesign()`/`LoadDesign()` persist your personal blueprint via `PlayerData`.

**Mount cells:** each grid cell needs a collider + a `ModuleMount` component
(`cellIndex` = `row * gridCols + col`), with one inactive child visual per module type
in `MODULE_NAMES` order.
