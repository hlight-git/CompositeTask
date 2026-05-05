# ConditionalNode & TaskCondition

Branching node. Evaluates branches top-to-bottom; the first branch whose evaluations all match runs its node. If no branch matches, the explicit `fallbackNode` runs.

## Setup

`ConditionalNode` exposes a list of branches plus a fallback node:

```csharp
[Serializable]
public class Branch
{
    public Evaluation[] evaluations;  // ALL must match (logical AND)
    public TaskNode node;             // runs when the branch matches
}

[Serializable]
public class Evaluation
{
    public bool useLastResult;        // reuse condition.LastResult instead of re-evaluating
    public TaskCondition condition;
    public bool expectedValue = true; // match target; set false to invert
}

// On ConditionalNode itself:
[SerializeField] private TaskNode fallbackNode;  // runs when no branch matches; null = no-op
```

**Match semantics**: a branch matches only when *every* `Evaluation.condition.Evaluate() == Evaluation.expectedValue`. Empty/null `evaluations` array = branch is skipped (use `fallbackNode` for unconditional default).

## TaskCondition

Subclass and override `OnEvaluate()`. The base class caches the result in `LastResult` so other branches can reuse it.

```csharp
public abstract class TaskCondition : MonoBehaviour
{
    public bool LastResult { get; private set; }

    public bool Evaluate()
    {
        LastResult = OnEvaluate();
        return LastResult;
    }

    protected abstract bool OnEvaluate();
}
```

Project example:

```csharp
public class IsPlayerLowHealthCondition : TaskCondition
{
    [SerializeField] private Player _player;
    [SerializeField, Range(0f, 1f)] private float _threshold = 0.3f;

    protected override bool OnEvaluate() => _player.CurrentHp / _player.MaxHp < _threshold;
}
```

## Hierarchy layout

`ConditionalNode`'s Transform children ARE the branch nodes AND the fallback node. The `Branch.node` / `fallbackNode` fields each point to one child. `TaskCondition` components can live on the same GameObjects as their nodes, or on separate dedicated GameObjects under the Conditional.

```
GameObject "Decide Reaction" (ConditionalNode)
├── GameObject "OnHit"   (HealSequence + IsPlayerLowHpCondition + IsAggroedCondition)
├── GameObject "Combat"  (AttackSequence + IsEnemyInRangeCondition)
└── GameObject "Idle"    (IdleSequence) ← assigned as fallbackNode
```

## Inspector wiring

1. Add `ConditionalNode` to a GameObject.
2. Add child GameObjects with TaskNode components (each a leaf or Sequential/Parallel).
3. Expand `_branches` array — for each:
   - Set `evaluations` array. For each entry: drag the `TaskCondition`, set `expectedValue` (default `true`), tick `useLastResult` if you're reusing a previous evaluation.
   - Set `node` to the child TaskNode that runs when all evaluations match.
4. Drag a child node into `fallbackNode` for the no-match path.

## Why use `useLastResult`

When two branches share a condition (e.g. both check `IsPlayerLowHp`), the second branch can reuse the cached `LastResult` instead of evaluating again — useful when:
- The evaluation is expensive (raycast, allocation).
- The condition is non-idempotent (changes state when called).
- You want to inspect the same snapshot in multiple branches.

Caveat: `LastResult` is the result of the *most recent* `Evaluate()` call, so order matters. The first branch evaluates, the second reads. If the second branch *also* sets `useLastResult = true` on its first evaluation, but no prior evaluation has run, `LastResult` is whatever default (`false`) — log a warning during testing if that's a concern.

## Negation pattern

`Evaluation.expectedValue = false` inverts the check:

```
Branch 0:
  evaluations: [ { condition: IsPlayerInvulnerable, expectedValue: false } ]
  node:        DealDamageSequence
```

Reads as: "if NOT invulnerable → deal damage". Avoids needing a `IsPlayerVulnerableCondition` mirror class.

## Multi-condition AND

```
Branch 0:
  evaluations: [
    { condition: HasAmmo,         expectedValue: true },
    { condition: EnemyInRange,    expectedValue: true },
    { condition: WeaponNotJammed, expectedValue: true }
  ]
  node: ShootSequence
```

All three must be true simultaneously. There's no built-in OR — model it with multiple branches:

```
Branch 0: { evaluations: [HasShield],          node: BlockSequence }
Branch 1: { evaluations: [HasParryWindowOpen], node: ParrySequence }   // OR'd via separate branch
fallbackNode: TakeHitSequence
```

## fallbackNode

Required for "always do something". Without it, the Conditional completes silently when no branch matches — fine for optional reactions, bad for state machines that expect a transition.

Validation flags missing `fallbackNode` as a warning. Validation also requires `fallbackNode` (and every `Branch.node`) to be a Transform child of the Conditional itself.

## Creating a custom TaskCondition

1. Subclass `TaskCondition`.
2. Add `[SerializeField]` refs for whatever data the predicate needs.
3. Override `protected bool OnEvaluate()` — cheap, pure, no side effects.
4. Attach to a GameObject under the ConditionalNode (or alongside the branch's TaskNode).

**Rules:**
- `OnEvaluate` is called per branch evaluation; should be fast.
- No async — pure sync predicate.
- No mutation — read state and return. Use `useLastResult` for "ask me again, give the same answer".
- For runtime-injected dependencies, the ConditionalNode propagates `ResolveFrom` into branch nodes only — if a `TaskCondition` itself needs DI, resolve via the parent branch node or the Conditional's own context plumbing.

## Anti-patterns

- ❌ Logic side-effects inside `OnEvaluate` (mutating state).
- ❌ Expensive computation per evaluation — pre-compute, store, read in `OnEvaluate`.
- ❌ Forgetting `fallbackNode` when the branch list isn't truly exhaustive.
- ❌ Setting `useLastResult` on the FIRST evaluation a condition has seen this run — `LastResult` is stale or default.
- ❌ Empty `evaluations` array on a non-fallback branch — branch is silently skipped (validation warns).

## Runtime control

- `ResetTask` resets every branch node AND the fallback.
- `Dispose` disposes every branch node AND the fallback.
- `Warm()` warms every branch node AND the fallback (in case any need OnWarm init).
- `ForceComplete` bubbles into the currently-running branch node.

## Example: "attack with priority + fallback"

```
ConditionalNode "Pick Attack Type"
├── Branch[0]
│     ├── evaluations: [ IsStaggered (true) ]
│     └── node: FinisherSequence
├── Branch[1]
│     ├── evaluations: [ IsInMeleeRange (true), HasMeleeWeapon (true) ]
│     └── node: MeleeAttackSequence
├── Branch[2]
│     ├── evaluations: [ HasAmmo (true) ]
│     └── node: RangedAttackSequence
└── fallbackNode: TauntSequence
```

Runtime priority: stagger finisher → melee combo → shoot → taunt if nothing else applies.
