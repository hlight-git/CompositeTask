using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>Leaf node that executes another TaskTree (tree-in-tree).</summary>
    [DefineTaskNode("Run Sub Tree", Description = "Executes another TaskTree and awaits its completion. Ref: subTree (TaskTree). Forwards progress from sub-tree root.")]
    public class RunSubTreeNode : TaskNode
    {
        [SerializeField] private TaskTree _subTree;

        protected override async UniTask OnRunning(CancellationToken ct)
        {
            if (_subTree == null)
            {
                Debug.LogWarning($"[RunSubTreeNode] No sub-tree assigned on '{gameObject.name}'.", this);
                return;
            }

            _subTree.ProgressChanged += OnSubTreeProgress;
            try
            {
                await _subTree.ExecuteAsync(ct);
            }
            finally
            {
                _subTree.ProgressChanged -= OnSubTreeProgress;
            }
        }

        public override void ResolveFrom(IServiceLocator locator)
        {
            base.ResolveFrom(locator);
            _subTree?.ResolveFrom(locator);
        }

        public override void ResetTask()
        {
            // Parent-first: cancel our own CTS before propagating into the sub-tree.
            // Sync UniTask continuations from the sub-tree's cancel would otherwise
            // re-enter our state machine and reach OnCompleted before SetStatus(Pending).
            base.ResetTask();
            _subTree?.ResetTree();
        }

        public override void Dispose()
        {
            base.Dispose();
            _subTree?.Dispose();
        }

        private void OnSubTreeProgress(TaskNode node, float delta)
        {
            if (_subTree?.Root != null)
                Progress = _subTree.Root.Progress;
        }
    }
}
