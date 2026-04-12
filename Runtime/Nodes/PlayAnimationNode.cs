using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [DefineTaskNode("Play Animation", Category = "Animation", Description = "Plays a clip on legacy Animation component. Ref: target (Animation). Config: clipName (string, empty = default clip), speed (float), mode (TaskPlayMode) — Once/OnceAndWait/Loop.")]
    public class PlayAnimationNode : TaskNode<PlayAnimationNode.Settings>
    {
        [SerializeField] private Animation target;

        protected override async UniTask OnRunning(Settings config, CancellationToken ct)
        {
            var clipName = config.clipName;
            if (string.IsNullOrEmpty(clipName) && target.clip != null)
                clipName = target.clip.name;

            switch (config.mode)
            {
                case TaskPlayMode.Loop:
                    target[clipName].wrapMode = WrapMode.Loop;
                    target[clipName].speed = config.speed;
                    target.Play(clipName);
                    break;
                case TaskPlayMode.Once:
                    target[clipName].wrapMode = WrapMode.Once;
                    target[clipName].speed = config.speed;
                    target.Play(clipName);
                    break;
                case TaskPlayMode.OnceAndWait:
                    target[clipName].wrapMode = WrapMode.Once;
                    target[clipName].speed = config.speed;
                    target.Play(clipName);
                    var duration = target[clipName].length / Mathf.Max(config.speed, 0.001f);
                    await UniTask.Delay(TimeSpan.FromSeconds(duration), cancellationToken: ct);
                    break;
            }
        }

        [Serializable]
        public class Settings
        {
            [Tooltip("Leave empty to use the default clip.")]
            public string clipName;
            public float speed = 1f;
            public TaskPlayMode mode = TaskPlayMode.OnceAndWait;
        }
    }
}
