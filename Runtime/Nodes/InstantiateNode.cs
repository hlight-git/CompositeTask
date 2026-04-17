using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    [DefineTaskNode("Instantiate", Description = "Instantiates a prefab on completion. Ref: prefab (GameObject), parent (Transform, optional). Config: destroyOnReset (bool) — destroys instance when tree resets.")]
    public class InstantiateNode : TaskNode<InstantiateNode.Settings>
    {
        [SerializeField] private GameObject prefab;
        [SerializeField] private Transform parent;

        private GameObject _instance;

        protected override UniTask OnRunning(Settings config, CancellationToken ct) => UniTask.CompletedTask;

        protected override void OnCompleted()
        {
            base.OnCompleted();
            _instance = Instantiate(prefab, parent);
        }

        protected override void OnReset()
        {
            if (Config.destroyOnReset && _instance != null)
            {
                Destroy(_instance);
                _instance = null;
            }
        }

        [Serializable]
        public class Settings
        {
            public bool destroyOnReset;
        }
    }
}
