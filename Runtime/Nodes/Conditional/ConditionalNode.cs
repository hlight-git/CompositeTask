using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Branching node: evaluates branches top-to-bottom and executes the first whose
    /// evaluations all match their expected values. Each branch can compose multiple
    /// <see cref="Evaluation"/> entries (logical AND). If no branch matches, the
    /// <see cref="fallbackNode"/> runs.
    /// </summary>
    [DefineTaskNode("Conditional", Description = "Branching node. Evaluates branches top-to-bottom; the first branch whose Evaluation entries all match runs its node. If none match, the fallbackNode runs.")]
    public class ConditionalNode : TaskNode
    {
        [SerializeField] private List<Branch> _branches = new();
        [SerializeField] private TaskNode fallbackNode;

        public IReadOnlyList<Branch> Branches => _branches;
        public TaskNode FallbackNode => fallbackNode;

        protected override async UniTask OnRunning(CancellationToken ct)
        {
            foreach (var branch in _branches)
            {
                ct.ThrowIfCancellationRequested();
                if (branch.node == null) continue;
                if (!branch.node.gameObject.activeSelf) continue;

                if (branch.evaluations == null || branch.evaluations.Length == 0) continue;

                if (!EvaluateBranch(branch)) continue;

                await branch.node.ExecuteAsync(ct);
                return;
            }

            if (fallbackNode == null) return;
            await fallbackNode.ExecuteAsync(ct);
        }

        private static bool EvaluateBranch(Branch branch)
        {
            foreach (var evaluation in branch.evaluations)
            {
                if (evaluation.condition == null) return false;

                var actual = evaluation.useLastResult
                    ? evaluation.condition.LastResult
                    : evaluation.condition.Evaluate();

                if (actual != evaluation.expectedValue) return false;
            }
            return true;
        }

        protected override void OnWarm()
        {
            foreach (var branch in _branches)
                branch.node?.Warm();
            fallbackNode?.Warm();
        }

        public override void ResolveFrom(IServiceLocator locator)
        {
            base.ResolveFrom(locator);
            foreach (var branch in _branches)
                branch.node?.ResolveFrom(locator);
            fallbackNode?.ResolveFrom(locator);
        }

        public override void ResetTask()
        {
            foreach (var branch in _branches)
                branch.node?.ResetTask();
            fallbackNode?.ResetTask();
            base.ResetTask();
        }

        public override void Dispose()
        {
            foreach (var branch in _branches)
                branch.node?.Dispose();
            fallbackNode?.Dispose();
            base.Dispose();
        }

        public override bool IsValidChild(TaskNode child)
        {
            if (child == fallbackNode) return true;
            foreach (var branch in _branches)
            {
                if (branch.node == child) return true;
            }
            return false;
        }

        public override void GetValidationWarnings(List<string> warnings)
        {
            var warningBuilder = new StringBuilder();

            for (int i = 0; i < _branches.Count; i++)
            {
                warningBuilder.Clear();
                var branch = _branches[i];

                if (branch.evaluations == null || branch.evaluations.Length == 0)
                    warningBuilder.Append("\n- Missing evaluations.");
                else
                {
                    foreach (var evaluation in branch.evaluations)
                    {
                        if (evaluation.condition != null) continue;
                        warningBuilder.Append($"\n- At least one evaluation is missing `{nameof(Evaluation.condition)}`.");
                        break;
                    }
                }

                if (branch.node == null)
                    warningBuilder.Append($"\n- Missing `{nameof(Branch.node)}`.");
                else if (!branch.node.transform.IsChildOf(transform))
                    warningBuilder.Append($"\n- Node `{branch.node.name}` is not a child of this Conditional.");

                if (warningBuilder.Length <= 0) continue;
                warningBuilder.Insert(0, $"Branch [{i}]:");
                warnings.Add(warningBuilder.ToString());
            }

            warningBuilder.Clear();
            if (fallbackNode == null)
                warningBuilder.Append($"`{nameof(fallbackNode)}` is not assigned.");
            else if (!fallbackNode.transform.IsChildOf(transform))
                warningBuilder.Append($"`{nameof(fallbackNode)}`: `{fallbackNode.name}` is not a child of this Conditional.");
            if (warningBuilder.Length > 0)
                warnings.Add(warningBuilder.ToString());
        }

        [Serializable]
        public class Branch
        {
            [Tooltip("Evaluation entries combined with logical AND. The branch matches only when every evaluation matches its expectedValue. Empty/null = branch is skipped (use fallbackNode for unconditional default).")]
            public Evaluation[] evaluations;

            [Tooltip("Node to execute if all evaluations match.")]
            public TaskNode node;
        }

        [Serializable]
        public class Evaluation
        {
            [Tooltip("If true, reuse the condition's cached LastResult instead of re-evaluating. Useful when several branches share a condition and re-evaluation would be wasteful or non-idempotent.")]
            public bool useLastResult;

            [Tooltip("Condition to evaluate.")]
            public TaskCondition condition;

            [Tooltip("Match this value against the condition's result. Set to false to invert the check (i.e. branch matches when condition is false).")]
            public bool expectedValue = true;
        }
    }
}
