---
name: composite-task
description: Use this skill whenever working with the Composite Task Unity package (Hlight.Structures.CompositeTask) — creating TaskNode/TaskNode<T> classes, authoring Blueprint JSON, building task trees in a scene via Unity MCP from a natural-language scenario, configuring PresetTaskTree, wiring TaskCondition/ConditionalNode branches, using IInlineTask, resolving dependencies via IDependencyContext, or debugging task lifecycle. Trigger on mentions of TaskTree, TaskNode, DefineTaskNode, SequentialNode, ParallelNode, Blueprint, composite task, or when the user describes a gameplay flow that resembles a sequence/parallel/conditional task graph — even if they don't name the package explicitly.
---

# Composite Task — Skill

Unity package at `Assets/Submodules/Composite Task`. MonoBehaviour hierarchy = task tree. Transform children = child tasks.

## Mental Model (read before anything)

```
TaskTree (MonoBehaviour)                  — root manager, CTS, events
  └── CompositeNode                       — Sequential / Parallel (container)
        └── TaskNode                      — leaf (abstract base)
              └── TaskNode<TConfig>       — leaf with Settings (pure data)
```

- **Scene hierarchy IS the data structure** — no serialized lists of children. Parent GameObject → child GameObject = parent task → child task.
- **Leaf task = unit of work**. Composite task = flow control (sequential, parallel, conditional).
- **Two kinds of data**:
  - `[SerializeField]` Unity object refs on the class (AudioSource, Transform) — set in Editor/prefab, cannot be JSON-serialized.
  - Nested `[Serializable] Settings` class for pure data (float, enum, Vector3) — serializable, override-able from JSON.

## Decision Flow (what does the user want?)

| User intent | What to do | Read |
|-------------|-----------|------|
| "Build a task tree that does X Y Z" (scenario description) | Dialog-first build flow + Unity MCP | `references/mcp-build-tree.md` |
| "Create a new task node for ..." | Choose TaskNode vs TaskNode<T>, use generator | `references/node-creation.md` + run `scripts/new-task-node.py` |
| "Write a JSON blueprint for ..." | Use JSON format spec + node catalog | `references/blueprint-json.md` + `references/built-in-nodes.md` |
| "What node does X?" / "Is there a built-in for Y?" | Look up catalog | `references/built-in-nodes.md` |
| "Why isn't my node completing?" / lifecycle questions | Execution flow + override rules | `references/lifecycle.md` |
| "Set up branching / if-then-else" | Conditional + TaskCondition | `references/conditional.md` |
| "Add logic at runtime without making a MonoBehaviour" | IInlineTask | `references/inline-tasks.md` |
| "Load tree from JSON at runtime with prefab leaves" | PresetTaskTree + LoadFromJson | `references/preset-tree.md` |
| "Inject dependencies into tasks" | Override `ResolveDependencies(IDependencyContext)` | `references/di-resolve.md` |

## Core Rules (apply to ALL tasks in this package)

### Rule 1: Pick the right base class

| Base | When |
|------|------|
| `TaskNode` | No configurable data — logic uses only Unity refs + constants. Ex: `StopSoundNode` just calls `source.Stop()`. |
| `TaskNode<TConfig>` | Has pure-data parameters (duration, speed, mode, etc.). Ex: `WaitNode(value, unit)`, `PlaySoundNode(mode)`. Settings class is JSON-serializable. |

**Rule of thumb**: if the user describes the task and says words like "for N seconds", "at speed X", "with mode Y" — it needs `TaskNode<T>`. If it's a simple action ("stop this", "destroy that") with no knobs, `TaskNode` is enough.

### Rule 2: Sync-only logic goes in `OnCompleted`, not `OnRunning`

`OnRunning` is awaitable — it can be cancelled/skipped. `OnCompleted` is synchronous and is guaranteed to fire after `OnFinishing` (unless the task Failed).

**For tasks that are purely instantaneous side-effects** (set transform, destroy GO, stop audio, invoke event):

```csharp
protected override UniTask OnRunning(CancellationToken ct) => UniTask.CompletedTask;

protected override void OnCompleted()
{
    // Side effect here — always fires, even if task was force-completed.
    target.SetActive(active);
    base.OnCompleted();  // MUST call base, sets Status = Completed + Progress = 1
}
```

Example existing nodes following this rule: `SetTransformNode`, `SetGameObjectActivationNode`, `DestroyNode`, `InvokeUnityEventNode`, `StopSoundNode`, `ForceCompleteNode`, `SetProgressNode`.

### Rule 3: Settings class holds PURE DATA ONLY

Unity object refs (Transform, AudioSource, GameObject, UnityEvent, Component[]) are **always** `[SerializeField]` on the class itself, never inside Settings. Settings holds `float`, `int`, `bool`, `enum`, `Vector3`, `Color`, `string`.

Reason: Settings is JSON-serialized; object refs can't survive JSON round-trip.

### Rule 4: `OnCompleted` must call `base.OnCompleted()`

Base implementation sets `Status = TaskStatus.Completed` and `Progress = 1f` (which fires events). Skipping it breaks the tree.

```csharp
protected override void OnCompleted()
{
    DoMyThing();
    base.OnCompleted();  // ← required
}
```

### Rule 5: `TaskNode<T>` has a `Config` property — don't cache it

