using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [Serializable]
    public abstract class ATask : IDisposable
    {
        public string name;
        [Range(0, 1)]
        public float targetProgressToComplete = 1;

        private float progress;

        protected CancellationTokenSource taskRunningCts;
        protected CancellationTokenSource taskFinishCts;

        [Newtonsoft.Json.JsonIgnore]
        public TaskStatus Status { get; private set; }

        [Newtonsoft.Json.JsonIgnore]
        public virtual float Progress
        {
            get => progress;
            set
            {
                var clampedValue = Mathf.Clamp01(value);
                var delta = clampedValue - progress;
                progress = clampedValue;
                ProgressChanged?.Invoke(this, delta);

                if (Status != TaskStatus.Running) return;
                if (Mathf.Approximately(targetProgressToComplete, 1)) return;
                if (Progress < targetProgressToComplete) return;
                ForceComplete();
            }
        }

        public event Action<ATask, float> ProgressChanged;
        public event Action<ATask> Completed;

        public async UniTask ExecuteAsync(CancellationToken externalCancellationToken)
        {
            if (Status == TaskStatus.Completed) return;

            Status = TaskStatus.Running;
            taskRunningCts = new CancellationTokenSource();
            taskFinishCts = new CancellationTokenSource();

            var registration = externalCancellationToken.Register(CancelAllCancellationTokenSources);

#if COMPOSITE_TASK_DEBUG && UNITY_EDITOR
            var beginDebugRegistration = taskRunningCts.Token
                .Register(() => TryWarningNotUseCancellationToken(true).Forget());
            var endDebugRegistration = taskFinishCts.Token
                .Register(() => TryWarningNotUseCancellationToken(false).Forget());
#endif

            try
            {
                await Try(OnRunning(taskRunningCts.Token));
                Status = TaskStatus.Finishing;
                if (!taskFinishCts.IsCancellationRequested)
                    await Try(OnFinishing(taskFinishCts.Token));
                OnCompleted();
            }
            finally
            {
                await registration.DisposeAsync();
                taskRunningCts?.Dispose();
                taskRunningCts = null;
                taskFinishCts?.Dispose();
                taskFinishCts = null;

#if COMPOSITE_TASK_DEBUG && UNITY_EDITOR
                await beginDebugRegistration.DisposeAsync();
                await endDebugRegistration.DisposeAsync();
#endif
            }
        }

#if COMPOSITE_TASK_DEBUG && UNITY_EDITOR
        protected virtual UniTask TryWarningNotUseCancellationToken(bool isRunning)
        {
            var phase = isRunning ? "Begin phase" : "End phase";
            Debug.Log($"- Task {name}: {phase} canceled.");
            return UniTask.CompletedTask;
        }
#endif

        protected virtual void CancelAllCancellationTokenSources()
        {
            taskRunningCts?.Cancel();
            taskFinishCts?.Cancel();
        }

        protected abstract UniTask OnRunning(CancellationToken cancellationToken);

        protected virtual UniTask OnFinishing(CancellationToken cancellationToken)
        {
            return UniTask.CompletedTask;
        }

        protected virtual void OnCompleted()
        {
            Status = TaskStatus.Completed;
            Progress = 1;
            Completed?.Invoke(this);
        }

        private async UniTask Try(UniTask task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException) {}
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Status = TaskStatus.Failed;
            }
        }

        public virtual void Reset()
        {
            if (taskRunningCts != null)
            {
                taskRunningCts.Cancel();
                taskRunningCts = null;
            }

            if (taskFinishCts != null)
            {
                taskFinishCts.Cancel();
                taskFinishCts = null;
            }

            ProgressChanged = null;
            Completed = null;
            Status = TaskStatus.Pending;
        }

        public virtual void ForceComplete(bool immediate = false)
        {
            switch (Status)
            {
                case TaskStatus.Pending:
                    OnCompleted();
                    return;
                case TaskStatus.Running:
                    taskRunningCts?.Cancel();
                    if (immediate) taskFinishCts?.Cancel();
                    return;
                case TaskStatus.Finishing:
                    taskFinishCts?.Cancel();
                    return;
            }
        }

        public virtual void Dispose()
        {
            Reset();
        }

        public virtual void Awake() {}
        public virtual void Accept(IDependencyInjectionVisitor dependencyInjectionVisitor) {}
    }
}
