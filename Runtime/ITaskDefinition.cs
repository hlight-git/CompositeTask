using System.Threading;
using Cysharp.Threading.Tasks;

namespace Hlight.Structures.CompositeTask.Runtime
{
    public interface ITaskDefinition
    {
        UniTask OnBegin(MonoTaskNode monoTaskNode, CancellationToken cancellationToken);
        UniTask OnEnd(MonoTaskNode monoTaskNode, CancellationToken cancellationToken);
        void OnCompleted(MonoTaskNode monoTaskNode);
        void OnCanceledWhenRunning(MonoTaskNode monoTaskNode);
    }
}