`TaskNode<TConfig>` already exposes `public TConfig Config { get; set; }`. Inside your class (including in `OnCompleted`, `OnReset`, etc.), read `Config.fieldName` directly. **Do not** stash the `config` parameter from `OnRunning` into a private field — the property is always the same reference.

### Rule 6: Never name the nested class `Config`

The convention is `Settings` (or `Data`, `Params`, `Options`). Using `Config` as the nested class name collides with the inherited `Config` property and breaks compilation.

### Rule 7: Namespace is user-specific — never assume

The package namespace is `Hlight.Structures.CompositeTask.Runtime` (that's where `TaskNode`, `TaskTree` etc. live), but the namespace of a **new task node being generated** depends entirely on the user's project conventions. Do not assume a company/product prefix from context. Ask the user for the target namespace and folder if not given.

### Rule 8: Register with `[DefineTaskNode]` for discovery

```csharp
[DefineTaskNode("My Task", Category = "Gameplay",
    Description = "What it does. Ref: target (Transform). Config: speed (float).")]
public class MyTaskNode : TaskNode<MyTaskNode.Settings> { ... }
```

- `Id` (first arg) = typeId used in JSON Blueprint — must be unique
- `Category` = group in Add Child dropdown (nestable via `/`)
- `Description` = editor tooltip — document refs, config fields, DI needs

## The Dialog-First Build Flow (short version)

When the user describes a scenario to turn into a real task tree in the scene, do NOT guess. Follow this:

1. **Listen** to the scenario. Read it as prose, not as node names.
2. **Propose** a tree as an **ASCII diagram with plain-English labels** (not node names). Example:
   ```
   Root (sequential)
   ├── Wait 1 second
   ├── In parallel:
   │    ├── Shake the camera briefly
   │    └── Flash the player sprite red
   └── Play hit sound
   ```
3. **Ask clarifying questions** where steps are ambiguous. Prefer natural-language options over node names:
   > "Step 2 you said 'character reacts' — do you mean (a) play an animation, (b) change sprite color, (c) both, or (d) something else?"
4. **Iterate** 2–4 until the user confirms the tree.
5. **Map** the confirmed plain-English steps to built-in node typeIds (catalog in `references/built-in-nodes.md`). Only now should node names enter the conversation.
6. **Build** via Unity MCP — see `references/mcp-build-tree.md` for the exact `Unity_RunCommand` flow.
7. **Refs** — after build, scan leaf nodes for unassigned `[SerializeField]` Unity refs. For each, ask the user about scope:
   > "The tree has 3 nodes needing refs: `PlaySound.source` (AudioSource), `TweenPosition.target` (Transform), `SetActive.target` (GameObject). Options: (a) let me try to auto-match from your scene, (b) I'll ask you for each one, (c) you'll wire them yourself in Inspector."
8. **Verify** — `Unity_GetConsoleLogs` to check for errors; optionally capture the hierarchy.

**Do not skip step 2–4. The tree shape must be user-confirmed in plain English before any typeId mapping.**

## Quick Glossary

| Term | Meaning |
|------|---------|
| TaskTree | Root MonoBehaviour. Manages execution, events, CTS. |
| TaskNode | Abstract base for all leaf/composite nodes. |
| CompositeNode | Abstract base for Sequential/Parallel. Reads children from Transform. |
| Leaf | Any TaskNode that's not a CompositeNode. Does actual work. |
| typeId | String Id from `[DefineTaskNode("...")]`. Used in Blueprint JSON. |
| Settings | Nested `[Serializable]` class on `TaskNode<T>` holding pure-data config. |
| Blueprint | Plain C# record model (CompositeBlueprint / LeafBlueprint). |
| PresetTaskTree | TaskTree variant mapping typeId → prefab for LoadFromJson. |

## File Layout of the Package

```
Assets/Submodules/Composite Task/
├── Runtime/
│   ├── Core/            — TaskNode, TaskNode<T>, CompositeNode, Sequential, Parallel, TaskTree, PresetTaskTree, enums
│   ├── Blueprint/       — JSON parser, TaskNodePreset, BlueprintTree/CompositeBlueprint/LeafBlueprint
│   ├── Builder/         — TaskTreeBuilder, TaskNodeRegistry (auto-scans [DefineTaskNode])
│   └── Nodes/
│       ├── Audio/       — Play Sound, Stop Sound
│       ├── Conditional/ — Conditional, TaskCondition
│       ├── Event/       — Wait Event, TaskEventSource + 2 subclasses
│       ├── Inline/      — IInlineTask, InlineTaskNode, extensions
│       ├── Tween/       — 7 DOTween nodes (separate asmdef)
│       └── *.cs         — Wait, Transform, Destroy, Instantiate, PlayAnimator, PlayAnimation, ParticleSystem, etc.
└── Editor/              — Inspectors, hierarchy icons, JSON import/export, menu items
```

## When In Doubt

- **Don't invent typeIds.** If a scenario step doesn't obviously map to a known node, either (a) suggest creating a new TaskNode, or (b) ask the user which existing one fits.
- **Don't invent Settings fields.** Look up the exact schema in `references/built-in-nodes.md`.
- **Don't skip the dialog.** The user explicitly wants the tree shape confirmed before generation.
- **Don't auto-fill refs without asking scope first.** Some users want everything auto-wired, others want to do it themselves.
