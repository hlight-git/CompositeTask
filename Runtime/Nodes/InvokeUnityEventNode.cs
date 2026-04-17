using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [DefineTaskNode("Invoke Unity Event", Description = "Invokes a UnityEvent on completion. Ref: onExecute (UnityEvent) — wire callbacks in Inspector.")]
    public class InvokeUnityEventNode : TaskNode
    {
        [SerializeField] private UnityEvent onExecute;

        protected override UniTask OnRunning(CancellationToken ct) => UniTask.CompletedTask;

        protected override void OnCompleted()
        {
            base.OnCompleted();
            onExecute.Invoke();
        }
    }
}
