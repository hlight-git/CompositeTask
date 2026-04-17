using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    /// <summary>
    /// Base for task nodes with a typed config. TConfig holds serializable pure data;
    /// Unity object references belong on the concrete subclass as [SerializeField].
    /// </summary>
    public abstract class TaskNode<TConfig> : TaskNode, IConfigurable
        where TConfig : class, new()
    {
        [SerializeField] private TConfig _config = new();

        public TConfig Config
        {
            get => _config;
            set => _config = value;
        }

        protected abstract UniTask OnRunning(TConfig config, CancellationToken ct);

        protected sealed override UniTask OnRunning(CancellationToken ct) => OnRunning(_config, ct);

        void IConfigurable.ApplyConfig(object config)
        {
            if (config is TConfig typed)
                _config = typed;
            else
                Debug.LogWarning(
                    $"[TaskNode<{typeof(TConfig).Name}>] Config type mismatch: " +
                    $"expected {typeof(TConfig).Name}, got {config?.GetType().Name ?? "null"}", this);
        }
    }
}
