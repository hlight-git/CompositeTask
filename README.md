# Composite Task

Tree-based task execution system for Unity. Declare behavioral sequences (sequential or parallel), track progress, and intervene at runtime (cancel, force complete).

**Namespace:** `Hlight.Structures.CompositeTask.Runtime` (runtime), `Hlight.Structures.CompositeTask.Editor` (editor)

**Dependencies:** UniTask, Newtonsoft.Json, Odin Inspector (optional)

## Files

```
Runtime/
  ATask.cs                        — abstract base, all tasks inherit this
  CompositeTask.cs                — has children list, runs Sequential or Parallel
  TaskTree.cs                     — holds root CompositeTask, entry points: Execute(), Accept()
  DefineTaskAttribute.cs          — [DefineTask("name")] attribute + TypeSerializationBindingMode enum
  IDependencyInjectionVisitor.cs  — visitor interface: void Visit<T>(T target)
  ExecutionMode.cs                — enum { Sequential, Parallel }
  TaskStatus.cs                   — enum { Pending, Running, Finishing, Completed, Failed }

Editor/
  TaskTreePropertyDrawer.cs       — IMGUI PropertyDrawer + Odin OdinValueDrawer<TaskTree>
  TaskTreeSerializationBinder.cs  — Newtonsoft ISerializationBinder whitelist
  TaskRegistry.cs                 — scans assemblies for [DefineTask], caches entries
  SearchablePopup.cs              — EditorWindow popup with search field
```

## Type Hierarchy

```
ATask (abstract, [Serializable])
  ├── CompositeTask                         — has List<Child>, executionMode
  │     Child { bool enabled, float subTaskValue, [SerializeReference] ATask task }
  │
  └── [DefineTask("X")] ConcreteTask        — user-defined, override RunTheTask()

TaskTree { CompositeTask root }             — serializable entry point, not MonoBehaviour
```

All nodes in the tree are `ATask`. There is no separate "leaf node" wrapper — concrete tasks inherit `ATask` directly and override lifecycle methods. `CompositeTask` is also an `ATask` and can be nested arbitrarily.

`TaskTree` is a plain `[Serializable]` class embedded as a `[SerializeField]` in any MonoBehaviour or ScriptableObject.

## ATask — Serialized Fields & Properties

| Field/Property | Type | Serialized | Description |
|---|---|---|---|
| `name` | `string` | yes | Display name in editor hierarchy |
| `targetProgressToComplete` | `float` [0,1] | yes | Progress threshold for auto-ForceComplete (default 1.0) |
| `Progress` | `float` [0,1] | no (JsonIgnore) | Current progress. Setting triggers `ProgressChanged` event. If `Progress >= targetProgressToComplete` while Running → auto `ForceComplete()` |
| `Status` | `TaskStatus` | no (JsonIgnore) | Read-only. Set internally by execution flow |
| `ProgressChanged` | `event Action<ATask, float>` | no | Fires on progress change with delta |
| `Completed` | `event Action<ATask>` | no | Fires when task completes |

## ATask — Execution Flow

```
ExecuteAsync(externalCancellationToken):
  1. if Status == Completed → return early
  2. Status = Running
  3. OnBeginExecute()                    ← virtual, called in try/catch (exception logged, not fatal)
  4. create taskRunningCts, taskFinishCts
  5. register externalCancellationToken → CancelAllCancellationTokenSources()
  6. await Try(RunTheTask(taskRunningCts.Token))
  7. Status = Finishing
  8. if !taskFinishCts.IsCancellationRequested → await Try(FinishTheTask(taskFinishCts.Token))
  9. OnCompleted()                        ← Status = Completed, Progress = 1, fire Completed event
  finally: dispose CTS registration + both CTS
```

The `Try()` wrapper catches `OperationCanceledException` (swallow) and other exceptions (log + set Status = Failed).

### ForceComplete Paths

