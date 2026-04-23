# Creating a New TaskNode

## Step 1: Choose the base class

### Use `TaskNode` (no generic) when:
The task has **no configurable data** — all behaviour comes from `[SerializeField]` Unity refs assigned in Editor, or from constants inside the class.

Example use cases:
- Stop an AudioSource
- Destroy a GameObject (with no parameters)
- Fire a UnityEvent
- Force-complete another node
- Any task where every knob is an object reference, not a value

```csharp
[DefineTaskNode("Stop Sound", Category = "Audio", Description = "Stops audio. Ref: source (AudioSource).")]
public class StopSoundNode : TaskNode
{
    [SerializeField] private AudioSource source;

    protected override UniTask OnRunning(CancellationToken ct) => UniTask.CompletedTask;

    protected override void OnCompleted()
    {
        source.Stop();
        base.OnCompleted();
    }
}
```

### Use `TaskNode<TConfig>` when:
The task has **pure-data parameters** that can be tweaked per-instance without changing the class: duration, speed, enum mode, Vector3 value, etc.

Example use cases:
- Wait for N milliseconds (config: value, unit)
- Tween to a position (config: value, duration, ease)
- Play a sound in a specific mode (config: mode)
- Any task where the user might want two instances with different values

```csharp
[DefineTaskNode("Wait", Category = "Control", Description = "Pauses execution. Config: value, unit.")]
public class WaitNode : TaskNode<WaitNode.Settings>
{
    protected override async UniTask OnRunning(Settings config, CancellationToken ct)
    {
        switch (config.unit)
        {
            case WaitUnit.Frames:        await UniTask.DelayFrame(config.value, cancellationToken: ct); break;
            case WaitUnit.Milliseconds:  await UniTask.Delay(config.value, cancellationToken: ct); break;
            // ...
        }
    }

    [Serializable]
    public class Settings
    {
        public int value = 1000;
        public WaitUnit unit = WaitUnit.Milliseconds;
    }
}
```

### The test: "Would I want two instances with different values?"
- YES → `TaskNode<T>` with Settings
- NO, behaviour is identical every time → `TaskNode`

### Mixed case: both Unity refs AND config data
Use `TaskNode<T>`, put refs on the class, pure data in Settings:

```csharp
public class PlaySoundNode : TaskNode<PlaySoundNode.Settings>
{
    [SerializeField] private AudioSource source;  // ← Unity ref on class
    [SerializeField] private AudioClip clip;      // ← Unity ref on class

    protected override async UniTask OnRunning(Settings config, CancellationToken ct) { ... }

    [Serializable]
    public class Settings
    {
        public TaskPlayMode mode = TaskPlayMode.OnceAndWait;  // ← pure data in Settings
    }
}
```

**Never put `Transform`, `GameObject`, `AudioSource`, `UnityEvent`, or any other UnityEngine.Object reference inside the Settings class.** JSON serialization cannot round-trip Unity refs.

### Accessing the config: use the `Config` property

`TaskNode<TConfig>` exposes a `public TConfig Config { get; set; }` property. Inside the class (especially in `OnCompleted`, `OnReset`, `OnFinishing`), read config values via `Config.fieldName` directly — **do not cache** the `config` parameter from `OnRunning` into a private field; that's redundant duplication.

```csharp
// GOOD — read from the Config property directly
protected override void OnCompleted()
{
    Debug.Log(Config.message);   // property is always available
    base.OnCompleted();
}

// UNNECESSARY — don't cache it
private Settings _cachedConfig;
protected override UniTask OnRunning(Settings config, CancellationToken ct)
{
    _cachedConfig = config;      // redundant — Config already holds this
    return UniTask.CompletedTask;
}
```

The `config` parameter in `OnRunning(TConfig config, CancellationToken ct)` is the same reference as the `Config` property, passed for convenience inside OnRunning only.

### Name the nested class carefully — avoid `Config`

The nested data class is conventionally called `Settings` in this package (e.g. `WaitNode.Settings`, `PlaySoundNode.Settings`). **Do not name the nested class `Config`** — it will shadow/collide with the inherited `Config` property on `TaskNode<TConfig>` and break compilation.

Safe names: `Settings`, `Data`, `Params`, `Options`. Unsafe: `Config` (property name collision).

## Step 2: Choose override points

All `TaskNode<T>` subclasses MUST override `OnRunning(TConfig, CancellationToken)`.
All `TaskNode` subclasses MUST override `OnRunning(CancellationToken)`.

| Method | Signature | Purpose |
|--------|-----------|---------|
| `OnWarm()` | `protected virtual void OnWarm()` | One-time init before first Execute. Cache, subscribe, prepare. |
| `OnRunning(...)` | abstract, returns UniTask | Main async work. Can await, can be cancelled. |
| `OnFinishing(ct)` | `protected virtual UniTask OnFinishing(CancellationToken ct)` | Cleanup after OnRunning. Still cancellable. |
| `OnCompleted()` | `protected virtual void OnCompleted()` | Sync finalization. ALWAYS call `base.OnCompleted()`. |
| `OnReset()` | `protected virtual void OnReset()` | Revert state on ResetTask. |
| `Dispose()` | `public virtual void Dispose()` | Full teardown (default: calls ResetTask). |

### Critical rule: Sync-only logic goes in `OnCompleted`

`OnRunning` can be force-completed/cancelled mid-execution. If your logic is a **one-shot synchronous side-effect** that must always fire (e.g., "set this flag", "destroy this GO"), put it in `OnCompleted`:

