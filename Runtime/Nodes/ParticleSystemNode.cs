using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    public enum ParticlePlayMode { PlayAndForget, PlayAndWait }

    [DefineTaskNode("Particle System", Category = "VFX", Description = "Plays a ParticleSystem. Ref: target (ParticleSystem). Config: mode (ParticlePlayMode) — PlayAndForget continues immediately, PlayAndWait awaits until particle stops.")]
    public class ParticleSystemNode : TaskNode<ParticleSystemNode.Settings>
    {
        [SerializeField] private ParticleSystem target;

        protected override async UniTask OnRunning(Settings config, CancellationToken ct)
        {
            target.Play();

            if (config.mode == ParticlePlayMode.PlayAndWait)
                await UniTask.WaitUntil(() => !target.isPlaying, cancellationToken: ct);
        }

        [Serializable]
        public class Settings
        {
            public ParticlePlayMode mode = ParticlePlayMode.PlayAndWait;
        }
    }
}
