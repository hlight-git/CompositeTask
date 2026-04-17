# Built-in Node Catalog

Complete reference for all built-in TaskNodes in the package. Use this to map a natural-language step to a typeId + Settings.

Convention for each entry:
- **typeId** — use in Blueprint JSON `"type": "..."`
- **Base** — `TaskNode` or `TaskNode<T>`
- **Refs** — `[SerializeField]` Unity object refs (must be assigned per-instance)
- **Config** — Settings fields (`field: Type = default`)

---

## Composite containers

These are automatically recognized — in Blueprint JSON use `"type": "sequential"` or `"type": "parallel"`.

### SequentialNode
- Runs active children one-by-one in sibling order.
- Exposes `CurrentChild` / `CurrentChildIndex`.
- Skips disabled children.

### ParallelNode
- Fires all active children concurrently, completes when all finish.
- Detects runtime child insertion via `OnTransformChildrenChanged`.
- Disabled children during execution are force-completed.

---

## Audio

### Play Sound (`PlaySoundNode`)
- **typeId:** `Play Sound`
- **Base:** `TaskNode<Settings>`
- **Refs:** `source: AudioSource`, `clip: AudioClip` (optional override)
- **Config:** `mode: TaskPlayMode = OnceAndWait`
- **Behaviour:** Once → `PlayOneShot`, OnceAndWait → PlayOneShot + await clip.length, Loop → set `source.loop=true` + `Play()`.

### Stop Sound (`StopSoundNode`)
- **typeId:** `Stop Sound`
- **Base:** `TaskNode`
- **Refs:** `source: AudioSource`
- **Behaviour:** Calls `source.Stop()` in `OnCompleted`.

---

## Animation

### Play Animator (`PlayAnimatorNode`)
- **typeId:** `Play Animator`
- **Base:** `TaskNode<Settings>`
- **Refs:** `target: Animator`
- **Config:** `stateName: string`, `layer: int = 0`, `speed: float = 1.0`, `mode: TaskPlayMode = OnceAndWait`
- **Behaviour:** Calls `Animator.Play(stateName, layer)` with speed applied. OnceAndWait awaits state duration.

### Play Animation (`PlayAnimationNode`)
- **typeId:** `Play Animation`
- **Base:** `TaskNode<Settings>`
- **Refs:** `target: Animation` (legacy Animation component)
- **Config:** `clipName: string` (empty = default clip), `speed: float = 1.0`, `mode: TaskPlayMode = OnceAndWait`
- **Behaviour:** Plays legacy Animation clip. Use PlayAnimator for Mecanim instead.

---

## Tween (DOTween — separate asmdef)

All tween nodes share: `isFrom`, `duration`, `ease`, `speedBased`. All override `OnCompleted` + `OnReset` (kill tween on reset).

### Tween Position (`TweenPositionNode`)
- **typeId:** `Tween Position`
- **Base:** `TaskNode<Settings>`
- **Refs:** `target: Transform`
- **Config:** `value: Vector3`, `isFrom: bool`, `duration: float = 0.5`, `ease: Ease = InOutSine`, `localSpace: bool = true`, `speedBased: bool`, `relative: bool`

### Tween Rotation (`TweenRotationNode`)
- **typeId:** `Tween Rotation`
- Same shape as Tween Position; value is Euler Vector3.

### Tween Scale (`TweenScaleNode`)
- **typeId:** `Tween Scale`
- **Refs:** `target: Transform`
- **Config:** `value: Vector3 = Vector3.one`, `isFrom: bool`, `duration: float = 0.5`, `ease: Ease = InOutSine`, `speedBased: bool`, `relative: bool`
- Always local scale.

### Tween Anchor Pos (`TweenAnchorPosNode`)
- **typeId:** `Tween Anchor Pos`
- **Refs:** `target: RectTransform`
- **Config:** `value: Vector2`, `isFrom: bool`, `duration: float = 0.5`, `ease: Ease = InOutSine`, `speedBased: bool`, `relative: bool`

### Tween Color (`TweenColorNode`)
- **typeId:** `Tween Color`
- **Refs:** `spriteRenderers: SpriteRenderer[]`, `graphics: Graphic[]`
- **Config:** `value: Color = Color.white`, `isFrom: bool`, `duration: float = 0.5`, `ease: Ease = InOutSine`, `speedBased: bool`
- Applies to all assigned targets.

### Tween Canvas Group Alpha (`TweenCanvasGroupAlphaNode`)
- **typeId:** `Tween Canvas Group Alpha`
- **Refs:** `target: CanvasGroup`
- **Config:** `value: float [0-1]`, `isFrom: bool`, `duration: float = 0.5`, `ease: Ease = InOutSine`, `speedBased: bool`

