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
            taskDefinition.Accept(dependencyInjectionVisitor);
        }

        protected override UniTask OnTaskBegin(CancellationToken cancellationToken)
        {
            return taskDefinition.OnBegin(this, cancellationToken);
        }

        protected override UniTask OnTaskEnd(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return UniTask.CompletedTask;
            return taskDefinition.OnEnd(this, cancellationToken);
        }

        public override void ForceCompleteImmediate()
        {
            if (Status == TaskNodeStatus.Completed) return;
            taskDefinition?.OnCompleted(this);
            base.ForceCompleteImmediate();
        }
    }
}