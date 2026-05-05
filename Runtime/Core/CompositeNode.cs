using System;
using System.Collections.Generic;
using UnityEngine;


namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Base for composite task nodes (Sequential, Parallel).
    /// Reads children from Transform hierarchy with lazy dirty-flag caching.
    /// </summary>
    public abstract class CompositeNode : TaskNode
    {
        private readonly List<TaskNode> _children = new();
        private readonly List<TaskNode> _activeChildrenCache = new();
        private bool _childrenDirty = true;
        private bool _activeCacheDirty = true;
        private float _cachedTotalWeight;

        public IReadOnlyList<TaskNode> Children
        {
            get
            {
                EnsureChildren();
                return _children;
            }
        }

        // ── Hierarchy Sync ─────────────────────────────────────────────

        protected virtual void OnTransformChildrenChanged()
        {
            _childrenDirty = true;
            _activeCacheDirty = true;
        }

        public void InvalidateChildren()
        {
            _childrenDirty = true;
            _activeCacheDirty = true;
        }

        protected void EnsureChildren()
        {
            if (!_childrenDirty) return;
            RebuildChildren();
        }

        private void RebuildChildren()
        {
            foreach (var child in _children)
            {
                if (child != null && child.Parent == this)
                    child.Parent = null;
            }

            _children.Clear();

            for (int i = 0; i < transform.childCount; i++)
            {
                if (transform.GetChild(i).TryGetComponent<TaskNode>(out var node))
                {
                    _children.Add(node);
                    node.Parent = this;
                }
            }

            _childrenDirty = false;
            RefreshActiveCache();
        }

        protected List<TaskNode> GetActiveChildren()
        {
            EnsureChildren();
            if (_activeCacheDirty) RefreshActiveCache();
            return _activeChildrenCache;
        }

        private void RefreshActiveCache()
        {
            float oldTotalWeight = _cachedTotalWeight;

            _activeChildrenCache.Clear();
            _cachedTotalWeight = 0f;
            foreach (var child in _children)
            {
                if (child != null && child.gameObject.activeSelf)
                {
                    _activeChildrenCache.Add(child);
                    _cachedTotalWeight += child.Weight;
                }
            }
            _activeCacheDirty = false;

            // Rescale progress when weight changes mid-execution (child added/removed)
            if (Status == TaskStatus.Running
                && oldTotalWeight > 0f
                && _cachedTotalWeight > 0f
                && !Mathf.Approximately(oldTotalWeight, _cachedTotalWeight))
            {
                Progress = Progress * oldTotalWeight / _cachedTotalWeight;
            }
        }

        // ── Progress Aggregation ───────────────────────────────────────

        internal void OnChildProgressChanged(TaskNode child, float delta)
        {
            if (child == null || !child.gameObject.activeSelf) return;
            if (_cachedTotalWeight <= 0f) return;
            Progress += delta * (child.Weight / _cachedTotalWeight);
        }

        internal virtual void OnChildDisabled(TaskNode child)
        {
            _activeCacheDirty = true;
        }

        // ── Lifecycle Propagation ──────────────────────────────────────

        public override void ResolveFrom(IServiceLocator locator)
        {
            base.ResolveFrom(locator);
            EnsureChildren();
            foreach (var child in _children) child?.ResolveFrom(locator);
        }

        protected override void OnWarm()
        {
            EnsureChildren();
            foreach (var child in _children) child?.Warm();
        }

        public override void ForceComplete(bool immediate = false)
        {
            // Cancel our own CTS FIRST. UniTask runs continuations synchronously, so cancelling
            // a child's CTS first would let our OnRunning state machine resume mid-iteration,
            // pass the loop's ct.ThrowIfCancellationRequested (our ct isn't cancelled yet),
            // and start the next child — re-entry bug. Cancelling parent first ensures any
            // synchronous re-entry sees our ct already cancelled and bails out.
            // Running/Finishing children are also cascaded automatically via the
            // externalCt.Register(CancelAllCancellationTokenSources) chain set up in
            // TaskNode.ExecuteAsync — so the foreach below mostly serves Pending children
            // (which don't have ExecuteAsync running, hence no Register callback to cascade).
            base.ForceComplete(immediate);

            foreach (var child in GetActiveChildren())
            {
                if (child.Status is TaskStatus.Running or TaskStatus.Finishing or TaskStatus.Pending)
                    child.ForceComplete(immediate);
            }
        }

        public override void ResetTask()
        {
            // Same parent-first rationale as ForceComplete: cancelling our CTS first ensures
            // any synchronous continuation re-entering our OnRunning sees a cancelled ct.
            base.ResetTask();

            EnsureChildren();
            foreach (var child in _children)
            {
                if (child == null) continue;
                try { child.ResetTask(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        public override void Dispose()
        {
            // Parent-first: Dispose calls ResetTask which cancels CTSes; same re-entry
            // hazard as ForceComplete if children are processed first.
            base.Dispose();

            EnsureChildren();
            foreach (var child in _children)
            {
                if (child == null) continue;
                try { child.Dispose(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        public override bool IsValidChild(TaskNode child) => true;

        public override void GetValidationWarnings(List<string> warnings)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.GetComponent<TaskNode>() == null)
                    warnings.Add($"Child '{child.name}' has no TaskNode component.");
            }
        }

        // ── Helpers ────────────────────────────────────────────────────

        protected bool IsAllChildrenCompleted()
        {
            var active = GetActiveChildren();
            foreach (var child in active)
            {
                if (child.Status != TaskStatus.Completed && child.Status != TaskStatus.Failed)
                    return false;
            }
            return true;
        }
    }
}
