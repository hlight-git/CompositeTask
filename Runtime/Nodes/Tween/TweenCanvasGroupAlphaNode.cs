using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [DefineTaskNode("Tween Canvas Group Alpha", Category = "Tween", Description = "Tweens CanvasGroup alpha. Ref: target (CanvasGroup). Config: value (float 0-1), isFrom (bool), duration, ease.")]
    public class TweenCanvasGroupAlphaNode : TaskNode<TweenCanvasGroupAlphaNode.Settings>
    {
        [SerializeField] private CanvasGroup target;
        private Tweener _tween;

        protected override async UniTask OnRunning(Settings config, CancellationToken ct)
        {
            if (config.isFrom)
            {
                float current = target.alpha;
                target.alpha = config.value;
                _tween = target.DOFade(current, config.duration).SetEase(config.ease);
            }
            else
            {
                _tween = target.DOFade(config.value, config.duration).SetEase(config.ease);
            }
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
            [Range(0f, 1f)] public float value;
            public bool isFrom;
            public float duration = 0.5f;
            public Ease ease = Ease.InOutSine;
        }
    }
}
