using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [DefineTaskNode("Set Active GO", Description = "Activates or deactivates a GameObject on completion. Ref: target (GameObject). Config: active (bool).")]
    public class SetGameObjectActivationNode : TaskNode<SetGameObjectActivationNode.Settings>
    {
        [SerializeField] private GameObject target;

        protected override UniTask OnRunning(Settings config, CancellationToken ct)
        {
            return UniTask.CompletedTask;
        }

        protected override void OnCompleted()
        {
            base.OnCompleted();
            if (target != null)
                target.SetActive(Config.active);
        }

        [Serializable]
        public class Settings
        {
            public bool active;
        }
    }
}
