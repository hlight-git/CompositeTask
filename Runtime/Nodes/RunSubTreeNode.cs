using System.Threading;
using Apero.Unity.Architecture.DependencyInjection;
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

        public override void ResolveDependencies(IDependencyContext context)
        {
            base.ResolveDependencies(context);
            _subTree?.ResolveDependencies(context);
        }

        public override void ResetTask()
        {
            _subTree?.ResetTree();
            base.ResetTask();
        }

        public override void Dispose()
        {
            _subTree?.Dispose();
            base.Dispose();
        }

        private void OnSubTreeProgress(TaskNode node, float delta)
        {
            if (_subTree?.Root != null)
                Progress = _subTree.Root.Progress;
        }
    }
}
