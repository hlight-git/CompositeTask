using System.Threading;
using Cysharp.Threading.Tasks;

namespace Hlight.Structures.CompositeTask.Runtime
{
    public interface ITaskDefinition
    {
        UniTask OnRunning(MonoTaskNode monoTaskNode, CancellationToken cancellationToken);
        UniTask OnFinishing(MonoTaskNode monoTaskNode, CancellationToken cancellationToken);
        void OnCompleted(MonoTaskNode monoTaskNode);
        void Dispose(MonoTaskNode monoTaskNode);
    }
}