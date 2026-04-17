using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [DefineTaskNode("Force Complete", Description = "Force-completes another TaskNode on completion. Ref: target (TaskNode). Config: immediate (bool) — true skips OnFinishing phase.")]
    public class ForceCompleteNode : TaskNode<ForceCompleteNode.Settings>
    {
        [SerializeField] private TaskNode target;

        protected override UniTask OnRunning(Settings config, CancellationToken ct) => UniTask.CompletedTask;

        protected override void OnCompleted()
        {
            base.OnCompleted();
            target.ForceComplete(Config.immediate);
        }

        [Serializable]
        public class Settings
        {
            public bool immediate;
        }
    }
}
