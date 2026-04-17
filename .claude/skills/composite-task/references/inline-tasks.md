# Inline Tasks (IInlineTask)

For adding logic to a tree at **runtime** without creating a MonoBehaviour. Useful for ad-hoc callbacks, test scaffolding, or logic that belongs next to calling code.

## Interface

```csharp
public interface IInlineTask
{
    UniTask OnRunning(CancellationToken ct);
    void OnWarm() { }
    void OnReset() { }
    void OnCompleted() { }
    UniTask OnFinishing(CancellationToken ct) => UniTask.CompletedTask;
}
```

Only `OnRunning` is required. Others have default no-ops.

## Insertion

Extension methods on `CompositeNode` (both Sequential and Parallel):

```csharp
// Lambda (most common):
sequentialNode.InsertInline(async ct =>
{
    Debug.Log("Running inline");
    await UniTask.Delay(500, cancellationToken: ct);
});

// Full lifecycle:
public class MyInlineLogic : IInlineTask
{
    public async UniTask OnRunning(CancellationToken ct)
    {
        await DoThing(ct);
    }
    public void OnCompleted() => Debug.Log("Done!");
}

sequentialNode.InsertInline(new MyInlineLogic());
```

## How it works internally

`InsertInline` creates a child GameObject with an `InlineTaskNode` (internal TaskNode subclass) that wraps your `IInlineTask`. The wrapper forwards lifecycle calls. No `[DefineTaskNode]` needed (not discoverable via registry).

## When to use

- **Test fixtures** — inject timing callbacks, assertion checkpoints without polluting runtime with MonoBehaviours.
- **Hot-patch a tree** — runtime logic changes (e.g. a debug menu button inserts a step).
- **One-off callbacks** — "after this sequence, log something" — simpler than InvokeUnityEventNode + wiring.
- **Glue between tree and calling code** — the lambda captures local variables directly.

## When NOT to use

- **Reusable gameplay logic** — make a proper TaskNode (discoverable, Inspector-friendly, JSON-serializable).
- **Designer-authored content** — inline tasks aren't in JSON, Inspector, or presets.
- **Cross-tree logic** — dependency on captured variables is fragile; prefer DI + typed TaskNodes.

## Insertion position

`InsertInline` adds the wrapper as the LAST child of the composite. For insertion at a specific index, manipulate hierarchy directly:

```csharp
var inlineGo = composite.InsertInline(myTask);
inlineGo.transform.SetSiblingIndex(2);  // put at position 2
```

## Lifecycle interaction

The `InlineTaskNode` respects all TaskNode lifecycle rules:
- `Warm()` → calls `IInlineTask.OnWarm()`
- `OnRunning(ct)` → calls `IInlineTask.OnRunning(ct)`
- etc.

So the sync-in-OnCompleted rule applies — put always-fire side-effects in `IInlineTask.OnCompleted`, not `OnRunning`.

## Cancellation

The `CancellationToken` passed to `OnRunning` is linked to the tree's execution CTS. Respect it in every await:

```csharp
composite.InsertInline(async ct =>
{
    for (int i = 0; i < 10; i++)
    {
        ct.ThrowIfCancellationRequested();
        await UniTask.Delay(100, cancellationToken: ct);
    }
});
```

## Gotchas

- **Captured `this` gotcha** — inline lambda captures outer instance. If outer is destroyed but tree keeps running, the closure may reference a dead object. Prefer capturing specific values, not whole `this`.
- **Cannot be serialized** — inline nodes disappear on domain reload, cannot be saved to JSON blueprint.
- **Hierarchy noise** — rapidly inserting many inline nodes creates GO clutter. Consider batching or using a proper TaskNode.
