using System;
using System.Collections.Generic;
using System.Threading;
using Apero.Unity.Architecture.DependencyInjection;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Branching node: evaluates conditions top-to-bottom, executes the first matching branch.
    /// A branch with no condition acts as fallback (always matches).
    /// If no branch matches, completes immediately.
    /// </summary>
    [DefineTaskNode("Conditional", Description = "Branching node. Evaluates branches top-to-bottom, executes the first where TaskCondition.Evaluate() returns true. A branch with no condition is a fallback (always matches).")]
    public class ConditionalNode : TaskNode
    {
        [SerializeField] private List<Branch> _branches = new();

        public IReadOnlyList<Branch> Branches => _branches;

        protected override async UniTask OnRunning(CancellationToken ct)
        {
            foreach (var branch in _branches)
            {
                ct.ThrowIfCancellationRequested();
                if (branch.node == null) continue;
                if (!branch.node.gameObject.activeSelf) continue;

                // null condition = fallback (always true)
                if (branch.condition != null && !branch.condition.Evaluate()) continue;

                await branch.node.ExecuteAsync(ct);
                return;
            }
        }

        protected override void OnWarm()
        {
            foreach (var branch in _branches)
                branch.node?.Warm();
        }

        public override void ResolveDependencies(IDependencyContext context)
        {
            base.ResolveDependencies(context);
            foreach (var branch in _branches)
                branch.node?.ResolveDependencies(context);
        }

        public override void ResetTask()
        {
            foreach (var branch in _branches)
                branch.node?.ResetTask();
            base.ResetTask();
        }

        public override void Dispose()
        {
            foreach (var branch in _branches)
                branch.node?.Dispose();
            base.Dispose();
        }

        public override bool IsValidChild(TaskNode child)
        {
            foreach (var branch in _branches)
            {
                if (branch.node == child) return true;
            }
            return false;
        }

        public override void GetValidationWarnings(List<string> warnings)
        {
            for (int i = 0; i < _branches.Count; i++)
            {
                if (_branches[i].node == null)
                    warnings.Add($"Branch [{i}]: node is not assigned.");
            }
        }

        [Serializable]
        public class Branch
        {
            [Tooltip("Condition to evaluate. Null = fallback (always matches).")]
            public TaskCondition condition;
            [Tooltip("Node to execute if condition passes.")]
            public TaskNode node;
        }
    }
}
