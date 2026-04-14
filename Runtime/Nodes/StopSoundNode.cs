using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [DefineTaskNode("Stop Sound", Category = "Audio", Description = "Stops audio playback. Ref: source (AudioSource).")]
    public class StopSoundNode : TaskNode
    {
        [SerializeField] private AudioSource source;

        protected override UniTask OnRunning(CancellationToken ct)
        {
            source.Stop();
            return UniTask.CompletedTask;
        }
    }
}
