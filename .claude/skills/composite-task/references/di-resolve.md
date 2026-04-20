# Dependency Injection (pull-model)

Inject shared runtime dependencies (camera, services, managers) into TaskNodes using the project's pull-model DI.

**Package**: `Apero.Unity.Architecture.DependencyInjection` (`Assets/Submodules/Dependency Injection Implementation/`)

## Mental model

Each TaskNode overrides `ResolveDependencies(IDependencyContext context)` and pulls what it needs via `context.Resolve<T>()` or `context.TryResolve(out T)`. The tree walks itself — `CompositeNode` propagates to children, `ConditionalNode` to branches, `RunSubTreeNode` to the sub-tree.

No visitor, no `IInjectable` marker interface, no reflection. Plain virtual method overrides.

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

## Producer side (a local context)

Define a `MonoBehaviour : IDependencyContext, IDependencyProvider<T1>, IDependencyProvider<T2>…` in your scene. Register in `OnEnable`, unregister in `OnDisable`. See the DI package's own docs for provider patterns — the Composite Task package is agnostic to how the context is assembled.

## Applying the context to a tree

```csharp
// After building / loading the tree, before Execute():
taskTree.ResolveDependencies(CompositeContext.Instance);
taskTree.Execute();
```

Typical order in a level-loading flow:

```csharp
tree.LoadFromJson(json);                              // build hierarchy
tree.ResolveDependencies(CompositeContext.Instance);  // inject
tree.Warm();                                          // one-time per-node init
tree.Execute();                                       // run
```

`CompositeContext.Instance` is the singleton aggregator from the DI package. Any scoped / project-local context that implements `IDependencyContext` works too.

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
