using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [DefineTaskNode("Set Transform", Description = "Instantly sets Transform properties on completion. Ref: target (Transform). Config: position/rotation/scale (Vector3), setPosition/setRotation/setScale (bool toggles), localSpace (bool, default true).")]
    public class SetTransformNode : TaskNode<SetTransformNode.Settings>
    {
        [SerializeField] private Transform target;

        protected override UniTask OnRunning(Settings config, CancellationToken ct) => UniTask.CompletedTask;

        protected override void OnCompleted()
        {
            base.OnCompleted();
            if (Config.setPosition)
            {
                if (Config.localSpace) target.localPosition = Config.position;
                else target.position = Config.position;
            }
            if (Config.setRotation)
            {
                if (Config.localSpace) target.localEulerAngles = Config.rotation;
                else target.eulerAngles = Config.rotation;
            }
            if (Config.setScale)
                target.localScale = Config.scale;
        }

        [Serializable]
        public class Settings
        {
            public bool setPosition;
            public Vector3 position;
            public bool setRotation;
            public Vector3 rotation;
            public bool setScale;
            public Vector3 scale = Vector3.one;
            public bool localSpace = true;
        }
    }
}
