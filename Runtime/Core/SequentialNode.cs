using System.Threading;
using Cysharp.Threading.Tasks;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Executes active children one-by-one in sibling order.
    /// Re-scans for the next Pending child each iteration to handle runtime hierarchy changes.
    /// Subclass to customize child selection or inject async logic between children via
    /// <see cref="SelectNextChild"/> / <see cref="OnChildTransition"/>.
    /// </summary>
    public class SequentialNode : CompositeNode
    {
        private System.Func<bool> _allCompletedCheck;

        public TaskNode CurrentChild { get; private set; }
        public int CurrentChildIndex { get; private set; } = -1;

        protected override async UniTask OnRunning(CancellationToken ct)
        {
            CurrentChild = null;
            CurrentChildIndex = -1;
            TaskNode previous = null;

            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    var next = SelectNextChild(out var nextIndex);
                    if (next == null) break;

                    await OnChildTransition(previous, next, ct);
                    ct.ThrowIfCancellationRequested();

                    CurrentChild = next;
                    CurrentChildIndex = nextIndex;
                    await next.ExecuteAsync(ct);
                    previous = next;
                }
            }
            finally
            {
                CurrentChild = null;
                CurrentChildIndex = -1;
            }
        }

        protected override UniTask OnFinishing(CancellationToken ct)
        {
            if (IsAllChildrenCompleted()) return UniTask.CompletedTask;
            _allCompletedCheck ??= IsAllChildrenCompleted;
            return UniTask.WaitUntil(_allCompletedCheck, cancellationToken: ct);
        }

        /// <summary>
        /// Async hook fired before each child executes. <paramref name="previous"/> is the just-completed
        /// child (null for the first child). <paramref name="next"/> is the child about to run.
        /// Override to insert delays, transitions, or conditional skips.
        /// Default: completes immediately (no-op).
        /// </summary>
        protected virtual UniTask OnChildTransition(TaskNode previous, TaskNode next, CancellationToken ct)
            => UniTask.CompletedTask;

        /// <summary>
        /// Selects the next child to execute. Default: first Pending child in sibling order.
        /// Override to implement priority-based selection, skipping, or shuffled order.
        /// Return null (with <paramref name="index"/> = -1) to end the sequence.
        /// </summary>
        protected virtual TaskNode SelectNextChild(out int index)
        {
            var active = GetActiveChildren();
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].Status == TaskStatus.Pending)
                {
                    index = i;
                    return active[i];
                }
            }
            index = -1;
            return null;
        }
    }
}
