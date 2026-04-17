# Composite Task

MonoBehaviour hierarchy-as-tree task execution system for Unity. Build behavioral sequences using the Transform hierarchy — sequential, parallel, conditional — with async execution, progress tracking, and runtime control.

**Namespace:** `Hlight.Structures.CompositeTask.Runtime`  
**Dependencies:** UniTask, Newtonsoft.Json (for Blueprint JSON), DOTween (for Tween nodes)

## Architecture

```
TaskTree (MonoBehaviour)          — root manager, CTS lifecycle, event forwarding
  └── CompositeNode               — base for Sequential/Parallel
        ├── SequentialNode         — runs children one-by-one
        ├── ParallelNode           — fires all children concurrently
        └── TaskNode               — abstract base for all nodes
              ├── TaskNode<TConfig> — generic base with typed Settings
              └── ConditionalNode   — branching: first matching condition executes
```

Transform hierarchy = task tree. Children GameObjects = child tasks. No serialized lists — Unity's scene graph IS the data structure.

## Quick Start

```csharp
// 1. Add TaskTree component to root GameObject
// 2. Add SequentialNode or ParallelNode as child
// 3. Add leaf TaskNodes as children of composites
// 4. Execute:
taskTree.Execute();          // fire-and-forget, returns CTS
await taskTree.ExecuteAsync(ct);  // awaitable

// 5. Listen:
taskTree.StatusChanged += (node, status) => { };
taskTree.ProgressChanged += (node, delta) => { };

// 6. Control:
taskTree.ForceComplete();
taskTree.ResetTree();
taskTree.Dispose();
```

## TaskNode Lifecycle

```
Pending → Running → Finishing → Completed
              ↓          ↓
            Failed     Failed
```

```
Warm()           — one-time init before first execution (sealed, calls OnWarm)
ExecuteAsync()   — OnRunning(ct) → OnFinishing(ct) → OnCompleted()
ResetTask()      — OnReset() → clear events → Status = Pending
Dispose()        — calls ResetTask()
```

### Override Points

| Method | When | Must call base? |
|--------|------|-----------------|
| `OnWarm()` | Once before first execute | No |
| `OnRunning(ct)` | Main async logic | Abstract |
| `OnFinishing(ct)` | Cleanup after running | No |
| `OnCompleted()` | Sync action on completion | Yes, first |
| `OnReset()` | Cleanup during reset | No |

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Status` | `TaskStatus` | Current lifecycle state |
| `Progress` | `float [0,1]` | Set by node, fires ProgressChanged |
| `Weight` | `float` | Progress weight in parent composite |
| `Parent` | `CompositeNode` | Set by hierarchy rebuild |

### Events

| Event | Signature | Description |
|-------|-----------|-------------|
| `ProgressChanged` | `Action<TaskNode, float>` | Delta-based progress |
| `StatusChanged` | `Action<TaskNode, TaskStatus>` | Every status transition |

## Creating a Task

```csharp
[DefineTaskNode("My Task", Category = "Gameplay", 
    Description = "Does something. Ref: target (Transform). Config: speed (float).")]
public class MyTaskNode : TaskNode<MyTaskNode.Settings>
{
    [SerializeField] private Transform target;  // Unity refs on class

    protected override async UniTask OnRunning(Settings config, CancellationToken ct)
    {
        // async logic using config.speed and target
        await UniTask.Delay(1000, cancellationToken: ct);
    }

    [Serializable]
    public class Settings  // Pure data config
    {
        public float speed = 1f;
    }
}
```

**Rules:**
- `[DefineTaskNode]` with Id, Description, optional Category
- Unity object refs as `[SerializeField]` on the class
- Pure data in nested `[Serializable] Settings` class
- Access config via `Config` property outside `OnRunning`

## Built-in Nodes

### Utility
| Node | Description |
|------|-------------|
| Wait | Pause execution (milliseconds or frames) |
| Set Transform | Instant position/rotation/scale |
| Set Active GO | Activate/deactivate GameObject |
| Set Component Enabled | Enable/disable Behaviour |
| Force Complete | Force-complete another node |
| Set Progress | Set progress on another node |
| Instantiate | Spawn prefab at runtime |
| Destroy | Destroy GameObject |
| Invoke Unity Event | Fire UnityEvent |
| Time Scale | Set Time.timeScale (restores on reset) |

### Event
| Node | Description |
|------|-------------|
| Wait Event | Wait for TaskEventSource(s) — Any or All mode |

### Animation & Audio
| Node | Description |
|------|-------------|
| Play Animator | Animator state (Once/OnceAndWait/Loop) |
| Play Animation | Legacy Animation clip |
| Play Sound | AudioSource + optional clip override |
| Stop Sound | Stops AudioSource playback on completion |
| Particle System | Play particle (PlayAndWait/PlayAndForget) |

### Tween (DOTween)
| Node | Description |
|------|-------------|
| Tween Position | value + isFrom + localSpace + speedBased + relative |
| Tween Rotation | Euler angles |
| Tween Scale | Local scale |
| Tween Anchor Pos | RectTransform |
| Tween Color | SpriteRenderer[] + Graphic[] |
| Tween Canvas Group Alpha | CanvasGroup fade |
| Tween Float | Generic float via UnityEvent callback |

### Special
| Node | Description |
|------|-------------|
| Conditional | Branch: evaluate conditions top-down, execute first match |
| Run Sub Tree | Execute another TaskTree |

## Composite Nodes

**SequentialNode** — runs active children one-by-one. Exposes `CurrentChild`/`CurrentChildIndex`.

**ParallelNode** — fires all active children concurrently. Detects runtime child insertion via `OnTransformChildrenChanged`. Disabled children during execution are force-completed.

**Progress aggregation** — weighted by `Weight`. Parent progress = sum of (child delta × child weight / total weight).

## Conditional Node

```
ConditionalNode
├── Branch[0]: condition=HealthCheck, node=HealSequence
├── Branch[1]: condition=null, node=DefaultSequence  ← fallback
```

`TaskCondition` — abstract MonoBehaviour with `bool Evaluate()`. Assign to branches via Inspector.

## Inline Tasks (Runtime)

Insert logic without creating MonoBehaviour:

```csharp
// Lambda
sequentialNode.InsertInline(async ct => await UniTask.Delay(1000, ct));

