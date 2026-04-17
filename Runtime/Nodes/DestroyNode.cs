using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [DefineTaskNode("Destroy", Description = "Destroys a GameObject on completion. Ref: target (GameObject). Config: delay (float seconds, 0 = immediate).")]
    public class DestroyNode : TaskNode<DestroyNode.Settings>
    {
        [SerializeField] private GameObject target;

        protected override UniTask OnRunning(Settings config, CancellationToken ct) => UniTask.CompletedTask;

        protected override void OnCompleted()
        {
            base.OnCompleted();
            Destroy(target, Config.delay);
        }

        [Serializable]
        public class Settings
        {
            public float delay;
        }
    }
}
