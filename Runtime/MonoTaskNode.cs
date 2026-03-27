using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    public class MonoTaskNode : ATaskNode
    {
        [SerializeReference] public ITaskDefinition taskDefinition;

        public override void Accept(IDependencyInjectionVisitor dependencyInjectionVisitor)
        {
            (taskDefinition as IDependencyInjectionVisitable)?.Accept(dependencyInjectionVisitor);
        }

#if COMPOSITE_TASK_DEBUG && UNITY_EDITOR
        protected override async UniTask TryWarningNotUseCancellationToken(bool isRunning)
        {
            await base.TryWarningNotUseCancellationToken(isRunning);
            await UniTask.Yield();
            
            if (isRunning && Status == TaskNodeStatus.Running)
            {
                Debug.LogError($"- Task {name}: The {nameof(ITaskDefinition.OnRunning)} method might not be using CancellationToken correctly!");
                return;
            }
                
            if (!isRunning && Status == TaskNodeStatus.Finishing)
            {
                Debug.LogError($"- Task {name}: The {nameof(ITaskDefinition.OnFinishing)} method might not be using CancellationToken correctly!");
            }
        }
#endif

        protected override UniTask RunTheTask(CancellationToken cancellationToken)
        {
            return taskDefinition.OnRunning(this, cancellationToken);
        }

        protected override UniTask FinishTheTask(CancellationToken cancellationToken)
        {
            return taskDefinition.OnFinishing(this, cancellationToken);
        }

        protected override void OnCompleted()
        {
            taskDefinition?.OnCompleted(this);
            base.OnCompleted();
        }

        public override void Dispose()
        {
            base.Dispose();
            taskDefinition?.Dispose(this);
        }

        public void IncreaseProgress(float value, ITaskDefinition requester)
        {
            if (requester == taskDefinition) Progress += value;
        }
    }
}