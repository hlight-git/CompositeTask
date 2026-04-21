# Dependency Injection (pull-model)

Inject shared runtime dependencies (camera, services, managers) into TaskNodes using a pull-model DI pattern.

The package defines its own minimal `IDependencyContext` interface (`Hlight.Structures.CompositeTask.Runtime.IDependencyContext`) so the submodule has **no hard dependency on any specific DI package**. Any project-level DI context that exposes the same method shape can satisfy it directly — just add the interface to the context's interface list. The submodule ships `Resolve<T>()` as an extension method that throws on miss (`DependencyContextExtensions`).

## Mental model

Each TaskNode overrides `ResolveDependencies(IDependencyContext context)` and pulls what it needs via `context.Resolve<T>()` or `context.TryResolve(out T)`. The tree walks itself — `CompositeNode` propagates to children, `ConditionalNode` to branches, `RunSubTreeNode` to the sub-tree.

No visitor, no marker interface, no reflection. Plain virtual method overrides.

## The contract

```csharp
public virtual void ResolveDependencies(IDependencyContext context) { }
```

Declared on `TaskNode`. Default: no-op. Override in subclasses that need deps. ALWAYS call `base.ResolveDependencies(context)` first — some base classes (CompositeNode, ConditionalNode, RunSubTreeNode) propagate through the base call.

## Consumer side (a TaskNode needing DI)

```csharp
public class ShakeCameraNode : TaskNode<ShakeCameraNode.Settings>
{
    private Camera _camera;
    private IAudioService _audio;

    public override void ResolveDependencies(IDependencyContext context)
    {
        base.ResolveDependencies(context);
        _camera = context.Resolve<Camera>();                   // required — throws if missing
        context.TryResolve(out _audio);                        // optional — null if missing
    }

    protected override async UniTask OnRunning(Settings config, CancellationToken ct)
    {
        // use _camera, _audio ...
    }

    [Serializable] public class Settings { /* pure data */ }
}
```

## Producer side — bridging from a project DI context

The Composite Task `IDependencyContext` is just:

```csharp
namespace Hlight.Structures.CompositeTask.Runtime
{
    public interface IDependencyContext
    {
        bool TryResolve<T>(out T value, string id = null) where T : class;
    }
}
```

If your project already has its own DI context with the exact same `TryResolve<T>(out, id)` signature, **add the interface to the class's interface list** — no method changes needed. Example from this project:

```csharp
public partial class GameplayContext :
    ASceneContext,
    Apero.Unity.Architecture.DependencyInjection.IDependencyContext,   // existing
    Hlight.Structures.CompositeTask.Runtime.IDependencyContext,        // added
    IDependencyProvider<Camera>,
    // ... providers ...
{
    public bool TryResolve<T>(out T value, string id = null) where T : class
    {
        // same body satisfies both interfaces
    }
}
```

If signatures don't match (e.g. different id type, different generic constraint), write a thin adapter class that delegates.

## Applying the context to a tree

```csharp
// After building / loading the tree, before Execute():
taskTree.ResolveDependencies(myContext);
taskTree.Execute();
```

Typical order in a level-loading flow:

```csharp
tree.LoadFromJson(json);             // build hierarchy
tree.ResolveDependencies(myContext); // inject
tree.Warm();                         // one-time per-node init
tree.Execute();                      // run
```

`myContext` can be a scene-level context, a singleton, or a wrapper — anything that implements the interface.

## Propagation under the hood

- `TaskNode.ResolveDependencies` — default no-op; leaf overrides do the pulling.
- `CompositeNode.ResolveDependencies` — calls `base` (which pulls the composite's own deps, if any), then iterates every `TaskNode` child and calls `child.ResolveDependencies(context)`.
- `ConditionalNode.ResolveDependencies` — calls `base`, then iterates every branch and calls `branch.node.ResolveDependencies(context)`. All branches get injected, not just the one that will execute.
- `RunSubTreeNode.ResolveDependencies` — calls `base`, then `_subTree.ResolveDependencies(context)` — propagates across sub-tree boundaries.

Single call on the root reaches every node in the tree, including sub-trees.

## When to call

| Phase | Safe to call `ResolveDependencies`? |
|-------|-------------------------------------|
| Immediately after `AddComponent<TaskTree>` / before hierarchy exists | No — tree has no children to inject. |
| After tree is built in Editor (prefab scene) | Yes, but only if contexts are already registered (rare in Edit Mode). |
| After `LoadFromJson` / manual scene build, before `Execute` | **Yes — canonical timing.** |
| After `ResetTree()` | Yes — state is cleared, re-inject before next Execute. |
| Inside `OnRunning` / async flow | No — dependencies should already be resolved. |

## Multiple passes

```csharp
tree.ResolveDependencies(sceneContext);          // scene-scoped services
tree.ResolveDependencies(CompositeContext.Instance); // global services
```

Each pass calls every node's `ResolveDependencies` with its own context. Idempotent — a node that already has its deps can either skip (check for null) or overwrite.

## Optional dependencies

```csharp
public override void ResolveDependencies(IDependencyContext context)
{
    base.ResolveDependencies(context);
    _camera = context.Resolve<Camera>();           // required
    if (context.TryResolve(out IAnalytics a))      // optional
        _analytics = a;
    // else: _analytics stays null
}
```

Rule: `Resolve` for required deps (fast-fail surfaces bugs early). `TryResolve` for genuinely optional ones.

## Anti-patterns

- ❌ Forgetting `base.ResolveDependencies(context)` — breaks propagation on `CompositeNode`, `ConditionalNode`, `RunSubTreeNode`. Always call base first.
- ❌ `FindObjectOfType` inside `OnWarm` / `OnRunning` — defeats DI. Pull via context.
- ❌ Storing `IDependencyContext` as a field and re-using later — contexts can die/change. Store the resolved T, not the context.
- ❌ Calling `ResolveDependencies` after `Execute` has started — injects into a partially-running tree; undefined behaviour for nodes mid-async.
- ❌ Putting resolved refs in the `Settings` nested class — Settings is pure data, serialized to JSON. Resolved refs live on the class as private fields.

## Assumption check

If `Resolve<T>()` throws `KeyNotFoundException`, the caller didn't register a `IDependencyProvider<T>` with the context. Verify the owning MonoBehaviour:
- Ran `OnEnable` (registered) before the tree called `ResolveDependencies`.
- Implements `IDependencyProvider<T>` and `TryProvide` returns `true` for the requested `id`.
- Is still registered — `OnDisable` unregisters it.

See the DI package's skill/docs for context-side debugging.
