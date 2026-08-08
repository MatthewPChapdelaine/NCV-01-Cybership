# 05 - Testing & Polish

How to test the world and what to verify before shipping.

## Single-player (editor / private instance)

1. Enter Play mode. Verify:
   - HUD shows rank/XP/department after `OnPlayerRestored`.
   - Station UI opens when seated, closes on exit.
   - Rank-gated stations show the locked UI for low-rank test player.
2. Sit at **Captain's Chair** → verify alert controls (Green/Yellow/Red/Black) and the locked/eject flow for low-rank players.
3. Each station minigame:
   - **Tactical**: spawn/destroy targets, score submit, friendly-fire penalty.
   - **Navigation**: node selection, error path, time penalty.
   - **Engineering**: sliders move reactor output, heat rises, alarm sounds.
   - **Science**: classify samples, correct/incorrect feedback.
   - **Communications**: Simon replay + input, relay message broadcast.
4. Trigger an **emergency** (host) → alert Red, HUD alert color, resolve event.
5. Start a **mission** → timer, station-score collection, results broadcast.

## Multi-player (instance with a test alt / friends)

- **Host + guest**: guest sits at a station → `_currentOperatorId` syncs, guest's score shows on host.
- **Non-host alert buttons**: a guest at Tactical / Captain's Chair raises/lowers alert → the **host** applies it and all clients see the HUD/light change (relay via `OnAlertLevelRemote0/1/2`).
- **Non-host engineer**: a guest adjusts the reactor sliders → host's reactor output/temperature follow (relay via the station's synced `_desiredOutput`).
- **Host leaves**: new host takes over master-only systems (mission/event/MAGI). Verify no double-start (host-guards prevent it).
- **Persistence**: award XP, disconnect, rejoin → rank/XP retained via PlayerData.
- **Relay board**: one player sends a message → all players see it (posting while a game is running must not break the operator's score writes).

## Common issues

| Symptom | Likely cause |
|---|---|
| `OnStationEntered` not firing | Seat isn't a `VRCStation`; or station behaviour's Sync Mode unset |
| Synced values never update | `RequestSerialization()` missing, or behaviour Sync Mode = `None` |
| XP not saving | `PlayerData` calls before `OnPlayerRestored` |
| Input events not firing | Method signature isn't `(bool, VRC.Udon.Common.UdonInputEventArgs)` |
| `VRCInstantiate` no-op | Prefab not assigned, or not a scene-referenced prefab |
| Buttons do nothing | UdonSharp has no `UnityEvent`; buttons must call a **public** `SendCustomEvent` method |
| Compile error `CS0115` | Overrode an event UdonSharpBehaviour doesn't declare (e.g. `OnPlayerRestored`) — use a plain public method |

## Before publishing

- [ ] Run **VRChat Creator Companion** → check the build succeeds
- [ ] Udon Program compile passes with 0 errors
- [ ] Performance window within limits (see `04-Optimization.md`)
- [ ] All references assigned (no `Missing` in Inspector)
- [ ] Spawn point / respawn height sensible
- [ ] Test 2-player session (host + guest) end-to-end

## Ideas for polish (nice-to-have)

- **Audio**: add ambience loop, station click SFX, alert klaxon, MAGI idle hum.
- **Animations**: opening/closing viewport shutters, reactor piston loop.
- **VFX**: warp-stretch on long jumps, thruster plumes when alert rises.
- **Intro screen**: "Welcome aboard NCV-01" panel before the first watch starts.
