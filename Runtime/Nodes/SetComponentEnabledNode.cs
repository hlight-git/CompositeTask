using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [DefineTaskNode("Set Component Enabled", Description = "Enables or disables a Behaviour on completion. Ref: target (Behaviour). Config: enabled (bool).")]
    public class SetComponentEnabledNode : TaskNode<SetComponentEnabledNode.Settings>
    {
        [SerializeField] private Behaviour target;

        protected override UniTask OnRunning(Settings config, CancellationToken ct) => UniTask.CompletedTask;

        protected override void OnCompleted()
        {
            base.OnCompleted();
            target.enabled = Config.enabled;
        }

        [Serializable]
        public class Settings
        {
            public bool enabled;
        }
    }
}
