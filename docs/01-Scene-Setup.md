# 01 - Scene Setup

How to assemble the NCV-01 Cybership world in the Unity Editor.

## Prerequisites

- Unity **2022.3.6f1** (see `ProjectSettings/ProjectVersion.txt`)
- VRChat Creator Companion with the **VRCSDK Worlds** + **UdonSharp** packages pinned in `Packages/manifest.json`
  - `com.vrchat.base` / `com.vrchat.worlds` `3.10.4`
  - `com.vrchat.udonsharp` `1.1.9`
- A `VRCSceneDescriptor` configured in the scene (Spawn Location, Respawn Height, etc.)

## Recommended GameObject Hierarchy

```
NCV-01 (Scene Root)
├── _SYSTEMS
│   ├── SHIP_STATE            (ShipStateManager)
│   ├── PLAYER_MANAGER        (PlayerProgressionManager)
│   ├── WATCH_MANAGER         (WatchScheduleManager)
│   ├── MAGI                  (MAGISystem)
│   ├── EMERGENCY_MANAGER     (EmergencyEventManager)
│   ├── MISSION_MANAGER       (MissionManager)
│   ├── SHIP_DESIGNER         (VRCStation + ShipDesignerManager)
│   └── HUD                   (HUDManager)
├── _STATIONS
│   ├── STATION_TACTICAL      (VRCStation + TacticalStation)
│   ├── STATION_NAVIGATION    (VRCStation + NavigationStation)
│   ├── STATION_ENGINEERING   (VRCStation + EngineeringStation)
│   ├── STATION_SCIENCE       (VRCStation + ScienceStation)
│   ├── STATION_COMMS         (VRCStation + CommunicationsStation)
│   └── CAPTAIN_CHAIR         (VRCStation + CaptainsChair)
└── _SHIP (meshes, hull lights, effects)
    └── _MOUNTS (ship designer grid cells: CELL_00.. with ModuleMount + colliders)
```

## Wiring Order

Wiring these in this order avoids null-reference confusion:

1. **SHIP_STATE** → assign `hudManager`, `alertAudioSource` + `alertSounds[]`, `emergencyLights[]`, `alertSurfaces[]`; tune reactor (`tempRiseRate`, `tempCoolRate`, `reactorCriticalTemp`).
2. **PLAYER_MANAGER** → assign `uiManager` (HUD).
3. **HUD** → assign `progression`, `shipState`, `watchSchedule`, `emergencyManager`, `missionManager`, plus all `Text`/`Image` UI fields.
4. **WATCH_MANAGER** → assign `progression`; fill `WATCH_NAMES`, `watchDuration`, `watchBonusXP`.
5. **Each station** → assign its `StationController` fields:
   - `shipState`, `progression`, `vrStation`, `stationUI`, `lockedUI`, `stationUIController`
   - then the station-specific fields (see `02-Prefab-Configuration.md`).
6. **MISSION_MANAGER** → assign `shipState`, `progression`, `hudManager`, and the `stations[]` array (all five `StationController`s).
7. **EMERGENCY_MANAGER** → assign `shipState`, `magiSystem`, `hudManager`; fill `eventNames`, `minEventInterval`/`maxEventInterval`, `eventResponseTime`, and the per-event effects/SFX.
8. **MAGI** → assign `shipState`; wire `coreRenderers`/`coreMaterials`/`coreParticles`, `decisionText`/`voteStatusText`, `magiVoice` + sounds, `DECISION_TEMPLATES`.
9. **SHIP_DESIGNER** → assign `cellAnchors`/`cellVisualRoots` (grid cells), `designerUI`,
   `statusText`, `gridText`, `hudManager`; wire the console buttons per `06-Ship-Designer.md`.

## Required Scene Components (per system)

| GameObject | Component | Purpose |
|---|---|---|
| SHIP_STATE | ShipStateManager | Synced alert level, reactor output/temp, reputation |
| PLAYER_MANAGER | PlayerProgressionManager | Local PlayerData rank/XP/department |
| WATCH_MANAGER | WatchScheduleManager | Watch rotation over server time |
| MAGI | MAGISystem | AI decision ticks (master), reputation changes |
| EMERGENCY_MANAGER | EmergencyEventManager | Synced alert events + reputation |
| MISSION_MANAGER | MissionManager | Master-driven mission lifecycle |
| SHIP_DESIGNER | VRCStation + ShipDesignerManager | Palette grid editor + synced ship design |
| HUD | HUDManager | Worldspace crew display |

## VRCStation Setup (each station)

- Add a **VRCStation** component to each station seat object.
- Assign the matching `VRCStation` reference on the station's `StationController`.
- **Station (sitting) mobility**: leave default so players can sit; the `CaptainsChair` additionally kicks denied players (rank < 6) back out.

## Sync Modes

Every UdonSharpBehaviour that uses `[UdonSynced]` must have its UdonBehaviour **Sync Mode** set in the Inspector:

| Behaviour | Recommended Sync Mode |
|---|---|
| ShipStateManager | `Continuous` |
| StationController + subclasses | `Manual` |
| WatchScheduleManager | `Continuous` |
| MAGISystem | `Manual` |
| EmergencyEventManager | `Manual` |
| MissionManager | `Continuous` |
| ShipDesignerManager | `Manual` |

UdonSharp auto-selects a mode if you set `[UdonBehaviourSyncMode]`, but pinning it in the Inspector is the safest path.

## First-Build Checklist

- [ ] `VRCSceneDescriptor` present, spawn position set
- [ ] All `[UdonSynced]` behaviours have a valid Sync Mode
- [ ] All public references assigned (no `Missing` entries in the Inspector)
- [ ] Udon Program compiles in the Editor (`UdonSharp` → `Compile All Udon Programs` / check Console)
- [ ] World is under the poly/performance limits (see `04-Optimization.md`)
