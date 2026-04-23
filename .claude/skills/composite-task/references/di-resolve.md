# Dependency Injection (pull-model)

Inject shared runtime dependencies (camera, services, managers) into TaskNodes using a pull-model DI pattern.

The package defines its own minimal `IDependencyContext` interface (`Hlight.Structures.CompositeTask.Runtime.IDependencyContext`) so the submodule has **no hard dependency on any specific DI package**. Any project-level DI context that exposes the same method shape can satisfy it directly — just add the interface to the context's interface list. Shape mirrors `Apero.Unity.DesignPattern.DependencyInversion.ServiceLocator.IServiceLocator` so one adapter class bridges an Apero scope in.

**No `Resolve` / `TryResolve` wrapper ships** — intentional. A generic wrapper hides `TryProvide` from IDE Find Usages behind a single call site. Task nodes call `context.GetProvider<T>()?.TryProvide(out _field, key)` at the use-site so Find Usages on `TryProvide` lists every real consumer of `T`.

## Mental model

Each TaskNode overrides `ResolveDependencies(IDependencyContext context)` and pulls what it needs via `context.GetProvider<T>()?.TryProvide(out _field, instanceKey)`. The tree walks itself — `CompositeNode` propagates to children, `ConditionalNode` to branches, `RunSubTreeNode` to the sub-tree.

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

        // Required — throw loudly if no provider for Camera.
        if (!context.GetProvider<Camera>().TryProvide(out _camera))
            throw new System.Collections.Generic.KeyNotFoundException("Camera provider missing");

        // Optional — null on miss.
        context.GetProvider<IAudioService>()?.TryProvide(out _audio);
    }

    protected override async UniTask OnRunning(Settings config, CancellationToken ct)
    {
        // use _camera, _audio ...
    }

    [Serializable] public class Settings { /* pure data */ }
}
```

## Producer side — bridging from a project DI context

The Composite Task `IDependencyContext` is:

```csharp
namespace Hlight.Structures.CompositeTask.Runtime
{
    public interface IDependencyContext
    {
        IProvider<T> GetProvider<T>() where T : class;

        public interface IProvider<T> where T : class
        {
            bool TryProvide(out T value, string instanceKey = null);
        }
    }
}
```

If your project's DI context already exposes `GetProvider<T>` with this exact shape, **add the interface to the class's interface list** — no new methods needed. Otherwise write a thin adapter class that delegates. Example adapter bridging an Apero scope (`IServiceLocator` in `Apero.Unity.DesignPattern.DependencyInversion.ServiceLocator`) into this contract:

```csharp
sealed class CompositeTaskContextAdapter : Hlight.Structures.CompositeTask.Runtime.IDependencyContext
{
    readonly Apero.Unity.DesignPattern.DependencyInversion.ServiceLocator.IServiceLocator _locator;
    readonly object _consumer;

    public CompositeTaskContextAdapter(
        Apero.Unity.DesignPattern.DependencyInversion.ServiceLocator.IServiceLocator locator,
        object consumer)
    {
        _locator = locator;
        _consumer = consumer;
    }

    public Hlight.Structures.CompositeTask.Runtime.IDependencyContext.IProvider<T> GetProvider<T>()
        where T : class
    {
        var aperoProvider = _locator.GetProvider<T>(_consumer);
        return aperoProvider == null ? null : new Wrap<T>(aperoProvider);
    }

    sealed class Wrap<T> : Hlight.Structures.CompositeTask.Runtime.IDependencyContext.IProvider<T>
        where T : class
    {
        readonly Apero.Unity.DesignPattern.DependencyInversion.ServiceLocator.IServiceLocator.IProvider<T> _inner;
        public Wrap(Apero.Unity.DesignPattern.DependencyInversion.ServiceLocator.IServiceLocator.IProvider<T> inner) { _inner = inner; }
        public bool TryProvide(out T value, string instanceKey = null) => _inner.TryProvide(out value, instanceKey);
    }
}
```

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

    // Required — fail loud.
    if (!context.GetProvider<Camera>().TryProvide(out _camera))
        throw new System.Collections.Generic.KeyNotFoundException("Camera provider missing");

    // Optional — null on miss.
    context.GetProvider<IAnalytics>()?.TryProvide(out _analytics);
}
```

Rule: throw on miss for required deps (fast-fail surfaces bugs early). Allow null for genuinely optional ones.

## Anti-patterns

- ❌ Forgetting `base.ResolveDependencies(context)` — breaks propagation on `CompositeNode`, `ConditionalNode`, `RunSubTreeNode`. Always call base first.
- ❌ `FindObjectOfType` inside `OnWarm` / `OnRunning` — defeats DI. Pull via context.
- ❌ Storing `IDependencyContext` as a field and re-using later — contexts can die/change. Store the resolved T, not the context.
- ❌ Calling `ResolveDependencies` after `Execute` has started — injects into a partially-running tree; undefined behaviour for nodes mid-async.
- ❌ Putting resolved refs in the `Settings` nested class — Settings is pure data, serialized to JSON. Resolved refs live on the class as private fields.

## Assumption check

If `context.GetProvider<T>()` returns null or `TryProvide` returns false, the caller's DI context doesn't serve `T`. Verify:
- The owning context was wired into the task tree before `ResolveDependencies` ran (canonical timing: after `LoadFromJson` / manual build, before `Execute`).
- The context's `GetProvider<T>` actually returns a non-null `IProvider<T>` for the requested type (for scope chains: check the whole chain up to root).
- `TryProvide` returns `true` for the requested `instanceKey` — mismatched key is a silent miss.

See the DI package's skill/docs for context-side debugging.
