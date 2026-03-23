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
        protected CancellationTokenSource taskBeginCancellationTokenSource;
        protected CancellationTokenSource taskEndCancellationTokenSource;
        
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
            }
        }
        public event Action<ATaskNode, float> ProgressChanged;
        public event Action<ATaskNode> Completed;
        
        public async UniTask ExecuteAsync(CancellationToken externalCancellationToken)
        {
            if (Status == TaskNodeStatus.Completed) return;
            Status = TaskNodeStatus.Running;
            
            taskBeginCancellationTokenSource = new CancellationTokenSource();
            taskEndCancellationTokenSource = new CancellationTokenSource();
            
            var registration = externalCancellationToken.Register(CancelAllCancellationTokenSources);

            try
            {
                await OnTaskBegin(taskBeginCancellationTokenSource.Token);
                await OnTaskEnd(taskEndCancellationTokenSource.Token);
                OnCompleted();
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
                    OnCompleted();
                    return;
                case TaskNodeStatus.Running:
                    taskBeginCancellationTokenSource?.Cancel();
                    return;
            }
        }

        protected virtual void CancelAllCancellationTokenSources()
        {
            taskBeginCancellationTokenSource.Cancel();
            taskEndCancellationTokenSource.Cancel();
        }

        protected virtual void OnCompleted()
        {
            Progress = 1;
            Status = TaskNodeStatus.Completed;
            Completed?.Invoke(this);
        }

        public virtual void ForceCompleteImmediate()
        {
            if (Status == TaskNodeStatus.Completed) return;
            taskBeginCancellationTokenSource?.Cancel();
            taskEndCancellationTokenSource?.Cancel();
            OnCompleted();
        }

        public virtual void Dispose()
        {
            Reset();
            taskBeginCancellationTokenSource?.Dispose();
            taskEndCancellationTokenSource?.Dispose();
            taskBeginCancellationTokenSource = null;
            taskEndCancellationTokenSource = null;
        }

        public abstract void Accept(IDependencyInjectionVisitor dependencyInjectionVisitor);
        protected abstract UniTask OnTaskBegin(CancellationToken cancellationToken);
        protected abstract UniTask OnTaskEnd(CancellationToken cancellationToken);
    }
}