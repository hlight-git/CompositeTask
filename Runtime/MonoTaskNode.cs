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

        protected override UniTask OnTaskBegin(CancellationToken cancellationToken)
        {
            return taskDefinition.OnBegin(this, cancellationToken);
        }

        protected override UniTask OnTaskEnd(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return UniTask.CompletedTask;
            return taskDefinition.OnEnd(this, cancellationToken);
        }

        protected override void OnCompleted()
        {
            taskDefinition?.OnCompleted(this);
            base.OnCompleted();
        }

        protected override void CancelAllCancellationTokenSources()
        {
            base.CancelAllCancellationTokenSources();
            taskDefinition.OnCanceledWhenRunning(this);
        }
    }
}