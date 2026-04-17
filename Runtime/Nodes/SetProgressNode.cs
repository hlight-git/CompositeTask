using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [DefineTaskNode("Set Progress", Description = "Sets progress on another TaskNode on completion. Ref: target (TaskNode). Config: progress (float 0-1).")]
    public class SetProgressNode : TaskNode<SetProgressNode.Settings>
    {
        [SerializeField] private TaskNode target;

        protected override UniTask OnRunning(Settings config, CancellationToken ct) => UniTask.CompletedTask;

        protected override void OnCompleted()
        {
            base.OnCompleted();
            target.SetProgress(Config.progress);
        }

        [Serializable]
        public class Settings
        {
            [Range(0f, 1f)] public float progress;
        }
    }
}
