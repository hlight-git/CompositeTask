# TaskNode Lifecycle

## States

```
Pending → Running → Finishing → Completed
              ↓          ↓
            Failed     Failed
```

| State | Meaning |
|-------|---------|
| `Pending` | Created, not yet executed. Ready for `ExecuteAsync()`. |
| `Running` | Inside `OnRunning(ct)`. Cancellable. |
| `Finishing` | Inside `OnFinishing(ct)`. Cancellable. |
| `Completed` | Finished successfully. `Progress=1`. |
| `Failed` | Exception thrown during Running or Finishing. Logged via `Debug.LogException`. |

## Execution flow (single task)

```
ExecuteAsync(externalCt)
│
├── SetStatus(Running)
├── _taskRunningCts = new CTS
├── _taskFinishCts  = new CTS
├── register externalCt → cancel all CTS
│
├── Try { await OnRunning(_taskRunningCts.Token) }
│     → catch OperationCanceledException (swallow)
│     → catch Exception → SetStatus(Failed), log
│
├── SetStatus(Finishing)
├── Try { await OnFinishing(_taskFinishCts.Token) }  (only if not Failed)
│
├── OnCompleted()  (only if not Failed AND not externally cancelled AND CTS still alive)
│     → SetStatus(Completed), Progress = 1
│
└── finally {
      _taskRunningCts?.Dispose(); _taskFinishCts?.Dispose()
    }
```

## Override points

### `OnWarm()` — one-time init

```csharp
protected virtual void OnWarm() { }
```

- Called once per task lifetime, before first `ExecuteAsync` (via public `Warm()`).
- After `ResetTask`, `_warmed = false` → next Execute re-warms.
- Use for: caching components, subscribing to events, pooling.
- NOT called during construction / `Awake` / `Start`.

### `OnRunning(ct)` — abstract, main async work

```csharp
protected abstract UniTask OnRunning(CancellationToken ct);

// Or for TaskNode<T>:
protected abstract UniTask OnRunning(TConfig config, CancellationToken ct);
```

- Every concrete leaf MUST implement.
- `ct` is the internal `_taskRunningCts.Token`, linked to external cancellation.
- Respect the token: pass it to every async call (`UniTask.Delay(ct: ct)`).
- Throwing `OperationCanceledException` via the token is fine (swallowed).
- Any other exception → task moves to Failed state.

### `OnFinishing(ct)` — post-run cleanup

```csharp
protected virtual UniTask OnFinishing(CancellationToken ct) => UniTask.CompletedTask;
```

- Runs between OnRunning and OnCompleted.
- Still cancellable via `_taskFinishCts`.
- Use for: final flush, cleanup that needs to await.
- Rarely needed. Most tasks use only OnRunning.

### `OnCompleted()` — sync finalization

```csharp
protected virtual void OnCompleted()
{
    SetStatus(Completed);
    Progress = 1f;
}
```

- Synchronous — no await.
- **MUST call `base.OnCompleted()`** if overridden.
- Runs ONLY if:
  - Status != Failed, AND
  - external CT not cancelled, AND
  - internal CTS still alive (not force-disposed)
- For sync side-effects that must fire, put them here BEFORE `base.OnCompleted()`.

**Why OnCompleted and not OnRunning?** If someone calls `ForceComplete(immediate: true)`, both CTSes cancel and OnRunning/OnFinishing are skipped. OnCompleted still fires (unless externally cancelled). This is the **guaranteed side-effect** slot.

### `OnReset()` — on ResetTask

```csharp
protected virtual void OnReset() { }
```

- Called from `ResetTask()` before events are cleared.
- Use to: restore captured state (e.g. TimeScale), kill tweens, release handles.
- After OnReset, Status → Pending, Progress → 0, _warmed → false.

### `Dispose()` — full teardown

```csharp
public virtual void Dispose() => ResetTask();
```

- Default: calls ResetTask.
- Override if you need extra disposal (e.g., unsubscribe global events, release pooled objects).
- `TaskTree.OnDestroy` calls `Dispose`.

