# Dependency Injection (pull-model)

Inject shared runtime dependencies (camera, services, managers) into TaskNodes using a pull-model DI pattern.

The package defines its own minimal `IServiceLocator` interface (`Hlight.Structures.CompositeTask.Runtime.IServiceLocator`) so the submodule has **no hard dependency on any specific DI package**. Any project-level DI context that exposes the same method shape can satisfy it directly — just add the interface to the context's interface list. Shape mirrors `Apero.Unity.DesignPattern.DependencyInversion.ServiceLocator.IServiceLocator` so one adapter class bridges an Apero scope in.

**No `Resolve` / `TryResolve` wrapper ships** — intentional. A generic wrapper hides `TryProvide` from IDE Find Usages behind a single call site. Task nodes call `locator.GetProvider<T>(this)?.TryProvide(out _field, key)` at the use-site so Find Usages on `TryProvide` lists every real consumer of `T`.

## Mental model

Each TaskNode overrides `ResolveFrom(IServiceLocator locator)` and pulls what it needs via `locator.GetProvider<T>(this)?.TryProvide(out _field, instanceKey)`. The tree walks itself — `CompositeNode` propagates to children, `ConditionalNode` to branches, `RunSubTreeNode` to the sub-tree.

No visitor, no marker interface, no reflection. Plain virtual method overrides.

## The contract

```csharp
public virtual void ResolveFrom(IServiceLocator locator) { }
```

Declared on `TaskNode`. Default: no-op. Override in subclasses that need deps. ALWAYS call `base.ResolveFrom(locator)` first — some base classes (CompositeNode, ConditionalNode, RunSubTreeNode) propagate through the base call.

## Consumer side (a TaskNode needing DI)

```csharp
public class ShakeCameraNode : TaskNode<ShakeCameraNode.Settings>
{
    private Camera _camera;
    private IAudioService _audio;

    public override void ResolveFrom(IServiceLocator locator)
    {
        base.ResolveFrom(locator);

        // Required — throw loudly if no provider for Camera.
        if (!locator.GetProvider<Camera>(this).TryProvide(out _camera))
            throw new System.Collections.Generic.KeyNotFoundException("Camera provider missing");

        // Optional — null on miss.
        locator.GetProvider<IAudioService>(this)?.TryProvide(out _audio);
    }

    protected override async UniTask OnRunning(Settings config, CancellationToken ct)
    {
        // use _camera, _audio ...
    }

    [Serializable] public class Settings { /* pure data */ }
}
```

## Producer side — bridging from a project DI context

The Composite Task `IServiceLocator` is:

```csharp
namespace Hlight.Structures.CompositeTask.Runtime
{
    public interface IServiceLocator
    {
        IProvider<T> GetProvider<T>(object dependencyConsumer) where T : class;

        public interface IProvider<T> where T : class
        {
            bool TryProvide(out T value, string instanceKey = null);
        }
    }
}
```

Shape matches `Apero.Unity.DesignPattern.DependencyInversion.ServiceLocator.IServiceLocator` exactly (same method name + params). If your project's DI context already exposes this shape, **add the interface to the class's interface list** — no new methods needed. Otherwise write a thin adapter class that delegates. Example adapter wrapping an Apero `IServiceLocator`:

```csharp
sealed class CompositeTaskContextAdapter : Hlight.Structures.CompositeTask.Runtime.IServiceLocator
{
    readonly Apero.Unity.DesignPattern.DependencyInversion.ServiceLocator.IServiceLocator _locator;

    public CompositeTaskContextAdapter(
        Apero.Unity.DesignPattern.DependencyInversion.ServiceLocator.IServiceLocator locator)
    {
        _locator = locator;
    }

    public Hlight.Structures.CompositeTask.Runtime.IServiceLocator.IProvider<T> GetProvider<T>(object consumer)
        where T : class
    {
        var aperoProvider = _locator.GetProvider<T>(consumer);
        return aperoProvider == null ? null : new Wrap<T>(aperoProvider);
    }

    sealed class Wrap<T> : Hlight.Structures.CompositeTask.Runtime.IServiceLocator.IProvider<T>
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
taskTree.ResolveFrom(myLocator);
taskTree.Execute();
```