### Tween Float (`TweenFloatNode`)
- **typeId:** `Tween Float`
- **Refs:** `onValueChanged: UnityEvent<float>`
- **Config:** `from: float`, `to: float = 1.0`, `duration: float = 0.5`, `ease: Ease = InOutSine`
- Generic float tween, invokes `onValueChanged` each frame. Wire the callback via Inspector.

**Tween semantics recap:**
- `isFrom=false` (default): tween current → value.
- `isFrom=true`: tween FROM value TO current (snapshot current before start).
- `relative=true`: value is additive (current + value).
- `speedBased=true`: duration is units-per-second instead of total seconds.

**Concrete examples:**
- *Fade in from invisible*: `Tween Canvas Group Alpha`, `value=1`, `isFrom=true`, `duration=0.3` → sets alpha to 0 (snapshots current 1, uses value 0 as "from"), then tweens back to 1. Common flash pattern.
- *Knockback 2 units to the right*: `Tween Position`, `value=(2,0,0)`, `relative=true`, `duration=0.15` → moves from current to (current + 2,0,0).
- *Constant-speed travel*: `Tween Position`, `value=(10,0,0)`, `speedBased=true`, `duration=5` → takes `distance/5` seconds (not 5 seconds total). Useful when distances vary per-instance.
- *Rise from below then settle*: pair two nodes — first `Tween Position value=(0,-100,0) isFrom=true relative=true` (starts 100 below, lifts to current), then a tiny overshoot tween for snap.

---

## VFX

### Particle System (`ParticleSystemNode`)
- **typeId:** `Particle System`
- **Base:** `TaskNode<Settings>`
- **Refs:** `target: ParticleSystem`
- **Config:** `mode: ParticlePlayMode = PlayAndWait`
- **Behaviour:** `target.Play()`; PlayAndWait awaits `isPlaying=false`.

---

## Transformation / state setters

### Set Transform (`SetTransformNode`)
- **typeId:** `Set Transform`
- **Base:** `TaskNode<Settings>`
- **Refs:** `target: Transform`
- **Config:** `setPosition: bool`, `position: Vector3`, `setRotation: bool`, `rotation: Vector3`, `setScale: bool`, `scale: Vector3 = Vector3.one`, `localSpace: bool = true`
- **Behaviour:** Runs in `OnCompleted`. Only assigns properties whose `setX` flag is true. Instant, not tweened.

### Set Active GO (`SetGameObjectActivationNode`)
- **typeId:** `Set Active GO`
- **Base:** `TaskNode<Settings>`
- **Refs:** `target: GameObject`
- **Config:** `active: bool`
- **Behaviour:** `target.SetActive(active)` in `OnCompleted`.

### Set Component Enabled (`SetComponentEnabledNode`)
- **typeId:** `Set Component Enabled`
- **Base:** `TaskNode<Settings>`
- **Refs:** `target: Behaviour`
- **Config:** `enabled: bool`

---

## Lifecycle & control

### Wait (`WaitNode`)
- **typeId:** `Wait`
- **Base:** `TaskNode<Settings>`
- **Config:** `value: int = 1000`, `unit: WaitUnit = Milliseconds`
- **Behaviour:** `Frames` → `DelayFrame`; `Milliseconds` → `Delay`; `UnscaledMilliseconds` → `Delay(ignoreTimeScale=true)`.

### Instantiate (`InstantiateNode`)
- **typeId:** `Instantiate`
- **Base:** `TaskNode<Settings>`
- **Refs:** `prefab: GameObject`, `parent: Transform` (optional)
- **Config:** `destroyOnReset: bool`
- **Behaviour:** Instantiates in `OnCompleted`. On `ResetTask`, destroys the instance if `destroyOnReset=true`.

### Destroy (`DestroyNode`)
- **typeId:** `Destroy`
- **Base:** `TaskNode<Settings>`
- **Refs:** `target: GameObject`
- **Config:** `delay: float`
- **Behaviour:** `Destroy(target, delay)` in `OnCompleted`.

### Time Scale (`TimeScaleNode`)
- **typeId:** `Time Scale`
- **Base:** `TaskNode<Settings>`
- **Config:** `timeScale: float = 1.0` (min 0)
- **Behaviour:** Sets `Time.timeScale`, remembers previous value; restores on `OnReset`.

### Force Complete (`ForceCompleteNode`)
- **typeId:** `Force Complete`
- **Base:** `TaskNode<Settings>`
- **Refs:** `target: TaskNode`
- **Config:** `immediate: bool`
- **Behaviour:** Calls `target.ForceComplete(immediate)` in `OnCompleted`.

### Set Progress (`SetProgressNode`)
- **typeId:** `Set Progress`
- **Base:** `TaskNode<Settings>`
- **Refs:** `target: TaskNode`
- **Config:** `progress: float [0-1]`
- **Behaviour:** Calls `target.SetProgress(progress)` in `OnCompleted`.

---

## Integration