| From Status | `ForceComplete()` | `ForceComplete(immediate: true)` |
|---|---|---|
| Pending | → `OnCompleted()` directly | → `OnCompleted()` directly |
| Running | cancel taskRunningCts → FinishTheTask runs → OnCompleted | cancel both CTS → OnCompleted |
| Finishing | cancel taskFinishCts → OnCompleted | cancel taskFinishCts → OnCompleted |
| Completed/Failed | no-op | no-op |

### Progress Auto-Complete Trigger

When `Progress` setter is called while `Status == Running` and `targetProgressToComplete < 1.0`:
if `Progress >= targetProgressToComplete` → calls `ForceComplete()`. Guard: skipped when `targetProgressToComplete ≈ 1.0`.

## ATask — Virtual Methods (Override Points)

| Method | Signature | Default | When to override |
|---|---|---|---|
| `RunTheTask` | `protected abstract UniTask RunTheTask(CancellationToken ct)` | abstract | **Required.** Main task logic |
| `FinishTheTask` | `protected virtual UniTask FinishTheTask(CancellationToken ct)` | returns CompletedTask | Async cleanup after Running ends |
| `OnBeginExecute` | `protected internal virtual void OnBeginExecute()` | no-op | Setup before RunTheTask (e.g., disable touch detectors) |
| `OnCompleted` | `protected virtual void OnCompleted()` | Set Status=Completed, Progress=1, fire event | Final cleanup. **Must call `base.OnCompleted()`** |
| `Dispose` | `public virtual void Dispose()` | calls `Reset()` | Resource cleanup on tree dispose. **Must call `base.Dispose()`** |
| `Reset` | `public virtual void Reset()` | Cancel CTS, null events, Status=Pending | Rarely overridden |
| `Accept` | `public virtual void Accept(IDependencyInjectionVisitor v)` | no-op | DI opt-in. Override to call `v.Visit(this)` |
| `CancelAllCancellationTokenSources` | `protected virtual void CancelAllCancellationTokenSources()` | Cancel both CTS | Custom cancel logic |

## CompositeTask — Behavior

`CompositeTask : ATask` with `List<Child> children` and `ExecutionMode executionMode`.

**Accept:** Propagates to all children (regardless of `enabled`).

**OnBeginExecute:** Propagates only to children with `enabled == true`.

**RunTheTask:**
- Sequential: `await ExecuteChildTask()` for each enabled child in order.
- Parallel: fire-and-forget `ExecuteChildTask()` for all enabled, then `WaitUntil(IsAllChildTasksCompleted)`.

**FinishTheTask:** If all enabled children are Completed/Failed → return immediately. Otherwise wait.

**Progress propagation:** Each child's progress delta is weighted by `child.subTaskValue / totalEnabledSubTaskValueSum`. Total doesn't need to equal 1 — it normalizes.

**InsertChild(index, child):** Runtime insertion. If Running+Parallel → immediately execute the new child. Recalculates progress scale.

## TaskTree — Entry Point

```csharp
// Embed in MonoBehaviour/ScriptableObject:
[SerializeField] TaskTree taskTree;

// DI (before execute):
taskTree.Accept(visitor);

// Execute:
var cts = taskTree.Execute();          // creates internal CTS, returns it
taskTree.Execute(cancellationToken);   // uses external token

// Cleanup:
taskTree.Dispose();
```

## Creating a Concrete Task

```csharp
[Serializable]
[DefineTask("Wait For Sec")]
public class WaitTimeTask : ATask
{
    [SerializeField] float duration = 1f;

    protected override async UniTask RunTheTask(CancellationToken ct)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            ct.ThrowIfCancellationRequested();
            elapsed += Time.deltaTime;
            Progress = elapsed / duration;
            await UniTask.Yield(ct);
        }
    }
}
```

Requirements:
- `[Serializable]` — Unity serializes via `[SerializeReference]` on `Child.task`
- `[DefineTask("Display Name")]` — registers in editor dropdown via `TaskRegistry`
- Override `RunTheTask(ct)` — the only abstract method

