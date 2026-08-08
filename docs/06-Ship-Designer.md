# 06 - Ship Designer (Level-Designer Console)

A palette-driven module editor that plays like a level designer. Players sit at the
**Designer Console**, pick a module from the palette, paint it onto the ship's mount
grid with their hand ray, rotate or erase cells, then **Apply** the layout to the
shared ship so the whole crew sees it.

## What it does

| Action | How |
|---|---|
| Pick a module | Palette buttons (`SelectModule0..5`) |
| Paint a cell | Point your right-hand ray at a `ModuleMount` cell, pull **Use** |
| Erase a cell | `SetEraseMode`, then Use the cell (or `RotateSelected`) |
| Rotate | `RotateSelected()` cycles the selected cell 90° |
| Clear all | `ClearDesign()` |
| Random fill | `FillRandom()` scatters modules across empty cells |
| Save blueprint | `SaveDesign()` → per-player `PlayerData` (persists) |
| Load blueprint | `LoadDesign()` → restores from `PlayerData` |
| Apply to ship | `ApplyDesign()` → synced to every player |

## Grid model

- `gridCols` × `gridRows` cells (default **4×4 = 16**). Cell index = `row * gridCols + col`.
- Each cell maps 1:1 to a hull mount:
  - `cellAnchors[i]` — optional selection highlight marker.
  - `cellVisualRoots[i]` — cell root `GameObject`; **its children are the module
    visuals, index-aligned with `MODULE_NAMES`** (child 0 = Hull Plate, child 1 =
    Engine, …). Exactly one child is active per cell at a time; when the cell is
    empty, none are active. Module children should be authored facing **+Z** so the
    90° rotation steps make sense.
- The applied design is encoded as a compact string:
  `"cellIndex:moduleId:rotation;"` (e.g. `"0:2:0;7:1:3;15:4:2;"`), synced in a single
  `[UdonSynced] string`.

## Scene setup

Add to the hierarchy (see `01-Scene-Setup.md` for placement):

```
_SYSTEMS
└── SHIP_DESIGNER            (ShipDesignerManager + VRCStation + UdonBehaviour)
_SHIP
└── _MOUNTS                  (grid cells, one per gridCols×gridRows slot)
    ├── CELL_00              (child: MODULE_00..05 visuals; collider + ModuleMount)
    ├── CELL_01
    └── ...
_SYSTEMS
└── SHIP_DESIGNER_CONSOLE    (world-space Canvas: palette/tool/action buttons + readouts)
```

### Mount cells (`_MOUNTS`)

1. Create `gridCols × gridRows` child GameObjects under `_MOUNTS`, named `CELL_00..`.
2. On each cell:
   - A `Collider` (for the hand-ray paint raycast). Keep it on a raycastable layer.
   - A `ModuleMount` component with `cellIndex` set to the cell's index
     (`row * gridCols + col`).
   - Child `GameObject`s — one per module type, **in the same order as
     `MODULE_NAMES`** — all **inactive** in the scene.
3. Assign the cells to `ShipDesignerManager.cellVisualRoots[]` in index order.
4. (Optional) assign `cellAnchors[]` with a small marker to highlight the selected cell.

### Designer console (seat + UI)

1. Add a **`VRCStation`** to the `SHIP_DESIGNER` GameObject. When a player sits,
   `OnStationEntered` opens the console; leaving closes it (the ShipDesignerManager
   behaviour and the VRCStation live on the same GameObject, so the events fire
   automatically).
2. Add `ShipDesignerManager` (UdonSharp) to the same object. Set **Sync Mode: Manual**.
3. Assign `designerUI`, `statusText`, `gridText`, `hudManager`.
4. Wire the console buttons with `SendCustomEvent` (UdonSharp has no `UnityEvent`):

| Button | Method on ShipDesignerManager |
|---|---|
| Module 0–5 | `SelectModule0()` … `SelectModule5()` |
| Place | `SetPlaceMode()` |
| Erase | `SetEraseMode()` |
| Rotate | `RotateSelected()` |
| Prev cell / Next cell | `CycleCellPrevious()` / `CycleCellNext()` |
| Clear | `ClearDesign()` |
| Random | `FillRandom()` |
| Save | `SaveDesign()` |
| Load | `LoadDesign()` |
| Apply | `ApplyDesign()` |

### Readouts

- `statusText` shows tool, active module, selected cell, and design author.
- `gridText` renders a mini-map of the grid (`0`–`5` = module id, `.` = empty).

## Networking

The applied design is **owned by whoever applied it last**, not just the host:

1. A player presses **Apply** → the console briefly takes ownership
   (`Networking.SetOwner` — the official "become-owner" pattern).
2. Once owned (`OnOwnershipTransferred`), it writes `_shipDesignData` +
   `_designAuthor` and calls `RequestSerialization()`.
3. After a 1s flush delay (`HandBackOwnership`), ownership returns to the instance
   master so the world stays host-controlled for future applies.

> Non-owners **cannot** write `[UdonSynced]` variables — a bare write +
> `RequestSerialization()` is silently dropped. That is why Apply takes ownership
> first. See `03-Networking-Reference.md`.

- While a player is **editing**, their client shows their local design as a preview
  (even if a remote design was applied meanwhile). Closing the console reverts the
  preview to the shared design.
- The **host** restores its saved blueprint to the ship on world load
  (`OnPlayerRestored` → `ApplyDesign`).

## Persistence

- `SaveDesign()` writes the encoded grid to the local player's `cybership_design`
  PlayerData key (world-scoped, client-authoritative — same model as rank/XP).
- PlayerData is only safe to read after `OnPlayerRestored`; `SaveDesign`/`LoadDesign`
  no-op with a notification until then.

## Sync budget

`gridCols × gridRows` cells, capped at `maxModulesPlaced` (default 24) placed
modules. Worst-case design string is well under 512 bytes, so it fits in a single
manual-sync serialization.

## Testing

1. Sit at the console → console opens, status shows `DESIGNER ONLINE`.
2. Select a module, point at a mount cell, pull Use → module appears on the cell
   (local preview) and `gridText` updates.
3. Rotate / erase / clear / random-fill → visuals track the local design.
4. Save, then Load → blueprint restores.
5. Apply → a second client (or your alt) sees the same layout + author name.
6. While a guest is editing, have the host apply a design → guest's preview is
   untouched until they close the console.
