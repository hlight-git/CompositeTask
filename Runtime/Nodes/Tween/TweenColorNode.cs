using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [DefineTaskNode("Tween Color", Category = "Tween", Description = "Tweens color on SpriteRenderers and/or Graphics. Ref: spriteRenderers[], graphics[]. Config: value (Color), isFrom (bool) — false=tween TO value, true=tween FROM value to current. duration, ease, speedBased.")]
    public class TweenColorNode : TaskNode<TweenColorNode.Settings>
    {
        [SerializeField] private SpriteRenderer[] spriteRenderers;
        [SerializeField] private Graphic[] graphics;

        private Tweener _tween;

        protected override async UniTask OnRunning(Settings config, CancellationToken ct)
        {
            Color from, to;
            if (config.isFrom)
            {
                from = config.value;
                to = GetCurrentColor();
            }
            else
            {
                from = GetCurrentColor();
                to = config.value;
            }

            ApplyColor(from);
            _tween = DOVirtual.Color(from, to, config.duration, ApplyColor)
                .SetEase(config.ease).SetSpeedBased(config.speedBased);
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

        private Color GetCurrentColor()
        {
            if (spriteRenderers is { Length: > 0 } && spriteRenderers[0]) return spriteRenderers[0].color;
            if (graphics is { Length: > 0 } && graphics[0]) return graphics[0].color;
            return Color.white;
        }

        private void ApplyColor(Color color)
        {
            if (spriteRenderers != null)
                foreach (var sr in spriteRenderers) sr.color = color;
            if (graphics != null)
                foreach (var g in graphics) g.color = color;
        }

        [Serializable]
        public class Settings
        {
            public Color value = Color.white;
            public bool isFrom;
            public float duration = 0.5f;
            public Ease ease = Ease.InOutSine;
            public bool speedBased;
        }
    }
}
