# Building a Task Tree via Unity MCP

The workflow when the user describes a scenario and wants an actual task tree materialized in the scene. This is **dialog-first** — never generate a tree from a vague description; always confirm first.

## The loop

```
┌─────────────┐   ┌────────────┐   ┌─────────────┐   ┌──────────┐   ┌──────────┐
│ 1. LISTEN   │ → │ 2. PROPOSE │ → │ 3. CLARIFY  │ → │ 4. BUILD │ → │ 5. REFS  │
│ scenario    │   │ ASCII tree │   │ (loop if    │   │ via MCP  │   │ fill in  │
└─────────────┘   └────────────┘   │  ambiguous) │   └──────────┘   └──────────┘
                                   └─────────────┘
```

## Step 1: Listen

Read the user's description as prose (any language). Identify:
- **Sequential beats** ("first X, then Y")
- **Parallel beats** ("at the same time", "while doing X")
- **Conditionals** ("if A, do B; otherwise C")
- **Waits** ("pause", "delay", "after N seconds")
- **Loops** ("repeat until", "while N") → may need a custom TaskNode; built-ins don't include generic loops

Don't match to typeIds yet. Stay at the prose level.

## Step 2: Propose the tree in plain English (ASCII diagram)

Use natural-language labels, NOT typeIds. Show the user what you understood.

```
Root (sequential)
├── Wait half a second
├── Simultaneously:
│    ├── Shake the camera briefly
│    ├── Flash the player sprite red
│    └── Play the hit sound effect
└── Show the damage number
```

Do NOT say `Wait (value=500, unit=1)` or `TweenColor` here. The point of this step is the user understanding the *shape* without jargon.

## Step 3: Clarify ambiguous steps

For each step that could map to multiple nodes or needs more info, ask a **multiple-choice question** in natural language:

> "When you say 'character reacts when hit' — do you want:
> (a) the character's sprite to flash red briefly
> (b) the character to play a hurt animation
> (c) the character to knock back (move) in the opposite direction
> (d) something else — describe it"

**Rules for good clarifying questions:**
- Use numbered/lettered options.
- Options are concrete behaviours, not node names.
- Include an "other / describe it" escape.
- Group related questions in one turn when possible — don't ping-pong.

Common things to clarify:
- **Durations** — "how long should the shake last?"
- **Targets** — "which character / object does this apply to?"
- **Sequencing** — "does X happen before Y, or together?"
- **Failure case** — "what if the condition doesn't hold?"
- **Magnitudes** — "how much movement / scale / alpha?"

Loop steps 2–3 until the user confirms ("yes", "looks good", "that's right", etc.).

## Step 4: Map to typeIds (internal only)

Once the tree shape is confirmed, mentally map each leaf label to a typeId using `built-in-nodes.md`. If a step has no match, propose creating a new TaskNode (see `node-creation.md`).