// Full lifecycle
public class MyLogic : IInlineTask
{
    public UniTask OnRunning(CancellationToken ct) => ...;
    public void OnWarm() { }
    public void OnReset() { }
}
sequentialNode.InsertInline(new MyLogic());
```

## Blueprint (JSON Import/Export)

```json
{
  "type": "sequential",
  "name": "Root",
  "children": [
    { "type": "Wait", "name": "Delay", "config": { "value": 1000, "unit": 1 } },
    { "type": "parallel", "children": [...] }
  ]
}
```

Import/Export via TaskTree Inspector foldout. `type` = `"sequential"` | `"parallel"` | DefineTaskNode Id.

### Runtime JSON Loading

Build a task tree into an existing `TaskTree` from JSON at runtime:

```csharp
taskTree.LoadFromJson(jsonString);  // destroys existing children, builds new hierarchy
taskTree.Execute();
```

### PresetTaskTree (prefab-backed leaves)

`PresetTaskTree` maps `typeId → prefab`. During `LoadFromJson`, matching leaf nodes are instantiated from the preset prefab (preserving pre-set `[SerializeField]` refs like `AudioSource`, `Transform`), then JSON `config` is applied on top via `IConfigurable.ApplyConfig`. Non-matching leaves fall back to plain GameObject + AddComponent.

Use case: JSON controls flow + configurable data; prefabs provide Unity object references that can't be serialized to JSON.

## Dependency Injection

Visitor pattern — opt-in per task:

```csharp
public class MyVisitor : IDependencyInjectionVisitor
{
    public void Visit<T>(T target)
    {
        if (target is MyTaskNode t) t.Camera = myCamera;
    }
}

taskTree.Accept(visitor);  // propagates to all nodes
```

## Editor Features

- **Hierarchy icons** — procedural (↓ Sequential, ≡ Parallel, ● Leaf, ✕ Invalid)
- **Status colors** — icon changes color during play (blue=Running, yellow=Finishing, green=Completed, red=Failed)
- **Add Child dropdown** — categorized, searchable, nested via `/` in Category. Package nodes grouped above project nodes.
- **Switch Sequential↔Parallel** — one-click button in Inspector. If GameObject name was the default ("Sequential"/"Parallel"), it auto-renames on switch.
- **GameObject menu** — `GameObject/Composite Task/` → Task Tree (with root), Sequential Node, Parallel Node
- **Validation warnings** — orphan nodes, invalid placement (via `IsValidChild`)
- **Import/Export JSON** — with overwrite confirmation
- **Runtime controls** — Execute/Reset/Dispose buttons, progress bar, status display

## File Structure

```
Runtime/
├── Core/           — TaskNode, CompositeNode, Sequential, Parallel, TaskTree, PresetTaskTree, enums
├── Blueprint/      — IBlueprint, BlueprintTree, JSON parser, TaskNodePreset
├── Builder/        — TaskTreeBuilder, TaskNodeRegistry
└── Nodes/
    ├── Audio/       — PlaySoundNode, StopSoundNode
    ├── Conditional/ — ConditionalNode, TaskCondition
    ├── Inline/      — IInlineTask, InlineTaskNode, extensions
    ├── Event/       — TaskEventSource, WaitEventNode, sources
    ├── Tween/       — DOTween nodes (separate asmdef)
    └── *.cs         — utility/animation nodes
Editor/
└── *.cs            — inspectors, hierarchy decorator, JSON exporter, menu items
```