## [DefineTask] Attribute

```csharp
[DefineTask("Display Name", Description = "optional", BindingMode = TypeSerializationBindingMode.ByDisplayName)]
```

`BindingMode` controls JSON serialization binder name:
- `ByDisplayName` (default) — uses `DisplayName`
- `ByTypeName` — uses `type.Name`
- `ByTypeFullName` — uses `type.FullName`

## Dependency Injection

Opt-in via `Accept` override. No separate interface needed.

```csharp
[Serializable, DefineTask("Click")]
public class ClickTask : ATask
{
    public Camera CameraForInput { get; set; }  // injected, [NonSerialized]

    public override void Accept(IDependencyInjectionVisitor v) => v.Visit(this);
    // ...
}
```

Visitor implementation:
```csharp
public class GameVisitor : IDependencyInjectionVisitor
{
    readonly Camera _cam;
    public GameVisitor(Camera cam) => _cam = cam;
    public void Visit<T>(T target)
    {
        if (target is ClickTask click) click.CameraForInput = _cam;
    }
}
```

`CompositeTask.Accept` propagates to all children. Tasks that don't override `Accept` → silently skipped.

## Editor — TaskTreePropertyDrawer

Renders inline in Unity Inspector. Two implementations in the same file:
- `TaskTreePropertyDrawer : PropertyDrawer` — standard IMGUI
- `TaskTreeOdinDrawer : OdinValueDrawer<TaskTree>` — when `ODIN_INSPECTOR` defined

### Sections

1. **Import/Export** — JSON export/import via `TaskTreeSerializationBinder` (whitelist: only `[DefineTask]` types + `CompositeTask`)
2. **Hierarchy** — Tree view with: expand/collapse, drag-drop reorder, inline rename (F2), search filter, multi-select (Ctrl+click, Shift+click). Focus/unfocus state (keyboard only active when focused). Resizable height via bottom drag handle.
3. **Inspector** — For selected node: [Enabled toggle + Name] → Task Type dropdown → SubTaskValue → remaining serialized fields. Root node: no type dropdown (always CompositeTask). Task type dropdown includes: Composite (Sequential), Composite (Parallel), all `[DefineTask]` entries. "+ Add Child" button for CompositeTask nodes opens type picker popup.
4. **Runtime** (Play Mode only) — Status dot colors: Running/Finishing=cyan, Completed=green, Failed=red, Pending=gray. Progress bar. ForceComplete / ForceImmediate / Reset buttons.

### Keyboard Shortcuts (when hierarchy focused)

| Key | Action |
|---|---|
| ↑ / ↓ | Navigate selection (auto-scrolls) |
| ← | Collapse if expanded, else jump to parent |
| → | Expand if collapsed, else jump to next sibling |
| F2 | Rename selected |
| Delete | Delete selected |
| Ctrl+D | Duplicate |
| Ctrl+C / Ctrl+V | Copy / Paste as children |
| Alt+← / Alt+→ | Collapse all / Expand all |

## Design Decisions

- **No separate leaf wrapper.** All tasks inherit `ATask` directly. `CompositeTask` is also an `ATask`. The editor unified type dropdown lets you convert any child between concrete and composite types freely.
- **Progress auto-complete.** Setting `targetProgressToComplete < 1` on a CompositeTask enables "complete when X% done" pattern without custom logic.
- **Two-phase cancellation.** Separate CTS for Running and Finishing phases allows `ForceComplete()` to cancel Running but still run Finishing (cleanup), while `ForceComplete(immediate: true)` skips both.
- **Visitor DI.** `Accept(visitor)` is a no-op by default. Only tasks that need injection override it. No marker interface required.
- **CompositeTask.OnBeginExecute skips disabled children.** Prevents disabled tasks from side-effecting shared objects (e.g., disabling a touch detector that an enabled sibling needs).
