using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    public enum WaitUnit { Frames, Milliseconds, UnscaledMilliseconds }

    [DefineTaskNode("Wait", Description = "Pauses execution. Config: value (int) = amount, unit (WaitUnit) = Frames/Milliseconds/UnscaledMilliseconds.")]
    public class WaitNode : TaskNode<WaitNode.Settings>
    {
        protected override UniTask OnRunning(Settings config, CancellationToken ct)
        {
            return config.unit switch
            {
                WaitUnit.Frames => UniTask.DelayFrame(config.value, cancellationToken: ct),
                WaitUnit.Milliseconds => UniTask.Delay(config.value, cancellationToken: ct),
                WaitUnit.UnscaledMilliseconds => UniTask.Delay(config.value, ignoreTimeScale: true, cancellationToken: ct),
                _ => UniTask.CompletedTask
            };
        }

        [Serializable]
        public class Settings
        {
            public int value = 1000;
            public WaitUnit unit = WaitUnit.Milliseconds;
        }
    }
}