```csharp
// WRONG: side effect may be skipped if force-completed during OnRunning
protected override UniTask OnRunning(CancellationToken ct)
{
    target.SetActive(active);  // ← could be skipped
    return UniTask.CompletedTask;
}

// RIGHT: OnCompleted always fires (unless task Failed)
protected override UniTask OnRunning(CancellationToken ct) => UniTask.CompletedTask;

protected override void OnCompleted()
{
    target.SetActive(active);
    base.OnCompleted();
}
```

Existing nodes that follow this pattern: `SetTransformNode`, `SetGameObjectActivationNode`, `SetComponentEnabledNode`, `DestroyNode`, `InvokeUnityEventNode`, `StopSoundNode`, `ForceCompleteNode`, `SetProgressNode`.

### When to use `OnRunning` vs `OnCompleted`

| Logic type | Where |
|------------|-------|
| `await UniTask.Delay`, `await SomeAsync()` | OnRunning |
| Per-frame work, tweens with DOTween `.ToUniTask(cancellationToken: ct)` | OnRunning |
| Wait until condition (UniTask.WaitUntil) | OnRunning |
| One-shot sync side-effect (set, destroy, invoke) | OnCompleted |
| Cleanup that must always run even on force-complete | OnCompleted or OnFinishing |
| One-time init that should only run once | OnWarm |
| State reset on ResetTask | OnReset |

## Step 3: Always call `base.OnCompleted()`

```csharp
protected override void OnCompleted()
{
    DoMyStuff();
    base.OnCompleted();  // ← required, sets Status = Completed + Progress = 1
}
```

Skipping `base.OnCompleted()` means the tree thinks your task never finished. The parent composite will hang.

## Step 4: Place the file

Convention:
- Package-internal nodes → under the package's `Runtime/Nodes/{Category}/` folder
- Project-specific nodes → under your project's Scripts folder, grouped by feature (ask the user where they want it — don't assume)

File name = PascalCase class name, `.cs`.

## Step 5: Use the generator

The skill ships `scripts/new-task-node.py` for deterministic boilerplate:

```bash
python .claude/skills/composite-task/scripts/new-task-node.py \
    --name MyTaskNode \
    --display-name "My Task" \
    --category "Gameplay" \
    --description "Does X. Ref: target (Transform)." \
    --base TaskNode \
    --namespace MyProject.Features.Combat \
    --path "<path chosen by the user>/MyTaskNode.cs"
```

For `TaskNode<T>`:
```bash
python .claude/skills/composite-task/scripts/new-task-node.py \
    --name MyConfigurableNode \
    --display-name "My Configurable Task" \
    --category "Gameplay" \
    --description "..." \
    --base TaskNodeT \
    --config-fields "duration:float=1.0;mode:TaskPlayMode=OnceAndWait" \
    --refs "target:Transform" \
    --namespace MyProject.Features.Combat \
    --path "<path chosen by the user>/MyConfigurableNode.cs"
```

**Ask the user for the target path and namespace** — don't assume the project's folder layout or root namespace. The generator tries to derive a namespace from directory segments under `Scripts/`, `0_Scripts/`, or `src/`, but any root prefix (company, product) must come from the user via `--namespace`.

The generator:
- Enforces the right base class signature
- Puts refs on class, pure data in nested Settings
- Emits a `TODO:` stub in `OnRunning`
- Reminds about `base.OnCompleted()` when needed
- Adds the `[DefineTaskNode]` attribute correctly

If the file exists, it aborts unless `--overwrite` is passed.

## Anti-Patterns (never do these)

❌ Object refs in Settings class:
```csharp
[Serializable]
public class Settings { public Transform target; }  // ← WRONG
```

❌ `public` fields:
```csharp
public AudioSource source;  // ← use [SerializeField] private
```

❌ Skipping `base.OnCompleted()`:
```csharp
protected override void OnCompleted() { DoThing(); /* missing base */ }
```

❌ Using `async void OnRunning`:
```csharp
protected override async void OnRunning(CancellationToken ct)  // ← wrong signature, use async UniTask
```

❌ Allocating in hot paths (UniTask.Delay loop that `new`s each frame, string concat, LINQ).

❌ `GetComponent<T>()` in `OnRunning` — cache in `OnWarm` or `[SerializeField]`.

❌ Making `Awake()`/`Start()` do task-related work — use `OnWarm()` instead. Unity lifecycle runs separately from task lifecycle; `Awake` happens when GameObject spawns, but `OnWarm` happens before first task Execute.

## Validation (editor)

Override `GetValidationWarnings(List<string>)` to report missing refs in editor:

```csharp
public override void GetValidationWarnings(List<string> warnings)
{
    if (target == null) warnings.Add("Missing target (Transform)");
    if (source == null) warnings.Add("Missing source (AudioSource)");
}
```

Warnings appear in the Inspector above the node fields.

## DI (dependency injection)

If the task needs a runtime-injected reference (camera, service), override `ResolveFrom` and pull from the context. Always call `base.ResolveFrom(locator)` first.

```csharp
using Hlight.Structures.CompositeTask.Runtime;

public class ShakeCameraNode : TaskNode<ShakeCameraNode.Settings>
{
    private Camera _camera;

    public override void ResolveFrom(IServiceLocator locator)
    {
        base.ResolveFrom(locator);
        if (!locator.GetProvider<Camera>(this).TryProvide(out _camera))
            throw new System.Collections.Generic.KeyNotFoundException("Camera provider missing");
    }
    // ...
}
```

The tree applies the context once (typically after build, before Execute):

```csharp
taskTree.ResolveFrom(myLocator);
```

See `di-resolve.md` for propagation details, optional deps, and timing rules.
