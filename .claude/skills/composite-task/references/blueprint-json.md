# Blueprint JSON Format

Pure-data representation of a task tree. Used for import/export in editor and `LoadFromJson()` at runtime.

## Top-level shape

The root of the JSON file IS the root node (no wrapper object).

```json
{
  "type": "sequential",
  "name": "Root",
  "enabled": true,
  "weight": 0,
  "children": [
    { "type": "Wait", "name": "Delay", "config": { "value": 1000, "unit": 1 } },
    { "type": "parallel", "name": "Group", "children": [ ... ] }
  ]
}
```

## Fields (all nodes)

| Field | Type | Required | Default | Purpose |
|-------|------|----------|---------|---------|
| `type` | string | yes | — | `"sequential"` \| `"parallel"` \| typeId of a leaf (e.g. `"Play Sound"`) |
| `name` | string | no | `"Node"` | GameObject name in hierarchy |
| `enabled` | bool | no | `true` | `GameObject.SetActive` on build |
| `weight` | float | no | `0` | Progress weight in parent composite |
| `children` | array | composite only | `[]` | Child node objects (composites only) |
| `config` | object | leaf w/ Settings | `null` | Pure-data Settings for `TaskNode<T>` |

## Leaf node (with Settings)

```json
{
  "type": "Wait",
  "name": "Wait 2s",
  "config": { "value": 2000, "unit": 1 }
}
```

- `config` keys must match Settings field names exactly.
- Enum values serialize as their integer index (or Newtonsoft may accept strings with `StringEnumConverter`). Default: integer.
- Missing `config` → Settings keeps its C# defaults.

## Leaf node (no Settings — pure `TaskNode`)

```json
{ "type": "Stop Sound", "name": "Silence" }
```

No `config` field — `StopSoundNode` has no Settings class.

## Composite node

```json
{
  "type": "sequential",        // or "parallel"
  "name": "Intro Sequence",
  "children": [ ... ]
}
```

- `children` is required for composites (but may be empty).
- No `config` field on composites.

## Enum values (common reference)

Integer indices — order matches the enum declaration:

```
TaskPlayMode:        Once=0, OnceAndWait=1, Loop=2
WaitUnit:            Frames=0, Milliseconds=1, UnscaledMilliseconds=2
EventWaitMode:       Any=0, All=1
ParticlePlayMode:    PlayAndForget=0, PlayAndWait=1
DOTween Ease:        Linear=1, InSine=2, OutSine=3, InOutSine=4, ... (many values)
```

**When in doubt, write the config value as a number comment**: `/* OnceAndWait=1 */ "mode": 1`.

## Complete example

Scenario: wait 1s → parallel(tween up + fade in) → play sfx.

```json
{
  "type": "sequential",
  "name": "Show Intro",
  "children": [
    {
      "type": "Wait",
      "name": "Delay 1s",
      "config": { "value": 1000, "unit": 1 }
    },
    {
      "type": "parallel",
      "name": "Show Card",
      "children": [
        {
          "type": "Tween Position",
          "name": "Rise",
          "config": {
            "value": { "x": 0, "y": 100, "z": 0 },
            "isFrom": false,
            "duration": 0.5,
            "ease": 4,
            "localSpace": true,
            "speedBased": false,
            "relative": true
          }
        },
        {
          "type": "Tween Canvas Group Alpha",
          "name": "Fade In",
          "config": {
            "value": 1,
            "isFrom": true,
            "duration": 0.5,
            "ease": 4,
            "speedBased": false
          }
        }
      ]
    },
    {
      "type": "Play Sound",
      "name": "SFX",
      "config": { "mode": 1 }
    }
  ]
}
```

## Vector/Color serialization

```json
"value": { "x": 1.0, "y": 2.0, "z": 3.0 }           // Vector3
"value": { "x": 0, "y": 100 }                         // Vector2
"color": { "r": 1.0, "g": 0, "b": 0, "a": 1.0 }       // Color (0–1 floats)
```

## Unity object refs — cannot be in JSON

Fields like `AudioSource source`, `Transform target`, `UnityEvent onExecute` are NOT represented in JSON. They are:
- **Filled from a prefab** when using `PresetTaskTree` + matching typeId preset.
- **Assigned after build** via Editor (Inspector) or scripted reflection.

This is why the skill's build flow has a separate **ref-fill** step after JSON build.

## Loading flow (runtime)

```csharp
taskTree.LoadFromJson(jsonString);
// → BlueprintJsonParser.Parse → BlueprintTree
// → TaskTreeBuilder.BuildNode (under taskTree.transform, with presets if PresetTaskTree)
// → config applied via IConfigurable.ApplyConfig
taskTree.Execute();
```

## Loading flow (editor import)

TaskTree inspector → Import foldout → JSON field → "Import" button. Destroys existing TaskNode children, builds new ones. Uses `PrefabUtility.InstantiatePrefab` (preserves prefab link) when a preset matches.

## Troubleshooting JSON

| Symptom | Cause |
|---------|-------|
| `"Unknown typeId 'xxx'"` warning | typeId string doesn't match any `[DefineTaskNode]` Id. Case-sensitive. |
| Config values all at C# defaults | `config` object missing or field names mismatched with Settings. |
| Node instantiated without prefab's refs | Not using `PresetTaskTree` OR no preset entry for that typeId. |
| Children not spawning | Composite missing `"children"` array, or child types unknown. |
