using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [DefineTaskNode("Play Sound", Category = "Audio", Description = "Plays audio. Ref: source (AudioSource), clip (AudioClip, optional override). Config: mode (TaskPlayMode) — Once fires and continues, OnceAndWait awaits clip length, Loop sets source.loop.")]
    public class PlaySoundNode : TaskNode<PlaySoundNode.Settings>
    {
        [SerializeField] private AudioSource source;
        [SerializeField, Tooltip("Optional override. Uses AudioSource.clip if null.")]
        private AudioClip clip;

        protected override async UniTask OnRunning(Settings config, CancellationToken ct)
        {
            var c = clip != null ? clip : source.clip;

            switch (config.mode)
            {
                case TaskPlayMode.Once:
                    source.loop = false;
                    source.PlayOneShot(c);
                    break;
                case TaskPlayMode.OnceAndWait:
                    source.loop = false;
                    source.PlayOneShot(c);
                    await UniTask.Delay(TimeSpan.FromSeconds(c.length), cancellationToken: ct);
                    break;
                case TaskPlayMode.Loop:
                    source.clip = c;
                    source.loop = true;
                    source.Play();
                    break;
            }
        }

        [Serializable]
        public class Settings
        {
            public TaskPlayMode mode = TaskPlayMode.OnceAndWait;
        }
    }
}