At this point you can show the user the **JSON blueprint** if they want (optional — some users care, some don't).

## Step 5: Build via `Unity_RunCommand`

Use the `Unity_RunCommand` MCP tool. The tool compiles + executes C# using the `IRunCommand` pattern.

### Required preamble for any command

```csharp
using UnityEngine;
using UnityEditor;
using Hlight.Structures.CompositeTask.Runtime;
using Hlight.Structures.CompositeTask.Runtime.Blueprint;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        // your logic
    }
}
```

### Build pattern

```csharp
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        // 1. Find or create the TaskTree host GameObject
        var go = GameObject.Find("<TARGET_NAME>");
        if (go == null)
        {
            go = new GameObject("<TARGET_NAME>");
            result.RegisterObjectCreation(go);
        }

        var tree = go.GetComponent<TaskTree>() ?? Undo.AddComponent<TaskTree>(go);
        result.RegisterObjectModification(tree);

        // 2. Parse the blueprint JSON
        var json = @"<JSON_HERE>";
        var blueprint = BlueprintJsonParser.Parse(json);
        if (blueprint == null || blueprint.root == null)
        {
            result.LogError("Failed to parse blueprint JSON.");
            return;
        }

        // 3. Clear existing task children
        for (int i = go.transform.childCount - 1; i >= 0; i--)
        {
            var child = go.transform.GetChild(i);
            if (child.GetComponent<TaskNode>() != null)
                result.DestroyObject(child.gameObject);
        }

        // 4. Build the tree
        var rootGo = TaskTreeBuilder.BuildNode(
            blueprint.root,
            go.transform,
            presets: null,            // or use PresetTaskTree.NodePresets
            instantiatePrefab: null); // editor: use (p, parent) => (GameObject)PrefabUtility.InstantiatePrefab(p, parent)

        if (rootGo != null)
        {
            result.RegisterObjectCreation(rootGo);
            result.Log("Built tree with root: {0}", rootGo);
        }
        else
        {
            result.LogError("BuildNode returned null — check Console for registry warnings.");
        }
    }
}
```

### If target uses PresetTaskTree

Replace step 4's `presets: null` with the preset list from the component:

```csharp
var presetTree = go.GetComponent<PresetTaskTree>();
var presets = presetTree?.NodePresets;
// Then pass: TaskTreeBuilder.BuildNode(blueprint.root, go.transform, presets, PrefabInstantiator);
```

For Editor prefab instantiation (preserves prefab link):

```csharp
TaskTreeBuilder.PrefabInstantiator instantiator =
    (p, parent) => (GameObject)PrefabUtility.InstantiatePrefab(p, parent);
```

### Querying the registry

To confirm which typeIds are registered (useful when the user asks "what nodes are available"):

```csharp
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        foreach (var kv in TaskNodeRegistry.AllEntries)
            result.Log("{0} -> {1}", kv.Key, kv.Value.FullName);
    }
}
```

## Step 6: Ref-fill (the human-in-the-loop part)

After `BuildNode`, leaf nodes with `[SerializeField]` Unity refs are null (unless PresetTaskTree matched a prefab). Don't silently leave them null — surface them to the user.

### 6a: Scan for unfilled refs

```csharp
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var go = GameObject.Find("<TARGET_NAME>");
        var missing = new System.Collections.Generic.List<string>();

        foreach (var node in go.GetComponentsInChildren<TaskNode>(true))
        {
            var type = node.GetType();
            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance
                                                | System.Reflection.BindingFlags.NonPublic
                                                | System.Reflection.BindingFlags.Public))
            {
                if (!typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) continue;
                if (field.GetValue(node) == null)
                    missing.Add($"{node.gameObject.name} ({type.Name}) . {field.Name} : {field.FieldType.Name}");
            }
        }

        if (missing.Count == 0) { result.Log("All refs filled."); return; }
        result.Log("Unfilled refs:\n" + string.Join("\n", missing));
    }
}
```

### 6b: Ask the user about scope FIRST

Before auto-matching anything, ask:

> "The tree has N nodes with unassigned refs:
> • `Shake` (Tween Position) needs a Transform
> • `Play SFX` (Play Sound) needs an AudioSource + (optional) AudioClip
> • `Flash Red` (Tween Color) needs SpriteRenderer[]
>
> How do you want to wire them?
> (a) I can try to auto-match from your current scene (I'll show you what I propose before applying)
> (b) Ask me about each one (you pick the object from the scene)
> (c) Skip — you'll wire them in the Inspector yourself
> (d) Mix: auto-match the easy ones (Transform/AudioSource), ask for the others"

### 6c: Auto-match strategies (when user chose a/d)

Only apply AFTER user confirmation on each proposed assignment. Heuristics:

1. **Same GameObject match** — if the leaf node's GameObject already has the required component (e.g., `SpriteRenderer` in a Flash Color task that lives on the sprite), use `GetComponent`.
2. **Parent hierarchy search** — walk up `transform.parent` looking for a match.
3. **Scene singletons** — if only one `AudioSource` tagged "SFX" exists, propose it.
4. **Name-based hints** — if the field name is `bgmSource` and there's a GameObject called "BGM" with an AudioSource, propose it.

For each match, `Debug.Log` the proposal and wait for user OK before applying via `result.RegisterObjectModification(...)` + reflection set.

### 6d: Apply ref via `Unity_RunCommand`

```csharp
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var nodeGo = GameObject.Find("<NODE_GAMEOBJECT_NAME>");
        var refGo  = GameObject.Find("<REFERENCED_OBJECT_NAME>");
        if (nodeGo == null || refGo == null) { result.LogError("GameObject not found"); return; }

        var node = nodeGo.GetComponent<TaskNode>();
        if (node == null) { result.LogError("No TaskNode on target"); return; }

        result.RegisterObjectModification(node);

        var field = node.GetType().GetField("<FIELD_NAME>",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field == null) { result.LogError("Field not found"); return; }

        // Pick the right UnityEngine.Object — GameObject, Component, or specific
        var value = refGo.GetComponent(field.FieldType) ?? (UnityEngine.Object)refGo;
        field.SetValue(node, value);

        EditorUtility.SetDirty(node);
        result.Log("Set {0}.{1} = {2}", node, field.Name, value);
    }
}
```

## Step 7: Verify

```
Unity_GetConsoleLogs          → check for errors/warnings from the build
Unity_SceneView_Capture2DScene → optional screenshot of hierarchy (if the user wants visual confirm)
```

Report back to the user with:
- The ASCII tree (re-displayed)
- Which refs were filled, which are still pending
- Any console warnings

## Complete example (end-to-end walkthrough)

User: "When I click the 'Start' button, fade out a black overlay, then transition to the next scene."

### Listen → Propose:

> Here's what I understood:
> ```
> Root (sequential)
> ├── Fade a black overlay from transparent to opaque (how long?)
> └── Transition to the next scene
> ```
>
> A few things to clarify:
> 1. How long should the fade last? (e.g. 0.5s, 1s)
> 2. Is the "black overlay" an existing UI Image/CanvasGroup in the scene, or should we create one?
> 3. What's the exact name of the "next scene"?

### User clarifies → Propose refined tree:

> ```
> Root (sequential)
> ├── Fade the 'BlackOverlay' CanvasGroup alpha 0 → 1 over 0.5s
> └── Trigger scene load event (UnityEvent on 'SceneLoader')
> ```
> Does that match what you want?

### User confirms → Map + Build:

- `Fade` → `Tween Canvas Group Alpha` with `{value: 1, isFrom: false, duration: 0.5}`
- `Trigger scene load` → `Invoke Unity Event` (user wires the callback in Inspector)

Run `Unity_RunCommand` with the Build pattern above.

### Ref scan → Ask about refs:

> Tree built. 2 refs still need assignment:
> • `Fade.target` (CanvasGroup)
> • `Trigger.onExecute` (UnityEvent — you must wire this in the Inspector; UnityEvent callbacks can only be configured via the UI)
>
> Do you want me to auto-match `Fade.target` to 'BlackOverlay' in the scene, or will you assign it yourself?

### Apply ref → verify → done.

## Important reminders

- **Never skip the dialog.** Even if you're 95% sure, ask.
- **Never invent typeIds or Settings fields.** Look them up in `built-in-nodes.md`.
- **Never auto-apply refs** without the user first confirming the scope.
- **Always `result.RegisterObjectCreation/Modification`** so Unity's Undo system works.
- **Report errors from console** — `Unity_GetConsoleLogs` after any build command.
- **The user is the source of truth for intent.** If you're unsure, ask.

## Alternative MCP APIs (and when to use them)

| MCP tool | Use for |
|----------|---------|
| `Unity_RunCommand` | **Main workhorse.** Everything: create GO, add components, call BuildNode, reflect fields. |
| `Unity_GetConsoleLogs` | Verify after build, diagnose Failed tasks. |
| `Unity_SceneView_Capture2DScene` | Optional visual confirm of hierarchy (2D projects). |
| `Unity_Camera_Capture` | When a specific camera view matters. |
| `Unity_AssetGeneration_GenerateAsset` | Create missing assets (sprites, sounds) before wiring — rarely needed. |

There is no direct "create prefab" or "set field" MCP tool — everything flows through `Unity_RunCommand` with C# code.
