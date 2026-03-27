using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Hlight.Structures.CompositeTask.Runtime
{
    public interface ITaskDefinition : IDisposable
    {
        void Awake();
        UniTask OnRunning(MonoTaskNode monoTaskNode, CancellationToken cancellationToken);
        UniTask OnFinishing(MonoTaskNode monoTaskNode, CancellationToken cancellationToken);
        void OnCompleted(MonoTaskNode monoTaskNode);
    }
}