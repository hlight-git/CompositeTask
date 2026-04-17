using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [DefineTaskNode("Tween Float", Category = "Tween", Description = "Tweens a float value via UnityEvent<float>. Ref: onValueChanged. Config: from, to (float — explicit both ends since no 'current' to read), duration, ease.")]
    public class TweenFloatNode : TaskNode<TweenFloatNode.Settings>
    {
        [SerializeField] private UnityEvent<float> onValueChanged;
        private Tweener _tween;

        protected override async UniTask OnRunning(Settings config, CancellationToken ct)
        {
            _tween = DOVirtual.Float(config.from, config.to, config.duration, v => onValueChanged.Invoke(v))
                .SetEase(config.ease);
            await _tween.ToUniTask(cancellationToken: ct);
        }

        protected override void OnCompleted()
        {
            _tween?.Kill();
            _tween = null;
            onValueChanged.Invoke(Config.to);
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
            public float from, to = 1f;
            public float duration = 0.5f;
            public Ease ease = Ease.InOutSine;
        }
    }
}
