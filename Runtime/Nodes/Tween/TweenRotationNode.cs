using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [DefineTaskNode("Tween Rotation", Category = "Tween", Description = "Tweens Transform rotation (euler). Ref: target (Transform). Config: value (Vector3), isFrom (bool), duration, ease, localSpace, speedBased, relative.")]
    public class TweenRotationNode : TaskNode<TweenRotationNode.Settings>
    {
        [SerializeField] private Transform target;
        private Tweener _tween;

        protected override async UniTask OnRunning(Settings config, CancellationToken ct)
        {
            var dur = config.speedBased ? 1f : config.duration;
            _tween = config.localSpace
                ? target.DOLocalRotate(config.value, dur)
                : target.DORotate(config.value, dur);

            if (config.isFrom) _tween.From(config.relative);
            else if (config.relative) _tween.SetRelative();
            _tween.SetEase(config.ease).SetSpeedBased(config.speedBased);
            await _tween.ToUniTask(cancellationToken: ct);
        }

        protected override void OnCompleted()
        {
            _tween?.Kill();
            _tween = null;
            base.OnCompleted();
        }

        protected override void OnReset()
        {
            _tween?.Kill();
            _tween = null;
        }

        [Serializable]
        public class Settings
        {
            public Vector3 value;
            public bool isFrom;
            public float duration = 0.5f;
            public Ease ease = Ease.InOutSine;
            public bool localSpace = true;
            public bool speedBased;
            public bool relative;
        }
    }
}
