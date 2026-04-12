using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [DefineTaskNode("Play Animator", Category = "Animation", Description = "Plays an Animator state. Ref: target (Animator). Config: stateName (string), layer (int), speed (float), mode (TaskPlayMode) — Once fires and continues, OnceAndWait awaits duration, Loop keeps playing.")]
    public class PlayAnimatorNode : TaskNode<PlayAnimatorNode.Settings>
    {
        [SerializeField] private Animator target;

        protected override async UniTask OnRunning(Settings config, CancellationToken ct)
        {
            target.Play(config.stateName, config.layer);
            target.speed = config.speed;

            if (config.mode == TaskPlayMode.OnceAndWait)
            {
                // Wait one frame for state to register
                await UniTask.Yield(ct);
                var stateInfo = target.GetCurrentAnimatorStateInfo(config.layer);
                var duration = stateInfo.length / Mathf.Max(config.speed, 0.001f);
                await UniTask.Delay(TimeSpan.FromSeconds(duration), cancellationToken: ct);
            }
        }

        [Serializable]
        public class Settings
        {
            public string stateName;
            public int layer;
            public float speed = 1f;
            public TaskPlayMode mode = TaskPlayMode.OnceAndWait;
        }
    }
}
