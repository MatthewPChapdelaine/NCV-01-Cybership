# 04 - Optimization

Getting the world within VRChat performance limits.

## General targets

- **Polygon count** — keep under ~200k polys for a mid-size world; use LODs.
- **Draw calls** — aim < 30; merge static geometry into batched meshes.
- **Texture budget** — single 2K or a few 1K atlases; avoid per-object textures.
- **Shaders** — use `VRChat/Mobile/Standard` or `VRChat/Mobile/Toon` for world geometry; avoid `Standard` with expensive features (SSR, parallax).

## UdonScript performance

- `Update()` is used only where needed (station minigames, HUD refresh). Keep per-frame work minimal:
  - Avoid `GetComponent` / string building in `Update`.
  - Cache `stationUIController`, `TextMesh` etc. in `Start()`.
- `[FieldChangeCallback]` methods only run when a synced value changes — use them instead of polling.
- `SendCustomEventDelayedSeconds` is fine for UI timers; avoid high-frequency network events.
- Limit `RequestSerialization()` calls; batch state writes (e.g., set fields, then serialize once).

## Particle / light budget

- Particle systems: cap emitter counts; use the **World Space** mode with small rates for ship effects.
- Realtime lights: limit to a few; prefer emissive materials + baked lighting for the hull.
- MAGI core / alert lighting should toggle **Light components** rather than spawn new ones.

## Object pooling

- Tactical targets are instantiated with `VRCInstantiate` — destroy old targets before spawning new ones (already capped by `targetLifetime`).
- For heavy effects, reuse a small pool of prefabs instead of spawning per shot.

## Avatar / instance checks

- `[RequireComponent]` where applicable to avoid missing components at runtime.
- World must be **`Allow Low Polygon Trust +`** friendly: no particle-on-spawn storms, no dynamic meshes per frame.

## Testing performance

1. Use the VRChat **Performance Stats** window in the Editor.
2. Build a test instance with 8+ avatars and verify frame time stays green.
3. Profile the world with the **Profile Analyzer** if frame spikes appear around station minigames.
