using System.Threading;
using Cysharp.Threading.Tasks;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Pure C# task for runtime insertion. Implement this instead of TaskNode
    /// when you only need logic without a persistent MonoBehaviour.
    /// Only OnRunning is required; all other methods are optional.
    /// </summary>
    public interface IInlineTask
    {
        UniTask OnRunning(CancellationToken ct);
        void OnWarm() { }
        void OnReset() { }
        void OnCompleted() { }
        UniTask OnFinishing(CancellationToken ct) => UniTask.CompletedTask;
    }
}
