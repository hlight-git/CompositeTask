using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [Serializable]
    public abstract class ATaskNode
    {
        public string name;
        [Range(0, 1)]
        public float targetProgressToComplete = 1; 
        private float progress;
        protected CancellationTokenSource taskBeginCancellationTokenSource;
        protected CancellationTokenSource taskEndCancellationTokenSource;
        
        public TaskNodeStatus Status { get; private set; }
        public virtual float Progress
        {
            get => progress;
            protected set
            {
                var clampedValue = Mathf.Clamp01(value);
                var delta = clampedValue - progress;
                progress = clampedValue;
                ProgressChanged?.Invoke(this, delta);

                if (progress >= targetProgressToComplete)
                {
                    ForceComplete();
                }
            }
        }
        public virtual event Action<ATaskNode, float> ProgressChanged;
        public virtual event Action<ATaskNode> Completed;
        
        public async UniTask ExecuteAsync(CancellationToken externalCancellationToken)
        {
            if (Status == TaskNodeStatus.Completed) return;
            Status = TaskNodeStatus.Running;
            taskBeginCancellationTokenSource = new CancellationTokenSource();
            taskEndCancellationTokenSource = new CancellationTokenSource();
            var registration = externalCancellationToken.Register(() =>
            {
                taskBeginCancellationTokenSource.Cancel();
                taskEndCancellationTokenSource.Cancel();
            });

            try
            {
                await OnTaskBegin(taskBeginCancellationTokenSource.Token);
                await OnTaskEnd(taskEndCancellationTokenSource.Token);
                ForceCompleteImmediate();
            }
            finally
            {
                await registration.DisposeAsync();
            }
        }

        public virtual void Reset()
        {
            taskBeginCancellationTokenSource?.Cancel();
            taskEndCancellationTokenSource?.Cancel();
            ProgressChanged = null;
            Completed = null;
            Status = TaskNodeStatus.Pending;
        }

        public virtual void ForceComplete()
        {
            switch (Status)
            {
                case TaskNodeStatus.Pending:
                    ForceCompleteImmediate();
                    return;
                case TaskNodeStatus.Running:
                    taskBeginCancellationTokenSource?.Cancel();
                    return;
            }
        }

        public virtual void ForceCompleteImmediate()
        {
            if (Status == TaskNodeStatus.Completed) return;
            taskBeginCancellationTokenSource?.Cancel();
            taskEndCancellationTokenSource?.Cancel();
            Progress = 1;
            Status = TaskNodeStatus.Completed;
            Completed?.Invoke(this);
        }

        public virtual void Dispose()
        {
            taskBeginCancellationTokenSource?.Dispose();
            taskEndCancellationTokenSource?.Dispose();
            taskBeginCancellationTokenSource = null;
            taskEndCancellationTokenSource = null;
            ProgressChanged = null;
            Completed = null;
        }

        protected abstract UniTask OnTaskBegin(CancellationToken cancellationToken);
        protected abstract UniTask OnTaskEnd(CancellationToken cancellationToken);
    }
}