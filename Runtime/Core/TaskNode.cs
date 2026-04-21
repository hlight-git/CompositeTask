using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Abstract base for all task nodes. Uses MonoBehaviour for Transform hierarchy,
    /// but execution is 100% explicit via <see cref="ExecuteAsync"/>.
    /// Lifecycle: Pending → Running → Finishing → Completed (or Failed at any point).
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class TaskNode : MonoBehaviour, IDisposable
    {
        [SerializeField, Range(0f, 1f)] private float _targetProgressToComplete = 1f;
        [SerializeField, Min(0f)] private float _weight;

        private float _progress;
        private bool _warmed;
        private CancellationTokenSource _taskRunningCts;
        private CancellationTokenSource _taskFinishCts;

        // ── Properties ─────────────────────────────────────────────────

        public string Name
        {
            get => gameObject.name;
            set => gameObject.name = value;
        }

        public float Weight
        {
            get => _weight;
            set => _weight = value;
        }

        public TaskStatus Status { get; private set; }

        public float Progress
        {
            get => _progress;
            protected set
            {
                var clamped = Mathf.Clamp01(value);
                var delta = clamped - _progress;
                if (Mathf.Approximately(delta, 0f)) return;

                _progress = clamped;
                ProgressChanged?.Invoke(this, delta);
                Parent?.OnChildProgressChanged(this, delta);

                if (Status != TaskStatus.Running) return;
                if (_targetProgressToComplete <= 0f) return;
                if (Mathf.Approximately(_targetProgressToComplete, 1f)) return;
                if (_progress < _targetProgressToComplete) return;
                ForceComplete();
            }
        }

        /// <summary>Set by <see cref="CompositeNode"/> during hierarchy rebuild.</summary>
        public CompositeNode Parent { get; internal set; }

        /// <summary>Set progress from external code (e.g. SetProgressNode).</summary>
        public void SetProgress(float value) => Progress = value;

        // ── Events ─────────────────────────────────────────────────────

        public event Action<TaskNode, float> ProgressChanged;
        public event Action<TaskNode, TaskStatus> StatusChanged;

        // ── Execution ──────────────────────────────────────────────────

        public async UniTask ExecuteAsync(CancellationToken externalCt)
        {
            if (Status == TaskStatus.Completed) return;

            SetStatus(TaskStatus.Running);
            _taskRunningCts = new CancellationTokenSource();
            _taskFinishCts = new CancellationTokenSource();

            var registration = externalCt.Register(CancelAllCancellationTokenSources);

            try
            {
                if (_taskRunningCts is { IsCancellationRequested: false })
                    await Try(OnRunning(_taskRunningCts.Token));

                if (Status == TaskStatus.Failed) return;

                SetStatus(TaskStatus.Finishing);

                if (_taskFinishCts is { IsCancellationRequested: false })
                    await Try(OnFinishing(_taskFinishCts.Token));

                if (Status != TaskStatus.Failed
                    && !externalCt.IsCancellationRequested
                    && _taskRunningCts != null && _taskFinishCts != null)
                    OnCompleted();
            }
            finally
            {
                await registration.DisposeAsync();
                _taskRunningCts?.Dispose();
                _taskRunningCts = null;
                _taskFinishCts?.Dispose();
                _taskFinishCts = null;
            }
        }

        public virtual void ForceComplete(bool immediate = false)
        {
            switch (Status)
            {
                case TaskStatus.Pending:
                    OnCompleted();
                    return;
                case TaskStatus.Running:
                    _taskRunningCts?.Cancel();
                    if (immediate) _taskFinishCts?.Cancel();
                    return;
                case TaskStatus.Finishing:
                    _taskFinishCts?.Cancel();
                    return;
            }
        }

        public virtual void ResetTask()
        {
            _taskRunningCts?.Cancel();
            _taskRunningCts?.Dispose();
            _taskRunningCts = null;

            _taskFinishCts?.Cancel();
            _taskFinishCts?.Dispose();
            _taskFinishCts = null;

            OnReset();

            // Fire Pending status for active subscribers before clearing
            SetStatus(TaskStatus.Pending);
            ProgressChanged = null;
            StatusChanged = null;
            _progress = 0f;
            _warmed = false;
        }

        public virtual void Dispose() => ResetTask();

        // ── Lifecycle Hooks ────────────────────────────────────────────

        protected abstract UniTask OnRunning(CancellationToken ct);

        protected virtual UniTask OnFinishing(CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>Called once before first execution. Subclasses override OnWarm for init logic.</summary>
        public void Warm()
        {
            if (_warmed) return;
            _warmed = true;
            OnWarm();
        }

        /// <summary>Override for one-time init. Guaranteed to run only once before first execution.</summary>
        protected virtual void OnWarm() { }

        /// <summary>Called during ResetTask before events are cleared. Override for leaf cleanup.</summary>
        protected virtual void OnReset() { }

        protected virtual void OnCompleted()
        {
            SetStatus(TaskStatus.Completed);
            Progress = 1f;
        }

        protected virtual void CancelAllCancellationTokenSources()
        {
            _taskRunningCts?.Cancel();
            _taskFinishCts?.Cancel();
        }

        // ── DI ─────────────────────────────────────────────────────────

        /// <summary>
        /// Pull dependencies from the provided context. Default: no-op.
        /// Override in subclasses that need runtime-injected services; call base first.
        /// Composite nodes propagate the call to children automatically.
        /// </summary>
        public virtual void ResolveDependencies(IDependencyContext context) { }

        // ── Validation (editor) ────────────────────────────────────────

        /// <summary>Collect editor warnings. Override to add node-specific validation.</summary>
        public virtual void GetValidationWarnings(List<string> warnings) { }

        /// <summary>Whether a child TaskNode is valid under this node. Override to allow children on non-composite nodes.</summary>
        public virtual bool IsValidChild(TaskNode child) => false;

        // ── Active State → Parent Notification ─────────────────────────

        private void OnEnable() => Parent?.InvalidateChildren();
        private void OnDisable() => Parent?.OnChildDisabled(this);

        // ── Helpers ────────────────────────────────────────────────────

        private void SetStatus(TaskStatus status)
        {
            if (Status == status) return;
            Status = status;
            StatusChanged?.Invoke(this, status);
        }

        private async UniTask Try(UniTask task)
        {
            try { await task; }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                SetStatus(TaskStatus.Failed);
            }
        }
    }
}
