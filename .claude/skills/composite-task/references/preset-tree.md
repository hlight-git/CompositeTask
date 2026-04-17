# PresetTaskTree

`PresetTaskTree` extends `TaskTree` with a typeId → prefab map. When `LoadFromJson` builds leaves, matching typeIds instantiate the prefab (keeping its `[SerializeField]` Unity refs) instead of creating a bare GameObject.

## When to use

- Runtime tree loading from JSON where certain leaves need pre-wired Unity refs (AudioSource, sprites, animators).
- JSON controls *flow and parameters*; prefabs provide *hard-to-serialize Unity references*.
- Content authoring pipeline: designers author JSON files, engineers maintain prefab presets.

**Don't use** when every ref can be injected via DI at runtime, or when there are no prefab-worthy leaves.

## Setup

1. Add `PresetTaskTree` component to the host GameObject (instead of `TaskTree`).
2. In the Inspector, populate `_nodePresets` — one entry per leaf type that should be prefab-backed:
   - `typeId` → string, must match `[DefineTaskNode]` Id (e.g., `"Play Sound"`)
   - `prefab` → GameObject with the TaskNode component already on it
3. Ensure the prefab's TaskNode has refs pre-assigned (or part of the prefab hierarchy).

## Runtime API

```csharp
presetTree.LoadFromJson(jsonString);
presetTree.Execute();
```

Flow:
1. `BlueprintJsonParser.Parse` → `BlueprintTree`
2. Destroys existing TaskNode children under `presetTree.transform`
3. For each leaf in the blueprint:
   - If `typeId` matches a preset → `Instantiate(preset.prefab, parent)` + apply config
   - Else → new GameObject + AddComponent → apply config (same as plain TaskTree)
4. Composite nodes (sequential/parallel) always use the no-preset path (they don't need prefabs).

## Config override on preset clones

After instantiating the prefab, `TryApplyConfig(node, blueprint)` calls `IConfigurable.ApplyConfig(config)` to override the Settings:

```
Preset prefab:        [SerializeField] source = AudioSource_BGM
                      Settings.mode = Loop
                      Settings.volume = 0.8

JSON blueprint:       "config": { "mode": 1, "volume": 0.5 }

After build:          [SerializeField] source = AudioSource_BGM    ← kept from prefab
                      Settings.mode = OnceAndWait                   ← overridden by JSON
                      Settings.volume = 0.5                         ← overridden by JSON
```

Intent: **prefab for refs, JSON for data.**

## Subclassing

If you need project-specific preset lookup (e.g. by scope, by level), override `GetNodePresets`:

```csharp
public class MyLevelTree : PresetTaskTree
{
    [SerializeField] private LevelDatabase _db;

    protected override IReadOnlyList<TaskNodePreset> GetNodePresets()
        => _db.GetActiveLevelPresets();
}
```

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| Preset not applied, bare GameObject instantiated | `typeId` in JSON doesn't match preset's `typeId` exactly (case-sensitive) | Log `TaskNodeRegistry.AllEntries` keys and compare. Spaces/casing matter. |
| `[SerializeField]` ref is null on cloned preset | Prefab's ref pointed to a scene object — Unity nulls scene refs when the prefab is loaded outside that scene | Keep refs inside the prefab's own hierarchy, or inject via `TaskTree.Accept(visitor)` after LoadFromJson |
| Config fields still show prefab defaults, not JSON values | JSON `config` object keys don't match `Settings` class field names (case matters) | Re-check Settings field names. JSON is case-sensitive. |
| Warnings about "Preset for 'X' has no TaskNode" | Preset prefab missing the component | Add the TaskNode component to the prefab's root GameObject |
| Old tree state persists after LoadFromJson | Code continues executing against stale refs | `LoadFromJson` destroys TaskNode children but not your captured references — re-acquire via `taskTree.Root` after load |
| LoadFromJson does nothing, no error | JSON parse failed silently | Check Console for `[BlueprintJsonParser]` warnings. Validate JSON structure (must have `type`, composites need `children`). |

## Gotchas

- **typeId case-sensitive.** Misspelled id → preset not matched → fallback to bare GameObject.
- **Prefab must have the right TaskNode component.** If preset prefab is missing the TaskNode, `TaskTreeBuilder.BuildLeafFromPreset` attempts `AddComponent` as a fallback — works but defeats the prefab purpose.
- **Prefab refs to scene objects** will be null after instantiate (standard Unity prefab behaviour). Keep refs inside the prefab's own hierarchy or inject via DI.
- **Re-LoadFromJson** destroys previous children (detached first, then `Destroy`/`DestroyImmediate`). Any state in old leaves is lost.
- **Call `Accept(visitor)` AFTER LoadFromJson** — the old tree's DI-injected refs don't survive rebuild; the new tree needs a fresh pass.

## JSON vs Preset: who wins?

| Property | Source |
|----------|--------|
| Unity object refs (`AudioSource`, `Transform`, `UnityEvent`) | **Prefab** — untouched |
| Settings fields (floats, enums, etc.) | **JSON** — overrides prefab |
| GameObject name | **JSON** `name` field — overrides prefab name |
| Active state | **JSON** `enabled` → `SetActive` |
| Weight | **JSON** `weight` |