## Composite node lifecycle extras

`CompositeNode` and subclasses (Sequential/Parallel) also participate:

- `OnWarm` → recursively warms all children.
- `OnRunning` → runs children per mode (sequential one-by-one, parallel concurrently).
- `OnReset` → recursively resets children.
- `Dispose` → recursively disposes children.

**Children are the direct Transform children** that have a `TaskNode` component and are `activeInHierarchy`.

## Progress

```csharp
public float Progress
{
    get => _progress;
    protected set
    {
        var clamped = Mathf.Clamp01(value);
        var delta = clamped - _progress;
        if (Mathf.Approximately(delta, 0f)) return;
        _progress = clamped;
        ProgressChanged?.Invoke(this, delta);
        Parent?.OnChildProgressChanged(this, delta);

        if (Status == Running && _targetProgressToComplete is not 0 nor 1
            && _progress >= _targetProgressToComplete) ForceComplete();
    }
}
```

- Setting `Progress` inside your `OnRunning` fires events + bubbles up to parent.
- If `_targetProgressToComplete` (serialized `[Range(0,1)]`) is less than 1, hitting that threshold triggers `ForceComplete`.
- Default target = 1 → only reaches completion via OnCompleted.

## Force-completion

```csharp
public virtual void ForceComplete(bool immediate = false)
{
    switch (Status)
    {
        case Pending:   OnCompleted(); return;
        case Running:   _taskRunningCts?.Cancel(); if (immediate) _taskFinishCts?.Cancel(); return;
        case Finishing: _taskFinishCts?.Cancel();  return;
    }
}
```

- From Pending → fire OnCompleted directly.
- From Running → cancel running CTS; OnFinishing runs unless `immediate=true`.
- From Finishing → cancel finishing CTS.

**Implication for override authors:** if you need cleanup on force-complete, put it in OnCompleted (sync-guaranteed) or OnFinishing (still runs unless immediate).

## The three common patterns

### Pattern A: pure async work

```csharp
public class WaitNode : TaskNode<WaitNode.Settings>
{
    protected override async UniTask OnRunning(Settings config, CancellationToken ct)
    {
        await UniTask.Delay(config.value, cancellationToken: ct);
    }
    // No OnCompleted override — base sets Completed + Progress=1.
}
```

### Pattern B: sync side-effect (always-fires)

```csharp
public class SetActiveNode : TaskNode<Settings>
{
    [SerializeField] private GameObject target;

    protected override UniTask OnRunning(Settings config, CancellationToken ct) => UniTask.CompletedTask;

    protected override void OnCompleted()
    {
        if (target != null) target.SetActive(config.active);
        base.OnCompleted();
    }
}
```

### Pattern C: async work + cleanup

```csharp
public class TweenPositionNode : TaskNode<Settings>
{
    private Tween _tween;

    protected override async UniTask OnRunning(Settings config, CancellationToken ct)
    {
        _tween = target.DOMove(config.value, config.duration).SetEase(config.ease);
        await _tween.ToUniTask(cancellationToken: ct);
    }

    protected override void OnCompleted()
    {
        _tween?.Kill();   // cleanup
        _tween = null;
        base.OnCompleted();
    }

    protected override void OnReset()
    {
        _tween?.Kill();
        _tween = null;
    }
}
```

## Common lifecycle bugs

| Bug | Cause | Fix |
|-----|-------|-----|
| Task never completes | Missing `base.OnCompleted()` | Add the call. |
| Side-effect didn't fire | Put in OnRunning + force-completed | Move to OnCompleted. |
| Cleanup not running on cancel | Put in OnCompleted but task Failed | Move to OnReset or OnFinishing. |
| Double-init | Using Awake/Start | Use OnWarm instead. |
| Stale refs after reset | Cached in OnWarm, not cleared in OnReset | Clear in OnReset or use `_warmed` flag. |
| CTS leak warning | Forgot to use `ct` passed in | Always pass the token to every async call. |
