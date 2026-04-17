# ConditionalNode & TaskCondition

Branching node that picks ONE child to execute based on conditions.

## Setup

`ConditionalNode` has a serialized list of `Branch`:

```csharp
[Serializable]
public class Branch
{
    public TaskCondition condition;   // null = fallback (always true)
    public TaskNode node;             // child to execute if this branch matches
}
```

Evaluation: top-to-bottom, first match wins. If no branch matches, the Conditional completes immediately (no-op).

## TaskCondition

Abstract MonoBehaviour. Override `Evaluate()`:

```csharp
public abstract class TaskCondition : MonoBehaviour
{
    public abstract bool Evaluate();
}
```

Each project condition is a separate class:

```csharp
public class IsPlayerLowHealthCondition : TaskCondition
{
    [SerializeField] private Player _player;
    [SerializeField, Range(0f, 1f)] private float _threshold = 0.3f;

    public override bool Evaluate() => _player.CurrentHp / _player.MaxHp < _threshold;
}
```

## Hierarchy layout

ConditionalNode's Transform children ARE the branch nodes. The `Branch.node` field points to one of the children; conditions may live on separate GameObjects or on the branch GameObject itself.

```
GameObject "If Low HP Then Heal Else Attack" (ConditionalNode)
├── GameObject "LowHp" (TaskCondition + TaskNode: HealSequence)
└── GameObject "Default" (TaskCondition = null / or separate cond GO, TaskNode: AttackSequence)
```

## Inspector wiring

1. Add ConditionalNode to a GameObject.
2. Add branch children (each a TaskNode — usually a Sequential or Parallel).
3. Expand `_branches` array → for each:
   - Drag the condition component to `condition` (or leave null for fallback).
   - Drag the child TaskNode to `node`.
4. Order branches from most-specific to fallback. Put fallback (null condition) last.

## Fallback pattern

```
Branch 0: condition = IsPlayerLowHpCondition,  node = HealSequence
Branch 1: condition = IsEnemyInRangeCondition, node = AttackSequence
Branch 2: condition = null,                    node = IdleSequence    ← fallback
```

## Creating a custom TaskCondition

1. Subclass `TaskCondition`.
2. Add `[SerializeField]` refs for whatever data Evaluate needs.
3. Implement `bool Evaluate()` — cheap, pure, no side effects.
4. Attach to the branch GameObject in Editor.

**Rules:**
- `Evaluate` is called once per Conditional execution; should be fast.
- No async — it's a pure sync predicate.
- No mutation — just read state and return.
- If multiple values needed for decision, inject them via `[SerializeField]` or DI visitor.

## Anti-patterns

❌ Logic side-effects inside Evaluate (mutating state).
❌ Expensive computation — cache outside Evaluate.
❌ Null-check forgotten on injected refs (will throw → entire tree fails).

## DI integration

If conditions need runtime-injected dependencies, override the ConditionalNode's `Accept` path OR make your TaskCondition visitable. The built-in ConditionalNode already propagates `Accept` to its child nodes.

See `di-visitor.md` for the pattern.

## Runtime control

- `ResetTask` on ConditionalNode resets all child task branches.
- `ForceComplete` bubbles to the currently-running branch.
- If evaluation happens DURING Warm (called once before first Execute), changing condition state after Warm but before Execute is respected (evaluated per Execute, not cached).

## Example: "attack with priority"

```
Sequence "Attack"
└── Conditional "Pick Attack Type"
    ├── Branch (IsStaggeredCondition):   FinisherSequence
    ├── Branch (IsInMeleeRangeCondition): MeleeAttackSequence
    └── Branch (null):                    RangedAttackSequence
```

At runtime: if stagger flag is set, plays the finisher; else if in melee range, swings; else shoots.
