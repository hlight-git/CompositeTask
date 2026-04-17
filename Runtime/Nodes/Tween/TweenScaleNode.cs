using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [DefineTaskNode("Tween Scale", Category = "Tween", Description = "Tweens Transform local scale. Ref: target (Transform). Config: value (Vector3), isFrom (bool), duration, ease, speedBased, relative.")]
    public class TweenScaleNode : TaskNode<TweenScaleNode.Settings>
    {
        [SerializeField] private Transform target;
        private Tweener _tween;

        protected override async UniTask OnRunning(Settings config, CancellationToken ct)
        {
            var dur = config.duration;
            _tween = target.DOScale(config.value, dur);

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
            public Vector3 value = Vector3.one;
            public bool isFrom;
            public float duration = 0.5f;
            public Ease ease = Ease.InOutSine;
            public bool speedBased;
            public bool relative;
        }
    }
}
