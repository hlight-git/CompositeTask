using System.Threading;
using Cysharp.Threading.Tasks;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Internal wrapper that bridges <see cref="IInlineTask"/> into the TaskNode system.
    /// Created automatically by <see cref="CompositeNodeExtensions.InsertInline"/>.
    /// </summary>
    internal class InlineTaskNode : TaskNode
    {
        private IInlineTask _task;

        internal void SetTask(IInlineTask task) => _task = task;

        protected override void OnWarm() => _task?.OnWarm();

        protected override UniTask OnRunning(CancellationToken ct)
            => _task?.OnRunning(ct) ?? UniTask.CompletedTask;

        protected override UniTask OnFinishing(CancellationToken ct)
            => _task?.OnFinishing(ct) ?? UniTask.CompletedTask;

        protected override void OnCompleted()
        {
            base.OnCompleted();
            _task?.OnCompleted();
        }

        protected override void OnReset() => _task?.OnReset();
    }
}
