using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Fires all active children concurrently, then waits for all to complete.
    /// Detects runtime child insertion via OnTransformChildrenChanged and fires new children immediately.
    /// </summary>
    public class ParallelNode : CompositeNode
    {
        private readonly HashSet<TaskNode> _firedChildren = new();
        private System.Func<bool> _allCompletedCheck;
        private CancellationToken _runningCt;

        protected override async UniTask OnRunning(CancellationToken ct)
        {
            _runningCt = ct;
            _firedChildren.Clear();

            foreach (var child in GetActiveChildren())
                FireChild(child, ct);

            await UniTask.WaitUntil(_allCompletedCheck ??= IsAllChildrenCompleted, cancellationToken: ct);

            // Clear token first — OnTransformChildrenChanged guards on Status != Running
            _runningCt = default;
            _firedChildren.Clear();
        }

        protected override UniTask OnFinishing(CancellationToken ct)
        {
            if (IsAllChildrenCompleted()) return UniTask.CompletedTask;
            return UniTask.WaitUntil(_allCompletedCheck ??= IsAllChildrenCompleted, cancellationToken: ct);
        }

        /// <summary>Fire newly-added children immediately during execution.</summary>
        protected override void OnTransformChildrenChanged()
        {
            base.OnTransformChildrenChanged();
            if (Status != TaskStatus.Running) return;

            foreach (var child in GetActiveChildren())
            {
                if (!_firedChildren.Contains(child))
                    FireChild(child, _runningCt);
            }
        }

        internal override void OnChildDisabled(TaskNode child)
        {
            base.OnChildDisabled(child);
            if (Status == TaskStatus.Running && child.Status == TaskStatus.Running)
                child.ForceComplete(immediate: true);
        }

        private void FireChild(TaskNode child, CancellationToken ct)
        {
            if (child == null || ct.IsCancellationRequested) return;
            if (child.Status != TaskStatus.Pending) return;
            _firedChildren.Add(child);
            child.ExecuteAsync(ct).Forget(Debug.LogException);
        }

        public override void ResetTask()
        {
            _firedChildren.Clear();
            _runningCt = default;
            base.ResetTask();
        }

        public override void Dispose()
        {
            _firedChildren.Clear();
            _runningCt = default;
            base.Dispose();
        }
    }
}
