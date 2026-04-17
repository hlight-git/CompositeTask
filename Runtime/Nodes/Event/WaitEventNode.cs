using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hlight.Structures.CompositeTask.Runtime
{
    public enum EventWaitMode { Any, All }

    [DefineTaskNode("Wait Event", Description = "Waits for TaskEventSource(s) to fire. Ref: sources (TaskEventSource[]). Config: mode (EventWaitMode) — Any completes on first fire, All waits for every source.")]
    public class WaitEventNode : TaskNode<WaitEventNode.Settings>
    {
        [SerializeField] private TaskEventSource[] sources;

        private AutoResetUniTaskCompletionSource _utcs;
        private readonly HashSet<TaskEventSource> _firedSources = new();
        private readonly Dictionary<TaskEventSource, Action> _handlers = new();

        protected override UniTask OnRunning(Settings config, CancellationToken ct)
        {
            if (sources == null || sources.Length == 0) return UniTask.CompletedTask;

            _firedSources.Clear();
            _utcs = AutoResetUniTaskCompletionSource.Create();

            foreach (var source in sources)
            {
                var s = source;
                Action handler = () => OnSourceFired(s);
                _handlers[s] = handler;
                s.Fired += handler;
            }

            return _utcs.Task.AttachExternalCancellation(ct);
        }

        protected override void OnCompleted()
        {
            Unsubscribe();
            _utcs = null;
            base.OnCompleted();
        }

        protected override void OnReset()
        {
            Unsubscribe();
            _utcs?.TrySetCanceled();
            _utcs = null;
            _firedSources.Clear();
        }

        private void OnSourceFired(TaskEventSource source)
        {
            if (Config.mode == EventWaitMode.Any)
            {
                _utcs?.TrySetResult();
                _utcs = null;
                return;
            }

            _firedSources.Add(source);
            if (_firedSources.Count >= sources.Length)
            {
                _utcs?.TrySetResult();
                _utcs = null;
            }
        }

        private void Unsubscribe()
        {
            foreach (var kvp in _handlers)
                kvp.Key.Fired -= kvp.Value;
            _handlers.Clear();
        }

        [Serializable]
        public class Settings
        {
            public EventWaitMode mode = EventWaitMode.Any;
        }
    }
}
