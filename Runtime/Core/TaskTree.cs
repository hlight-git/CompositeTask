using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Hlight.Structures.CompositeTask.Runtime.Blueprint;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Root manager for a task tree. Manages CTS lifecycle, event forwarding, and execution API.
    /// Not a TaskNode — it's a companion component on the root GameObject.
    /// </summary>
    public class TaskTree : MonoBehaviour
    {
        [SerializeField, Tooltip("Explicit root. If null, auto-detects first TaskNode child.")]
        private TaskNode _rootOverride;

        private TaskNode _root;
        private CancellationTokenSource _cts;

        public TaskNode Root
        {
            get
            {
                if (_root == null) _root = ResolveRoot();
                return _root;
            }
        }

        public bool IsRunning => Root != null &&
            (Root.Status == TaskStatus.Running || Root.Status == TaskStatus.Finishing);

        public event Action<TaskNode, float> ProgressChanged;
        public event Action<TaskNode, TaskStatus> StatusChanged;

        // ── Execution ──────────────────────────────────────────────────

        public CancellationTokenSource Execute()
        {
            if (IsRunning) return _cts;

            CancelCts();
            _cts = new CancellationTokenSource();
            ExecuteInternal(_cts.Token).Forget(Debug.LogException);
            return _cts;
        }

        /// <summary>Awaitable execution. Creates a linked CTS so ResetTree() can cancel.</summary>
        public async UniTask ExecuteAsync(CancellationToken ct)
        {
            if (IsRunning) return;

            CancelCts();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await ExecuteInternal(_cts.Token);
        }

        public void ForceComplete(bool immediate = false) => Root?.ForceComplete(immediate);

        public void ResetTree()
        {
            CancelCts();
            UnsubscribeRoot();
            Root?.ResetTask();
            _root = null;
            ProgressChanged = null;
            StatusChanged = null;
        }

        public void Dispose()
        {
            CancelCts();
            UnsubscribeRoot();
            if (_root != null)
            {
                _root.Dispose();
                _root = null;
            }
            ProgressChanged = null;
            StatusChanged = null;
        }

        public void Accept(IDependencyInjectionVisitor visitor) => Root?.Accept(visitor);

        /// <summary>Pre-warms the entire tree without executing. Safe to call before Execute().</summary>
        public void Warm() => Root?.Warm();

        /// <summary>
        /// Clears existing TaskNode children, then builds a new hierarchy from JSON under this transform.
        /// Override <see cref="GetNodePresets"/> to supply preset prefabs for leaf nodes.
        /// </summary>
        public void LoadFromJson(string json)
        {
            var blueprint = BlueprintJsonParser.Parse(json);
            if (blueprint?.root == null) return;

            CancelCts();
            UnsubscribeRoot();
            _root = null;

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.GetComponent<TaskNode>() == null) continue;
                // Detach before destroy so BuildNode won't see stale children in childCount.
                child.SetParent(null, false);
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }

            TaskTreeBuilder.BuildNode(blueprint.root, transform, GetNodePresets());
        }

        /// <summary>Override to supply preset prefabs used during <see cref="LoadFromJson"/>.</summary>
        protected virtual IReadOnlyList<TaskNodePreset> GetNodePresets() => null;

        private void OnDestroy() => Dispose();

        // ── Internals ──────────────────────────────────────────────────

        private async UniTask ExecuteInternal(CancellationToken ct)
        {
            var root = Root;
            if (root == null)
            {
                Debug.LogWarning($"[TaskTree] No root found on '{gameObject.name}'.", this);
                return;
            }

            UnsubscribeRoot();
            SubscribeRoot(root);
            root.Warm();
            await root.ExecuteAsync(ct);
        }

        private TaskNode ResolveRoot()
        {
            if (_rootOverride != null) return _rootOverride;

            for (int i = 0; i < transform.childCount; i++)
            {
                if (transform.GetChild(i).TryGetComponent<TaskNode>(out var node))
                    return node;
            }

            return GetComponent<TaskNode>();
        }

        private void SubscribeRoot(TaskNode root)
        {
            root.ProgressChanged += OnRootProgress;
            root.StatusChanged += OnRootStatusChanged;
        }

        private void UnsubscribeRoot()
        {
            if (_root == null) return;
            _root.ProgressChanged -= OnRootProgress;
            _root.StatusChanged -= OnRootStatusChanged;
        }

        private void CancelCts()
        {
            if (_cts == null) return;
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }

        private void OnRootProgress(TaskNode node, float delta) => ProgressChanged?.Invoke(node, delta);
        private void OnRootStatusChanged(TaskNode node, TaskStatus status) => StatusChanged?.Invoke(node, status);
    }
}
