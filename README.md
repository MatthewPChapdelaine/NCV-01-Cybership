# NCV-01 Cybership

A VRChat world project: the **NCV-01 Cybership** — a synced, seat-based starship with crew ranks, XP persistence, station minigames, and a crew-driven mission loop.

Built with **Unity 2022.3.6f1**, **VRCSDK Worlds 3.10.4**, and **UdonSharp 1.1.9**.

## Features

- **5 operating stations** (Tactical, Navigation, Engineering, Science, Communications) + a rank-gated **Captain's Chair**.
- **Rank & XP system** persisted via VRChat PlayerData (`Recruit` → `Captain`), with department assignments.
- **Synced ship state**: alert levels (green→yellow→red→black), reactor output, credits, reputation.
- **MAGI** ship AI that reacts to crew performance and damages cores when the crew slacks off.
- **Ship Designer**: a level-designer-style console where players paint modules onto the ship's mount grid, save blueprints to PlayerData, and apply the layout for the whole crew to see.
- **Emergency events** that raise alerts and require crew response.
- **Missions** that judge success from combined station scores and broadcast results.
- **Watch schedule** rotation using server time.

## Layout

```
NCV-01-Cybership/
├── Assets/
│   ├── _Cybership/
│   │   ├── Scripts/
│   │   │   ├── Core/        # ShipStateManager, PlayerProgressionManager, WatchScheduleManager
│   │   │   ├── Stations/    # StationController + 5 stations + TacticalTarget + ModuleMount + CaptainsChair
│   │   │   ├── Systems/     # MAGISystem, EmergencyEventManager, MissionManager, ShipDesignerManager
│   │   │   └── UI/          # HUDManager, StationUIController
│   │   ├── Prefabs/         # (empty - build these in-editor)
│   │   ├── Materials/       # (empty)
│   │   ├── Audio/           # (empty)
│   │   ├── Animations/      # (empty)
│   │   └── Scenes/          # (empty - create your scene here)
│   └── VRChatExamples/      # drop-in SDK examples as needed
├── Packages/                # VPM registry + pinned package versions
├── ProjectSettings/         # Unity version pin
└── docs/                    # setup, configuration, networking, optimization, testing, ship designer
```

## Getting started

1. Open this folder in **Unity 2022.3.6f1** (or import via VRChat Creator Companion).
2. Open the docs: start with `docs/01-Scene-Setup.md`, then `docs/02-Prefab-Configuration.md`.
3. Wire up the scene per the docs, compile Udon, and test (see `docs/05-Testing-And-Polish.md`). The Ship Designer has its own build guide in `docs/06-Ship-Designer.md`.

## Design notes

- **No `UnityEvent` usage** — UdonSharp doesn't support it, so buttons call public methods via `SendCustomEvent` (wrapper methods like `SelectNode0()`, `PressPad0()`, `SendChannel0()` are provided for binding).
- **`switch` expressions** are avoided (unstable in UdonSharp) — converted to `if/else` and `switch` statements.
- **PlayerData** is read only after `OnPlayerRestored`; rank decisions are client-authoritative.
- **Networking** is host-authoritative for missions/events/MAGI; stations are operator-owned (seated player takes ownership and writes slot/score); alerts and reactor output are relayed to the host. See `docs/03-Networking-Reference.md`.

## Validation

All scripts compile cleanly against API stubs (0 errors). See `/tmp/opencode/cybercheck` (local, not part of the project) if you need to re-run the stub check.
