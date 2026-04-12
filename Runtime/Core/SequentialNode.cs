using System.Threading;
using Cysharp.Threading.Tasks;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Executes active children one-by-one in sibling order.
    /// Re-scans for the next Pending child each iteration to handle runtime hierarchy changes.
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

            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    var active = GetActiveChildren();
                    TaskNode next = null;
                    int nextIndex = -1;
                    for (int i = 0; i < active.Count; i++)
                    {
                        if (active[i].Status == TaskStatus.Pending)
                        {
                            next = active[i];
                            nextIndex = i;
                            break;
                        }
                    }

                    if (next == null) break;

                    CurrentChild = next;
                    CurrentChildIndex = nextIndex;
                    await next.ExecuteAsync(ct);
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
    }
}
