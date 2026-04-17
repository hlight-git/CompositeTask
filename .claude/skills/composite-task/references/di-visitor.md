# Dependency Injection (Visitor pattern)

Inject shared runtime dependencies (camera, services, managers) into TaskNodes without making them MonoBehaviour-accessible or resorting to singletons.

## Why visitor, not constructor?

TaskNodes are MonoBehaviours — Unity constructs them. Constructor DI isn't available. The visitor pattern lets you walk the tree at a specific moment (usually after scene load, before execute) and fill dependencies where each node declares it needs them.

## Contract

```csharp
public interface IDependencyInjectionVisitor
{
    void Visit<T>(T target) where T : class;
}
```

The visitor's `Visit<T>` is called for each node; use pattern-matching / `is` to target the right types:

```csharp
public class GameplayVisitor : IDependencyInjectionVisitor
{
    private readonly Camera _camera;
    private readonly IAudioService _audio;

    public GameplayVisitor(Camera camera, IAudioService audio)
    {
        _camera = camera;
        _audio = audio;
    }

    public void Visit<T>(T target) where T : class
    {
        if (target is IUsesCamera uc) uc.Camera = _camera;
        if (target is IUsesAudio ua)  ua.Audio  = _audio;
    }
}
```

## Node side: expose dependencies

On your TaskNode, define what it needs:

```csharp
public class ShakeCameraTaskNode : TaskNode<ShakeCameraTaskNode.Settings>, IUsesCamera
{
    public Camera Camera { get; set; }  // ← filled by visitor

    protected override UniTask OnRunning(Settings config, CancellationToken ct)
    {
        return DOTween.Shake(Camera.transform, ...).ToUniTask(cancellationToken: ct);
    }
    ...
}
```

Mark the interface:
```csharp
public interface IUsesCamera { Camera Camera { get; set; } }
```

## Applying the visitor

```csharp
var visitor = new GameplayVisitor(Camera.main, AudioService.Instance);
taskTree.Accept(visitor);  // walks entire tree, calls Visit on every node
```

`TaskTree.Accept` calls `Root.Accept(visitor)` which propagates down via `CompositeNode.Accept` → children.

## The default Accept

```csharp
public virtual void Accept(IDependencyInjectionVisitor v) => v.Visit(this);
```

Called once per node. Override only if you need custom traversal or additional DI points.

## When to call Accept

- Right after building the tree (via `LoadFromJson` or manual construction).
- Before the first `Execute()`.
- Again after `ResetTree()` if visitor state may have changed.

Composition order:
```
tree.LoadFromJson(json);
tree.Accept(visitor);
tree.Execute();
```

## Propagation detail

- `CompositeNode.Accept` → `v.Visit(this)` then each child's `Accept`.
- `ConditionalNode.Accept` → `v.Visit(this)` then each branch's `node.Accept`.
- `RunSubTreeNode.Accept` → `v.Visit(this)` then `_subTree.Accept(v)`.

So a single `tree.Accept(visitor)` visits every TaskNode in the whole tree, including sub-trees.

## Multiple visitors

Totally fine. Run multiple passes with different visitor implementations:

```csharp
tree.Accept(new CameraVisitor(cam));
tree.Accept(new AudioVisitor(audioSvc));
tree.Accept(new LocalizationVisitor(locSvc));
```

Or combine into one `CompositeVisitor` that dispatches.

## Patterns

### Interface markers (recommended)

```csharp
interface IUsesCamera { Camera Camera { get; set; } }
interface IUsesAudio  { IAudioService Audio { get; set; } }
```

Pros: typed, obvious, multi-dep-friendly.

### Reflection (for ad-hoc)

```csharp
public void Visit<T>(T target) where T : class
{
    foreach (var prop in target.GetType().GetProperties())
    {
        if (prop.PropertyType == typeof(Camera) && prop.GetValue(target) == null)
            prop.SetValue(target, _camera);
    }
}
```

Slower, less explicit, but no interface boilerplate.

### Per-project registry

A `GameplayDI` MonoBehaviour on a bootstrap scene that aggregates services and exposes a `Visitor` property. Any TaskTree in the level calls `GameplayDI.Visitor.Apply(tree)`.

## Anti-patterns

❌ Stored refs in Settings class (Settings = pure data only).
❌ Using `FindObjectOfType` in `OnWarm` — defeats the whole point of DI.
❌ Assuming visitor ran — always null-check `Camera` etc. in `OnRunning` and log warn if missing.

## Debugging

Log-based discovery: make the visitor log each visited node type during Accept. If a node expected injection but didn't receive it, either the marker interface is missing or the visitor's Visit doesn't handle that type.