Typical order in a level-loading flow:

```csharp
tree.LoadFromJson(json);             // build hierarchy
tree.ResolveFrom(myLocator); // inject
tree.Warm();                         // one-time per-node init
tree.Execute();                      // run
```

`myLocator` can be a scene-level context, a singleton, or a wrapper — anything that implements the interface.

## Propagation under the hood

- `TaskNode.ResolveFrom` — default no-op; leaf overrides do the pulling.
- `CompositeNode.ResolveFrom` — calls `base` (which pulls the composite's own deps, if any), then iterates every `TaskNode` child and calls `child.ResolveFrom(locator)`.
- `ConditionalNode.ResolveFrom` — calls `base`, then iterates every branch and calls `branch.node.ResolveFrom(locator)`. All branches get injected, not just the one that will execute.
- `RunSubTreeNode.ResolveFrom` — calls `base`, then `_subTree.ResolveFrom(locator)` — propagates across sub-tree boundaries.

Single call on the root reaches every node in the tree, including sub-trees.

## When to call

| Phase | Safe to call `ResolveFrom`? |
|-------|-------------------------------------|
| Immediately after `AddComponent<TaskTree>` / before hierarchy exists | No — tree has no children to inject. |
| After tree is built in Editor (prefab scene) | Yes, but only if contexts are already registered (rare in Edit Mode). |
| After `LoadFromJson` / manual scene build, before `Execute` | **Yes — canonical timing.** |
| After `ResetTree()` | Yes — state is cleared, re-inject before next Execute. |
| Inside `OnRunning` / async flow | No — dependencies should already be resolved. |

## Multiple passes

```csharp
tree.ResolveFrom(sceneLocator);          // scene-scoped services
tree.ResolveFrom(globalLocator); // global services
```

Each pass calls every node's `ResolveFrom` with its own context. Idempotent — a node that already has its deps can either skip (check for null) or overwrite.

## Optional dependencies

```csharp
public override void ResolveFrom(IServiceLocator locator)
{
    base.ResolveFrom(locator);

    // Required — fail loud.
    if (!locator.GetProvider<Camera>(this).TryProvide(out _camera))
        throw new System.Collections.Generic.KeyNotFoundException("Camera provider missing");

    // Optional — null on miss.
    locator.GetProvider<IAnalytics>(this)?.TryProvide(out _analytics);
}
```

Rule: throw on miss for required deps (fast-fail surfaces bugs early). Allow null for genuinely optional ones.

## Anti-patterns

- ❌ Forgetting `base.ResolveFrom(locator)` — breaks propagation on `CompositeNode`, `ConditionalNode`, `RunSubTreeNode`. Always call base first.
- ❌ `FindObjectOfType` inside `OnWarm` / `OnRunning` — defeats DI. Pull via context.
- ❌ Storing `IServiceLocator` as a field and re-using later — contexts can die/change. Store the resolved T, not the context.
- ❌ Calling `ResolveFrom` after `Execute` has started — injects into a partially-running tree; undefined behaviour for nodes mid-async.
- ❌ Putting resolved refs in the `Settings` nested class — Settings is pure data, serialized to JSON. Resolved refs live on the class as private fields.

## Assumption check

If `locator.GetProvider<T>(this)` returns null or `TryProvide` returns false, the caller's DI context doesn't serve `T`. Verify:
- The owning context was wired into the task tree before `ResolveFrom` ran (canonical timing: after `LoadFromJson` / manual build, before `Execute`).
- The context's `GetProvider<T>` actually returns a non-null `IProvider<T>` for the requested type (for scope chains: check the whole chain up to root).
- `TryProvide` returns `true` for the requested `instanceKey` — mismatched key is a silent miss.

See the DI package's skill/docs for context-side debugging.
