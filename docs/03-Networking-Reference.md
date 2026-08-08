# 03 - Networking Reference

How synchronization, ownership, and events work across the world.

## Data that is `[UdonSynced]`

| Behaviour | Synced field | Who writes |
|---|---|---|
| ShipStateManager | `_alertLevel` (int) | Host; non-host requests relayed as named events (levels 0–2). Condition Black = host/captain only |
| ShipStateManager | `_reactorOutput` (float) | Host only; the seated Engineer relays desired output via the station's own synced `_desiredOutput` |
| ShipStateManager | `_credits`, `_reputation` (int) | Host only |
| StationController | `_currentOperatorId` (int) | Seated operator (become-owner on sit) |
| StationController | `_isActive` (bool) | Seated operator |
| StationController | `_currentScore` (int) | Seated operator |
| CommunicationsStation | `_relaySender`, `_relayMessage` | Any crew member (become-owner, returned to host) |
| EngineeringStation | `_desiredOutput` (float) | Seated engineer |
| ShipDesignerManager | `_shipDesignData`, `_designAuthor` | Whoever applied last (become-owner), then returned to host |
| WatchScheduleManager | `_watchStartTime` (float) | Host |
| MAGISystem | `_activeCores`, `_magiReputation` | Host |
| EmergencyEventManager | `_activeEventIndex`, `_crewResolved`, `_resolvedByMaster` | Host |
| MissionManager | `_missionActive`, `_missionIndex`, `_difficulty` | Host |

## Who is authoritative?

- **Alerts** — the host owns the alert field. Any player may raise/lower levels 0–2; non-host callers forward to the host via `SendCustomNetworkEvent` (`OnAlertLevelRemote0/1/2`), so station controls work for everyone. Level 3 (Black) requires **host or seated captain** and is not relayed.
- **Reactor output** — host-owned and host-written. The seated engineer writes a `_desiredOutput` field on their **own station object** (which they own); a `FieldChangeCallback` on the host applies it to the reactor. This avoids last-writer-wins races on a shared object.
- **Scores / station state** — the **seated operator owns the station object** and writes operator slot, active flag, and score. Rank-gating is client-authoritative (the local client ejects itself if it lacks rank), so a locked player can never claim a slot.
- **Missions / events / MAGI** — host-only decisions, broadcast to everyone via `SendCustomNetworkEvent`.

## Event Flow: Mission Results

1. Host's `MissionManager` ends a mission.
2. `SendCustomNetworkEvent(NetworkEventTarget.All, "OnMissionSuccess")` (or `"OnMissionFailed"`).
3. Every client runs the matching handler, calls its **own** `PlayerProgressionManager.AwardMissionXP(...)`.
4. XP/rank/missions write to that player's `PlayerData` (persisted per-player, world-scoped).

This avoids the host having to manipulate other players' PlayerData (which is not allowed — PlayerData is per-player and world-scoped). `AwardMissionXP` is guarded by `_dataReady` so an early event can never zero stored data.

## Event Flow: Station Occupation

1. Player sits → `OnStationEntered(VRCPlayerApi)` fires on the seat's behaviour.
2. If the **local** player lacks the required rank, they show the locked UI and are **ejected** (`vrStation.ExitStation`) — rank-gating is client-authoritative.
3. The seated operator takes ownership (`Networking.SetOwner`) — the claim lands immediately if already owner, otherwise in `OnOwnershipTransferred`. Then it writes `_currentOperatorId` + `_isActive = true` → `RequestSerialization()`.
4. Leaving triggers `OnStationExited` — the operator clears the slot, resets the score, and returns ownership to the host so the seat stays neutral.

> `OnStationEntered()` / `OnStationExited()` **parameterless** overloads are `[Obsolete(error: true)]` — do **not** implement those. Use the `(VRCPlayerApi player)` overloads.

## Event Flow: Ship Designer Apply (become-owner)

`[UdonSynced]` variables can only be written by the **owner** of the object — a
non-owner's write + `RequestSerialization()` is silently dropped. So the Ship
Designer's Apply uses the official "become-owner" pattern so **any** player can push
a design:

1. Player presses Apply → `Networking.SetOwner(Networking.LocalPlayer, gameObject)`.
2. When the transfer completes (`OnOwnershipTransferred`), write `_shipDesignData` +
   `_designAuthor` and `RequestSerialization()`.
3. After a 1s flush delay, ownership returns to the instance master
   (`HandBackOwnership`) so the world stays host-controlled.

## Event Flow: Alerts

- `SetAlertLevel(int)` on the **host** writes the synced field + `RequestSerialization()`. On a **non-host** client it forwards levels 0–2 to the host as a named event (`OnAlertLevelRemote0/1/2`) because non-owners cannot write synced fields; level 3 is host/captain-only and never forwarded.
- `AlertLevel` is a `FieldChangeCallback` property that fires on **every client** when the value changes → `ApplyAlertLevel` drives lights, HUD color, and sound.

## Event Flow: Reactor Output (relay)

1. The seated engineer owns the **EngineeringStation object** and writes its synced `_desiredOutput` (throttled) as they adjust the sliders.
2. On every other client a `FieldChangeCallback` (`OnDesiredOutputChanged`) fires; the **host's** copy calls `ShipStateManager.SetReactorOutput(_desiredOutput)`.
3. The host's temperature simulation reads the synced `_reactorOutput` as before.

## PlayerData (persistence)

- Only safe after `OnPlayerRestored(VRCPlayerApi player)` fires for the **local** player (NOT declared on `UdonSharpBehaviour`, so it is a plain public method, not an override).
- `PlayerProgressionManager` reads keys on restore, writes on every award.
- Department/rank read is client-authoritative (each client trusts its own persisted data for rank-gating decisions).

## Ownership notes

- All networked decisions that must be single-source are host-guarded (`Networking.IsMaster`).
- `SendCustomNetworkEvent` strings must match a **public method name** on the target behaviour exactly.
- VRChat input events (`InputUse` etc.) arrive with signature `(bool value, VRC.Udon.Common.UdonInputEventArgs args)`.
