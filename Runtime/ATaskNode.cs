using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [Serializable]
    public abstract class ATaskNode : IDisposable
    {
        public string name;
        [Range(0, 1)]
        public float targetProgressToComplete = 1; 
        
        private float progress;
        
        protected CancellationTokenSource taskRunningCts;
        protected CancellationTokenSource taskFinishCts;
        
        [Newtonsoft.Json.JsonIgnore]
        public TaskNodeStatus Status { get; private set; }
        
        [Newtonsoft.Json.JsonIgnore]
        public virtual float Progress
        {
            get => progress;
            protected set
            {
                var clampedValue = Mathf.Clamp01(value);
                var delta = clampedValue - progress;
                progress = clampedValue;
                ProgressChanged?.Invoke(this, delta);

                if (Status != TaskNodeStatus.Running) return;
                if (Mathf.Approximately(targetProgressToComplete, 1)) return;
                if (Progress < targetProgressToComplete) return;
                ForceComplete();
            }
        }
        
        public event Action<ATaskNode, float> ProgressChanged;
        public event Action<ATaskNode> Completed;
        
        public async UniTask ExecuteAsync(CancellationToken externalCancellationToken)
        {
            if (Status == TaskNodeStatus.Completed) return;

            Status = TaskNodeStatus.Running;

            try
            {
                OnBeginExecute();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

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
                await Try(RunTheTask(taskRunningCts.Token));
                Status = TaskNodeStatus.Finishing;
                if (!taskFinishCts.IsCancellationRequested)
                    await Try(FinishTheTask(taskFinishCts.Token));
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

        protected virtual void OnCompleted()
        {
            Status = TaskNodeStatus.Completed;
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
                Status = TaskNodeStatus.Failed;
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
            Status = TaskNodeStatus.Pending;
        }

        public virtual void ForceComplete(bool immediate = false)
        {
            switch (Status)
            {
                case TaskNodeStatus.Pending:
                    OnCompleted();
                    return;
                case TaskNodeStatus.Running:
                    taskRunningCts?.Cancel();
                    if (immediate) taskFinishCts?.Cancel();
                    return;
                case TaskNodeStatus.Finishing:
                    taskFinishCts?.Cancel();
                    return;
            }
        }

        public virtual void Dispose()
        {
            Reset();
        }

        public abstract void Accept(IDependencyInjectionVisitor dependencyInjectionVisitor);

        protected internal abstract void OnBeginExecute();
        protected abstract UniTask RunTheTask(CancellationToken cancellationToken);
        protected abstract UniTask FinishTheTask(CancellationToken cancellationToken);
    }
}