### Invoke Unity Event (`InvokeUnityEventNode`)
- **typeId:** `Invoke Unity Event`
- **Base:** `TaskNode`
- **Refs:** `onExecute: UnityEvent`
- **Behaviour:** Invokes in `OnCompleted`. Wire callbacks in Inspector.

### Run Sub Tree (`RunSubTreeNode`)
- **typeId:** `Run Sub Tree`
- **Base:** `TaskNode`
- **Refs:** `_subTree: TaskTree`
- **Behaviour:** Executes another TaskTree; forwards progress from sub-root.

---

## Conditional

### Conditional (`ConditionalNode`)
- **typeId:** `Conditional`
- **Base:** `TaskNode`
- **Refs:** `_branches: List<Branch>` where `Branch = { TaskCondition condition, TaskNode node }`
- **Behaviour:** Top-to-bottom evaluation; first matching branch's node executes. Null condition = fallback (always matches). If no branch matches, completes immediately.
- **Children:** branches point to TaskNodes that are children of Conditional.

**TaskCondition** — abstract MonoBehaviour with `bool Evaluate()`. Subclass it for each project-specific condition.

---

## Event

### Wait Event (`WaitEventNode`)
- **typeId:** `Wait Event`
- **Base:** `TaskNode<Settings>`
- **Refs:** `sources: TaskEventSource[]`
- **Config:** `mode: EventWaitMode = Any` (Any / All)
- **Behaviour:** Awaits `.Fire()` call on assigned source(s). Any → first fire completes. All → wait for every source.

**TaskEventSource subclasses:**
- `GameObjectActiveEventSource` — fires on `OnEnable`.
- `TriggerEnterEventSource` — fires on `OnTriggerEnter2D`. Has `tagFilter: string` (optional).

Custom sources: extend `TaskEventSource`, call `Fire()` from your own logic.

**⚠ WaitEventNode gotcha with `EventWaitMode.All`:** the task waits FOREVER until every source has fired. If any source is unreachable (disabled GameObject, wrong tag filter, never-met condition), the tree hangs. Solutions:
- Pair with `ForceComplete` on a timer to guarantee progress
- Switch to `Any` mode if some sources are optional
- Add a fallback `TriggerEnterEventSource` with no tag filter
Also: cancellation via the tree's CTS does exit `WaitEventNode` cleanly — so `ResetTree()` / `Dispose()` still work.

---

## JSON type mapping

For Blueprint JSON `"type"` field:
- Composite → lowercase: `"sequential"` | `"parallel"`
- Leaf → exact typeId string: `"Play Sound"`, `"Wait"`, `"Tween Position"`, etc. (case-sensitive, spaces included)

The `TaskNodeRegistry` auto-scans `[DefineTaskNode]` classes. To check what's registered at runtime:

```csharp
foreach (var kv in TaskNodeRegistry.AllEntries)
    Debug.Log($"{kv.Key} → {kv.Value.FullName}");
```

---

## Enum reference

```csharp
enum TaskPlayMode      { Once, OnceAndWait, Loop }
enum WaitUnit          { Frames, Milliseconds, UnscaledMilliseconds }
enum EventWaitMode     { Any, All }
enum ParticlePlayMode  { PlayAndForget, PlayAndWait }
enum BlueprintExecutionMode { Sequential, Parallel }  // used internally in JSON
```

## Mapping tips (natural language → typeId)

| User says | Likely node |
|-----------|-------------|
| "wait / delay / pause N seconds/frames" | `Wait` |
| "play a sound / sfx / music" | `Play Sound` |
| "stop the music / silence" | `Stop Sound` |
| "play animation / state" | `Play Animator` (Mecanim) or `Play Animation` (legacy) |
| "move to position / slide" | `Tween Position` |
| "rotate / turn" | `Tween Rotation` |
| "scale / grow / shrink" | `Tween Scale` |
| "fade in/out" | `Tween Color` (alpha channel) or `Tween Canvas Group Alpha` (UI) |
| "flash color" | `Tween Color` with `isFrom=true` |
| "shake screen" | Custom TaskNode (no built-in) — propose creating one |
| "spawn / create prefab" | `Instantiate` |
| "destroy / remove" | `Destroy` |
| "slow motion / pause time" | `Time Scale` |
| "show / hide / toggle" | `Set Active GO` or `Set Component Enabled` |
| "teleport / snap to position" | `Set Transform` |
| "emit / play particles" | `Particle System` |
| "fire event / call callback" | `Invoke Unity Event` |
| "wait until player touches X" | `Wait Event` with `TriggerEnterEventSource` |
| "if condition then ... else ..." | `Conditional` + custom `TaskCondition` |
| "run another tree" | `Run Sub Tree` |

**When the user's description has no obvious match:** propose creating a new TaskNode (see `node-creation.md`) rather than forcing a wrong mapping.
