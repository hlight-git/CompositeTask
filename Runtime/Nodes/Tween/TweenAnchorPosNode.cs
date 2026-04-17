using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [DefineTaskNode("Tween Anchor Pos", Category = "Tween", Description = "Tweens RectTransform anchoredPosition. Ref: target (RectTransform). Config: value (Vector2), isFrom (bool), duration, ease, speedBased, relative.")]
    public class TweenAnchorPosNode : TaskNode<TweenAnchorPosNode.Settings>
    {
        [SerializeField] private RectTransform target;
        private Tweener _tween;

        protected override async UniTask OnRunning(Settings config, CancellationToken ct)
        {
            var dur = config.duration;
            _tween = target.DOAnchorPos(config.value, dur);

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
            public Vector2 value;
            public bool isFrom;
            public float duration = 0.5f;
            public Ease ease = Ease.InOutSine;
            public bool speedBased;
            public bool relative;
        }
    }
}
