using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [DefineTaskNode("Time Scale", Description = "Sets Time.timeScale on completion. Restores previous value on reset. Config: timeScale (float, min 0). Use 0 for pause, <1 for slow-mo.")]
    public class TimeScaleNode : TaskNode<TimeScaleNode.Settings>
    {
        private float _previousTimeScale = 1f;

        protected override UniTask OnRunning(Settings config, CancellationToken ct) => UniTask.CompletedTask;

        protected override void OnCompleted()
        {
            _previousTimeScale = Time.timeScale;
            base.OnCompleted();
            Time.timeScale = Config.timeScale;
        }

        protected override void OnReset()
        {
            Time.timeScale = _previousTimeScale;
        }

        [Serializable]
        public class Settings
        {
            [Min(0f)] public float timeScale = 1f;
        }
    }
}
